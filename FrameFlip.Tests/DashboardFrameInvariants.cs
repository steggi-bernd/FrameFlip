using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Dashboard;
using FrameFlip.Playback;
using FrameFlip.Projects;
using FrameFlip.Views;
using Image = System.Windows.Controls.Image;

namespace FrameFlip.Tests;

public static class DashboardFrameInvariants
{
    public static void Run()
    {
        DecodeQueue();
        PreloadAndRange();
        DefaultPreloader();
    }

    private static void DecodeQueue()
    {
        Check.Group("Dashboard-Bilder - ein Decoder und der letzte Wunsch");
        using var h = new Harness(blockReads: true);
        h.Open();
        h.Until(() => h.Pending.Count == 1);
        h.Call("ShowFrame", 2);
        h.Call("ShowFrame", 3);
        Check.That(h.Reads.Count == 1, "weitere Frame-Wuensche starten keinen zweiten gleichzeitigen Decoder");
        var first = h.Pending.First();
        Check.That(first.Path.EndsWith("0001.png") && first.Width >= 960,
            "der Einzelbildleser erhaelt Framepfad und die bisherige Mindestbreite");
        first.Result.SetResult(h.Picture);
        h.Until(() => h.Pending.Count == 2);
        var last = h.Pending.Last();
        Check.That(last.Path.EndsWith("0003.png") && h.Reads.Count == 2,
            "nach dem laufenden Bild wird nur der letzte wartende Wunsch gelesen");
        var newest = Picture(180);
        last.Result.SetResult(newest);
        h.Until(() => ReferenceEquals(h.Image.Source, newest));
        Check.That(h.Text("StageZoom") == "3 / 3", "Bild und Positionsanzeige erreichen den letzten Wunsch");
        h.BlockReads = false;
        h.FailReads = true;
        h.Call("ShowFrame", 2);
        h.Until(() => h.Reads.Count == 3 && h.Workers == 0);
        h.Pump();
        Check.That(ReferenceEquals(h.Image.Source, newest), "ein gesperrter oder defekter Frame laesst das vorige Bild stehen");
    }

    private static void PreloadAndRange()
    {
        Check.Group("Dashboard-Bilder - Vorladen, Teilfolge und Abbruch");
        using var h = new Harness();
        h.Open();
        h.Until(() => h.Image.Source is not null);
        Check.That((bool)h.Call("NeedsPreload")!, "eine noch nicht gepufferte Folge muss vorladen");
        h.Poke("InPoint", 2);
        h.Poke("OutPoint", 3);
        h.Call("StartPreload");
        var loader = h.Loaders.Single();
        Check.That(loader.Paths.Count == 3 && loader.Paths[0].EndsWith("0001.png"),
            "vorausgeladen wird weiterhin die ganze Folge in Frame-Reihenfolge");
        Check.That(loader.Width >= 480 && loader.Budget == 128L * 1024 * 1024 && loader.Aspect == 1,
            "Breite, Speicherbudget und Seitenverhaeltnis werden unveraendert weitergegeben");
        Check.That(h.Bar.Visibility == Visibility.Visible && h.Text("PreloadCount").StartsWith("0 / 3"),
            "der Ladevorgang zeigt sofort den Ausgangsfortschritt");
        loader.Report(new PreloadProgress(2, 3, PreloadPace.Slow, 1024) { Width = 480, WantedWidth = 960 });
        Check.That(h.Text("PreloadCount").StartsWith("2 / 3") && h.Text("PreloadNote").Length > 0,
            "Teilfortschritt und reduzierte Aufloesung erscheinen im Dashboard");
        loader.Frames = new BitmapSource?[] { null, h.Picture, h.Picture };
        loader.Complete(false);
        h.Until(() => loader.Disposals == 1 && h.Playback.IsPlaying is true);
        h.Call("Pause");
        Check.That(h.Bar.Visibility == Visibility.Collapsed && !(bool)h.Call("NeedsPreload")!,
            "auch eine teilweise geladene Folge startet und deckt einen vollstaendigen Teilbereich ab");
        int reads = h.Reads.Count;
        h.Call("ShowFrame", 3);
        Check.That(h.Reads.Count == reads && ReferenceEquals(h.Image.Source, h.Picture),
            "ein Cache-Treffer wird ohne weiteren Dateizugriff angezeigt");
        h.Poke("InPoint", 1);
        Check.That((bool)h.Call("NeedsPreload")!, "eine Luecke im erweiterten Bereich fordert weiteres Vorladen");
        h.Call("StartPreload");
        var cancelled = h.Loaders.Last();
        h.Call("OnTogglePlay", h.Window, new RoutedEventArgs());
        Check.That(cancelled.Cancels == 1 && h.Bar.Visibility == Visibility.Collapsed,
            "derselbe Abspielknopf bricht einen laufenden Ladevorgang ab");
        cancelled.Complete(true);
        h.Until(() => cancelled.Disposals == 1);
        Check.That(h.Playback.IsPlaying is false, "ein abgebrochener Vorlader startet keine spaete Wiedergabe");
    }

    private static void DefaultPreloader()
    {
        Check.Group("Dashboard-Bilder - echter Vorlader mit eigenen Testbildern");
        using var h = new Harness();
        var reports = new List<PreloadProgress>();
        using var loader = DashboardFrameSources.Default.CreatePreloader(
            new[] { h.PathFor(1), Path.Combine(h.Render, "absent.png"), h.PathFor(3) },
            () => PreloadPace.Medium, reports.Add);
        var run = loader.RunAsync(480, 16L * 1024 * 1024, 1);
        h.Until(() => run.IsCompleted);
        Check.That(!run.GetAwaiter().GetResult() && loader.Loaded == 2 && loader.Frames[1] is null,
            "der bestehende Leser behaelt fehlende Frames als Luecken im Array");
        Check.That(loader.Frames[0]?.IsFrozen == true && loader.Frames[2]?.IsFrozen == true
                   && reports.Last().Loaded == 2, "echte Bitmaps sind eingefroren und der Fortschritt zaehlt gelesene Bilder");
    }

    public static void RetiredWork()
    {
        Check.Group("Dashboard-Bilder - Decoder nach leerer Auswahl und Schliessen");
        using (var h = new Harness(blockReads: true))
        {
            h.Open();
            h.Until(() => h.Pending.Count == 1);
            h.Call("Select", h.Empty);
            h.Pending.First().Result.SetResult(h.Picture);
            h.Until(() => h.Workers == 0);
            h.Pump();
            Check.That(h.Image.Source is null && ((TextBlock)h.Window.FindName("StageEmpty")).Visibility == Visibility.Visible,
                "ein altes Decoder-Ergebnis fuellt eine leere Auswahl nicht erneut");
            h.Call("Select", h.Render);
            h.Until(() => h.Pending.Count == 2);
            var before = h.Image.Source;
            h.Close();
            h.Pending.Last().Result.SetResult(Picture(90));
            h.Until(() => h.Workers == 0);
            h.Pump();
            Check.That(ReferenceEquals(before, h.Image.Source), "ein geschlossenes Fenster erhaelt kein spaetes Decoderbild");
        }

        Check.Group("Dashboard-Bilder - Cache-Treffer vor altem Decoder");
        using (var h = new Harness(blockReads: true))
        {
            h.Open();
            h.Until(() => h.Pending.Count == 1);
            h.Call("StartPreload");
            var loader = h.Loaders.Single();
            var cached = Picture(200);
            loader.Frames = new BitmapSource?[] { cached, cached, cached };
            loader.Complete(true);
            h.Until(() => loader.Disposals == 1);
            h.Call("Pause");
            h.Pending.First().Result.SetResult(h.Picture);
            h.Until(() => h.Workers == 0);
            h.Pump();
            Check.That(ReferenceEquals(h.Image.Source, cached), "ein langsamer Decoder ueberschreibt keinen neueren Cache-Treffer");
            h.Call("Select", h.Empty);
            Check.That(h.Read("_frames") is DashboardFrameController { CachedFrameCount: 0 }, "eine leere Auswahl gibt auch den Bildspeicher frei");
        }

        Check.Group("Dashboard-Bilder - Fortschritt abgeloester Vorlader");
        using (var h = new Harness())
        {
            h.Open();
            h.Until(() => h.Image.Source is not null);
            h.Call("StartPreload");
            var old = h.Loaders.Single();
            Task.Run(() => old.Report(new PreloadProgress(3, 3, PreloadPace.Fast, 100))).GetAwaiter().GetResult();
            h.Call("CancelPreload");
            h.Call("StartPreload");
            var current = h.Loaders.Last();
            old.Complete(true);
            h.Until(() => old.Disposals == 1);
            Check.That(h.Text("PreloadCount").StartsWith("0 / 3"),
                "schon eingereihter alter Fortschritt veraendert den neuen Ladevorgang nicht");
            h.Call("Select", h.Empty);
            Check.That(current.Cancels == 1 && h.Bar.Visibility == Visibility.Collapsed,
                "ein Wechsel zur leeren Ausgabe bricht den Vorlader sofort ab");
            current.Complete(true);
            h.Until(() => current.Disposals == 1);
            Check.That(h.Playback.IsPlaying is false, "die alte Folge startet nach dem Wechsel nicht wieder");
            h.Call("Select", h.Render);
            h.Call("StartPreload");
            var closing = h.Loaders.Last();
            string before = h.Text("PreloadCount");
            h.Close();
            Task.Run(() => closing.Report(new PreloadProgress(3, 3, PreloadPace.Fast, 100))).GetAwaiter().GetResult();
            closing.Complete(false);
            h.Until(() => closing.Disposals == 1);
            Check.That(h.Text("PreloadCount") == before, "nach dem Schliessen bleibt auch spaeter Ladefortschritt wirkungslos");
        }
    }

    internal static BitmapSource Picture(byte value)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { value, 0, 0, 255 }, 4);
        image.Freeze();
        return image;
    }

    internal sealed class Harness : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "frameflip-frames-" + Guid.NewGuid().ToString("N"));
        internal string Render { get; }
        internal string Empty { get; }
        internal MainWindow Window { get; private set; } = null!;
        internal BitmapSource Picture { get; } = DashboardFrameInvariants.Picture(30);
        internal ConcurrentQueue<(string Path, int Width)> Reads { get; } = new();
        internal ConcurrentQueue<ReadJob> Pending { get; } = new();
        internal List<Loader> Loaders { get; } = new();
        internal bool BlockReads, FailReads;
        internal int Workers;
        private bool _closed;
        private readonly string? _previousConfig = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private readonly Func<FrameFlip.Diagnostics.LoadSnapshot?> _load = LivePage.Load;

        internal Harness(bool blockReads = false)
        {
            BlockReads = blockReads;
            Render = Directory.CreateDirectory(Path.Combine(Root, "render")).FullName;
            Empty = Directory.CreateDirectory(Path.Combine(Root, "empty")).FullName;
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(Root, "config.json"));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            LivePage.Load = () => null;
            for (int n = 1; n <= 3; n++)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(Picture));
                using var stream = File.Create(PathFor(n));
                encoder.Save(stream);
            }
            ProjectLibrary.Save(new ProjectKnowledge { Files = new()
            {
                new KnownBlend { Path = Path.Combine(Root, "render.blend"), Output = Render, Seed = PathFor(1) },
                new KnownBlend { Path = Path.Combine(Root, "empty.blend"), Output = Empty },
            }});
        }
        internal string PathFor(int n) => Path.Combine(Render, $"frame_{n:0000}.png");
        internal void Open(DashboardVideoSources? videoSources = null, AppSettings? settings = null,
            Action<AppSettings>? persist = null)
        {
            var sources = new DashboardFrameSources((path, width) =>
            {
                Interlocked.Increment(ref Workers);
                try
                {
                    Reads.Enqueue((path, width));
                    if (FailReads) throw new IOException("synthetic decode failure");
                    if (!BlockReads) return Picture;
                    var job = new ReadJob(path, width);
                    Pending.Enqueue(job);
                    if (!job.Result.Task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic decoder was not released");
                    return job.Result.Task.GetAwaiter().GetResult();
                }
                finally { Interlocked.Decrement(ref Workers); }
            }, (paths, pace, report) =>
            {
                var loader = new Loader(paths, report);
                Loaders.Add(loader);
                return loader;
            });
            Window = new MainWindow(sources, null, () => null, () => { }, _ => { }, () => { },
                settings ?? new AppSettings { Prebuffer = true, PrepareVideo = false, MemoryBudgetMb = 128 },
                persist, videoSources: videoSources);
        }
        internal Image Image => (Image)Window.FindName("StageImage");
        internal FrameworkElement Bar => (FrameworkElement)Window.FindName("PreloadBar");
        internal string Text(string name) => ((TextBlock)Window.FindName(name)).Text;
        internal object? Read(string name) => DashboardSelectionInvariants.Read(Window, name);
        internal DashboardPlaybackController Playback => DashboardPlaybackInvariants.Playback(Window);
        internal void Poke(string property, object value) => DashboardPlaybackInvariants.Poke(Window, property, value);
        internal void Set(string name, object value) => typeof(MainWindow).GetField(name, Hidden)!.SetValue(Window, value);
        internal object? Call(string name, params object[] args) => typeof(MainWindow).GetMethods(Hidden)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length
                && m.GetParameters().Select((p, i) => p.ParameterType.IsInstanceOfType(args[i])).All(x => x)).Invoke(Window, args);
        internal void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        internal void Until(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(6)) { Pump(); Thread.Sleep(1); }
            if (!ready()) throw new TimeoutException("Dashboard frame work did not finish");
        }
        internal void Close() { if (!_closed && Window is not null) { _closed = true; Window.Close(); } }
        public void Dispose()
        {
            Close();
            BlockReads = false;
            foreach (var job in Pending) job.Result.TrySetResult(null);
            foreach (var loader in Loaders) loader.Complete(false);
            try
            {
                Until(() => Workers == 0 && Loaders.All(l => l.Disposals > 0));
                Pump();
                bool removed = false;
                Until(() =>
                {
                    if (removed) return true;
                    try { Directory.Delete(Root, true); removed = true; return true; }
                    catch (IOException) { return false; }
                });
            }
            finally
            {
                LivePage.Load = _load;
                Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", _previousConfig);
                SynchronizationContext.SetSynchronizationContext(_context);
            }
        }
    }

    internal sealed record ReadJob(string Path, int Width)
    {
        internal TaskCompletionSource<BitmapSource?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class Loader(IReadOnlyList<string> paths, Action<PreloadProgress> report) : IDashboardPreloader
    {
        internal IReadOnlyList<string> Paths => paths;
        internal int Width, Cancels, Disposals;
        internal long Budget;
        internal double Aspect;
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BitmapSource?[] Frames { get; set; } = new BitmapSource?[paths.Count];
        public int Loaded => Frames.Count(f => f is not null);
        public Task<bool> RunAsync(int width, long budget, double aspect) { Width = width; Budget = budget; Aspect = aspect; return _done.Task; }
        internal void Report(PreloadProgress progress) => report(progress);
        internal void Complete(bool whole) => _done.TrySetResult(whole);
        public void Cancel() => Cancels++;
        public void Dispose() => Disposals++;
    }
}
