using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Nodes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Der Knoteneditor: der Graph ueber dem abgedunkelten Bild, wie der Compositor in
/// Blender mit seinem Hintergrundbild.
///
/// Gezeichnet statt aus Elementen gebaut. Ein Graph aus dreissig Knoten mit je fuenf
/// Anschluessen und Beschriftungen waeren sonst einige hundert WPF-Elemente, die beim
/// Verschieben alle ihre Lage neu vermessen - gezeichnet ist es ein Durchgang, und das
/// Verschieben bleibt fluessig.
///
/// Bedienung: Knoten anklicken waehlt, ziehen verschiebt. Ziehen auf freier Flaeche oder
/// mit der mittleren Taste schiebt die Ansicht, das Mausrad zoomt um den Zeiger, Pos1
/// zeigt alles. M schaltet den gewaehlten Knoten stumm. Rechtsklick bricht ein Ziehen ab
/// - wie an den Reglern.
///
/// Verbindungen werden in dieser Stufe nur gezeigt; umstecken kommt mit der naechsten.
/// </summary>
public sealed class NodeEditor : FrameworkElement
{
    // ------------------------------------------------------------ Masse (Graphkoordinaten)

    // Dieselben Masse wie in der Anordnung - sonst stuenden Knoten uebereinander,
    // die dort nebeneinander geplant waren.
    public const double NodeWidth = NodeLayout.Width;
    public const double Header = NodeLayout.Header;
    public const double Row = NodeLayout.Row;
    public const double Pad = NodeLayout.Pad;

    private NodeGraph? _graph;

    public NodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;

        SizeChanged += (_, _) =>
        {
            if (_frameOnSize && ActualWidth > 0) Frame();
        };
    }

    /// <summary>Der gezeigte Graph. Beim Setzen wird alles ins Bild geholt.</summary>
    public NodeGraph? Graph
    {
        get => _graph;
        set
        {
            _graph = value;
            _texts.Clear();
            Select(null);
            Frame();
        }
    }

    /// <summary>Wie ein Knoten heisst - von der Seite geliefert, damit er in der Sprache der Oberflaeche heisst.</summary>
    public Func<Node, string>? Title { get; set; }

    /// <summary>Wie ein Anschluss heisst.</summary>
    public Func<string, string>? SocketTitle { get; set; }

    /// <summary>Der gewaehlte Knoten - oder keiner.</summary>
    public Node? Selected { get; private set; }

    public event Action<Node?>? SelectionChanged;

    /// <summary>Knoten wurden verschoben - die Lage gehoert in die Einstellungen.</summary>
    public event Action? LayoutChanged;

    /// <summary>Am Graphen selbst hat sich etwas geaendert, das neu gerechnet werden muss.</summary>
    public event Action? GraphChanged;

    // ------------------------------------------------------------ Ansicht

    /// <summary>Vergroesserung: Punkte auf dem Schirm je Einheit im Graphen.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>Wo der Ursprung des Graphen auf dem Schirm liegt.</summary>
    public Vector Pan { get; private set; }

    public const double MinZoom = 0.2;
    public const double MaxZoom = 3;

    public Point ToScreen(Point graph) => new(graph.X * Zoom + Pan.X, graph.Y * Zoom + Pan.Y);

    public Point ToGraph(Point screen) => new((screen.X - Pan.X) / Zoom, (screen.Y - Pan.Y) / Zoom);

    private bool _frameOnSize;

    /// <summary>
    /// Holt alle Knoten ins Bild - gross genug zum Lesen, klein genug, dass alles
    /// darauf passt.
    /// </summary>
    public void Frame()
    {
        if (_graph is null || _graph.Nodes.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _frameOnSize = true;
            InvalidateVisual();
            return;
        }

        _frameOnSize = false;

        var all = _graph.Nodes.Select(Bounds).Aggregate(Rect.Union);
        const double margin = 40;

        double zoom = Math.Min((ActualWidth - 2 * margin) / Math.Max(1, all.Width),
                               (ActualHeight - 2 * margin) / Math.Max(1, all.Height));

        // Passt der ganze Graph nur noch so klein hinein, dass man nichts mehr lesen
        // kann, zeigt die Ansicht lieber einen lesbaren Ausschnitt - um die Ausgabe
        // herum, denn dort stehen die Effekte, an denen man am haeufigsten dreht. Wer
        // alles sehen will, zoomt heraus.
        const double Readable = 0.55;

        if (zoom < Readable && _graph.Output is { } output)
        {
            Zoom = Readable;
            var focus = Bounds(output);
            Pan = new Vector(ActualWidth - margin - focus.Right * Zoom,
                             ActualHeight / 2 - (focus.Y + focus.Height / 2) * Zoom);
        }
        else
        {
            Zoom = Math.Clamp(zoom, MinZoom, 1.2);
            Pan = new Vector(ActualWidth / 2 - (all.X + all.Width / 2) * Zoom,
                             ActualHeight / 2 - (all.Y + all.Height / 2) * Zoom);
        }

        _texts.Clear();
        InvalidateVisual();
    }

    /// <summary>Zoomt um einen Punkt auf dem Schirm - dieser Punkt bleibt stehen.</summary>
    public void ZoomAt(Point screen, double factor)
    {
        var anchor = ToGraph(screen);

        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        Pan = new Vector(screen.X - anchor.X * Zoom, screen.Y - anchor.Y * Zoom);

        _texts.Clear();
        InvalidateVisual();
    }

    // ------------------------------------------------------------ Geometrie

    /// <summary>Wo ein Knoten steht und wie gross er ist - in Graphkoordinaten.</summary>
    public static Rect Bounds(Node node) => new(node.X, node.Y, NodeWidth, NodeLayout.Height(node));

    public static Point InputAt(Node node, int index)
        => new(node.X, node.Y + Header + Pad + index * Row + Row / 2);

    public static Point OutputAt(Node node, int index)
        => new(node.X + NodeWidth, node.Y + Header + Pad + index * Row + Row / 2);

    /// <summary>Der oberste Knoten unter einem Punkt auf dem Schirm - oder keiner.</summary>
    public Node? NodeAt(Point screen)
    {
        if (_graph is null) return null;

        var at = ToGraph(screen);

        // Von oben nach unten: Der gewaehlte liegt zuoberst, danach die zuletzt gezeichneten.
        if (Selected is not null && Bounds(Selected).Contains(at)) return Selected;

        for (int i = _graph.Nodes.Count - 1; i >= 0; i--)
            if (Bounds(_graph.Nodes[i]).Contains(at)) return _graph.Nodes[i];

        return null;
    }

    public void Select(Node? node)
    {
        if (ReferenceEquals(Selected, node)) return;

        Selected = node;
        InvalidateVisual();
        SelectionChanged?.Invoke(node);
    }

    // ------------------------------------------------------------ Maus

    private enum Drag { None, Node, Pan }

    private Drag _drag;
    private Point _start;
    private Point _origin;
    private Vector _panOrigin;
    private bool _moved;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var at = e.GetPosition(this);

        // Rechts bricht einen laufenden Zug ab - der Knoten springt zurueck.
        if (e.ChangedButton == MouseButton.Right)
        {
            if (_drag == Drag.Node && Selected is not null)
            {
                Selected.X = _origin.X;
                Selected.Y = _origin.Y;
                _moved = false;
            }

            EndDrag();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            StartPan(at);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        var node = NodeAt(at);
        Select(node);

        if (node is null)
        {
            StartPan(at);
        }
        else
        {
            _drag = Drag.Node;
            _start = at;
            _origin = new Point(node.X, node.Y);
            _moved = false;
            CaptureMouse();
        }

        e.Handled = true;
    }

    private void StartPan(Point at)
    {
        _drag = Drag.Pan;
        _start = at;
        _panOrigin = Pan;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_drag == Drag.None) return;

        var at = e.GetPosition(this);
        var delta = at - _start;

        if (_drag == Drag.Pan)
        {
            Pan = _panOrigin + delta;
            InvalidateVisual();
            return;
        }

        if (Selected is null) return;

        // Erst ab ein paar Punkten ist es ein Zug - ein Klick zum Waehlen soll nichts verschieben.
        if (!_moved && Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3) return;

        _moved = true;
        Selected.X = Math.Round(_origin.X + delta.X / Zoom);
        Selected.Y = Math.Round(_origin.Y + delta.Y / Zoom);

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_drag == Drag.None) return;

        bool moved = _drag == Drag.Node && _moved;

        EndDrag();

        if (moved) LayoutChanged?.Invoke();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_drag != Drag.None) EndDrag();
    }

    private void EndDrag()
    {
        _drag = Drag.None;
        Cursor = null;

        if (IsMouseCaptured) ReleaseMouseCapture();

        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        ZoomAt(e.GetPosition(this), Math.Pow(1.1, e.Delta / 120.0));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Home:
                Frame();
                e.Handled = true;
                break;

            case Key.M when Selected is not null and not OutputNode:
                Selected.Muted = !Selected.Muted;
                InvalidateVisual();
                GraphChanged?.Invoke();
                e.Handled = true;
                break;

            case Key.Escape when _drag == Drag.Node && Selected is not null:
                Selected.X = _origin.X;
                Selected.Y = _origin.Y;
                _moved = false;
                EndDrag();
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------ Zeichnen

    /// <summary>Das Bild dahinter bleibt zu sehen, nur gedaempft - genug, um die Knoten zu lesen.</summary>
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x9A, 0x0C, 0x0C, 0x10)));

    private static readonly Brush Body = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x23, 0x23, 0x2A)));
    private static readonly Pen Outline = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46)), 1));
    private static readonly Pen Chosen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xA4, 0x7B, 0xF0)), 2));
    private static readonly Pen SocketRim = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), 1));
    private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)));
    private static readonly Brush Faint = Frozen(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB4)));

    private static readonly Brush ImageSocket = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)));
    private static readonly Brush ValueSocket = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA8)));
    private static readonly Brush DataSocket = Frozen(new SolidColorBrush(Color.FromRgb(0x8F, 0x7F, 0xE8)));

    private static readonly FontFamily Font = new("Segoe UI");

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Die Farbe eines Anschlusses - dieselbe Sprache wie in Blender.</summary>
    public static Brush SocketBrush(SocketType type) => type switch
    {
        SocketType.Value => ValueSocket,
        SocketType.Data => DataSocket,
        _ => ImageSocket,
    };

    /// <summary>Die Farbe des Kopfes: was fuer eine Art Knoten es ist.</summary>
    public static Color HeaderColour(Node node) => node switch
    {
        RenderNode or PictureNode or BlackNode => Color.FromRgb(0x4B, 0x50, 0x5E),
        MaskNode => Color.FromRgb(0x68, 0x4E, 0x94),
        PlaceNode or ExposureTintNode or LayerGradeNode or RestrictNode or MixNode or FallbackNode
            => Color.FromRgb(0x2E, 0x6B, 0x60),
        ViewNode => Color.FromRgb(0x86, 0x66, 0x2A),
        OverlayNode => Color.FromRgb(0x2E, 0x6B, 0x60),
        OutputNode => Color.FromRgb(0x86, 0x3E, 0x3E),
        _ => Color.FromRgb(0x48, 0x57, 0xA6),
    };

    private readonly Dictionary<(string, double, Brush), FormattedText> _texts = new();

    private FormattedText Label(string text, double size, Brush brush)
    {
        var key = (text, Math.Round(size, 1), brush);

        if (_texts.TryGetValue(key, out var cached)) return cached;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                          new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                          size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _texts[key] = formatted;
        return formatted;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Dim, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_graph is null) return;

        foreach (var link in _graph.Links) DrawLink(dc, link);

        foreach (var node in _graph.Nodes)
            if (!ReferenceEquals(node, Selected)) DrawNode(dc, node);

        if (Selected is not null) DrawNode(dc, Selected);
    }

    private void DrawLink(DrawingContext dc, NodeLink link)
    {
        var from = _graph!.Find(link.From);
        var to = _graph.Find(link.To);
        if (from is null || to is null) return;

        int output = IndexOf(from.Outputs, link.Output);
        int input = IndexOf(to.Inputs, link.Input);
        if (output < 0 || input < 0) return;

        var a = ToScreen(OutputAt(from, output));
        var b = ToScreen(InputAt(to, input));

        double reach = Math.Max(30 * Zoom, Math.Abs(b.X - a.X) / 2);

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(a, isFilled: false, isClosed: false);
            context.BezierTo(new Point(a.X + reach, a.Y), new Point(b.X - reach, b.Y), b, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();

        var pen = new Pen(SocketBrush(from.Outputs[output].Type), Math.Max(1.2, 2 * Math.Min(1, Zoom)));
        pen.Freeze();

        dc.PushOpacity(from.Muted || to.Muted ? 0.35 : 0.85);
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    private static int IndexOf(IReadOnlyList<Socket> sockets, string name)
    {
        for (int i = 0; i < sockets.Count; i++)
            if (sockets[i].Name == name) return i;

        return -1;
    }

    private void DrawNode(DrawingContext dc, Node node)
    {
        var bounds = Bounds(node);
        var topLeft = ToScreen(bounds.TopLeft);
        var box = new Rect(topLeft, new Size(bounds.Width * Zoom, bounds.Height * Zoom));

        // Ausserhalb des Sichtbaren wird nichts gezeichnet - bei einem grossen Graphen
        // ist das der groesste Teil.
        if (box.Right < 0 || box.Bottom < 0 || box.Left > ActualWidth || box.Top > ActualHeight) return;

        double radius = 5 * Zoom;
        bool selected = ReferenceEquals(node, Selected);

        dc.PushOpacity(node.Muted ? 0.5 : 1);

        dc.DrawRoundedRectangle(Body, selected ? Chosen : Outline, box, radius, radius);

        // Der Kopf: oben gerundet, unten gerade - ein zweites Rechteck deckt die untere Rundung.
        var head = new Rect(box.X, box.Y, box.Width, Header * Zoom);
        var headBrush = new SolidColorBrush(HeaderColour(node));
        headBrush.Freeze();

        dc.DrawRoundedRectangle(headBrush, null, head, radius, radius);
        dc.DrawRectangle(headBrush, null, new Rect(head.X, head.Y + head.Height / 2, head.Width, head.Height / 2));

        if (selected) dc.DrawRoundedRectangle(null, Chosen, box, radius, radius);

        bool labels = Zoom >= 0.45;

        if (labels)
        {
            string title = Title?.Invoke(node) ?? node.GetType().Name;
            if (node.Muted) title += " ⊘";

            var text = Label(title, 12 * Zoom, Text);
            text.MaxTextWidth = Math.Max(1, box.Width - 14 * Zoom);
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;

            dc.DrawText(text, new Point(box.X + 8 * Zoom, box.Y + (Header * Zoom - text.Height) / 2));
        }

        double socket = 4.5 * Zoom;

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            var at = ToScreen(InputAt(node, i));
            dc.DrawEllipse(SocketBrush(node.Inputs[i].Type), SocketRim, at, socket, socket);

            if (!labels) continue;

            var text = Label(SocketTitle?.Invoke(node.Inputs[i].Name) ?? node.Inputs[i].Name, 10.5 * Zoom, Faint);
            dc.DrawText(text, new Point(at.X + 9 * Zoom, at.Y - text.Height / 2));
        }

        for (int i = 0; i < node.Outputs.Count; i++)
        {
            var at = ToScreen(OutputAt(node, i));
            dc.DrawEllipse(SocketBrush(node.Outputs[i].Type), SocketRim, at, socket, socket);

            if (!labels) continue;

            var text = Label(SocketTitle?.Invoke(node.Outputs[i].Name) ?? node.Outputs[i].Name, 10.5 * Zoom, Faint);
            dc.DrawText(text, new Point(at.X - 9 * Zoom - text.Width, at.Y - text.Height / 2));
        }

        dc.Pop();
    }
}
