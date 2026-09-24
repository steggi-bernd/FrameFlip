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
    /// <summary>
    /// Welche ausgeblendeten Ebenen des Stapels dem Graphen fehlen - ihre Namen, von unten
    /// nach oben: erst die Ebenen und Gruppen, dann die Wasserzeichen obenauf.
    /// </summary>
    public static IReadOnlyList<string> Missing(NodeGraph graph, NodeGraph fresh)
    {
        var names = new List<string>();

        if (Chain(fresh).Count > Chain(graph).Count)
            names.AddRange(Chain(fresh).OfType<MixNode>().Where(m => m.Muted).Reverse().Select(m => m.Label ?? ""));

        if (Overlays(fresh).Count > Overlays(graph).Count)
            names.AddRange(Overlays(fresh).Where(o => o.Muted).Select(o => o.Label ?? ""));

        return names;
    }

    /// <summary>
    /// Setzt die fehlenden ausgeblendeten Ebenen in den Graphen. False, wenn er nicht mehr
    /// zum Stapel passt - dann bleibt er unberuehrt.
    /// </summary>
    public static bool Adopt(NodeGraph graph, NodeGraph fresh)
    {
        if (!CanAdopt(graph, fresh)) return false;

        if (Chain(fresh).Count > Chain(graph).Count) AdoptChain(graph, fresh);
        if (Overlays(fresh).Count > Overlays(graph).Count) AdoptOverlays(graph, fresh);

        return true;
    }

    /// <summary>
    /// Ob sich die fehlenden Ebenen hineinsetzen lassen - fuer die Kette der Ebenen und
    /// fuer die Wasserzeichen je fuer sich: Was fehlt, muss einen Platz haben.
    /// </summary>
    public static bool CanAdopt(NodeGraph graph, NodeGraph fresh)
    {
        bool chain = Chain(fresh).Count > Chain(graph).Count;
        bool overlays = Overlays(fresh).Count > Overlays(graph).Count;

        return (chain || overlays) && (!chain || ChainFits(graph, fresh)) && (!overlays || OverlaysFit(graph, fresh));
    }

    /// <summary>Die ausgeblendeten Ebenen und Gruppen in die Kette.</summary>
    private static void AdoptChain(NodeGraph graph, NodeGraph fresh)
    {
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

        // Die ausgeblendeten Ebenen mit ihren Zweigen kopieren - bei einer Gruppe mit
        // allen Kindern, auch den ausgeblendeten darin.
        var copied = new HashSet<string>(StringComparer.Ordinal);
        var links = new HashSet<string>(want.Select(n => n.Id), StringComparer.Ordinal);

        foreach (var hidden in want.OfType<MixNode>().Where(m => m.Muted))
        {
            foreach (var node in Branch(fresh, hidden, map, links))
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
    }

    /// <summary>
    /// Die ausgeblendeten Wasserzeichen: jedes hinter das sichtbare, das im Stapel vor ihm
    /// liegt - oder vor das naechste. Gibt es keines, vor die Durchgaenge ueber das fertige
    /// Bild, wo der Umwandler sie hinsetzt.
    /// </summary>
    private static void AdoptOverlays(NodeGraph graph, NodeGraph fresh)
    {
        var have = Overlays(graph);
        var want = Overlays(fresh);

        int next = 0;
        Node? after = null;

        foreach (var overlay in want)
        {
            if (!overlay.Muted)
            {
                // Der Name kommt mit, wie bei den Ebenen.
                if (have[next] is { Label: null } known && overlay.Label is { Length: > 0 } label) known.Label = label;

                after = have[next++];
                continue;
            }

            var copy = graph.Add((OverlayNode)NodeGraph.CopyOf(overlay));

            if (fresh.Into(overlay.Id, "Ebene") is { } source && fresh.Find(source.From) is PictureNode picture)
                graph.Connect(graph.Add(NodeGraph.CopyOf(picture)), "Bild", copy, "Ebene");

            if (after is not null)
                NodeEdits.InsertAfter(graph, after, copy);
            else if (next < have.Count && graph.Into(have[next].Id, "Bild") is { } before)
                NodeEdits.InsertInto(graph, before, copy);
            else if (Tail(graph) is { } tail)
                NodeEdits.InsertInto(graph, tail, copy);

            after = copy;
        }
    }

    /// <summary>Das Kabel vor den Durchgaengen ueber das fertige Bild - oder vor der Ausgabe.</summary>
    private static NodeLink? Tail(NodeGraph graph)
    {
        if (graph.Output is not { } output) return null;

        var link = graph.Into(output.Id, "Bild");

        for (int guard = 0; link is not null && guard < 256 && graph.Find(link.From) is FramePassNode pass; guard++)
            link = graph.Into(pass.Id, "Bild");

        return link;
    }

    /// <summary>
    /// Die Wasserzeichen auf dem Weg zur Ausgabe - in der Reihenfolge, in der sie
    /// aufgetragen werden. Gesucht wird vom Ende her, den Bildweg entlang, bis zum Stapel.
    /// </summary>
    internal static List<OverlayNode> Overlays(NodeGraph graph)
    {
        var found = new List<OverlayNode>();
        Node? node = graph.Output;

        for (int guard = 0; node is not null && node is not FallbackNode && guard < 10_000; guard++)
        {
            var (input, _) = NodeEdits.Through(node);
            if (input is null || graph.Into(node.Id, input) is not { } link) break;

            node = graph.Find(link.From);
            if (node is OverlayNode overlay) found.Add(overlay);
        }

        found.Reverse();
        return found;
    }

    /// <summary>Ob die sichtbaren Wasserzeichen des Stapels noch die des Graphen sind - an ihrer Datei erkannt.</summary>
    private static bool OverlaysFit(NodeGraph graph, NodeGraph fresh)
    {
        var have = Overlays(graph);
        var shown = Overlays(fresh).Where(o => !o.Muted).ToList();

        if (shown.Count != have.Count) return false;

        for (int i = 0; i < have.Count; i++)
            if (Picture(graph, have[i]) != Picture(fresh, shown[i])) return false;

        return graph.Output is not null;
    }

    private static string? Picture(NodeGraph graph, OverlayNode overlay)
        => graph.Into(overlay.Id, "Ebene") is { } link && graph.Find(link.From) is PictureNode picture ? picture.Path : null;

    /// <summary>
    /// Ob die fehlenden Ebenen in die Kette passen: Die sichtbaren Ebenen des Stapels
    /// muessen Stueck fuer Stueck noch die Kette des Graphen sein - dieselben Ebenen, an
    /// ihrer Quelle erkannt, in derselben Reihenfolge. Sonst weiss niemand, zwischen welche
    /// beiden eine ausgeblendete gehoert. Was an einer Ebene seitdem verstellt wurde -
    /// Mischart, Deckkraft, Korrektur -, stoert dabei nicht.
    /// </summary>
    private static bool ChainFits(NodeGraph graph, NodeGraph fresh)
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
    private static List<Node> Branch(NodeGraph fresh, MixNode hidden, Dictionary<string, Node> known,
                                     IReadOnlySet<string> chain)
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

                // Glieder der Kette - auch andere ausgeblendete - werden nicht mitkopiert:
                // Sie gibt es schon oder sie kommen selbst.
                if (known.ContainsKey(link.From) || chain.Contains(link.From) || !seen.Add(link.From)) continue;
                if (fresh.Find(link.From) is not { } from || from is BlackNode) continue;

                branch.Add(from);
                open.Push(from);
            }
        }

        return branch;
    }
}
