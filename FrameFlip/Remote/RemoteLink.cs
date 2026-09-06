using System.Text.Json;
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
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly RenderMonitor _monitor;
    private readonly RelayClient _client;
    private readonly object _gate = new();

    /// <summary>Liefert CPU, RAM und GPU-Last. Null, wenn die Lasterkennung aus ist.</summary>
    private readonly Func<Diagnostics.LoadSnapshot?> _load;

    /// <summary>VRAM und Temperatur. Still, wenn nvidia-smi nicht erreichbar ist.</summary>
    private readonly Diagnostics.NvidiaProbe _gpu = new();

    private DateTime _lastSent = DateTime.MinValue;
    private string? _lastShape;

    public RemoteLink(PairingInvite invite, RenderMonitor monitor, Func<Diagnostics.LoadSnapshot?>? load = null)
    {
        _monitor = monitor;
        _load = load ?? (() => null);
        _client = new RelayClient(invite);

        _monitor.Changed += OnChanged;
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

                if (!changed && DateTime.UtcNow - _lastSent < Interval) return;

                _lastShape = shape;
                _lastSent = DateTime.UtcNow;
            }

            _client.Send(System.Text.Encoding.UTF8.GetBytes(json));
        }
        catch (Exception)
        {
            // Diese Kette haengt an der Vorschau. Sie darf unter keinen Umstaenden
            // etwas nach oben werfen.
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
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.Changed -= OnChanged;
        _gpu.Dispose();
        await _client.DisposeAsync();
    }
}
