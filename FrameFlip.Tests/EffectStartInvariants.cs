using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
/// Effekte beginnen sichtbar, Korrekturen neutral (docs/Atelier-Arbeitsablauf.md,
/// Entscheidung 2 und Punkt 13). Gemeldet war "Pixel Sort funktioniert nicht": Es rechnete
/// richtig, stand aber mit geschlossenem Fenster da, und eine 16-Bit-Ausgabe liess es ohne
/// Hinweis weg. Dasselbe galt fuer jeden Effekt des Knotenmenues.
/// </summary>
public static class EffectStartInvariants
{
    private static readonly string[] Effects =
    {
        "S_Bloom", "S_Halation", "S_Motion", "S_Displace", "S_DepthField", "S_Distortion", "S_Chromatic",
        "S_Vignette", "S_Dither", "S_NodeDiffusion", "S_Sort", "S_Grain",
    };

    private static readonly string[] Corrections =
    {
        "S_Correction", "S_Levels", "S_Curves", "S_WhiteBalance", "S_Zones", "S_ColourBands", "S_Vibrance",
        "S_Dehaze", "S_Noise", "S_Clarity", "S_Texture", "S_Sharpen", "S_Lut",
    };

    public static void Run()
    {
        NodesStartVisible();
        TheStackStartsVisible();
        TheExportSaysWhatItLeavesOut();
    }

    /// <summary>Das Knotenmenue: Effekte mit Startwert, Korrekturen neutral - und Pixel Sort aendert das Bild.</summary>
    private static void NodesStartVisible()
    {
        Check.Group("Effekte: im Knotenmenue sichtbar, Korrekturen neutral");

        object? ToolOf(Node node) => node switch
        {
            LocalNode l => l.Tool, OpticsNode o => o.Tool, GeometryNode g => g.Tool, DataNode d => d.Tool,
            FramePassNode f => f.Pass, PointToolNode p => p.Tool, LayerGradeNode => null, _ => null,
        };

        bool Neutral(object? tool) => tool switch
        {
            null => true,
            IGradingTool g => g.IsNeutral, ILocalTool l => l.IsNeutral, IOpticsTool o => o.IsNeutral,
            IGeometryTool m => m.IsNeutral, IDataTool d => d.IsNeutral, IFramePass f => f.IsNeutral,
            _ => true,
        };

        var silent = NodeCatalog.All.Where(k => Effects.Contains(k.TitleKey)).Where(k => Neutral(ToolOf(k.Create()))).Select(k => k.TitleKey).ToList();
        var loud = NodeCatalog.All.Where(k => Corrections.Contains(k.TitleKey)).Where(k => !Neutral(ToolOf(k.Create()))).Select(k => k.TitleKey).ToList();

        Check.That(NodeCatalog.All.Count(k => Effects.Contains(k.TitleKey)) == Effects.Length && silent.Count == 0,
                   "jeder Effekt des Menues kommt mit einem Startwert", string.Join(", ", silent));
        Check.That(loud.Count == 0, "jede Korrektur beginnt neutral", string.Join(", ", loud));

        // Schon Eingestelltes bleibt, wie es ist.
        var set = new SortTool { Low = 0.1f, High = 0.3f };
        Check.That(!EffectStart.Apply(set) && set.Low == 0.1f && set.High == 0.3f, "ein eingestellter Effekt behaelt seine Werte");

        // Am Bild: Pixel Sort aus dem Menue sortiert, Filmkorn koernt, die Vignette dunkelt ab.
        const int W = 240, H = 120;
        var bgra = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            bgra[i * 4] = (byte)(i % W);
            bgra[i * 4 + 1] = (byte)((i / W) * 2);
            bgra[i * 4 + 2] = (byte)((i * 7) % 255);
            bgra[i * 4 + 3] = 255;
        }

        var frame = FloatFrame.FromBgra32(bgra, W, H, W * 4);
        var sources = new Dictionary<string, FloatFrame> { [""] = frame };
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };
        byte[] plain = Render(StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack()), sources, W, H);

        foreach (string key in new[] { "S_Sort", "S_Grain", "S_Vignette" })
        {
            var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());
            var node = graph.Add(NodeCatalog.All.First(k => k.TitleKey == key).Create());
            var output = graph.Output!;
            var into = graph.Into(output.Id, "Bild")!;
            var before = graph.Find(into.From)!;

            graph.Links.Remove(into);
            graph.Connect(before, into.Output, node, "Bild");
            graph.Connect(node, "Bild", output, "Bild");

            int changed = Changed(plain, Render(graph, sources, W, H));
            Check.That(changed > W * H / 20, $"{Localization.Strings.T(key)} aus dem Menue aendert das Bild sofort", $"{changed} Punkte");
        }
    }

    /// <summary>Der Farbstreifen im Stapel: dieselben Startwerte, beim Rastern nur fuer das Raster.</summary>
    private static void TheStackStartsVisible()
    {
        Check.Group("Effekte: im Stapel sichtbar, Korrekturen neutral");

        var panel = new GradingPanel();
        var window = new Window { Content = panel, Width = 420, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };

        try
        {
            window.Show();
            panel.Load(ImageAdjustments.Neutral, new GradingStack());

            panel.Show("Sort");
            var sort = panel.Stack.Frame.OfType<SortTool>().Single();
            Check.That(!sort.IsNeutral, "Pixel Sort hinzugefuegt: das Fenster steht offen", $"{sort.Low} bis {sort.High}");

            panel.Show("Clarity");
            Check.That(panel.Stack.Local.OfType<ClarityTool>().Single().IsNeutral, "Klarheit hinzugefuegt: neutral");

            panel.Show("Dither");
            Check.That(!panel.Stack.Optics.OfType<DitherTool>().Single().IsNeutral && panel.Stack.Frame.OfType<DiffusionTool>().Single().IsNeutral,
                       "Rastern: nur das Raster bekommt den Startwert, die Diffusion auf demselben Regler bleibt aus");

            // Wer das Fenster schliesst, dem wird es beim naechsten Zeigen nicht wieder aufgezogen.
            sort.Low = sort.High = 0.5f;
            panel.Show("Sort");
            Check.That(sort.IsNeutral, "eine schon hinzugefuegte Karte wird beim erneuten Zeigen nicht wieder aufgezogen");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Die Exportleiste sagt, wenn ein 16-Bit-Format Pixel Sort weglaesst.</summary>
    private static void TheExportSaysWhatItLeavesOut()
    {
        Check.Group("Ausgabe: der Hinweis auf 8 Bit, wo Pixel Sort sonst fehlte");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-startwerte-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[160 * 90 * 4];
        for (int i = 0; i < 160 * 90; i++) { pixels[i * 4] = (byte)(i % 160); pixels[i * 4 + 1] = 120; pixels[i * 4 + 2] = (byte)(i / 160 * 2); pixels[i * 4 + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            var note = (TextBlock)page.FindName("EightBitNote");
            var format = (ComboBox)page.FindName("FormatBox");
            var tools = (GradingPanel)page.FindName("Tools");

            Check.That(format.SelectedIndex == 0 && note.Visibility != Visibility.Visible, "ohne Pixel Sort: kein Hinweis, auch in 16 Bit");

            tools.Show("Sort");
            Pump(() => false, 0.3);
            Check.That(note.Visibility == Visibility.Visible, "Pixel Sort und PNG 16 Bit: der Hinweis steht an der Ausgabe");

            format.SelectedIndex = 1;
            Check.That(note.Visibility != Visibility.Visible, "PNG 8 Bit nimmt es mit: kein Hinweis");

            format.SelectedIndex = 2;
            Check.That(note.Visibility == Visibility.Visible, "TIFF 16 Bit: wieder der Hinweis");

            format.SelectedIndex = 0;

            // Mit dem Regler geschlossen: Nach dem Zug ist der Durchgang weg - und der Hinweis auch.
            // Der Zug rechnet erst grob und am Ende genau; auch dieses Ende muss den Hinweis stellen.
            var low = (Slider)tools.FindName("SortLowSlider");
            var high = (Slider)tools.FindName("SortHighSlider");
            double open = high.Value;

            high.Value = low.Value;
            Pump(() => note.Visibility != Visibility.Visible, 2);
            Check.That(note.Visibility != Visibility.Visible, "Pixel Sort mit dem Regler geschlossen: nach dem Zug kein Hinweis mehr");

            high.Value = open;
            Pump(() => note.Visibility == Visibility.Visible, 2);
            Check.That(note.Visibility == Visibility.Visible, "und mit dem Regler wieder geoeffnet: der Hinweis ist zurueck");

            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);
            Check.That(page.FramePassesActive() && note.Visibility == Visibility.Visible, "im Knotenmodus ebenso - der Knoten aus dem Stapel");
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

    private static int Changed(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;
        return n;
    }

    private static byte[] Render(NodeGraph graph, Dictionary<string, FloatFrame> sources, int width, int height)
    {
        var inputs = new GraphInputs
        {
            Sources = sources, Data = new Dictionary<PassNeed, FloatFrame?>(), View = new StandardViewTransform(), Step = 1, Number = 0,
        };

        var pixels = new byte[width * height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            GraphEvaluator.Render(graph, inputs, buffer, width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
