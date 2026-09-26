using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using Point = System.Windows.Point;

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

        NodeLayers.MoveWanted += MoveLayer;
        NodeLayers.StepWanted += StepLayer;
        NodeLayers.DuplicateWanted += DuplicateLayer;
        NodeLayers.RemoveWanted += RemoveLayer;

        NodeLayers.AddWanted += ShowLayerAddMenu;
        NodeLayers.MenuWanted += ShowLayerMenu;
        NodeLayers.MaskMenuWanted += ShowFreeMaskMenu;
        NodeLayers.ModeWanted += OnLayerMode;
        NodeLayers.OpacityWanted += OnLayerOpacity;
    }

    private void MoveLayer(Node layer, Node target, bool above)
        => LayerEdit(() => LayerEdits.Move(_graph!, layer, target, above) ? layer : null);

    private void StepLayer(Node layer, bool up)
        => LayerEdit(() => LayerEdits.Step(_graph!, layer, up) ? layer : null);

    private void DuplicateLayer(Node layer) => LayerEdit(() => LayerEdits.Duplicate(_graph!, layer));

    private void RemoveLayer(Node layer)
        => LayerEdit(() => LayerEdits.Remove(_graph!, layer) ? _graph!.Output : null, arrange: false);

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

    /// <summary>
    /// Das Plus der Liste oeffnet den Hub bei den Ebenen - neben der Liste, und was er
    /// anlegt, kommt auf die gewaehlte Ebene.
    /// </summary>
    private void ShowLayerAddMenu(FrameworkElement anchor)
    {
        if (_graph is null) return;

        var middle = NodeView.ToGraph(new Point(NodeView.ActualWidth / 2, NodeView.ActualHeight / 2));
        ShowNodeHub(middle, "layers", anchor, ListTarget);
    }

    /// <summary>Das zuletzt geoeffnete Menue einer Zeile - fuer die Probe.</summary>
    internal FlipMenu? LayerMenu { get; private set; }

    /// <summary>
    /// Rechtsklick auf eine Zeile der Ebenenliste: Die Ebene wird gewaehlt, und ein Menue
    /// bietet an, was sich mit ihr tun laesst - umbenennen, zeigen, verdoppeln, loeschen,
    /// verschieben, anschneiden.
    /// </summary>
    private void ShowLayerMenu(NodeLayer layer)
    {
        if (_graph is null) return;

        if (layer.Target is { } target) OnLayerChosen(target);

        var menu = new FlipMenu(NodeLayers);
        var node = layer.Switch;

        if (node is not null)
        {
            string current = node.Label ?? layer.Name;

            menu.Rename(Strings.T("S_LayerMenuRename"), current, name =>
            {
                RememberNodes();
                node.Label = name.Length > 0 ? name : null;
                NodeView.InvalidateVisual();
                AfterNodeEdit();
            });

            menu.Toggle(Strings.T("S_LayerMenuVisible"), !node.Muted, () => OnLayerMuted(node));
        }

        if (layer.Target is { } shown) menu.Item("⌖", Strings.T("S_LayerMenuShowInGraph"), () => OnLayerChosen(shown));

        // Die Ebene allein: was in ihr Mischen oben hineinfliesst, im Betrachter.
        if (layer.Mix is { } mix && _graph.Into(mix.Id, "Oben") is { } up)
            menu.Item("◉", Strings.T("S_LayerMenuViewAlone"), () => SetViewer((up.From, up.Output)));

        if (node is not null && layer.Chain is { } chain)
        {
            int at = chain.Members.IndexOf(node);

            menu.Separator()
                .Item("❐", Strings.T("S_DuplicateLayer"), () => DuplicateLayer(node))
                .Item("✕", Strings.T("S_RemoveLayer"), () => RemoveLayer(node))
                .Separator()
                .Item("▲", Strings.T("S_MoveLayerUp"), () => StepLayer(node, true), enabled: at + 1 < chain.Members.Count)
                .Item("▼", Strings.T("S_MoveLayerDown"), () => StepLayer(node, false), enabled: at > 0);

            if (node is MixNode clipped)
            {
                menu.Toggle(Strings.T("S_LayerMenuClip"), clipped.Clip, () =>
                {
                    RememberNodes();
                    clipped.Clip = !clipped.Clip;
                    NodeView.InvalidateVisual();
                    AfterNodeEdit();
                });
            }
        }

        // Die Maske der Ebene: dieselben Handgriffe wie an ihrem Knoten - samt Verlauf. Wer
        // mit Masken arbeitet, klickt die Ebene in der Liste an und nicht den Knoten im Graphen.
        if (layer.MaskSource is MaskNode mask) AddMaskItems(menu, mask, NodeLayers);

        LayerMenu = menu;
        menu.Open();
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

        // Nach der Aenderung: Der Verlauf bekommt den festgehaltenen Stand davor.
        RememberValueEdit();

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

        RememberValueEdit();

        NodeView.InvalidateVisual();
        Refresh(interim, recompose: false);
        ShowNodeLayers();

        if (interim) return;

        KeepNodes();
        if (ReferenceEquals(_shownNode, layer)) ShowNodeSettings();
    }
}
