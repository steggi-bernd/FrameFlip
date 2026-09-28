using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Das Schnellfeld am Knoten (docs/Atelier-Arbeitsablauf.md, C5b, Punkt 7): Ein Doppelklick
/// auf einen Knoten oeffnet an der Maus, was sich an genau diesem Knoten tun laesst.
///
/// Es erfindet keinen eigenen Weg. Eine Korrektur oder ein Effekt geht ueber die Werkzeugleiste
/// (<see cref="UseTool"/>) und landet damit dort, wohin auch "+ Korrektur" in der Zielzeile sie
/// setzt: hinter den Knoten, in den Zweig seiner Ebene, wenn er in ihrer Maske liegt. Nur die
/// freie Maske hat etwas Eigenes - hinter ihr gibt es kein Bild, also wird sie die Maske einer
/// neuen Korrektur.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Das zuletzt geoeffnete Schnellfeld - fuer die Probe.</summary>
    internal FlipMenu? QuickPanel { get; private set; }

    private void SetUpQuickPanel() => NodeView.QuickWanted += ShowQuickPanel;

    internal void ShowQuickPanel(Node node)
    {
        if (_graph is null) return;

        if (!ReferenceEquals(NodeView.Selected, node)) NodeView.Select(node);

        var menu = new FlipMenu(NodeView).Heading(Strings.T("S_QuickTitle", NodeTitles.For(node)));

        bool inMask = TargetPath.OwnerOf(node, _graph) is (_, true);
        bool carriesPicture = node is not OutputNode && NodeEdits.Through(node).Output is not null;

        if (node is MaskNode free && !inMask)
        {
            // Hinter einer Maske gibt es kein Bild: Sie wird die Maske einer neuen Korrektur.
            menu.Item("◐", Strings.T("S_QuickCorrectWithMask"), () => CorrectThroughMask(free));
        }
        else if (carriesPicture || inMask)
        {
            menu.Item(GlyphFor("S_Correction"), Strings.T("S_Correction"), () => UseCatalog("S_Correction"))
                .Item(GlyphFor("S_NodeLight"), Strings.T("S_NodeLight"), () => UseCatalog("S_NodeLight"))
                .Item(GlyphFor("S_Levels"), Strings.T("S_Levels"), () => UseCatalog("S_Levels"))
                .Item("ƒ", Strings.T("S_QuickEffect"), ToolBand.OpenSearch, "Strg+K");
        }

        if (menu.Items.Count > 0) menu.Separator();

        if (node is MaskNode { Mask.Kind: MaskKind.Cryptomatte })
        {
            // Die Maske bleibt gewaehlt - ein Klick ins Bild nimmt Objekte in sie auf (C3b).
            menu.Item("⬢", Strings.T("S_QuickSelectObjects"), () => MouseTools.Select(AtelierTool.Select, notify: true));
        }

        if (node is MixNode mix && LayerEdits.ChainOf(LayerEdits.Chains(_graph), mix) is not null &&
            _graph.Into(mix.Id, "Faktor") is null && ObjectSet() is not null)
        {
            menu.Item("⬢", Strings.T("S_QuickObjectMask"), () => StartObjectMask(mix));
        }

        // Isolieren (C6): die Ebene dieses Mischens allein - oder die Ebene, deren Maske dieser Knoten ist.
        var layers = NodeLayerList.Of(_graph);

        if (layers.FirstOrDefault(l => ReferenceEquals(l.Mix, node)) is { } own)
            menu.Item("◧", Strings.T("S_QuickIsolateLayer"), () => Isolate(own, mask: false), "Alt+Klick aufs Auge");

        if (layers.FirstOrDefault(l => l.Mix is { } m && _graph.Into(m.Id, "Faktor")?.From == node.Id) is { } masked)
            menu.Item("◧", Strings.T("S_QuickIsolateMask"), () => Isolate(masked, mask: true), "Alt+Klick auf die Maske");

        if (node.Outputs.Count > 0) menu.Item("◉", Strings.T("S_HubViewer"), () => OnViewWanted(node), "Strg+Umschalt+Klick");

        // Die Ausgabe hat nichts davon - dann gar kein Feld statt eines leeren.
        QuickPanel = menu.Items.Count > 0 ? menu : null;
        QuickPanel?.Open();
    }

    /// <summary>Ein Eintrag der Werkzeugleiste - derselbe Weg wie dort, mit dem Knoten als Ziel.</summary>
    private void UseCatalog(string titleKey) => UseTool(ToolCatalog.All.Single(entry => entry.TitleKey == titleKey));

    private static string GlyphFor(string titleKey) => ToolCatalog.All.Single(entry => entry.TitleKey == titleKey).Glyph;

    /// <summary>
    /// Eine Korrektur, begrenzt durch eine freie Maske: eine Einstellungsebene ueber der gewaehlten
    /// Ebene oder oben auf den Ebenen, mit dieser Maske im Faktor. Gewaehlt ist danach die
    /// Korrektur, damit ihre Karten gleich zu sehen sind. Ein Schritt fuer "Rueckgaengig".
    /// </summary>
    internal LayerGradeNode? CorrectThroughMask(MaskNode mask)
    {
        if (_graph is null || NodeEdits.LayerTop(_graph) is not { } after || NodeEdits.Through(after).Output is null) return null;

        RememberNodes();

        if (LayerEdits.AddAdjustment(_graph, after) is not var (grade, mix)) return null;

        mix.Label = NodeTitles.MaskName(mask);
        _graph.Connect(mask, "Maske", mix, "Faktor");

        ArrangeLayer(after, after, grade, mix);

        NodeView.Select(grade);
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return grade;
    }
}
