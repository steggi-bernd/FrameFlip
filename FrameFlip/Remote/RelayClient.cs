using System.IO;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
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
    private readonly Func<IRelaySocket> _socketFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _lifecycle = new();

    private Task? _loop;
    private Task? _dispose;
    private bool _disposing;
    private RelayState _state = RelayState.Off;

    public RelayClient(PairingInvite invite)
        : this(invite, static () => new ClientRelaySocket(), static (delay, token) => Task.Delay(delay, token))
    {
    }

    /// <summary>
    /// Interner Testeingang. Die Produktfassung verwendet ausschliesslich
    /// <see cref="ClientRelaySocket"/> und <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// </summary>
    internal RelayClient(
        PairingInvite invite,
        Func<IRelaySocket> socketFactory,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _invite = invite ?? throw new ArgumentNullException(nameof(invite));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));

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
        lock (_lifecycle)
        {
            // Der Ausgangskanal hat genau einen Leser. Ohne das Schloss konnten zwei
            // gleichzeitige Start-Aufrufe zwei Verbindungsloops und damit zwei Leser
            // erzeugen.
            if (_loop is not null || _disposing) return;

            _loop = Task.Run(() => RunAsync(_stopping.Token));
        }
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

        lock (_lifecycle)
        {
            if (_disposing) return;

            _outgoing.Writer.TryWrite(payload.ToArray());
        }
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
                await _delay(wait, token);
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

        using var socket = _socketFactory();
        await socket.ConnectAsync(new Uri(_invite.SocketUrl(RelayRole.Host)), token);

        SetState(RelayState.Waiting);

        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var sends = new SemaphoreSlim(1, 1);
        PeerSession? peer = null;

        try
        {
            await foreach (var (kind, data) in ReadAsync(socket, connection.Token))
            {
                if (kind == WebSocketMessageType.Text)
                {
                    string text = System.Text.Encoding.UTF8.GetString(data);

                    switch (RelayControl.Parse(text, out _))
                    {
                        case RelayMessage.Waiting:
                            await StopPeerSessionAsync(peer);
                            peer = null;
                            SetState(RelayState.Waiting);
                            break;

                        case RelayMessage.PeerUp:
                            // Beim Wiedersehen faengt alles von vorn an: neues Salz,
                            // neuer Schluessel, Zaehler bei null. Der alte Sender muss
                            // vollstaendig enden, bevor die neue Sitzung den einzigen
                            // Leser des Ausgangskanals bekommt.
                            await StopPeerSessionAsync(peer);

                            byte[] hello = SecureChannel.Hello(out byte[] ourSalt);
                            peer = new PeerSession(ourSalt);
                            SetState(RelayState.Waiting);
                            await SendFrameAsync(socket, sends, hello, WebSocketMessageType.Binary, connection.Token);
                            break;

                        case RelayMessage.PeerDown:
                            await StopPeerSessionAsync(peer);
                            peer = null;
                            SetState(RelayState.Waiting);
                            break;

                        case RelayMessage.Error:
                            return;
                    }

                    continue;
                }

                if (kind != WebSocketMessageType.Binary || peer is null) continue;

                if (peer.Channel is null)
                {
                    // Das erste Binaerpaket nach einem peer:true ist die Begruessung
                    // der Gegenseite. Alles andere an dieser Stelle ist Unsinn oder
                    // ein Fremder im Raum - beides wird verworfen.
                    if (!SecureChannel.TryReadHello(data, out byte[]? theirSalt)) continue;

                    peer.TheirSalt = theirSalt!;
                    peer.Channel = SecureChannel.Establish(_invite.Key, RelayRole.Host, peer.OurSalt, peer.TheirSalt);

                    byte[] proof = _invite.Key.Confirmation(RelayRole.Host, peer.OurSalt, peer.TheirSalt);
                    try
                    {
                        await SendFrameAsync(socket, sends, peer.Channel.Seal(proof), WebSocketMessageType.Binary, connection.Token);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(proof);
                    }

                    continue;
                }

                if (!peer.Confirmed)
                {
                    if (peer.TheirSalt is not null &&
                        peer.Channel.TryOpen(data, out byte[]? proof) &&
                        _invite.Key.IsConfirmation(proof!, RelayRole.Client, peer.OurSalt, peer.TheirSalt))
                    {
                        peer.Confirmed = true;
                        SetState(RelayState.Paired);
                        peer.Pump = PumpAsync(socket, sends, peer, connection);
                    }

                    continue;
                }

                if (peer.Channel.TryOpen(data, out byte[]? payload))
                {
                    try { PayloadReceived?.Invoke(payload!); }
                    catch (Exception) { /* ein Empfaenger darf die Leitung nicht reissen */ }
                }
            }

            Exception? sendFailure = peer is null ? null : Volatile.Read(ref peer.Failure);
            if (sendFailure is not null) ExceptionDispatchInfo.Capture(sendFailure).Throw();
        }
        finally
        {
            connection.Cancel();
            await StopPeerSessionAsync(peer);
        }
    }

    /// <summary>
    /// Schaufelt den Sendepuffer auf die Leitung, solange genau diese Sitzung steht.
    /// Ein Sendefehler beendet auch den Leser der Verbindung; nur so beginnt der
    /// aeussere Loop verlaesslich einen neuen Aufbau.
    /// </summary>
    private async Task PumpAsync(IRelaySocket socket, SemaphoreSlim sends, PeerSession peer, CancellationTokenSource connection)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(connection.Token, peer.Stopping.Token);
        CancellationToken token = linked.Token;
        SecureChannel channel = peer.Channel!;

        try
        {
            while (await _outgoing.Reader.WaitToReadAsync(token))
            {
                while (_outgoing.Reader.TryRead(out byte[]? payload))
                {
                    byte[] frame = channel.Seal(payload);
                    await SendFrameAsync(socket, sends, frame, WebSocketMessageType.Binary, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // peer:false, ein Verbindungsende oder Dispose: der Besitzer wartet in
            // StopPeerSessionAsync auf dieses Ende, bevor der Kanal entsorgt wird.
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref peer.Failure, ex, null);
            connection.Cancel();
        }
    }

    private static async Task SendFrameAsync(
        IRelaySocket socket,
        SemaphoreSlim sends,
        byte[] frame,
        WebSocketMessageType kind,
        CancellationToken token)
    {
        // ClientWebSocket erlaubt keine parallelen Sends. Begruessung, Nachweis und
        // Pump laufen deshalb durch dasselbe Tor.
        await sends.WaitAsync(token);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(frame), kind, endOfMessage: true, token);
        }
        finally
        {
            sends.Release();
        }
    }

    private static async Task StopPeerSessionAsync(PeerSession? peer)
    {
        if (peer is null) return;

        peer.Stopping.Cancel();

        try
        {
            await peer.Pump;
        }
        catch (Exception)
        {
            // Der Pump hat den eigentlichen Fehler bereits beim Verbindungsbesitzer
            // hinterlegt. Beim Aufraeumen darf er keine Ressource offen lassen.
        }
        finally
        {
            peer.Channel?.Dispose();
            CryptographicOperations.ZeroMemory(peer.OurSalt);

            if (peer.TheirSalt is not null) CryptographicOperations.ZeroMemory(peer.TheirSalt);

            peer.Stopping.Dispose();
        }
    }

    /// <summary>Ein zusammengehoeriger Besitzblock fuer eine einzelne Gegenstelle.</summary>
    private sealed class PeerSession
    {
        public PeerSession(byte[] ourSalt) => OurSalt = ourSalt;

        public CancellationTokenSource Stopping { get; } = new();
        public byte[] OurSalt { get; }
        public byte[]? TheirSalt { get; set; }
        public SecureChannel? Channel { get; set; }
        public bool Confirmed { get; set; }
        public Task Pump { get; set; } = Task.CompletedTask;
        public Exception? Failure;
    }

    /// <summary>
    /// Setzt die Bruchstuecke eines WebSocket-Frames zusammen.
    ///
    /// ReceiveAsync liefert keine Nachrichten, sondern Stuecke davon. Wer das
    /// uebersieht, bekommt bei kleinen Meldungen jahrelang recht und bei der ersten
    /// Vorschau ein halbes Bild.
    /// </summary>
    private static async IAsyncEnumerable<(WebSocketMessageType Kind, byte[] Data)> ReadAsync(
        IRelaySocket socket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        byte[] chunk = new byte[ReceiveChunk];
        using var assembled = new MemoryStream();

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;

            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), token);
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

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (_dispose is null)
            {
                _disposing = true;
                _outgoing.Writer.TryComplete();
                _stopping.Cancel();
                _dispose = DisposeCoreAsync(_loop);
            }

            return new ValueTask(_dispose);
        }
    }

    private async Task DisposeCoreAsync(Task? loop)
    {
        if (loop is not null)
        {
            try { await loop; }
            catch (Exception) { /* beim Beenden interessiert kein Fehler mehr */ }
        }

        _stopping.Dispose();
    }
}
