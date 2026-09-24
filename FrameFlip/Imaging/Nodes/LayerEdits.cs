using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>Welche Art Kette von Ebenen.</summary>
public enum LayerChainKind
{
    /// <summary>Die Ebenen selbst - was ueber den Rueckfall zum Bild wird.</summary>
    Main,

    /// <summary>Die Kinder einer Gruppe: Sie liegen auf dem, worauf die Gruppe liegt.</summary>
    Group,

    /// <summary>Was an einen Traeger geschnitten ist: Es liegt auf dessen Bild.</summary>
    Clip,

    /// <summary>Wasserzeichen obenauf, nach der Bildwerdung.</summary>
    Overlay,
}

/// <summary>
/// Ebenen, die aufeinander liegen: Jede liest ueber ihren unteren Eingang die vorige - beim
/// Mischen "Unten", beim Obenauf "Bild". Unten zuerst, wie im Stapel.
/// </summary>
public sealed class LayerChain
{
    internal LayerChain(LayerChainKind kind, MixNode? parent, int depth)
    {
        Kind = kind;
        Parent = parent;
        Depth = depth;
    }

    public LayerChainKind Kind { get; }

    /// <summary>Die Gruppe oder der Traeger, in dessen "Oben" die Kette muendet. Null bei der Hauptkette und den Wasserzeichen.</summary>
    public MixNode? Parent { get; }

    /// <summary>Wie tief in Gruppen - eine Schnittkette steht so tief wie ihr Traeger.</summary>
    public int Depth { get; }

    public List<Node> Members { get; } = new();
}

/// <summary>
/// Ebenen im Graphen als Ganzes bewegen - verschieben, loeschen, verdoppeln.
///
/// Eine Ebene ist im Graphen ein Mischen und alles, was nur ihm zuarbeitet: Quelle,
/// Platzieren, Korrektur, Maske, bei einer Gruppe ihre Kinder, bei einem Traeger was an
/// ihn geschnitten ist. Wer sie verschiebt, verschiebt diesen Zweig mit. Und was im Zweig
/// den Untergrund liest - eine Einstellungsebene, eine Maske auf dem Darunter, die
/// unterste Ebene einer Gruppe -, liest danach den Untergrund an der neuen Stelle. Genau
/// das tut der Stapel, wenn man eine Ebene dort verschiebt; eine Probe verlangt dieselben
/// Bytes.
///
/// Reine Operationen auf dem Modell. Was nicht aufgeht, laesst den Graphen, wie er war.
/// </summary>
public static class LayerEdits
{
    /// <summary>Der Eingang, ueber den eine Ebene liest, was unter ihr liegt. Null: keine Ebene.</summary>
    public static string? Below(Node node) => node switch
    {
        MixNode => "Unten",
        OverlayNode => "Bild",
        _ => null,
    };

    // ------------------------------------------------------------ Ketten

    /// <summary>
    /// Die Ketten eines Graphen: die Hauptkette unter dem Rueckfall, darin je Gruppe und
    /// Traeger die Kette, die in sein "Oben" muendet, und die Folgen der Wasserzeichen.
    /// </summary>
    public static IReadOnlyList<LayerChain> Chains(NodeGraph graph)
    {
        var chains = new List<LayerChain>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        if (NodeEdits.LayerTop(graph) is MixNode top)
        {
            var main = new LayerChain(LayerChainKind.Main, null, 0);
            main.Members.AddRange(Collect(graph, top, floor: null, taken));
            chains.Add(main);
        }

        // Fliesst in ein Mischen oben ein Mischen, ist das eine Kette fuer sich. Endet sie
        // auf dem, worauf das Mischen selbst liegt, ist es eine Gruppe; sonst ein Traeger,
        // und die Kette liegt auf seinem Bild.
        for (int c = 0; c < chains.Count; c++)
        {
            var chain = chains[c];

            foreach (var member in chain.Members.ToList())
            {
                if (member is not MixNode parent ||
                    graph.Into(parent.Id, "Oben") is not { } up || graph.Find(up.From) is not MixNode inner)
                {
                    continue;
                }

                string? floor = graph.Into(parent.Id, "Unten")?.From;
                if (inner.Id == floor || taken.Contains(inner.Id)) continue;

                var members = Collect(graph, inner, floor, taken);
                if (members.Count == 0) continue;

                bool group = floor is not null && graph.Into(members[0].Id, "Unten")?.From == floor;

                var sub = new LayerChain(group ? LayerChainKind.Group : LayerChainKind.Clip, parent,
                                         group ? chain.Depth + 1 : chain.Depth);
                sub.Members.AddRange(members);
                chains.Add(sub);
            }
        }

        // Die Wasserzeichen: jede Folge, die ueber "Bild" aufeinander liegt, von ihrem
        // obersten aus - dem, das kein anderes Wasserzeichen mehr liest.
        foreach (var overlay in graph.Nodes.OfType<OverlayNode>())
        {
            if (taken.Contains(overlay.Id)) continue;

            bool read = graph.Links.Any(l => l.From == overlay.Id && l.Input == "Bild" &&
                                             graph.Find(l.To) is OverlayNode);
            if (read) continue;

            var run = new LayerChain(LayerChainKind.Overlay, null, 0);
            run.Members.AddRange(Collect(graph, overlay, floor: null, taken));
            chains.Add(run);
        }

        return chains;
    }

    /// <summary>Die Kette, in der eine Ebene steht - oder null.</summary>
    public static LayerChain? ChainOf(IReadOnlyList<LayerChain> chains, Node layer)
        => chains.FirstOrDefault(c => c.Members.Contains(layer));

    /// <summary>
    /// Von <paramref name="top"/> ueber den unteren Eingang abwaerts, solange es Ebenen
    /// derselben Art sind und nicht der Boden erreicht ist. Unten zuerst.
    /// </summary>
    private static List<Node> Collect(NodeGraph graph, Node top, string? floor, HashSet<string> taken)
    {
        var members = new List<Node>();
        string input = Below(top)!;
        Node? node = top;

        for (int guard = 0; node is not null && guard <= graph.Nodes.Count; guard++)
        {
            if (node.GetType() != top.GetType() || node.Id == floor || !taken.Add(node.Id)) break;

            members.Add(node);
            node = graph.Into(node.Id, input) is { } below ? graph.Find(below.From) : null;
        }

        members.Reverse();
        return members;
    }

    /// <summary>
    /// Der Zweig einer Ebene: alles, was in ihre anderen Eingaenge fliesst und nicht schon
    /// unter ihr liegt. Das wandert mit, wenn sie wandert, und geht mit, wenn sie geht.
    /// </summary>
    public static HashSet<string> Branch(NodeGraph graph, Node layer)
    {
        string? input = Below(layer);
        var into = graph.Links.ToLookup(l => l.To, StringComparer.Ordinal);

        var below = new HashSet<string>(StringComparer.Ordinal);

        if (input is not null && graph.Into(layer.Id, input) is { } link)
        {
            var open = new Stack<string>();
            open.Push(link.From);

            while (open.Count > 0)
            {
                string id = open.Pop();
                if (!below.Add(id)) continue;

                foreach (var up in into[id]) open.Push(up.From);
            }
        }

        var branch = new HashSet<string>(StringComparer.Ordinal);
        var rest = new Stack<string>(into[layer.Id].Where(l => l.Input != input).Select(l => l.From));

        while (rest.Count > 0)
        {
            string id = rest.Pop();
            if (id == layer.Id || below.Contains(id) || !branch.Add(id)) continue;

            foreach (var up in into[id]) rest.Push(up.From);
        }

        return branch;
    }

    // ------------------------------------------------------------ Verschieben

    /// <summary>
    /// Ob sich eine Ebene neben eine andere legen laesst. Innerhalb einer Kette immer;
    /// zwischen Hauptkette und Gruppen auch. Was angeschnitten ist, bleibt bei seinem
    /// Traeger, und Wasserzeichen bleiben unter sich - dort hiesse Verschieben, etwas
    /// anderes daraus zu machen. Und eine Gruppe kann nicht in sich selbst.
    /// </summary>
    public static bool CanMove(NodeGraph graph, Node layer, Node target, IReadOnlyList<LayerChain>? chains = null)
    {
        if (ReferenceEquals(layer, target) || Below(layer) is null) return false;

        chains ??= Chains(graph);

        if (ChainOf(chains, layer) is not { } from || ChainOf(chains, target) is not { } to) return false;

        bool free(LayerChain chain) => chain.Kind is LayerChainKind.Main or LayerChainKind.Group;

        if (!ReferenceEquals(from, to) && !(free(from) && free(to))) return false;

        return !Branch(graph, layer).Contains(target.Id);
    }

    /// <summary>
    /// Legt eine Ebene samt Zweig ueber oder unter eine andere. Wo sie wegging, schliesst
    /// sich die Luecke; wo sie ankommt, liegt alles darueber jetzt auf ihr.
    /// </summary>
    public static bool Move(NodeGraph graph, Node layer, Node target, bool above)
    {
        if (!CanMove(graph, layer, target)) return false;

        var saved = graph.Links.ToList();

        if (Lift(graph, layer) is not { } lifted || ChainOf(Chains(graph), target) is not { } chain)
            return Restore(graph, saved);

        int at = chain.Members.IndexOf(target);
        Node x;
        string output;
        Node? next;

        if (above)
        {
            x = target;
            output = NodeEdits.Through(target).Output!;
            next = at + 1 < chain.Members.Count ? chain.Members[at + 1] : null;
        }
        else
        {
            if (graph.Into(target.Id, Below(target)!) is not { } below || graph.Find(below.From) is not { } under)
                return Restore(graph, saved);

            x = under;
            output = below.Output;
            next = target;
        }

        Insert(graph, layer, lifted, x, output, next);

        return Sound(graph, layer) || Restore(graph, saved);
    }

    /// <summary>Eine Ebene einen Platz hoeher oder tiefer - innerhalb ihrer Kette.</summary>
    public static bool Step(NodeGraph graph, Node layer, bool up)
    {
        if (ChainOf(Chains(graph), layer) is not { } chain) return false;

        int at = chain.Members.IndexOf(layer);

        return up
            ? at + 1 < chain.Members.Count && Move(graph, layer, chain.Members[at + 1], above: true)
            : at > 0 && Move(graph, layer, chain.Members[at - 1], above: false);
    }

    // ------------------------------------------------------------ Loeschen und verdoppeln

    /// <summary>
    /// Nimmt eine Ebene heraus - samt allem in ihrem Zweig, das sonst niemand liest. Die
    /// Datei bleibt, auch wenn keine Ebene mehr aus ihr liest.
    /// </summary>
    public static bool Remove(NodeGraph graph, Node layer)
    {
        if (Below(layer) is null || !graph.Nodes.Contains(layer)) return false;

        var branch = Branch(graph, layer);
        var saved = graph.Links.ToList();

        if (Lift(graph, layer) is null) return Restore(graph, saved);

        graph.Links.RemoveAll(l => l.To == layer.Id || l.From == layer.Id);
        graph.Nodes.Remove(layer);

        // Von hinten nach vorn: Was niemand mehr liest, geht - und dann, was nur das las.
        for (bool removed = true; removed;)
        {
            removed = false;

            foreach (string id in branch)
            {
                if (graph.Find(id) is not { } node || node is RenderNode or OutputNode) continue;
                if (graph.Links.Any(l => l.From == id)) continue;

                graph.Links.RemoveAll(l => l.To == id);
                graph.Nodes.Remove(node);
                removed = true;
            }
        }

        return true;
    }

    /// <summary>
    /// Verdoppelt eine Ebene samt Zweig und legt die Kopie direkt darueber - wie im Stapel.
    /// Die Datei gibt es nur einmal: Die Kopie liest aus derselben.
    /// </summary>
    /// <returns>Das Mischen (oder Obenauf) der Kopie - oder null.</returns>
    public static Node? Duplicate(NodeGraph graph, Node layer)
    {
        if (Below(layer) is not { } input || graph.Into(layer.Id, input) is not { } below ||
            graph.Find(below.From) is not { } source || ChainOf(Chains(graph), layer) is not { } chain)
        {
            return null;
        }

        var set = Branch(graph, layer);
        set.RemoveWhere(id => graph.Find(id) is null or RenderNode or OutputNode);
        set.Add(layer.Id);

        var copies = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes.Where(n => set.Contains(n.Id)).ToList())
        {
            var copy = graph.Add(NodeGraph.CopyOf(node));
            copy.X = node.X;
            copy.Y = node.Y;
            copies[node.Id] = copy;
        }

        var under = new List<(string To, string Input)>();

        foreach (var link in graph.Links.Where(l => set.Contains(l.To)).ToList())
        {
            if (link.To == layer.Id && link.Input == input) continue;

            var to = copies[link.To];

            if (copies.TryGetValue(link.From, out var from))
                graph.Connect(from, link.Output, to, link.Input);
            else if (link.From == below.From && link.Output == below.Output && !IsSource(source))
                under.Add((to.Id, link.Input));
            else if (graph.Find(link.From) is { } outside)
                graph.Connect(outside, link.Output, to, link.Input);
        }

        int at = chain.Members.IndexOf(layer);
        var next = at + 1 < chain.Members.Count ? chain.Members[at + 1] : null;
        var twin = copies[layer.Id];

        Insert(graph, twin, new Lifted(under), layer, NodeEdits.Through(layer).Output!, next);

        return twin;
    }

    /// <summary>
    /// Eine Einstellungsebene ueber <paramref name="after"/>: eine Korrektur dessen, was
    /// darunter liegt, auf Normal darueber gemischt - wie im Stapel. In einer Schnittkette
    /// wird sie angeschnitten wie die Ebenen neben ihr.
    /// </summary>
    public static (LayerGradeNode Grade, MixNode Mix)? AddAdjustment(NodeGraph graph, Node after)
    {
        if (NodeEdits.Through(after).Output is not { } output) return null;

        bool clip = ChainOf(Chains(graph), after) is { Kind: LayerChainKind.Clip };

        var mix = graph.Add(new MixNode { Clip = clip });
        var grade = graph.Add(new LayerGradeNode
        {
            Adjustments = ImageAdjustments.Neutral,
            Tools = new GradingStack(),
            Adjustment = true,
        });

        NodeEdits.InsertAfter(graph, after, mix);

        graph.Connect(after, output, grade, "Bild");
        graph.Connect(grade, "Bild", mix, "Oben");

        return (grade, mix);
    }

    /// <summary>Ob eine neue Ebene hinter diesem Knoten in einer Schnittkette laege - dann wird sie angeschnitten.</summary>
    public static bool InClip(NodeGraph graph, Node after)
        => ChainOf(Chains(graph), after) is { Kind: LayerChainKind.Clip };

    // ------------------------------------------------------------ Heraus und hinein

    /// <summary>Was vom Zweig einer herausgenommenen Ebene den Untergrund las - Knoten und Eingang.</summary>
    private sealed record Lifted(List<(string To, string Input)> Under);

    /// <summary>
    /// Nimmt eine Ebene aus ihrer Kette: Was sie las, lesen jetzt die, die sie gelesen
    /// haben. Was in ihrem Zweig den Untergrund las, haengt danach lose - es wird beim
    /// Einsetzen an den neuen Untergrund gesteckt.
    ///
    /// Liegt die Ebene direkt auf einer Quelle, ist nicht zu unterscheiden, ob ihr Zweig
    /// den Untergrund liest oder dieselbe Datei als Bild - dann bleibt der Zweig, wie er ist.
    /// </summary>
    private static Lifted? Lift(NodeGraph graph, Node layer)
    {
        string input = Below(layer)!;
        string? output = NodeEdits.Through(layer).Output;

        if (output is null || graph.Into(layer.Id, input) is not { } below || graph.Find(below.From) is not { } source)
            return null;

        var branch = Branch(graph, layer);

        // Auch die Ebene selbst kann den Untergrund noch einmal lesen: Eine leere Gruppe
        // hat ihn in "Unten" und in "Oben".
        var under = IsSource(source)
            ? new List<(string, string)>()
            : graph.Links.Where(l => l.From == below.From && l.Output == below.Output &&
                                     (branch.Contains(l.To) || (l.To == layer.Id && l.Input != input)))
                         .Select(l => (l.To, l.Input))
                         .ToList();

        foreach (var link in graph.Links.Where(l => l.From == layer.Id && l.Output == output).ToList())
            if (graph.Find(link.To) is { } reader) graph.Connect(source, below.Output, reader, link.Input);

        graph.Links.RemoveAll(l => (l.To == layer.Id && l.Input == input) ||
                                   (l.From == below.From && l.Output == below.Output && under.Contains((l.To, l.Input))));

        return new Lifted(under);
    }

    /// <summary>
    /// Setzt eine Ebene auf <paramref name="x"/>. Was dort ueber <paramref name="x"/> lag -
    /// <paramref name="next"/> und was in dessen Zweig <paramref name="x"/> las -, liegt
    /// danach auf ihr. Ohne <paramref name="next"/> kommt sie obenauf: Dann liest alles,
    /// was <paramref name="x"/> las, jetzt sie.
    ///
    /// Nicht alles, was <paramref name="x"/> liest, zieht mit: Liegt darueber eine Gruppe,
    /// lesen ihr Mischen und ihr unterstes Kind denselben Boden. Kommt die Ebene in die
    /// Gruppe, zieht nur das Kind mit; kommt sie unter die Gruppe, beide.
    /// </summary>
    private static void Insert(NodeGraph graph, Node layer, Lifted lifted, Node x, string output, Node? next)
    {
        string input = Below(layer)!;
        string own = NodeEdits.Through(layer).Output!;

        HashSet<string>? above = null;

        if (next is not null)
        {
            above = IsSource(x) ? new HashSet<string>(StringComparer.Ordinal) : Branch(graph, next);
            above.Add(next.Id);
        }

        var movers = graph.Links
            .Where(l => l.From == x.Id && l.Output == output && (above is null || above.Contains(l.To)))
            .ToList();

        graph.Connect(x, output, layer, input);

        foreach (var (to, socket) in lifted.Under)
            if (graph.Find(to) is { } node) graph.Connect(x, output, node, socket);

        foreach (var link in movers)
            if (graph.Find(link.To) is { } reader) graph.Connect(layer, own, reader, link.Input);
    }

    private static bool IsSource(Node node) => node is RenderNode or PictureNode;

    /// <summary>Ob der Graph nach einem Zug noch rechnet und die Ebene in einer Kette steht.</summary>
    private static bool Sound(NodeGraph graph, Node layer)
        => graph.Order() is not null && ChainOf(Chains(graph), layer) is not null;

    private static bool Restore(NodeGraph graph, List<NodeLink> saved)
    {
        graph.Links.Clear();
        graph.Links.AddRange(saved);
        return false;
    }
}
