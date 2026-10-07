using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Das Fenster von Pixel Sort als Bereichsregler und die Verteilungen unter den Skalen
/// (docs/Atelier-Arbeitsablauf.md, C7b).
/// </summary>
public static class SortWindowInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        HardWindowAndDistribution();
        ThePageShowsThem();
    }

    private static void HardWindowAndDistribution()
    {
        Check.Group("Fenster ohne Kanten und Verteilung: Rechnung");

        var scale = RangeScale.Window;
        var window = new RangeWindow(0.3f, 0.7f, 0f, 0f);

        Check.That(RangeWindows.HandleAt(window, 0.3f, 0.02f, alt: true, scale) == RangeHandle.Low &&
                   RangeWindows.HandleAt(window, 0.7f, 0.02f, alt: false, scale) == RangeHandle.High,
                   "ein hartes Fenster hat nur die beiden inneren Griffe - auch mit Alt");

        var dragged = RangeWindows.Drag(window, RangeHandle.Low, 0.1f, alone: true, scale);
        Check.That(MathF.Abs(dragged.Low - 0.4f) < 0.001f && dragged.SoftLow == 0f, "und es bekommt beim Ziehen keine Kante");

        var bins = RangeWindows.Distribution(new[] { (0.1f, 1f), (0.1f, 1f), (0.1f, 1f), (0.9f, 1f) }, RangeScale.Unit, 10)!;
        Check.That(bins[1] == 1f && MathF.Abs(bins[9] - 1f / 3f) < 0.001f && bins[5] == 0f,
                   "die Verteilung zaehlt die Werte in Faecher, auf das hoechste bezogen");

        var hue = RangeWindows.Distribution(new[] { (370f, 1f), (10f, 0.5f) }, RangeScale.Hue, 36)!;
        Check.That(hue[1] == 1f, "auf dem Farbkreis: 370 Grad sind 10 Grad, und ein Gewicht zaehlt mit");
        Check.That(RangeWindows.Distribution(Array.Empty<(float, float)>(), RangeScale.Unit) is null, "ohne Werte keine Verteilung");

        Check.That(MathF.Abs(SortTool.KeyOf(SortKey.Brightness, 255, 0, 0) - 0.2126f) < 0.001f &&
                   MathF.Abs(SortTool.KeyOf(SortKey.Hue, 0, 0, 255) - 240f / 360f) < 0.01f &&
                   SortTool.KeyOf(SortKey.Saturation, 128, 128, 128) == 0f,
                   "der Sortierwert fuer sich - derselbe, nach dem sortiert wird");
    }

    private static void ThePageShowsThem()
    {
        Check.Group("Fenster ohne Kanten und Verteilung: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-sortwin-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        // Links dunkel, rechts hell: zwei Berge in jeder Verteilung.
        const int W = 40, H = 20;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            byte v = i % W < W / 2 ? (byte)40 : (byte)220;
            pixels[i * 4] = v;
            pixels[i * 4 + 1] = v;
            pixels[i * 4 + 2] = v;
            pixels[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var layers = (LayerPanel)page.FindName("Layers");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            // Pixel Sort aus der Werkzeugleiste: die Karte mit offenem Fenster (A5).
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Sort"));
            Pump(() => false, 0.8);

            var sort = (SortTool)typeof(GradingPanel).GetField("_sort", flags)!.GetValue(tools)!;
            var range = (RangeSlider)tools.FindName("SortRange");
            var low = (Slider)tools.FindName("SortLowSlider");

            Check.That(tools.SortShown && MathF.Abs(range.Window.Low - sort.Low) < 0.001f && MathF.Abs(range.Window.High - sort.High) < 0.001f &&
                       range.Window.SoftLow == 0f && range.Scale.HasEdges == false,
                       "die Karte von Pixel Sort: das Fenster als Regler ohne Kanten, mit den Werten der beiden Regler",
                       $"{range.Window} / {sort.Low:0.00}-{sort.High:0.00}");

            var bins = range.Distribution;
            int filled = bins?.Count(v => v > 0.01f) ?? 0;
            Check.That(bins is not null && filled is >= 2 and <= 6 && bins.Max() == 1f,
                       "darunter die Verteilung des Sortierwerts: zwei Berge, dunkel und hell", $"{filled} Faecher");

            range.Measure(new Size(212, 32));
            range.Arrange(new Rect(0, 0, 212, 32));
            double x = range.ScreenOf(RangeHandle.Low);
            float before = sort.Low;
            range.Grab(x, alt: false);
            range.MoveTo(x + 20);
            range.Release();
            Check.That(MathF.Abs(sort.Low - (before + 0.1f)) < 0.002f && Math.Abs(low.Value - sort.Low) < 0.002,
                       "ein Zug am Fenster: Pixel Sort und der Regler Von folgen", $"{sort.Low:0.000} / {low.Value:0.000}");

            typeof(GradingPanel).GetMethod("OnSortRangeChanged", flags)!.Invoke(tools, new object[] { new RangeWindow(0f, 0f, 0f, 0f), false });
            Check.That(sort.Low == 0f && sort.High == 0f && sort.IsNeutral, "Doppelklick schliesst das Fenster - Pixel Sort ruht");

            ((ComboBox)tools.FindName("SortKeyBox")).SelectedIndex = (int)SortKey.Hue;
            Check.That(range.Track == RangeTrack.Hue, "nach Farbton sortiert: unter dem Fenster liegt der Farbkreis");

            // Die Helligkeitsmaske: dieselben zwei Berge unter ihrem Bereichsregler.
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);
            layers.AddAdjustment();
            var box = (ComboBox)layers.FindName("MaskBox");
            box.SelectedIndex = box.Items.IndexOf(T("S_MaskLuminance"));
            Pump(() => false, 0.3);

            var maskBins = ((RangeSlider)layers.FindName("MaskRange")).Distribution;
            int maskFilled = maskBins?.Count(v => v > 0.01f) ?? 0;
            Check.That(maskBins is not null && maskFilled is >= 2 and <= 6,
                       "unter der Helligkeitsmaske: die Verteilung der Helligkeit im Bild", $"{maskFilled} Faecher");
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
