using System.Text.Json;
using System.Threading;
using FrameFlip.Bridge;

namespace FrameFlip.Remote;

/// <summary>
/// Verbindet den Renderfortschritt mit der Leitung zum Handy.
///
/// Zwischen <see cref="RenderMonitor"/> und <see cref="RelayClient"/> fehlt genau
/// zweierlei: eine Form, in der sich der Zustand uebertragen laesst, und ein Takt,
/// der den Kanal nicht flutet. Beides steht hier.
///
/// Der Takt ist nicht Sparsamkeit: Blender meldet den Statustext mehrmals je
/// Sekunde, und jede Meldung waere ein verschluesseltes Paket ueber ein Mobilnetz.
/// Eine Sekunde ist feiner, als ein Mensch auf ein Handy schaut. Zustandswechsel -
/// Frame fertig, Render zu Ende - gehen sofort durch; auf die wartet jemand.
/// </summary>
public sealed class RemoteLink : IAsyncDisposable
{
    /// <summary>Takt waehrend eines Renders.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Takt im Leerlauf.
    ///
    /// Auch ohne Render soll etwas ankommen - gerade dann ist die Frage, ob der
    /// Rechner ueberhaupt noch wach ist und was die Karte macht. Aber nicht im
    /// Sekundentakt: Eine Leerlaufmeldung sind rund 120 Bytes, im Sekundentakt
    /// waeren das ueber ein Mobilnetz gut 400 KB je Stunde fuer die Nachricht
    /// "hier passiert nichts".
    /// </summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    private readonly RenderMonitor _monitor;
    private readonly RelayClient _client;
    private readonly object _gate = new();

    /// <summary>Liefert CPU, RAM und GPU-Last. Null, wenn die Lasterkennung aus ist.</summary>
    private readonly Func<Diagnostics.LoadSnapshot?> _load;

    /// <summary>VRAM und Temperatur. Still, wenn nvidia-smi nicht erreichbar ist.</summary>
    private readonly Diagnostics.NvidiaProbe _gpu = new();

    private readonly Timer _ticker;

    private DateTime _lastSent = DateTime.MinValue;
    private string? _lastShape;

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
        _load = load ?? (() => null);
        _client = new RelayClient(invite);

        // Ohne Einstellungen bleibt die Bibliothek zu: Was hier nicht durchgereicht
        // wird, kann auch niemand freigeschaltet haben.
        var read = settings ?? (() => new Configuration.AppSettings());

        _browse = new BrowseService(read, payload => _client.Send(payload));

        void Answer(object payload) => _client.Send(Envelope.Json(JsonSerializer.Serialize(payload)));

        _render = new RenderService(read, Answer, new Rendering.RenderRunner(read, monitor.Feed));
        _upload = new UploadService(read, Answer);

        _monitor.Changed += OnChanged;
        _monitor.FrameWritten += OnFrameWritten;
        _client.PayloadReceived += OnCommand;

        // Der eigene Takt ist nicht Beiwerk, sondern die Grundlage: Das Ereignis der
        // Bruecke feuert nur, wenn Blender etwas meldet. Ohne laufenden Render - und
        // ohne installiertes Addon - kommt es nie, und dann ginge ueberhaupt nichts
        // ans Handy. Der Bildschirm dort blieb leer, ohne dass jemand sagen koennte
        // warum.
        _ticker = new Timer(_ => OnChanged(), null, Interval, Interval);
    }

    public RelayState State => _client.State;

    public event Action<RelayState>? StateChanged
    {
        add => _client.StateChanged += value;
        remove => _client.StateChanged -= value;
    }

    public void Start() => _client.Start();

    private void OnChanged()
    {
        try
        {
            string json = Describe(_monitor.Job, _load(), _gpu.Read());

            lock (_gate)
            {
                // Ein Zustandswechsel wartet nicht auf den Takt. Verglichen wird nur
                // der grobe Umriss, nicht der ganze Text - sonst waere jede neue
                // Restzeit ein "Wechsel" und der Takt wirkungslos.
                string shape = Shape(_monitor.Job);
                bool changed = shape != _lastShape;

                // Im Leerlauf seltener - siehe IdleInterval.
                var wait = _monitor.Job?.IsRunning == true ? Interval : IdleInterval;

                if (!changed && DateTime.UtcNow - _lastSent < wait) return;

                _lastShape = shape;
                _lastSent = DateTime.UtcNow;
            }

            _client.Send(Envelope.Json(json));
        }
        catch (Exception)
        {
            // Diese Kette haengt an der Vorschau. Sie darf unter keinen Umstaenden
            // etwas nach oben werfen.
        }
    }

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

    /// <summary>Woran ein echter Wechsel erkannt wird - nicht am Zahlenrauschen.</summary>
    private static string Shape(RenderJob? job)
        => job is null ? "-" : $"{job.Id}/{job.State}/{job.CurrentFrame}/{job.FramesWritten}";

    /// <summary>
    /// Der Zustand als JSON, so wie die App ihn liest.
    ///
    /// Kurze Namen, weil jedes Byte durch ein Mobilnetz geht und die Nachricht
    /// jede Sekunde faellt. Was fehlt, fehlt - die App muss ohnehin damit umgehen,
    /// dass Blender nicht jede Zahl liefert.
    /// </summary>
    public static string Describe(
        RenderJob? job,
        Diagnostics.LoadSnapshot? load = null,
        Diagnostics.GpuReading gpu = default)
    {
        var buffer = new System.IO.MemoryStream(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (job is null)
            {
                writer.WriteString("t", "idle");
                WriteMachine(writer, load, gpu);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString("t", "job");
                writer.WriteString("state", job.State.ToString().ToLowerInvariant());

                // Ob Fortschrittsbalken und Framezaehler ueberhaupt etwas aussagen.
                writer.WriteBoolean("anim", job.IsAnimation);

                // Blender ist mitten im Render verschwunden. Das ist etwas anderes
                // als ein Render, der von sich aus gescheitert ist, und die App
                // soll es anders benennen duerfen.
                if (job.Vanished) writer.WriteBoolean("gone", true);
                writer.WriteString("scene", job.Scene);
                writer.WriteString("engine", job.Engine);
                writer.WriteString("file", System.IO.Path.GetFileName(job.BlendFile));

                writer.WriteNumber("frame", job.CurrentFrame);
                writer.WriteNumber("first", job.FirstFrame);
                writer.WriteNumber("last", job.LastFrame);
                writer.WriteNumber("written", job.FramesWritten);
                writer.WriteNumber("progress", Math.Round(job.Progress, 4));
                writer.WriteNumber("elapsed", Math.Round(job.Elapsed.TotalSeconds, 1));

                if (job.Remaining is TimeSpan left)
                    writer.WriteNumber("remaining", Math.Round(left.TotalSeconds, 1));

                if (job.SecondsPerFrame is double spf)
                    writer.WriteNumber("spf", Math.Round(spf, 2));

                RenderStats stats = job.Stats;

                if (stats.Sample is int sample) writer.WriteNumber("sample", sample);
                if (stats.SampleTotal is int total) writer.WriteNumber("samples", total);
                if (stats.MemoryMb is long memory) writer.WriteNumber("memMb", memory);
                if (stats.Activity is { Length: > 0 } activity) writer.WriteString("activity", activity);

                WriteMachine(writer, load, gpu);
                writer.WriteEndObject();
            }
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Der Zustand der Maschine - auch im Leerlauf.
    ///
    /// Gerade wenn kein Render laeuft ist es die Frage, ob der Rechner ueberhaupt
    /// noch wach ist. Ein Bildschirm, der dann gar nichts zeigt, beantwortet sie
    /// nicht.
    ///
    /// Jeder Wert einzeln optional: GPU-Last kommt aus einem Zaehler, den es nur
    /// unter Windows 10 aufwaerts gibt, VRAM und Temperatur nur von NVIDIA. Was
    /// fehlt, wird weggelassen und nicht als Null geschickt - die App zeigt dafuer
    /// einen Gedankenstrich, und der sagt die Wahrheit.
    /// </summary>
    private static void WriteMachine(Utf8JsonWriter writer, Diagnostics.LoadSnapshot? load, Diagnostics.GpuReading gpu)
    {
        if (load is not null)
        {
            writer.WriteNumber("cpu", Math.Round(load.CpuPercent, 1));

            if (load.TotalMb > 0)
            {
                writer.WriteNumber("ramUsedMb", Math.Max(0, load.TotalMb - load.AvailableMb));
                writer.WriteNumber("ramTotalMb", load.TotalMb);
            }

            if (load.GpuPercent is double percent) writer.WriteNumber("gpu", Math.Round(percent, 1));
        }

        // Der Zaehler oben ist herstellerunabhaengig und deshalb die bessere Quelle
        // fuer die Auslastung; nvidia-smi ergaenzt nur, was er nicht kennt.
        if (load?.GpuPercent is null && gpu.UtilizationPercent is int utilization)
            writer.WriteNumber("gpu", utilization);

        if (gpu.MemoryUsedMb is long used) writer.WriteNumber("vramUsedMb", used);
        if (gpu.MemoryTotalMb is long total) writer.WriteNumber("vramTotalMb", total);
        if (gpu.TemperatureCelsius is int temperature) writer.WriteNumber("gpuTemp", temperature);
        if (gpu.Name is { Length: > 0 } card) writer.WriteString("gpuName", card);
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.Changed -= OnChanged;
        _monitor.FrameWritten -= OnFrameWritten;
        _client.PayloadReceived -= OnCommand;
        _browse.Dispose();
        _upload.Dispose();
        await _ticker.DisposeAsync();
        _gpu.Dispose();
        await _client.DisposeAsync();
    }
}
