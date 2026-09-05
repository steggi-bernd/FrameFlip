namespace FrameFlip.Bridge;

public enum JobState { Idle, Preparing, Rendering, Finished, Cancelled, Failed }

/// <summary>
/// Der Zustand eines Renders, aus den gemeldeten Ereignissen aufgebaut.
///
/// Grundsatz: Die verlaesslichen Zahlen kommen aus den EREIGNISSEN, nicht aus dem
/// Statustext. Wie viele Frames fertig sind, weiss diese Klasse, weil sie mitzaehlt,
/// welche geschrieben wurden - nicht weil Blender es irgendwo hineingeschrieben hat.
/// Der Statustext liefert nur, was sonst gar nicht zu bekommen waere: den
/// Sample-Fortschritt innerhalb des laufenden Frames.
///
/// Ebenso die Restzeit: Cycles' "Remaining" gilt fuer den AKTUELLEN Frame. Fuer den
/// ganzen Auftrag ist die gemessene Dauer der bisherigen Frames die bessere Grundlage,
/// und sie ist auch dann da, wenn der Statustext nichts hergibt.
/// </summary>
public sealed class RenderJob
{
    /// <summary>Wie viele Frame-Dauern in den Mittelwert eingehen.</summary>
    private const int TimingWindow = 12;

    private readonly Queue<double> _frameSeconds = new();

    /// <summary>
    /// Dauer JEDES Frames, in Reihenfolge. Anders als das gleitende Fenster oben
    /// wird hier nichts vergessen - daraus entsteht der Tempoverlauf, an dem man
    /// sieht, welche Stellen der Animation teuer waren.
    /// </summary>
    private readonly List<double> _allFrameSeconds = new();

    private long _frameStartedTicks;

    public string Id { get; init; } = string.Empty;
    public string BlendFile { get; init; } = string.Empty;
    public string Scene { get; init; } = string.Empty;
    public string Engine { get; init; } = string.Empty;

    public int FirstFrame { get; init; }
    public int LastFrame { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Ausgabeordner, damit FrameFlip die geschriebenen Frames selbst findet.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    public JobState State { get; private set; } = JobState.Preparing;

    public int CurrentFrame { get; private set; }
    public int FramesWritten { get; private set; }

    /// <summary>Zuletzt geschriebene Datei. Das ist die Live-Vorschau.</summary>
    public string? LatestFrameFile { get; private set; }

    public RenderStats Stats { get; private set; }

    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; private set; }

    /// <summary>Frames im Auftrag. Mindestens einer, auch bei einem Einzelbild.</summary>
    public int TotalFrames => Math.Max(1, LastFrame - FirstFrame + 1);

    public bool IsRunning => State is JobState.Preparing or JobState.Rendering;

    public TimeSpan Elapsed => (EndedUtc ?? DateTime.UtcNow) - StartedUtc;

    /// <summary>Mittlere Dauer je Frame ueber die letzten Frames, oder null.</summary>
    public double? SecondsPerFrame
        => _frameSeconds.Count > 0 ? _frameSeconds.Average() : null;

    /// <summary>
    /// Dauer jedes fertigen Frames, in Reihenfolge. Kopie, weil die Liste von einem
    /// Hintergrundthread waechst waehrend die Anzeige sie zeichnet.
    /// </summary>
    public double[] FrameDurations
    {
        get { lock (_allFrameSeconds) return _allFrameSeconds.ToArray(); }
    }

    /// <summary>Der teuerste bisher gerenderte Frame, oder null.</summary>
    public double? SlowestFrame
    {
        get { lock (_allFrameSeconds) return _allFrameSeconds.Count > 0 ? _allFrameSeconds.Max() : null; }
    }

    public double? FastestFrame
    {
        get { lock (_allFrameSeconds) return _allFrameSeconds.Count > 0 ? _allFrameSeconds.Min() : null; }
    }

    /// <summary>
    /// Fortschritt ueber den ganzen Auftrag. Der laufende Frame zaehlt anteilig mit,
    /// soweit sein Sample-Fortschritt bekannt ist - sonst stuende der Balken bei einer
    /// Sequenz aus zehn Frames minutenlang still und spraenge dann um ein Zehntel.
    /// </summary>
    public double Progress
    {
        get
        {
            double done = FramesWritten;

            if (State == JobState.Rendering && Stats.SampleProgress is double partial)
                done += Math.Clamp(partial, 0, 1);

            return Math.Clamp(done / TotalFrames, 0, 1);
        }
    }

    /// <summary>
    /// Geschaetzte Restzeit fuer den ganzen Auftrag.
    ///
    /// Aus der gemessenen Dauer der bisherigen Frames, nicht aus Blenders Angabe: Die
    /// gilt nur fuer den laufenden Frame und fehlt in der Oberflaeche ganz. Solange
    /// noch kein Frame fertig ist, gibt es keine Grundlage - dann eben nichts, statt
    /// einer erfundenen Zahl.
    /// </summary>
    public TimeSpan? Remaining
    {
        get
        {
            if (!IsRunning) return null;
            if (SecondsPerFrame is not double perFrame || perFrame <= 0) return null;

            double left = TotalFrames - FramesWritten;

            // Den laufenden Frame anteilig abziehen, soweit bekannt.
            if (Stats.SampleProgress is double partial) left -= Math.Clamp(partial, 0, 1);

            return left <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(left * perFrame);
        }
    }

    // ---------------------------------------------------------------- Ereignisse

    public void BeginFrame(int frame)
    {
        CurrentFrame = frame;
        State = JobState.Rendering;
        _frameStartedTicks = Environment.TickCount64;
    }

    public void FrameWritten(int frame, string? path)
    {
        CurrentFrame = frame;
        FramesWritten++;
        if (!string.IsNullOrEmpty(path)) LatestFrameFile = path;

        if (_frameStartedTicks > 0)
        {
            double seconds = (Environment.TickCount64 - _frameStartedTicks) / 1000.0;

            // Ausreisser nach unten sind Doppelmeldungen, nach oben ein Rechner, der
            // zwischendurch stand. Beides verzerrt die Schaetzung mehr als es hilft.
            if (seconds is > 0.02 and < 60 * 60 * 6)
            {
                _frameSeconds.Enqueue(seconds);
                while (_frameSeconds.Count > TimingWindow) _frameSeconds.Dequeue();

                lock (_allFrameSeconds) _allFrameSeconds.Add(seconds);
            }
        }

        _frameStartedTicks = Environment.TickCount64;

        // Der Sample-Zaehler des fertigen Frames gilt nicht mehr fuer den naechsten.
        Stats = Stats with { Sample = null };
    }

    public void UpdateStats(RenderStats stats)
    {
        // Nichts Gelesenes soll Gelesenes verdraengen: Faellt der Sample-Zaehler aus
        // dem Text, bleibt der letzte bekannte stehen, statt die Anzeige leer zu
        // blinken.
        Stats = new RenderStats(
            stats.Sample ?? Stats.Sample,
            stats.SampleTotal ?? Stats.SampleTotal,
            stats.MemoryMb ?? Stats.MemoryMb,
            stats.PeakMemoryMb ?? Stats.PeakMemoryMb,
            stats.FrameRemaining ?? Stats.FrameRemaining,
            stats.Activity ?? Stats.Activity);
    }

    public void Finish(JobState state)
    {
        State = state;
        EndedUtc = DateTime.UtcNow;
    }
}
