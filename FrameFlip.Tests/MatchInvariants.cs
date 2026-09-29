using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// Farbe angleichen und Deflicker (docs/Atelier-Werkzeugplan.md, W2f).
/// </summary>
public static class MatchInvariants
{
    public static void Run()
    {
        Matching();
        Smoothing();
        Keeping();
        ThePageMatches();
        ThePageDeflickers();
    }

    /// <summary>Eine Stichprobe um eine Farbe, mit Streuung in Helligkeit und Farbe.</summary>
    private static float[] Around(float r, float g, float b, float spread, int count = 2000)
    {
        var random = new Random(11);
        var rgb = new float[count * 3];

        for (int i = 0; i < count; i++)
        {
            float light = (float)(random.NextDouble() - 0.5) * spread;
            float tint = (float)(random.NextDouble() - 0.5) * spread * 0.5f;

            rgb[i * 3] = r + light + tint;
            rgb[i * 3 + 1] = g + light;
            rgb[i * 3 + 2] = b + light - tint;
        }

        return rgb;
    }

    private static float[] Through(MatchTool tool, float[] rgb)
    {
        tool.Prepare();
        var result = (float[])rgb.Clone();

        for (int i = 0; i < result.Length; i += 3)
            tool.Apply(ref result[i], ref result[i + 1], ref result[i + 2]);

        return result;
    }

    private static void Matching()
    {
        Check.Group("Farbe angleichen: die Rechnung");

        var (y, cb, cr) = MatchTool.ToYcc(0.7f, 0.4f, 0.2f);
        var (r, g, b) = MatchTool.FromYcc(y, cb, cr);
        Check.That(MathF.Abs(r - 0.7f) < 1e-5f && MathF.Abs(g - 0.4f) < 1e-5f && MathF.Abs(b - 0.2f) < 1e-5f,
                   "Helligkeit und Farbdifferenzen hin und zurueck: dieselbe Farbe");

        var warm = Around(0.65f, 0.45f, 0.30f, 0.2f);
        var cool = Around(0.35f, 0.45f, 0.60f, 0.1f);
        var reference = ColourStats.Measure(warm);
        var source = ColourStats.Measure(cool);

        var tool = new MatchTool { Reference = reference, Source = source, Amount = 1f };
        var matched = ColourStats.Measure(Through(tool, cool));

        Check.That(MathF.Abs(matched.MeanCb - reference.MeanCb) < 0.005f && MathF.Abs(matched.MeanCr - reference.MeanCr) < 0.005f &&
                   MathF.Abs(matched.SpreadCr - reference.SpreadCr) < 0.005f,
                   "das kuehle Bild bekommt Farbe und Farbstreuung des warmen Vorbilds",
                   $"Cr {source.MeanCr:0.000} -> {matched.MeanCr:0.000}, Vorbild {reference.MeanCr:0.000}");

        Check.That(MathF.Abs(matched.MeanY - source.MeanY) < 0.005f && MathF.Abs(matched.SpreadY - source.SpreadY) < 0.005f,
                   "ohne \"Helligkeit mit\" bleiben Helligkeit und Kontrast, wie sie waren");

        tool.Tone = true;
        var toned = ColourStats.Measure(Through(tool, cool));
        Check.That(MathF.Abs(toned.MeanY - reference.MeanY) < 0.005f && MathF.Abs(toned.SpreadY - reference.SpreadY) < 0.01f,
                   "mit \"Helligkeit mit\": auch Helligkeit und Kontrast des Vorbilds");

        var half = new MatchTool { Reference = reference, Source = source, Amount = 0.5f };
        var halfway = ColourStats.Measure(Through(half, cool));
        Check.That(MathF.Abs(halfway.MeanCr - (source.MeanCr + reference.MeanCr) / 2) < 0.005f, "halbe Staerke: auf halbem Weg");

        Check.That(new MatchTool { Reference = reference, Amount = 1f }.IsNeutral &&
                   new MatchTool { Source = source, Amount = 1f }.IsNeutral &&
                   new MatchTool { Reference = reference, Source = source }.IsNeutral,
                   "ohne Vorbild, ohne Messung dieses Bildes oder ohne Staerke rechnet nichts");

        // Ein graues Bild hat keine Farbstreuung - gestreckt wird dann nichts, verschoben schon.
        var grey = Around(0.5f, 0.5f, 0.5f, 0f, 10);
        var tinted = Through(new MatchTool { Reference = reference, Source = ColourStats.Measure(grey), Amount = 1f }, grey);
        Check.That(tinted.All(float.IsFinite) && tinted[0] > tinted[2], "ein graues Bild wird warm, ohne dass etwas explodiert");
    }

    private static void Smoothing()
    {
        Check.Group("Deflicker: die Rechnung");

        var steady = Enumerable.Range(1, 20).ToDictionary(n => n, _ => 0.4f);
        Check.That(DeflickerTool.Gains(steady, 9, 1f).Values.All(g => MathF.Abs(g - 1f) < 1e-5f),
                   "eine ruhige Folge: kein Faktor");

        var fade = Enumerable.Range(1, 30).ToDictionary(n => n, n => 0.1f * MathF.Pow(1.08f, n));
        Check.That(DeflickerTool.Gains(fade, 9, 1f).Values.All(g => MathF.Abs(g - 1f) < 1e-4f),
                   "eine gewollte Blende - gleichmaessig im Logarithmus - bleibt unberuehrt, auch an den Enden");

        var flicker = Enumerable.Range(1, 40).ToDictionary(n => n, n => 0.4f * (n % 2 == 0 ? 1.2f : 1f) * (n == 17 ? 1.3f : 1f));
        var gains = DeflickerTool.Gains(flicker, 9, 1f);
        double Spread(IEnumerable<double> values)
        {
            var list = values.ToList();
            double mean = list.Average();
            return Math.Sqrt(list.Average(v => (v - mean) * (v - mean)));
        }

        double before = Spread(flicker.Values.Select(v => Math.Log(v)));
        double after = Spread(flicker.Select(p => Math.Log(p.Value * gains[p.Key])));
        Check.That(after < before * 0.25, "Flackern um 20 % und ein Sprung: danach weniger als ein Viertel der Streuung",
                   $"{before:0.000} -> {after:0.000}");

        var none = DeflickerTool.Gains(flicker, 9, 0f);
        Check.That(none.Values.All(g => g == 1f), "Staerke null: kein Faktor");

        var tool = new DeflickerTool { Levels = flicker, Amount = 1f };
        tool.Prepare();
        float r = 0.5f, g = 0.5f, b = 0.5f;
        tool.Apply(new OpticsPlace(10, 10, 2), 0, 0, ref r, ref g, ref b);
        float r2 = 0.5f, g2 = 0.5f, b2 = 0.5f;
        tool.Apply(new OpticsPlace(10, 10, 500), 0, 0, ref r2, ref g2, ref b2);
        Check.That(MathF.Abs(r - 0.5f * gains[2]) < 1e-6f && r2 == 0.5f,
                   "je Bildnummer ihr Faktor - ein Bild ausserhalb der Messung bleibt, wie es ist");

        Check.That(new DeflickerTool { Amount = 1f }.IsNeutral &&
                   new DeflickerTool { Amount = 1f, Levels = new() { [1] = 0.5f } }.IsNeutral &&
                   new DeflickerTool { Levels = flicker }.IsNeutral,
                   "ohne Messung, mit einem einzigen Bild oder ohne Staerke rechnet nichts");
    }

    private static void Keeping()
    {
        Check.Group("Farbe angleichen und Deflicker: speichern und kopieren");

        var stack = new GradingStack();
        stack.Tools.Add(new MatchTool
        {
            Reference = new ColourStats(0.5f, -0.05f, 0.08f, 0.2f, 0.03f, 0.04f),
            ReferenceName = "vorbild.png",
            Source = new ColourStats(0.4f, 0.04f, -0.02f, 0.1f, 0.02f, 0.02f),
            Amount = 0.7f,
            Tone = true,
        });
        stack.Optics.Add(new DeflickerTool { Levels = new() { [1] = 0.4f, [2] = 0.5f }, Amount = 0.9f, Window = 13 });

        var back = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack))!;
        var match = back.Tools.OfType<MatchTool>().Single();
        var deflicker = back.Optics.OfType<DeflickerTool>().Single();

        Check.That(match.Reference == stack.Tools.OfType<MatchTool>().Single().Reference && match.ReferenceName == "vorbild.png" &&
                   match.Source!.MeanCr == -0.02f && match.Amount == 0.7f && match.Tone &&
                   deflicker.Levels![2] == 0.5f && deflicker.Amount == 0.9f && deflicker.Window == 13,
                   "gespeichert und geladen: Vorbild, Messung, Staerke, Glaettung");

        var copy = stack.Clone();
        copy.Optics.OfType<DeflickerTool>().Single().Levels![1] = 9f;
        copy.Tools.OfType<MatchTool>().Single().Amount = 0f;

        Check.That(stack.Optics.OfType<DeflickerTool>().Single().Levels![1] == 0.4f &&
                   stack.Tools.OfType<MatchTool>().Single().Amount == 0.7f,
                   "eine Kopie ist unabhaengig - auch die Messung der Folge");
    }

    /// <summary>Schreibt eine Folge einfarbiger Bilder - je Bild eine Farbe.</summary>
    private static void Sequence(string folder, params (byte R, byte G, byte B)[] colours)
    {
        const int W = 64, H = 32;

        for (int k = 0; k < colours.Length; k++)
        {
            var pixels = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                // Ein leiser Verlauf, damit es etwas zu streuen gibt.
                int shade = i % W / 8;
                pixels[i * 4] = (byte)Math.Clamp(colours[k].B + shade, 0, 255);
                pixels[i * 4 + 1] = (byte)Math.Clamp(colours[k].G + shade, 0, 255);
                pixels[i * 4 + 2] = (byte)Math.Clamp(colours[k].R + shade, 0, 255);
                pixels[i * 4 + 3] = 255;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
            using var file = File.Create(Path.Combine(folder, $"render_{k + 1:0000}.png"));
            encoder.Save(file);
        }
    }

    private static (double R, double G, double B) Shown(AtelierPage page)
    {
        var surface = (WriteableBitmap)typeof(AtelierPage).GetField("_surface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
        int w = surface.PixelWidth, h = surface.PixelHeight;
        var pixels = new byte[w * h * 4];
        surface.CopyPixels(pixels, w * 4, 0);

        double r = 0, g = 0, b = 0;
        for (int i = 0; i < w * h; i++)
        {
            b += pixels[i * 4];
            g += pixels[i * 4 + 1];
            r += pixels[i * 4 + 2];
        }

        return (r / (w * h), g / (w * h), b / (w * h));
    }

    private static void WithPage(string name, Action<string, AtelierPage, GradingPanel> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"frameflip-{name}-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };

        try
        {
            window.Show();
            body(root, page, (GradingPanel)page.FindName("Tools"));
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

    private static void Open(AtelierPage page, string path)
    {
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
        Pump(() => false, 0.3);
    }

    private static void ThePageMatches()
    {
        Check.Group("Farbe angleichen: im Atelier");

        WithPage("match", (root, page, tools) =>
        {
            // Bild 1 warm, Bild 2 kuehl - dieselbe Folge.
            Sequence(root, (200, 140, 90), (90, 140, 200));
            Open(page, Path.Combine(root, "render_0001.png"));

            var order = tools.Stack.Tools;
            Check.That(order.Last() is MatchTool && order.FindIndex(t => t is LutTool) < order.FindIndex(t => t is MatchTool),
                       "Farbe angleichen steht zuletzt im Stapel, hinter der LUT");

            tools.Show("Match");
            var warm = Shown(page);
            ((Button)tools.FindName("MatchApplyButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(tools.Match.Source is null && !((Button)tools.FindName("MatchApplyButton")).IsEnabled,
                       "ohne Vorbild ist \"Angleichen\" aus");

            Check.That(page.MatchReference(tools.Match) && tools.Match.ReferenceName == "render_0001.png" &&
                       ((TextBlock)tools.FindName("MatchState")).Text.Contains("render_0001.png"),
                       "\"Als Vorbild merken\": das warme Bild, mit Namen in der Karte");

            page.StepFrame(+1);
            Pump(() => false, 0.5);
            var cool = Shown(page);

            ((Button)tools.FindName("MatchApplyButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => false, 0.5);
            var matched = Shown(page);

            Check.That(tools.Match.Amount == 1f && cool.R < cool.B && matched.R > matched.B &&
                       Math.Abs((matched.R - matched.B) - (warm.R - warm.B)) < 12,
                       "\"Angleichen\" im kuehlen Bild: es wird so warm wie das Vorbild",
                       $"R-B: Vorbild {warm.R - warm.B:0}, vorher {cool.R - cool.B:0}, danach {matched.R - matched.B:0}");

            // Ein Vorbild aus einer Datei - so, wie sie ohne Korrektur aussieht.
            string other = Path.Combine(root, "vorbild");
            Directory.CreateDirectory(other);
            Sequence(other, (90, 200, 90));
            Check.That(page.MatchReferenceFrom(tools.Match, Path.Combine(other, "render_0001.png")) &&
                       tools.Match.ReferenceName == "render_0001.png" && tools.Match.Reference!.MeanCb < 0 && tools.Match.Reference.MeanCr < 0,
                       "ein Vorbild aus einer Datei: gruen, gemessen ohne Korrektur");
        });
    }

    private static void ThePageDeflickers()
    {
        Check.Group("Deflicker: im Atelier");

        WithPage("deflicker", (root, page, tools) =>
        {
            // Zwoelf Bilder, jedes zweite heller - ein Flackern.
            Sequence(root, Enumerable.Range(0, 12).Select(k => k % 2 == 0 ? ((byte)100, (byte)100, (byte)100) : ((byte)130, (byte)130, (byte)130)).ToArray());
            Open(page, Path.Combine(root, "render_0002.png"));

            tools.Show("Deflicker");
            var bright = Shown(page);
            Check.That(tools.Deflicker.IsNeutral && ((TextBlock)tools.FindName("DeflickerState")).Text.Length > 0,
                       "hinzugefuegt: neutral, noch nicht gemessen");

            ((Button)tools.FindName("DeflickerMeasureButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => tools.Deflicker.Levels is not null, 20);
            Pump(() => false, 0.5);

            var evened = Shown(page);
            Check.That(tools.Deflicker.Levels is { Count: 12 } && tools.Deflicker.Amount == 1f && page.DeflickerScan is { IsCompleted: true },
                       "\"Folge messen\": im Hintergrund alle zwoelf Bilder, danach wirkt es ganz",
                       $"{tools.Deflicker.Levels?.Count} Bilder");
            Check.That(evened.G < bright.G - 5 && ((TextBlock)tools.FindName("DeflickerState")).Text.Contains("12"),
                       "das helle Bild wird dunkler - zum Verlauf der Folge hin - und die Karte sagt, wie viel gemessen ist",
                       $"{bright.G:0} -> {evened.G:0}");

            page.StepFrame(-1);
            Pump(() => false, 0.5);
            var dark = Shown(page);
            Check.That(Math.Abs(dark.G - evened.G) < 6, "das dunkle Bild davor steht jetzt fast gleich hell",
                       $"{dark.G:0} gegen {evened.G:0}");

            // An einer Ebene gesperrt, wie alles, was fuer das ganze Bild gilt - das prueft die
            // Gruppenprobe fuer jede Karte der Kategorie.
        });
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
