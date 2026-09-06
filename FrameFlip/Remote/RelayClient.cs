using System.IO;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace FrameFlip.Remote;

/// <summary>Woran die Oberflaeche ablesen kann, wie es um die Fernsteuerung steht.</summary>
public enum RelayState
{
    /// <summary>Aus - nicht eingeschaltet oder keine Kopplung.</summary>
    Off,

    /// <summary>Verbindungsversuch laeuft, oder es wird auf den naechsten gewartet.</summary>
    Connecting,

    /// <summary>Im Raum, aber allein. Der Normalfall, solange das Handy in der Tasche liegt.</summary>
    Waiting,

    /// <summary>Handy ist da, Kanal steht.</summary>
    Paired
}

/// <summary>
/// Haelt die Verbindung zum Relay und verschluesselt, was hindurchgeht.
///
/// Der ganze Aufbau folgt einer Regel: <b>Das darf die Vorschau nie stoeren.</b>
/// FrameFlip laeuft waehrend eines Renders, und ein Netzwerkfehler ist dort ein
/// Nichts-Ereignis - keine Ausnahme nach aussen, keine Wartezeit im Aufrufer, kein
/// blockierter Sendeaufruf. <see cref="Send"/> legt in einen Puffer fester Groesse
/// und kehrt sofort zurueck; ist der voll, faellt die aelteste Nachricht heraus.
/// Renderfortschritt von vor zwanzig Sekunden interessiert niemanden mehr.
///
/// Getrennt wird nicht als Fehler behandelt, sondern als Zustand: Es wird mit
/// wachsendem Abstand neu versucht, bis jemand aufhoert. Ein Router startet neu,
/// ein WLAN wechselt, ein Laptop klappt zu - das ist der Alltag und kein Anlass,
/// die Fernsteuerung endgueltig aufzugeben.
/// </summary>
public sealed class RelayClient : IAsyncDisposable
{
    /// <summary>Muss zu RELAY_MAX_MESSAGE passen; darueber trennt der Relay.</summary>
    private const int MaxMessage = 1024 * 1024;

    private const int ReceiveChunk = 16 * 1024;

    /// <summary>
    /// Wieviele Nachrichten fuer eine langsame Leitung zurueckgehalten werden.
    ///
    /// Bei etwa einer Meldung je Sekunde sind das gut anderthalb Minuten. Mehr
    /// aufzuheben hiesse, veraltete Zahlen auszuliefern, sobald es weitergeht.
    /// </summary>
    private const int SendQueue = 64;

    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(2);

    private readonly PairingInvite _invite;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<byte[]> _outgoing;

    private Task? _loop;
    private RelayState _state = RelayState.Off;

    public RelayClient(PairingInvite invite)
    {
        _invite = invite;

        // DropOldest statt Warten: Ein voller Puffer darf den Aufrufer nicht anhalten.
        _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(SendQueue)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    /// <summary>Zustandswechsel. Kommt vom Netzwerk-Thread - der Empfaenger muss selbst zurueck in seinen.</summary>
    public event Action<RelayState>? StateChanged;

    /// <summary>Eine entschluesselte Nachricht vom Handy.</summary>
    public event Action<byte[]>? PayloadReceived;

    public RelayState State => _state;

    public void Start()
    {
        if (_loop is not null) return;

        _loop = Task.Run(() => RunAsync(_stopping.Token));
    }

    /// <summary>
    /// Legt eine Nachricht zum Versand. Kehrt immer sofort zurueck.
    ///
    /// Ob gerade eine Verbindung steht, spielt hier keine Rolle: Ohne Gegenseite
    /// laeuft die Nachricht in den Puffer und faellt spaeter hinten heraus. Der
    /// Aufrufer soll den Zustand der Leitung nicht kennen muessen.
    /// </summary>
    public void Send(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0 || payload.Length > MaxMessage - SecureChannel.Overhead) return;
        if (_stopping.IsCancellationRequested) return;

        _outgoing.Writer.TryWrite(payload.ToArray());
    }

    private async Task RunAsync(CancellationToken token)
    {
        TimeSpan wait = FirstRetry;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await OneConnectionAsync(token);

                // Beim Beenden endet die Leseschleife ohne Ausnahme - der Abbruch
                // wird dort verschluckt. Ohne diese Zeile meldete der Client auf dem
                // Weg nach draussen noch einmal "Connecting".
                if (token.IsCancellationRequested) break;

                // Eine Verbindung, die getragen hat, setzt den Abstand zurueck. Sonst
                // wartete man nach Stunden Betrieb minutenlang auf den Neuaufbau.
                wait = FirstRetry;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Jeder Fehler endet hier: Namensaufloesung, Zertifikat, Zeitablauf,
                // ein Relay, der neu startet. Keiner davon darf nach aussen dringen.
            }

            SetState(RelayState.Connecting);

            try
            {
                await Task.Delay(wait, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            wait = wait < MaxRetry ? wait + wait : MaxRetry;
        }

        SetState(RelayState.Off);
    }

    private async Task OneConnectionAsync(CancellationToken token)
    {
        SetState(RelayState.Connecting);

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await socket.ConnectAsync(new Uri(_invite.SocketUrl(RelayRole.Host)), token);

        SetState(RelayState.Waiting);

        // Der Handschlag steht offen im Raum; das Salz ist kein Geheimnis. Erst wenn
        // beide da sind, entsteht daraus ein Schluessel.
        byte[] hello = SecureChannel.Hello(out byte[] ourSalt);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            SecureChannel? channel = null;
            var pump = Task.CompletedTask;

            await foreach (var (kind, data) in ReadAsync(socket, linked.Token))
            {
                if (kind == WebSocketMessageType.Text)
                {
                    string text = System.Text.Encoding.UTF8.GetString(data);

                    switch (RelayControl.Parse(text, out _))
                    {
                        case RelayMessage.Waiting:
                            SetState(RelayState.Waiting);
                            break;

                        case RelayMessage.PeerUp:
                            // Beim Wiedersehen faengt alles von vorn an: neues Salz,
                            // neuer Schluessel, Zaehler bei null. Ein alter Kanal waere
                            // gegen ein Handy gerichtet, das nicht mehr dran ist.
                            channel?.Dispose();
                            channel = null;

                            await SendFrameAsync(socket, hello, linked.Token);
                            break;

                        case RelayMessage.PeerDown:
                            channel?.Dispose();
                            channel = null;
                            SetState(RelayState.Waiting);
                            break;

                        case RelayMessage.Error:
                            return;
                    }

                    continue;
                }

                if (channel is null)
                {
                    // Das erste Binaerpaket nach einem peer:true ist die Begruessung
                    // der Gegenseite. Alles andere an dieser Stelle ist Unsinn oder
                    // ein Fremder im Raum - beides wird verworfen.
                    if (!SecureChannel.TryReadHello(data, out byte[]? theirSalt)) continue;

                    channel = SecureChannel.Establish(_invite.Key, RelayRole.Host, ourSalt, theirSalt!);

                    SetState(RelayState.Paired);

                    pump = PumpAsync(socket, channel, linked.Token);
                    continue;
                }

                if (channel.TryOpen(data, out byte[]? payload))
                {
                    try { PayloadReceived?.Invoke(payload!); }
                    catch (Exception) { /* ein Empfaenger darf die Leitung nicht reissen */ }
                }
            }

            await linked.CancelAsync();
            await pump;

            channel?.Dispose();
        }
        finally
        {
            await linked.CancelAsync();
        }
    }

    /// <summary>Schaufelt den Sendepuffer auf die Leitung, solange der Kanal steht.</summary>
    private async Task PumpAsync(ClientWebSocket socket, SecureChannel channel, CancellationToken token)
    {
        try
        {
            while (await _outgoing.Reader.WaitToReadAsync(token))
            {
                while (_outgoing.Reader.TryRead(out byte[]? payload))
                    await SendFrameAsync(socket, channel.Seal(payload), token);
            }
        }
        catch (Exception)
        {
            // Bricht die Leitung, endet auch der Leser - der Aufbau faengt von vorn an.
        }
    }

    private static Task SendFrameAsync(ClientWebSocket socket, byte[] frame, CancellationToken token)
        => socket.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, token);

    /// <summary>
    /// Setzt die Bruchstuecke eines WebSocket-Frames zusammen.
    ///
    /// ReceiveAsync liefert keine Nachrichten, sondern Stuecke davon. Wer das
    /// uebersieht, bekommt bei kleinen Meldungen jahrelang recht und bei der ersten
    /// Vorschau ein halbes Bild.
    /// </summary>
    private static async IAsyncEnumerable<(WebSocketMessageType Kind, byte[] Data)> ReadAsync(
        ClientWebSocket socket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        byte[] chunk = new byte[ReceiveChunk];
        var assembled = new MemoryStream();

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;

            try
            {
                result = await socket.ReceiveAsync(chunk, token);
            }
            catch (Exception)
            {
                yield break;
            }

            if (result.MessageType == WebSocketMessageType.Close) yield break;

            assembled.Write(chunk, 0, result.Count);

            if (assembled.Length > MaxMessage) yield break;
            if (!result.EndOfMessage) continue;

            byte[] data = assembled.ToArray();
            assembled.SetLength(0);

            yield return (result.MessageType, data);
        }
    }

    private void SetState(RelayState next)
    {
        if (_state == next) return;

        _state = next;

        try { StateChanged?.Invoke(next); }
        catch (Exception) { /* wie oben: der Empfaenger darf nichts umwerfen */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        if (_loop is not null)
        {
            try { await _loop; }
            catch (Exception) { /* beim Beenden interessiert kein Fehler mehr */ }
        }

        _stopping.Dispose();
    }
}
