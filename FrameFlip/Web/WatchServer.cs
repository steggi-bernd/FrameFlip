using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;
using FrameFlip.Remote;

namespace FrameFlip.Web;

/// <summary>
/// Eine Seite zum Zusehen - mehr nicht.
///
/// WAS SIE KANN: den Fortschritt eines laufenden Renders zeigen und den zuletzt
/// geschriebenen Frame. Das ist der ganze Umfang, und er ist mit Absicht so klein.
///
/// WAS SIE NICHT KANN, und zwar grundsaetzlich nicht:
///
///   - Nichts entgegennehmen. Es gibt keinen POST, kein PUT, kein Formular. Alles
///     ausser GET wird abgewiesen, bevor auch nur der Pfad angesehen wird.
///   - Keinen Pfad vom Aufrufer annehmen. Der Frame wird nicht ueber einen
///     Dateinamen angefordert - der Server entscheidet selbst, welches Bild das
///     aktuelle ist. Damit gibt es keine Stelle, an der sich ein "../.." hineinlegen
///     liesse, denn es gibt gar keine Stelle fuer einen Dateinamen.
///   - Nichts anstossen. Kein Render, kein Oeffnen, kein Loeschen. Die Seite liest
///     nur, was das Programm ohnehin gerade weiss.
///
/// WER HINEINDARF. In der Adresse steht ein Zeichen, das beim Einschalten aus dem
/// Zufallsgenerator des Betriebssystems entsteht. Ohne genau dieses Zeichen antwortet
/// der Server mit 404 - nicht mit 401: Ein "falsches Kennwort" waere die Auskunft,
/// dass es hier ueberhaupt etwas gibt.
///
/// WOHIN SIE HOERT. Auf das eigene Netz, nicht auf das Internet. Wer von unterwegs
/// zusehen will, nimmt die App ueber den Relay - dafuer gibt es sie.
///
/// Ein eigener kleiner HTTP-Server statt HttpListener: Letzterer verlangt unter
/// Windows fuer jede Adresse ausser localhost eine Registrierung mit
/// Administratorrechten. Das waere ein hoher Preis fuer eine Seite, die drei Dinge
/// ausliefert.
/// </summary>
public sealed class WatchServer : IDisposable
{
    /// <summary>So lange darf ein Aufrufer fuer seine Anfrage brauchen.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Mehr als das ist keine Anfrage mehr, sondern ein Versuch.</summary>
    private const int MaxRequestBytes = 8 * 1024;

    /// <summary>Gleichzeitige Verbindungen. Ein Handy braucht zwei, ein Angreifer viele.</summary>
    private const int MaxConnections = 16;

    private readonly Func<RenderJob?> _job;
    private readonly Func<LoadSnapshot?> _load;
    private readonly Func<string?> _newestFrame;

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);

    private TcpListener? _listener;

    public WatchServer(Func<RenderJob?> job, Func<LoadSnapshot?> load, Func<string?> newestFrame)
    {
        _job = job;
        _load = load;
        _newestFrame = newestFrame;
    }

    /// <summary>Der Port, auf dem wirklich gehorcht wird. 0 heisst: laeuft nicht.</summary>
    public int Port { get; private set; }

    /// <summary>Das Zeichen in der Adresse. Leer heisst: laeuft nicht.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>Die Adresse, die man aufs Handy tippt - oder null, wenn nichts laeuft.</summary>
    public string? Address => Port > 0 && Token.Length > 0 && LocalAddress() is { } ip
        ? $"http://{ip}:{Port}/{Token}/"
        : null;

    /// <summary>
    /// Anfangen zu horchen. Gibt zurueck, ob es geklappt hat.
    ///
    /// Ein neues Zeichen bei jedem Start: Wer die alte Adresse noch offen hat, sieht
    /// ab dann nichts mehr. Das ist gewollt - eine Adresse, die ueber Wochen gilt,
    /// ist keine Adresse mehr, sondern eine Tuer.
    /// </summary>
    public bool Start(int port)
    {
        if (_listener is not null) return true;

        Token = NewToken();

        try
        {
            _listener = new TcpListener(IPAddress.Any, Math.Clamp(port, 1024, 65535));
            _listener.Start();

            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
        catch (SocketException)
        {
            // Port belegt oder von einer Regel verboten. Dann gibt es die Seite eben
            // nicht; alles andere an FrameFlip laeuft davon unberuehrt weiter.
            Stop();
            return false;
        }

        _ = Task.Run(AcceptLoopAsync);

        return true;
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch (Exception) { }

        _listener = null;
        Port = 0;
        Token = string.Empty;
    }

    public void Dispose()
    {
        _stop.Cancel();
        Stop();
        _stop.Dispose();
        _slots.Dispose();
    }

    // ================================================================ Annehmen

    private async Task AcceptLoopAsync()
    {
        var listener = _listener;

        while (listener is not null && !_stop.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                // Beendet, abgebrochen, Netz weg - in allen Faellen ist hier Schluss.
                return;
            }

            // Ueber der Grenze wird nicht gewartet, sondern aufgelegt. Warten hiesse,
            // dass ein einzelner Aufrufer die Seite fuer alle anderen zumachen kann.
            if (!_slots.Wait(0))
            {
                try { client.Dispose(); } catch (Exception) { }
                continue;
            }

            _ = Task.Run(async () =>
            {
                try { await ServeAsync(client); }
                catch (Exception) { }
                finally
                {
                    try { client.Dispose(); } catch (Exception) { }
                    _slots.Release();
                }
            });
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        client.ReceiveTimeout = (int)ReadTimeout.TotalMilliseconds;
        client.SendTimeout = (int)ReadTimeout.TotalMilliseconds;

        using var stream = client.GetStream();

        string? head = await ReadHeadAsync(stream);
        if (head is null) return;

        // Alles ausser GET faellt hier, bevor irgendetwas ausgewertet wird. Die Seite
        // nimmt nichts entgegen, also braucht sie auch keinen anderen Weg zu kennen.
        if (!head.StartsWith("GET ", StringComparison.Ordinal))
        {
            await WriteAsync(stream, 405, "text/plain; charset=utf-8",
                             Encoding.UTF8.GetBytes("Nur GET.\n"));
            return;
        }

        int end = head.IndexOf(' ', 4);
        string target = end > 4 ? head[4..end] : "/";

        // Fragezeichen ab: Es gibt keinen Parameter, den der Aufrufer setzen koennte.
        int query = target.IndexOf('?');
        if (query >= 0) target = target[..query];

        await RouteAsync(stream, target);
    }

    /// <summary>
    /// Die Anfragezeile samt Kopfzeilen lesen - bis zur Leerzeile, hoechstens bis zur
    /// Grenze. Der Rumpf wird nicht gelesen; bei GET gibt es keinen.
    /// </summary>
    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        var text = new StringBuilder();

        while (text.Length < MaxRequestBytes)
        {
            int read;

            try { read = await stream.ReadAsync(buffer); }
            catch (Exception) { return null; }

            if (read <= 0) return null;

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));

            int line = text.ToString().IndexOf('\n');
            if (line >= 0) return text.ToString(0, line).TrimEnd('\r');
        }

        return null;
    }

    // ================================================================ Wegweiser

    private async Task RouteAsync(NetworkStream stream, string target)
    {
        var parts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Ohne das richtige Zeichen gibt es hier nichts - und zwar so, wie es fuer
        // jede andere Adresse auch nichts gibt.
        if (parts.Length == 0 || !TokenMatches(parts[0]))
        {
            await NotFoundAsync(stream);
            return;
        }

        string route = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;

        switch (route)
        {
            case "":
                await WriteAsync(stream, 200, "text/html; charset=utf-8",
                                 Encoding.UTF8.GetBytes(WatchPage.Html));
                return;

            case "state":
                await WriteAsync(stream, 200, "application/json; charset=utf-8",
                                 Encoding.UTF8.GetBytes(WatchState.Describe(_job(), _load(), _newestFrame())));
                return;

            case "frame":
                await SendFrameAsync(stream);
                return;

            default:
                await NotFoundAsync(stream);
                return;
        }
    }

    private async Task SendFrameAsync(NetworkStream stream)
    {
        // Der Aufrufer sagt nicht, welches Bild er will - er bekommt das aktuelle.
        // Genau deshalb gibt es hier keinen Pfad zu pruefen.
        byte[]? jpeg = null;

        try { jpeg = PreviewEncoder.Encode(_newestFrame(), 1280); }
        catch (Exception) { }

        if (jpeg is null)
        {
            await WriteAsync(stream, 204, "text/plain", Array.Empty<byte>());
            return;
        }

        await WriteAsync(stream, 200, "image/jpeg", jpeg);
    }

    private Task NotFoundAsync(NetworkStream stream)
        => WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Nichts hier.\n"));

    // ================================================================ Kleinteile

    /// <summary>
    /// Vergleich ohne Zeitverrat.
    ///
    /// Ein gewoehnlicher Zeichenvergleich bricht beim ersten Unterschied ab, und aus
    /// der Dauer laesst sich Zeichen fuer Zeichen erraten, wie das richtige lautet.
    /// Im eigenen Netz ist das kaum auszunutzen - aber es kostet nichts, es richtig
    /// zu machen.
    /// </summary>
    private bool TokenMatches(string candidate)
    {
        if (Token.Length == 0 || candidate.Length != Token.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(Token));
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);

        return Base64Url.Encode(bytes);
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string type, byte[] body)
    {
        string reason = status switch
        {
            200 => "OK",
            204 => "No Content",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "OK",
        };

        var head = new StringBuilder();

        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        head.Append("Content-Type: ").Append(type).Append("\r\n");
        head.Append("Content-Length: ").Append(body.Length).Append("\r\n");

        // Nichts aufheben: Der Frame aendert sich, und eine zwischengespeicherte
        // Fassung waere schlimmer als keine.
        head.Append("Cache-Control: no-store\r\n");
        head.Append("X-Content-Type-Options: nosniff\r\n");
        head.Append("Referrer-Policy: no-referrer\r\n");

        // Die Seite laedt nichts nach. Was sie braucht, steht in ihr.
        head.Append("Content-Security-Policy: default-src 'none'; img-src 'self' data:; ")
            .Append("style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'\r\n");

        head.Append("Connection: close\r\n\r\n");

        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));

            if (body.Length > 0) await stream.WriteAsync(body);

            await stream.FlushAsync();
        }
        catch (Exception)
        {
            // Aufgelegt, waehrend geschrieben wurde. Kommt vor.
        }
    }

    /// <summary>
    /// Die Adresse, unter der dieser Rechner im eigenen Netz zu finden ist.
    ///
    /// Genommen wird die Schnittstelle, die laeuft und ein Gateway hat - also die,
    /// ueber die der Rechner wirklich im Netz haengt. Ohne diese Pruefung faellt die
    /// Wahl gern auf eine Adresse von VirtualBox oder WSL, und die hilft dem Handy
    /// nicht weiter.
    /// </summary>
    public static string? LocalAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = nic.GetIPProperties();

                if (props.GatewayAddresses.Count == 0) continue;

                foreach (var address in props.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(address.Address)) continue;

                    return address.Address.ToString();
                }
            }
        }
        catch (Exception)
        {
            // Ohne Netz keine Adresse. Die Seite gibt es dann eben nur ueber localhost.
        }

        return null;
    }
}
