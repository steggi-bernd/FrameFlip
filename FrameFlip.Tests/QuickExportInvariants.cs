using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Der Schnell-Export (docs/Projekte-und-Masken.md, Punkt 6): in den Ordner FrameFlip neben
/// den Bildern, unter der naechsten freien Nummer, nie ueberschreibend - ein Einzelbild als
/// Bild, eine Sequenz als Ordner. Alles unter %TEMP%.
/// </summary>
public static class QuickExportInvariants
{
    public static void Run()
    {
        TheNamesNeverRepeat();
        KeepingContext(ThePageExportsQuickly);
    }

    /// <summary>
    /// Laesst eine Probe laufen und stellt danach den Synchronisationskontext wieder her.
    /// Der Testaufbau des Dashboards und der Schnell-Export setzen den des
    /// Oberflaechenfadens; stehen gelassen, landeten die Fortschrittsmeldungen spaeterer
    /// Gruppen in einer Warteschlange, die niemand abarbeitet.
    /// </summary>
    private static void KeepingContext(Action probe)
    {
        var previous = SynchronizationContext.Current;

        try { probe(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void TheNamesNeverRepeat()
    {
        Check.Group("Schnell-Export: Namen und Nummern");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-schnell-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            string first = QuickExport.Claim(folder, "render", ".png", asFolder: false);
            string second = QuickExport.Claim(folder, "render", ".png", asFolder: false);

            Check.That(first == "render_FrameFlip_001" && second == "render_FrameFlip_002" &&
                       File.Exists(Path.Combine(folder, "render_FrameFlip_001.png")),
                       "fortlaufend nummeriert, jeder Name gleich belegt", $"{first}, {second}");

            // Belegt ist ein Name auch durch einen Ordner oder eine Datei anderer Endung.
            Directory.CreateDirectory(Path.Combine(folder, "render_FrameFlip_003"));
            File.WriteAllText(Path.Combine(folder, "render_FrameFlip_004.mp4"), "");

            Check.That(QuickExport.Claim(folder, "render", ".png", asFolder: false) == "render_FrameFlip_005",
                       "ein vorhandener Ordner oder ein Video mit der Nummer zaehlt als belegt");
            Check.That(QuickExport.Claim(folder, "render", "", asFolder: true) == "render_FrameFlip_006" &&
                       Directory.Exists(Path.Combine(folder, "render_FrameFlip_006")),
                       "eine Sequenz belegt ihren Ordner");

            var key = SequenceKey.Of(Path.Combine(folder, "render_0001.exr"))!;
            Check.That(QuickExport.FolderFor(key, null) == Path.Combine(key.Folder, "FrameFlip") &&
                       QuickExport.FolderFor(key, @"D:\ziel") == @"D:\ziel",
                       "ohne gewaehltes Ziel der Ordner FrameFlip neben den Bildern, sonst das Ziel");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static void ThePageExportsQuickly()
    {
        Check.Group("Schnell-Export: auf der Seite");

        // Der Fortschritt eines Laufs kommt ueber den Synchronisationskontext zurueck - in der
        // App der des Oberflaechenfadens, in der Probe muss er gesetzt sein.
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());

        string root = Path.Combine(Path.GetTempPath(), "frameflip-schnellseite-" + Guid.NewGuid().ToString("N")[..8]);
        string single = Path.Combine(root, "einzeln", "bild.png");
        string[] sequence = Enumerable.Range(1, 3).Select(n => Path.Combine(root, "folge", $"render_{n:0000}.png")).ToArray();

        foreach (string file in sequence.Append(single))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            WritePng(file);
        }

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        void Show(string path)
        {
            page.Open(path);
            Pump(() => ((Button)page.FindName("QuickExportButton")).IsEnabled &&
                       page.Projects.Current is { } key && key.Equals(SequenceKey.Of(path)));
        }

        void Export()
        {
            var task = page.QuickExportNow();
            Pump(() => task.IsCompleted, 20);
        }

        try
        {
            window.Show();

            // Keine Sequenz: ein einzelnes Bild, zweimal - zwei Dateien, keine ueberschrieben.
            Show(single);
            Export();
            Export();

            string target = Path.Combine(root, "einzeln", "FrameFlip");
            var images = Directory.GetFiles(target, "*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray();

            Check.That(images.SequenceEqual(new[] { "bild_FrameFlip_001.png", "bild_FrameFlip_002.png" }),
                       "ein Einzelbild zweimal: bild_FrameFlip_001 und _002 im Ordner FrameFlip", string.Join(", ", images));
            Check.That(images.All(n => new FileInfo(Path.Combine(target, n!)).Length > 100) && ReadsAsImage(Path.Combine(target, images[0]!)),
                       "beide sind fertige Bilder, keine leeren Platzhalter");
            Check.That(page.LastQuickExport == Path.Combine(target, "bild_FrameFlip_002.png"),
                       "und die Seite weiss, wohin es ging");

            // Eine Sequenz: ein eigener Ordner mit allen Bildern unter ihren Namen.
            Show(sequence[1]);
            Export();

            string folder = Path.Combine(root, "folge", "FrameFlip", "render_FrameFlip_001");
            var frames = Directory.Exists(folder) ? Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n).ToArray() : Array.Empty<string?>();

            Check.That(frames.SequenceEqual(new[] { "render_0001.png", "render_0002.png", "render_0003.png" }),
                       "eine Sequenz: der Ordner render_FrameFlip_001 mit allen drei Bildern", string.Join(", ", frames));
            Check.That(sequence.All(File.Exists) && Directory.GetFiles(Path.Combine(root, "folge"), "*.png").Length == 3,
                       "die Quellen bleiben unberuehrt");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static bool ReadsAsImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth == 40;
        }
        catch (Exception) { return false; }
    }

    private static void WritePng(string path)
    {
        const int W = 40, H = 24;
        var pixels = Enumerable.Range(0, W * H * 4).Select(i => (byte)(i % 4 == 3 ? 255 : i % 200)).ToArray();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }
}
