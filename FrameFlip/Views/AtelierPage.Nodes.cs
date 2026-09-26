using System.Windows;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Knotenmodus der Atelierseite - siehe docs/Atelier-Nodes.md.
///
/// Er ist eine Einbahnstrasse: Wer umschaltet, bekommt den Stapel als Graphen, und von
/// da an rechnet nur noch der Graph - in der Vorschau, im Histogramm und im Export.
/// Der Stapel bleibt in den Einstellungen liegen, wie er beim Umschalten war, aber
/// niemand rechnet mehr mit ihm.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Der Graph, wenn das Atelier im Knotenmodus ist - sonst null.</summary>
    private NodeGraph? _graph;

    /// <summary>Ob das Atelier im Knotenmodus rechnet.</summary>
    public bool InNodes => _graph is not null;

    /// <summary>Der Graph - oeffentlich fuer die Probe.</summary>
    public NodeGraph? Graph => _graph;

    /// <summary>
    /// Holt den Knotenmodus aus den Einstellungen zurueck.
    ///
    /// Ein Graph, der sich nicht lesen oder nicht rechnen laesst, fuehrt NICHT still
    /// zurueck in den Stapel: Der Text bleibt in den Einstellungen stehen, und das
    /// Atelier zeigt den Stapel - wer eine neuere Fassung hatte, verliert so nichts.
    /// </summary>
    private void RestoreNodes()
    {
        // Mit Projektdateien bringt das erste Bild sein Projekt und damit seinen Graphen
        // mit. Der Graph in den Einstellungen ist dann nur noch die Sicherung von vorher.
        if (_settings.AtelierRecipeMoved) return;

        if (_recipe.Nodes is not { Length: > 0 } json) return;

        var graph = NodeGraph.Load(json);
        if (graph is null || graph.Problems().Count > 0) return;

        _graph = graph;
        EnterNodes();
    }

    /// <summary>
    /// Wandelt den Stapel in einen Graphen um - einmal, und nicht zurueck.
    ///
    /// Umgewandelt wird das FERTIGE Bild: der Ebenenstapel, die Grundkorrektur und die
    /// Werkzeuge des ganzen Bildes - nicht das, was der Farbstreifen gerade zeigt.
    /// </summary>
    public void ConvertToNodes()
    {
        if (_graph is not null) return;

        _graph = StackToGraph.Convert(Layers.Stack, _recipe.Adjustments ?? ImageAdjustments.Neutral,
                                      _recipe.Grading ?? new GradingStack());

        SaveNodes();
        EnterNodes();
        FetchNodeSources();
    }

    /// <summary>Was sich im Knotenmodus an der Seite aendert.</summary>
    private void EnterNodes()
    {
        // Die Ebenen stehen jetzt als Knoten im Graphen. Im Reiter steht statt des
        // Streifens eine Liste der Ebenen, die ihre Knoten findet.
        Layers.Visibility = Visibility.Collapsed;
        NodeLayers.Visibility = Visibility.Visible;
        Dock.SetAvailable("layers", _layersShown, Strings.T("S_LayersUnavailable"));
        ShowLayerCount();

        NodeView.Graph = _graph;
        NodeView.Title = NodeTitles.For;
        NodeView.MaskTitle = NodeTitles.MaskName;

        ShowNodeMode();
        ShowNodeSettings();
        ShowNodeWarning();
        ShowPlacement();
        ShowNodeLayers();
        ShowMissingLayers();
    }

    /// <summary>Schreibt den Graphen in die Einstellungen.</summary>
    private void SaveNodes()
    {
        if (_graph is null) return;

        _recipe.Nodes = _graph.Save();
        _persist(_settings);
    }

    /// <summary>
    /// Holt nach, was der Graph lesen will und noch nicht im Vorrat liegt - Passe,
    /// Bilddateien, Renderdaten. Derselbe Lesevorrat wie im Stapel.
    /// </summary>
    /// <param name="soon">Erst grob rechnen und voll nach einer Pause - nach einem Bauschritt.</param>
    private void FetchNodeSources(bool soon = false)
    {
        if (_graph is null || _path is null || _base is null)
        {
            Refresh(interim: soon, recompose: true);
            return;
        }

        var missing = GraphEvaluator.Reads(_graph, _viewer)
            .Where(r => r.Key.Length > 0 && !_sources.ContainsKey(r.Key))
            .ToList();

        // Eine Kryptomatte, an der gewaehlt wird, braucht ihre Stufen, auch wenn sie
        // noch nirgends steckt - gewaehlt wird im Bild, nicht im Graphen.
        if (NodeView.Selected is MaskNode { Mask: { Kind: MaskKind.Cryptomatte } picking })
        {
            foreach (string level in picking.Levels)
            {
                if (!_sources.ContainsKey(level) && missing.All(r => r.Key != level))
                    missing.Add(new LayerRead(level, LayerContent.Pass, false));
            }
        }

        var dataMissing = GraphEvaluator.Needs(_graph, _viewer)
            .Select(need => FramePasses.NameFor(need, _passes))
            .OfType<string>()
            .Where(name => !_sources.ContainsKey(name))
            .Distinct()
            .ToList();

        if (missing.Count == 0 && dataMissing.Count == 0)
        {
            Refresh(interim: soon, recompose: true);
            return;
        }

        Fetch(_path, missing, dataMissing, _ => Refresh(interim: false, recompose: true));
    }

    /// <summary>Die Renderdaten fuer den Graphen - aus dem Vorrat, nach Bedeutung.</summary>
    private Dictionary<PassNeed, FloatFrame?> NodeData()
    {
        var data = new Dictionary<PassNeed, FloatFrame?>();

        if (_graph is null) return data;

        foreach (var need in GraphEvaluator.Needs(_graph, _viewer))
        {
            data[need] = FramePasses.NameFor(need, _passes) is { } name && _sources.TryGetValue(name, out var frame)
                ? frame
                : null;
        }

        return data;
    }

    /// <summary>Die Felder der Zwischenbilder - von einer Rechnung zur naechsten wiederverwendet.</summary>
    private readonly GridPool _pool = new();

    /// <summary>
    /// Die Felder der Ausschnitte beim Malen - ein eigener Vorrat, damit ihre wechselnden
    /// Groessen nicht die Felder des ganzen Bildes verdraengen.
    /// </summary>
    private readonly GridPool _regionPool = new(sizes: 16);

    /// <summary>Was vor dem gewaehlten Knoten gerechnet wurde - beim naechsten Zug an ihm gilt es noch.</summary>
    private readonly GraphCache _cache = new();

    /// <summary>
    /// Rechnet den Graphen in die Anzeigeflaeche. False, wenn er sich nicht rechnen
    /// laesst - dann bleibt das Bild, wie es war.
    /// </summary>
    private bool RenderNodes(IntPtr target, int stride)
    {
        if (_graph is null || _base is null) return false;

        var inputs = NodeInputs(_base, _coarse ? CoarseStep : 1);

        WantPreviews();

        bool done;

        try
        {
            done = GraphEvaluator.Render(_graph, inputs, target, stride);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Ein Graph, der sich nicht rechnen laesst, darf die Seite nicht anhalten. Die
            // App verschluckt unbehandelte Fehler - das Bild bliebe stehen, jeder weitere
            // Zug liefe in denselben Fehler, und von aussen saehe das aus wie ein Absturz.
            // Hier wird er benannt, und was halb gerechnet war, wird verworfen.
            _cache.Clear();
            _pool.Clear();
            _regionPool.Clear();
            _renderFailed = true;

            NodeView.Warning = Strings.T("S_NodeWarnFailed", (e as AggregateException)?.InnerException?.Message ?? e.Message);
            Configuration.SettingsStore.Trace("Knoten: " + e);

            return false;
        }

        // Nach einem Fehler, der behoben ist, gilt wieder, was der Aufbau sagt.
        if (_renderFailed)
        {
            _renderFailed = false;
            ShowNodeWarning();
        }

        ShowPreviews();
        return done;
    }

    /// <summary>Was der Graph fuer eine Rechnung dieser Seite bekommt - fuer das ganze Bild und fuer einen Ausschnitt.</summary>
    private GraphInputs NodeInputs(FloatFrame canvas, int step, GridPool? pool = null) => new()
    {
        Sources = _sources,
        Data = NodeData(),
        View = ViewFor(canvas),
        Step = step,
        Number = _number,
        Pool = pool ?? _pool,
        Cache = _cache,
        Focus = NodeView.Selected?.Id,
        Previews = _previews,
        Viewer = _viewer,
    };

    /// <summary>
    /// Ob die Anzeigeflaeche gerade ein ganzes, voll aufgeloestes Bild des Graphen traegt.
    /// Nur darauf darf ein Ausschnitt geschrieben werden: auf ein grobes Bild gesetzt,
    /// stuende ein scharfes Rechteck in einem unscharfen.
    /// </summary>
    private bool _wholeShown;

    /// <summary>
    /// Beim Malen: rechnet nur, was der Pinsel seit dem letzten Bild beruehrt hat, und
    /// schreibt es an seine Stelle - siehe <see cref="GraphEvaluator.RenderRegion"/>.
    /// False, wenn das nicht geht; dann rechnet der Aufrufer wie bisher das ganze Bild grob.
    /// </summary>
    private bool PaintRegion()
    {
        var touched = Placement.TakeTouched();

        // Kein Tupfer seit dem letzten Bild - nichts zu rechnen, aber auch kein Grund,
        // das ganze Bild grob zu zeigen.
        if (touched.IsEmpty) return _wholeShown;

        if (!_wholeShown || _surface is null || _graph is null || _base is null || _showingOriginal || _viewer is not null)
            return false;

        // Zwei Maskenpunkte Rand: Die Maske wird zwischen ihren Punkten weich gelesen, ein
        // geaenderter Punkt wirkt also bis zu einem Maskenpunkt weit ins Bild daneben.
        //
        // Auf 32 Bildpunkte nach aussen gerundet: ein paar Punkte mehr zu rechnen kostet
        // weniger, als fuer jede neue Groesse neue Felder anzulegen.
        var region = GraphRegion.Around(touched.X0, touched.Y0, touched.X1, touched.Y1, 2 * PaintedMask.Coarse).Snapped(32);
        if (region.Within(_surface.PixelWidth, _surface.PixelHeight) is not { } inside) return true;

        _surface.Lock();

        try
        {
            if (!GraphEvaluator.RenderRegion(_graph, NodeInputs(_base, 1, _regionPool), inside, _surface.BackBuffer, _surface.BackBufferStride))
                return false;

            _surface.AddDirtyRect(new Int32Rect(inside.X0, inside.Y0, inside.Width, inside.Height));
            _regionPainted = true;
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Wie beim ganzen Bild: nicht anhalten. Das ganze Bild danach sagt, was fehlt.
            Configuration.SettingsStore.Trace("Knoten, Ausschnitt: " + e);
            return false;
        }
        finally
        {
            _surface.Unlock();
        }
    }

    /// <summary>Ob die letzte Rechnung mit einem Fehler endete - dann steht er im Editor.</summary>
    private bool _renderFailed;

    /// <summary>
    /// Die Verteilung im Knotenmodus: gemessen am fertig gerechneten Bild.
    ///
    /// Im Stapel misst ein eigener Durchgang die Kette bis zur Anzeige nach. Ein Graph
    /// hat keine feste Kette, die man nachmessen koennte - das fertige Bild ist die
    /// einzige ehrliche Antwort, und es liegt ohnehin schon da.
    /// </summary>
    private void MeasureNodes()
    {
        var surface = _surface;
        var picture = _base;
        if (surface is null || picture is null) return;

        int width = surface.PixelWidth, height = surface.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];

        surface.CopyPixels(pixels, stride, 0);

        var histogram = new Histogram();
        FrameProcessor.Measure(pixels, width, height, stride, histogram, step: 4);

        // Die Reserve ueber Weiss steht in der Datei, nicht im fertigen Bild.
        long sampled = 0, above = 0;

        for (int y = 0; y < picture.Height; y += 4)
        {
            for (int x = 0; x < picture.Width; x += 4)
            {
                int i = y * picture.Width + x;
                if (picture.R[i] > 1f || picture.G[i] > 1f || picture.B[i] > 1f) above++;
                sampled++;
            }
        }

        histogram.AboveWhite = sampled > 0 ? above / (double)sampled : 0;

        Tools.ShowHistogram(histogram);
    }

    /// <summary>
    /// Zeigt, dass das Atelier im Knotenmodus ist - und wie man hineinkommt, solange
    /// es das nicht ist. Siehe <see cref="NodeEditor"/> und die Werkzeugspalte.
    /// </summary>
    private void ShowNodeMode()
    {
        bool nodesTool = _tool == AtelierTool.Nodes;

        NodeView.Visibility = nodesTool && InNodes ? Visibility.Visible : Visibility.Collapsed;
        NodeOffer.Visibility = nodesTool && !InNodes && _frame is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnConvertClicked(object sender, RoutedEventArgs e)
    {
        ConvertToNodes();
        ShowNodeMode();
    }

    private void OnConvertCancelled(object sender, RoutedEventArgs e)
        => MouseTools.Select(AtelierTool.Move, notify: true);
}
