using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // Strg+Z gilt auf der ganzen Seite - nicht nur, solange der Editor den Fokus hat.
        // Nach einem Klick in die Ebenenliste, den Farbstreifen oder den Hub lag er
        // woanders, und Strg+Z tat nichts.
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window && !ReferenceEquals(window, _keyWindow))
            {
                if (_keyWindow is not null) _keyWindow.PreviewKeyDown -= OnWindowKey;
                _keyWindow = window;
                window.PreviewKeyDown += OnWindowKey;
            }
        };

        Unloaded += (_, _) =>
        {
            if (_keyWindow is not null) _keyWindow.PreviewKeyDown -= OnWindowKey;
            _keyWindow = null;
        };

        NodeView.Translate = key => Strings.T(key);
        NodeView.Editing += RememberNodes;
        NodeView.MenuWanted += OnNodeMenuWanted;
        NodeView.SectionDropped += DropFromPalette;
        NodeView.ViewWanted += OnViewWanted;
        SetUpNodePreviews();
        SetUpNodeLayerList();
        SetUpPictureMenu();
        SetUpLevels();
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

    private Window? _keyWindow;

    /// <summary>Strg+Z, Strg+Y und Strg+Umschalt+Z im Knotenmodus - ausser in einem Textfeld, das sein eigenes hat.</summary>
    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;

        // Strg+S speichert das Projekt - in beiden Modi, nicht nur bei den Knoten.
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            SaveProject();
            e.Handled = true;
            return;
        }

        if (HandleUndoKey(e.Key, Keyboard.Modifiers, e.OriginalSource)) e.Handled = true;
    }

    /// <summary>Strg+Z und Co. - getrennt vom Tastenereignis, damit die Probe es ohne Tastatur pruefen kann.</summary>
    internal bool HandleUndoKey(Key key, ModifierKeys modifiers, object? source)
    {
        if (!InNodes || source is System.Windows.Controls.Primitives.TextBoxBase) return false;
        if ((modifiers & ModifierKeys.Control) == 0) return false;

        bool shift = (modifiers & ModifierKeys.Shift) != 0;

        if (key == Key.Z) StepNodes(back: !shift);
        else if (key == Key.Y) StepNodes(back: false);
        else return false;

        return true;
    }

    /// <summary>Ob gerade an einem Wert gezogen wird - der Stand davor liegt dann schon im Verlauf.</summary>
    private bool _valueEditOpen;

    /// <summary>
    /// Vor einer Aenderung an einem Wert - Regler, Mischung, Pinselstrich, Verschieben: Der
    /// zuletzt festgehaltene Stand kommt in den Verlauf, einmal je Zug. Der festgehaltene,
    /// nicht der jetzige: Wenn die Meldung kommt, hat sich der Wert schon geaendert.
    /// </summary>
    private void RememberValueEdit()
    {
        if (_valueEditOpen || _graph is null || _recipe.Nodes is not { } before) return;

        // Nichts geaendert - etwa ein Farbstreifen, der sich beim Waehlen eines Knotens
        // fuellt und dabei meldet: kein Schritt im Verlauf.
        if (_graph.Save() == before) return;

        _valueEditOpen = true;

        if (_undo.Count > 0 && _undo[^1] == before) return;

        _undo.Add(before);
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

        _valueEditOpen = false;
        to.Add(_graph.Save());

        _graph = graph;
        ShowGraph(graph);

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

        // Namen, Aufbau und die Auswahl einer Kryptomatte koennen sich geaendert haben, ohne dass
        // das Ziel wechselte.
        ShowTargetPath();
        ShowCryptoView();

        // Erst grob, dann voll - wie beim Ziehen an einem Regler. Der Editor zeigt die
        // Aenderung sofort, das Bild zieht im naechsten Bild nach, scharf nach einer Pause.
        // Voll und sofort hielt jeder Klick die Seite an, bis das ganze Bild gerechnet war.
        FetchNodeSources(soon: true);
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

        var after = SelectedNode is { } chosen && chosen is not OutputNode &&
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

    /// <summary>
    /// Worauf eine neue Ebene kommt: auf den gewaehlten Knoten, wenn er ein Bild liefert -
    /// sonst oben auf die Ebenen, vor die Werkzeuge am Bild, wo sie im Stapel auch laege.
    /// </summary>
    private Node? LayerTarget()
    {
        if (_graph is null) return null;

        if (SelectedNode is { } chosen && chosen is not OutputNode && NodeEdits.Through(chosen).Output is not null)
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
        var picture = _graph.Add(new PictureNode { Path = path, FollowSequence = false, Preview = true });

        if (NodeEdits.AddLayer(_graph, after, picture, "Bild", ImageProbe.ModeFor(path)) is not var (_, mix)) return;

        // In einer Schnittkette wird die neue Ebene angeschnitten wie ihre Nachbarn.
        mix.Clip = clip;

        ArrangeLayer();

        NodeView.Select(mix);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Ein Pass als Ebene - wie im Ebenenstreifen auf Addieren, oder auf dem, was der Name
    /// verraet (PassRoles: AO multipliziert, Glare addiert). Die Passe einer Datei
    /// setzen das Bild zusammen, und das Licht eines Passes kommt zum Bisherigen dazu.
    /// </summary>
    private void AddPassLayer(string pass, Node? target = null)
    {
        if (_graph is null || (target ?? LayerTarget()) is not { } after) return;

        RememberNodes();

        bool clip = LayerEdits.InClip(_graph, after);

        // Jeder Pass hat seine eigene Quelle am linken Rand - liest ihn schon eine Ebene, dieselbe.
        var source = NodeGroups.PassSource(_graph, pass);

        if (NodeEdits.AddLayer(_graph, after, source, pass, PassRoles.ByName(pass, sceneLinear: true) ?? BlendMode.Add) is not var (_, mix))
        {
            if (!_graph.Links.Any(l => l.From == source.Id)) _graph.Nodes.Remove(source);
            return;
        }

        mix.Clip = clip;

        ArrangeLayer();

        NodeView.Select(mix);
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Nach einer neuen Ebene: Der Graph wird in Gruppen neu angeordnet - die Ebene bekommt
    /// ihren Rahmen an ihrer Stelle in der Folge, ihre Quelle steht links bei den anderen.
    /// </summary>
    private void ArrangeLayer()
    {
        if (_graph is null) return;

        NodeLayout.Arrange(_graph);
        NodeView.InvalidateVisual();
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
