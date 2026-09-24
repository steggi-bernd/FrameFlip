using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Am Graphen bauen: hinzufuegen, einfuegen, loeschen, rueckgaengig machen.
///
/// Der Editor meldet, WAS geschehen soll oder geschehen ist; hier steht, was daraus fuer
/// die Seite folgt - neu rechnen, fehlende Quellen nachlesen, den Stand festhalten. Und
/// hier steht der Verlauf: Ein Graph, an dem man Kabel zieht, braucht "Rueckgaengig"
/// dringender als ein Stapel, an dem man nur Regler schiebt.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Staende vor den letzten Aenderungen am Aufbau - als Text.</summary>
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    /// <summary>Wie weit zurueck es geht. Ein Graph ist ein paar Kilobyte Text.</summary>
    private const int HistoryDepth = 80;

    private void SetUpNodeEditing()
    {
        NodeView.Translate = key => Strings.T(key);
        NodeView.Editing += RememberNodes;
        NodeView.MenuWanted += ShowNodeMenu;
        NodeView.SectionDropped += DropFromPalette;
        NodeView.ViewWanted += OnViewWanted;
        SetUpNodePreviews();
        SetUpNodeLayerList();
        NodeView.UndoWanted += () => StepNodes(back: true);
        NodeView.RedoWanted += () => StepNodes(back: false);

        Tools.NodeToolWanted += InsertFromPalette;
    }

    /// <summary>Haelt den Stand fest, bevor sich am Aufbau etwas aendert.</summary>
    private void RememberNodes()
    {
        if (_graph is null) return;

        _undo.Add(_graph.Save());
        if (_undo.Count > HistoryDepth) _undo.RemoveAt(0);

        _redo.Clear();
    }

    /// <summary>Einen Schritt zurueck oder wieder vor.</summary>
    public void StepNodes(bool back)
    {
        if (_graph is null) return;

        var from = back ? _undo : _redo;
        var to = back ? _redo : _undo;

        if (from.Count == 0) return;

        string state = from[^1];
        from.RemoveAt(from.Count - 1);

        if (NodeGraph.Load(state) is not { } graph) return;

        to.Add(_graph.Save());

        _graph = graph;
        NodeView.Replace(graph);

        AfterNodeEdit();
    }

    /// <summary>
    /// Nach jeder Aenderung am Aufbau: festhalten, nachlesen, was fehlt, neu rechnen,
    /// und sagen, wenn der Graph kein Bild mehr ergibt.
    /// </summary>
    private void AfterNodeEdit()
    {
        KeepNodes();
        ShowNodeWarning();
        ShowNodeSettings();
        ShowNodeLayers();
        ShowMissingLayers();
        KeepViewer();
        FetchNodeSources();
    }

    /// <summary>Ein Hinweis im Editor, wenn der Graph kein Bild ergibt.</summary>
    private void ShowNodeWarning()
    {
        if (_graph is null) return;

        NodeView.Warning = _graph.Output is { } output && _graph.Into(output.Id, "Bild") is null
            ? Strings.T("S_NodeWarnOutput")
            : null;
    }

    // ------------------------------------------------------------ Hinzufuegen

    /// <summary>
    /// Ein Effekt aus der Palette: Er kommt hinter den gewaehlten Knoten, und das Bild
    /// laeuft durch ihn weiter - so, wie er im Stapel dazugekommen waere. Ohne Wahl kommt
    /// er als letzter vor die Ausgabe.
    /// </summary>
    private void InsertFromPalette(string section)
    {
        if (_graph is null || NodeCatalog.ForSection(section) is not { } kind) return;

        var after = NodeView.Selected is { } chosen && chosen is not OutputNode &&
                    NodeEdits.Through(chosen).Output is not null
            ? chosen
            : _graph.Output is { } output && _graph.Into(output.Id, "Bild") is { } last
                ? _graph.Find(last.From)
                : null;

        if (after is null) return;

        RememberNodes();

        var node = kind.Create();
        _graph.Add(node);

        node.X = after.X + NodeLayout.ColumnStep;
        node.Y = after.Y;

        NodeEdits.InsertAfter(_graph, after, node);
        NodeEdits.MakeRoom(_graph, node, NodeLayout.ColumnStep);
        NodeCatalog.WireData(_graph, node);

        NodeView.Select(node);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Ein Effekt aus der Palette, in den Editor gezogen: frei dort, wo er losgelassen
    /// wurde - oder in das Kabel darunter, wie ein freier Knoten, den man darauf legt.
    /// </summary>
    private void DropFromPalette(string section, Point at, NodeLink? link)
    {
        if (_graph is null || NodeCatalog.ForSection(section) is not { } kind) return;

        RememberNodes();

        var node = _graph.Add(kind.Create());
        node.X = at.X;
        node.Y = at.Y;

        NodeCatalog.WireData(_graph, node);
        NodeView.Select(node);

        // Faellt er ins Kabel, meldet der Editor die Aenderung selbst.
        if (link is not null && NodeView.Land(node, link)) return;

        NodeView.InvalidateVisual();
        AfterNodeEdit();
    }

    /// <summary>Legt einen neuen Knoten an eine Stelle im Graphen - frei, noch nicht verbunden.</summary>
    private Node Place(Node node, Point at)
    {
        RememberNodes();

        _graph!.Add(node);
        node.X = Math.Round(at.X - NodeLayout.Width / 2);
        node.Y = Math.Round(at.Y - NodeLayout.Header / 2);

        NodeCatalog.WireData(_graph, node);

        NodeView.Select(node);
        NodeView.InvalidateVisual();

        AfterNodeEdit();

        return node;
    }

    /// <summary>
    /// Das Menue im Editor: hinzufuegen nach Gruppen, und fuer den gewaehlten Knoten
    /// stummschalten und loeschen.
    /// </summary>
    private void ShowNodeMenu(Point at)
    {
        if (_graph is null) return;

        // Die Passe im Menue bekommen Miniaturen - beim ersten Oeffnen entstehen sie.
        MakePassThumbs();

        var menu = new ContextMenu();

        foreach (var group in NodeCatalog.All.GroupBy(k => k.Group))
        {
            var item = new MenuItem { Header = Strings.T(group.Key) };

            foreach (var kind in group)
            {
                var entry = new MenuItem { Header = Strings.T(kind.TitleKey) };
                entry.Click += (_, _) => Place(kind.Create(), at);
                item.Items.Add(entry);
            }

            if (group.Key == NodeCatalog.Layers)
            {
                item.Items.Add(new Separator());

                var layer = new MenuItem { Header = Strings.T("S_NodeMenuImageLayer") };
                layer.Click += (_, _) => AddImageLayer();
                item.Items.Add(layer);

                if (PassMenu("S_NodeMenuPassLayer", pass => AddPassLayer(pass)) is { } passLayer) item.Items.Add(passLayer);

                var file = new MenuItem { Header = Strings.T("S_NodeMenuPictureFile") };
                file.Click += (_, _) =>
                {
                    if (ChoosePicture() is { } path) Place(new PictureNode { Path = path, FollowSequence = false }, at);
                };
                item.Items.Add(file);
            }

            if (group.Key == NodeCatalog.Masks)
            {
                var extra = new List<object>();

                if (PassMenu("S_NodeMenuPassMask", pass => Place(new MaskNode
                    {
                        Mask = new LayerMask { Kind = MaskKind.Pass, Source = pass },
                    }, at)) is { } passMask)
                {
                    extra.Add(passMask);
                }

                foreach (var set in _cryptomattes)
                {
                    var crypto = new MenuItem { Header = Strings.T("S_NodeMenuCrypto", set.ShortName) };
                    crypto.Click += (_, _) => Place(new MaskNode
                    {
                        Mask = new LayerMask
                        {
                            Kind = MaskKind.Cryptomatte,
                            Source = set.Prefix,
                            Levels = Cryptomatte.Levels(_passes, set.Prefix).ToList(),
                        },
                    }, at);
                    extra.Add(crypto);
                }

                if (extra.Count > 0)
                {
                    item.Items.Add(new Separator());
                    foreach (var entry in extra) item.Items.Add(entry);
                }
            }

            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        if (NodeView.Selected is { } node and not OutputNode)
        {
            var mute = new MenuItem { Header = Strings.T(node.Muted ? "S_NodeMenuUnmute" : "S_NodeMenuMute") };
            mute.Click += (_, _) =>
            {
                RememberNodes();
                node.Muted = !node.Muted;
                NodeView.InvalidateVisual();
                OnGraphChanged();
            };
            menu.Items.Add(mute);

            var view = new MenuItem { Header = Strings.T("S_NodeMenuView") };
            view.Click += (_, _) => OnViewWanted(node);
            menu.Items.Add(view);

            var preview = new MenuItem { Header = Strings.T(node.Preview ? "S_NodeMenuPreviewOff" : "S_NodeMenuPreviewOn") };
            preview.Click += (_, _) => NodeView.TogglePreview(node);
            menu.Items.Add(preview);

            if (node is not RenderNode)
            {
                var duplicate = new MenuItem { Header = Strings.T("S_NodeMenuDuplicate") };
                duplicate.Click += (_, _) => NodeView.Duplicate(node);
                menu.Items.Add(duplicate);
            }

            var delete = new MenuItem { Header = Strings.T("S_NodeMenuDelete") };
            delete.Click += (_, _) => NodeView.Remove(node);
            menu.Items.Add(delete);

            menu.Items.Add(new Separator());
        }

        if (_viewer is not null)
        {
            var viewOff = new MenuItem { Header = Strings.T("S_NodeMenuViewOff") };
            viewOff.Click += (_, _) => SetViewer(null);
            menu.Items.Add(viewOff);
        }

        var previews = new MenuItem { Header = Strings.T("S_NodeMenuPreviewAll") };
        previews.Click += (_, _) => PreviewAllLayers();
        menu.Items.Add(previews);

        var rebuild = new MenuItem { Header = Strings.T("S_NodeMenuRebuild") };
        rebuild.Click += (_, _) => RebuildFromStack();
        menu.Items.Add(rebuild);

        var arrange = new MenuItem { Header = Strings.T("S_NodeMenuArrange") };
        arrange.Click += (_, _) =>
        {
            RememberNodes();
            NodeLayout.Arrange(_graph);
            NodeView.Frame();
            KeepNodes();
        };
        menu.Items.Add(arrange);

        var frame = new MenuItem { Header = Strings.T("S_NodeMenuFrame") };
        frame.Click += (_, _) => NodeView.Frame();
        menu.Items.Add(frame);

        menu.PlacementTarget = NodeView;
        menu.IsOpen = true;
    }

    /// <summary>Ein Untermenue mit den Passen der Datei - oder null, wenn sie keine hat.</summary>
    private MenuItem? PassMenu(string titleKey, Action<string> chosen)
    {
        var passes = NodePasses();
        if (passes.Count == 0) return null;

        var menu = new MenuItem { Header = Strings.T(titleKey) };

        foreach (var (name, label) in passes)
        {
            // Unterstriche waeren sonst Zugriffstasten - "Diff_Col" verloere seinen Strich.
            var entry = new MenuItem { Header = label.Replace("_", "__") };

            if (PassThumb(name) is { } thumb)
                entry.Icon = new System.Windows.Controls.Image { Source = thumb, Width = 48, Height = 27, Stretch = System.Windows.Media.Stretch.Uniform };

            entry.Click += (_, _) => chosen(name);
            menu.Items.Add(entry);
        }

        return menu;
    }

    /// <summary>
    /// Worauf eine neue Ebene kommt: auf den gewaehlten Knoten, wenn er ein Bild liefert -
    /// sonst oben auf die Ebenen, vor die Werkzeuge am Bild, wo sie im Stapel auch laege.
    /// </summary>
    private Node? LayerTarget()
    {
        if (_graph is null) return null;

        if (NodeView.Selected is { } chosen && chosen is not OutputNode && NodeEdits.Through(chosen).Output is not null)
            return chosen;

        return NodeEdits.LayerTop(_graph);
    }

    /// <summary>
    /// Ein Bild als Ebene: Bilddatei, Platzieren und Mischen, hinter den gewaehlten
    /// Knoten gesetzt - oder hinter <paramref name="target"/>. Drei Knoten und vier Kabel
    /// in einem Griff - so haeufig, wie ein Logo ins Bild soll, waere alles andere eine
    /// Schikane.
    /// </summary>
    private void AddImageLayer(Node? target = null)
    {
        if (_graph is null || (target ?? LayerTarget()) is not { } after || ChoosePicture() is not { } path) return;

        RememberNodes();

        bool clip = LayerEdits.InClip(_graph, after);
        var picture = _graph.Add(new PictureNode { Path = path, FollowSequence = false });

        if (NodeEdits.AddLayer(_graph, after, picture, "Bild", BlendMode.Normal) is not var (place, mix)) return;

        // In einer Schnittkette wird die neue Ebene angeschnitten wie ihre Nachbarn.
        mix.Clip = clip;

        ArrangeLayer(after, picture, place, mix);
        picture.X = place.X - NodeLayout.ColumnStep;
        picture.Y = place.Y;

        NodeView.Select(mix);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Ein Pass als Ebene - wie im Ebenenstreifen auf Addieren: Die Passe einer Datei
    /// setzen das Bild zusammen, und das Licht eines Passes kommt zum Bisherigen dazu.
    /// </summary>
    private void AddPassLayer(string pass, Node? target = null)
    {
        if (_graph?.Nodes.OfType<RenderNode>().FirstOrDefault() is not { } file || (target ?? LayerTarget()) is not { } after) return;

        RememberNodes();

        bool clip = LayerEdits.InClip(_graph, after);

        if (!NodeEdits.ShowPass(_graph, file, pass, on: true) ||
            NodeEdits.AddLayer(_graph, after, file, pass, BlendMode.Add) is not var (place, mix))
        {
            return;
        }

        mix.Clip = clip;

        ArrangeLayer(after, file, place, mix);

        NodeView.Select(mix);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Mischen rechts neben den Knoten, auf den es kommt; Platzieren eine Spalte davor und
    /// darueber. Kommt die Ebene aus demselben Knoten - ein Pass auf die Datei selbst -,
    /// steht Platzieren eine Spalte weiter rechts, sonst liefe sein Kabel rueckwaerts.
    /// </summary>
    private void ArrangeLayer(Node after, Node source, Node place, MixNode mix)
    {
        int columns = source.X >= after.X - 1 ? 2 : 1;

        mix.X = after.X + NodeLayout.ColumnStep;
        mix.Y = after.Y;
        NodeEdits.MakeRoom(_graph!, mix, columns * NodeLayout.ColumnStep);

        mix.X = after.X + columns * NodeLayout.ColumnStep;
        place.X = mix.X - NodeLayout.ColumnStep;
        place.Y = after.Y - NodeLayout.Height(place) - NodeLayout.Gap;
    }

    /// <summary>Fragt nach einer Bilddatei. Null, wenn niemand eine waehlt.</summary>
    private string? ChoosePicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_NodeMenuPictureFile"),
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.exr;*.bmp|Alle Dateien|*.*",
            InitialDirectory = _path is null ? null : Path.GetDirectoryName(_path),
        };

        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }
}
