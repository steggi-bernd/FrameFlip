using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Namen der Maske (docs/Atelier-Masken-Konzept.md, M4): "Deckungsschleier" statt
/// "Freistellung", "wirkt auf: Korrektur / Sichtbarkeit", "gilt für: alle Bilder / dieses Bild".
/// </summary>
public static class MaskNameInvariants
{
    public static void Run()
    {
        Check.Group("Maske: die Namen");

        Check.That(Strings.T("S_MatteGroup") == "Deckungsschleier" && Strings.T("S_MaskScopeLabel") == "wirkt auf:" &&
                   Strings.T("S_MaskScopeColour") == "Korrektur" && Strings.T("S_MaskScopeShow") == "Sichtbarkeit" &&
                   Strings.T("S_MaskLockLabel") == "gilt für:" && Strings.T("S_MaskLockAll") == "alle Bilder" &&
                   Strings.T("S_MaskLockOne") == "dieses Bild",
                   "Deckungsschleier, wirkt auf: Korrektur / Sichtbarkeit, gilt fuer: alle Bilder / dieses Bild");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-masknames-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 64, H = 32;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 120; pixels[i + 2] = 160; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);

            var mask = layers.Stack.Layers[0].Mask;
            mask.Kind = MaskKind.Painted;
            mask.PaintLocked = true;
            layers.ShowThumbnails();
            Pump(() => false, 0.2);

            var row = (FrameworkElement)layers.FindName("MaskLockRow");
            var all = (ToggleButton)layers.FindName("MaskLockButton");
            var one = (ToggleButton)layers.FindName("MaskLooseButton");

            Check.That(row.Visibility == Visibility.Visible && all.IsChecked == true && one.IsChecked != true,
                       "gemalte Maske: \"gilt fuer\" mit beiden Moeglichkeiten, \"alle Bilder\" gewaehlt");

            one.IsChecked = true;
            Pump(() => false, 0.1);
            Check.That(!layers.Stack.Layers[0].Mask.PaintLocked && all.IsChecked != true,
                       "\"dieses Bild\": je Bild ein eigener Anstrich");

            all.IsChecked = true;
            Pump(() => false, 0.1);
            Check.That(layers.Stack.Layers[0].Mask.PaintLocked && one.IsChecked != true,
                       "\"alle Bilder\": wieder ein Anstrich fuer die ganze Folge");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
