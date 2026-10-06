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
    /// Der Graph in Gruppen, als Bild zum Ansehen (docs/Atelier-Knoten-Gruppen.md): links die
    /// Quellen, je Ebene ein Rahmen, die Mischen in einer Spalte, rechts das Gesamtbild. Einmal
    /// ganz, einmal so, wie der Editor ihn oeffnet. Nur ein synthetischer Graph, keine Medien.
    /// </summary>
    private static void TestNodeGroups()
    {
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Pass, Source = "AO", Mode = BlendMode.Multiply, Name = "AO",
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Pass, Source = "Glossy", Mode = BlendMode.Add, Name = "Glanz",
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 30, Width = 0.6f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Pass, Source = "Emit", Mode = BlendMode.Screen, Name = "Lampe",
            Adjustments = new ImageAdjustments { Exposure = 0.4 },
            Mask = new LayerMask { Kind = MaskKind.Gradient, Scope = MaskScope.Colour, Angle = 0, Width = 0.5f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Adjustment, Name = "Abend",
            Adjustments = new ImageAdjustments { Exposure = -0.3 },
            Tools = new GradingStack(),
            Mask = new LayerMask { Kind = MaskKind.Painted },
        });

        var group = new ImageLayer { Content = LayerContent.Group, Name = "Nebel", Opacity = 0.8f };
        group.Children.Add(new ImageLayer { Content = LayerContent.Pass, Source = "Mist", Mode = BlendMode.Screen, Name = "Dunst" });
        group.Children.Add(new ImageLayer { Content = LayerContent.Pass, Source = "Volume", Mode = BlendMode.Add, Clipped = true, Name = "Strahlen" });
        stack.Layers.Add(group);

        var graph = StackToGraph.Convert(stack, new ImageAdjustments { Exposure = 0.2 },
                                         new GradingStack { Optics = { new VignetteTool { Amount = -0.4f } } });

        var editor = new NodeEditor
        {
            Graph = graph,
            Translate = key => Strings.T(key),
            Title = NodeTitles.For,
            MaskTitle = NodeTitles.MaskName,
            SocketTitle = NodeTitles.Socket,
        };

        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1A)), Child = editor };

        // So, wie der Editor ihn oeffnet: lesbar gross, oben links.
        Layout(host, 1600, 900);
        editor.Frame();
        editor.Select(graph.Nodes.OfType<MixNode>().First(m => m.Label == "Lampe"));
        Render(host, 1600, 900, "Knoten-Gruppen.png");

        // Und ganz: herausgezoomt, bis alles darauf passt.
        var all = graph.Nodes.Select(NodeEditor.Bounds).Aggregate(Rect.Union);
        double fit = Math.Min(1560 / all.Width, 860 / all.Height);
        editor.ZoomAt(new Point(0, 0), fit / editor.Zoom);

        // Die linke obere Ecke des Graphen an die linke obere Ecke des Bildes.
        typeof(NodeEditor).GetProperty(nameof(NodeEditor.Pan))!
            .SetValue(editor, new Vector(20 - all.X * editor.Zoom, 20 - all.Y * editor.Zoom));
        editor.InvalidateVisual();

        Render(host, 1600, 900, "Knoten-Gruppen-ganz.png");

        Check(NodeGroups.Of(graph).Count == graph.Nodes.OfType<MixNode>().Count() + 1 && graph.Layout == NodeLayout.Grouped,
              "Knoten in Gruppen: je Ebene eine Gruppe, dazu das Gesamtbild");
    }
}
