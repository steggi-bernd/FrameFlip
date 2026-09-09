using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Lastbedarf und wartende Messwerte ohne echte Timer, Prozessprioritaeten oder Benutzerdateien.</summary>
public static class LoadLifecycleInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly LoadSnapshot Snapshot = new(12, 7, 8192, LoadLevel.Idle);
    private static readonly ResourceProfile Profile = ResourceProfile.Conservative with { ProcessPriority = ProcessPriorityClass.Normal };

    public static void Run()
    {
        DemandAndReuse();
        Delivery();
        LateCallbacks();
        RenderMode();
        FirstViewer();
        FailedStartsAndDisposal();
        RealViewerDispatcher();
    }

    private static void DemandAndReuse()
    {
        Check.Group("Lastmonitor - Verbraucher und Wiederverwendung");
        foreach (bool adaptive in new[] { false, true })
        foreach (bool viewer in new[] { false, true })
        foreach (bool main in new[] { false, true })
        foreach (bool remote in new[] { false, true })
        {
            using var host = new Harness();
            host.Settings.AdaptiveResources = adaptive;
            host.Ensure(viewer, main, remote);
            bool wanted = (viewer || main || remote) && (adaptive || remote);
            Check.That((host.Monitor is not null) == wanted && host.Created.Count == (wanted ? 1 : 0),
                       $"Bedarf: adaptiv={adaptive}, Viewer={viewer}, Hauptfenster={main}, Remote={remote}");
        }

        using var running = new Harness();
        running.Ensure(true, false, false);
        var first = running.Created.Single();
        Check.That(first.Starts == 1 && first.Subscribers == 1 && first.Threads == 6 && first.Interval == TimeSpan.FromSeconds(9),
                   "Monitor startet einmal mit Decoderlimit und Messtakt aus den Einstellungen");
        running.Settings.MaxDecoderThreads = 3;
        running.Settings.LoadIntervalSeconds = 4;
        running.Settings.AdaptiveResources = false;
        running.Ensure(false, true, false);
        running.Ensure(false, false, true);
        Check.That(ReferenceEquals(running.Monitor, first) && first.Starts == 1 && first.Subscribers == 1,
                   "Verbraucher- und Einstellungswechsel schneiden eine laufende Messreihe nicht ab");
        running.Ensure(false, false, false);
        Check.That(first.Disposals == 1 && first.Subscribers == 0 && running.Monitor is null,
                   "letzter Verbraucher gibt Monitor und Updated-Abonnement frei");
        Check.That(running.Priorities.Last() == ProcessPriorityClass.BelowNormal, "ohne Verbraucher gilt wieder die niedrige Prozessprioritaet");
        running.Ensure(false, false, true);
        Check.That(running.Created.Count == 2 && running.Created[1].Threads == 3 && running.Created[1].Interval == TimeSpan.FromSeconds(4),
                   "ein neuer Monitor liest die inzwischen geaenderten Einstellungen");
    }

    private static void Delivery()
    {
        Check.Group("Lastmonitor - Messwerte und UI-Rueckkehr");
        using var host = new Harness();
        host.Ensure(false, true, false);
        var monitor = host.Created.Single();
        monitor.Emit(Snapshot, Profile);
        Check.That(ReferenceEquals(host.LastSnapshot, Snapshot) && host.Priorities.Count == 1,
                   "ohne Viewer bleibt der Messwert fuer Hauptfenster und Remote lesbar, ohne Prioritaetswechsel");
        var first = new Target();
        host.Viewer = first;
        Task.Run(() => monitor.Emit(Snapshot, Profile)).GetAwaiter().GetResult();
        Check.That(first.Pending.Count == 1 && first.Applied.Count == 0 && host.Priorities.Count == 1,
                   "Hintergrundmessung wartet vor UI-Zugriff und Prioritaetswechsel auf den Dispatcher");
        first.Drain();
        Check.That(first.Applied.Single() == (Snapshot, Profile) && host.Priorities.Last() == ProcessPriorityClass.Normal,
                   "Dispatcher liefert Messwert und Profil gemeinsam an den Viewer");

        monitor.Emit(Snapshot, Profile);
        var next = new Target();
        host.Viewer = next;
        first.Drain();
        Check.That(first.Applied.Count == 1 && next.Applied.Count == 1, "wartender Systemlastwert erreicht den inzwischen aktuellen Viewer");
        monitor.Emit(Snapshot, Profile);
        host.Viewer = null;
        int priorities = host.Priorities.Count;
        next.Drain();
        Check.That(next.Applied.Count == 1 && host.Priorities.Count == priorities, "nach Viewer-Schliessung wird ein wartender UI-Aufruf verworfen");
        host.Ensure(false, false, false);
        Check.That(host.LastSnapshot is null, "nach Monitorende bleibt kein alter Messwert als aktueller Wert sichtbar");
    }

    private static void LateCallbacks()
    {
        Check.Group("Lastmonitor - spaete Messwerte nach Stop und Austausch");
        using var host = new Harness { Viewer = new Target() };
        host.Ensure(true, false, false);
        var old = host.Created.Single();
        var captured = old.Capture();
        old.Emit(Snapshot, Profile);
        host.Ensure(false, false, false);
        int priorities = host.Priorities.Count;
        host.Viewer.Drain();
        Check.That(host.Viewer.Applied.Count == 0 && host.Priorities.Count == priorities,
                   "vor Stop eingereihte Messung aendert weder Viewer noch zurueckgesetzte Prioritaet");

        host.Ensure(true, false, false);
        captured(Snapshot, Profile);
        Check.That(host.Viewer.Pending.Count == 0, "bereits erfasster Callback des alten Monitors liefert nichts an die neue Sitzung");
        host.Viewer.Drain();
        int applied = host.Viewer.Applied.Count;
        host.Created.Last().Emit(Snapshot, Profile);
        host.Viewer.Drain();
        Check.That(host.Viewer.Applied.Count == applied + 1, "neuer Monitor liefert weiterhin aktuelle Messwerte");
    }

    private static void RenderMode()
    {
        Check.Group("Lastmonitor - Messtakt waehrend eines Renders");
        using var host = new Harness();
        host.Rendering(true);
        Check.That(host.Created.Count == 0, "ein Render-Ereignis allein erzeugt keinen Lastmonitor");
        host.Ensure(false, false, true);
        var first = host.Created.Single();
        Check.That(first.Modes.SequenceEqual(new[] { true }), "spaeter gestarteter Monitor uebernimmt den bereits laufenden Render");
        host.Rendering(true);
        host.Rendering(true);
        Check.That(first.Modes.SequenceEqual(new[] { true }), "wiederholte Fortschrittsmeldungen verschieben die naechste Messung nicht durch erneutes Setzen des Timers");
        host.Rendering(false);
        host.Rendering(true);
        Check.That(first.Modes.TakeLast(2).SequenceEqual(new[] { false, true }), "Render-Ende und naechster Start erreichen den laufenden Monitor");
        host.Ensure(false, false, false);
        host.Ensure(false, false, true);
        Check.That(host.Created.Last().Modes.SequenceEqual(new[] { true }), "ersetzter Monitor behaelt den aktuellen Render-Modus");
    }

    private static void FirstViewer()
    {
        Check.Group("Lastmonitor - erster Viewer ohne andere Verbraucher");
        using var host = new Harness();
        Check.That(host.PrepareViewer() == 6 && host.Monitor is not null,
                   "erster Viewer reserviert vor der Konstruktion Lastmessung und Decoderlimit");
        using var fixedResources = new Harness();
        fixedResources.Settings.AdaptiveResources = false;
        Check.That(fixedResources.PrepareViewer() == 1 && fixedResources.Created.Count == 0,
                   "ohne adaptive Regelung startet der erste Viewer weiterhin mit einem Worker ohne Lastmonitor");
    }

    private static void FailedStartsAndDisposal()
    {
        Check.Group("Lastmonitor - Startfehler und endgueltiges Beenden");
        using var failed = new Harness { FailMonitorStart = true, Viewer = new Target() };
        Check.Throws<InvalidOperationException>(() => failed.Ensure(true, false, false), "Fehler beim Monitorstart wird an den Aufrufer weitergegeben");
        var partial = failed.Created.Single();
        Check.That(!failed.Controller.IsRunning && partial.Disposals == 1 && partial.Subscribers == 0,
                   "teilweise gestarteter Monitor wird freigegeben und abgemeldet");
        failed.FailMonitorStart = false;
        failed.Ensure(true, false, false);
        var current = failed.Created.Last();
        current.Emit(Snapshot, Profile);
        failed.Host.Dispose();
        int priorities = failed.Priorities.Count;
        failed.Viewer.Drain();
        failed.Rendering(true);
        failed.Ensure(true, true, true);
        failed.Host.Dispose();
        Check.That(current.Disposals == 1 && current.Subscribers == 0 && !failed.Controller.IsRunning
                   && failed.Created.Count == 2 && failed.Priorities.Count == priorities && failed.Viewer.Applied.Count == 0,
                   "Host-Ende verwirft wartende Werte; spaete Aufrufe starten nichts neu und geben nicht doppelt frei");

        int workers = 0;
        using var opening = new Harness((_, count) =>
        {
            workers = count;
            throw new InvalidOperationException("Synthetisch fehlgeschlagener Viewer-Aufbau");
        });
        var sequence = new ImageSequence(new SequencePattern(".", "frame_", 4, "", ".png"),
            new[] { new SequenceFrame(1, "missing.png", "frame_0001.png") });
        var request = new ViewerOpenRequest(sequence, "missing.png", 0, IntPtr.Zero, 80, 40);
        var error = CatchViewerOpen(opening.Host, request);
        Check.That(error is InvalidOperationException && workers == 6, "Viewer-Fabrik erhaelt das Decoderlimit vor dem simulierten Fehler");
        Check.That(!opening.Controller.IsRunning && opening.Created.Single().Disposals == 1,
                   "fehlgeschlagener Viewer-Aufbau hinterlaesst keinen reservierten Lastmonitor");
    }

    private static Exception? CatchViewerOpen(AppHost host, ViewerOpenRequest request)
    {
        try { typeof(AppHost).GetMethod("OpenNewViewer", Hidden)!.Invoke(host, new object[] { request }); }
        catch (TargetInvocationException error) { return error.InnerException; }
        return null;
    }

    private static void RealViewerDispatcher()
    {
        Check.Group("Lastmonitor - echte WPF-Zustellung");
        var sequence = new ImageSequence(new SequencePattern(".", "frame_", 4, "", ".png"),
            new[] { new SequenceFrame(1, "missing.png", "frame_0001.png") });
        var settings = new AppSettings { AdaptiveResources = true, RawCacheEnabled = false, MemoryBudgetMb = 32 };
        var window = new ViewerWindow(sequence, 0, settings, _ => { }, FrameDecoderRegistry.CreateDefault(),
            new PixelRect(0, 0, 80, 40), 80, 40, 1);
        var monitor = new Monitor(4, TimeSpan.FromSeconds(10));
        var priorities = new List<ProcessPriorityClass>();
        using var controller = new AppLoadController(() => settings,
            new AppLoadSources((_, _) => monitor, () => new AppViewerLoadTarget(window), priorities.Add));
        try
        {
            controller.Ensure(true, false, false);
            Task.Run(() => monitor.Emit(Snapshot, Profile)).GetAwaiter().GetResult();
            var lastLoad = typeof(ViewerWindow).GetField("_lastLoad", Hidden)!;
            Check.That(lastLoad.GetValue(window) is null && priorities.Count == 1, "echter WPF-Viewer wird nicht vom Messthread aus beschrieben");
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Check.That(ReferenceEquals(lastLoad.GetValue(window), Snapshot)
                       && ReferenceEquals(typeof(ViewerWindow).GetField("_profile", Hidden)!.GetValue(window), Profile)
                       && priorities.Last() == ProcessPriorityClass.Normal,
                       "WPF-Dispatcher uebernimmt Messwert, Profil und zugehoerige Prioritaet");
        }
        finally
        {
            controller.Dispose();
            window.Close();
        }
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AppHost Host;
        internal readonly List<Monitor> Created = new();
        internal readonly List<ProcessPriorityClass> Priorities = new();
        internal Target? Viewer;
        internal bool FailMonitorStart;
        internal Harness(Func<ViewerOpenRequest, int, ViewerWindow>? createViewer = null)
        {
            Host = new AppHost(new AppLoadSources((threads, interval) =>
            {
                var monitor = new Monitor(threads, interval) { FailStart = FailMonitorStart };
                Created.Add(monitor);
                return monitor;
            }, () => Viewer, Priorities.Add), createViewer);
            typeof(AppHost).GetField("_settings", Hidden)!.SetValue(Host,
                new AppSettings { AdaptiveResources = true, MaxDecoderThreads = 6, LoadIntervalSeconds = 9 });
        }
        internal AppSettings Settings => (AppSettings)typeof(AppHost).GetField("_settings", Hidden)!.GetValue(Host)!;
        internal AppLoadController Controller => (AppLoadController)typeof(AppHost).GetField("_load", Hidden)!.GetValue(Host)!;
        internal IAppLoadMonitor? Monitor => Controller.IsRunning ? Created.Last() : null;
        internal LoadSnapshot? LastSnapshot => Controller.LastSnapshot;
        internal void Ensure(bool viewer, bool main, bool remote) => Controller.Ensure(viewer, main, remote);
        internal void Rendering(bool rendering) => Controller.SetRenderMode(rendering);
        internal int PrepareViewer() => (int)Call("PrepareViewerLoad")!;
        private object? Call(string method, params object[] args) => typeof(AppHost).GetMethod(method, Hidden)!.Invoke(Host, args);
        public void Dispose() => Host.Dispose();
    }

    private sealed class Monitor(int threads, TimeSpan interval) : IAppLoadMonitor
    {
        public event Action<LoadSnapshot, ResourceProfile>? Updated;
        public LoadSnapshot? LastSnapshot { get; private set; }
        public int MaxDecoderThreads => threads;
        internal int Threads => threads;
        internal TimeSpan Interval => interval;
        internal int Starts, Disposals;
        internal bool FailStart;
        internal int Subscribers => Updated?.GetInvocationList().Length ?? 0;
        internal readonly List<bool> Modes = new();
        public void Start()
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("Synthetisch fehlgeschlagener Monitorstart");
        }
        public void SetRenderMode(bool rendering) => Modes.Add(rendering);
        public void Dispose() => Disposals++;
        internal Action<LoadSnapshot, ResourceProfile> Capture() => Updated!;
        internal void Emit(LoadSnapshot snapshot, ResourceProfile profile)
        {
            LastSnapshot = snapshot;
            Updated?.Invoke(snapshot, profile);
        }
    }

    private sealed class Target : IAppLoadTarget
    {
        internal readonly Queue<Action> Pending = new();
        internal readonly List<(LoadSnapshot, ResourceProfile)> Applied = new();
        public void Dispatch(Action callback) => Pending.Enqueue(callback);
        public void ApplyLoad(LoadSnapshot snapshot, ResourceProfile profile) => Applied.Add((snapshot, profile));
        internal void Drain() { while (Pending.TryDequeue(out var callback)) callback(); }
    }
}
