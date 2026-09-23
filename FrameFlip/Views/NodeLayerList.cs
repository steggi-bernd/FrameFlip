using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace FrameFlip.Views;

/// <summary>Eine Ebene im Graphen: das Mischen, das sie auf das Bisherige legt, und woher ihr Bild kommt.</summary>
/// <param name="Mix">Das Mischen - bei der Grundlage keines: Sie wird auf nichts gelegt.</param>
/// <param name="Source">Der Knoten, der in "Oben" fliesst - seine Vorschau ist die Miniatur der Ebene.</param>
public sealed record NodeLayer(MixNode? Mix, Node? Source, string Name, string Detail)
{
    /// <summary>Welcher Knoten gewaehlt wird, wenn man die Ebene anklickt.</summary>
    public Node? Target => Mix ?? Source;
}

/// <summary>
/// Die Ebenen im Knotenmodus - im Reiter, in dem sonst der Ebenenstreifen steht.
///
/// Im Graphen ist eine Ebene kein Eintrag, sondern ein Zweig, der in ein Mischen
/// muendet; bei zehn Ebenen sucht man den richtigen Zweig. Die Liste zeigt jedes
/// Mischen als Zeile, oben das zuletzt gemischte, mit einer Miniatur dessen, was
/// hineinfliesst. Ein Klick waehlt den Knoten und rueckt ihn ins Bild, das Auge
/// schaltet die Ebene stumm.
/// </summary>
public sealed class NodeLayerList : Border
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _empty;

    /// <summary>Eine Ebene wurde angeklickt - ihr Mischen, bei der Grundlage ihr Knoten.</summary>
    public event Action<Node>? Chosen;

    /// <summary>Das Auge einer Ebene wurde angeklickt.</summary>
    public event Action<MixNode>? MuteWanted;

    public NodeLayerList()
    {
        Padding = new Thickness(10, 8, 10, 8);

        _empty = new TextBlock
        {
            Text = Strings.T("S_NodeLayersEmpty"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB4)),
            FontSize = 11,
            Margin = new Thickness(2, 4, 2, 4),
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Strings.T("S_NodeLayersHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB4)),
            FontSize = 11,
            Margin = new Thickness(2, 0, 2, 8),
        });
        content.Children.Add(_rows);
        content.Children.Add(_empty);

        Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Die Zeilen, wie sie gerade dastehen - fuer die Probe.</summary>
    internal IReadOnlyList<NodeLayer> Shown { get; private set; } = Array.Empty<NodeLayer>();

    /// <summary>Zeigt die Ebenen eines Graphen. <paramref name="thumb"/> liefert die Miniatur zu einem Knoten.</summary>
    public void Show(IReadOnlyList<NodeLayer> layers, Node? selected, Func<Node, ImageSource?> thumb)
    {
        Shown = layers;
        _rows.Children.Clear();
        _empty.Visibility = layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var layer in layers) _rows.Children.Add(Row(layer, ReferenceEquals(selected, layer.Target), thumb));
    }

    private static readonly Brush ChosenBack = new SolidColorBrush(Color.FromArgb(0x55, 0xA4, 0x7B, 0xF0));
    private static readonly Brush RowBack = new SolidColorBrush(Color.FromArgb(0x30, 0x23, 0x23, 0x2A));

    private UIElement Row(NodeLayer layer, bool chosen, Func<Node, ImageSource?> thumb)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool muted = layer.Mix?.Muted == true;

        // Die Grundlage hat kein Mischen, das man stummschalten koennte.
        if (layer.Mix is { } mix)
        {
            var eye = new ToggleButton
            {
                Style = (Style)FindResource("OverlayToggle"),
                IsChecked = !muted,
                Content = muted ? "–" : "●",
                ToolTip = Strings.T("S_NodeLayerMute"),
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };

            eye.Click += (_, e) =>
            {
                MuteWanted?.Invoke(mix);
                e.Handled = true;
            };

            Grid.SetColumn(eye, 0);
            grid.Children.Add(eye);
        }

        var picture = new Border
        {
            Width = 64,
            Height = 36,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)),
            Margin = new Thickness(2, 0, 6, 0),
            Child = new Image
            {
                Source = layer.Source is null ? null : thumb(layer.Source),
                Stretch = Stretch.Uniform,
            },
        };

        Grid.SetColumn(picture, 1);
        grid.Children.Add(picture);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = layer.Name,
            Foreground = Brushes.White,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = muted ? 0.5 : 1,
        });
        text.Children.Add(new TextBlock
        {
            Text = layer.Detail,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB4)),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var row = new Border
        {
            Child = grid,
            Background = chosen ? ChosenBack : RowBack,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(layer.Mix?.Clip == true ? 16 : 0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Tag = layer,
        };

        row.MouseLeftButtonUp += (_, _) =>
        {
            if (layer.Target is { } target) Chosen?.Invoke(target);
        };

        return row;
    }

    // ------------------------------------------------------------ Ebenen im Graphen

    /// <summary>
    /// Die Ebenen eines Graphen: jedes Mischen, das zur Ausgabe beitraegt, oben das
    /// zuletzt gerechnete - die Reihenfolge des Ebenenstreifens.
    /// </summary>
    public static IReadOnlyList<NodeLayer> Of(NodeGraph graph)
    {
        var order = graph.Order() ?? Array.Empty<Node>();
        var layers = new List<NodeLayer>();

        foreach (var mix in order.OfType<MixNode>().Reverse())
        {
            var link = graph.Into(mix.Id, "Oben");
            var source = link is null ? null : graph.Find(link.From);

            string name = source is null ? Strings.T("S_NodeLayerNothing") : NameOf(graph, source, link!.Output);

            string detail = Strings.T(NodeTitles.BlendKey(mix.Mode)) + " · " +
                            (mix.Opacity * 100).ToString("0", CultureInfo.CurrentCulture) + " %" +
                            (mix.Clip ? " · " + Strings.T("S_NodeLayerClipped") : "");

            layers.Add(new NodeLayer(mix, source, name, detail));
        }

        if (Base(graph, order) is var (baseNode, baseOutput))
            layers.Add(new NodeLayer(null, baseNode, NameOf(graph, baseNode, baseOutput), Strings.T("S_NodeLayerBase")));

        return layers;
    }

    /// <summary>
    /// Die Grundlage: was unter dem untersten Mischen liegt - oder, wenn nichts gemischt
    /// wird, was in die Ausgabe fliesst. Die schwarze Leinwand, auf der der Stapel
    /// beginnt, ist keine; dann ist die unterste Ebene schon die Grundlage.
    /// </summary>
    private static (Node Node, string Output)? Base(NodeGraph graph, IReadOnlyList<Node> order)
    {
        var bottom = order.OfType<MixNode>().FirstOrDefault(m => !m.Clip);

        NodeLink? link = bottom is not null ? graph.Into(bottom.Id, "Unten")
                       : graph.Output is { } output ? graph.Into(output.Id, "Bild")
                       : null;

        for (int guard = 0; link is not null && guard < 64; guard++)
        {
            if (graph.Find(link.From) is not { } node) return null;

            switch (node)
            {
                case BlackNode or MixNode:
                    return null;
                case RenderNode or PictureNode:
                    return (node, link.Output);
            }

            var (input, _) = NodeEdits.Through(node);
            var next = input is null ? null : graph.Into(node.Id, input);

            if (next is null) return (node, NodeEdits.Through(node).Output ?? "Bild");

            link = next;
        }

        return null;
    }

    /// <summary>
    /// Wie eine Ebene heisst: nach dem, woher ihr Bild kommt - den Bildweg hinauf bis zu
    /// einer Quelle. Ein Pass heisst wie in der Datei, eine Bilddatei wie ihre Datei.
    /// </summary>
    private static string NameOf(NodeGraph graph, Node node, string output)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            switch (node)
            {
                case RenderNode:
                    return output == RenderNode.Picture ? Strings.T("S_NodeRender") : NodeTitles.Socket(output);
                case PictureNode:
                case LayerGradeNode { Adjustment: true }:
                case BlackNode:
                case ColorRampNode:
                    return NodeTitles.For(node);
                case MixNode:
                    return Strings.T("S_NodeLayerGroup");
            }

            var (input, _) = NodeEdits.Through(node);

            if (input is null || graph.Into(node.Id, input) is not { } link || graph.Find(link.From) is not { } from)
                return NodeTitles.For(node);

            node = from;
            output = link.Output;
        }

        return NodeTitles.For(node);
    }
}
