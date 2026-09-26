using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// "Nur dieses Bild" (docs/Atelier-Werkzeugplan.md, Entscheidung 7): Ein Bild einer Folge wird
/// zu einem eigenen Einzelbild - ein Ordner mit seinem Namen im Quellordner, darin eine Kopie
/// des Originals und ein Projekt, das mit dem Stand der Folge beginnt. Nichts wird
/// ueberschrieben, und der Weg zurueck in die Folge bleibt.
/// </summary>
public static class FrameDetachInvariants
{
    public static void Run()
    {
        TheFrameMovesOut();
        ThePageSwitches();
    }

    private static void Png(string path, byte shade)
    {
        const int W = 32, H = 20;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { pixels[i * 4] = shade; pixels[i * 4 + 1] = (byte)(i % W * 6); pixels[i * 4 + 2] = 90; pixels[i * 4 + 3] = 255; }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static string Folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-einzelbild-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        for (int n = 1; n <= 3; n++) Png(Path.Combine(folder, $"render_{n:0000}.png"), (byte)(40 * n));
        return folder;
    }

    private static void TheFrameMovesOut()
    {
        Check.Group("Nur dieses Bild: herausloesen");

        string folder = Folder();
        string frame = Path.Combine(folder, "render_0002.png");

        try
        {
            var store = new AtelierProjectStore(() => Path.Combine(folder, "ausweich"));
            var sequence = SequenceKey.Of(frame)!;

            store.Save(sequence, new AtelierProject { Adjustments = new ImageAdjustments { Exposure = 0.7 } });
            store.SaveSide(sequence, "verlauf/maske-alle.json", new byte[] { 1, 2, 3 });

            string copy = FrameDetach.Detach(frame, store.Load(sequence)!, store);

            Check.That(copy == Path.Combine(folder, "render_0002", "render_0002.png") && File.Exists(copy),
                       "ein Ordner mit dem Namen des Bildes im Quellordner, darin die Kopie", copy);
            Check.That(File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(frame)), "die Kopie ist das Original, Byte fuer Byte");
            Check.That(!Directory.EnumerateFiles(Path.GetDirectoryName(copy)!, "*.kopie").Any(), "keine halbe Kopie bleibt liegen");

            var project = store.Load(SequenceKey.Of(copy)!);
            Check.That(project?.Adjustments?.Exposure == 0.7 && project.Origin == Path.GetFullPath(frame) &&
                       store.PlaceOf(SequenceKey.Of(copy)!)!.StartsWith(Path.Combine(folder, "render_0002", "FrameFlip"), StringComparison.OrdinalIgnoreCase),
                       "sein Projekt liegt im neuen Ordner, beginnt mit dem Stand der Folge und kennt seine Herkunft");
            Check.That(store.LoadSide(SequenceKey.Of(copy)!, "verlauf/maske-alle.json")?.SequenceEqual(new byte[] { 1, 2, 3 }) == true,
                       "und der Maskenverlauf der Folge kommt mit");

            // Ein zweites Mal: Was schon da ist, bleibt, wie es ist.
            File.WriteAllBytes(copy, new byte[] { 9 });
            string again = FrameDetach.Detach(frame, new AtelierProject(), store);
            Check.That(again == copy && File.ReadAllBytes(copy).SequenceEqual(new byte[] { 9 }),
                       "gibt es das Einzelbild schon, wird es nicht ueberschrieben");

            Check.That(File.Exists(frame) && store.Load(sequence)?.Origin is null, "die Folge bleibt, wie sie war");
        }
        finally
        {
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static void ThePageSwitches()
    {
        Check.Group("Nur dieses Bild: auf der Seite");

        string folder = Folder();
        string frame = Path.Combine(folder, "render_0002.png");
        string lone = Path.Combine(folder, "allein", "bild.png");
        Directory.CreateDirectory(Path.GetDirectoryName(lone)!);
        Png(lone, 200);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var band = (ToolBand)page.FindName("ToolBand");

        void Pump(Func<bool> until, double seconds = 10)
        {
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < end && !until())
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(5);
            }
        }

        void Show(string path)
        {
            page.Open(path);
            Pump(() => page.Projects.Current is { } key && key.Equals(SequenceKey.Of(path)) &&
                       ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.2);
        }

        try
        {
            window.Show();

            Show(lone);
            Check.That(!band.FrameSwitchShown, "ein gewoehnliches Einzelbild: kein Umschalter - nichts zu unterscheiden");

            Show(frame);
            Check.That(band.FrameSwitchShown && !band.SingleFrame, "ein Bild einer Folge: der Umschalter steht auf der ganzen Folge");

            var exposure = (Slider)((GradingPanel)page.FindName("Tools")).FindName("ExposureSlider");
            exposure.Value = 0.8;
            Pump(() => false, 0.2);

            var switching = page.UseFrameMode(single: true);
            Pump(() => switching.IsCompleted);
            string copy = FrameDetach.CopyFor(frame);
            Pump(() => page.Projects.Current is { } key && key.Equals(SequenceKey.Of(copy)) &&
                       ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.2);

            Check.That(switching.IsCompleted && switching.Result && File.Exists(copy), "nur dieses Bild: die Kopie entsteht und oeffnet sich");
            Check.That(band.FrameSwitchShown && band.SingleFrame && page.Projects.Origin == Path.GetFullPath(frame),
                       "der Umschalter steht jetzt auf dem Bild, das Einzelbild kennt seine Herkunft");
            Check.Near(exposure.Value, 0.8, 1e-6, "und es beginnt mit dem Stand der Folge");

            // Etwas nur hier aendern - die Folge merkt davon nichts.
            exposure.Value = -0.5;
            Pump(() => false, 0.2);

            var back = page.UseFrameMode(single: false);
            Pump(() => back.IsCompleted);
            Pump(() => page.Projects.Current is { } key && key.Equals(SequenceKey.Of(frame)));
            Pump(() => false, 0.2);

            Check.That(back.Result && !band.SingleFrame && band.FrameSwitchShown, "zurueck in die Folge, auf das Bild, aus dem es kam");
            Check.Near(exposure.Value, 0.8, 1e-6, "die Folge hat ihren eigenen Stand behalten");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
