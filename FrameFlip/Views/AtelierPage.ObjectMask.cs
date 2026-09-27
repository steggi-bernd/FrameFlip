using System.Windows;
using System.Windows.Input;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die Objektmaske per Klick (R4 im Werkzeugplan): Eine Ebene in der Liste waehlen, den Chip
/// "Objektmaske" druecken, ein Objekt im Bild anklicken - und aus der Kryptomatte entsteht die
/// Maske DIESER Ebene. Weitere Klicks nehmen Objekte hinzu, ein zweiter Klick auf dasselbe
/// nimmt es wieder heraus.
///
/// Bisher ging das nur ueber Umwege: "Objekt hier als Maske" legte eine neue Maskenebene an,
/// und wer die Maske an einer vorhandenen Ebene wollte, musste sie im Graphen verbinden.
/// </summary>
public partial class AtelierPage
{
    /// <summary>
    /// Woran gerade gewaehlt wird: die Ebene, die die Maske bekommt - null, dann entsteht beim
    /// ersten Klick eine eigene Maskenebene -, und aus welcher Kryptomatte.
    /// </summary>
    private (MixNode? Layer, CryptomatteSet Set)? _objectPick;

    /// <summary>Ob ein Klick ins Bild gerade ein Objekt fuer eine Maske waehlt - fuer die Probe.</summary>
    internal bool ObjectPicking => _objectPick is not null;

    /// <summary>
    /// Die Kryptomatte fuer Objekte: die, deren Name nach Objekt klingt - Blender nennt sie
    /// CryptoObject -, sonst die erste. Material und Asset waehlt man ueber das Bildmenue.
    /// </summary>
    private CryptomatteSet? ObjectSet()
        => _cryptomattes.FirstOrDefault(s => s.ShortName.Contains("obj", StringComparison.OrdinalIgnoreCase))
           ?? _cryptomattes.FirstOrDefault();

    /// <summary>Die Ebene, die in der Liste gewaehlt ist - ihr Mischen. Die Grundlage hat keines.</summary>
    private MixNode? ChosenLayer()
        => _graph is not null && SelectedNode is MixNode mix && LayerEdits.ChainOf(LayerEdits.Chains(_graph), mix) is not null
            ? mix
            : null;

    /// <summary>
    /// Beginnt das Waehlen: Das Werkzeug wird "Auswaehlen", und ueber dem Bild steht, was ein
    /// Klick jetzt tut. Ohne Kryptomatte in der Datei gibt es nichts zu waehlen.
    /// </summary>
    internal bool StartObjectMask(MixNode? layer, CryptomatteSet? set = null)
    {
        if (!InNodes || _graph is null || (set ?? ObjectSet()) is not { } chosen) return false;

        MouseTools.Select(AtelierTool.Select);
        OnToolChanged(AtelierTool.Select);

        _objectPick = (layer, chosen);

        PreviewKeyDown -= OnObjectPickKey;
        PreviewKeyDown += OnObjectPickKey;

        ShowObjectBadge();
        return true;
    }

    /// <summary>Hoert auf zu waehlen - mit "Fertig", Escape, einem anderen Werkzeug oder einem anderen Bild.</summary>
    internal void EndObjectMask()
    {
        if (_objectPick is null) return;

        _objectPick = null;
        PreviewKeyDown -= OnObjectPickKey;
        ObjectBadge.Visibility = Visibility.Collapsed;
    }

    private void OnObjectPickKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        EndObjectMask();
        e.Handled = true;
    }

    private void OnObjectPickDone(object sender, RoutedEventArgs e) => EndObjectMask();

    private void ShowObjectBadge()
    {
        if (_objectPick is not { } pick) return;

        ObjectBadgeText.Text = pick.Layer is { } layer
            ? Strings.T("S_ObjectPickLayer", LayerName(layer))
            : Strings.T("S_ObjectPickNew");

        ObjectBadge.Visibility = Visibility.Visible;
    }

    /// <summary>Wie eine Ebene in der Liste heisst.</summary>
    private string LayerName(MixNode mix)
        => _graph is null ? mix.Label ?? "" : NodeLayerList.Of(_graph).FirstOrDefault(l => ReferenceEquals(l.Mix, mix))?.Name ?? mix.Label ?? "";

    /// <summary>
    /// Ein Klick ins Bild, waehrend gewaehlt wird: das Objekt an diesem Bildpunkt in die Maske
    /// der Ebene - oder, wenn es schon darin ist, wieder heraus. Hat die Ebene noch keine
    /// Objektmaske, entsteht eine und kommt in ihren Faktor; eine andere Maske, die dort
    /// steckte, bleibt frei im Graphen stehen. False, wenn dort kein Objekt ist.
    /// </summary>
    internal bool ObjectMaskAt(int x, int y)
    {
        if (_objectPick is not { } pick || _graph is null || _path is null) return false;

        var levels = Cryptomatte.Levels(_passes, pick.Set.Prefix).ToList();
        if (levels.Count == 0) return false;

        // Die erste Stufe traegt das Objekt mit dem groessten Anteil - wie beim Bildmenue.
        var frame = _sources.TryGetValue(levels[0], out var known) ? known : FloatFrame.FromExrPass(_path, levels[0]);
        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;

        float id = frame.R[y * frame.Width + x];
        if (id == 0f) return false;

        string name = pick.Set.NameOf(id) ?? Strings.T("S_MaskLayerName");

        // Keine Ebene gewaehlt: eine eigene Maskenebene wie bisher - und an ihr geht es weiter.
        if (pick.Layer is not { } layer || !_graph.Nodes.Contains(layer))
        {
            var fresh = new LayerMask { Kind = MaskKind.Cryptomatte, Source = pick.Set.Prefix, Levels = levels };
            fresh.TogglePick(name, id);

            if (AddNodeMaskLayer(fresh, name) is not { } made) return false;

            _objectPick = (LayersOf(made).FirstOrDefault(), pick.Set);
            ShowObjectBadge();
            return true;
        }

        RememberNodes();

        if (_graph.Into(layer.Id, "Faktor") is { } factor &&
            _graph.Find(factor.From) is MaskNode { Mask: { Kind: MaskKind.Cryptomatte } own } &&
            own.Source == pick.Set.Prefix)
        {
            own.TogglePick(name, id);
        }
        else
        {
            var mask = _graph.Add(new MaskNode
            {
                Mask = new LayerMask { Kind = MaskKind.Cryptomatte, Source = pick.Set.Prefix, Levels = levels },
                Preview = true,
            });

            mask.Mask.TogglePick(name, id);
            mask.Mask.EnsureId();

            _graph.Connect(mask, "Maske", layer, "Faktor");

            mask.X = layer.X - NodeLayout.ColumnStep;
            mask.Y = layer.Y + NodeLayout.Height(layer) + NodeLayout.Gap;
        }

        NodeView.InvalidateVisual();
        AfterNodeEdit();
        ShowObjectBadge();

        return true;
    }
}
