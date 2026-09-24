using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Holt ausgeblendete Ebenen des Stapels in einen Graphen, der ohne sie umgewandelt wurde.
///
/// Frueher fielen ausgeblendete Ebenen beim Umwandeln weg. Ein solcher Graph ist oft
/// schon weiterbearbeitet - ihn neu umzuwandeln verloere, was seitdem gebaut wurde. Also
/// wird hineingesetzt, was fehlt: Der Stapel wird noch einmal umgewandelt, die Kette
/// der Ebenen beider Graphen nebeneinandergelegt, und jede ausgeblendete Ebene kommt mit
/// ihrem ganzen Zweig an die Stelle, an der sie im Stapel liegt - als stummes Mischen,
/// das das Bild darunter unveraendert durchreicht. Am Bild aendert sich nichts.
///
/// Das geht nur, solange die Kette des Graphen noch die des Stapels ist: dieselben
/// Ebenen in derselben Reihenfolge. Sonst laesst sich nicht sagen, wohin eine Ebene
/// gehoert, und es bleibt beim Neuaufbauen.
/// </summary>
public static class HiddenLayers
{
    /// <summary>Welche ausgeblendeten Ebenen des Stapels dem Graphen fehlen - ihre Namen, von unten nach oben.</summary>
    public static IReadOnlyList<string> Missing(NodeGraph graph, NodeGraph fresh)
    {
        var have = Chain(graph);
        var want = Chain(fresh);

        if (want.Count <= have.Count) return Array.Empty<string>();

        return want.OfType<MixNode>().Where(m => m.Muted).Reverse()
                   .Select(m => m.Label ?? "").ToList();
    }

    /// <summary>
    /// Setzt die fehlenden ausgeblendeten Ebenen in den Graphen. False, wenn seine Kette
    /// nicht mehr zu der des Stapels passt - dann bleibt er unberuehrt.
    /// </summary>
    public static bool Adopt(NodeGraph graph, NodeGraph fresh)
    {
        if (!CanAdopt(graph, fresh)) return false;

        var have = Chain(graph);
        var want = Chain(fresh);
        var shown = want.Where(n => n is not MixNode { Muted: true }).ToList();

        var render = graph.Nodes.OfType<RenderNode>().First();
        var freshRender = fresh.Nodes.OfType<RenderNode>().First();
        var fallback = graph.Nodes.OfType<FallbackNode>().First();

        // Was im frischen Graphen welchem Knoten des alten entspricht.
        var map = new Dictionary<string, Node>(StringComparer.Ordinal) { [freshRender.Id] = render };

        for (int i = 0; i < have.Count; i++)
        {
            map[shown[i].Id] = have[i];

            // Die Namen der Ebenen kommen dabei gleich mit.
            if (shown[i] is MixNode { Label: { Length: > 0 } label } && have[i] is MixNode { Label: null } old)
                old.Label = label;
        }

        // Die ausgeblendeten Ebenen mit ihren Zweigen kopieren.
        var copied = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hidden in want.OfType<MixNode>().Where(m => m.Muted))
        {
            foreach (var node in Branch(fresh, hidden, map))
            {
                map[node.Id] = graph.Add(NodeGraph.CopyOf(node));
                copied.Add(node.Id);
            }
        }

        // Ihre Kabel - nur die, die in Kopiertes fuehren. Was der Graph schon hatte, bleibt,
        // wie es war; die Kette selbst wird danach neu gelegt.
        foreach (var link in fresh.Links)
        {
            if (!copied.Contains(link.To) || !map.TryGetValue(link.From, out var from)) continue;

            if (from is RenderNode file && link.Output is not (RenderNode.Picture or RenderNode.Depth or RenderNode.Motion or RenderNode.Normal))
                NodeEdits.ShowPass(graph, file, link.Output, on: true);

            graph.Connect(from, link.Output, map[link.To], link.Input);
        }

        // Die Kette von oben nach unten: jedes Glied liest das darunter.
        graph.Connect(map[want[0].Id], "Bild", fallback, "Stapel");

        for (int i = 0; i + 1 < want.Count; i++)
            if (map[want[i].Id] is MixNode above) graph.Connect(map[want[i + 1].Id], "Bild", above, "Unten");

        return true;
    }

    /// <summary>
    /// Ob sich die fehlenden Ebenen hineinsetzen lassen: Die sichtbaren Ebenen des Stapels
    /// muessen Stueck fuer Stueck noch die Kette des Graphen sein - dieselben Ebenen, an
    /// ihrer Quelle erkannt, in derselben Reihenfolge. Sonst weiss niemand, zwischen welche
    /// beiden eine ausgeblendete gehoert. Was an einer Ebene seitdem verstellt wurde -
    /// Mischart, Deckkraft, Korrektur -, stoert dabei nicht.
    /// </summary>
    public static bool CanAdopt(NodeGraph graph, NodeGraph fresh)
    {
        var have = Chain(graph);
        var want = Chain(fresh);
        var shown = want.Where(n => n is not MixNode { Muted: true }).ToList();

        if (want.Count <= have.Count || shown.Count != have.Count || have.Count == 0) return false;

        for (int i = 0; i < have.Count; i++)
            if (Identity(graph, have[i]) != Identity(fresh, shown[i])) return false;

        return graph.Nodes.OfType<RenderNode>().Any() && fresh.Nodes.OfType<RenderNode>().Any() &&
               graph.Nodes.OfType<FallbackNode>().Any();
    }

    /// <summary>
    /// Woran ein Glied der Kette als Ebene zu erkennen ist: an dem, woher das Bild kommt,
    /// das es oben auflegt - eine Bilddatei, ein Pass der Datei, eine Einstellungsebene,
    /// eine Gruppe. Die Leinwand ist die Leinwand.
    /// </summary>
    private static string Identity(NodeGraph graph, Node node)
    {
        if (node is not MixNode mix) return node.GetType().Name;
        if (graph.Into(mix.Id, "Oben") is not { } link || graph.Find(link.From) is not { } from) return "leer";

        string output = link.Output;

        for (int guard = 0; guard < 64; guard++)
        {
            switch (from)
            {
                case RenderNode:
                    return "pass:" + output;
                case PictureNode picture:
                    return "bild:" + picture.Path;
                case LayerGradeNode { Adjustment: true }:
                    return "einstellung";
                case MixNode:
                    return "gruppe";
            }

            var (input, _) = NodeEdits.Through(from);
            if (input is null || graph.Into(from.Id, input) is not { } up || graph.Find(up.From) is not { } next)
                return from.GetType().Name;

            from = next;
            output = up.Output;
        }

        return "?";
    }

    /// <summary>
    /// Die Kette der Ebenen: von dem, was in "Stapel" des Rueckfalls fliesst, ueber das
    /// "Unten" jedes Mischens hinab bis zur Leinwand. Oben zuerst.
    /// </summary>
    internal static List<Node> Chain(NodeGraph graph)
    {
        var chain = new List<Node>();
        var fallback = graph.Nodes.OfType<FallbackNode>().FirstOrDefault();
        var link = fallback is null ? null : graph.Into(fallback.Id, "Stapel");

        while (link is not null && graph.Find(link.From) is { } node && chain.Count < 10_000)
        {
            chain.Add(node);
            if (node is not MixNode) break;

            link = graph.Into(node.Id, "Unten");
        }

        return chain;
    }

    /// <summary>
    /// Der Zweig einer ausgeblendeten Ebene: ihr Mischen und alles, was ueber "Oben" und
    /// "Faktor" in es fliesst - ohne die Datei und ohne Glieder der Kette, die es im
    /// Graphen schon gibt.
    /// </summary>
    private static List<Node> Branch(NodeGraph fresh, MixNode hidden, Dictionary<string, Node> known)
    {
        var branch = new List<Node> { hidden };
        var seen = new HashSet<string>(StringComparer.Ordinal) { hidden.Id };
        var open = new Stack<Node>();
        open.Push(hidden);

        while (open.Count > 0)
        {
            var node = open.Pop();

            foreach (var link in fresh.Links.Where(l => l.To == node.Id))
            {
                // Das Unten des stummen Mischens ist die Kette - die wird neu gelegt.
                if (ReferenceEquals(node, hidden) && link.Input == "Unten") continue;

                if (known.ContainsKey(link.From) || !seen.Add(link.From)) continue;
                if (fresh.Find(link.From) is not { } from || from is MixNode { Muted: true } || from is BlackNode) continue;

                branch.Add(from);
                open.Push(from);
            }
        }

        return branch;
    }
}
