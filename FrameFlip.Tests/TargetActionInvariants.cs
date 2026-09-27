using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Aktionen am Bearbeitungsziel (docs/Atelier-Arbeitsablauf.md, C2): Ein Effekt geht dorthin,
/// wohin die Zielzeile zeigt, und ist verbunden. Am Gesamtbild an die Stelle, an der der
/// Stapel ihn rechnen wuerde; an einer Maske in den Zweig ihrer Ebene.
/// </summary>
public static class TargetActionInvariants
{
    public static void Run()
    {
        TheChainKnowsItsOrder();
        ThePageAddsWhereTheLinePoints();
    }

    /// <summary>Ohne Fenster: die Kette des Gesamtbilds und wohin ein neuer Knoten darin gehoert.</summary>
    private static void TheChainKnowsItsOrder()
    {
        Check.Group("Aktionen: die Kette des Gesamtbilds");

        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };
        var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());
        var chain = GlobalChain.Chain(graph);

        Check.That(chain.Count >= 3 && chain[0] is LightNode && chain.Any(n => n is ViewNode) && chain[^1] is ToneNode,
                   "die Kette laeuft vom Licht ueber die Sichtumwandlung bis zu den Tonwerten vor der Ausgabe",
                   string.Join(" > ", chain.Select(n => n.GetType().Name)));

        var light = chain[0];
        var tone = chain[^1];

        Check.That(ReferenceEquals(GlobalChain.After(graph, new OpticsNode { Tool = new VignetteTool() }), light),
                   "eine Vignette (Film) kommt vor die Sichtumwandlung - hinter das Licht");
        Check.That(ReferenceEquals(GlobalChain.After(graph, new PointToolNode { Tool = new LevelsTool() }), tone),
                   "ein Tonwert (Anzeige) kommt hinter die Tonwerte");
        Check.That(ReferenceEquals(GlobalChain.After(graph, new FramePassNode { Pass = new SortTool() }), tone),
                   "Pixel Sort ganz ans Ende");
        Check.That(ReferenceEquals(GlobalChain.After(graph, new MaskNode()), tone),
                   "etwas ohne Platz in der Kette: vor die Ausgabe, damit es verbunden bleibt");
    }

    /// <summary>Auf der Seite, im Knotenmodus: Gesamtbild, Maske einer Ebene, und die Knoepfe der Zielzeile.</summary>
    private static void ThePageAddsWhereTheLinePoints()
    {
        Check.Group("Aktionen: der Effekt geht dorthin, wohin die Zeile zeigt");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-aktionen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[160 * 90 * 4];
        for (int i = 0; i < 160 * 90; i++) { pixels[i * 4] = (byte)(i % 160); pixels[i * 4 + 1] = 110; pixels[i * 4 + 2] = (byte)(i / 160 * 2); pixels[i * 4 + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");
        var editor = (NodeEditor)page.FindName("NodeView");
        var tools = (GradingPanel)page.FindName("Tools");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            // Im Stapel: "+ Korrektur" zeigt die Grundkarte des Ziels, "+ Effekt ..." oeffnet die Suche.
            Click((Button)page.FindName("TargetCorrect"));
            Check.That(tools.IsShown("Basic"), "im Stapel: \"+ Korrektur\" nimmt den Weg der Werkzeugleiste");
            Click((Button)page.FindName("TargetEffect"));
            var search = (Popup)((FrameworkElement)page.FindName("ToolBand")).FindName("SearchPopup");
            Check.That(search.IsOpen, "\"+ Effekt ...\" oeffnet die Suche der Werkzeugleiste");
            search.IsOpen = false;

            var mask = new LayerMask { Kind = MaskKind.Painted };
            layers.AddAdjustment();
            layers.Selection!.Mask = mask;
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);
            var graph = page.Graph!;

            // Nichts gewaehlt - das Gesamtbild.
            editor.Select(null);
            int count = graph.Nodes.Count;
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Vignette"));
            var vignette = graph.Nodes.OfType<OpticsNode>().Single(n => n.Tool is VignetteTool);
            var view = graph.Nodes.OfType<ViewNode>().Single();

            Check.That(graph.Nodes.Count == count + 1 && graph.Into(view.Id, "Bild")?.From == vignette.Id &&
                       GlobalChain.Chain(graph).Contains(vignette),
                       "am Gesamtbild: die Vignette steht verbunden in der Kette, vor der Sichtumwandlung",
                       string.Join(" > ", GlobalChain.Chain(graph).Select(n => n.GetType().Name)));

            editor.Select(null);
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Sort"));
            var sort = graph.Nodes.OfType<FramePassNode>().Single(n => n.Pass is SortTool);
            Check.That(graph.Into(graph.Output!.Id, "Bild")?.From == sort.Id, "Pixel Sort am Gesamtbild: ganz am Ende, vor der Ausgabe");

            editor.Select(null);
            Click((Button)page.FindName("TargetCorrect"));
            var correction = graph.Nodes.OfType<LayerGradeNode>().FirstOrDefault(n => GlobalChain.Chain(graph).Contains(n));
            Check.That(correction is not null && graph.Into(correction.Id, "Bild")?.From == GlobalChain.Chain(graph)[0].Id,
                       "\"+ Korrektur\" am Gesamtbild: eine Korrektur gleich hinter dem Licht");

            // Die Maske einer Ebene gewaehlt: in den Zweig der Ebene.
            var owner = NodeLayerList.Of(graph).Single(l => l.MaskSource is MaskNode);
            editor.Select(owner.MaskSource);
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Grain"));
            var grain = graph.Nodes.OfType<OpticsNode>().Single(n => n.Tool is GrainTool);

            Check.That(LayerEdits.Branch(graph, owner.Mix!).Contains(grain.Id) &&
                       graph.Into(owner.Mix!.Id, "Oben")?.From == grain.Id,
                       "die Maske einer Ebene gewaehlt: der Effekt kommt in den Zweig der Ebene, vor ihr Mischen");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
