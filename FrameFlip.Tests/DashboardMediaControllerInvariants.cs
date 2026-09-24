using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Dashboard;
using FrameFlip.Export;
using FrameFlip.Playback;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

public static class DashboardMediaControllerInvariants
{
    public static void Run()
    {
        using var loop = new UiLoop();
        Frames(loop);
        Videos(loop);
        WindowVideo();
    }

    private static void Frames(UiLoop loop)
    {
        Check.Group("Dashboard-Bildcontroller - Sitzungen und Fehlerrueckgaben");
        var loaders = new List<DashboardFrameInvariants.Loader>();
        var progress = new List<PreloadProgress>();
        var shown = new List<BitmapSource>();
        bool failFactory = false;
        var image = DashboardFrameInvariants.Picture(50);
        var sources = new DashboardFrameSources((_, _) => image, (paths, pace, report) =>
        {
            if (failFactory) throw new IOException("synthetic factory failure");
            var loader = new DashboardFrameInvariants.Loader(paths, report);
            loaders.Add(loader);
            return loader;
        });
        using var controller = new DashboardFrameController(sources, loop.Post, shown.Add, progress.Add,
            () => 960, () => PreloadPace.Medium);
        controller.Reset(Sequence());
        var old = controller.PreloadAsync(800, 1024, 1);
        Check.That(controller.IsPreloading && loaders.Count == 1, "ein Vorlader besitzt die laufende Sitzung");
        var duplicate = controller.PreloadAsync(800, 1024, 1);
        Check.That(duplicate.IsCompletedSuccessfully && duplicate.Result is null && loaders.Count == 1,
            "ein zweiter Start erzeugt keinen parallelen Vorlader");
        controller.Reset(Sequence());
        Check.That(loaders[0].Cancels == 1 && loaders[0].Disposals == 0,
            "Wechsel bricht sofort ab, entsorgt den Lader aber erst nach dessen Ende");
        var next = controller.PreloadAsync(800, 1024, 1);
        loaders[0].Frames = new BitmapSource?[] { image, image };
        loaders[0].Complete(true);
        loop.Until(() => old.IsCompleted);
        Check.That(old.Result is null && controller.IsPreloading && controller.CachedFrameCount == 0,
            "das Ende des alten Laders gibt weder neuen Besitzer noch fremden Cache frei");
        loaders[1].Frames = new BitmapSource?[] { image, null };
        loaders[1].Complete(false);
        loop.Until(() => next.IsCompleted);
        Check.That(next.Result is { Whole: false, Loaded: 1, Total: 2 } && controller.IsCurrent(next.Result),
            "der aktuelle Lader liefert einen brauchbaren Teilerfolg");
        controller.Request(0);
        Check.That(shown.Count == 1 && ReferenceEquals(shown[0], image), "das angenommene Bild ist sofort im Cache erreichbar");
        controller.CancelPreload();
        Check.That(!controller.IsCurrent(next.Result!), "auch ein schon zurueckgegebener Abschluss kann vor der UI-Fortsetzung verfallen");
        failFactory = true;
        var failed = controller.PreloadAsync(800, 1024, 1);
        loop.Until(() => failed.IsCompleted);
        Check.That(failed.Result is { Whole: false, Loaded: 0 } && !controller.IsPreloading,
            "ein Fehler beim Erzeugen hinterlaesst keinen blockierten Vorladestatus");
        failFactory = false;
        var closing = controller.PreloadAsync(800, 1024, 1);
        var last = loaders.Last();
        controller.Dispose();
        controller.Dispose();
        last.Report(new PreloadProgress(2, 2, PreloadPace.Fast, 10));
        last.Complete(true);
        loop.Until(() => closing.IsCompleted);
        Check.That(closing.Result is null && last.Cancels == 1 && last.Disposals == 1 && progress.Count == 0,
            "Beenden verwirft Fortschritt und Abschluss und gibt den letzten Lader genau einmal frei");
        controller.Reset(Sequence());
        controller.Request(0);
        Check.That(controller.CachedFrameCount == 0 && controller.PreloadAsync(800, 1024, 1).Result is null,
            "nach Dispose starten weder Cache noch Vorlader neu");
    }

    private static void Videos(UiLoop loop)
    {
        Check.Group("Dashboard-Vorbereitung - Snapshot, Wiederverwendung und Export");
        var prints = new List<VideoFingerprint>();
        var jobs = new List<(ExportRequest Request, ProcessPriorityClass Priority, CancellationToken Token, TaskCompletionSource<ExportResult> Done)>();
        var noted = new List<string>();
        int folders = 0;
        string? reusable = "synthetic-existing.mp4";
        bool failRead = false;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool block = true;
        var sources = new DashboardVideoSources(paths =>
        {
            if (failRead) throw new IOException("synthetic fingerprint failure");
            if (block)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic fingerprint not released");
            }
            return 12345;
        }, (print, _) => { prints.Add(print); return reusable; }, () => folders++,
        (_, extension) => "synthetic-output" + extension, (_, path) => noted.Add(path),
        (exe, request, priority, token) =>
        {
            var done = new TaskCompletionSource<ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.Add((request, priority, token, done));
            return done.Task;
        });
        ProcessPriorityClass priority = ProcessPriorityClass.BelowNormal;
        using var controller = new DashboardVideoController(sources, () => priority);
        var frames = Sequence().Frames.ToList();
        var request = new DashboardVideoRequest("synthetic-ffmpeg", "frame_####.png", 1, 3,
            frames, 24, 1920, 1080, ExportPreset.H264, 3);
        try
        {
            var reused = controller.PrepareAsync(request);
            Check.That(entered.Wait(TimeSpan.FromSeconds(5)), "der Fingerabdruck liest seine Zeitstempel im Hintergrund");
            Check.That(controller.PrepareAsync(request).Result is null, "eine laufende Vorbereitung wird nicht doppelt gestartet");
            frames.Clear();
            release.Set();
            loop.Until(() => reused.IsCompleted);
            Check.That(reused.Result is { Reused: true, Path: "synthetic-existing.mp4" } && jobs.Count == 0 && folders == 0,
                "eine fertige passende Datei wird ohne Encoder oder Ordneranlage wiederverwendet");
            Check.That(prints[0] is { First: 1, Last: 3, Count: 2, Fps: 24, SourceWidth: 1920, SourceHeight: 1080, NewestTicks: 12345 },
                "Fingerabdruck und Frame-Liste bleiben ein Snapshot des angeforderten Bereichs");
            Check.That(reused.Result is { } ready && controller.PreparedPath == ready.Path && !controller.IsPreparing,
                "die wiederverwendete Datei beendet die Vorbereitung");
            controller.Reset();
            Check.That(controller.PreparedPath is null && reused.Result is { } retired && !controller.IsCurrent(retired),
                "ein Auswahlwechsel verwirft vorbereiteten Pfad und schon zurueckgegebenes UI-Ergebnis");

            block = false;
            reusable = null;
            request = request with { Frames = Sequence().Frames };
            priority = ProcessPriorityClass.Idle;
            var export = controller.PrepareAsync(request);
            loop.Until(() => jobs.Count == 1);
            var job = jobs[0];
            Check.That(job.Priority == ProcessPriorityClass.Idle && job.Request is
                { Fps: 24, SourceWidth: 1920, SourceHeight: 1080, Threads: 3, TargetWidth: 0, Gaps: GapHandling.HoldLast }
                && job.Request.Frames.Count == 2 && folders == 1,
                "der Encoder erhaelt die bestehenden Exportwerte und die aktuelle Renderprioritaet");
            job.Done.SetResult(new ExportResult(true, false, null, "ready.mp4"));
            loop.Until(() => export.IsCompleted);
            Check.That(export.Result is { Path: "ready.mp4", Reused: false } && noted.SequenceEqual(new[] { "ready.mp4" }),
                "erst ein erfolgreicher aktueller Export bekommt seine Cache-Metadaten");

            var old = controller.PrepareAsync(request);
            loop.Until(() => jobs.Count == 2);
            controller.Reset();
            Check.That(jobs[1].Token.IsCancellationRequested && !controller.IsPreparing && controller.PreparedPath is null,
                "Wechsel meldet Abbruch sofort und gibt die alte Vorbereitung frei");
            var next = controller.PrepareAsync(request);
            var overlapWindow = Task.Delay(100);
            loop.Until(() => jobs.Count == 3 || overlapWindow.IsCompleted);
            Check.That(jobs.Count == 2 && controller.IsPreparing,
                "ein Nachfolger wartet auf die Dateifreigabe des alten Encoders statt denselben Zielpfad parallel zu schreiben");
            jobs[1].Done.SetResult(new ExportResult(true, false, null, "obsolete.mp4"));
            loop.Until(() => old.IsCompleted && jobs.Count == 3);
            Check.That(old.Result is null && controller.IsPreparing && noted.Count == 1 && controller.PreparedPath is null,
                "ein verspaeteter alter Erfolg veraendert weder Nachfolger noch Cache-Metadaten");
            jobs[2].Done.SetResult(new ExportResult(false, false, "synthetic export failure", null));
            loop.Until(() => next.IsCompleted);
            Check.That(next.Result?.Error == "synthetic export failure" && !controller.IsPreparing,
                "ein aktueller Exportfehler bleibt sichtbar und gibt die Sitzung frei");
            failRead = true;
            var failed = controller.PrepareAsync(request);
            loop.Until(() => failed.IsCompleted);
            Check.That(failed.Result?.Error == "synthetic fingerprint failure" && jobs.Count == 3,
                "ein Fehler beim Fingerabdruck startet keinen Encoder");
            failRead = false;
            entered.Reset();
            release.Reset();
            block = true;
            int reads = prints.Count, preparedFolders = folders;
            var cancelledRead = controller.PrepareAsync(request);
            Check.That(entered.Wait(TimeSpan.FromSeconds(5)), "der abbrechbare Fingerabdruck hat seinen Dateizugriff begonnen");
            controller.Cancel();
            release.Set();
            loop.Until(() => cancelledRead.IsCompleted);
            Check.That(cancelledRead.Result is null && prints.Count == reads && folders == preparedFolders && jobs.Count == 3,
                "Abbruch waehrend des Dateizugriffs verhindert Cache-Suche, Ordneranlage und Encoderstart");
            block = false;
            var closing = controller.PrepareAsync(request);
            loop.Until(() => jobs.Count == 4);
            controller.Dispose();
            controller.Dispose();
            jobs[3].Done.SetResult(new ExportResult(true, false, null, "after-close.mp4"));
            loop.Until(() => closing.IsCompleted);
            Check.That(closing.Result is null && jobs[3].Token.IsCancellationRequested && noted.Count == 1,
                "Schliessen verhindert auch bei spaetem Erfolg weitere Cache-Schreibzugriffe");
            Check.That(controller.PrepareAsync(request).Result is null && jobs.Count == 4,
                "ein geschlossener Controller nimmt keine weitere Vorbereitung an");
        }
        finally
        {
            release.Set();
            foreach (var job in jobs) job.Done.TrySetResult(new ExportResult(false, true, null, null));
        }
    }

    private static void WindowVideo()
    {
        Check.Group("Dashboard-Vorbereitung - Fensteranbindung");
        using var h = new DashboardFrameInvariants.Harness();
        string executable = Path.Combine(h.Root, "synthetic-ffmpeg.exe");
        File.WriteAllBytes(executable, Array.Empty<byte>());
        var jobs = new List<(ExportRequest Request, CancellationToken Token, TaskCompletionSource<ExportResult> Done)>();
        int notes = 0;
        var sources = new DashboardVideoSources(_ => 0, (_, _) => null, () => { },
            (_, _) => "synthetic-output.mp4", (_, _) => notes++, (_, request, _, token) =>
            {
                var done = new TaskCompletionSource<ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                jobs.Add((request, token, done));
                return done.Task;
            });
        var settings = new AppSettings { Prebuffer = false, PrepareVideo = false, MemoryBudgetMb = 128, FfmpegPath = executable };
        h.Open(sources, settings);
        h.Set("_inPoint", 2);
        h.Set("_outPoint", 3);
        h.Set("_fps", 30d);
        var controller = (DashboardVideoController)h.Read("_videos")!;
        var sequence = (ImageSequence)h.Read("_sequence")!;
        try
        {
            var first = (Task)h.Call("PrepareVideoAsync", sequence)!;
            h.Until(() => jobs.Count == 1);
            Check.That(jobs[0].Request.Frames.Select(f => f.Number).SequenceEqual(new[] { 2, 3 }) && jobs[0].Request.Fps == 30,
                "das Fenster uebergibt den gewaehlten Bereich und seine Bildrate");
            h.Call("SetPrepareVideo", false);
            Check.That(jobs[0].Token.IsCancellationRequested && !controller.IsPreparing,
                "Ausschalten bricht die laufende Vorbereitung ab");
            h.Call("SetPrepareVideo", true);
            jobs[0].Done.SetResult(new ExportResult(true, false, null, "obsolete.mp4"));
            h.Until(() => first.IsCompleted && jobs.Count == 2);
            Check.That(settings.PrepareVideo && controller.IsPreparing && notes == 0,
                "Einschalten startet sofort neu und ein alter Erfolg beendet den Nachfolger nicht");
            jobs[1].Done.SetResult(new ExportResult(true, false, null, "ready.mp4"));
            h.Until(() => controller.PreparedPath == "ready.mp4");
            h.Call("DropCache");
            Check.That(controller.PreparedPath is null && notes == 1,
                "Cache-Verwerfen im Fenster gibt auch die fertige Videovorbereitung frei");
            h.Call("Play");
            h.Until(() => jobs.Count == 3);
            h.Close();
            jobs[2].Done.SetResult(new ExportResult(true, false, null, "closed.mp4"));
            h.Pump();
            Check.That(jobs[2].Token.IsCancellationRequested && !controller.IsPreparing && controller.PreparedPath is null,
                "Abspielen startet die Vorbereitung und Fensterschliessen beendet ihren Besitzer");
        }
        finally
        {
            foreach (var job in jobs) job.Done.TrySetResult(new ExportResult(false, true, null, null));
            h.Pump();
        }
    }

    private static ImageSequence Sequence() => new(new SequencePattern(@"C:\synthetic", "frame_", 4, "", ".png"),
        new[] { new SequenceFrame(1, @"C:\synthetic\frame_0001.png", "frame_0001.png"),
                new SequenceFrame(3, @"C:\synthetic\frame_0003.png", "frame_0003.png") });

    private sealed class UiLoop : IDisposable
    {
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        internal UiLoop() => SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        internal void Post(Action action) { if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(action); }
        internal void Until(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(6))
            {
                var frame = new DispatcherFrame();
                _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(1);
            }
            if (!ready()) throw new TimeoutException("Dashboard media operation did not finish");
        }
        public void Dispose() => SynchronizationContext.SetSynchronizationContext(_previous);
    }
}
