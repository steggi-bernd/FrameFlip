using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
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
        NodeView.PreviewOf = node => PreviewImage(node) ?? QuietPreview(node);
        NodeView.PreviewToggled += OnPreviewToggled;

        NodeLayers.Chosen += OnLayerChosen;
        NodeLayers.MuteWanted += OnLayerMuted;
        NodeLayers.MissingWanted += adoptable =>
        {
            if (adoptable) AdoptHiddenLayers();
            else RebuildFromStack();
        };
    }

    /// <summary>
    /// Der Stapel, frisch umgewandelt - der Massstab fuer das, was dem Graphen fehlt. Solange
    /// der Streifen noch keinen geladen hat (beim Start stellt sich der Graph vor der Datei
    /// her), der gespeicherte.
    /// </summary>
    private NodeGraph FreshFromStack()
        => StackToGraph.Convert(Stack(), _recipe.Adjustments ?? ImageAdjustments.Neutral,
                                _recipe.Grading ?? new GradingStack());

    private LayerStack Stack()
        => Layers.Stack.Layers.Count > 0 ? Layers.Stack : _recipe.Layers ?? Layers.Stack;

    /// <summary>
    /// Sagt in der Ebenenliste, welche ausgeblendeten Ebenen des Stapels dem Graphen fehlen.
    /// Nach jeder Aenderung am Aufbau - der Stapel selbst aendert sich im Knotenmodus nicht.
    /// </summary>
    private void ShowMissingLayers()
    {
        if (_graph is null) return;

        var fresh = FreshFromStack();
        var missing = HiddenLayers.Missing(_graph, fresh);

        NodeLayers.ShowMissing(missing, missing.Count > 0 && HiddenLayers.CanAdopt(_graph, fresh));
    }

    /// <summary>
    /// Setzt die ausgeblendeten Ebenen des Stapels, die dem Graphen fehlen, hinein - stumm,
    /// an ihre Stelle, mit ihren Namen. Was sonst am Graphen gebaut wurde, bleibt.
    /// </summary>
    private void AdoptHiddenLayers()
    {
        if (_graph is null) return;

        var fresh = FreshFromStack();
        if (!HiddenLayers.CanAdopt(_graph, fresh)) return;

        RememberNodes();

        HiddenLayers.Adopt(_graph, fresh);
        NodeLayout.Arrange(_graph);

        _cache.Clear();
        NodeView.Replace(_graph);
        NodeView.Frame();

        AfterNodeEdit();
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
        {
            if (layer.Source is { } source) _previews.Wanted.Add(source.Id);
            if (layer.MaskSource is { } mask) _previews.Wanted.Add(mask.Id);
        }

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

    /// <summary>
    /// Die Vorschau eines Knotens, der nicht gerechnet wird - im Zweig einer
    /// ausgeblendeten Ebene. Eine Bilddatei und ein Platzieren zeigen dann ihre Quelle,
    /// wie die Ebenenliste; alles andere bleibt leer.
    /// </summary>
    private ImageSource? QuietPreview(Node node)
    {
        if (_graph is null || node is not (PictureNode or PlaceNode)) return null;

        var (_, output) = NodeEdits.Through(node);

        return NodeLayerList.Origin(_graph, node, output ?? "Bild") is var (origin, from) ? SourceThumb(origin, from) : null;
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

        NodeLayers.Show(NodeLayerList.Of(_graph), NodeView.Selected, LayerThumb,
                        layer => layer.MaskSource is { } mask ? PreviewImage(mask) : null);
        ShowLayerCount();
    }

    /// <summary>
    /// Die Miniatur einer Ebene: die Vorschau dessen, was in ihr Mischen fliesst. Eine
    /// ausgeblendete Ebene wird nicht gerechnet und hat keine - dann zeigt die Liste ihre
    /// Quelle, die Bilddatei oder den Pass, wie er in der Datei steht.
    /// </summary>
    private ImageSource? LayerThumb(NodeLayer layer)
    {
        if (layer.Source is { } source && PreviewImage(source) is { } computed) return computed;

        return layer.Origin is var (node, output) ? SourceThumb(node, output) : null;
    }

    private readonly Dictionary<string, ImageSource> _sourceThumbs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sourceThumbsBusy = new(StringComparer.Ordinal);

    /// <summary>
    /// Die Miniatur einer Quelle, ohne dass der Graph sie rechnet: ein Pass aus den
    /// Miniaturen der Datei, eine Bilddatei einmal im Hintergrund gelesen und verkleinert.
    /// </summary>
    private ImageSource? SourceThumb(Node node, string output)
    {
        switch (node)
        {
            case RenderNode when output != RenderNode.Picture:
                MakePassThumbs();
                return PassThumb(output);

            case RenderNode when _base is not null:
                return Thumb("datei", () => _base);

            case PictureNode picture when picture.Path.Length > 0 && _path is { } framePath:
                return Thumb("bild:" + picture.Path, () => LayeredFrameLoader.Read(
                    new LayerRead(picture.Path, LayerContent.Image, picture.FollowSequence), framePath));

            default:
                return null;
        }
    }

    /// <summary>Eine Miniatur, im Hintergrund gelesen - beim ersten Mal keine, danach die Liste neu.</summary>
    private ImageSource? Thumb(string key, Func<FloatFrame?> read)
    {
        if (_sourceThumbs.TryGetValue(key, out var known)) return known;
        if (!_sourceThumbsBusy.Add(key) || _base is null) return null;

        string? path = _path;
        long opened = _source.Opened;
        var view = ViewFor(_base);

        Task.Run(() => read() is { } frame ? NodePreviews.Draw(frame, view) : null)
            .ContinueWith(task => Dispatcher.Invoke(() =>
            {
                _sourceThumbsBusy.Remove(key);

                // Waehrend gelesen wurde, kann eine andere Datei geoeffnet worden sein.
                if (!_source.IsCurrent(opened) || !string.Equals(path, _path, StringComparison.Ordinal)) return;
                if (!task.IsCompletedSuccessfully || task.Result is not { } thumb) return;

                var image = BitmapSource.Create(thumb.Width, thumb.Height, 96, 96, PixelFormats.Bgra32, null,
                                                thumb.Bgra, thumb.Width * 4);
                image.Freeze();

                _sourceThumbs[key] = image;
                ShowNodeLayers();
                NodeView.InvalidateVisual();
            }));

        return null;
    }

    /// <summary>
    /// Baut den Graphen neu aus dem gespeicherten Stapel - fuer einen Graphen, der
    /// umgewandelt wurde, bevor ausgeblendete Ebenen, Namen und Vorschauen mitkamen.
    /// Was seitdem am Graphen gebaut wurde, ist danach weg; Rueckgaengig holt es zurueck.
    /// </summary>
    private void RebuildFromStack()
    {
        if (_graph is null) return;

        RememberNodes();

        _graph = FreshFromStack();
        _cache.Clear();

        NodeView.Replace(_graph);
        NodeView.Frame();

        AfterNodeEdit();
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

    private void OnLayerMuted(Node mix)
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
        long opened = _source.Opened;
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
            if (!_source.IsCurrent(opened) || !string.Equals(path, _path, StringComparison.Ordinal)) return;

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
