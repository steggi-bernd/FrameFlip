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

using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Weissabgleich per Pipette (docs/Atelier-Arbeitsablauf.md, C4c): eine Stelle grau machen, oder
/// sie an eine gemerkte Farbe angleichen - aus einem anderen Bild, und an einer Ebene nur dort.
/// </summary>
public static class WhiteBalancePickInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        Solves();
        ThePageMatches();
    }

    private static void Solves()
    {
        Check.Group("Weissabgleich per Pipette: Rechnung");

        static (float, float, float) Same((float R, float G, float B) c) => c;

        var (kelvin, tint) = ColourSolve.WhiteBalance(0.6f, 0.5f, 0.38f, Same, null);
        var (r, g, b) = Apply(kelvin, tint, 0.6f, 0.5f, 0.38f);
        Check.That(Chroma(r, g, b, (1f, 1f, 1f)) < 0.01f && kelvin < 6500f,
                   "eine warme Stelle wird grau - der Wert nennt warmes Licht, die Korrektur kuehlt", $"{kelvin:0} K, {tint:0.0}");

        var (nk, nt) = ColourSolve.WhiteBalance(0.4f, 0.4f, 0.4f, Same, null);
        Check.That(MathF.Abs(nk - 6500f) < 60f && MathF.Abs(nt) < 1f, "was schon grau ist, bleibt fast in Grundstellung", $"{nk:0} K, {nt:0.0}");

        var warm = (0.55f, 0.5f, 0.42f);
        var (mk, mt) = ColourSolve.WhiteBalance(0.5f, 0.5f, 0.5f, Same, warm);
        var (mr, mg, mb) = Apply(mk, mt, 0.5f, 0.5f, 0.5f);
        Check.That(Chroma(mr, mg, mb, warm) < 0.01f, "angleichen: Grau bekommt die Farbart der Vorlage", $"{mk:0} K, {mt:0.0}");

        var (xk, xt) = ColourSolve.WhiteBalance(0.9f, 0.3f, 0.05f, Same, null);
        Check.That(xk is >= 1999f and <= 15000f && xt is >= -100f and <= 100f,
                   "auch bei einer Farbe, die sich nicht ganz neutral machen laesst: innerhalb der Regler", $"{xk:0} K, {xt:0.0}");
    }

    private static void ThePageMatches()
    {
        Check.Group("Weissabgleich per Pipette: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-wb-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (string folder in new[] { "a", "b", "c", "d" }) Directory.CreateDirectory(Path.Combine(root, folder));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string warm = Picture(Path.Combine(root, "a", "warm_0001.png"), 205, 175, 140);
        string cool = Picture(Path.Combine(root, "b", "cool_0001.png"), 150, 165, 200);
        string local = Picture(Path.Combine(root, "c", "local_0001.png"), 205, 175, 140);
        string masked = Picture(Path.Combine(root, "d", "masked_0001.png"), 205, 175, 140);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var layers = (LayerPanel)page.FindName("Layers");
        var neutral = (ToggleButton)tools.FindName("WbPickNeutral");
        var match = (ToggleButton)tools.FindName("WbPickMatch");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        (byte R, byte G, byte B) Shown(int x, int y)
            => ((byte, byte, byte))typeof(AtelierPage).GetMethod("ShownAt", flags)!.Invoke(page, new object[] { x, y })!;

        try
        {
            window.Show();

            // Neutral: die warme Flaeche wird grau.
            Show(page, warm);
            neutral.IsChecked = true;
            Check.That(page.ColourPicking && Loupe(page) == T("S_PickForWbNeutral"), "die Pipette Neutral wartet, und die Lupe sagt, wofuer");

            var before = Shown(5, 5);
            Check.That(page.ColourPickAt(5, 5) && !page.ColourPicking && neutral.IsChecked != true,
                       "ein Klick: eingestellt, die Pipette ist fertig, der Knopf aus");
            Pump(() => false, 0.6);

            var after = Shown(5, 5);
            Check.That(Spread(after) <= 4 && Spread(before) > 40,
                       "die Stelle ist im angezeigten Bild grau", $"vorher {before}, nachher {after}");

            // Angleichen: Das kuehle Bild gibt die Vorlage, das warme uebernimmt sie.
            Show(page, cool);
            page.HandleToolKey(Key.I);
            typeof(AtelierPage).GetMethod("ReadAt", flags)!.Invoke(page, new object[] { 5, 5 });
            var reference = settings.AtelierColours[0];

            Show(page, local);
            match.IsChecked = true;
            Check.That(Loupe(page)?.Contains(reference.Hex) == true, "Angleichen wartet, an die zuletzt gemerkte Farbe", Loupe(page));

            // Ein Feld im Speicher wird die Vorlage - nicht die Toenung.
            var other = new SavedColour { ShownR = 90, ShownG = 90, ShownB = 90, R = 0.1f, G = 0.1f, B = 0.1f, From = "grau.png" };
            Check.That(page.TakeStoredColour(other) && Loupe(page)?.Contains(other.Hex) == true && page.ColourPicking,
                       "ein Feld aus dem Speicher wird die Vorlage, die Pipette wartet weiter");
            page.TakeStoredColour(reference);

            Check.That(page.ColourPickAt(5, 5), "ein Klick gleicht an");
            Pump(() => false, 0.6);

            var matched = Shown(5, 5);
            Check.That(Chroma(matched.R, matched.G, matched.B, (reference.ShownR, reference.ShownG, reference.ShownB)) < 0.03f,
                       "die Stelle hat jetzt die Farbe der Stelle im anderen Bild, wie angezeigt",
                       $"{matched} gegen {reference.ShownR},{reference.ShownG},{reference.ShownB}");

            // Stellenweise: An einer Einstellungsebene geht der Weissabgleich in die Ebene.
            Show(page, masked);
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);
            layers.AddAdjustment();
            Pump(() => false, 0.3);
            var layer = layers.Selection!;
            var toolsLayer = (ImageLayer?)typeof(AtelierPage).GetProperty("ToolsLayer", flags)!.GetValue(page);

            Check.That(ReferenceEquals(toolsLayer, layer), "(Vorbedingung: der Farbstreifen zeigt die Einstellungsebene)");

            neutral.IsChecked = true;
            page.ColourPickAt(5, 5);
            Pump(() => false, 0.4);

            var layerBalance = layer.Tools?.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();
            var recipe = typeof(AtelierPage).GetField("_recipe", flags)!.GetValue(page)!;
            var grading = (GradingStack?)recipe.GetType().GetProperty("Grading")!.GetValue(recipe);
            var wholeBalance = grading?.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();

            Check.That(layerBalance is { IsNeutral: false } && wholeBalance is null or { IsNeutral: true },
                       "an einer Einstellungsebene: der Weissabgleich steht in der Ebene, nicht im ganzen Bild - mit ihrer Maske also nur dort",
                       layerBalance is null ? "keiner" : $"{layerBalance.Kelvin:0} K");
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

    private static (float, float, float) Apply(float kelvin, float tint, float r, float g, float b)
    {
        var tool = new WhiteBalanceTool { Kelvin = kelvin, Tint = tint };
        tool.Prepare();
        tool.Apply(ref r, ref g, ref b);
        return (r, g, b);
    }

    private static float Chroma(float r, float g, float b, (float R, float G, float B) goal)
    {
        float s = r + g + b, t = goal.R + goal.G + goal.B;
        return MathF.Abs(r / s - goal.R / t) + MathF.Abs(g / s - goal.G / t) + MathF.Abs(b / s - goal.B / t);
    }

    private static int Spread((byte R, byte G, byte B) c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    private static string? Loupe(AtelierPage page)
    {
        page.PipetteAt(5, 5, new Point(60, 60));
        return page.LoupeText?.Split(" | ", 2)[1];
    }

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
