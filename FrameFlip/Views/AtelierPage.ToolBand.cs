using System.Windows;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Was ein Werkzeug der Leiste auf der Seite tut (docs/Atelier-Werkzeugplan.md, 3.1). Die
/// Leiste meldet nur, welches gewaehlt wurde; hier steht, was daraus wird - im Stapel und im
/// Knotenmodus verschieden, weil dort verschieden gerechnet wird.
/// </summary>
public partial class AtelierPage
{
    /// <summary>"Bild oeffnen" - vorn in der Werkzeugleiste, vor den Kategorien.</summary>
    private System.Windows.Controls.Button OpenButton()
    {
        // Zeichen und Name wie die Chips darunter (Entscheidung 9).
        var label = new IconLabel { Icon = "open" };
        label.SetResourceReference(IconLabel.TextProperty, "S_OpenImage");

        var button = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("OverlayButton"),
            Margin = new Thickness(0, 0, 8, 0),
            Content = label,
        };

        button.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "S_OpenImage");
        button.Click += OnOpenClicked;
        return button;
    }

    /// <summary>Strg+K: die Suche der Werkzeugleiste.</summary>
    public void OpenToolSearch() => ToolBand.OpenSearch();

    /// <summary>Ein Werkzeug aus der Leiste oder der Suche.</summary>
    internal void UseTool(ToolEntry entry)
    {
        switch (entry.Action)
        {
            case ToolAction.Effect or ToolAction.Mask:
                // Im Knotenmodus ein Knoten - hinter den gewaehlten, wenn er einen Ausgang hat,
                // sonst frei in die Mitte der Ansicht. Im Stapel die Karte im Farbstreifen.
                if (InNodes && entry.Kind is { } kind) AddKind(kind);
                else if (!InNodes && entry.Section is { } section) Tools.Show(section);
                break;

            case ToolAction.Brush:
                MouseTools.Select(AtelierTool.Brush, notify: true);
                Properties.ChooseBrush(entry.Area, entry.Shape);
                ToolBand.MarkActive(entry.Key);
                break;

            case ToolAction.MaskEdit:
                if (PaintTarget() is { } painted) ShowMaskEditMenu(painted, ToolBand);
                else Properties.Told(Strings.T("S_ToolNeedsPaintedMask"));
                break;
        }
    }

    /// <summary>Ein Knoten aus der Leiste: wie im Hub, nur ohne Stelle, an die geklickt wurde.</summary>
    private void AddKind(NodeKind kind)
    {
        if (_graph is null) return;

        var node = kind.Create();

        // Eine Ebene in der Liste gewaehlt: Der Effekt wirkt nur auf sie - er kommt in ihren
        // Zweig, vor ihr Mischen, und nicht dahinter auf alles, was darunter liegt.
        if (NodeEdits.Through(node).Input is not null && LayerBranch() is { } branch)
        {
            AddIntoLayer(node, branch);
            return;
        }

        if (NodeView.Selected is { } after and not OutputNode &&
            NodeEdits.Through(after).Output is not null && NodeEdits.Through(node).Input is not null)
        {
            InsertAfterNode(after, node);
            return;
        }

        var centre = NodeView.ToGraph(new System.Windows.Point(NodeView.ActualWidth / 2, NodeView.ActualHeight / 2));
        PlaceAt(node, centre, null);
    }
}
