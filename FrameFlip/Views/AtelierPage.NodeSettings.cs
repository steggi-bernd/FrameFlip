using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der gewaehlte Knoten: seine Einstellungen im Farbstreifen, und die Werkzeuge im
/// Bild, die auf ihn wirken.
///
/// Verschieben wirkt auf einen Platzieren- oder Obenauf-Knoten, der Pinsel auf eine
/// gemalte Maske - dieselben Griffe wie im Stapel, nur dass ihr Ziel jetzt ein Knoten
/// ist und keine Ebene. Wer den Graphen ausblendet, um im Bild zu arbeiten, arbeitet
/// am Knoten, den er zuletzt gewaehlt hat.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Der Knoten, dessen Einstellungen der Farbstreifen gerade zeigt.</summary>
    private Node? _shownNode;

    /// <summary>Der Knoten, dem der Greifrahmen gehoert - gemerkt, solange ein Zug laeuft.</summary>
    private Node? _placingNode;

    private void OnNodeSelected()
    {
        ShowNodeSettings();
        ShowPlacement();
        ShowNodeLayers();

        // Gewaehlt wird im Bild, und das geht, bevor die Maske irgendwo steckt - ihre
        // Stufen muessen also schon da sein, wenn sie nur gewaehlt ist.
        if (NodeView.Selected is MaskNode { Mask: { Kind: MaskKind.Cryptomatte } mask } &&
            mask.Levels.Any(level => !_sources.ContainsKey(level)))
        {
            FetchNodeSources();
        }
    }

    /// <summary>
    /// Die Passe, die ein Ausgang der Datei werden koennen: alle ausser dem Bild selbst
    /// und den Stufen einer Kryptomatte - die sind Kennungen und kein Licht. Heissen zwei
    /// in verschiedenen Ansichtsebenen gleich, steht der ganze Name da.
    /// </summary>
    private IReadOnlyList<(string Name, string Label)> NodePasses()
    {
        var passes = _passes.Where(p => p.Name.Length > 0 && !Cryptomatte.IsLevel(p.ShortName)).ToList();

        return passes
            .Select(p => (p.Name, passes.Count(q => q.ShortName == p.ShortName) > 1 ? p.Name : p.ShortName))
            .ToList();
    }

    /// <summary>Was die Felder im Streifen von der Seite brauchen.</summary>
    private NodeFieldContext FieldContext() => new(_graph!, NodePasses(), change =>
    {
        RememberNodes();
        change();
        NodeView.InvalidateVisual();
        AfterNodeEdit();
    }, PassThumb);

    /// <summary>Die Werkzeuge, die eine Ebene haben kann - die Karten einer Ebenenkorrektur.</summary>
    private static readonly string[] LayerSections = { "Basic", "Curve", "WhiteBalance", "Zones", "Bands", "Lut" };

    /// <summary>Zeigt im Farbstreifen, was sich am gewaehlten Knoten einstellen laesst.</summary>
    private void ShowNodeSettings()
    {
        if (_graph is null)
        {
            _shownNode = null;
            return;
        }

        var node = NodeView.Selected;
        _shownNode = node;

        // Im Knotenmodus gibt es kein "Ebene oder Bild" - das Ziel ist der Knoten, und
        // alle Karten sind bedienbar.
        Tools.ShowTarget(null, onLayer: false, locked: false);

        switch (node)
        {
            case null:
                Tools.Load(ImageAdjustments.Neutral, new GradingStack());
                Tools.ShowNode(Strings.T("S_NodeChooseHint"), Array.Empty<string>(), Array.Empty<NodeField>());
                return;

            case LayerGradeNode grade:
                // Der Streifen legt beim Laden an, was fehlt - zurueckgeschrieben wird
                // beim ersten Zug, genau wie bei einer Ebene.
                Tools.Load(grade.Adjustments, grade.Tools);
                Tools.ShowNode(null, LayerSections, Array.Empty<NodeField>());
                return;

            case PointToolNode { Tool: VibranceTool }:
                break;

            case var tool when NodeTitles.Section(tool) is { } section:
                // Die Karte bekommt das Werkzeug des Knotens selbst - was sie einstellt,
                // stellt sie am Knoten ein.
                Tools.Load(ImageAdjustments.Neutral, StackOf(tool));
                Tools.ShowNode(null, new[] { section }, Array.Empty<NodeField>());
                return;
        }

        // Die Datei zeigt ihre Passe mit Miniaturen - die entstehen beim ersten Mal.
        if (node is RenderNode) MakePassThumbs();

        Tools.Load(ImageAdjustments.Neutral, new GradingStack());
        Tools.ShowNode(null, Array.Empty<string>(), NodeFields.For(node, FieldContext()));
    }

    /// <summary>Ein Stapel mit genau dem Werkzeug dieses Knotens - demselben Objekt, keine Kopie.</summary>
    private static GradingStack StackOf(Node node)
    {
        var stack = new GradingStack();

        switch (node)
        {
            case PointToolNode { Tool: { } tool }: stack.Tools.Add(tool); break;
            case OpticsNode { Tool: { } tool }: stack.Optics.Add(tool); break;
            case LocalNode { Tool: { } tool }: stack.Local.Add(tool); break;
            case GeometryNode { Tool: { } tool }: stack.Geometry.Add(tool); break;
            case DataNode { Tool: { } tool }: stack.Data.Add(tool); break;
            case FramePassNode { Pass: { } pass }: stack.Frame.Add(pass); break;
        }

        return stack;
    }

    /// <summary>
    /// Im Farbstreifen wurde am Knoten gedreht. Die Karten haben das Werkzeug des Knotens
    /// schon selbst geaendert; eine Ebenenkorrektur bekommt ihren Stand zurueck, wie eine
    /// Ebene im Stapel.
    /// </summary>
    private void OnNodeSettingsChanged(bool interim)
    {
        RememberValueEdit();

        if (_shownNode is LayerGradeNode grade)
        {
            grade.Adjustments = Tools.Adjustments;
            grade.Tools = Tools.Stack.Clone();
        }

        // Ein Titel kann sich geaendert haben - "Mischen: Negativ multiplizieren".
        NodeView.InvalidateVisual();

        Refresh(interim, recompose: false);

        if (!interim) KeepNodes();
    }

    /// <summary>Am Graphen selbst hat sich etwas geaendert - ein Knoten wurde stummgeschaltet.</summary>
    private void OnGraphChanged() => AfterNodeEdit();

    /// <summary>
    /// Haelt den Graphen in den Einstellungen fest - ohne die Datei zu schreiben. Das
    /// geschieht wie beim Stapel beim Beenden; ein Regler, der bei jedem Zug die Platte
    /// anfasst, waere ein Regler, der hakt.
    /// </summary>
    private void KeepNodes()
    {
        if (_graph is null) return;

        _settings.AtelierNodes = _graph.Save();

        // Ein Zug an einem Wert ist zu Ende, wenn sein Stand festgehalten wird.
        _valueEditOpen = false;
    }

    /// <summary>
    /// Das gelesene Bild, das in einen Eingang eines Knotens fuehrt - dasselbe, das der
    /// Graph dort beim Rechnen bekommt.
    /// </summary>
    private FloatFrame? SourceInto(Node node, string input)
    {
        if (_graph?.Into(node.Id, input) is not { } link) return null;

        string? key = _graph.Find(link.From) switch
        {
            RenderNode => link.Output == RenderNode.Picture ? "" : link.Output,
            PictureNode picture => picture.Path,
            _ => null,
        };

        return key is not null && _sources.TryGetValue(key, out var frame) ? frame : null;
    }

    /// <summary>Wo der gewaehlte Knoten platziert - oder null, wenn er nichts platziert.</summary>
    private (LayerTransform Place, FloatFrame Source)? NodePlacement(Node? node) => node switch
    {
        PlaceNode place when SourceInto(place, "Bild") is { } source => (place.Place, source),
        OverlayNode overlay when SourceInto(overlay, "Ebene") is { } source => (overlay.Place, source),
        _ => null,
    };

    /// <summary>Der Greifrahmen im Knotenmodus - am gewaehlten Platzieren- oder Obenauf-Knoten.</summary>
    private void ShowNodePlacement()
    {
        var node = _dragHooked ? _placingNode : NodeView.Selected;

        if (_frame is null || _showingOriginal || _tool is not (AtelierTool.Move or AtelierTool.Crop) ||
            NodePlacement(node) is not var (place, source))
        {
            Placement.Track(null, 0, 0, 0, 0, false);
            if (!_dragHooked) _placingNode = null;

            return;
        }

        if (!_dragHooked) _placingNode = node;

        Placement.Track(place, source.Width, source.Height, _frame.Width, _frame.Height,
                        Display.Stretch == Stretch.Uniform);
    }

    /// <summary>Der Pinsel im Knotenmodus - auf der gemalten Maske des gewaehlten Knotens.</summary>
    private void ShowNodeBrush()
    {
        // Wie im Stapel: Der Pinsel faengt immer. Ist eine gemalte Maske gewaehlt - oder
        // eine Ebene, an der eine haengt -, malt er darauf; sonst legt der erste Strich
        // eine Maskenebene an. Frueher fing er ohne gewaehlte Maske gar nichts, und mit
        // ihm waren auch Groesse und Haerte am Bild tot.
        Placement.MaskWanted = MakeNodeMask;

        if (_frame is null)
        {
            Placement.Paint(null, 0, 0, false);
            Display.Cursor = null;

            return;
        }

        var mask = PaintTarget()?.Mask.PaintOn(_number, _frame.Width, _frame.Height);

        Placement.Paint(mask, _frame.Width, _frame.Height, Display.Stretch == Stretch.Uniform);
        Display.Cursor = Cursors.None;

        UseBrushSettings();
    }

    /// <summary>
    /// Worauf der Pinsel im Knotenmodus malt: die gewaehlte gemalte Maske - oder die, die
    /// in den Faktor der gewaehlten Ebene fliesst. Sonst keine.
    /// </summary>
    private MaskNode? PaintTarget() => NodeView.Selected switch
    {
        MaskNode { Mask.Kind: MaskKind.Painted } mask => mask,
        MixNode mix when _graph?.Into(mix.Id, "Faktor") is { } factor &&
                         _graph.Find(factor.From) is MaskNode { Mask.Kind: MaskKind.Painted } mask => mask,
        _ => null,
    };

    /// <summary>
    /// Der erste Strich ohne Maske: eine Maskenebene - eine Einstellungsebene mit gemalter
    /// Maske, ueber der gewaehlten Ebene oder oben auf den Ebenen. Sie aendert nichts, bis
    /// jemand an ihrer Korrektur dreht; gewaehlt ist danach die Maske, damit weitergemalt
    /// wird. Dasselbe, was der Stapel beim ersten Strich anlegt.
    /// </summary>
    private PaintedMask? MakeNodeMask()
    {
        if (_graph is null || _frame is null) return null;

        if (PaintTarget() is { } known) return known.Mask.PaintOn(_number, _frame.Width, _frame.Height);

        return AddNodeMaskLayer(new LayerMask { Kind = MaskKind.Painted }, Strings.T("S_MaskLayerName")) is { } made
            ? made.Mask.PaintOn(_number, _frame.Width, _frame.Height)
            : null;
    }

    /// <summary>
    /// Eine Maskenebene im Graphen: eine Einstellungsebene, deren Mischen diese Maske im
    /// Faktor hat - ueber der gewaehlten Ebene oder oben auf den Ebenen. Gewaehlt ist danach
    /// die Maske. Fuer den ersten Pinselstrich und fuer "Objekt hier als Maske".
    /// </summary>
    private MaskNode? AddNodeMaskLayer(LayerMask mask, string label)
    {
        if (_graph is null || ListTarget() is not { } after || NodeEdits.Through(after).Output is not { } below) return null;

        RememberNodes();

        if (LayerEdits.AddAdjustment(_graph, after) is not var (grade, mix)) return null;

        mix.Label = label;

        var node = _graph.Add(new MaskNode { Mask = mask, Preview = true });

        _graph.Connect(grade, "Bild", node, "Ebene");
        _graph.Connect(after, below, node, "Untergrund");
        _graph.Connect(node, "Maske", mix, "Faktor");

        ArrangeLayer(after, after, grade, mix);
        node.X = grade.X;
        node.Y = grade.Y - NodeLayout.Height(node) - NodeLayout.Gap;

        NodeView.Select(node);
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return node;
    }

    /// <summary>Am Rahmen wurde gezogen - im Knotenmodus bekommt der Knoten die neue Lage.</summary>
    private void OnNodePlacementDragged(LayerTransform place, bool interim)
    {
        switch (_placingNode ?? NodeView.Selected)
        {
            case PlaceNode node: node.Place = place; break;
            case OverlayNode node: node.Place = place; break;
            default: return;
        }

        RememberValueEdit();

        if (!interim)
        {
            StopDragFrames();
            Refresh(interim: false, recompose: false);
            KeepNodes();

            // Die Felder im Streifen zeigen die neue Lage.
            ShowNodeSettings();

            return;
        }

        _pendingPlace = place;

        if (_dragHooked) return;

        _dragHooked = true;
        CompositionTarget.Rendering += OnDragFrame;
    }

    /// <summary>Es wurde gemalt - im Knotenmodus auf die Maske des Knotens.</summary>
    private void OnNodePainted(bool interim)
    {
        RememberValueEdit();

        if (!interim)
        {
            StopDragFrames();
            Refresh(interim: false, recompose: false);
            KeepNodes();

            return;
        }

        _pendingPaint = true;

        if (_dragHooked) return;

        _dragHooked = true;
        CompositionTarget.Rendering += OnDragFrame;
    }
}
