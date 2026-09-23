using System.IO;
using System.Windows;
using System.Windows.Controls;
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

                var file = new MenuItem { Header = Strings.T("S_NodeMenuPictureFile") };
                file.Click += (_, _) =>
                {
                    if (ChoosePicture() is { } path) Place(new PictureNode { Path = path, FollowSequence = false }, at);
                };
                item.Items.Add(file);
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

            var delete = new MenuItem { Header = Strings.T("S_NodeMenuDelete") };
            delete.Click += (_, _) => NodeView.Remove(node);
            menu.Items.Add(delete);

            menu.Items.Add(new Separator());
        }

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

    /// <summary>
    /// Ein Bild als Ebene: Bilddatei, Platzieren und Mischen, hinter den gewaehlten
    /// Knoten gesetzt. Drei Knoten und vier Kabel in einem Griff - so haeufig, wie ein
    /// Logo ins Bild soll, waere alles andere eine Schikane.
    /// </summary>
    private void AddImageLayer()
    {
        if (_graph is null || ChoosePicture() is not { } path) return;

        var after = NodeView.Selected is { } chosen && chosen is not OutputNode &&
                    NodeEdits.Through(chosen).Output is not null
            ? chosen
            : _graph.Output is { } output && _graph.Into(output.Id, "Bild") is { } last
                ? _graph.Find(last.From)
                : null;

        if (after is null) return;

        RememberNodes();

        var mix = _graph.Add(new MixNode());
        var place = _graph.Add(new PlaceNode());
        var picture = _graph.Add(new PictureNode { Path = path, FollowSequence = false });

        mix.X = after.X + NodeLayout.ColumnStep;
        mix.Y = after.Y;
        place.X = after.X;
        place.Y = after.Y - NodeLayout.Height(place) - NodeLayout.Gap;
        picture.X = place.X - NodeLayout.ColumnStep;
        picture.Y = place.Y;

        NodeEdits.InsertAfter(_graph, after, mix);
        NodeEdits.MakeRoom(_graph, mix, NodeLayout.ColumnStep);

        _graph.Connect(picture, "Bild", place, "Bild");
        _graph.Connect(place, "Bild", mix, "Oben");

        NodeView.Select(mix);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
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
