using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

internal static partial class Program
{
    /// <summary>
    /// Die Ebenenliste im Knotenmodus, als Bild zum Ansehen (R3): Zeilen mit etwas kleinerer
    /// Miniatur, rechts die Marken - fx fuer eigene Effekte, ⬢ fuer eine Objektmaske -, und
    /// unter der Liste die Chips. Ein synthetischer Graph ohne Bilder.
    /// </summary>
    private static void TestLayerList()
    {
        var graph = StackToGraph.Convert(new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Grundlage" },
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Glanz", Mode = BlendMode.Add },
                new ImageLayer
                {
                    Content = LayerContent.Pass, Source = "", Name = "Licht", Mode = BlendMode.Screen,
                    Mask = new LayerMask { Kind = MaskKind.Cryptomatte, Source = "CryptoObject" },
                },
            },
        }, new ImageAdjustments(), new GradingStack());

        // Ein Effekt im Zweig der mittleren Ebene - so, wie ihn die Werkzeugleiste setzt.
        var glanz = NodeLayerList.Of(graph).Single(l => l.Name == "Glanz").Mix!;
        var effect = NodeCatalog.All.First(k => k.Section == "Vignette").Create();
        graph.Add(effect);
        NodeEdits.InsertInto(graph, graph.Into(glanz.Id, "Oben")!, effect);

        var layers = NodeLayerList.Of(graph);
        var list = new NodeLayerList { Width = 300 };
        list.Show(layers, glanz, _ => null, _ => null);

        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)), Child = list };
        host.Measure(new Size(300, double.PositiveInfinity));
        Render(host, 300, (int)Math.Ceiling(host.DesiredSize.Height), "Ebenenliste.png");

        Check(layers.Single(l => l.Name == "Glanz").Effects == 1 && layers.Where(l => l.Name != "Glanz").All(l => l.Effects == 0),
              "Ebenenliste: fx nur an der Ebene mit eigenem Effekt - Platzieren zaehlt nicht");
        Check(layers.Single(l => l.Name == "Licht").MaskSource is MaskNode { Mask.Kind: MaskKind.Cryptomatte },
              "Ebenenliste: die Ebene mit Kryptomatte traegt die Objektmaske");
    }
}
