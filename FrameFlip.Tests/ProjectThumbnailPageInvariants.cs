using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Views;
using FrameFlip.Projects;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace FrameFlip.Tests;

/// <summary>Zuerst gegen das Thumbnail-Laden im Code-behind ausgefuehrt.</summary>
public static class ProjectThumbnailPageInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Run()
    {
        Check.Group("Projekt-Thumbnails - Darstellung, Dateifreigabe und Wiederverwendung");
        string root = Path.Combine(Path.GetTempPath(), "frameflip-thumbnails-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = SynchronizationContext.Current;
        var context = new ThumbnailContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var page = new ProjectsPage(_ => { }, () => new(), () => new());
            FinishScan(page, "_libraryTask");
            FinishScan(page, "_contentTask");
            string path = Path.Combine(root, "frame.png");
            WritePng(path);
            var first = Load(page, path, 8, context);
            var brush = (ImageBrush)first.Fill;
            var bitmap = (BitmapImage)brush.ImageSource;
            Check.That(brush.Stretch == Stretch.UniformToFill, "das Vorschaubild fuellt die Kachel proportional");
            Check.That(bitmap.PixelWidth == 8 && bitmap.PixelHeight == 4, "Dekodierung erhaelt Seitenverhaeltnis und gewuenschte Breite");
            Check.That(bitmap.IsFrozen, "das dekodierte Bild kann den Worker-Thread verlassen");
            Check.That(bitmap.CacheOption == BitmapCacheOption.OnLoad
                       && bitmap.CreateOptions == BitmapCreateOptions.IgnoreColorProfile,
                       "Datei wird vollstaendig eingelesen und Farbprofil wie bisher ignoriert");
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check.That(true, "nach dem Dekodieren ist die Quelldatei exklusiv beschreibbar");

            var small = (BitmapSource)((ImageBrush)Load(page, path, 4, context).Fill).ImageSource;
            Check.That(small.PixelWidth == 4 && !ReferenceEquals(small, bitmap), "andere Vorschaugroesse erhaelt ein eigenes Cache-Bild");
            File.Delete(path);
            Check.That(ReferenceEquals(((ImageBrush)Load(page, path, 8, context).Fill).ImageSource, bitmap),
                       "erneutes Anzeigen derselben Groesse verwendet das Bild auch ohne Quelldatei");
            var otherPage = new ProjectsPage(_ => { }, () => new(), () => new());
            FinishScan(otherPage, "_libraryTask");
            FinishScan(otherPage, "_contentTask");
            Check.That(ReferenceEquals(((ImageBrush)Load(otherPage, path, 8, context).Fill).ImageSource, bitmap),
                       "Projektseiten teilen sich bereits geladene Vorschaubilder");

            string missing = Path.Combine(root, "later.png");
            Check.That(Load(page, missing, 8, context).Fill == Brushes.Transparent, "fehlende Datei behaelt den Kachelhintergrund");
            WritePng(missing);
            Check.That(Load(page, missing, 8, context).Fill is ImageBrush, "fehlgeschlagene Dekodierung verhindert einen spaeteren Versuch nicht");
            string broken = Path.Combine(root, "broken.png");
            File.WriteAllText(broken, "not a PNG");
            Check.That(Load(page, broken, 8, context).Fill == Brushes.Transparent, "defektes Bild behaelt den Hintergrund ohne Fehlermeldung");
            using (File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check.That(true, "auch nach defektem Bild ist die Datei sofort exklusiv beschreibbar");
            WritePng(broken);
            Check.That(Load(page, broken, 8, context).Fill is ImageBrush, "ein repariertes Bild wird beim naechsten Versuch geladen");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            Directory.Delete(root, recursive: true);
        }
        Superseded(cancelBeforeDecode: true);
        Superseded(cancelBeforeDecode: false);
        WithoutContext();
    }

    private static void Superseded(bool cancelBeforeDecode)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var posted = new ManualResetEventSlim();
        var image = ProjectThumbnailServiceInvariants.Bitmap();
        int reads = 0;
        var service = new ProjectThumbnailService((_, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1) { started.Set(); ProjectThumbnailServiceInvariants.Wait(release); }
            return image;
        });
        var page = new ProjectsPage(_ => { }, new ProjectScanService(() => new(), () => new()), service);
        FinishScan(page, "_libraryTask");
        FinishScan(page, "_contentTask");
        var previous = SynchronizationContext.Current;
        var context = new ThumbnailContext();
        SynchronizationContext.SetSynchronizationContext(context);
        DispatcherHookEventHandler onPosted = (_, _) => posted.Set();
        try
        {
            var target = new Rectangle { Fill = Brushes.Transparent };
            page.Dispatcher.Hooks.OperationPosted += onPosted;
            typeof(ProjectsPage).GetMethod("LoadThumb", Hidden)!.Invoke(page, new object[] { target, "old", 320 });
            ProjectThumbnailServiceInvariants.Wait(started);
            if (!cancelBeforeDecode)
            {
                release.Set();
                // Die Rueckgabe liegt jetzt im Dispatcher, aber die Seite hat
                // sie noch nicht gezeichnet. Genau da kann Navigation eintreffen.
                ProjectThumbnailServiceInvariants.Wait(posted);
            }
            typeof(ProjectsPage).GetMethod("Render", Hidden)!.Invoke(page, null);
            release.Set();
            PumpUntil(() => context.Active == 0);
            FinishScan(page, "_contentTask");
            Check.That(target.Fill == Brushes.Transparent, cancelBeforeDecode
                ? "Ansichtswechsel verwirft ein noch dekodierendes Vorschaubild"
                : "Ansichtswechsel verwirft auch schon im Dispatcher wartende Bilder");
            Check.That(Load(page, "old", 320, context).Fill is ImageBrush,
                       "die aktuelle Ansicht kann dasselbe Bild anschliessend anzeigen");
            if (cancelBeforeDecode) Check.That(reads == 2, "das ueberholte Worker-Ergebnis liegt nicht im Cache");
        }
        finally
        {
            release.Set();
            PumpUntil(() => context.Active == 0);
            page.Dispatcher.Hooks.OperationPosted -= onPosted;
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void WithoutContext()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var image = ProjectThumbnailServiceInvariants.Bitmap();
        var service = new ProjectThumbnailService((_, _) =>
        {
            started.Set();
            ProjectThumbnailServiceInvariants.Wait(release);
            return image;
        });
        var page = new ProjectsPage(_ => { }, new ProjectScanService(() => new(), () => new()), service);
        FinishScan(page, "_libraryTask");
        FinishScan(page, "_contentTask");
        var target = new Rectangle { Fill = Brushes.Transparent };
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            typeof(ProjectsPage).GetMethod("LoadThumb", Hidden)!.Invoke(page, new object[] { target, "cold", 320 });
            ProjectThumbnailServiceInvariants.Wait(started);
            release.Set();
            PumpUntil(() => target.Fill is ImageBrush);
            Check.That(ReferenceEquals(((ImageBrush)target.Fill).ImageSource, image), "Worker-Bild erreicht den UI-Thread auch ohne SynchronizationContext");
            var cached = new Rectangle();
            typeof(ProjectsPage).GetMethod("LoadThumb", Hidden)!.Invoke(page, new object[] { cached, "cold", 320 });
            Check.That(cached.Fill is ImageBrush, "Cache-Treffer zeichnet ohne einen weiteren Dispatcher-Durchlauf");
        }
        finally { release.Set(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static Rectangle Load(ProjectsPage page, string path, int width, ThumbnailContext context)
    {
        var target = new Rectangle { Fill = Brushes.Transparent };
        typeof(ProjectsPage).GetMethod("LoadThumb", Hidden)!.Invoke(page, new object[] { target, path, width });
        PumpUntil(() => context.Active == 0);
        return target;
    }

    private static void FinishScan(ProjectsPage page, string name)
    {
        var task = (Task)typeof(ProjectsPage).GetField(name, Hidden)!.GetValue(page)!;
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 8 * 4], 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!ready()) throw new TimeoutException("Thumbnail wurde nicht angewendet.");
    }

    private sealed class ThumbnailContext : SynchronizationContext
    {
        private readonly DispatcherSynchronizationContext _dispatcher = new();
        private int _active;
        internal int Active => Volatile.Read(ref _active);
        public override void OperationStarted() => Interlocked.Increment(ref _active);
        public override void OperationCompleted() => Interlocked.Decrement(ref _active);
        public override void Post(SendOrPostCallback callback, object? state) => _dispatcher.Post(callback, state);
    }
}
