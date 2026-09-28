using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Sichthilfen und der Kurvenpunkt aus dem Bild (docs/Atelier-Werkzeugplan.md, W2c): Clipping und
/// Falschfarben nur in der Anzeige, die Pipette liest darunter, Strg+Klick setzt einen Kurvenpunkt.
/// </summary>
public static class ViewAidInvariants
{
    public static void Run()
    {
        Zones();
        ThePageShowsThem();
    }

    private static void Zones()
    {
        Check.Group("Sichthilfen: Zonen und Grenzen");

        Check.That(ViewAids.ZoneOf(0.01f).Key == "S_FalseColourCrushed" && ViewAids.ZoneOf(0.05f).Key == "S_FalseColourShadows" &&
                   ViewAids.ZoneOf(0.45f).Key == "S_FalseColourMiddle" && ViewAids.ZoneOf(0.55f).Key == "S_FalseColourSkin" &&
                   ViewAids.ZoneOf(0.95f).Key == "S_FalseColourHighlights" && ViewAids.ZoneOf(0.99f).Key == "S_FalseColourBlown" &&
                   ViewAids.ZoneOf(0.3f).Grey && ViewAids.ZoneOf(0.7f).Grey,
                   "Falschfarben: abgesoffen, Tiefen, Mittelgrau, Haut, Lichter, ausgefressen - dazwischen grau");

        Check.That(ViewAids.ClippingAt(255, 10, 10, 0.005f, 0.995f) is (235, 20, 20) &&
                   ViewAids.ClippingAt(0, 0, 0, 0.005f, 0.995f) is (20, 70, 255) &&
                   ViewAids.ClippingAt(128, 128, 128, 0.005f, 0.995f) is null &&
                   ViewAids.ClippingAt(20, 20, 20, 0.1f, 0.9f) is (20, 70, 255) &&
                   ViewAids.ClippingAt(20, 20, 200, 0.1f, 0.9f) is null,
                   "Clipping: ein Kanal oben reicht fuer rot, fuer blau muessen alle unten sein");

        var pixels = new byte[] { 100, 100, 100, 255 };
        unsafe
        {
            fixed (byte* p = pixels) ViewAids.Apply((IntPtr)p, 1, 1, 4, ViewAid.FalseColour, 0f, 1f);
        }
        Check.That(pixels[0] == pixels[1] && pixels[1] == pixels[2] && pixels[2] == 100, "in einer grauen Zone bleibt die Helligkeit stehen");
    }

    private static void ThePageShowsThem()
    {
        Check.Group("Sichthilfen: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-viewaid-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        // Ein Verlauf von Schwarz nach Weiss, 256 Punkte breit: jeder Punkt ist sein eigener Wert.
        const int W = 256, H = 8;
        string path = Path.Combine(root, "render_0001.png");
        var source = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            byte v = (byte)(i % W);
            source[i * 4] = v; source[i * 4 + 1] = v; source[i * 4 + 2] = v; source[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, source, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        (byte R, byte G, byte B) Screen(int x)
        {
            var surface = (WriteableBitmap)typeof(AtelierPage).GetField("_surface", flags)!.GetValue(page)!;
            var pixel = new byte[4];
            surface.CopyPixels(new Int32Rect(x, 1, 1, 1), pixel, 4, 0);
            return (pixel[2], pixel[1], pixel[0]);
        }

        (byte R, byte G, byte B) Read(int x)
            => ((byte, byte, byte))typeof(AtelierPage).GetMethod("ShownAt", flags)!.Invoke(page, new object[] { x, 1 })!;

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            var plainMid = Screen(128);

            page.SetViewAid(ViewAid.Clipping);
            Check.That(Screen(0) is (20, 70, 255) && Screen(255) is (235, 20, 20) && Screen(128) == plainMid,
                       "Clipping: das schwarze Ende blau, das weisse rot, die Mitte wie sie ist", $"{Screen(0)} {Screen(255)}");
            Check.That(Read(0) is (0, 0, 0) && Read(255) is (255, 255, 255), "die Pipette liest das Bild unter der Sichthilfe");
            Check.That(((FrameworkElement)page.FindName("ClipRow")).Visibility == Visibility.Visible, "in der Statuszeile stehen die Grenzen");

            page.SetClipping(0.2f, 0.8f);
            Check.That(Screen(40) is (20, 70, 255) && Screen(220) is (235, 20, 20) && Screen(128) == plainMid,
                       "engere Grenzen: auch 40 und 220 gelten als abgeschnitten");

            page.SetViewAid(ViewAid.FalseColour);
            int middle = Enumerable.Range(0, W).First(x => ViewAids.ZoneOf(ViewAids.Luma(Read(x).R, Read(x).G, Read(x).B)).Key == "S_FalseColourMiddle");
            Check.That(Screen(middle) is (20, 190, 40) && Screen(250) is (235, 20, 20) &&
                       ((FrameworkElement)page.FindName("FalseColourRow")).Visibility == Visibility.Visible,
                       "Falschfarben: Mittelgrau gruen, das Ende rot, darunter die Legende", $"Mittelgrau bei {middle}");

            Check.That(page.HandleToolKey(Key.J) && page.ViewAidShown == ViewAid.None && Screen(0) is (0, 0, 0),
                       "J: weiter zu Normal - das Bild ohne alles");

            // Strg+Klick: ohne Kurvenkarte nichts, mit ihr ein Punkt beim Ton der Stelle.
            Check.That(!page.CurvePointAt(64, 1), "ohne sichtbare Kurvenkarte setzt Strg+Klick keinen Punkt");

            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Curves"));
            Pump(() => tools.CurvesShown);
            Pump(() => false, 0.3);

            var tone = Read(64);
            float expected = ViewAids.Luma(tone.R, tone.G, tone.B);
            Check.That(page.CurvePointAt(64, 1) && tools.Curves.Master.Points.Count == 3 &&
                       MathF.Abs(tools.Curves.Master.Points[1].X - expected) < 0.01f &&
                       MathF.Abs(tools.Curves.Master.Points[1].Y - expected) < 0.01f,
                       "Strg+Klick: ein Punkt auf der Gesamtkurve beim Ton der Stelle - auf der Kurve, sie aendert sich nicht",
                       string.Join(" ", tools.Curves.Master.Points.Select(p => $"({p.X:0.00},{p.Y:0.00})")));

            page.CurvePointAt(64, 1);
            Check.That(tools.Curves.Master.Points.Count == 3, "dieselbe Stelle noch einmal: kein zweiter Punkt");

            // Im Knotenmodus misst das Histogramm das fertige Bild auf der Anzeige - auch dort das
            // Bild unter der Sichthilfe. Und ein beim Malen neu gerechneter Ausschnitt bekommt sie auch.
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);

            var histogram = (HistogramView)tools.FindName("Histogram");
            var measure = typeof(AtelierPage).GetMethod("Measure", flags, Type.EmptyTypes)!;
            int[] Luma() => ((int[])typeof(HistogramView).GetField("_luma", flags)!.GetValue(histogram)!).ToArray();

            measure.Invoke(page, null);
            var plainLuma = Luma();

            page.SetViewAid(ViewAid.Clipping);
            measure.Invoke(page, null);
            Check.That(Screen(0) is (20, 70, 255) && Luma().SequenceEqual(plainLuma),
                       "Knotenmodus: das Histogramm misst das Bild unter der Sichthilfe", $"{Screen(0)}");

            // Ein Ausschnitt geht nur mit gewaehltem Knoten - wie beim Malen auf einer neuen Maske.
            typeof(AtelierPage).GetMethod("MakeNodeMask", flags)!.Invoke(page, null);
            Pump(() => false, 0.6);

            typeof(AtelierPage).GetMethod("Refresh", flags, new[] { typeof(bool), typeof(bool) })!.Invoke(page, new object[] { false, false });
            var adorner = (PlacementAdorner)page.FindName("Placement");
            typeof(PlacementAdorner).GetField("_pendingTouched", flags)!.SetValue(adorner, new PaintBounds(0, 0, 20, H));
            bool region = (bool)typeof(AtelierPage).GetMethod("PaintRegion", flags)!.Invoke(page, null)! &&
                          (bool)typeof(AtelierPage).GetField("_regionPainted", flags)!.GetValue(page)!;
            Check.That(region && Screen(0) is (20, 70, 255) && Read(0) is (0, 0, 0),
                       "ein beim Malen neu gerechneter Ausschnitt traegt die Sichthilfe, die Pipette liest darunter",
                       $"Ausschnitt={region} {Screen(0)} {Read(0)}");
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
