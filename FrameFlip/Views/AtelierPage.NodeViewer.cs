using System.Windows;
using System.Windows.Input;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Betrachter: Das grosse Bild zeigt, was ein Knoten ausgibt, statt der Ausgabe - wie
/// der Viewer in Blender.
///
/// Strg+Umschalt+Klick auf einen Knoten zeigt seinen ersten Ausgang; ein weiterer auf
/// denselben Knoten den naechsten, und nach dem letzten ist wieder die Ausgabe zu sehen.
/// Ein Klick auf die Ausgabe, der Knopf ueber dem Bild oder das Menue schalten ebenfalls
/// zurueck. Der Betrachter bleibt, wenn man zu einem anderen Werkzeug wechselt - so laesst
/// sich eine Maske malen, waehrend man sie sieht. Deshalb steht ueber dem Bild, was es
/// gerade zeigt: Ein Bild, das nicht das fertige ist, ohne es zu sagen, waere eine Falle.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Was der Betrachter zeigt - Knoten und Ausgang. Null: die Ausgabe.</summary>
    private (string Node, string Output)? _viewer;

    /// <summary>Strg+Umschalt+Klick: der erste Ausgang, bei demselben Knoten der naechste, nach dem letzten zurueck.</summary>
    private void OnViewWanted(Node node)
    {
        if (node is OutputNode || node.Outputs.Count == 0)
        {
            SetViewer(null);
            return;
        }

        int next = 0;

        if (_viewer is var (id, output) && id == node.Id)
        {
            next = IndexOf(node, output) + 1;

            if (next >= node.Outputs.Count)
            {
                SetViewer(null);
                return;
            }
        }

        SetViewer((node.Id, node.Outputs[next].Name));
    }

    private static int IndexOf(Node node, string output)
    {
        for (int i = 0; i < node.Outputs.Count; i++)
            if (node.Outputs[i].Name == output) return i;

        return -1;
    }

    /// <summary>Zeigt einen Ausgang im Betrachter - oder mit null wieder die Ausgabe.</summary>
    internal void SetViewer((string Node, string Output)? viewer)
    {
        _viewer = viewer;

        ShowViewer();
        FetchNodeSources();
    }

    /// <summary>
    /// Nach einer Aenderung am Aufbau: Gibt es den betrachteten Knoten oder seinen Ausgang
    /// nicht mehr, zeigt das Bild wieder die Ausgabe.
    /// </summary>
    private void KeepViewer()
    {
        if (_viewer is var (id, output) && _graph?.Find(id) is { } node && node.Output(output) is not null)
        {
            ShowViewer();
            return;
        }

        if (_viewer is null) return;

        _viewer = null;
        ShowViewer();
    }

    /// <summary>Das Schild ueber dem Bild und die Markierung im Editor.</summary>
    private void ShowViewer()
    {
        ShowSoloSwitch();

        var node = _viewer is var (id, _) ? _graph?.Find(id) : null;

        NodeView.Viewed = node is not null ? (node, _viewer!.Value.Output) : null;

        if (node is null)
        {
            // Im Stapel kann eine Ebene isoliert sein - dann spricht das Schild von ihr (C6b).
            if (!ShowSoloBadge()) ViewerBadge.Visibility = Visibility.Collapsed;
            return;
        }

        string output = _viewer!.Value.Output;

        // Zeigt der Betrachter genau das, was eine Ebene oben oder im Faktor bekommt, heisst
        // es "isoliert" - abgeleitet aus dem Betrachter, kein eigener Zustand (C6).
        ViewerText.Text = IsolationOf(node, output) switch
        {
            (var layer, false) => Strings.T("S_IsolatedLayer", layer.Name),
            (var layer, true) => Strings.T("S_IsolatedMask", layer.Name),
            _ => Strings.T("S_ViewerShowing", NodeTitles.For(node) + " · " + NodeTitles.Socket(output)),
        };

        ViewerBadge.Visibility = Visibility.Visible;
    }

    /// <summary>Welche Ebene der Betrachter allein zeigt - oder deren Maske. Null: nichts davon.</summary>
    private (NodeLayer Layer, bool Mask)? IsolationOf(Node node, string output)
    {
        if (_graph is null) return null;

        foreach (var layer in NodeLayerList.Of(_graph))
        {
            if (layer.Mix is not { } mix) continue;

            if (_graph.Into(mix.Id, "Oben") is { } up && up.From == node.Id && up.Output == output) return (layer, false);
            if (_graph.Into(mix.Id, "Faktor") is { } factor && factor.From == node.Id && factor.Output == output) return (layer, true);
        }

        return null;
    }

    /// <summary>
    /// Eine Ebene allein zeigen - oder ihre Maske: der Betrachter auf das, was oben oder im
    /// Faktor in ihr Mischen fliesst. Der Aufbau bleibt, wie er ist. Ein zweites Mal beendet es.
    /// </summary>
    internal bool Isolate(NodeLayer layer, bool mask)
    {
        if (_graph is null || layer.Mix is not { } mix || _graph.Into(mix.Id, mask ? "Faktor" : "Oben") is not { } link) return false;

        (string, string) wanted = (link.From, link.Output);
        SetViewer(_viewer == wanted ? null : wanted);
        return true;
    }

    /// <summary>
    /// Esc beendet, was der Betrachter zeigt - aber nur, wenn niemand sonst Esc braucht: kein
    /// Zug mit der Maus, keine Objektwahl. Die haben Vorrang.
    /// </summary>
    private bool EndViewer()
    {
        if (_objectPick is not null || Mouse.Captured is not null) return false;

        if (_stackSolo is not null)
        {
            EndStackSolo(render: true);
            return true;
        }

        if (_viewer is null) return false;

        SetViewer(null);
        return true;
    }

    private void OnViewerClosed(object sender, RoutedEventArgs e)
    {
        if (_stackSolo is not null) EndStackSolo(render: true);
        else SetViewer(null);
    }
}
