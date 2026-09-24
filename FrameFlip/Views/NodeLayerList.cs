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
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>Eine Ebene im Graphen: das Mischen, das sie auf das Bisherige legt, und woher ihr Bild kommt.</summary>
/// <param name="Mix">Das Mischen - bei der Grundlage keines: Sie wird auf nichts gelegt.</param>
/// <param name="Source">Der Knoten, der in "Oben" fliesst - seine Vorschau ist die Miniatur der Ebene.</param>
public sealed record NodeLayer(MixNode? Mix, Node? Source, string Name, string Detail)
{
    /// <summary>Ein Wasserzeichen obenauf - statt eines Mischens.</summary>
    public OverlayNode? Overlay { get; init; }

    /// <summary>Wie tief die Ebene in Gruppen steckt - 0 ganz aussen.</summary>
    public int Depth { get; init; }

    /// <summary>Welcher Knoten gewaehlt wird, wenn man die Ebene anklickt.</summary>
    public Node? Target => (Node?)Mix ?? (Node?)Overlay ?? Source;

    /// <summary>Der Knoten, den das Auge stummschaltet - bei der Grundlage keiner.</summary>
    public Node? Switch => (Node?)Mix ?? Overlay;

    /// <summary>Was in den Faktor des Mischens fliesst - die Maske der Ebene, wenn sie eine hat.</summary>
    public Node? MaskSource { get; init; }

    /// <summary>
    /// Woher das Bild der Ebene letztlich kommt - eine Bilddatei oder ein Ausgang der
    /// Datei. Fuer die Miniatur einer ausgeblendeten Ebene: Ihr Zweig wird nicht
    /// gerechnet, ihr Bild laesst sich trotzdem lesen.
    /// </summary>
    public (Node Node, string Output)? Origin { get; init; }

    /// <summary>Die Kette, in der die Ebene liegt - null, wenn sie in keiner liegt und sich nicht verschieben laesst.</summary>
    public LayerChain? Chain { get; init; }

    /// <summary>Die Kette in ihr: die Kinder einer Gruppe oder was an einen Traeger geschnitten ist.</summary>
    public LayerChain? Inner { get; init; }

    /// <summary>Ob die Ebene an einen Traeger geschnitten ist.</summary>
    public bool Clipped => Chain?.Kind == LayerChainKind.Clip;
}

/// <summary>
/// Die Ebenen im Knotenmodus - im Reiter, in dem sonst der Ebenenstreifen steht.
///
/// Im Graphen ist eine Ebene kein Eintrag, sondern ein Zweig, der in ein Mischen
/// muendet; bei zehn Ebenen sucht man den richtigen Zweig. Die Liste zeigt jedes
/// Mischen als Zeile, oben das zuletzt gemischte, mit einer Miniatur dessen, was
/// hineinfliesst. Ein Klick waehlt den Knoten und rueckt ihn ins Bild, das Auge
/// schaltet die Ebene stumm.
///
/// Und man arbeitet in ihr wie im Ebenenstreifen: Zeilen ziehen, hinzufuegen,
/// verdoppeln, loeschen, Mischung und Deckkraft der gewaehlten Ebene. Was dabei am
/// Graphen geschieht, steht in <see cref="LayerEdits"/>; die Liste sagt nur, was
/// gewuenscht ist.
/// </summary>
public sealed class NodeLayerList : Border
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _empty;

    /// <summary>Eine Ebene wurde angeklickt - ihr Mischen, bei der Grundlage ihr Knoten.</summary>
    public event Action<Node>? Chosen;

    /// <summary>Das Auge einer Ebene wurde angeklickt - ihr Mischen oder ihr Wasserzeichen.</summary>
    public event Action<Node>? MuteWanted;

    /// <summary>Die fehlenden ausgeblendeten Ebenen sollen in den Graphen - oder, wenn das nicht geht, der Graph neu.</summary>
    public event Action<bool>? MissingWanted;

    /// <summary>Eine Zeile wurde gezogen: die Ebene ueber (true) oder unter eine andere.</summary>
    public event Action<Node, Node, bool>? MoveWanted;

    /// <summary>Die gewaehlte Ebene einen Platz hoeher (true) oder tiefer.</summary>
    public event Action<Node, bool>? StepWanted;

    public event Action<Node>? RemoveWanted;

    public event Action<Node>? DuplicateWanted;

    /// <summary>Der Knopf zum Hinzufuegen - wer das Menue kennt, klappt es an ihm auf.</summary>
    public event Action<FrameworkElement>? AddWanted;

    public event Action<Node, BlendMode>? ModeWanted;

    /// <summary>Die Deckkraft der gewaehlten Ebene - mit true, solange noch gezogen wird.</summary>
    public event Action<Node, float, bool>? OpacityWanted;

    /// <summary>Ob sich eine Ebene neben eine andere legen laesst - gefragt, waehrend gezogen wird.</summary>
    public Func<Node, Node, bool>? CanMove { get; set; }

    private readonly Border _missing;
    private readonly TextBlock _missingText;
    private readonly Button _missingButton;
    private bool _adoptable;

    private readonly Canvas _dropCanvas = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroller;
    private readonly Border _dropLine;

    private readonly Button _add, _duplicate, _remove, _up, _down;

    private readonly StackPanel _controls = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _opacity = new() { Minimum = 0, Maximum = 1, SmallChange = 0.01, LargeChange = 0.05 };
    private readonly TextBlock _opacityValue = new();
    private bool _filling;

    /// <summary>Die gewaehlte Zeile - auf sie wirken die Knoepfe und die Regler.</summary>
    private NodeLayer? _chosen;

    /// <summary>Welche Art Zug in einer Zeile - die Kennung, nicht die Ebene; siehe den Ebenenstreifen.</summary>
    private const string RowFormat = "FrameFlip.NodeLayerRow";

    private NodeLayer? _pressed;
    private NodeLayer? _dragging;
    private Point _pressAt;

    public NodeLayerList()
    {
        Padding = new Thickness(10, 8, 10, 8);

        var muted = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB4));

        _empty = new TextBlock
        {
            Text = Strings.T("S_NodeLayersEmpty"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(2, 4, 2, 4),
        };

        var head = new StackPanel();
        head.Children.Add(new TextBlock
        {
            Text = Strings.T("S_NodeLayersHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(2, 0, 2, 8),
        });

        _missingText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6),
        };

        _missingButton = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 11 };
        _missingButton.Click += (_, _) => MissingWanted?.Invoke(_adoptable);

        var missingPanel = new StackPanel();
        missingPanel.Children.Add(_missingText);
        missingPanel.Children.Add(_missingButton);

        _missing = new Border
        {
            Child = missingPanel,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xA4, 0x7B, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xA4, 0x7B, 0xF0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        head.Children.Add(_missing);

        // Der Strich liegt ueber den Zeilen und faengt keine Maus - er zeigt beim Ziehen,
        // wo die Zeile landen wuerde, eingerueckt wie die Kette, in die sie kaeme.
        _dropLine = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromRgb(0xA4, 0x7B, 0xF0)),
            Visibility = Visibility.Collapsed,
        };
        _dropCanvas.Children.Add(_dropLine);

        var rowsArea = new Grid();
        rowsArea.Children.Add(_rows);
        rowsArea.Children.Add(_dropCanvas);

        var body = new StackPanel();
        body.Children.Add(rowsArea);
        body.Children.Add(_empty);

        var scroller = _scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            AllowDrop = true,
        };

        scroller.DragOver += OnRowDragOver;
        scroller.DragLeave += (_, _) => HideDropLine();
        scroller.Drop += OnRowDrop;

        // Die Leiste unter der Liste - dieselben Zeichen wie im Ebenenstreifen.
        _add = Tool("+", "S_NodeLayerAdd");
        _duplicate = Tool("❐", "S_NodeLayerDuplicate");
        _remove = Tool("✕", "S_NodeLayerRemove");
        _up = Tool("▲", "S_MoveLayerUp");
        _down = Tool("▼", "S_MoveLayerDown");

        _add.Click += (_, _) => AddWanted?.Invoke(_add);
        _duplicate.Click += (_, _) => { if (_chosen?.Switch is { } layer) DuplicateWanted?.Invoke(layer); };
        _remove.Click += (_, _) => { if (_chosen?.Switch is { } layer) RemoveWanted?.Invoke(layer); };
        _up.Click += (_, _) => { if (_chosen?.Switch is { } layer) StepWanted?.Invoke(layer, true); };
        _down.Click += (_, _) => { if (_chosen?.Switch is { } layer) StepWanted?.Invoke(layer, false); };

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_add);
        left.Children.Add(_duplicate);
        left.Children.Add(_remove);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_up);
        right.Children.Add(_down);

        var toolbar = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        toolbar.Children.Add(left);
        toolbar.Children.Add(right);

        BuildControls();

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(head, 0);
        Grid.SetRow(scroller, 1);
        Grid.SetRow(toolbar, 2);
        Grid.SetRow(_controls, 3);

        layout.Children.Add(head);
        layout.Children.Add(scroller);
        layout.Children.Add(toolbar);
        layout.Children.Add(_controls);

        Child = layout;
        ShowTools();
    }

    private static Button Tool(string glyph, string tipKey)
    {
        var button = new Button { Content = glyph, ToolTip = Strings.T(tipKey) };
        button.SetResourceReference(StyleProperty, "StripButton");
        return button;
    }

    /// <summary>Mischung und Deckkraft der gewaehlten Ebene - wie unter dem Ebenenstreifen.</summary>
    private void BuildControls()
    {
        var label = new TextBlock { Text = Strings.T("S_BlendMode"), Margin = new Thickness(0, 0, 0, 4) };
        label.SetResourceReference(StyleProperty, "PanelLabel");

        _mode.SetResourceReference(StyleProperty, "OverlayCombo");

        foreach (var (mode, key) in Blending.All)
        {
            var item = new ComboBoxItem { Content = Strings.T(key), Tag = mode };
            item.SetResourceReference(StyleProperty, "OverlayComboItem");
            _mode.Items.Add(item);
        }

        _mode.SelectionChanged += (_, _) =>
        {
            if (_filling || _chosen?.Switch is not { } layer || _mode.SelectedItem is not ComboBoxItem { Tag: BlendMode mode }) return;
            ModeWanted?.Invoke(layer, mode);
        };

        var opacityHead = new Grid { Margin = new Thickness(0, 10, 0, 2) };
        var opacityLabel = new TextBlock { Text = Strings.T("S_Opacity") };
        opacityLabel.SetResourceReference(StyleProperty, "PanelLabel");
        _opacityValue.SetResourceReference(StyleProperty, "PanelValue");
        opacityHead.Children.Add(opacityLabel);
        opacityHead.Children.Add(_opacityValue);

        _opacity.SetResourceReference(StyleProperty, "PanelSlider");

        _opacity.ValueChanged += (_, e) =>
        {
            ShowOpacity(e.NewValue);
            if (_filling || _chosen?.Switch is not { } layer) return;
            OpacityWanted?.Invoke(layer, (float)e.NewValue, true);
        };

        // Losgelassen: Jetzt darf der Rest nachziehen - der Farbstreifen, der dasselbe Mischen zeigt.
        _opacity.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_chosen?.Switch is { } layer) OpacityWanted?.Invoke(layer, (float)_opacity.Value, false);
        };

        _opacity.MouseDoubleClick += (_, _) => _opacity.Value = 1;

        _controls.Children.Add(label);
        _controls.Children.Add(_mode);
        _controls.Children.Add(opacityHead);
        _controls.Children.Add(_opacity);
    }

    private void ShowOpacity(double value)
        => _opacityValue.Text = (value * 100).ToString("0", CultureInfo.CurrentCulture) + " %";

    /// <summary>Welche Knoepfe gerade etwas tun, und die Regler der gewaehlten Ebene.</summary>
    private void ShowTools()
    {
        var layer = _chosen?.Switch;
        var chain = _chosen?.Chain;
        int at = layer is null || chain is null ? -1 : chain.Members.IndexOf(layer);

        _duplicate.IsEnabled = at >= 0;
        _remove.IsEnabled = at >= 0;
        _up.IsEnabled = at >= 0 && at + 1 < chain!.Members.Count;
        _down.IsEnabled = at > 0;

        var (mode, opacity) = layer switch
        {
            MixNode mix => (mix.Mode, mix.Opacity),
            OverlayNode overlay => (overlay.Mode, overlay.Opacity),
            _ => ((BlendMode?)null, 0f),
        };

        _controls.Visibility = mode is null ? Visibility.Collapsed : Visibility.Visible;
        if (mode is null) return;

        _filling = true;

        try
        {
            _mode.SelectedItem = _mode.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (BlendMode)i.Tag == mode);

            // Waehrend gezogen wird, steht der Regler schon dort - ihn neu zu setzen liesse ihn zucken.
            if (!_opacity.IsMouseCaptureWithin) _opacity.Value = opacity;

            ShowOpacity(_opacity.Value);
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>Die Zeilen, wie sie gerade dastehen - fuer die Probe.</summary>
    internal IReadOnlyList<NodeLayer> Shown { get; private set; } = Array.Empty<NodeLayer>();

    /// <summary>Die Regler der gewaehlten Ebene - fuer die Probe.</summary>
    internal (ComboBox Mode, Slider Opacity, FrameworkElement Panel) Controls => (_mode, _opacity, _controls);

    /// <summary>Welche ausgeblendeten Ebenen des Stapels im Graphen fehlen, wie der Hinweis sie nennt - fuer die Probe.</summary>
    internal IReadOnlyList<string> Missing { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Nennt ausgeblendete Ebenen des Stapels, die dem Graphen fehlen - er wurde umgewandelt,
    /// bevor sie mitkamen. <paramref name="adoptable"/>: Sie lassen sich hineinsetzen; sonst
    /// bleibt nur, den Graphen neu aufzubauen.
    /// </summary>
    public void ShowMissing(IReadOnlyList<string> names, bool adoptable)
    {
        Missing = names;
        _adoptable = adoptable;

        if (names.Count == 0)
        {
            _missing.Visibility = Visibility.Collapsed;
            return;
        }

        _missingText.Text = Strings.T(adoptable ? "S_NodeLayersMissing" : "S_NodeLayersMissingRebuild",
                                      string.Join(", ", names.Select(n => n.Length > 0 ? n : "?")));
        _missingButton.Content = Strings.T(adoptable ? "S_NodeLayersAdopt" : "S_NodeMenuRebuild");

        if (TryFindResource("OverlayButton") is Style style) _missingButton.Style = style;

        _missing.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Zeigt die Ebenen eines Graphen. <paramref name="picture"/> liefert die Miniatur einer
    /// Ebene, <paramref name="mask"/> die ihrer Maske.
    /// </summary>
    public void Show(IReadOnlyList<NodeLayer> layers, Node? selected,
                     Func<NodeLayer, ImageSource?> picture, Func<NodeLayer, ImageSource?> mask)
    {
        Shown = layers;
        _chosen = layers.FirstOrDefault(l => selected is not null && ReferenceEquals(selected, l.Target));
        _rows.Children.Clear();
        _empty.Visibility = layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var layer in layers)
            _rows.Children.Add(Row(layer, ReferenceEquals(layer, _chosen), picture(layer),
                                   layer.MaskSource is null ? null : mask(layer)));

        ShowTools();
    }

    private static readonly Brush ChosenBack = new SolidColorBrush(Color.FromArgb(0x55, 0xA4, 0x7B, 0xF0));
    private static readonly Brush RowBack = new SolidColorBrush(Color.FromArgb(0x30, 0x23, 0x23, 0x2A));

    /// <summary>Wie weit eine Zeile eingerueckt ist - eine Gruppe tiefer, angeschnitten noch einmal.</summary>
    private static double Indent(NodeLayer layer) => 16 * layer.Depth + (layer.Clipped ? 16 : 0);

    private UIElement Row(NodeLayer layer, bool chosen, ImageSource? thumb, ImageSource? maskThumb)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool muted = layer.Switch?.Muted == true;

        // Die Grundlage hat kein Mischen, das man stummschalten koennte.
        if (layer.Switch is { } mix)
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
            Child = new Image { Source = thumb, Stretch = Stretch.Uniform },
        };

        Grid.SetColumn(picture, 1);
        grid.Children.Add(picture);

        // Die Maske daneben, wie in einem Ebenenstapel: klein und grau.
        if (layer.MaskSource is not null)
        {
            var mask = new Border
            {
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Strings.T("S_NodeLayerMask"),
                Child = new Image { Source = maskThumb, Stretch = Stretch.UniformToFill },
            };

            Grid.SetColumn(mask, 2);
            grid.Children.Add(mask);
        }

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

        Grid.SetColumn(text, 3);
        grid.Children.Add(text);

        var row = new Border
        {
            Child = grid,
            Background = chosen ? ChosenBack : RowBack,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(Indent(layer), 0, 0, 3),
            Cursor = Cursors.Hand,
            Tag = layer,
        };

        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Wer auf das Auge drueckt, will es umlegen und nicht die Zeile ziehen.
            _pressed = Inside<ButtonBase>(e.OriginalSource as DependencyObject) ? null : layer;
            _pressAt = e.GetPosition(this);
        };

        row.MouseMove += (_, e) => BeginDrag(e);

        row.MouseLeftButtonUp += (_, _) =>
        {
            _pressed = null;
            if (layer.Target is { } target) Chosen?.Invoke(target);
        };

        return row;
    }

    private static bool Inside<T>(DependencyObject? source) where T : DependencyObject
    {
        for (; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is T) return true;

        return false;
    }

    // ------------------------------------------------------------ Ziehen

    /// <summary>
    /// Ein Zug beginnt erst nach einer Mindeststrecke - sonst verschoebe man Ebenen,
    /// waehrend man sie nur waehlen wollte. Gezogen wird nur, was in einer Kette liegt.
    /// </summary>
    private void BeginDrag(MouseEventArgs e)
    {
        if (_pressed is not { Switch: not null, Chain: not null } pressed || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);

        if (Math.Abs(now.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragging = pressed;
        _pressed = null;

        try
        {
            DragDrop.DoDragDrop(this, new DataObject(RowFormat, RowFormat), DragDropEffects.Move);
        }
        finally
        {
            _dragging = null;
            HideDropLine();
        }
    }

    /// <summary>
    /// Wohin eine gezogene Zeile kaeme, wenn sie an dieser Stelle der Liste losgelassen wird:
    /// neben welche Ebene, darueber oder darunter. Die obere Haelfte einer Zeile heisst
    /// darueber. Die untere heisst darunter - ausser bei einer Gruppe oder einem Traeger,
    /// deren Kinder direkt folgen: Dort heisst sie "zuoberst hinein", denn genau dort
    /// steht der Strich. Ueber der Grundlage heisst: ganz unten in die Ebenen.
    /// </summary>
    internal (Node Target, bool Above, NodeLayer Row, bool Lower)? DropAt(NodeLayer over, bool upper)
    {
        if (over.Switch is null)
        {
            // Die Grundlage: darueber ist ganz unten in der Hauptkette.
            var bottom = Shown.FirstOrDefault(l => l.Chain is { Kind: LayerChainKind.Main });
            return upper && bottom?.Chain?.Members.FirstOrDefault() is { } first ? (first, false, over, false) : null;
        }

        if (over.Chain is null) return null;

        if (upper) return (over.Switch, true, over, false);

        if (over.Inner is { Members.Count: > 0 } inner) return (inner.Members[^1], true, over, true);

        return (over.Switch, false, over, true);
    }

    /// <summary>Die Zeile unter dem Zeiger - und ob er in ihrer oberen Haelfte steht.</summary>
    private (NodeLayer Layer, FrameworkElement Row, bool Upper)? RowUnder(DragEventArgs e)
    {
        foreach (FrameworkElement row in _rows.Children)
        {
            var at = e.GetPosition(row);
            if (at.Y < 0 || at.Y > row.ActualHeight + 3) continue;

            return row.Tag is NodeLayer layer ? (layer, row, at.Y < row.ActualHeight / 2) : null;
        }

        return null;
    }

    private (Node Target, bool Above)? Landing(DragEventArgs e, out FrameworkElement? row, out bool lower, out NodeLayer? over)
    {
        row = null;
        lower = false;
        over = null;

        if (_dragging?.Switch is not { } moved || !e.Data.GetDataPresent(RowFormat) || RowUnder(e) is not var (layer, element, upper))
            return null;

        if (Allowed(moved, layer, upper) is not var (target, above, isLower)) return null;

        row = element;
        lower = isLower;
        over = layer;
        return (target, above);
    }

    /// <summary>Wohin eine Ebene kaeme, wenn sie hier losgelassen wird - und nur, wenn sie dorthin darf.</summary>
    private (Node Target, bool Above, bool Lower)? Allowed(Node moved, NodeLayer over, bool upper)
    {
        if (DropAt(over, upper) is not var (target, above, _, lower)) return null;
        if (ReferenceEquals(target, moved) || CanMove?.Invoke(moved, target) != true) return null;

        return (target, above, lower);
    }

    /// <summary>Wie ein Loslassen ueber einer Zeile, in ihrer oberen oder unteren Haelfte - fuer die Probe.</summary>
    internal bool DropOn(Node moved, NodeLayer over, bool upper)
    {
        if (Allowed(moved, over, upper) is not var (target, above, _)) return false;

        MoveWanted?.Invoke(moved, target, above);
        return true;
    }

    /// <summary>Die Knoepfe unter der Liste - fuer die Probe.</summary>
    internal (Button Add, Button Duplicate, Button Remove, Button Up, Button Down) Buttons => (_add, _duplicate, _remove, _up, _down);

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(RowFormat)) return;

        e.Handled = true;

        // Am Rand rollt die Liste mit - eine lange Liste liesse sich sonst nur bis zum Rand ordnen.
        double y = e.GetPosition(_scroller).Y;
        if (y < 18) _scroller.LineUp();
        else if (y > _scroller.ActualHeight - 18) _scroller.LineDown();

        if (Landing(e, out var row, out bool lower, out var over) is not var (target, _) || row is null || over is null)
        {
            e.Effects = DragDropEffects.None;
            HideDropLine();
            return;
        }

        e.Effects = DragDropEffects.Move;

        // Eingerueckt wie die Ebene, neben die sie kaeme - so sieht man, ob sie in die
        // Gruppe geht oder unter sie.
        var top = row.TranslatePoint(new Point(0, 0), _rows);
        double indent = Indent(Shown.FirstOrDefault(l => ReferenceEquals(l.Switch, target)) ?? over);

        _dropLine.Width = Math.Max(20, _rows.ActualWidth - indent);
        Canvas.SetLeft(_dropLine, indent);
        Canvas.SetTop(_dropLine, lower ? top.Y + row.ActualHeight + 0.5 : top.Y - 2);
        _dropLine.Visibility = Visibility.Visible;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(RowFormat)) return;

        e.Handled = true;
        var moved = _dragging?.Switch;
        var landing = Landing(e, out _, out _, out _);

        HideDropLine();

        if (moved is not null && landing is var (target, above)) MoveWanted?.Invoke(moved, target, above);
    }

    private void HideDropLine() => _dropLine.Visibility = Visibility.Collapsed;

    // ------------------------------------------------------------ Ebenen im Graphen

    /// <summary>
    /// Die Ebenen eines Graphen, oben das zuletzt gemischte - die Reihenfolge des
    /// Ebenenstreifens. Gelesen aus den Ketten und nicht aus der Rechenreihenfolge: Die
    /// haengt davon ab, in welcher Folge Kabel gesteckt wurden, und nach dem ersten
    /// Verschieben stuende eine Gruppe nicht mehr ueber ihren Kindern.
    ///
    /// Unter einer Gruppe stehen ihre Kinder, unter einem Traeger was an ihn geschnitten
    /// ist - eingerueckt. Mischen, die in keiner Kette liegen, stehen am Ende.
    /// </summary>
    public static IReadOnlyList<NodeLayer> Of(NodeGraph graph)
    {
        var order = graph.Order() ?? Array.Empty<Node>();
        var chains = LayerEdits.Chains(graph);
        var inner = new Dictionary<string, LayerChain>(StringComparer.Ordinal);

        foreach (var chain in chains)
            if (chain.Parent is { } parent) inner.TryAdd(parent.Id, chain);

        var layers = new List<NodeLayer>();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        // Obenauf liegt, was zuletzt aufgetragen wird - die Wasserzeichen nach der Bildwerdung.
        foreach (var overlay in order.OfType<OverlayNode>().Reverse())
        {
            var link = graph.Into(overlay.Id, "Ebene");
            var source = link is null ? null : graph.Find(link.From);

            string name = overlay.Label is { Length: > 0 } label ? label
                        : source is null ? Strings.T("S_NodeLayerNothing")
                        : NameOf(graph, source, link!.Output);

            string detail = Strings.T(NodeTitles.BlendKey(overlay.Mode)) + " · " +
                            (overlay.Opacity * 100).ToString("0", CultureInfo.CurrentCulture) + " % · " +
                            Strings.T("S_NodeLayerOnTop") +
                            (overlay.Muted ? " · " + Strings.T("S_NodeLayerHidden") : "");

            layers.Add(new NodeLayer(null, source, name, detail)
            {
                Overlay = overlay,
                Origin = source is null ? null : Origin(graph, source, link!.Output),
                Chain = LayerEdits.ChainOf(chains, overlay),
            });
        }

        void List(LayerChain chain)
        {
            for (int i = chain.Members.Count - 1; i >= 0; i--)
            {
                if (chain.Members[i] is not MixNode mix) continue;

                var sub = inner.GetValueOrDefault(mix.Id);

                layers.Add(Layer(graph, mix, chain, sub));
                listed.Add(mix.Id);

                if (sub is not null) List(sub);
            }
        }

        foreach (var main in chains.Where(c => c.Kind == LayerChainKind.Main)) List(main);

        foreach (var mix in order.OfType<MixNode>().Reverse())
            if (!listed.Contains(mix.Id)) layers.Add(Layer(graph, mix, null, null));

        if (Base(graph, order) is var (baseNode, baseOutput))
        {
            layers.Add(new NodeLayer(null, baseNode, NameOf(graph, baseNode, baseOutput), Strings.T("S_NodeLayerBase"))
            {
                Origin = (baseNode, baseOutput),
            });
        }

        return layers;
    }

    /// <summary>Die Zeile eines Mischens: Name, Mischung und Deckkraft, Maske und Quelle.</summary>
    private static NodeLayer Layer(NodeGraph graph, MixNode mix, LayerChain? chain, LayerChain? inner)
    {
        var link = graph.Into(mix.Id, "Oben");
        var source = link is null ? null : graph.Find(link.From);
        var origin = source is null ? null : Origin(graph, source, link!.Output);

        string name = mix.Label is { Length: > 0 } label ? label
                    : source is null ? Strings.T("S_NodeLayerNothing")
                    : NameOf(graph, source, link!.Output);

        string detail = Strings.T(NodeTitles.BlendKey(mix.Mode)) + " · " +
                        (mix.Opacity * 100).ToString("0", CultureInfo.CurrentCulture) + " %" +
                        (mix.Clip ? " · " + Strings.T("S_NodeLayerClipped") : "") +
                        (mix.Muted ? " · " + Strings.T("S_NodeLayerHidden") : "");

        var factor = graph.Into(mix.Id, "Faktor");

        return new NodeLayer(mix, source, name, detail)
        {
            MaskSource = factor is null ? null : graph.Find(factor.From),
            Origin = origin,
            Depth = chain?.Depth ?? 0,
            Chain = chain,
            Inner = inner,
        };
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

    /// <summary>Die Quelle am Anfang des Bildwegs - eine Bilddatei oder ein Ausgang der Datei. Sonst keine.</summary>
    internal static (Node Node, string Output)? Origin(NodeGraph graph, Node node, string output)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (node is RenderNode or PictureNode) return (node, output);

            var (input, _) = NodeEdits.Through(node);

            if (input is null || graph.Into(node.Id, input) is not { } link || graph.Find(link.From) is not { } from)
                return null;

            node = from;
            output = link.Output;
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
