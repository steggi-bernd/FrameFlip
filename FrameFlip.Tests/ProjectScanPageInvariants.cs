using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Projects;
using FrameFlip.Views;

namespace FrameFlip.Tests;

public static class ProjectScanPageInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Run()
    {
        Check.Group("Projektseite - Ordnerinhalt und Bildreihenfolge");
        string root = Path.Combine(Path.GetTempPath(), "frameflip-scan-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            string render = Directory.CreateDirectory(Path.Combine(root, "render")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            string first = Path.Combine(root, "frame_0001.png");
            string second = Path.Combine(root, "frame_0002.png");
            WritePng(second);
            WritePng(first);
            WritePng(Path.Combine(render, "last.png"));
            File.WriteAllText(Path.Combine(root, "ignore.txt"), "not an image");
            var project = new BlendProject("scan", "Scan", root, Array.Empty<BlendVersion>());
            var page = new ProjectsPage(_ => { }, () => new List<BlendProject> { project }, () => new());
            PumpUntil(() => Field<int>(page, "_generation") > 0);
            Open(page, project);
            FinishContent(page);
            var panels = ((Panel)page.FindName("Body")).Children.OfType<WrapPanel>().ToArray();
            Check.That(panels.Length == 2, "Ordner und Frames stehen in getrennten Kachelreihen");
            var folders = panels[0].Children.OfType<Border>().Select(b => (string)b.ToolTip).ToArray();
            Check.That(folders.SequenceEqual(new[] { render, Path.Combine(root, "empty") }),
                       "Ordner mit Bildern stehen vor leeren Ordnern");
            var frames = panels[1].Children.OfType<Border>().Select(b => (string)b.ToolTip).ToArray();
            Check.That(frames.SequenceEqual(new[] { first, second }), "nur Bilder erscheinen, in derselben Frame-Reihenfolge");
            Click(panels[0].Children.OfType<Border>().Last());
            FinishContent(page);
            Check.That(((Panel)page.FindName("Body")).Children.OfType<TextBlock>().Any(),
                       "ein leerer Ordner zeigt eine Rueckmeldung");
            Check.That(page.Back(), "nach dem Lesen eines Ordners funktioniert Zurueck weiter");
            FinishContent(page);
        }
        finally
        {
            // Noch laufende Vorschaudekodierer koennen Dateien kurz geoeffnet
            // halten. Das Testmaterial erst nach deren Freigabe entfernen.
            try
            {
                bool removed = false;
                PumpUntil(() =>
                {
                    if (removed) return true;
                    try { Directory.Delete(root, recursive: true); removed = true; return true; }
                    catch (IOException) { return false; }
                });
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
    }

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Open(ProjectsPage page, BlendProject project)
        => Click((Border)typeof(ProjectsPage).GetMethod("ProjectTile", Hidden)!.Invoke(page, new object?[] { project, null })!);
    private static void FinishContent(ProjectsPage page)
    {
        var task = Field<Task>(page, "_contentTask");
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
    private static void Click(Border target)
        => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    private static T Field<T>(ProjectsPage page, string name)
        => (T)typeof(ProjectsPage).GetField(name, Hidden)!.GetValue(page)!;

    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!ready()) throw new TimeoutException("Der Projekt-Scan wurde nicht angewendet.");
    }
}
