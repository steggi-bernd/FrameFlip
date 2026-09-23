using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ein Bild der Folge fuer einen Graphen: lesen, was er braucht, und rechnen.
///
/// Der Weg, den der Export im Knotenmodus nimmt - fuer Bilder und fuer Video. Er liest
/// ueber dieselben Wege wie der Stapel (<see cref="LayeredFrameLoader.Read"/>, die
/// Renderdaten nach Bedeutung), damit ein Pass im Export genau so ankommt wie in der
/// Vorschau.
///
/// Ein Graph haelt beim Rechnen Zustand in seinen Knoten - vorbereitete Tabellen der
/// Werkzeuge. Wer mehrere Bilder zugleich rechnet, braucht deshalb je Faden eine eigene
/// Kopie (<see cref="NodeGraph.Clone"/>).
/// </summary>
public static class GraphFrames
{
    /// <summary>
    /// Liest, was der Graph fuer dieses Bild braucht. Null, wenn das Bild selbst nicht
    /// lesbar ist.
    /// </summary>
    public static GraphInputs? Read(NodeGraph graph, string path, IViewTransform view, int step = 1)
    {
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

        foreach (var read in GraphEvaluator.Reads(graph))
        {
            var frame = LayeredFrameLoader.Read(read, path);
            if (frame is not null) sources[read.Key] = frame;
        }

        if (!sources.TryGetValue("", out var picture)) return null;

        var data = new Dictionary<PassNeed, FloatFrame?>();
        var needs = GraphEvaluator.Needs(graph);

        if (needs.Count > 0)
        {
            var passes = ExrPasses.Of(path);

            foreach (var need in needs)
            {
                string? name = FramePasses.NameFor(need, passes);
                data[need] = name is null ? null : FloatFrame.FromExrPass(path, name);
            }
        }

        return new GraphInputs
        {
            Sources = sources,
            Data = data,

            // Ein zurueckgerechnetes PNG ist schon durch eine Bildwerdung gegangen -
            // dieselbe Regel wie im Stapel.
            View = picture.IsSceneReferred ? view : new StandardViewTransform(),
            Step = step,
            Number = SequenceLink.NumberOf(path) ?? 0,
        };
    }

    /// <summary>Wie gross das Bild wird, das der Graph aus dieser Datei rechnet - die Leinwand.</summary>
    public static (int Width, int Height)? Size(GraphInputs inputs)
        => inputs.Sources.TryGetValue("", out var picture) ? (picture.Width, picture.Height) : null;
}
