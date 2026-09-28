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
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Farbspeicher und Farbraeder (docs/Atelier-Arbeitsablauf.md, C4b): Die Pipette merkt sich
/// Farben ueber das Bild hinaus, die Toenung uebernimmt eine, Lift, Gamma und Gain machen eine
/// Stelle grau - mit der Rechnung, die auch das Bild macht.
/// </summary>
public static class ColourStoreInvariants
{
    public static void Run()
    {
        StoreAndSolve();
        ThePageUsesThem();
    }

    private static void StoreAndSolve()
    {
        Check.Group("Farbspeicher und Farbraeder: Rechnung");

        var store = new List<SavedColour>();
        ColourStore.Add(store, Colour(10, "a.png"));
        ColourStore.Add(store, Colour(20, "a.png"));
        bool again = ColourStore.Add(store, Colour(20, "a.png"));
        ColourStore.Add(store, Colour(20, "b.png"));
        Check.That(store.Count == 3 && !again && store[0].From == "b.png" && store[2].ShownR == 10,
                   "die neueste vorn, dieselbe Farbe aus derselben Datei nicht zweimal hintereinander");

        for (int i = 0; i < 20; i++) ColourStore.Add(store, Colour((byte)(30 + i), "c.png"));
        Check.That(store.Count == ColourStore.Capacity && store[0].ShownR == 49, "aeltere fallen hinten heraus");

        // Die Toenung uebernimmt eine Farbe: dieselben Verhaeltnisse der Kanaele, dieselbe Helligkeit.
        var point = ColourSolve.TintOf(0.6f, 0.45f, 0.3f, 0.5f);
        var (tr, tg, tb) = ColourWheelMath.ToChannels(point, 0f, 1f, 0.5f);
        Check.That(MathF.Abs(tr / tb - 0.6f / 0.3f) < 0.01f && MathF.Abs(tg / tb - 0.45f / 0.3f) < 0.01f &&
                   MathF.Abs((tr + tg + tb) / 3f - 1f) < 0.001f,
                   "Toenung aus einer Farbe: ihre Richtung, ihre Kraft, keine Helligkeit", $"{tr:0.000} {tg:0.000} {tb:0.000}");
        Check.That(ColourSolve.TintOf(0.4f, 0.4f, 0.4f, 0.5f).IsCentre, "Grau gibt keine Toenung");

        // Neutralisieren: dieselbe Rechnung wie das Bild, jede Zone, die Helligkeit bleibt.
        foreach (var (zone, scale, r, g, b) in new[]
                 {
                     (ZoneKind.Gain, 0.25f, 0.82f, 0.74f, 0.66f),
                     (ZoneKind.Lift, 0.12f, 0.10f, 0.12f, 0.16f),
                     (ZoneKind.Gamma, 0.35f, 0.50f, 0.46f, 0.40f),
                 })
        {
            var tool = new LiftGammaGainTool();
            var wheel = ColourSolve.Neutralise(tool, zone, r, g, b, scale);
            float neutral = zone == ZoneKind.Lift ? 0f : 1f;
            var (cr, cg, cb) = ColourWheelMath.ToChannels(wheel, 0f, neutral, scale);
            var triplet = new ColourTriplet(cr, cg, cb);

            if (zone == ZoneKind.Lift) tool.Lift = triplet;
            else if (zone == ZoneKind.Gamma) tool.Gamma = triplet;
            else tool.Gain = triplet;

            tool.Prepare();
            float or = r, og = g, ob = b;
            tool.Apply(ref or, ref og, ref ob);

            Check.That(Spread(or, og, ob) < 0.004f && Spread(r, g, b) > 0.05f &&
                       MathF.Abs(ColourWheelMath.ToBrightness(cr, cg, cb, neutral)) < 0.001f,
                       $"{zone}: die Stelle kommt grau heraus, die Helligkeit der Zone bleibt",
                       $"{or:0.000} {og:0.000} {ob:0.000}");
        }

        // Weiter, als das Rad reicht: so nah wie moeglich, am Rand.
        var strong = new LiftGammaGainTool();
        var edge = ColourSolve.Neutralise(strong, ZoneKind.Gain, 0.9f, 0.3f, 0.2f, 0.25f);
        var (sr, sg, sb) = ColourWheelMath.ToChannels(edge, 0f, 1f, 0.25f);
        strong.Gain = new ColourTriplet(sr, sg, sb);
        strong.Prepare();
        float xr = 0.9f, xg = 0.3f, xb = 0.2f;
        strong.Apply(ref xr, ref xg, ref xb);
        Check.That(edge.Radius > 0.98f && Spread(xr, xg, xb) < Spread(0.9f, 0.3f, 0.2f),
                   "zu kraeftig fuer das Rad: der Rand, und naeher an Grau als vorher");
    }

    private static void ThePageUsesThem()
    {
        Check.Group("Farbspeicher und Farbraeder: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-colours-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string warm = Picture(Path.Combine(root, "a", "warm_0001.png"), 210, 180, 140);
        string cool = Picture(Path.Combine(root, "b", "cool_0001.png"), 120, 150, 200);

        var settings = new AppSettings();
        int saved = 0;
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => saved++);
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var properties = (PropertiesPanel)page.FindName("Properties");
        var tools = (GradingPanel)page.FindName("Tools");
        var layers = (LayerPanel)page.FindName("Layers");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        try
        {
            window.Show();
            Show(page, warm);

            // Die Pipette liest - und merkt.
            page.HandleToolKey(Key.I);
            typeof(AtelierPage).GetMethod("ReadAt", flags)!.Invoke(page, new object[] { 5, 5 });
            typeof(AtelierPage).GetMethod("ReadAt", flags)!.Invoke(page, new object[] { 6, 5 });
            Check.That(settings.AtelierColours.Count == 1 && settings.AtelierColours[0].From == "warm_0001.png" &&
                       properties.StoredShown == 1 && saved > 0,
                       "ein Klick der Pipette merkt die Farbe - zweimal dieselbe nur einmal - und sie wird gespeichert",
                       $"{settings.AtelierColours.Count}");

            // Gain neutralisiert die warme Stelle.
            var zones = (LiftGammaGainTool)typeof(GradingPanel).GetField("_zones", flags)!.GetValue(tools)!;
            var gainPick = (ToggleButton)tools.FindName("GainPick");
            gainPick.IsChecked = true;
            Check.That(page.ColourPicking && Loupe(page)?.Contains(Localization.Strings.T("S_ZoneGain")) == true,
                       "die Pipette am Rad der Lichter wartet, und die Lupe sagt, wofuer");

            var input = ((float R, float G, float B)?)typeof(AtelierPage).GetMethod("LevelsInputAt", flags)!
                .Invoke(page, new object[] { zones, 5, 5 });
            Check.That(page.ColourPickAt(5, 5) && !page.ColourPicking && gainPick.IsChecked != true,
                       "ein Klick ins Bild: eingestellt, die Pipette ist fertig, der Knopf aus");

            var (ir, ig, ib) = input!.Value;
            zones.Prepare();
            zones.Apply(ref ir, ref ig, ref ib);
            Check.That(Spread(ir, ig, ib) < 0.01f && !zones.Gain.Near(1f),
                       "die Lichter sind verstellt, und die Stelle kommt grau aus Lift, Gamma und Gain", $"{ir:0.000} {ig:0.000} {ib:0.000}");

            // Die Toenung uebernimmt die Farbe aus dem Bild.
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);
            var layer = layers.Selection!;
            var tintPick = (ToggleButton)layers.FindName("TintPick");
            tintPick.IsChecked = true;
            Check.That(page.ColourPicking && Loupe(page)?.Contains(layer.Name) == true, "die Pipette an der Toenung wartet");

            var stored = settings.AtelierColours[0];
            Check.That(page.ColourPickAt(5, 5) && Ratio(layer.Tint.R, layer.Tint.B, stored.R, stored.B) &&
                       Ratio(layer.Tint.G, layer.Tint.B, stored.G, stored.B),
                       "ein Klick: die Toenung hat die Verhaeltnisse der Farbe an der Stelle",
                       $"{layer.Tint.R:0.000} {layer.Tint.G:0.000} {layer.Tint.B:0.000}");

            // Ein anderes Bild: Der Speicher bleibt, und seine Farbe wird dort die Toenung.
            Show(page, cool);
            Check.That(properties.StoredShown == settings.AtelierColours.Count && settings.AtelierColours.Count > 0,
                       "ein anderes Bild: der Farbspeicher ist noch da");

            var coolLayer = layers.Selection!;
            ((ToggleButton)layers.FindName("TintPick")).IsChecked = true;
            Check.That(page.TakeStoredColour(stored) && Ratio(coolLayer.Tint.R, coolLayer.Tint.B, stored.R, stored.B) && !page.ColourPicking,
                       "ein Feld aus dem Speicher: die Farbe des warmen Bildes wird die Toenung im kuehlen");

            // Vergessen mit Rechtsklick.
            var store = (Panel)properties.FindName("PickStore");
            int before = settings.AtelierColours.Count;
            ((UIElement)store.Children[0]).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            {
                RoutedEvent = UIElement.MouseRightButtonUpEvent,
            });
            Check.That(settings.AtelierColours.Count == before - 1 && properties.StoredShown == before - 1,
                       "Rechtsklick auf ein Feld vergisst die Farbe");
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

    private static string? Loupe(AtelierPage page)
    {
        page.PipetteAt(5, 5, new System.Windows.Point(60, 60));
        return page.LoupeText;
    }

    private static SavedColour Colour(byte shade, string from) => new() { ShownR = shade, ShownG = shade, ShownB = shade, From = from };

    private static float Spread(float r, float g, float b) => MathF.Max(r, MathF.Max(g, b)) - MathF.Min(r, MathF.Min(g, b));

    private static bool Ratio(float a, float b, float c, float d) => MathF.Abs(a / b - c / d) < 0.02f * (c / d);

    private static string Picture(string path, byte r, byte g, byte b)
    {
        const int W = 40, H = 20;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Show(AtelierPage page, string path)
    {
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("FileText")).Text == Path.GetFileName(path) &&
                   ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
        Pump(() => false, 0.4);
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
