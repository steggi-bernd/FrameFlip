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

    public RemoteLink(PairingInvite invite, RenderMonitor monitor, Func<Diagnostics.LoadSnapshot?>? load = null)
    {
        _monitor = monitor;
        _load = load ?? (() => null);
        _client = new RelayClient(invite);

        _monitor.Changed += OnChanged;
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
            if (kind != PayloadKind.Json) return;

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("c", out JsonElement command)) return;
            if (command.GetString() != "preview") return;

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
                ? "Auf dem Rechner laeuft gerade kein Render."
                : string.IsNullOrEmpty(job.LatestFrameFile)
                    ? "Noch kein Frame geschrieben."
                    : null;

            if (why is null)
            {
                byte[]? jpeg = PreviewEncoder.Encode(job!.LatestFrameFile, width);

                if (jpeg is not null)
                {
                    _client.Send(Envelope.Preview(job.CurrentFrame, jpeg));
                    return;
                }

                why = "Das Bild liess sich nicht lesen.";
            }

            _client.Send(Envelope.Json(
                $$"""{"t":"preview","ok":false,"why":{{JsonSerializer.Serialize(why)}}}"""));
        }
        catch (Exception)
        {
            // Siehe oben: Diese Kette haengt an der Vorschau und darf nichts werfen.
        }
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
        _client.PayloadReceived -= OnCommand;
        await _ticker.DisposeAsync();
        _gpu.Dispose();
        await _client.DisposeAsync();
    }
}
