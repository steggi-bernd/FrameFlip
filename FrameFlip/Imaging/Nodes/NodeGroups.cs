namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Eine Gruppe im Knoteneditor: eine Ebene mit allem, was nur ihr zuarbeitet, oder das
/// Gesamtbild hinter allen Ebenen. Gezeichnet als Rahmen um ihre Knoten, wie ein Frame im
/// Compositor von Blender - nur legt ihn niemand an: Er ergibt sich aus den Kabeln und
/// wandert mit, wenn sich der Graph aendert. Siehe docs/Atelier-Knoten-Gruppen.md.
/// </summary>
/// <param name="Head">Das Mischen der Ebene - beim Gesamtbild die Ausgabe.</param>
/// <param name="Members">Die Knoten im Rahmen in Rechenreihenfolge, der Kopf zuletzt.</param>
/// <param name="Parent">Die Gruppe oder der Traeger, in dem die Ebene liegt - dessen Rahmen umschliesst ihren.</param>
/// <param name="Depth">Wie tief die Ebene in Gruppen und Traegern steckt - 0 ganz aussen.</param>
public sealed record NodeGroup(Node Head, IReadOnlyList<Node> Members, Node? Parent, int Depth)
{
    /// <summary>Ob es der Rahmen des Gesamtbilds ist - alles hinter den Ebenen bis zur Ausgabe.</summary>
    public bool IsPicture => Head is OutputNode;
}

/// <summary>
/// Teilt einen Graphen in Quellen und Gruppen - die Grundlage fuer die Anordnung wie in
/// Blender: links alle Quellen mit ihrer Vorschau, rechts davon je Ebene ein Rahmen, und
/// die Rahmen verbinden sich ueber ihre Mischen zum Bild.
/// </summary>
public static class NodeGroups
{
    /// <summary>Ob ein Knoten am linken Rand steht: etwas, das ein Bild von der Platte liest.</summary>
    public static bool IsSource(Node node) => node is RenderNode or PictureNode;

    /// <summary>
    /// Die Gruppen eines Graphen: die Ebenen wie in der Ebenenliste, oben zuerst und unter
    /// einer Gruppe oder einem Traeger das, was in ihm liegt - danach das Gesamtbild.
    /// Quellen gehoeren zu keiner Gruppe, ebenso die schwarze Leinwand unter dem Stapel.
    /// </summary>
    public static IReadOnlyList<NodeGroup> Of(NodeGraph graph)
    {
        var order = graph.Order();
        if (order is null || order.Count == 0) return Array.Empty<NodeGroup>();

        var chains = LayerEdits.Chains(graph);
        var inner = new Dictionary<string, LayerChain>(StringComparer.Ordinal);

        foreach (var chain in chains)
            if (chain.Parent is { } parent) inner.TryAdd(parent.Id, chain);

        var layers = new List<(MixNode Mix, Node? Parent, int Depth)>();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        void List(LayerChain chain)
        {
            for (int i = chain.Members.Count - 1; i >= 0; i--)
            {
                if (chain.Members[i] is not MixNode mix || !listed.Add(mix.Id)) continue;

                layers.Add((mix, chain.Parent, chain.Depth));
                if (inner.TryGetValue(mix.Id, out var sub)) List(sub);
            }
        }

        foreach (var main in chains.Where(c => c.Kind == LayerChainKind.Main)) List(main);

        // Mischen ausserhalb jeder Kette - etwa ein frei gebautes - stehen am Ende.
        foreach (var mix in order.OfType<MixNode>().Reverse())
            if (listed.Add(mix.Id)) layers.Add((mix, null, 0));

        // Ein Knoten gehoert zur innersten Ebene, deren Zweig ihn enthaelt. Der Zweig einer
        // Gruppe enthaelt auch ihre Kinder samt deren Zweigen - die haben eigene Rahmen.
        var owner = new Dictionary<string, (int Index, int Size)>(StringComparer.Ordinal);

        for (int i = 0; i < layers.Count; i++)
        {
            var branch = LayerEdits.Branch(graph, layers[i].Mix);

            foreach (string id in branch)
            {
                if (listed.Contains(id)) continue;
                if (!owner.TryGetValue(id, out var known) || branch.Count < known.Size) owner[id] = (i, branch.Count);
            }
        }

        var members = layers.Select(_ => new List<Node>()).ToList();
        var claimed = new HashSet<string>(listed, StringComparer.Ordinal);

        foreach (var node in order)
        {
            if (IsSource(node) || !owner.TryGetValue(node.Id, out var o)) continue;

            members[o.Index].Add(node);
            claimed.Add(node.Id);
        }

        var groups = new List<NodeGroup>();

        for (int i = 0; i < layers.Count; i++)
        {
            members[i].Add(layers[i].Mix);
            groups.Add(new NodeGroup(layers[i].Mix, members[i], layers[i].Parent, layers[i].Depth));
        }

        if (graph.Output is { } output)
        {
            var picture = order.Where(n => !IsSource(n) && n is not BlackNode && !claimed.Contains(n.Id)).ToList();

            // Die Ausgabe zuletzt, auch wenn die Rechenordnung sie schon dort hat.
            picture.Remove(output);
            picture.Add(output);

            groups.Add(new NodeGroup(output, picture, null, 0));
        }

        return groups;
    }

    /// <summary>Der Dateiknoten mit allen Ausgaengen - das Bild der Datei und ihre Renderdaten.</summary>
    public static RenderNode? File(NodeGraph graph)
        => graph.Nodes.OfType<RenderNode>().FirstOrDefault(r => r.Only is null) ??
           graph.Nodes.OfType<RenderNode>().FirstOrDefault();

    /// <summary>
    /// Die Quelle eines Passes: die vorhandene, oder eine neue neben der Datei. Ein Pass hat
    /// eine Quelle, gleich wie viele Ebenen und Masken ihn lesen.
    /// </summary>
    public static RenderNode PassSource(NodeGraph graph, string pass)
    {
        foreach (var known in graph.Nodes.OfType<RenderNode>())
            if (known.Only == pass) return known;

        var source = graph.Add(RenderNode.ForPass(pass));

        if (File(graph) is { } file && !ReferenceEquals(file, source))
        {
            source.X = file.X;
            source.Y = file.Y + NodeLayout.Height(file) + NodeLayout.Gap;
        }

        return source;
    }

    /// <summary>
    /// Holt die Passe aus der Datei: Jeder Pass, der aus dem einen Dateiknoten gelesen wird,
    /// bekommt eine eigene Quelle - einen Dateiknoten, der nur ihn zeigt, mit Vorschau. Das
    /// Bild und die Renderdaten bleiben am Dateiknoten. Am gerechneten Bild aendert sich
    /// nichts; derselbe Pass wird weiter einmal gelesen. False, wenn es nichts zu holen gab.
    /// </summary>
    public static bool SplitSources(NodeGraph graph)
    {
        if (graph.Nodes.OfType<RenderNode>().FirstOrDefault(r => r.Only is null) is not { } file) return false;

        var moved = graph.Links
            .Where(l => l.From == file.Id &&
                        l.Output is not (RenderNode.Picture or RenderNode.Depth or RenderNode.Motion or RenderNode.Normal))
            .ToList();

        if (moved.Count == 0) return false;

        var sources = new Dictionary<string, RenderNode>(StringComparer.Ordinal);

        foreach (var known in graph.Nodes.OfType<RenderNode>())
            if (known.Only is { Length: > 0 } only) sources.TryAdd(only, known);

        foreach (var link in moved)
        {
            if (!sources.TryGetValue(link.Output, out var source))
            {
                source = graph.Add(RenderNode.ForPass(link.Output));
                source.X = file.X;
                source.Y = file.Y;
                sources[link.Output] = source;
            }

            link.From = source.Id;
        }

        // Was der Dateiknoten jetzt nicht mehr ausgibt, verschwindet von ihm. Ein Pass, den
        // man dort nur zum Ansehen eingeschaltet hat, bleibt.
        var gone = moved.Select(l => l.Output).ToHashSet(StringComparer.Ordinal);
        file.Passes.RemoveAll(p => gone.Contains(p) && !graph.Links.Any(l => l.From == file.Id && l.Output == p));

        return true;
    }
}
