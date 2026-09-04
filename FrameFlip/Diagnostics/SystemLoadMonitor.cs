using System.Diagnostics;
using FrameFlip.Interop;

namespace FrameFlip.Diagnostics;

public enum LoadLevel
{
    /// <summary>Nichts los - FrameFlip darf zulangen.</summary>
    Idle,
    Moderate,
    Busy,
    /// <summary>Maschine am Anschlag - FrameFlip macht sich so klein wie moeglich.</summary>
    Critical
}

public sealed record LoadSnapshot(double CpuPercent, double? GpuPercent, long AvailableMb, LoadLevel Level)
{
    /// <summary>
    /// True, wenn der Arbeitsspeicher knapp ist - im Unterschied zu blosser CPU-Last.
    ///
    /// Die Unterscheidung ist wesentlich: bei CPU-Last muss der Decoder langsamer
    /// werden, der PUFFER aber gerade nicht kleiner. Er ist dann die einzige Reserve,
    /// aus der die Wiedergabe noch fluessig laufen kann. Nur wenn wirklich der
    /// Speicher fehlt, darf gekuerzt werden.
    /// </summary>
    public bool MemoryTight { get; init; }

    public string Describe()
    {
        string gpu = GpuPercent is null ? "GPU n/a" : $"GPU {GpuPercent.Value:0}%";
        string memory = MemoryTight ? ", Speicher knapp" : "";
        return $"{Level}: CPU {CpuPercent:0}%, {gpu}, frei {AvailableMb} MB{memory}";
    }
}

/// <summary>Wie viel sich FrameFlip bei der aktuellen Systemlast nehmen darf.</summary>
public sealed record ResourceProfile(
    LoadLevel Level,
    int DecoderThreads,
    ThreadPriority ThreadPriority,
    ProcessPriorityClass ProcessPriority,
    double WindowScale,
    double BudgetScale)
{
    /// <summary>Ausgangslage, bis die erste Messung vorliegt: zurueckhaltend.</summary>
    public static ResourceProfile Conservative { get; } = new(
        LoadLevel.Busy, 1, ThreadPriority.Lowest, ProcessPriorityClass.BelowNormal, 1.0, 1.0);
}

/// <summary>
/// Misst CPU, GPU und freien Arbeitsspeicher, solange eine Vorschau offen ist, und
/// leitet daraus ab, wie aggressiv der Decoder arbeiten darf.
///
/// Der eigene Verbrauch wird von der CPU-Messung abgezogen - sonst sieht FrameFlip
/// die Last, die es selbst erzeugt, und drosselt sich grundlos.
/// </summary>
public sealed class SystemLoadMonitor : IDisposable
{
    private readonly GpuLoadCounter _gpu = new();
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly object _gate = new();
    private readonly int _maxThreads;
    private readonly TimeSpan _interval;

    private Timer? _timer;
    private long _prevIdle, _prevKernel, _prevUser;
    private TimeSpan _prevOwnCpu;
    private bool _primed;
    private bool _disposed;

    public event Action<LoadSnapshot, ResourceProfile>? Updated;

    public ResourceProfile Current { get; private set; } = ResourceProfile.Conservative;
    public LoadSnapshot? LastSnapshot { get; private set; }

    /// <summary>
    /// Obergrenze fuer Decoder-Threads.
    ///
    /// Frueher die halbe Kernzahl. Das war zu knapp: ein 1080p-PNG mit 8,5 MB
    /// braucht rund 46 ms zum Entpacken, fuer 60 fps sind das 2,8 Kerne
    /// Dauerlast - mit der halben Kernzahl als Deckel und der Halbierung bei
    /// mittlerer Last blieben davon zwei Threads und 37 Bilder je Sekunde uebrig.
    ///
    /// Zwei Kerne bleiben frei, damit ein laufender Render und die Oberflaeche
    /// Luft behalten. Heruntergeregelt wird ohnehin ueber die Laststufen.
    /// </summary>
    public static int ThreadCeiling => Math.Max(1, Environment.ProcessorCount - 2);

    /// <param name="maxThreads">Obergrenze aus den Einstellungen; zusaetzlich auf ThreadCeiling begrenzt.</param>
    public SystemLoadMonitor(int maxThreads, TimeSpan interval)
    {
        _maxThreads = Math.Clamp(Math.Min(maxThreads, ThreadCeiling), 1, 16);
        _interval = interval < TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : interval;
    }

    public int MaxDecoderThreads => _maxThreads;

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null || _disposed) return;

            Sample();       // Basiswerte
            Thread.Sleep(0);

            // Erste echte Messung schnell, danach im konfigurierten Takt.
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(600), _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _primed = false;
        }
    }

    private void Tick()
    {
        try
        {
            var snapshot = Sample();
            if (snapshot is null) return;

            var profile = Derive(snapshot);
            Current = profile;
            LastSnapshot = snapshot;

            // Immer melden, nicht nur bei Stufenwechsel: die Anzeige zeigt auch den Messwert.
            Updated?.Invoke(snapshot, profile);
        }
        catch (Exception)
        {
            // Eine fehlgeschlagene Messung darf die Wiedergabe nicht beruehren.
        }
    }

    private LoadSnapshot? Sample()
    {
        if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user)) return null;

        _self.Refresh();
        TimeSpan ownCpu = _self.TotalProcessorTime;

        if (!_primed)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _prevOwnCpu = ownCpu;
            _primed = true;
            _gpu.Read();       // Basiswert fuer die Differenzbildung
            return null;
        }

        long idleDelta = idle - _prevIdle;
        long totalDelta = (kernel - _prevKernel) + (user - _prevUser);   // kernel enthaelt idle
        long ownDelta = (ownCpu - _prevOwnCpu).Ticks;                    // beides in 100-ns-Einheiten

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        _prevOwnCpu = ownCpu;

        double cpu = 0;
        if (totalDelta > 0)
        {
            double busy = totalDelta - idleDelta - ownDelta;             // eigener Anteil raus
            cpu = Math.Clamp(100.0 * busy / totalDelta, 0, 100);
        }

        long availableMb = 0;
        var status = new NativeMethods.MEMORYSTATUSEX { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref status))
            availableMb = (long)(status.AvailablePhysical / (1024 * 1024));

        double? gpu = _gpu.Read();

        // Unter 2 GB frei gilt der Speicher als knapp. 0 heisst "nicht messbar" und
        // wird nicht als Mangel gewertet.
        bool memoryTight = availableMb > 0 && availableMb < 2048;

        return new LoadSnapshot(cpu, gpu, availableMb, ClassifyWithHysteresis(cpu, gpu, availableMb))
        {
            MemoryTight = memoryTight,
        };
    }

    /// <summary>
    /// Schwellen mit Totband: eine Stufe wird erst verlassen, wenn der Wert sie
    /// deutlich ueberschreitet. Sonst pendelt das Profil im 10-s-Takt hin und her.
    /// </summary>
    private LoadLevel ClassifyWithHysteresis(double cpu, double? gpu, long availableMb)
    {
        double pressure = Math.Max(cpu, gpu ?? 0);
        LoadLevel current = Current.Level;

        const double margin = 7.0;
        double idleLimit = current == LoadLevel.Idle ? 20 + margin : 20;
        double moderateLimit = current <= LoadLevel.Moderate ? 45 + margin : 45;
        double busyLimit = current <= LoadLevel.Busy ? 80 + margin : 80;

        LoadLevel level =
            pressure < idleLimit ? LoadLevel.Idle :
            pressure < moderateLimit ? LoadLevel.Moderate :
            pressure < busyLimit ? LoadLevel.Busy :
            LoadLevel.Critical;

        // Knapper Arbeitsspeicher schlaegt die CPU-Einstufung.
        if (availableMb > 0 && availableMb < 1024) return LoadLevel.Critical;
        if (availableMb > 0 && availableMb < 2048 && level < LoadLevel.Busy) return LoadLevel.Busy;

        return level;
    }

    /// <summary>
    /// Leitet aus der Messung ab, wie viel sich FrameFlip nehmen darf.
    ///
    /// Threadzahl und Prioritaet folgen der CPU-Last - das ist die Groesse, um die
    /// ein laufender Render konkurriert. Die Puffergroesse folgt dagegen NUR dem
    /// freien Arbeitsspeicher.
    ///
    /// Die fruehere Fassung kuerzte den Puffer zusammen mit der Threadzahl, sobald
    /// die Maschine ausgelastet war. Das war ein Denkfehler: gerade dann, wenn der
    /// Decoder nur noch einen Thread hat, ist ein GROSSER Vorrat die einzige Reserve,
    /// aus der die Wiedergabe fluessig laufen kann. Nachgemessen mit 1080p-Material:
    /// bei 512 MB Budget fassen 64 Frames 2,7 Sekunden - auf 70 % gekuerzt bleiben
    /// 1,8 Sekunden, und der Ring laeuft beim ersten Stocken leer.
    /// </summary>
    private ResourceProfile Derive(LoadSnapshot snapshot)
    {
        var (threads, threadPriority, processPriority) = snapshot.Level switch
        {
            LoadLevel.Idle => (_maxThreads, ThreadPriority.Normal, ProcessPriorityClass.Normal),
            // Zwei Drittel statt der Haelfte: das Halbieren riss die Bildrate bei
            // 60-fps-Material unter das Ziel, obwohl die Maschine noch Luft hatte.
            LoadLevel.Moderate => (Math.Max(1, _maxThreads * 2 / 3), ThreadPriority.BelowNormal,
                                   ProcessPriorityClass.BelowNormal),
            LoadLevel.Busy => (1, ThreadPriority.Lowest, ProcessPriorityClass.BelowNormal),
            _ => (1, ThreadPriority.Lowest, ProcessPriorityClass.BelowNormal),
        };

        // Nur echter Speichermangel verkleinert den Ring - und dann deutlich, weil
        // Auslagern die Wiedergabe schlimmer trifft als ein kurzer Puffer.
        double windowScale = 1.0, budgetScale = 1.0;
        if (snapshot.MemoryTight)
        {
            bool severe = snapshot.AvailableMb > 0 && snapshot.AvailableMb < 1024;
            windowScale = severe ? 0.35 : 0.6;
            budgetScale = severe ? 0.4 : 0.7;
        }

        return new ResourceProfile(snapshot.Level, threads, threadPriority, processPriority,
                                   windowScale, budgetScale);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _gpu.Dispose();
        _self.Dispose();
        Updated = null;
    }
}
