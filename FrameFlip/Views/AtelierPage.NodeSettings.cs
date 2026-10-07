using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    /// <summary>
    /// Der Knoten, an dem gerade gearbeitet wird - aus dem Bearbeitungsziel der Sitzung, das der
    /// Wahl im Editor folgt (<see cref="OnNodeSelected"/>). Wohin ein Regler, ein Strich, ein
    /// Klick ins Bild oder ein neuer Knoten geht, richtet sich danach. Die Markierung in der
    /// Ebenenliste zeigt weiter, was im Editor gewaehlt ist.
    /// </summary>
    private Node? SelectedNode => _recipe.Target is Atelier.EditingTarget.GraphNode { Node: var node } ? node : null;

    /// <summary>
    /// Tauscht den Graphen im Editor aus, ohne die Ansicht zu verlassen - Rueckgaengig, ein
    /// Neuaufbau. Der Editor waehlt denselben Knoten im neuen Graphen und meldet das. Gibt es
    /// ihn dort nicht mehr, waehlt er still ab; das Ziel folgt dann hier, sonst zeigte es auf
    /// einen Knoten, der nicht mehr im Graphen steht.
    /// </summary>
    private void ShowGraph(NodeGraph graph)
    {
        NodeView.Replace(graph);

        if (NodeView.Selected is null) _recipe.Focus(Atelier.EditingTarget.Picture);
    }

    /// <summary>Der Knoten, dem der Greifrahmen gehoert - gemerkt, solange ein Zug laeuft.</summary>
    private Node? _placingNode;

    private void OnNodeSelected()
    {
        // Das Ziel folgt der Wahl im Editor. Die Ebene aus der Liste gilt weiter, solange genau
        // ihr Mischen gewaehlt bleibt. Etwas anderes gewaehlt, und sie gilt nicht mehr als
        // gewaehlte Ebene - auch nicht, wenn man danach ihr Mischen im Graphen anklickt.
        var chosen = NodeView.Selected;
        bool asLayer = _recipe.Target is Atelier.EditingTarget.GraphNode { FromLayerList: true } focus &&
                       ReferenceEquals(focus.Node, chosen);

        _recipe.Focus(chosen is null ? Atelier.EditingTarget.Picture : new Atelier.EditingTarget.GraphNode(chosen, asLayer));

        ShowNodeSettings();
        ShowPlacement();
        ShowNodeLayers();

        // Gewaehlt wird im Bild, und das geht, bevor die Maske irgendwo steckt - ihre
        // Stufen muessen also schon da sein, wenn sie nur gewaehlt ist.
        if (SelectedNode is MaskNode { Mask: { Kind: MaskKind.Cryptomatte } mask } &&
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
    }, PassThumb, ArmNodeTint);

    /// <summary>Die Werkzeuge, die eine Ebene haben kann - die Karten einer Ebenenkorrektur.</summary>
    private static readonly string[] LayerSections = { "Basic", "Levels", "Curve", "WhiteBalance", "Zones", "Bands", "Lut" };

    /// <summary>Zeigt im Farbstreifen, was sich am gewaehlten Knoten einstellen laesst.</summary>
    private void ShowNodeSettings()
    {
        if (_graph is null)
        {
            _shownNode = null;
            return;
        }

        var node = SelectedNode;
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

        _recipe.Nodes = _graph.Save();

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
    private (LayerTransform Place, FloatFrame Source)? NodePlacement(Node? node) => Placed(node) switch
    {
        PlaceNode place when SourceInto(place, "Bild") is { } source => (place.Place, source),
        OverlayNode overlay when SourceInto(overlay, "Ebene") is { } source => (overlay.Place, source),

        // Ein ausgeschnittenes Stueck ist so gross wie die Leinwand.
        CutoutNode cutout when _frame is not null => (cutout.Place, _frame),
        _ => null,
    };

    /// <summary>
    /// Der Knoten, den der Rahmen bewegt: der gewaehlte selbst - oder bei einer
    /// ausgeschnittenen Ebene, deren Mischen gewaehlt ist, ihr Ausschneiden. Wer eine
    /// Ebene waehlt und zieht, will sie verschieben, nicht erst ihren Knoten suchen.
    /// </summary>
    private Node? Placed(Node? node) => node switch
    {
        MixNode mix when _graph?.Into(mix.Id, "Oben") is { } over && _graph.Find(over.From) is CutoutNode cutout => cutout,
        _ => node,
    };

    /// <summary>Der Greifrahmen im Knotenmodus - am gewaehlten Platzieren- oder Obenauf-Knoten.</summary>
    private void ShowNodePlacement()
    {
        var node = _dragHooked ? _placingNode : SelectedNode;

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

        var target = PaintTarget();
        var mask = target?.Mask.PaintOn(_number, _frame.Width, _frame.Height);

        if (target is not null && mask is not null) WatchMask(target, mask);

        // Den Verlauf gibt es, sobald der Pinsel auf einer gemalten Maske liegt.
        Properties.ShowBrushHistory(target is not null);

        Placement.Paint(mask, _frame.Width, _frame.Height, Display.Stretch == Stretch.Uniform);
        Display.Cursor = Cursors.None;

        UseBrushSettings();
    }

    /// <summary>
    /// Worauf der Pinsel im Knotenmodus malt: die gewaehlte gemalte Maske - oder eine gemalte,
    /// die in den Faktor der gewaehlten Ebene fliesst, auch ueber eine Maskenrechnung. Sonst keine.
    /// </summary>
    private MaskNode? PaintTarget() => SelectedNode switch
    {
        MaskNode { Mask.Kind: MaskKind.Painted } mask => mask,
        _ when ChosenLayer() is { } layer => PaintedOf(layer),
        _ => null,
    };

    /// <summary>Die gemalte Maske einer Ebene - im Faktor, oder in einer Maskenrechnung davor.</summary>
    private MaskNode? PaintedOf(MixNode layer)
    {
        var open = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (_graph?.Into(layer.Id, "Faktor") is { } factor) open.Enqueue(factor.From);

        while (open.Count > 0 && seen.Count < 64)
        {
            string id = open.Dequeue();
            if (!seen.Add(id)) continue;

            switch (_graph!.Find(id))
            {
                case MaskNode { Mask.Kind: MaskKind.Painted } painted:
                    return painted;

                case MaskMathNode math:
                    foreach (string input in new[] { "A", "B" })
                        if (_graph.Into(math.Id, input) is { } link) open.Enqueue(link.From);
                    break;
            }
        }

        return null;
    }

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

        if (AddNodeMaskLayer(new LayerMask { Kind = MaskKind.Painted }, Strings.T("S_MaskLayerName")) is not { } made) return null;

        var paint = made.Mask.PaintOn(_number, _frame.Width, _frame.Height);
        WatchMask(made, paint);
        Properties.ShowBrushHistory(true);

        return paint;
    }

    /// <summary>
    /// Eine neue Maske. Ist eine Ebene gewaehlt - ihr Mischen, ein Knoten ihrer Gruppe oder ihr
    /// Pass -, kommt sie an diese Ebene und begrenzt, wo sie zu sehen ist (<see cref="AttachMask"/>).
    /// Sonst, und fuer eine Auswahl, an der man Farbe dreht (<paramref name="onLayer"/> aus), eine
    /// Maskenebene: eine Einstellungsebene, deren Mischen diese Maske im Faktor hat - ueber der
    /// gewaehlten Ebene oder oben auf den Ebenen. Gewaehlt ist danach die Maske.
    /// </summary>
    private MaskNode? AddNodeMaskLayer(LayerMask mask, string label, bool onLayer = true)
    {
        if (_graph is null) return null;

        if (onLayer && ChosenLayer() is { } layer)
        {
            RememberNodes();

            var attached = AttachMask(layer, mask);
            ArrangeLayer();

            NodeView.Select(attached);
            NodeView.InvalidateVisual();
            AfterNodeEdit();

            return attached;
        }

        if (ListTarget() is not { } after || NodeEdits.Through(after).Output is not { } below) return null;

        RememberNodes();

        if (LayerEdits.AddAdjustment(_graph, after) is not var (grade, mix)) return null;

        mix.Label = label;

        var node = _graph.Add(new MaskNode { Mask = mask, Preview = true });

        _graph.Connect(grade, "Bild", node, "Ebene");
        _graph.Connect(after, below, node, "Untergrund");
        _graph.Connect(node, "Maske", mix, "Faktor");

        // Die Maske steht in der Gruppe ihrer Ebene, in der Bahn unter dem Bildweg.
        ArrangeLayer();

        NodeView.Select(node);
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return node;
    }

    /// <summary>Am Rahmen wurde gezogen - im Knotenmodus bekommt der Knoten die neue Lage.</summary>
    private void OnNodePlacementDragged(LayerTransform place, bool interim)
    {
        switch (Placed(_placingNode ?? SelectedNode))
        {
            case PlaceNode node: node.Place = place; break;
            case OverlayNode node: node.Place = place; break;
            case CutoutNode node: node.Place = place; break;
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

    /// <summary>
    /// Es wurde gemalt - im Knotenmodus auf die Maske des Knotens.
    ///
    /// Waehrend des Strichs nur das Noetigste: den Takt anmelden. Frueher fragte hier
    /// jede Mausmeldung den Verlauf, ob schon ein Schritt faellig sei - und schrieb dafuer
    /// den ganzen Graphen samt aller gemalten Masken als Text. Bei einer Maus, die
    /// tausendmal in der Sekunde meldet, und ein paar Masken im Bild war das ein Ruckeln,
    /// das man dem Pinsel zuschrieb. Der Schritt ist der ganze Strich; gemerkt wird er
    /// einmal, an seinem Ende.
    /// </summary>
    private void OnNodePainted(bool interim)
    {
        if (!interim)
        {
            // Was seit dem letzten Takt dazukam, noch zeigen - der Strich endet sonst
            // einen Takt zu frueh. Blieb jeder Takt ein genauer Ausschnitt, stimmt das Bild
            // danach schon voll aufgeloest.
            //
            // Nur wenn dieser Strich ueberhaupt als Ausschnitt ins Bild kam: Eine Aenderung
            // an der Maske, die der Pinsel nicht gemeldet hat, stuende sonst erst nach der
            // Ruhepause im Bild.
            bool exact = !_regionFailed && PaintRegion() && _regionPainted;

            StopDragFrames();
            _regionFailed = false;
            _regionPainted = false;

            RememberValueEdit();
            RecordMaskStroke();

            // Das ganze Bild - fuer die Vorschauen der Knoten und das Histogramm - erst,
            // wenn der Pinsel ruht. Gleich nach jedem Strich hiesse bei 4K eine Fuenftel-
            // sekunde Stillstand zwischen zwei Strichen, und genau dort setzt man wieder an.
            // Fiel ein Takt auf das grobe Bild zurueck, muss das scharfe dagegen sofort her.
            if (exact) SettleAfterPainting();
            else Refresh(interim: false, recompose: false);

            KeepNodes();
            return;
        }

        // Ein neuer Strich: Das ganze Bild wartet, bis auch er zu Ende ist.
        _calm?.Stop();

        _pendingPaint = true;

        if (_dragHooked) return;

        _dragHooked = true;
        CompositionTarget.Rendering += OnDragFrame;
    }

    /// <summary>Ob in diesem Strich ein Takt keinen Ausschnitt rechnen konnte - dann ist das Bild grob.</summary>
    private bool _regionFailed;

    /// <summary>Ob in diesem Strich ein Ausschnitt ins Bild geschrieben wurde.</summary>
    private bool _regionPainted;

    /// <summary>Wartet nach dem letzten Strich, bevor das ganze Bild gerechnet wird.</summary>
    private DispatcherTimer? _calm;

    /// <summary>Wie lange der Pinsel ruhen muss - kuerzer, als man auf das Histogramm schaut.</summary>
    private static readonly TimeSpan PaintingCalm = TimeSpan.FromMilliseconds(700);

    private void SettleAfterPainting()
    {
        if (_calm is null)
        {
            _calm = new DispatcherTimer(DispatcherPriority.Background) { Interval = PaintingCalm };
            _calm.Tick += (_, _) =>
            {
                _calm.Stop();

                // Malt gerade jemand, kommt das ganze Bild nach seinem Strich.
                if (_dragHooked || !InNodes) return;

                Refresh(interim: false, recompose: false);
            };
        }

        _calm.Stop();
        _calm.Start();
    }

    /// <summary>Ob das ganze Bild nach dem Malen noch aussteht - fuer die Probe.</summary>
    internal bool SettlingAfterPainting => _calm?.IsEnabled == true;
}
