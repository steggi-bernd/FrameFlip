using System.Windows;
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
        var node = _viewer is var (id, _) ? _graph?.Find(id) : null;

        NodeView.Viewed = node is not null ? (node, _viewer!.Value.Output) : null;

        if (node is null)
        {
            ViewerBadge.Visibility = Visibility.Collapsed;
            return;
        }

        string output = NodeTitles.Socket(_viewer!.Value.Output);
        ViewerText.Text = Strings.T("S_ViewerShowing", NodeTitles.For(node) + " · " + output);
        ViewerBadge.Visibility = Visibility.Visible;
    }

    private void OnViewerClosed(object sender, RoutedEventArgs e) => SetViewer(null);
}
