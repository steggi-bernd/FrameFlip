using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using FrameFlip.Views;

internal static partial class Program
{
    /// <summary>
    /// Masken im Graphen, als Bild zum Ansehen: eine Maske an ihrer Ebene und an einer daraus
    /// ausgeschnittenen, gewaehlt - ihre Ebenen umrandet -, und eine freie Kopie. Nur ein
    /// synthetischer Graph, keine Medien.
    /// </summary>
    private static void TestMaskGraph()
    {
        var graph = StackToGraph.Convert(new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer
                {
                    Content = LayerContent.Adjustment,
                    Adjustments = new ImageAdjustments { Exposure = -1 },
                    Mask = new LayerMask { Kind = MaskKind.Painted },
                },
            },
        }, new ImageAdjustments(), new GradingStack());

        var mask = graph.Nodes.OfType<MaskNode>().Single();
        var layer = graph.Nodes.OfType<MixNode>().Single(m => graph.Into(m.Id, "Faktor")?.From == mask.Id);
        layer.Label = "Himmel";

        var cut = LayerEdits.AddCutout(graph, layer, layer, "Bild", mask, "Maske")!.Value;
        cut.Mix.Label = Strings.T("S_CutoutLayerName");

        var copy = NodeEdits.Duplicate(graph, mask)!;
        NodeLayout.Arrange(graph);
        copy.Y += 180;

        var editor = new NodeEditor
        {
            Graph = graph,
            Translate = key => Strings.T(key),
            Title = NodeTitles.For,
            MaskTitle = NodeTitles.MaskName,
            SocketTitle = NodeTitles.Socket,
        };

        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)), Child = editor };
        Layout(host, 1200, 560);

        editor.Frame();
        editor.Select(mask);

        Render(host, 1200, 560, "Masken-im-Graphen.png");

        Check(MaskUse.Users(graph, mask).Count == 2 && MaskUse.Free(graph, (MaskNode)copy),
              "Masken im Graphen: eine Maske an zwei Ebenen, eine freie Kopie");
    }
}
