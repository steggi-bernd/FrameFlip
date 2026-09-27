using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Die Kette des Gesamtbilds: was hinter allen Ebenen auf das fertige Bild wirkt - Licht,
/// Werkzeuge, Sichtumwandlung, Tonwerte, Wasserzeichen, Durchgaenge -, und wohin ein neuer
/// Knoten fuer das Gesamtbild darin gehoert (docs/Atelier-Arbeitsablauf.md, C2).
///
/// Die Reihenfolge ist die, in der <see cref="StackToGraph"/> das fertige Bild aus dem Stapel
/// umwandelt. Ein Effekt, der im Knotenmodus fuers Gesamtbild hinzukommt, steht damit dort,
/// wo der Stapel ihn rechnen wuerde - Glanz vor der Sichtumwandlung, Tonwert dahinter, Pixel
/// Sort ganz am Ende. Vorher kam er frei in die Mitte der Ansicht, ohne Kabel.
/// </summary>
public static class GlobalChain
{
    /// <summary>
    /// Der Platz eines Knotens in der Kette des Gesamtbilds - oder null, wenn er nicht dorthin
    /// gehoert (Mischen, Masken, Quellen).
    /// </summary>
    public static int? Rank(Node node) => node switch
    {
        LightNode => 0,
        LayerGradeNode => 1,
        PointToolNode { Tool: { Stage: GradingStage.SceneLinear } } => 1,
        OpticsNode { Tool: { Stage: OpticsStage.Lens } } => 2,
        GeometryNode => 3,
        DataNode => 4,
        LocalNode { Tool: { Stage: <= LocalStage.Light } } => 5,
        OpticsNode { Tool: { Stage: OpticsStage.Film } } => 6,
        ViewNode => 7,
        ToneNode => 8,
        PointToolNode => 9,
        LocalNode => 10,
        OverlayNode => 11,
        FramePassNode => 12,
        _ => null,
    };

    /// <summary>
    /// Die Kette vor der Ausgabe, vom Licht an: rueckwaerts ueber die Bildeingaenge, solange
    /// ein Knoten einen Platz darin hat. Leer, wenn vor der Ausgabe nichts davon steht.
    /// </summary>
    public static IReadOnlyList<Node> Chain(NodeGraph graph)
    {
        var chain = new List<Node>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (Node? node = graph.Output; node is not null;)
        {
            if (NodeEdits.Through(node).Input is not { } input || graph.Into(node.Id, input) is not { } link) break;
            if (graph.Find(link.From) is not { } before || Rank(before) is null || !taken.Add(before.Id)) break;

            chain.Add(before);
            if (before is LightNode) break;

            node = before;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Hinter welchen Knoten ein neuer fuer das Gesamtbild gehoert: den letzten der Kette, der
    /// nach der Rechenordnung nicht nach ihm kommt. Hat der neue keinen Platz in der Kette oder
    /// ist sie leer, der Knoten unmittelbar vor der Ausgabe - so bleibt er wenigstens verbunden.
    /// Null nur ohne Ausgabe oder ohne etwas davor.
    /// </summary>
    public static Node? After(NodeGraph graph, Node node)
    {
        var chain = Chain(graph);

        if (Rank(node) is { } rank && chain.Count > 0)
        {
            Node? after = null;

            foreach (var member in chain)
            {
                if (Rank(member) > rank) break;
                after = member;
            }

            // Vor allem, was die Kette hat, geht es nicht - vor das Licht gehoert nichts.
            return after ?? chain[0];
        }

        return graph.Output is { } output && NodeEdits.Through(output).Input is { } into &&
               graph.Into(output.Id, into) is { } last
            ? graph.Find(last.From)
            : null;
    }
}
