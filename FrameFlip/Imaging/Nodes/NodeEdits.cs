namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Was sich an einem Graphen aendern laesst - verbinden, trennen, einfuegen, loeschen.
///
/// Reine Operationen auf dem Modell, ohne Oberflaeche. Der Editor ruft sie, und die Probe
/// ruft sie genauso; was hier geprueft ist, gilt fuer beide. Die eine Frage, an der alles
/// haengt, steht in <see cref="CannotConnect"/>: Sie beantwortet der Editor schon WAEHREND
/// des Ziehens, damit ein Kabel gar nicht erst an einem Anschluss landen kann, an den es
/// nicht passt.
/// </summary>
public static class NodeEdits
{
    /// <summary>
    /// Warum sich dieser Ausgang nicht mit diesem Eingang verbinden laesst - oder null,
    /// wenn es geht. Der Satz ist fuer Menschen: Er steht im Editor neben dem Kabel.
    /// </summary>
    public static string? CannotConnect(NodeGraph graph, Node from, string output, Node to, string input)
    {
        if (ReferenceEquals(from, to)) return "S_NodeWhySelf";

        if (from.Output(output) is not { } source || to.Input(input) is not { } target) return "S_NodeWhyMissing";

        if (!NodeGraph.Fits(source.Type, target.Type)) return "S_NodeWhyType";

        if (target.Source && !source.Source) return "S_NodeWhySource";

        // Ein Kreis entsteht, wenn der Empfaenger schon - ueber wie viele Knoten auch
        // immer - in den Absender fliesst.
        if (Feeds(graph, to, from)) return "S_NodeWhyLoop";

        return null;
    }

    /// <summary>Ob <paramref name="upstream"/> ueber irgendeinen Weg in <paramref name="downstream"/> fliesst.</summary>
    public static bool Feeds(NodeGraph graph, Node upstream, Node downstream)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var open = new Stack<string>();
        open.Push(upstream.Id);

        while (open.Count > 0)
        {
            string id = open.Pop();
            if (id == downstream.Id) return true;
            if (!seen.Add(id)) continue;

            foreach (var link in graph.Links)
                if (link.From == id) open.Push(link.To);
        }

        return false;
    }

    /// <summary>Verbindet, wenn es geht. Eine Verbindung, die schon in den Eingang fuehrte, wird ersetzt.</summary>
    public static bool Connect(NodeGraph graph, Node from, string output, Node to, string input)
    {
        if (CannotConnect(graph, from, output, to, input) is not null) return false;

        graph.Connect(from, output, to, input);
        return true;
    }

    /// <summary>Trennt, was in diesen Eingang fuehrt.</summary>
    public static void Disconnect(NodeGraph graph, Node to, string input)
        => graph.Links.RemoveAll(l => l.To == to.Id && l.Input == input);

    /// <summary>Der erste Bildeingang und der erste Bildausgang - der Weg, den ein Bild durch den Knoten nimmt.</summary>
    public static (string? Input, string? Output) Through(Node node)
        => (node.Inputs.FirstOrDefault(s => s.Type == SocketType.Image && !s.Source).Name
                ?? node.Inputs.FirstOrDefault(s => s.Type == SocketType.Image).Name,
            node.Outputs.FirstOrDefault(s => s.Type == SocketType.Image).Name);

    /// <summary>
    /// Nimmt einen Knoten heraus. Mit <paramref name="reconnect"/> schliesst sich die
    /// Luecke: Was in seinen Bildeingang floss, fliesst jetzt dorthin, wohin sein Bild
    /// floss - so wie man eine Karte aus dem Stapel nimmt, und das Bild laeuft weiter.
    ///
    /// Die Ausgabe laesst sich nicht loeschen: Ohne sie gibt es kein Bild.
    /// </summary>
    public static bool Remove(NodeGraph graph, Node node, bool reconnect = true)
    {
        if (node is OutputNode) return false;

        if (reconnect)
        {
            var (input, output) = Through(node);
            var feed = input is null ? null : graph.Into(node.Id, input);

            if (feed is not null && output is not null && graph.Find(feed.From) is { } source)
            {
                foreach (var link in graph.Links.Where(l => l.From == node.Id && l.Output == output).ToList())
                {
                    if (graph.Find(link.To) is { } reader &&
                        CannotConnect(graph, source, feed.Output, reader, link.Input) is null)
                    {
                        graph.Connect(source, feed.Output, reader, link.Input);
                    }
                }
            }
        }

        graph.Links.RemoveAll(l => l.From == node.Id || l.To == node.Id);
        graph.Nodes.Remove(node);

        return true;
    }

    /// <summary>
    /// Setzt einen Knoten hinter einen anderen: Er bekommt dessen Bild, und alles, was
    /// dieses Bild bisher las, liest jetzt seins. Der Weg, auf dem ein Effekt aus der
    /// Palette in den Graphen kommt.
    /// </summary>
    public static bool InsertAfter(NodeGraph graph, Node after, Node node)
    {
        var (_, output) = Through(after);
        var (input, own) = Through(node);

        if (output is null || input is null || own is null) return false;

        if (!graph.Nodes.Contains(node)) graph.Add(node);

        var readers = graph.Links.Where(l => l.From == after.Id && l.Output == output).ToList();

        graph.Connect(after, output, node, input);

        foreach (var link in readers)
            if (graph.Find(link.To) is { } reader) graph.Connect(node, own, reader, link.Input);

        return true;
    }

    /// <summary>
    /// Legt einen Knoten in eine bestehende Verbindung: A -> B wird A -> Knoten -> B.
    /// So faellt ein Knoten, den man auf ein Kabel zieht, hinein.
    /// </summary>
    public static bool InsertInto(NodeGraph graph, NodeLink link, Node node)
    {
        var (input, output) = Through(node);
        var from = graph.Find(link.From);
        var to = graph.Find(link.To);

        if (input is null || output is null || from is null || to is null) return false;
        if (CannotConnect(graph, from, link.Output, node, input) is not null) return false;

        string target = link.Input;

        graph.Connect(from, link.Output, node, input);

        if (CannotConnect(graph, node, output, to, target) is not null)
        {
            // Passt der Ausgang nicht in den alten Eingang, bleibt alles, wie es war.
            Disconnect(graph, node, input);
            graph.Connect(from, link.Output, to, target);
            return false;
        }

        graph.Connect(node, output, to, target);
        return true;
    }

    /// <summary>
    /// Rueckt Knoten nach rechts, die hinter <paramref name="from"/> liegen und von ihm
    /// gespeist werden - damit ein eingefuegter Knoten Platz hat, statt auf dem naechsten
    /// zu liegen.
    /// </summary>
    public static void MakeRoom(NodeGraph graph, Node from, double by)
    {
        var downstream = new HashSet<string>(StringComparer.Ordinal);
        var open = new Stack<string>();
        open.Push(from.Id);

        while (open.Count > 0)
        {
            string id = open.Pop();

            foreach (var link in graph.Links.Where(l => l.From == id))
                if (downstream.Add(link.To)) open.Push(link.To);
        }

        foreach (var node in graph.Nodes)
        {
            if (!downstream.Contains(node.Id) || ReferenceEquals(node, from)) continue;
            if (node.X > from.X - 1 && Math.Abs(node.Y - from.Y) < 400) node.X += by;
        }
    }
}
