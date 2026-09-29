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
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Ausgleichen, CLAHE und Tonwerttrennung nach Quantilen (docs/Atelier-Werkzeugplan.md, W2e).
/// </summary>
public static class EqualiseInvariants
{
    public static void Run()
    {
        Equalising();
        Posterising();
        Local();
        Keeping();
        ThePageMeasures();
    }

    /// <summary>Eine dunkle Verteilung: y = u hoch drei, fast alles in den Tiefen.</summary>
    private static float[] Dark(int count)
    {
        var rgb = new float[count * 3];

        for (int i = 0; i < count; i++)
        {
            float u = (i + 0.5f) / count, y = u * u * u;
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = y;
        }

        return rgb;
    }

    private static float[] Through(EqualiseTool tool, float[] rgb)
    {
        tool.Prepare();
        var lumas = new float[rgb.Length / 3];

        for (int i = 0; i < lumas.Length; i++)
        {
            float r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            tool.Apply(ref r, ref g, ref b);
            lumas[i] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        Array.Sort(lumas);
        return lumas;
    }

    private static void Equalising()
    {
        Check.Group("Ausgleich: stufenlos");

        var sample = Dark(4000);
        var tool = new EqualiseTool { Measured = EqualiseTool.Measure(sample), Amount = 1f };
        var result = Through(tool, sample);

        float q1 = result[result.Length / 4], q2 = result[result.Length / 2], q3 = result[result.Length * 3 / 4];
        Check.That(MathF.Abs(q1 - 0.25f) < 0.03f && MathF.Abs(q2 - 0.5f) < 0.03f && MathF.Abs(q3 - 0.75f) < 0.03f,
                   "eine dunkle Verteilung wird gleichmaessig: die Viertel bei 25, 50 und 75 %",
                   $"{q1:0.000} {q2:0.000} {q3:0.000}");

        var half = new EqualiseTool { Measured = tool.Measured, Amount = 0.5f };
        var halfway = Through(half, sample);
        float before = sample[sample.Length / 2 / 3 * 3];
        Check.That(MathF.Abs(halfway[halfway.Length / 2] - (before + q2) / 2) < 0.02f,
                   "halbe Staerke: auf halbem Weg", $"{halfway[halfway.Length / 2]:0.000}");

        float r = 0.6f, g = 0.3f, b = 0.2f;
        tool.Prepare();
        tool.Apply(ref r, ref g, ref b);
        Check.That(MathF.Abs((r - g) - 0.3f) < 1e-5f && MathF.Abs((g - b) - 0.1f) < 1e-5f && r > 0.6f,
                   "die Farbe wandert als Abstand zur Helligkeit mit");

        var none = new EqualiseTool { Amount = 1f };
        var off = new EqualiseTool { Measured = tool.Measured };
        Check.That(none.IsNeutral && off.IsNeutral && !tool.IsNeutral, "ohne Messung oder ohne Staerke rechnet nichts");
    }

    private static void Posterising()
    {
        Check.Group("Ausgleich: Tonwerttrennung nach Quantilen");

        var sample = Dark(4000);
        var tool = new EqualiseTool { Measured = EqualiseTool.Measure(sample), Amount = 1f, Steps = 4 };
        var result = Through(tool, sample);

        var tones = new float[4];
        for (int k = 0; k < 4; k++) tones[k] = EqualiseTool.Quantile(tool.Measured!, (k + 0.5f) / 4);

        int onTone = result.Count(y => tones.Any(t => MathF.Abs(y - t) < 0.004f));
        Check.That(onTone >= result.Length * 0.97, "vier Stufen: fast jeder Punkt steht auf einem der vier Toene",
                   $"{onTone} von {result.Length}");

        var shares = tones.Select(t => result.Count(y => MathF.Abs(y - t) < 0.004f) / (float)result.Length).ToArray();
        Check.That(shares.All(s => MathF.Abs(s - 0.25f) < 0.03f), "jede Stufe deckt ein Viertel des Bildes",
                   string.Join(" ", shares.Select(s => $"{s:0.000}")));

        // Der Ton einer Stufe ist der mittlere ihrer Punkte: das Achtel, das Dreiachtel ... der Verteilung.
        bool kept = Enumerable.Range(0, 4).All(k => MathF.Abs(tones[k] - MathF.Pow((k + 0.5f) / 4, 3)) < 0.01f);
        Check.That(kept, "und ihr Ton bleibt, wo er war - der Median ihrer Punkte",
                   string.Join(" ", tones.Select(t => $"{t:0.000}")));
    }

    private static unsafe void Local()
    {
        Check.Group("Oertlicher Ausgleich (CLAHE)");

        const int W = 256, H = 128;

        byte[] Picture(Func<int, int, byte> value)
        {
            var pixels = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = value(x, y);
                    pixels[i + 3] = 255;
                }
            return pixels;
        }

        void Run(ClaheTool tool, byte[] pixels)
        {
            fixed (byte* p = pixels) tool.Apply((IntPtr)p, W, H, W * 4);
        }

        // Der Mittelwert und der oertliche Kontrast: wie weit benachbarte Zellen des Musters auseinanderliegen.
        (double Mean, double Local) Stats(byte[] pixels)
        {
            double mean = Enumerable.Range(0, W * H).Average(i => (double)pixels[i * 4]);
            double local = Enumerable.Range(0, H).SelectMany(y => Enumerable.Range(0, W - 4).Select(x => (x, y)))
                .Average(p => Math.Abs(pixels[(p.y * W + p.x) * 4] - pixels[(p.y * W + p.x + 4) * 4]));
            return (mean, local);
        }

        var flat = Picture((_, _) => 128);
        Run(new ClaheTool { Amount = 1f }, flat);
        Check.That(Enumerable.Range(0, W * H).All(i => Math.Abs(flat[i * 4] - 128) <= 2),
                   "eine gleichmaessige Flaeche bleibt, wie sie ist - die Grenze haelt sie ruhig");

        // Flauer Verlauf mit leisem Muster: 100 bis 130.
        byte Soft(int x, int y) => (byte)(100 + x * 24 / W + ((x / 4 + y / 4) % 2) * 6);

        var soft = Picture(Soft);
        var (mean, local) = Stats(soft);
        Run(new ClaheTool { Amount = 1f, Tiles = 8, Limit = 4f }, soft);
        var (after, stronger) = Stats(soft);
        Check.That(stronger > local * 2 && Math.Abs(after - mean) < 25,
                   "ein flaues Bild bekommt oertlichen Kontrast, ohne heller oder dunkler zu werden",
                   $"oertlich {local:0.0} -> {stronger:0.0}, Mittel {mean:0} -> {after:0}");

        var calm = Picture(Soft);
        Run(new ClaheTool { Amount = 1f, Tiles = 8, Limit = 1.2f }, calm);
        Check.That(Stats(calm).Local < stronger, "eine engere Grenze verstaerkt weniger",
                   $"{Stats(calm).Local:0.0} gegen {stronger:0.0}");

        var untouched = Picture(Soft);
        Run(new ClaheTool(), untouched);
        Check.That(new ClaheTool().IsNeutral && untouched.SequenceEqual(Picture(Soft)), "ohne Staerke rechnet nichts");

        var coarse = Picture(Soft);
        var full = Picture(Soft);
        fixed (byte* p = coarse) new ClaheTool { Amount = 0.7f }.ApplyCoarse((IntPtr)p, W, H, W * 4, 0, 4);
        Run(new ClaheTool { Amount = 0.7f }, full);
        Check.That(coarse.SequenceEqual(full), "grob auf dem Gitter dieselbe Rechnung - Kacheln sind eine Anzahl");

        // Kosten: ein Durchgang ueber das fertige Bild, bei 4K neben der Fehlerdiffusion.
        const int BigW = 3840, BigH = 2160;
        var big = new byte[BigW * BigH * 4];
        new Random(3).NextBytes(big);
        var clahe = new ClaheTool { Amount = 1f };
        var diffusion = new DiffusionTool { Amount = 1f };
        diffusion.Prepare();

        fixed (byte* p = big)
        {
            var start = (IntPtr)p;
            var times = Measure.Fastest(3, t => t[0] < t[1],
                () => clahe.Apply(start, BigW, BigH, BigW * 4),
                () => diffusion.Apply(start, BigW, BigH, BigW * 4));

            Check.Timing(times[0] < times[1], "4K: der oertliche Ausgleich kostet weniger als die Fehlerdiffusion",
                         $"{times[0]:0} ms gegen {times[1]:0} ms");
        }
    }

    private static void Keeping()
    {
        Check.Group("Ausgleich: speichern und kopieren");

        var stack = new GradingStack();
        stack.Tools.Add(new EqualiseTool { Measured = EqualiseTool.Measure(Dark(100)), Amount = 0.8f, Steps = 5 });
        stack.Frame.Add(new ClaheTool { Amount = 0.4f, Tiles = 6, Limit = 3f });

        var back = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack))!;
        var equalise = back.Tools.OfType<EqualiseTool>().Single();
        var clahe = back.Frame.OfType<ClaheTool>().Single();

        Check.That(equalise.Amount == 0.8f && equalise.Steps == 5 &&
                   equalise.Measured!.SequenceEqual(stack.Tools.OfType<EqualiseTool>().Single().Measured!) &&
                   clahe.Amount == 0.4f && clahe.Tiles == 6 && clahe.Limit == 3f,
                   "gespeichert und geladen: Messung, Staerke, Stufen, Kacheln, Grenze");

        var copy = stack.Clone();
        var copied = copy.Tools.OfType<EqualiseTool>().Single();
        copied.Measured![10] = 0.9f;
        copy.Frame.OfType<ClaheTool>().Single().Amount = 1f;

        Check.That(stack.Tools.OfType<EqualiseTool>().Single().Measured![10] != 0.9f &&
                   stack.Frame.OfType<ClaheTool>().Single().Amount == 0.4f,
                   "eine Kopie ist unabhaengig - auch die Messung");
    }

    private static void ThePageMeasures()
    {
        Check.Group("Ausgleich: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-equalise-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        // Ein dunkler Verlauf: Spalte x hat den Wert (x/255) hoch drei.
        const int W = 256, H = 8;
        string path = Path.Combine(root, "render_0001.png");
        var source = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            byte v = (byte)MathF.Round(MathF.Pow(i % W / 255f, 3) * 255f);
            source[i * 4] = source[i * 4 + 1] = source[i * 4 + 2] = v;
            source[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, source, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");

        double Mean()
        {
            var surface = (WriteableBitmap)typeof(AtelierPage).GetField("_surface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
            var row = new byte[W * 4];
            surface.CopyPixels(new Int32Rect(0, 1, W, 1), row, W * 4, 0);
            return Enumerable.Range(0, W).Average(x => row[x * 4 + 1]) / 255.0;
        }

        int Distinct()
        {
            var surface = (WriteableBitmap)typeof(AtelierPage).GetField("_surface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
            var row = new byte[W * 4];
            surface.CopyPixels(new Int32Rect(0, 1, W, 1), row, W * 4, 0);
            return Enumerable.Range(0, W).Select(x => row[x * 4 + 1]).Distinct().Count();
        }

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            var order = tools.Stack.Tools;
            Check.That(order.FindIndex(t => t is LevelsTool) + 1 == order.FindIndex(t => t is EqualiseTool) &&
                       order.FindIndex(t => t is EqualiseTool) + 1 == order.FindIndex(t => t is CurvesTool) &&
                       tools.Stack.Frame.FirstOrDefault() is ClaheTool,
                       "der Ausgleich steht hinter dem Tonwert und vor den Kurven, CLAHE als erster Durchgang");

            double dark = Mean();
            tools.Show("Equalise");
            Check.That(tools.Equalise.IsNeutral, "hinzugefuegt: neutral - eine Korrektur");

            ((Button)tools.FindName("EqualiseMeasureButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => false, 0.3);

            double even = Mean();
            Check.That(tools.Equalise.Measured is not null && tools.Equalise.Amount == 1f && dark < 0.3 && Math.Abs(even - 0.5) < 0.06,
                       "\"Ausgleichen\": gemessen, ganz wirksam - der dunkle Verlauf wird gleichmaessig hell",
                       $"Mittel {dark:0.00} -> {even:0.00}");

            // Gemessen wird, was beim Ausgleich ANKOMMT: Eine Kurve dahinter aendert die Messung nicht.
            var measured = tools.Equalise.Measured!.ToArray();
            tools.Stack.Tools.OfType<CurvesTool>().First().Master =
                new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.85f), new CurvePoint(1, 1) });
            tools.LevelsChangedOutside();
            Pump(() => false, 0.3);

            page.MeasureEqualise(tools.Equalise);
            Check.That(tools.Equalise.Measured!.Zip(measured).All(p => MathF.Abs(p.First - p.Second) < 1e-5f),
                       "eine Kurve hinter dem Ausgleich aendert die Messung nicht");

            tools.Stack.Tools.OfType<CurvesTool>().First().Master = new ToneCurve();
            tools.LevelsChangedOutside();

            ((Slider)tools.FindName("EqualiseStepsSlider")).Value = 4;
            Pump(() => false, 0.6);
            Check.That(tools.Equalise.Steps == 4 && Distinct() <= 8, "vier Stufen: im Bild stehen nur noch wenige Toene",
                       $"{Distinct()} Toene");

            ((Slider)tools.FindName("EqualiseStepsSlider")).Value = 0;
            ((Slider)tools.FindName("EqualiseAmountSlider")).Value = 0;
            Pump(() => false, 0.6);

            // Die Staerke hochgezogen, bevor gemessen wurde: dann misst die Seite von selbst.
            tools.Equalise.Measured = null;
            ((Slider)tools.FindName("EqualiseAmountSlider")).Value = 0.5;
            Check.That(tools.Equalise.Measured is not null && Math.Abs(tools.Equalise.Amount - 0.5f) < 1e-4f,
                       "ohne Messung die Staerke gezogen: gemessen, die Staerke bleibt");

            // CLAHE auf der Seite: ein Durchgang ueber das fertige Bild.
            tools.Show("Clahe");
            ((Slider)tools.FindName("ClaheAmountSlider")).Value = 1;
            Pump(() => false, 0.8);
            Check.That(tools.Stack.Frame.OfType<ClaheTool>().Single().Amount == 1f &&
                       ((TextBlock)page.FindName("EightBitNote")).Visibility == Visibility.Visible,
                       "CLAHE eingestellt - und wie Pixel Sort mit dem Hinweis fuer den Export mit 16 Bit");

            // Im Knotenmodus: ein Ausgleichsknoten, gemessen an seinem Eingang.
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Equalise"));
            Pump(() => false, 0.3);

            var node = page.Graph!.Nodes.OfType<PointToolNode>().LastOrDefault(n => n.Tool is EqualiseTool);
            var nodeTool = node?.Tool as EqualiseTool;
            Check.That(nodeTool is not null && page.MeasureEqualise(nodeTool) && nodeTool.Measured is not null && !nodeTool.IsNeutral,
                       "im Knotenmodus: ein Ausgleichsknoten, gemessen an seinem Eingang");
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
