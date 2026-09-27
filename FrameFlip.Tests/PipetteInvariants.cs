using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Die Pipette (docs/Atelier-Arbeitsablauf.md, C4): eine Lupe mit Farbe und Werten, gelesen aus
/// dem Ergebnis oder der Quelle, und ein Klick, der einem Farbbereich den Farbton gibt.
/// </summary>
public static class PipetteInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        ColoursAreNamed();
        ThePageReadsAndGives();
    }

    private static void ColoursAreNamed()
    {
        Check.Group("Pipette: HEX, HSV, Farbton");

        Check.That(ColourReadout.Hex(255, 0, 128) == "#FF0080", "HEX in Grossbuchstaben mit Raute");
        Check.That(ColourReadout.Hsv(255, 0, 0) == (0, 100, 100) && ColourReadout.Hsv(0, 0, 255) == (240, 100, 100) &&
                   ColourReadout.Hsv(128, 128, 128) is (0, 0, 50), "HSV: Rot, Blau, Grau");
        Check.That(ColourReadout.Hue(0.2f, 0.2f, 0.2f) is null && ColourReadout.Hue(0f, 1f, 0f) is 120f, "Grau hat keinen Farbton, Gruen 120 Grad");
        Check.That(ColourReadout.Widen(0f, 30f, 240f) == 125f && ColourReadout.Widen(0f, 30f, 10f) == 30f && ColourReadout.Widen(0f, 30f, 180f) == 180f,
                   "Erweitern: so weit, dass die Farbe dazugehoert - nie schmaler, nie ueber den halben Kreis");
    }

    private static void ThePageReadsAndGives()
    {
        Check.Group("Pipette: Lupe, Ergebnis oder Quelle, Farbbereich");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-pipette-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 40, H = 20;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            bool red = i % W < W / 2;
            pixels[i * 4] = red ? (byte)0 : (byte)128;
            pixels[i * 4 + 1] = red ? (byte)0 : (byte)128;
            pixels[i * 4 + 2] = red ? (byte)255 : (byte)128;
            pixels[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var properties = (PropertiesPanel)page.FindName("Properties");
        var fromSource = (ToggleButton)properties.FindName("PickFromSource");
        var exposure = (Slider)((GradingPanel)page.FindName("Tools")).FindName("ExposureSlider");
        var layers = (LayerPanel)page.FindName("Layers");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            page.HandleToolKey(Key.I);
            Check.That(page.PipetteAt(5, 5, new Point(60, 60)) && page.LoupeText == "#FF0000 | " + T("S_PickReadOnly"),
                       "ueber dem Rot: die Lupe zeigt #FF0000 und dass die Pipette nur abliest", page.LoupeText ?? "keine Lupe");

            exposure.Value = 1;
            Pump(() => false, 0.5);
            page.PipetteAt(30, 5, new Point(60, 60));
            string shown = page.LoupeText!.Split(' ')[0];

            fromSource.IsChecked = true;
            page.PipetteAt(30, 5, new Point(60, 60));
            string source = page.LoupeText!.Split(' ')[0];
            fromSource.IsChecked = false;

            Check.That(source == "#808080" && shown != source, "nach Belichtung +1: das Ergebnis ist heller, die Quelle bleibt #808080",
                       $"Ergebnis {shown}, Quelle {source}");

            // Ein Farbbereich im Stapel: der Klick setzt den Farbton, Umschalt erweitert.
            exposure.Value = 0;
            layers.AddAdjustment();
            layers.Selection!.Mask.Kind = MaskKind.Colour;
            layers.Selection.Mask.Hue = 200f;
            layers.Selection.Name = "Himmel";

            page.PipetteAt(5, 5, new Point(60, 60));
            Check.That(page.LoupeText!.EndsWith(T("S_PickForColourRange", "Himmel"), StringComparison.Ordinal),
                       "an einem Farbbereich: die Lupe sagt, wofuer sie liest", page.LoupeText);

            typeof(AtelierPage).GetMethod("ReadAt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { 5, 5 });
            Check.That(Math.Abs(layers.Selection.Mask.Hue) < 0.5f && ((TextBlock)properties.FindName("PickHex")).Text == "#FF0000",
                       "ein Klick aufs Rot: der Farbbereich sucht jetzt Rot, und die Leiste nennt #FF0000", $"{layers.Selection.Mask.Hue}");

            page.PickColourRange(0f, 0f, 1f, widen: true);
            Check.That(Math.Abs(layers.Selection.Mask.Hue) < 0.5f && Math.Abs(layers.Selection.Mask.Spread - 125f) < 0.5f,
                       "mit Umschalt auf Blau: der Farbton bleibt, die Breite reicht bis Blau", $"{layers.Selection.Mask.Spread}");

            // Im Knotenmodus: dieselbe Maske als Knoten, mit Rueckgaengig.
            page.HandleToolKey(Key.V);
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            var editor = (NodeEditor)page.FindName("NodeView");
            var mask = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Colour);
            editor.Select(mask);
            page.HandleToolKey(Key.I);

            Check.That(page.PickColourRange(0f, 1f, 0f, widen: false) && Math.Abs(mask.Mask.Hue - 120f) < 0.5f,
                       "im Knotenmodus: der Farbbereichsknoten bekommt den Farbton");

            page.StepNodes(back: true);
            var restored = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Colour);
            Check.That(Math.Abs(restored.Mask.Hue) < 0.5f, "und Rueckgaengig nimmt ihn zurueck");

            page.HandleToolKey(Key.V);
            Check.That(page.LoupeText is null, "ein anderes Werkzeug: keine Lupe");
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
