using System.Windows;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Masken als eigene Elemente im Knotenmodus (docs/Projekte-und-Masken.md, Punkt 3 und 4):
/// eine Maske von ihrer Ebene loesen, duplizieren, mit weiteren Ebenen verbinden, und was
/// sie zeigt als eigene Ebene ausschneiden.
///
/// Eine Maske ist im Graphen schon ein Knoten, und ihr Ausgang kann an beliebig viele
/// Ebenen gehen. Was fehlte, waren die Handgriffe dafuer - bisher ging das nur, indem man
/// Kabel zog.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Ebenen, die diese Maske begrenzt - die Mischen, in deren Faktor sie steckt.</summary>
    internal List<MixNode> LayersOf(Node mask)
    {
        if (_graph is null) return new List<MixNode>();

        return _graph.Links
            .Where(l => l.From == mask.Id && l.Input == "Faktor")
            .Select(l => _graph.Find(l.To))
            .OfType<MixNode>()
            .Distinct()
            .ToList();
    }

    /// <summary>Die Eintraege fuer eine Maske im Menue ihres Knotens.</summary>
    private void AddMaskItems(FlipMenu menu, MaskNode mask)
    {
        var users = LayersOf(mask);
        var others = NodeLayers.Shown.Select(l => l.Mix).OfType<MixNode>().Where(m => !users.Contains(m)).ToList();

        menu.Separator()
            .Item("✂", Strings.T("S_MaskMenuExtract"), () => ExtractLayer(mask))
            .Item("⊘", Strings.T("S_MaskMenuDetach"), () => DetachMask(mask), enabled: users.Count > 0)
            .Item("❐", Strings.T("S_MaskMenuDuplicate"), () => DuplicateMask(mask))
            .Item("⤳", Strings.T("S_MaskMenuConnect"), () => ShowConnectMenu(mask, others), enabled: others.Count > 0);
    }

    /// <summary>Die Ebenen, mit denen sich eine Maske zusaetzlich verbinden laesst - ein zweites Menue.</summary>
    private void ShowConnectMenu(MaskNode mask, IReadOnlyList<MixNode> layers)
    {
        var menu = new FlipMenu(NodeView);

        foreach (var layer in layers)
            menu.Item("▭", Strings.T("S_MaskMenuConnectTo", NodeTitles.For(layer)), () => ConnectMask(mask, layer));

        NodeMenu = menu;
        menu.Open();
    }

    /// <summary>
    /// Was die Maske zeigt, als eigene Ebene - direkt ueber ihrer Ebene, die ihre Maske
    /// behaelt (Entscheidung 4); eine freie Maske kommt oben auf die Ebenen.
    ///
    /// Ausgeschnitten wird, was an dieser Stelle zu sehen ist - dasselbe Bild, das unten in
    /// die neue Ebene fliesst. Nur so bleibt das Bild ohne weitere Aenderung gleich, auch an
    /// weichen Maskenraendern und mit jeder Mischart darunter: Die Ebene selbst noch einmal
    /// darueberzulegen verdoppelte sie am Rand, und das Bild der Datei verloere dort, was
    /// die Ebenen darunter daraus gemacht haben.
    /// </summary>
    internal MixNode? ExtractLayer(MaskNode mask) => Extract(mask, remember: true);

    /// <param name="remember">Ein eigener Schritt im Verlauf - nicht, wenn der Aufrufer ihn schon hat.</param>
    private MixNode? Extract(MaskNode mask, bool remember)
    {
        if (_graph is null || ExtractPlace(mask) is not { } after || NodeEdits.Through(after).Output is not { } output) return null;

        if (remember) RememberNodes();

        if (LayerEdits.AddCutout(_graph, after, after, output, mask, "Maske") is not ({ } cutout, { } mix)) return null;

        mix.Label = Strings.T("S_CutoutLayerName");
        ArrangeLayer(after, after, cutout, mix);

        NodeView.Select(mix);
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return mix;
    }

    /// <summary>Wohinter die ausgeschnittene Ebene kommt - die erste Ebene der Maske, sonst oben auf die Ebenen.</summary>
    private Node? ExtractPlace(MaskNode mask)
        => _graph is null ? null : LayersOf(mask).FirstOrDefault() ?? NodeEdits.LayerTop(_graph);

    /// <summary>
    /// Loest eine Maske von ihren Ebenen: Die Ebenen wirken danach ueberall, die Maske
    /// bleibt als freier Knoten stehen und laesst sich woanders anschliessen.
    /// </summary>
    internal void DetachMask(MaskNode mask)
    {
        if (_graph is null) return;

        var users = LayersOf(mask).Select(m => m.Id).ToHashSet();
        if (users.Count == 0) return;

        RememberNodes();
        _graph.Links.RemoveAll(l => l.From == mask.Id && l.Input == "Faktor" && users.Contains(l.To));

        NodeView.InvalidateVisual();
        AfterNodeEdit();
    }

    /// <summary>Eine Kopie der Maske - mit eigener Kennung und frei, die Ebenen behalten das Original.</summary>
    internal MaskNode? DuplicateMask(MaskNode mask)
    {
        if (_graph is null) return null;

        RememberNodes();

        if (NodeEdits.Duplicate(_graph, mask) is not MaskNode copy) return null;

        copy.Mask.Id = "";
        copy.Mask.EnsureId();

        NodeView.Select(copy);
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return copy;
    }

    /// <summary>Verbindet eine Maske mit einer weiteren Ebene - hatte die schon eine, loest diese sich.</summary>
    internal void ConnectMask(MaskNode mask, MixNode layer)
    {
        if (_graph is null) return;

        RememberNodes();
        _graph.Connect(mask, "Maske", layer, "Faktor");

        NodeView.InvalidateVisual();
        AfterNodeEdit();
    }

    /// <summary>
    /// Das Objekt an einem Bildpunkt als eigene Ebene - Bildmenue, Knotenmodus: eine
    /// Kryptomatte, die genau dieses Objekt waehlt, und damit ausgeschnitten, was oben auf
    /// den Ebenen zu sehen ist. False, wenn dort nichts steht.
    /// </summary>
    internal bool ObjectAsLayerAt(CryptomatteSet set, int x, int y)
    {
        if (_graph is null || _path is null) return false;

        var levels = Cryptomatte.Levels(_passes, set.Prefix).ToList();
        if (levels.Count == 0) return false;

        var frame = _sources.TryGetValue(levels[0], out var known) ? known : Imaging.FloatFrame.FromExrPass(_path, levels[0]);
        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;

        float id = frame.R[y * frame.Width + x];
        if (id == 0f) return false;

        string name = set.NameOf(id) ?? Strings.T("S_CutoutLayerName");

        // Ohne Ebenen gaebe es nichts, wohinter die neue kaeme.
        if (NodeEdits.LayerTop(_graph) is not { } top || NodeEdits.Through(top).Output is null) return false;

        RememberNodes();

        var mask = _graph.Add(new MaskNode
        {
            Mask = new LayerMask { Kind = MaskKind.Cryptomatte, Source = set.Prefix, Levels = levels },
            Preview = true,
        });

        mask.Mask.TogglePick(name, id);
        mask.Mask.EnsureId();

        // Maske und Ausschnitt sind ein Schritt - der Verlauf hat ihn schon oben.
        if (Extract(mask, remember: false) is not { } mix) return false;

        mix.Label = name;
        mask.X = mix.X - 2 * NodeLayout.ColumnStep;
        mask.Y = mix.Y + NodeLayout.Height(mix) + NodeLayout.Gap;

        return true;
    }
}
