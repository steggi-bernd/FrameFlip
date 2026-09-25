using System.Windows;
using System.Windows.Controls.Primitives;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Der Hub im Knoteneditor: Rechtsklick oder Umschalt+A - und das Plus der Ebenenliste.
///
/// Hier steht, was er anbietet und was ein Klick tut. Wo das Neue hinkommt, entscheidet
/// die Stelle des Rechtsklicks: auf einem Kabel faellt ein Knoten hinein, sonst liegt er
/// frei dort. Eine Ebene kommt, wie im Stapel, auf die gewaehlte oder oben auf die Ebenen.
/// </summary>
public partial class AtelierPage
{
    private Popup? _hubPopup;

    /// <summary>Welche Eintraege zuletzt gewaehlt wurden - der Hub zeigt sie unter "Zuletzt".</summary>
    private static readonly List<string> Recent = new();

    /// <summary>Der offene Hub - fuer die Probe.</summary>
    internal NodeHub? Hub => _hubPopup is { IsOpen: true } popup ? popup.Child as NodeHub : null;

    /// <summary>
    /// Oeffnet den Hub an einer Stelle des Graphen. <paramref name="category"/> waehlt die
    /// Kategorie vor; <paramref name="anchor"/> heisst: neben diesem Element statt am Zeiger.
    /// </summary>
    internal void ShowNodeHub(Point at, string? category = null, UIElement? anchor = null, Func<Node?>? target = null)
    {
        if (_graph is null) return;

        // Die Passe bekommen Miniaturen - beim ersten Oeffnen entstehen sie.
        MakePassThumbs();

        var screen = NodeView.ToScreen(at);
        var link = anchor is null && NodeView.NodeAt(screen) is null ? NodeView.LinkAt(screen) : null;
        target ??= LayerTarget;

        string? note = link is not null && _graph.Find(link.From) is { } from && _graph.Find(link.To) is { } to
            ? Strings.T("S_HubIntoWire", NodeTitles.For(from), NodeTitles.For(to))
            : null;

        var chosen = NodeView.Selected is { } node and not OutputNode ? node : null;

        var hub = new NodeHub(HubCategories(at, link, target), note,
                              chosen is null ? null : NodeTitles.For(chosen),
                              chosen is null ? Array.Empty<HubAction>() : NodeActions(chosen),
                              ViewActions());

        if (category is not null && hub.CategoryByKey(category) is { } start) hub.Choose(start);

        var popup = new Popup
        {
            Child = hub,
            StaysOpen = false,
            AllowsTransparency = true,
            PlacementTarget = anchor ?? NodeView,
        };

        if (anchor is not null)
        {
            popup.Placement = PlacementMode.Left;
        }
        else
        {
            // Neben dem Zeiger, nicht unter ihm: Die rechte Taste, die den Hub geoeffnet
            // hat, wird ueber ihm losgelassen, und das darf nichts ausloesen. Ganz im
            // Editor - am rechten oder unteren Rand weicht er nach links oder oben aus.
            const double Gap = 8;

            double x = screen.X + Gap, y = screen.Y + Gap;
            if (x + hub.Width > NodeView.ActualWidth) x = screen.X - Gap - hub.Width;
            if (y + hub.Height > NodeView.ActualHeight) y = screen.Y - Gap - hub.Height;

            popup.Placement = PlacementMode.Relative;
            popup.HorizontalOffset = Math.Clamp(x, 0, Math.Max(0, NodeView.ActualWidth - hub.Width));
            popup.VerticalOffset = Math.Clamp(y, 0, Math.Max(0, NodeView.ActualHeight - hub.Height));
        }

        hub.Done += () => popup.IsOpen = false;

        // Eine Kachel wird in den Editor gezogen: Der Hub geht zu, der Zug laeuft weiter
        // - der Editor nimmt ihn wie einen aus der Palette.
        hub.DragWanted += (section, _) =>
        {
            popup.IsOpen = false;
            DragDrop.DoDragDrop(NodeView, new DataObject(NodeEditor.SectionFormat, section), DragDropEffects.Copy);
        };

        _hubPopup = popup;
        popup.IsOpen = true;
    }

    /// <summary>Was der Hub anbietet - Kategorie fuer Kategorie.</summary>
    private List<HubCategory> HubCategories(Point at, NodeLink? link, Func<Node?> target)
    {
        var categories = new List<HubCategory>();
        var passes = NodePasses();

        // Wer etwas nimmt, merkt es sich - fuer "Zuletzt".
        HubEntry Used(HubEntry entry) => entry with
        {
            Activate = () => { Remember(entry.Title); entry.Activate(); },
            Alternate = entry.Alternate is { } other ? () => { Remember(entry.Title); other(); } : null,
        };

        HubEntry Kind(NodeKind kind) => Used(new HubEntry(Strings.T(kind.TitleKey),
                                                         kind.Section is { } s ? GradingPanel.GlyphOf(s) ?? "•" : "•",
                                                         () => PlaceAt(kind.Create(), at, link))
        {
            Section = kind.Section,
            Words = Strings.T(kind.Group),
        });

        // Ebenen: die Passe der Datei, Neues, die Bausteine und was schon im Graphen liegt.
        var passLayers = passes.Select(p => Used(new HubEntry(p.Label, "▣", () => AddPassLayer(p.Name, target()))
        {
            Thumb = PassThumb(p.Name),
            Alternate = () => PlacePassMask(p.Name, at),
            Detail = Strings.T("S_HubPassesHint"),
            Words = "pass",
        })).ToList();

        var fresh = new List<HubEntry>
        {
            Used(new HubEntry(Strings.T("S_AddAdjustment"), "◧", () =>
            {
                if (target() is { } after) AddAdjustmentLayer(after);
            })),
            Used(new HubEntry(Strings.T("S_NodeMenuImageLayer"), "▨", () => AddImageLayer(target()))),
            Used(new HubEntry(Strings.T("S_NodeMenuPictureFile"), "▭", () =>
            {
                if (ChoosePicture() is { } path) PlaceAt(new PictureNode { Path = path, FollowSequence = false }, at, link);
            })),
        };

        // Die Ebenen im Graphen: Ein Klick legt ihr Bild als neuen Knoten an, Umschalt
        // springt zu ihr.
        var inGraph = NodeLayerList.Of(_graph!)
            .Where(l => l.Target is not null)
            .Select(l => Used(new HubEntry(l.Name, "❏", () => AddLayerNode(l, at, link))
            {
                Thumb = LayerThumb(l),
                Detail = l.Detail + " – " + Strings.T("S_HubInGraphHint"),
                Alternate = () => OnLayerChosen(l.Target!),
            }))
            .ToList();

        categories.Add(new HubCategory("layers", Strings.T(NodeCatalog.Layers), "❏", new[]
        {
            new HubGroup(Strings.T("S_HubPasses"), passLayers, Strings.T("S_HubPassesHint")),
            new HubGroup(Strings.T("S_HubNew"), fresh),
            new HubGroup(Strings.T("S_HubBlocks"), NodeCatalog.All.Where(k => k.Group == NodeCatalog.Layers).Select(Kind).ToList()),
            new HubGroup(Strings.T("S_HubInGraph"), inGraph, Strings.T("S_HubInGraphHint")),
        }));

        // Masken: die Arten, jeder Pass als Maske, die Kryptomatten der Datei.
        var passMasks = passes.Select(p => Used(new HubEntry(p.Label, "◐", () => PlacePassMask(p.Name, at))
        {
            Thumb = PassThumb(p.Name),
            Words = "pass maske",
        })).ToList();

        var crypto = _cryptomattes.Select(set => Used(new HubEntry(Strings.T("S_NodeMenuCrypto", set.ShortName), "⬢", () =>
            PlaceAt(new MaskNode
            {
                Mask = new LayerMask
                {
                    Kind = MaskKind.Cryptomatte,
                    Source = set.Prefix,
                    Levels = Cryptomatte.Levels(_passes, set.Prefix).ToList(),
                },
            }, at, null)))).ToList();

        categories.Add(new HubCategory("masks", Strings.T(NodeCatalog.Masks), "◐", new[]
        {
            new HubGroup(Strings.T("S_HubBlocks"), NodeCatalog.All.Where(k => k.Group == NodeCatalog.Masks).Select(Kind).ToList()),
            new HubGroup(Strings.T("S_HubPassMasks"), passMasks),
            new HubGroup(Strings.T("S_HubCrypto"), crypto),
        }));

        // Die Effekte in der Ordnung der Palette - mit ihren Zeichen.
        foreach (var (group, glyph) in new[]
        {
            ("S_GroupBasics", "☀"), ("S_GroupLight", "✦"), ("S_GroupOptics", "◉"), ("S_GroupFilm", "⁙"), ("S_GroupTable", "⊞"),
        })
        {
            var kinds = NodeCatalog.All.Where(k => k.Group == group).Select(Kind).ToList();
            categories.Add(new HubCategory(group, Strings.T(group), glyph, new[] { new HubGroup(Strings.T("S_HubEffectsHint"), kinds) }));
        }

        categories.Add(new HubCategory("picture", Strings.T(NodeCatalog.Picture), "▣", new[]
        {
            new HubGroup(Strings.T("S_HubBlocks"), NodeCatalog.All.Where(k => k.Group == NodeCatalog.Picture).Select(Kind).ToList()),
        }));

        categories.Add(new HubCategory("convert", Strings.T(NodeCatalog.Convert), "⇄", new[]
        {
            new HubGroup(Strings.T("S_HubBlocks"), NodeCatalog.All.Where(k => k.Group == NodeCatalog.Convert).Select(Kind).ToList()),
        }));

        // Zuletzt: dieselben Eintraege noch einmal, in der Reihenfolge ihres Gebrauchs.
        var all = categories.SelectMany(c => c.Groups.SelectMany(g => g.Entries)).ToList();
        var recent = Recent.Select(title => all.FirstOrDefault(e => e.Title == title)).OfType<HubEntry>().ToList();

        categories.Insert(0, new HubCategory("recent", Strings.T("S_HubRecent"), "↺", new[] { new HubGroup(Strings.T("S_HubRecent"), recent) }));

        return categories;
    }

    private static void Remember(string title)
    {
        Recent.Remove(title);
        Recent.Insert(0, title);
        if (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1);
    }

    /// <summary>Was sich am gewaehlten Knoten tun laesst - die Leiste unten links.</summary>
    private IReadOnlyList<HubAction> NodeActions(Node node)
    {
        var actions = new List<HubAction>
        {
            new("⊘", Strings.T("S_HubMute"), Strings.T(node.Muted ? "S_NodeMenuUnmute" : "S_NodeMenuMute"), () =>
            {
                RememberNodes();
                node.Muted = !node.Muted;
                NodeView.InvalidateVisual();
                OnGraphChanged();
            }, node.Muted),
            new("◉", Strings.T("S_HubViewer"), Strings.T("S_NodeMenuView"), () => OnViewWanted(node), _viewer is var (id, _) && id == node.Id),
            new("▣", Strings.T("S_HubPreview"), Strings.T(node.Preview ? "S_NodeMenuPreviewOff" : "S_NodeMenuPreviewOn"),
                () => NodeView.TogglePreview(node), node.Preview),
        };

        if (node is not RenderNode)
            actions.Add(new("❐", Strings.T("S_HubDuplicate"), Strings.T("S_NodeMenuDuplicate"), () => NodeView.Duplicate(node)));

        actions.Add(new("✕", Strings.T("S_HubDelete"), Strings.T("S_NodeMenuDelete"), () => NodeView.Remove(node)));
        return actions;
    }

    /// <summary>Was die ganze Ansicht betrifft - die Leiste unten rechts.</summary>
    private IReadOnlyList<HubAction> ViewActions()
    {
        var actions = new List<HubAction>();

        if (_viewer is not null) actions.Add(new("◎", Strings.T("S_HubViewOff"), Strings.T("S_NodeMenuViewOff"), () => SetViewer(null), true));

        actions.Add(new("▦", Strings.T("S_HubPreviews"), Strings.T("S_NodeMenuPreviewAll"), PreviewAllLayers));
        actions.Add(new("⊞", Strings.T("S_HubArrange"), Strings.T("S_NodeMenuArrange"), () =>
        {
            RememberNodes();
            NodeLayout.Arrange(_graph!);
            NodeView.Frame();
            KeepNodes();
        }));
        actions.Add(new("⤢", Strings.T("S_HubFrame"), Strings.T("S_NodeMenuFrame"), NodeView.Frame));
        actions.Add(new("↻", Strings.T("S_HubRebuild"), Strings.T("S_NodeMenuRebuild"), RebuildFromStack));

        return actions;
    }

    /// <summary>
    /// Legt einen neuen Knoten an eine Stelle des Graphen - oder, wenn der Rechtsklick auf
    /// einem Kabel war, hinein. Passt er dort nicht, liegt er frei daneben.
    /// </summary>
    private void PlaceAt(Node node, Point at, NodeLink? link)
    {
        if (_graph is null) return;

        RememberNodes();

        _graph.Add(node);
        node.X = Math.Round(at.X - NodeLayout.Width / 2);
        node.Y = Math.Round(at.Y - NodeLayout.Header / 2);

        NodeCatalog.WireData(_graph, node);
        NodeView.Select(node);

        // Faellt er ins Kabel, meldet der Editor die Aenderung selbst.
        if (link is not null && _graph.Links.Contains(link) && NodeView.Land(node, link)) return;

        NodeView.InvalidateVisual();
        AfterNodeEdit();
    }

    /// <summary>
    /// Eine Ebene aus dem Graphen noch einmal als Knoten: ihr Bild, frei an der Stelle -
    /// eine Bilddatei als neue Bilddatei, ein Pass als Platzieren mit seinem Kabel von der
    /// Datei (die es nur einmal gibt). Eine Ebene ohne eigenes Bild - eine Einstellungsebene,
    /// eine Gruppe - wird samt Zweig verdoppelt.
    /// </summary>
    private void AddLayerNode(NodeLayer layer, Point at, NodeLink? link)
    {
        if (_graph is null) return;

        switch (layer.Origin)
        {
            case ({ } picture, _) when picture is PictureNode file:
                PlaceAt(new PictureNode { Path = file.Path, FollowSequence = file.FollowSequence }, at, link);
                return;

            case ({ } source, { } output) when source is RenderNode:
            {
                RememberNodes();

                var place = _graph.Add(new PlaceNode { Preview = true });
                place.X = Math.Round(at.X - NodeLayout.Width / 2);
                place.Y = Math.Round(at.Y - NodeLayout.Header / 2);

                if (output != RenderNode.Picture && source is RenderNode render) NodeEdits.ShowPass(_graph, render, output, on: true);
                _graph.Connect(source, output, place, "Bild");

                NodeView.Select(place);
                NodeView.InvalidateVisual();
                AfterNodeEdit();
                return;
            }
        }

        if (layer.Switch is { } whole) DuplicateLayer(whole);
    }

    /// <summary>Ein Pass als Maske - frei an der Stelle, mit seinem Kabel von der Datei.</summary>
    private void PlacePassMask(string pass, Point at)
        => PlaceAt(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Pass, Source = pass } }, at, null);
}
