using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Views;

/// <summary>
/// Vorschauen im Knotenmodus: das kleine Bild auf einem Knoten, die Miniaturen der
/// Ebenenliste und die der Passe am Dateiknoten.
///
/// Die Bilder auf den Knoten und in der Liste liest der Auswerter beim Rechnen ab - sie
/// kosten keine eigene Rechnung. Die Passe dagegen stehen noch nicht im Vorrat, solange
/// keiner benutzt wird; ihre Miniaturen werden einmal je Datei im Hintergrund gelesen,
/// verkleinert und ohne das volle Bild zu behalten.
/// </summary>
public partial class AtelierPage
{
    private readonly NodePreviews _previews = new();
    private readonly Dictionary<string, (long Version, ImageSource Image)> _previewImages = new(StringComparer.Ordinal);
    private long _previewsShown = -1;

    private void SetUpNodePreviews()
    {
        NodeView.PreviewOf = PreviewImage;
        NodeView.PreviewToggled += OnPreviewToggled;

        NodeLayers.Chosen += OnLayerChosen;
        NodeLayers.MuteWanted += OnLayerMuted;
    }

    /// <summary>
    /// Was die naechste Rechnung ablesen soll: die eingeschalteten Vorschauen und die
    /// Miniaturen der Ebenenliste - das, was in jedes Mischen oben hineinfliesst.
    /// </summary>
    private void WantPreviews()
    {
        _previews.Wanted.Clear();
        if (_graph is null) return;

        foreach (var node in _graph.Nodes)
            if (node.Preview) _previews.Wanted.Add(node.Id);

        foreach (var layer in NodeLayerList.Of(_graph))
            if (layer.Source is { } source) _previews.Wanted.Add(source.Id);

        _previews.Keep(_graph.Nodes.Select(n => n.Id));
    }

    /// <summary>Das kleine Bild eines Knotens - einmal je neuer Vorschau in ein Bild umgesetzt.</summary>
    private ImageSource? PreviewImage(Node node)
    {
        if (_previews.For(node.Id) is not { } thumb) return null;

        if (_previewImages.TryGetValue(node.Id, out var known) && known.Version == thumb.Version) return known.Image;

        var image = BitmapSource.Create(thumb.Width, thumb.Height, 96, 96, PixelFormats.Bgra32, null,
                                        thumb.Bgra, thumb.Width * 4);
        image.Freeze();

        _previewImages[node.Id] = (thumb.Version, image);
        return image;
    }

    /// <summary>Nach einer Rechnung: Sind neue Vorschauen da, werden sie gezeigt.</summary>
    private void ShowPreviews()
    {
        if (_previews.Version == _previewsShown) return;

        _previewsShown = _previews.Version;

        NodeView.InvalidateVisual();
        ShowNodeLayers();
    }

    /// <summary>Die Ebenenliste im Reiter der Ebenen - mit dem gewaehlten Knoten hervorgehoben.</summary>
    private void ShowNodeLayers()
    {
        if (_graph is null) return;

        NodeLayers.Show(NodeLayerList.Of(_graph), NodeView.Selected, PreviewImage);
        ShowLayerCount();
    }

    /// <summary>
    /// Die Vorschau eines Knotens wurde umgeschaltet. Was der Zwischenspeicher
    /// auslaesst, wird nicht gerechnet und liefert kein Bild - also einmal alles.
    /// </summary>
    private void OnPreviewToggled(Node node)
    {
        _cache.Clear();
        KeepNodes();
        Refresh(interim: false, recompose: true);
    }

    private void OnLayerChosen(Node node)
    {
        NodeView.Select(node);
        NodeView.Reveal(node);
        ShowNodeLayers();
    }

    private void OnLayerMuted(MixNode mix)
    {
        RememberNodes();
        mix.Muted = !mix.Muted;
        NodeView.InvalidateVisual();

        AfterNodeEdit();
    }

    /// <summary>
    /// Schaltet die Vorschau an allen Knoten ein, die eine Ebene liefern - dem, was in ein
    /// Mischen oben hineinfliesst, und jeder Maske -, und ordnet neu an, weil die Knoten
    /// dabei hoeher werden.
    /// </summary>
    private void PreviewAllLayers()
    {
        if (_graph is null) return;

        RememberNodes();

        foreach (var layer in NodeLayerList.Of(_graph))
            if (layer.Source is { } source && source is not (RenderNode or BlackNode)) source.Preview = true;

        foreach (var node in _graph.Nodes.Where(n => n is MaskNode or MaskMathNode or MaskShapeNode or MapRangeNode))
            node.Preview = true;

        NodeLayout.Arrange(_graph);
        NodeView.Frame();

        OnPreviewToggled(_graph.Nodes[0]);
    }

    // ------------------------------------------------------------ Passe

    private readonly Dictionary<string, ImageSource> _passThumbs = new(StringComparer.Ordinal);

    /// <summary>Fuer welche Passe die Miniaturen gemacht sind - andere Datei, andere Passe.</summary>
    private string? _passThumbsFor;

    private bool _passThumbsBusy;

    /// <summary>Die Miniatur eines Passes - oder keine, solange sie noch entsteht.</summary>
    private ImageSource? PassThumb(string pass) => _passThumbs.GetValueOrDefault(pass);

    /// <summary>
    /// Liest die Miniaturen aller Passe - einmal je Datei, im Hintergrund, jeden Pass
    /// verkleinert, ohne das volle Bild zu behalten. Sind sie fertig, bekommt der
    /// Dateiknoten sie zu sehen, falls er gerade gewaehlt ist.
    /// </summary>
    private void MakePassThumbs()
    {
        if (_path is not { } path || _base is null || _passThumbsBusy) return;

        var passes = NodePasses();
        string signature = path.Length + "|" + string.Join("|", passes.Select(p => p.Name));

        if (passes.Count == 0 || signature == _passThumbsFor) return;

        _passThumbsBusy = true;
        var view = ViewFor(_base);

        Task.Run(() =>
        {
            var made = new List<(string Name, NodePreviews.Thumb Thumb)>();

            foreach (var (name, _) in passes)
            {
                var frame = FloatFrame.FromExrPass(path, name, NodePreviews.Width * 2, NodePreviews.Height * 2);
                if (frame is not null) made.Add((name, NodePreviews.Draw(frame, view)));
            }

            return made;
        }).ContinueWith(task => Dispatcher.Invoke(() =>
        {
            _passThumbsBusy = false;

            // Waehrend gelesen wurde, kann eine andere Datei geoeffnet worden sein.
            if (!string.Equals(path, _path, StringComparison.Ordinal)) return;

            _passThumbsFor = signature;
            _passThumbs.Clear();

            if (task.IsCompletedSuccessfully)
            {
                foreach (var (name, thumb) in task.Result)
                {
                    var image = BitmapSource.Create(thumb.Width, thumb.Height, 96, 96, PixelFormats.Bgra32, null,
                                                    thumb.Bgra, thumb.Width * 4);
                    image.Freeze();
                    _passThumbs[name] = image;
                }
            }

            if (NodeView.Selected is RenderNode) ShowNodeSettings();
        }));
    }
}
