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
/// zeigt alles. M schaltet den gewaehlten Knoten stumm, Entf nimmt ihn heraus und
/// schliesst die Luecke. Rechtsklick bricht ein Ziehen ab - wie an den Reglern - und
/// oeffnet sonst das Menue zum Hinzufuegen, wie Umschalt+A.
///
/// Verbinden wie in Blender: von einem Ausgang auf einen Eingang ziehen oder umgekehrt.
/// Ein Kabel, das man an seinem Eingang packt, loest sich und laesst sich woanders
/// anstecken oder ins Leere fallen lassen. Schon waehrend des Ziehens leuchten nur die
/// Anschluesse auf, an die es passt.
///
/// Ein Knoten, dessen Bildweg frei ist, faellt in ein Kabel, ueber dem er losgelassen
/// wird - das Kabel leuchtet vorher auf. Dasselbe gilt fuer einen Effekt, der aus der
/// Palette des Farbstreifens hereingezogen wird.
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

    /// <summary>Unter diesem Namen traegt ein Zug aus der Palette seinen Effekt.</summary>
    public const string SectionFormat = "FrameFlip.NodeSection";

    public NodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
        AllowDrop = true;

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

    /// <summary>Das kleine Bild eines Knotens mit Vorschau - oder keines, solange noch nichts gerechnet ist.</summary>
    public Func<Node, ImageSource?>? PreviewOf { get; set; }

    /// <summary>Am Knopf im Kopf eines Knotens wurde die Vorschau ein- oder ausgeschaltet.</summary>
    public event Action<Node>? PreviewToggled;

    /// <summary>Strg+Umschalt+Klick auf einen Knoten: Er soll in den Betrachter - oder sein naechster Ausgang.</summary>
    public event Action<Node>? ViewWanted;

    /// <summary>Was gerade im Betrachter steht - der Knoten ist markiert, der Ausgang benannt.</summary>
    public (Node Node, string Output)? Viewed
    {
        get => _viewed;
        set
        {
            _viewed = value;
            InvalidateVisual();
        }
    }

    private (Node Node, string Output)? _viewed;

    /// <summary>Der gewaehlte Knoten - oder keiner.</summary>
    public Node? Selected { get; private set; }

    public event Action<Node?>? SelectionChanged;

    /// <summary>Knoten wurden verschoben - die Lage gehoert in die Einstellungen.</summary>
    public event Action? LayoutChanged;

    /// <summary>Am Graphen selbst hat sich etwas geaendert, das neu gerechnet werden muss.</summary>
    public event Action? GraphChanged;

    /// <summary>
    /// Gleich aendert sich der Aufbau oder die Lage - jetzt ist der Moment, den Stand
    /// fuer "Rueckgaengig" festzuhalten.
    /// </summary>
    public event Action? Editing;

    /// <summary>Ein Menue zum Hinzufuegen ist gewuenscht - an dieser Stelle im Graphen.</summary>
    public event Action<Point>? MenuWanted;

    /// <summary>
    /// Ein Effekt aus der Palette wurde hereingezogen: welcher, wohin (die linke obere
    /// Ecke des Knotens im Graphen) und in welches Kabel er fallen soll - oder in keines.
    /// </summary>
    public event Action<string, Point, NodeLink?>? SectionDropped;

    public event Action? UndoWanted;

    public event Action? RedoWanted;

    /// <summary>Ein Satz oben links - etwa, dass die Ausgabe nicht verbunden ist. Null: keiner.</summary>
    public string? Warning
    {
        get => _warning;
        set
        {
            _warning = value;
            InvalidateVisual();
        }
    }

    private string? _warning;

    /// <summary>Uebersetzt die Begruendung, warum ein Kabel nicht passt - eine Kennung aus NodeEdits.</summary>
    public Func<string, string>? Translate { get; set; }

    /// <summary>
    /// Tauscht den Graphen aus, ohne die Ansicht zu verlassen - fuer Rueckgaengig. Der
    /// gewaehlte Knoten bleibt gewaehlt, wenn es ihn noch gibt.
    /// </summary>
    public void Replace(NodeGraph graph)
    {
        string? chosen = Selected?.Id;

        _graph = graph;
        _texts.Clear();
        Selected = null;

        Select(chosen is null ? null : graph.Find(chosen));
        InvalidateVisual();
    }

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

    /// <summary>
    /// Rueckt einen Knoten in die Mitte der Ansicht - lesbar gross, falls die Ansicht
    /// gerade weit herausgezoomt ist.
    /// </summary>
    public void Reveal(Node node)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        Zoom = Math.Max(Zoom, 0.8);

        var bounds = Bounds(node);
        Pan = new Vector(ActualWidth / 2 - (bounds.X + bounds.Width / 2) * Zoom,
                         ActualHeight / 2 - (bounds.Y + bounds.Height / 2) * Zoom);

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

    private enum Drag { None, Node, Pan, Wire }

    private Drag _drag;
    private Point _start;
    private Point _origin;
    private Vector _panOrigin;
    private bool _moved;

    // Ein Kabel im Zug: sein festes Ende, das freie Ende an der Maus, und - wenn es an
    // seinem Eingang gepackt wurde - die Verbindung, die es vorher war.
    private Node? _wireNode;
    private string? _wireSocket;
    private bool _wireFromInput;
    private NodeLink? _lifted;

    /// <summary>Der Anschluss, an dem das Kabel gepackt wurde - dort losgelassen, war es ein Klick.</summary>
    private (string Node, string Socket, bool Input)? _wirePressed;
    private Point _wireEnd;
    private Dictionary<(string, string, bool), string?>? _fits;
    private (Node Node, Socket Socket, bool Input)? _hover;

    /// <summary>Das Kabel, in das der gezogene Knoten beim Loslassen fiele - es leuchtet.</summary>
    private NodeLink? _landing;

    /// <summary>Der Knoten, der gerade aus der Palette hereingezogen wird - noch nicht im Graphen.</summary>
    private Node? _ghost;
    private string? _ghostSection;

    /// <summary>
    /// Der Anschluss unter einem Punkt auf dem Schirm - mit etwas Spielraum, denn ein
    /// Kreis von neun Punkten trifft man nicht auf den Punkt genau.
    /// </summary>
    internal (Node Node, Socket Socket, bool Input)? SocketAt(Point screen)
    {
        if (_graph is null) return null;

        double reach = Math.Max(8, 7 * Zoom);
        (Node, Socket, bool)? best = null;
        double nearest = reach;

        foreach (var node in _graph.Nodes)
        {
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                double d = (ToScreen(InputAt(node, i)) - screen).Length;
                if (d < nearest) { nearest = d; best = (node, node.Inputs[i], true); }
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                double d = (ToScreen(OutputAt(node, i)) - screen).Length;
                if (d < nearest) { nearest = d; best = (node, node.Outputs[i], false); }
            }
        }

        return best;
    }

    /// <summary>Wo ein Anschluss auf dem Schirm liegt - fuer die Probe.</summary>
    internal Point ScreenOf(Node node, string socket, bool input)
    {
        var list = input ? node.Inputs : node.Outputs;
        int index = IndexOf(list, socket);

        return ToScreen(input ? InputAt(node, index) : OutputAt(node, index));
    }

    /// <summary>Die Verbindung, die an einem Punkt auf dem Schirm vorbeilaeuft - oder keine.</summary>
    internal NodeLink? LinkAt(Point screen, double reach = 10)
    {
        if (_graph is null) return null;

        foreach (var link in _graph.Links)
        {
            if (Curve(link) is not var (a, b)) continue;

            for (int s = 0; s <= 24; s++)
                if ((Bezier(a, b, s / 24.0) - screen).Length < reach) return link;
        }

        return null;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var at = e.GetPosition(this);

        // Rechts bricht einen laufenden Zug ab - der Knoten springt zurueck, das Kabel
        // steckt wieder, wo es war. Ohne Zug waehlt es den Knoten darunter; der Hub
        // oeffnet beim Loslassen. Oeffnete er beim Druecken, landete das Loslassen in
        // ihm - und dort sprang frueher das Windows-Menue des Suchfelds auf.
        if (e.ChangedButton == MouseButton.Right)
        {
            if (_drag != Drag.None)
            {
                Cancel();
                _rightPressed = false;
            }
            else
            {
                var node = NodeAt(at);
                if (node is not null) Select(node);

                _rightPressed = true;
            }

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

        // Wie in Blender: Strg+Umschalt+Klick zeigt den Knoten im Betrachter.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) &&
            NodeAt(at) is { } viewed)
        {
            Select(viewed);
            ViewWanted?.Invoke(viewed);
            e.Handled = true;
            return;
        }

        if (SocketAt(at) is { } socket)
        {
            StartWire(socket, at);
            e.Handled = true;
            return;
        }

        if (EyeAt(at) is { } eyed)
        {
            TogglePreview(eyed);
            e.Handled = true;
            return;
        }

        var hit = NodeAt(at);
        Select(hit);

        if (hit is null)
        {
            StartPan(at);
        }
        else
        {
            _drag = Drag.Node;
            _start = at;
            _origin = new Point(hit.X, hit.Y);
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

    /// <summary>
    /// Beginnt ein Kabel. Am Ausgang: ein neues. Am Eingang mit Verbindung: diese
    /// Verbindung, geloest von ihrem Eingang. Am freien Eingang: ein Kabel rueckwaerts,
    /// das einen Ausgang sucht.
    /// </summary>
    private void StartWire((Node Node, Socket Socket, bool Input) socket, Point at)
    {
        _lifted = null;
        _wirePressed = (socket.Node.Id, socket.Socket.Name, socket.Input);

        if (socket.Input && _graph!.Into(socket.Node.Id, socket.Socket.Name) is { } link &&
            _graph.Find(link.From) is { } source)
        {
            _lifted = link;
            _wireNode = source;
            _wireSocket = link.Output;
            _wireFromInput = false;
        }
        else
        {
            _wireNode = socket.Node;
            _wireSocket = socket.Socket.Name;
            _wireFromInput = socket.Input;
        }

        _drag = Drag.Wire;
        _wireEnd = at;
        _fits = Fits();
        CaptureMouse();
        InvalidateVisual();
    }

    /// <summary>
    /// Welche Anschluesse das Kabel nehmen wuerden - einmal zu Beginn des Zuges
    /// gerechnet, nicht bei jeder Mausbewegung: Die Kreisprobe geht durch den Graphen.
    /// </summary>
    private Dictionary<(string, string, bool), string?> Fits()
    {
        var fits = new Dictionary<(string, string, bool), string?>();

        foreach (var node in _graph!.Nodes)
        {
            if (_wireFromInput)
            {
                foreach (var output in node.Outputs)
                    fits[(node.Id, output.Name, false)] = NodeEdits.CannotConnect(_graph, node, output.Name, _wireNode!, _wireSocket!);
            }
            else
            {
                foreach (var input in node.Inputs)
                {
                    // Der eigene, gerade geloeste Eingang passt immer - dorthin zurueck
                    // heisst: nichts geaendert.
                    bool own = _lifted is not null && _lifted.To == node.Id && _lifted.Input == input.Name;

                    fits[(node.Id, input.Name, true)] = own
                        ? null
                        : NodeEdits.CannotConnect(_graph, _wireNode!, _wireSocket!, node, input.Name);
                }
            }
        }

        return fits;
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

        if (_drag == Drag.Wire)
        {
            _wireEnd = at;
            _hover = SocketAt(at) is { } s && s.Input != _wireFromInput ? s : null;
            InvalidateVisual();
            return;
        }

        if (Selected is null) return;

        // Erst ab ein paar Punkten ist es ein Zug - ein Klick zum Waehlen soll nichts verschieben.
        if (!_moved && Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3) return;

        if (!_moved) Editing?.Invoke();

        _moved = true;
        Selected.X = Math.Round(_origin.X + delta.X / Zoom);
        Selected.Y = Math.Round(_origin.Y + delta.Y / Zoom);

        _landing = LandingFor(Selected, at);

        InvalidateVisual();
    }

    /// <summary>Die rechte Taste wurde ueber dem Editor gedrueckt, ohne einen Zug abzubrechen.</summary>
    private bool _rightPressed;

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Right)
        {
            if (_rightPressed)
            {
                _rightPressed = false;
                MenuWanted?.Invoke(ToGraph(e.GetPosition(this)));
            }

            e.Handled = true;
            return;
        }

        if (_drag == Drag.None) return;

        if (_drag == Drag.Wire)
        {
            FinishWire(e.GetPosition(this));
            return;
        }

        bool moved = _drag == Drag.Node && _moved;
        var node = Selected;
        var landing = _landing;

        EndDrag();

        if (!moved || node is null) return;

        // Ein freier Knoten, auf ein Kabel gelegt, faellt hinein - wie in Blender.
        if (landing is not null && Land(node, landing)) return;

        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Ob der Bildweg eines Knotens frei ist: sein Bildeingang ohne Kabel, und niemand
    /// liest ihn. Ein Knoten, an dem schon Renderdaten stecken, zaehlt als frei - er
    /// soll genauso in ein Kabel fallen koennen wie einer ganz ohne.
    /// </summary>
    private bool Free(Node node)
    {
        var (input, output) = NodeEdits.Through(node);

        return input is not null && output is not null &&
               _graph!.Into(node.Id, input) is null &&
               !_graph.Links.Any(l => l.From == node.Id);
    }

    /// <summary>
    /// Das Kabel, in das ein Knoten fiele, laege er hier: eines, das unter seinem Koerper
    /// hindurchlaeuft und an beiden Enden zu ihm passt - von mehreren das, das dem Zeiger
    /// am naechsten ist. Nur fuer einen Knoten mit freiem Bildweg; der Knoten muss noch
    /// nicht im Graphen stehen.
    /// </summary>
    internal NodeLink? LandingFor(Node node, Point pointer)
    {
        if (_graph is null || !Free(node)) return null;

        var (input, output) = NodeEdits.Through(node);

        var corner = ToScreen(new Point(node.X, node.Y));
        var body = new Rect(corner.X, corner.Y, NodeWidth * Zoom, NodeLayout.Height(node) * Zoom);
        body.Inflate(4, 4);

        NodeLink? best = null;
        double nearest = double.MaxValue;

        foreach (var link in _graph.Links)
        {
            if (link.From == node.Id || link.To == node.Id) continue;
            if (Curve(link) is not var (a, b)) continue;

            double closest = double.MaxValue;

            for (int s = 0; s <= 32; s++)
            {
                var p = Bezier(a, b, s / 32.0);
                if (body.Contains(p)) closest = Math.Min(closest, (p - pointer).Length);
            }

            if (closest >= nearest) continue;

            if (_graph.Find(link.From) is not { } from || _graph.Find(link.To) is not { } to) continue;

            if (NodeEdits.CannotConnect(_graph, from, link.Output, node, input!) is not null ||
                NodeEdits.CannotConnect(_graph, node, output!, to, link.Input) is not null)
            {
                continue;
            }

            nearest = closest;
            best = link;
        }

        return best;
    }

    /// <summary>
    /// Legt einen Knoten in ein Kabel und schafft dahinter so viel Platz, wie er braucht -
    /// die Knoten rechts von ihm ruecken nach rechts, wenn er sie ueberdeckt. Ein Kabel,
    /// das in die naechste Reihe zurueckspringt, schiebt nichts: Dort liegt der Leser
    /// links, und Platz nach rechts hilft ihm nicht.
    /// </summary>
    internal bool Land(Node node, NodeLink link)
    {
        if (_graph is null || !NodeEdits.InsertInto(_graph, link, node)) return false;

        double need = _graph.Links
            .Where(l => l.From == node.Id)
            .Select(l => _graph.Find(l.To))
            .OfType<Node>()
            .Where(reader => reader.X > node.X - 1)
            .Select(reader => node.X + NodeLayout.ColumnStep - reader.X)
            .DefaultIfEmpty(0)
            .Max();

        if (need > 0) NodeEdits.MakeRoom(_graph, node, need);

        GraphChanged?.Invoke();
        return true;
    }

    // ------------------------------------------------------------ Aus der Palette

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        Follow(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        Follow(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);

        _ghost = null;
        _ghostSection = null;
        _landing = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);

        if (e.Data.GetData(SectionFormat) is string section) DropSection(section, e.GetPosition(this));

        e.Handled = true;
    }

    /// <summary>Der Zug steht ueber dem Editor: ein Geist zeigt, wo der Knoten hinkaeme.</summary>
    private void Follow(DragEventArgs e)
    {
        e.Handled = true;

        if (_graph is null || e.Data.GetData(SectionFormat) is not string section)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;

        if (_ghostSection != section)
        {
            _ghost = NodeCatalog.ForSection(section)?.Create();
            _ghostSection = section;
        }

        if (_ghost is null) return;

        Hover(_ghost, e.GetPosition(this));
    }

    /// <summary>Stellt den Geist an den Zeiger und sucht das Kabel darunter.</summary>
    private void Hover(Node ghost, Point pointer)
    {
        var at = ToGraph(pointer);

        ghost.X = Math.Round(at.X - NodeWidth / 2);
        ghost.Y = Math.Round(at.Y - Header / 2);

        _landing = LandingFor(ghost, pointer);
        InvalidateVisual();
    }

    /// <summary>
    /// Ein Effekt wird an einem Punkt auf dem Schirm abgelegt - derselbe Weg wie beim
    /// Loslassen der Maus. Fuer die Probe, die nicht ziehen kann.
    /// </summary>
    internal void DropSection(string section, Point pointer)
    {
        var ghost = NodeCatalog.ForSection(section)?.Create();

        _ghost = null;
        _ghostSection = null;

        if (ghost is null || _graph is null)
        {
            _landing = null;
            InvalidateVisual();
            return;
        }

        Hover(ghost, pointer);

        var landing = _landing;
        _landing = null;

        SectionDropped?.Invoke(section, new Point(ghost.X, ghost.Y), landing);
        InvalidateVisual();
    }

    /// <summary>
    /// Beginnt ein Kabel an einem Punkt auf dem Schirm - derselbe Weg wie ein Druck der
    /// linken Taste auf einen Anschluss. Fuer die Probe, die keine Maus bewegen kann.
    /// </summary>
    internal bool BeginWire(Point screen)
    {
        if (SocketAt(screen) is not { } socket) return false;

        StartWire(socket, screen);
        return true;
    }

    /// <summary>Das Kabel wird losgelassen - angesteckt, verworfen oder zurueckgelegt.</summary>
    internal void FinishWire(Point at)
    {
        var target = SocketAt(at);
        var node = _wireNode!;
        string socket = _wireSocket!;
        var lifted = _lifted;
        bool fromInput = _wireFromInput;

        // Die Liste der passenden Anschluesse VOR dem Aufraeumen festhalten - EndDrag
        // vergisst sie, und danach hiesse jeder Anschluss "passt nicht".
        var fits = _fits;
        var pressed = _wirePressed;

        _warning = null;
        EndDrag();

        // Dort losgelassen, wo gepackt wurde: ein Klick, kein Zug - nichts aendert sich.
        if (target is { } same && pressed == (same.Node.Id, same.Socket.Name, same.Input)) return;

        if (target is { } t)
        {
            // Auf einem Anschluss derselben Richtung - zwei Ausgaenge, zwei Eingaenge - gibt
            // es keine Verbindung. Am eigenen Knoten heisst das: an sich selbst.
            string? why = t.Input == fromInput
                ? (ReferenceEquals(t.Node, node) ? "S_NodeWhySelf" : "S_NodeWhyDirection")
                : fits is not null && fits.TryGetValue((t.Node.Id, t.Socket.Name, t.Input), out var reason)
                    ? reason
                    : "S_NodeWhyMissing";

            // An einen Anschluss, der nicht passt: nichts aendert sich, und der Grund
            // steht oben links, bis zum naechsten Zug. Das gilt auch fuer ein Kabel, das
            // von einem Eingang geloest wurde - es kommt zurueck an seinen Platz. Frueher
            // war es dann weg: Wer einen Knoten an sich selbst stecken wollte, indem er
            // das Kabel an seinem Eingang packte und auf seinen Ausgang zog, verlor die
            // Verbindung, und das Bild wurde schwarz.
            if (why is not null)
            {
                Warning = Translate?.Invoke(why) ?? why;
                return;
            }

            // Zurueck an denselben Eingang: nichts geaendert.
            if (lifted is not null && lifted.To == t.Node.Id && lifted.Input == t.Socket.Name) return;

            Editing?.Invoke();

            if (lifted is not null) _graph!.Links.Remove(lifted);

            if (fromInput) _graph!.Connect(t.Node, t.Socket.Name, node, socket);
            else _graph!.Connect(node, socket, t.Node, t.Socket.Name);

            GraphChanged?.Invoke();
            return;
        }

        // Wirklich ins Leere: Ein geloestes Kabel ist damit weg, ein neues war nie da.
        if (lifted is not null)
        {
            Editing?.Invoke();
            _graph!.Links.Remove(lifted);
            GraphChanged?.Invoke();
        }
    }

    /// <summary>Bricht einen Zug ab - alles steht wieder, wie es vor ihm stand.</summary>
    private void Cancel()
    {
        if (_drag == Drag.Node && Selected is not null && _moved)
        {
            Selected.X = _origin.X;
            Selected.Y = _origin.Y;
        }

        _moved = false;
        EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_drag != Drag.None) EndDrag();
    }

    private void EndDrag()
    {
        _drag = Drag.None;
        _lifted = null;
        _landing = null;
        _wireNode = null;
        _wireSocket = null;
        _fits = null;
        _hover = null;
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

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Home:
                Frame();
                e.Handled = true;
                break;

            case Key.M when !control && Selected is not null and not OutputNode:
                Editing?.Invoke();
                Selected.Muted = !Selected.Muted;
                InvalidateVisual();
                GraphChanged?.Invoke();
                e.Handled = true;
                break;

            case Key.Delete or Key.Back or Key.X when !control && Selected is not null and not OutputNode:
                Remove(Selected);
                e.Handled = true;
                break;

            case Key.V when !control && !shift && Selected is not null and not OutputNode:
                TogglePreview(Selected);
                e.Handled = true;
                break;

            // Umschalt+D wie in Blender, Strg+D wie fast ueberall sonst.
            case Key.D when (shift || control) && Selected is not null:
                Duplicate(Selected);
                e.Handled = true;
                break;

            case Key.Z when control && !shift:
                UndoWanted?.Invoke();
                e.Handled = true;
                break;

            case Key.Y when control:
            case Key.Z when control && shift:
                RedoWanted?.Invoke();
                e.Handled = true;
                break;

            case Key.A when shift && !control:
                MenuWanted?.Invoke(ToGraph(Mouse.GetPosition(this)));
                e.Handled = true;
                break;

            case Key.Escape when _drag != Drag.None:
                Cancel();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Schaltet die Vorschau eines Knotens um - am Knopf im Kopf, oder aus dem Menue.</summary>
    public void TogglePreview(Node node)
    {
        if (node is OutputNode) return;

        node.Preview = !node.Preview;
        InvalidateVisual();
        PreviewToggled?.Invoke(node);
    }

    /// <summary>Wo der Vorschauknopf eines Knotens liegt - in Graphkoordinaten, rechts im Kopf.</summary>
    private static Rect Eye(Node node) => new(node.X + NodeWidth - 22, node.Y + 5, 16, Header - 10);

    /// <summary>Der Knoten, dessen Vorschauknopf unter einem Punkt auf dem Schirm liegt.</summary>
    internal Node? EyeAt(Point screen)
    {
        if (_graph is null || Zoom < 0.45) return null;

        var at = ToGraph(screen);

        foreach (var node in _graph.Nodes)
        {
            if (node is OutputNode) continue;

            var eye = Eye(node);
            eye.Inflate(3, 3);

            if (eye.Contains(at)) return node;
        }

        return null;
    }

    /// <summary>Nimmt einen Knoten heraus und schliesst die Luecke - Entf, oder aus dem Menue.</summary>
    public void Remove(Node node)
    {
        if (_graph is null || node is OutputNode) return;

        Editing?.Invoke();
        NodeEdits.Remove(_graph, node, reconnect: true);

        Select(null);
        GraphChanged?.Invoke();
    }

    /// <summary>
    /// Verdoppelt einen Knoten - er liest dasselbe, rechnet gleich und wird gewaehlt.
    /// Wohin sein Bild geht, zieht man danach: der Anfang einer Verzweigung.
    /// </summary>
    public void Duplicate(Node node)
    {
        if (_graph is null || node is OutputNode or RenderNode) return;

        Editing?.Invoke();
        var copy = NodeEdits.Duplicate(_graph, node);

        Select(copy);
        GraphChanged?.Invoke();
    }

    // ------------------------------------------------------------ Zeichnen

    /// <summary>
    /// Die Flaeche hinter den Knoten: durchsichtig, aber sie faengt die Maus.
    ///
    /// Frueher lag hier ein Schleier aus 60 % Schwarz - huebsch, und er machte die Knoten
    /// leicht lesbar. Aber er faelschte genau das, wofuer man den Graphen offen hat: Wer
    /// an einer Farbe dreht, sah das Bild dunkler und flauer, als es ist, und musste den
    /// Graphen ausblenden, um es wirklich zu sehen. Jetzt steht das Bild unverfaelscht da;
    /// die Knoten haben ihren eigenen deckenden Koerper, die Kabel einen dunklen Saum.
    /// </summary>
    private static readonly Brush Catch = Brushes.Transparent;

    /// <summary>Der Saum unter jedem Kabel - damit es auch auf einem hellen Bild zu sehen ist.</summary>
    private static readonly Brush Halo = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x08, 0x08, 0x0C)));

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
        dc.DrawRectangle(Catch, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_graph is null) return;

        foreach (var link in _graph.Links)
            if (!ReferenceEquals(link, _lifted)) DrawLink(dc, link);

        foreach (var node in _graph.Nodes)
            if (!ReferenceEquals(node, Selected)) DrawNode(dc, node);

        if (Selected is not null) DrawNode(dc, Selected);

        if (_ghost is not null)
        {
            dc.PushOpacity(0.55);
            DrawNode(dc, _ghost);
            dc.Pop();
        }

        if (_drag == Drag.Wire) DrawWire(dc);

        if (_warning is { Length: > 0 } warning)
        {
            var text = Label(warning, 12, Text);
            text.MaxTextWidth = Math.Max(1, ActualWidth - 40);

            var box = new Rect(12, 12, text.Width + 20, text.Height + 12);
            dc.DrawRoundedRectangle(WarningBack, WarningEdge, box, 4, 4);
            dc.DrawText(text, new Point(22, 18));
        }
    }

    private static readonly Brush WarningBack = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x3A, 0x22, 0x22)));
    private static readonly Pen WarningEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0x5A, 0x4A)), 1));
    private static readonly Pen Fitting = Frozen(new Pen(Brushes.White, 2));

    /// <summary>Das Kabel im Zug - und die Anschluesse, an die es passt.</summary>
    private void DrawWire(DrawingContext dc)
    {
        if (_wireNode is null || _wireSocket is null) return;

        var list = _wireFromInput ? _wireNode.Inputs : _wireNode.Outputs;
        int index = IndexOf(list, _wireSocket);
        if (index < 0) return;

        var anchor = ToScreen(_wireFromInput ? InputAt(_wireNode, index) : OutputAt(_wireNode, index));
        var free = _hover is { } h ? ScreenOf(h.Node, h.Socket.Name, h.Input) : _wireEnd;

        var (a, b) = _wireFromInput ? (free, anchor) : (anchor, free);

        var pen = new Pen(SocketBrush(list[index].Type), 2) { DashStyle = DashStyles.Dash };
        pen.Freeze();

        dc.DrawGeometry(null, pen, Path(a, b));

        if (_fits is null) return;

        double ring = Math.Max(6, 7 * Zoom);

        foreach (var ((id, name, input), why) in _fits)
        {
            if (why is not null || _graph!.Find(id) is not { } node) continue;

            dc.DrawEllipse(null, Fitting, ScreenOf(node, name, input), ring, ring);
        }

        // Ueber einem Anschluss, der nicht passt, steht gleich dort, warum.
        if (_hover is { } over && _fits.TryGetValue((over.Node.Id, over.Socket.Name, over.Input), out var reason) &&
            reason is not null)
        {
            var text = Label(Translate?.Invoke(reason) ?? reason, 11, Text);
            var at = new Point(_wireEnd.X + 14, _wireEnd.Y - text.Height - 6);

            dc.DrawRoundedRectangle(WarningBack, WarningEdge,
                                    new Rect(at.X - 6, at.Y - 3, text.Width + 12, text.Height + 6), 3, 3);
            dc.DrawText(text, at);
        }
    }

    /// <summary>Wo eine Verbindung auf dem Schirm beginnt und endet - oder nichts, wenn sie ins Leere zeigt.</summary>
    private (Point A, Point B)? Curve(NodeLink link)
    {
        var from = _graph!.Find(link.From);
        var to = _graph.Find(link.To);
        if (from is null || to is null) return null;

        int output = IndexOf(from.Outputs, link.Output);
        int input = IndexOf(to.Inputs, link.Input);
        if (output < 0 || input < 0) return null;

        return (ToScreen(OutputAt(from, output)), ToScreen(InputAt(to, input)));
    }

    private double Reach(Point a, Point b) => Math.Max(30 * Zoom, Math.Abs(b.X - a.X) / 2);

    /// <summary>Ein Punkt auf der Kurve einer Verbindung - fuer die Frage, ob die Maus auf ihr liegt.</summary>
    private Point Bezier(Point a, Point b, double s)
    {
        double reach = Reach(a, b);
        var c1 = new Point(a.X + reach, a.Y);
        var c2 = new Point(b.X - reach, b.Y);
        double u = 1 - s;

        return new Point(u * u * u * a.X + 3 * u * u * s * c1.X + 3 * u * s * s * c2.X + s * s * s * b.X,
                         u * u * u * a.Y + 3 * u * u * s * c1.Y + 3 * u * s * s * c2.Y + s * s * s * b.Y);
    }

    private StreamGeometry Path(Point a, Point b)
    {
        double reach = Reach(a, b);
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(a, isFilled: false, isClosed: false);
            context.BezierTo(new Point(a.X + reach, a.Y), new Point(b.X - reach, b.Y), b, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawLink(DrawingContext dc, NodeLink link)
    {
        if (Curve(link) is not var (a, b)) return;

        var from = _graph!.Find(link.From)!;
        var to = _graph.Find(link.To)!;

        // Das Kabel, in das ein Knoten gleich faellt, leuchtet - wie in Blender.
        bool landing = ReferenceEquals(link, _landing);

        var pen = landing
            ? new Pen(Brushes.White, Math.Max(2.5, 3.5 * Math.Min(1, Zoom)))
            : new Pen(SocketBrush(from.Outputs[IndexOf(from.Outputs, link.Output)].Type),
                      Math.Max(1.2, 2 * Math.Min(1, Zoom)));
        pen.Freeze();

        var path = Path(a, b);
        var halo = new Pen(Halo, pen.Thickness + 3);
        halo.Freeze();

        dc.PushOpacity(landing ? 1 : from.Muted || to.Muted ? 0.35 : 0.85);
        dc.DrawGeometry(null, halo, path);
        dc.DrawGeometry(null, pen, path);
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
            text.MaxTextWidth = Math.Max(1, box.Width - 34 * Zoom);
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

        if (labels && node is not OutputNode) DrawEye(dc, node);

        if (_viewed is var (shown, output) && ReferenceEquals(shown, node)) DrawViewed(dc, box, output, radius);

        if (node.Preview) DrawPreview(dc, node);

        dc.Pop();
    }

    private static readonly Pen Viewing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x3C)), 2));
    private static readonly Brush ViewingBack = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x3C)));

    /// <summary>Der Knoten im Betrachter: orange umrandet, darueber ein Schild mit dem Ausgang.</summary>
    private void DrawViewed(DrawingContext dc, Rect box, string output, double radius)
    {
        var frame = box;
        frame.Inflate(3, 3);
        dc.DrawRoundedRectangle(null, Viewing, frame, radius + 2, radius + 2);

        if (Zoom < 0.45) return;

        string label = (Translate?.Invoke("S_NodeViewerTag") ?? "Betrachter") + " · " + (SocketTitle?.Invoke(output) ?? output);
        var text = Label(label, 10.5 * Zoom, Brushes.Black);
        var tag = new Rect(box.X, box.Y - text.Height - 8 * Zoom, text.Width + 12 * Zoom, text.Height + 4 * Zoom);

        dc.DrawRoundedRectangle(ViewingBack, null, tag, 3 * Zoom, 3 * Zoom);
        dc.DrawText(text, new Point(tag.X + 6 * Zoom, tag.Y + 2 * Zoom));
    }

    private static readonly Pen EyeOn = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)), 1.2));
    private static readonly Pen EyeOff = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xEE, 0xEE, 0xF2)), 1.2));
    private static readonly Brush PreviewGround = Frozen(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)));

    /// <summary>Der Knopf fuer die Vorschau: ein Auge, offen, wenn sie gezeigt wird.</summary>
    private void DrawEye(DrawingContext dc, Node node)
    {
        var eye = Eye(node);
        var centre = ToScreen(new Point(eye.X + eye.Width / 2, eye.Y + eye.Height / 2));
        double rx = eye.Width / 2 * Zoom, ry = eye.Height / 2.6 * Zoom;

        var pen = node.Preview ? EyeOn : EyeOff;
        dc.DrawEllipse(null, pen, centre, rx, ry);

        if (node.Preview) dc.DrawEllipse(pen.Brush, null, centre, ry * 0.7, ry * 0.7);
        else dc.DrawLine(pen, new Point(centre.X - rx, centre.Y + ry), new Point(centre.X + rx, centre.Y - ry));
    }

    /// <summary>Das kleine Bild unter den Anschluessen - im Seitenverhaeltnis des Bildes, mittig.</summary>
    private void DrawPreview(DrawingContext dc, Node node)
    {
        var area = new Rect(node.X + (NodeWidth - NodePreviews.Width) / 2, NodeLayout.PreviewTop(node),
                            NodePreviews.Width, NodePreviews.Height);

        var topLeft = ToScreen(area.TopLeft);
        var box = new Rect(topLeft, new Size(area.Width * Zoom, area.Height * Zoom));

        dc.DrawRectangle(PreviewGround, null, box);

        if (PreviewOf?.Invoke(node) is not { } image || image.Width <= 0 || image.Height <= 0) return;

        double scale = Math.Min(box.Width / image.Width, box.Height / image.Height);
        double w = image.Width * scale, h = image.Height * scale;

        dc.DrawImage(image, new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
    }
}
