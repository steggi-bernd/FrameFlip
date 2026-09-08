using System.Text.Json;
using FrameFlip.Bridge;

namespace FrameFlip.Remote;

/// <summary>
/// Verbindet den Renderfortschritt mit der Leitung zum Handy.
///
/// Sie behaelt die stabile Fassade fuer die App, waehrend die ausgehende
/// Status-/Telemetrie-Seite intern beim <see cref="RemoteStatusPublisher"/> liegt.
/// Befehle, Vorschauen und Dateiuebertragungen bleiben hier koordiniert.
/// </summary>
public sealed class RemoteLink : IAsyncDisposable
{
    private readonly RenderMonitor _monitor;
    private readonly RelayClient _client;
    private readonly RemoteStatusPublisher _status;
    private readonly object _gate = new();

    /// <summary>
    /// Ob das Handy jedem neuen Frame folgen will.
    ///
    /// Vorher fragte es von sich aus alle paar Sekunden nach - und lag damit
    /// zwangslaeufig daneben: Wer im falschen Moment fragt, bekommt das vorige Bild,
    /// und wer oft fragt, verbraucht Daten fuer Bilder, die es noch gar nicht gibt.
    /// Der Rechner weiss dagegen genau, wann eines fertig ist.
    /// </summary>
    private volatile bool _follow;

    /// <summary>Breite der Bilder, denen gefolgt wird. Klein - es ist eine Kachel, kein Vollbild.</summary>
    private int _followWidth = 480;

    private DateTime _lastFollow = DateTime.MinValue;

    /// <summary>Blaettern, Ansehen und Holen in der Bibliothek. Antwortet selbst.</summary>
    private readonly BrowseService _browse;

    /// <summary>Rendern auf Zuruf. Nur mit Erlaubnis, und nur mit freigegebenen Dateien.</summary>
    private readonly RenderService _render;

    /// <summary>Dateien entgegennehmen. Die einzige Stelle, an der etwas hereinkommt.</summary>
    private readonly UploadService _upload;

    public RemoteLink(PairingInvite invite, RenderMonitor monitor, Func<Diagnostics.LoadSnapshot?>? load = null,
                      Func<Configuration.AppSettings>? settings = null)
    {
        _monitor = monitor;
        _client = new RelayClient(invite);

        // Ohne Einstellungen bleibt die Bibliothek zu: Was hier nicht durchgereicht
        // wird, kann auch niemand freigeschaltet haben.
        var read = settings ?? (() => new Configuration.AppSettings());

        _browse = new BrowseService(read, payload => _client.Send(payload));

        void Answer(object payload) => _client.Send(Envelope.Json(JsonSerializer.Serialize(payload)));

        _render = new RenderService(read, Answer, new Rendering.RenderRunner(read, monitor.Feed));
        _upload = new UploadService(read, Answer);

        _monitor.FrameWritten += OnFrameWritten;
        _client.PayloadReceived += OnCommand;
        _status = new RemoteStatusPublisher(_monitor, payload => _client.Send(payload), load ?? (() => null));
    }

    public RelayState State => _client.State;

    public event Action<RelayState>? StateChanged
    {
        add => _client.StateChanged += value;
        remove => _client.StateChanged -= value;
    }

    public void Start() => _client.Start();

    /// <summary>
    /// Ein Befehl vom Handy.
    ///
    /// Bisher gibt es genau einen: die Vorschau anfordern. Sie wird ausdruecklich
    /// nur auf Anfrage geschickt und nicht bei jedem neuen Frame - ein Bild sind
    /// ein paar hundert Kilobyte, und bei sieben Sekunden je Frame waeren das
    /// zweistellige Megabyte in der Stunde, ungefragt, oft ueber Mobilfunk.
    ///
    /// Die Arbeit laeuft auf dem Threadpool: Ein grosses PNG zu dekodieren dauert
    /// Millisekunden bis Zehntelsekunden, und diese Kette haengt am Netzwerk-Thread
    /// der Verbindung. Ihn zu blockieren hiesse, waehrenddessen keine Metriken mehr
    /// zu senden.
    /// </summary>
    private void OnCommand(byte[] payload)
    {
        try
        {
            if (!Envelope.TryRead(payload, out PayloadKind kind, out byte[] body)) return;

            // Ein Stueck einer Datei, die hereinkommt. Es traegt keinen Befehl - die
            // Zuordnung steht in seiner Vorgangsnummer.
            if (kind == PayloadKind.Chunk)
            {
                if (Envelope.TryReadChunk(body, out int transfer, out int index, out bool last, out byte[] data))
                    _upload.OnChunk(transfer, index, last, data);

                return;
            }

            if (kind != PayloadKind.Json) return;

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("c", out JsonElement command)) return;

            string? name = command.GetString();

            if (name is null) return;

            // Alles rund um die Bibliothek beantwortet der Browser-Dienst selbst -
            // samt Ablehnung, falls sie gar nicht freigegeben ist.
            //
            // Auf dem Threadpool, und mit einer losgeloesten Kopie des Befehls: Das
            // Blaettern geht auf die Platte, und diese Kette haengt am Netzwerk-Thread
            // der Verbindung. Ohne Clone() zeigte die Kopie in ein JsonDocument, das
            // beim Verlassen dieser Methode schon weg ist.
            if (BrowseService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _browse.Handle(name, copy));
                return;
            }

            // Der Render geht denselben Weg: eigener Thread, losgeloeste Kopie. Das
            // Nachsehen in einer .blend-Datei startet Blender und dauert Sekunden.
            if (RenderService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _render.Handle(name, copy));
                return;
            }

            if (UploadService.Handles(name))
            {
                JsonElement copy = document.RootElement.Clone();

                Task.Run(() => _upload.Handle(name, copy));
                return;
            }

            // Dem Render folgen: Ab jetzt schickt der Rechner jedes fertige Bild von
            // selbst. Das ist der Unterschied zwischen "alle sechs Sekunden fragen"
            // und "da ist es".
            if (name == "follow")
            {
                _follow = !document.RootElement.TryGetProperty("on", out JsonElement on) || on.GetBoolean();

                if (document.RootElement.TryGetProperty("w", out JsonElement followWidth)
                    && followWidth.TryGetInt32(out int wanted))
                {
                    _followWidth = Math.Clamp(wanted, 240, 1920);
                }

                // Sofort eines schicken, damit nicht bis zum naechsten Frame ein
                // leeres Feld dasteht.
                if (_follow) Task.Run(() => SendPreview(_followWidth));

                return;
            }

            if (name != "preview") return;

            // Die gewuenschte Breite. Ohne Angabe die volle - so verhaelt sich eine
            // aeltere App wie bisher.
            int width = document.RootElement.TryGetProperty("w", out JsonElement w) && w.TryGetInt32(out int value)
                ? value
                : PreviewEncoder.Width;

            Task.Run(() => SendPreview(width));
        }
        catch (Exception)
        {
            // Was hereinkommt, ist zwar entschluesselt und damit echt - aber echt
            // heisst nicht wohlgeformt. Eine aeltere oder neuere App darf hier
            // nichts umwerfen.
        }
    }

    /// <summary>
    /// Die Vorschau beantworten - immer, auch wenn es keine gibt.
    ///
    /// Vorher wurde in diesem Fall einfach nichts geschickt, und in der App stand
    /// dauerhaft "Bild wird geholt". Eine Anfrage ohne Antwort ist die schlechteste
    /// Art zu scheitern: Der Fragende wartet, und niemand sagt ihm, worauf.
    ///
    /// Die haeufigsten Gruende sind harmlos und sollen genau so dastehen - ein
    /// Render, der gerade erst angelaufen ist, hat schlicht noch keinen Frame
    /// geschrieben.
    /// </summary>
    private void SendPreview(int width)
    {
        try
        {
            RenderJob? job = _monitor.Job;

            string? why = job is null
                ? "No render is running on the machine."
                : string.IsNullOrEmpty(job.LatestFrameFile)
                    ? "No frame written yet."
                    : null;

            if (why is null)
            {
                byte[]? jpeg = PreviewEncoder.Encode(job!.LatestFrameFile, width);

                if (jpeg is not null)
                {
                    _client.Send(Envelope.Preview(job.CurrentFrame, jpeg));
                    return;
                }

                why = "The image could not be read.";
            }

            _client.Send(Envelope.Json(
                $$"""{"t":"preview","ok":false,"why":{{JsonSerializer.Serialize(why)}}}"""));
        }
        catch (Exception)
        {
            // Siehe oben: Diese Kette haengt an der Vorschau und darf nichts werfen.
        }
    }

    /// <summary>
    /// Ein Frame ist auf der Platte - und das Handy will ihn sehen.
    ///
    /// Gedrosselt, weil ein schneller Render mehrere Bilder je Sekunde schreiben
    /// kann und jedes ein paar hundert Kilobyte kostet. Zwei Sekunden sind fuer das
    /// Auge fluessig genug und fuer ein Mobilnetz vertretbar.
    /// </summary>
    private void OnFrameWritten(string path)
    {
        if (!_follow) return;

        var now = DateTime.UtcNow;

        lock (_gate)
        {
            if (now - _lastFollow < TimeSpan.FromSeconds(2)) return;

            _lastFollow = now;
        }

        Task.Run(() => SendPreview(_followWidth));
    }

    /// <summary>
    /// Die Fassade behaelt die bisherige Serialisierungs-API, obwohl die
    /// ausgehende Verantwortung jetzt beim Status-Publisher liegt.
    /// </summary>
    public static string Describe(
        RenderJob? job,
        Diagnostics.LoadSnapshot? load = null,
        Diagnostics.GpuReading gpu = default)
        => RemoteStatusPublisher.Describe(job, load, gpu);

    public async ValueTask DisposeAsync()
    {
        _monitor.FrameWritten -= OnFrameWritten;
        _client.PayloadReceived -= OnCommand;
        await _status.DisposeAsync();
        _browse.Dispose();
        _upload.Dispose();
        await _client.DisposeAsync();
    }
}
