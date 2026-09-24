using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// In der Ebenenliste am Graphen arbeiten: Ebenen ziehen, hinzufuegen, verdoppeln,
/// loeschen, Mischung und Deckkraft einstellen.
///
/// Was am Graphen geschieht, steht in <see cref="LayerEdits"/>. Hier steht, was daraus
/// fuer die Seite folgt: Rueckgaengig, neu anordnen, neu rechnen. Ein Zug, der nichts
/// veraendert - eine Zeile an ihren eigenen Platz gelegt -, hinterlaesst keinen Schritt
/// im Verlauf.
/// </summary>
public partial class AtelierPage
{
    private void SetUpNodeLayerList()
    {
        NodeLayers.CanMove = (layer, target) => _graph is not null && LayerEdits.CanMove(_graph, layer, target);

        NodeLayers.MoveWanted += (layer, target, above) =>
            LayerEdit(() => LayerEdits.Move(_graph!, layer, target, above) ? layer : null);

        NodeLayers.StepWanted += (layer, up) =>
            LayerEdit(() => LayerEdits.Step(_graph!, layer, up) ? layer : null);

        NodeLayers.DuplicateWanted += layer => LayerEdit(() => LayerEdits.Duplicate(_graph!, layer));

        NodeLayers.RemoveWanted += layer =>
            LayerEdit(() => LayerEdits.Remove(_graph!, layer) ? _graph!.Output : null, arrange: false);

        NodeLayers.AddWanted += ShowLayerAddMenu;
        NodeLayers.ModeWanted += OnLayerMode;
        NodeLayers.OpacityWanted += OnLayerOpacity;
    }

    /// <summary>
    /// Eine Aenderung an den Ebenen: Der Stand davor kommt in den Verlauf, wenn sich etwas
    /// geaendert hat. Liefert die Aenderung einen Knoten, wird er gewaehlt - nach dem
    /// Loeschen die Ausgabe, damit nichts Geloeschtes gewaehlt bleibt.
    ///
    /// Danach wird neu angeordnet: Ein verschobener Zweig laege sonst an seiner alten
    /// Stelle, und seine Kabel liefen quer durch den Graphen.
    /// </summary>
    private void LayerEdit(Func<Node?> edit, bool arrange = true)
    {
        if (_graph is null) return;

        string before = _graph.Save();
        var chosen = edit();

        if (chosen is null || _graph.Save() == before) return;

        _undo.Add(before);
        if (_undo.Count > HistoryDepth) _undo.RemoveAt(0);
        _redo.Clear();

        if (arrange) NodeLayout.Arrange(_graph);

        NodeView.Select(chosen is OutputNode ? null : chosen);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>Das Menue am Plus der Liste - dieselben Ebenen wie im Menue des Editors.</summary>
    private void ShowLayerAddMenu(FrameworkElement anchor)
    {
        if (_graph is null) return;

        MakePassThumbs();

        var after = ListTarget();
        if (after is null) return;

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        var adjustment = new MenuItem { Header = Strings.T("S_AddAdjustment") };
        adjustment.Click += (_, _) => AddAdjustmentLayer(after);
        menu.Items.Add(adjustment);

        var image = new MenuItem { Header = Strings.T("S_NodeMenuImageLayer") };
        image.Click += (_, _) => AddImageLayer(after);
        menu.Items.Add(image);

        if (PassMenu("S_NodeMenuPassLayer", pass => AddPassLayer(pass, after)) is { } passes) menu.Items.Add(passes);

        menu.IsOpen = true;
    }

    /// <summary>
    /// Worauf eine Ebene aus der Liste kommt: auf die gewaehlte Ebene - sonst oben auf die
    /// Ebenen. Ein gewaehltes Wasserzeichen zaehlt nicht: Hinter ihm laege die neue Ebene
    /// nach der Bildwerdung, und das waere keine Ebene mehr.
    /// </summary>
    private Node? ListTarget()
    {
        if (_graph is null) return null;

        if (NodeView.Selected is MixNode mix && LayerEdits.ChainOf(LayerEdits.Chains(_graph), mix) is not null) return mix;

        return NodeEdits.LayerTop(_graph);
    }

    /// <summary>
    /// Eine Einstellungsebene: Sie korrigiert, was unter ihr liegt, und legt das Ergebnis auf
    /// Normal darueber - wie im Ebenenstreifen. Gewaehlt ist danach die Korrektur, damit der
    /// Farbstreifen gleich ihre Karten zeigt.
    /// </summary>
    internal void AddAdjustmentLayer(Node after)
    {
        if (_graph is null) return;

        RememberNodes();

        if (LayerEdits.AddAdjustment(_graph, after) is not var (grade, mix)) return;

        ArrangeLayer(after, after, grade, mix);

        NodeView.Select(grade);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>Die Mischung einer Ebene aus der Liste - und der Farbstreifen, wenn er dasselbe Mischen zeigt.</summary>
    private void OnLayerMode(Node layer, BlendMode mode)
    {
        switch (layer)
        {
            case MixNode mix:
                mix.Mode = mode;
                break;
            case OverlayNode overlay:
                overlay.Mode = mode;
                break;
            default:
                return;
        }

        NodeView.InvalidateVisual();
        Refresh(interim: false, recompose: false);
        KeepNodes();
        ShowNodeLayers();

        if (ReferenceEquals(_shownNode, layer)) ShowNodeSettings();
    }

    /// <summary>
    /// Die Deckkraft einer Ebene aus der Liste. Waehrend gezogen wird grob, wie an jedem
    /// Regler; losgelassen zieht der Farbstreifen nach.
    /// </summary>
    private void OnLayerOpacity(Node layer, float opacity, bool interim)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);

        switch (layer)
        {
            case MixNode mix:
                mix.Opacity = opacity;
                break;
            case OverlayNode overlay:
                overlay.Opacity = opacity;
                break;
            default:
                return;
        }

        NodeView.InvalidateVisual();
        Refresh(interim, recompose: false);
        ShowNodeLayers();

        if (interim) return;

        KeepNodes();
        if (ReferenceEquals(_shownNode, layer)) ShowNodeSettings();
    }
}
