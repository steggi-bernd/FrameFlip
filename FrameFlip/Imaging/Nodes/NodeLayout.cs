namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ordnet die Knoten eines Graphen so an, dass man ihn lesen kann - wie im Compositor von
/// Blender (docs/Atelier-Knoten-Gruppen.md):
///
/// 1. **Links die Quellen.** Jeder Pass, jede Bilddatei steht als eigener Knoten mit
///    Vorschau am linken Rand - auf der Hoehe dessen, was ihn liest.
/// 2. **Je Ebene eine Gruppe.** Rechts davon liegen die Ebenen untereinander, oben die
///    oberste, wie in der Ebenenliste. In einer Gruppe laeuft der Bildweg von links nach
///    rechts auf ihr Mischen zu, die Masken liegen in einer Bahn darunter.
/// 3. **Die Mischen in einer Spalte.** Alle Gruppen enden an derselben Stelle; dort
///    reicht jedes Mischen sein Bild an das naechste weiter - die Gruppen verbinden sich.
/// 4. **Rechts das Gesamtbild.** Was hinter allen Ebenen auf das fertige Bild wirkt, bis
///    zur Ausgabe - umbrochen, wenn es mehr als eine Zeile wird.
///
/// Die Rahmen um die Gruppen zeichnet der Editor (<see cref="NodeGroups"/>); hier wird nur
/// Platz fuer sie gelassen.
/// </summary>
public static class NodeLayout
{
    public const double Width = 176;
    public const double Header = 24;
    public const double Row = 20;
    public const double Pad = 6;

    /// <summary>Waagerechter Abstand von Spalte zu Spalte.</summary>
    public const double ColumnStep = Width + 64;

    /// <summary>Senkrechter Abstand zwischen Knoten einer Spalte.</summary>
    public const double Gap = 28;

    /// <summary>Die Kennung in <see cref="NodeGraph.Layout"/> fuer diese Anordnung.</summary>
    public const int Grouped = 1;

    /// <summary>Der Kopf eines Rahmens ueber seinen Knoten - dort steht der Name der Ebene.</summary>
    public const double FrameHeader = 26;

    /// <summary>Wie weit ein Rahmen seine Knoten umgibt.</summary>
    public const double FramePad = 14;

    /// <summary>Wie weit der Rahmen einer Gruppe den einer Ebene in ihr umschliesst.</summary>
    public const double FrameNest = 10;

    /// <summary>Luft zwischen zwei Rahmen.</summary>
    public const double FrameGap = 26;

    /// <summary>Wie weit rechts der Quellen die Gruppen beginnen - Platz fuer die Kabel.</summary>
    public const double SourceGap = 110;

    /// <summary>Wie viele Knoten das Gesamtbild in einer Zeile hat, bevor umbrochen wird.</summary>
    public const int PictureColumns = 6;

    /// <summary>Wie hoch ein Knoten ist - dieselbe Rechnung, mit der der Editor ihn zeichnet.</summary>
    public static double Height(Node node)
        => Header + Pad + Rows(node) * Row + Pad + (node.Preview ? PreviewSize(node).Height + Pad : 0);

    /// <summary>Wie viel Platz eine Vorschau unter den Anschluessen braucht.</summary>
    public const double PreviewHeight = NodePreviews.Height;

    /// <summary>Die Vorschau einer Maske: kleiner, sie zeigt nur Hell und Dunkel.</summary>
    public const double MaskPreviewHeight = 54;

    /// <summary>Wie gross die Vorschau eines Knotens gezeichnet wird.</summary>
    public static (double Width, double Height) PreviewSize(Node node)
        => MaskLike(node) ? (MaskPreviewHeight * NodePreviews.Width / NodePreviews.Height, MaskPreviewHeight)
                          : (NodePreviews.Width, PreviewHeight);

    /// <summary>Wo die Vorschau eines Knotens steht - unter den Anschluessen, damit diese bleiben, wo sie sind.</summary>
    public static double PreviewTop(Node node) => node.Y + Header + Pad + Rows(node) * Row + Pad;

    private static int Rows(Node node) => Math.Max(1, Math.Max(ShownInputs(node).Count, node.Outputs.Count));

    /// <summary>
    /// Die Eingaenge, die der Editor zeigt. Eine Maske zeigt nur, was sie wirklich liest
    /// (<see cref="MaskNode.Reads"/>): Ein Verlauf, ein Anstrich oder eine Kryptomatte braucht
    /// weder die Ebene noch den Untergrund. Ein Kabel an einem verborgenen Eingang steckt
    /// weiter und tut, was es vorher tat - nichts.
    /// </summary>
    public static IReadOnlyList<Socket> ShownInputs(Node node)
        => node is MaskNode mask ? mask.Inputs.Where(s => mask.Reads(s.Name)).ToList() : node.Inputs;

    /// <summary>Ob ein Knoten in die Bahn der Masken gehoert: Er gibt eine Zahl je Bildpunkt aus.</summary>
    public static bool MaskLike(Node node) => node.Outputs.Count > 0 && node.Outputs[0].Type == SocketType.Value;

    public static void Arrange(NodeGraph graph)
    {
        var order = graph.Order();
        if (order is null || order.Count == 0) return;

        graph.Layout = Grouped;

        var groups = NodeGroups.Of(graph);
        var readers = graph.Links.ToLookup(l => l.From, StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        var layers = groups.Where(g => !g.IsPicture).ToList();

        // Wie weit vor seinem Mischen ein Knoten steht - die breiteste Gruppe bestimmt, wo
        // die Spalte der Mischen liegt.
        var column = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in layers) Columns(group, readers, column);

        int widest = layers.Count == 0 ? 0 : layers.Max(g => g.Members.Max(m => column[m.Id]));
        double mixX = ColumnStep + SourceGap + widest * ColumnStep;

        var level = Levels(groups);
        double top = FrameHeader + FramePad;
        NodeGroup? previous = null;

        foreach (var group in layers)
        {
            // Schliessen sich nach der vorigen Ebene Rahmen von Gruppen, braucht deren Rand Platz.
            if (previous is not null)
            {
                int closing = Math.Max(0, level[previous.Head.Id] - level[group.Head.Id]);
                top += FramePad + closing * FrameNest + FrameGap + FrameHeader + FramePad;
            }

            top = PlaceGroup(group, column, mixX, top);
            foreach (var member in group.Members) placed.Add(member.Id);

            previous = group;
        }

        if (groups.FirstOrDefault(g => g.IsPicture) is { } picture)
        {
            double x = mixX + ColumnStep + SourceGap;
            double y = layers.Count > 0 ? layers[0].Head.Y : FrameHeader + FramePad;

            PlacePicture(graph, picture, readers, x, y);
            foreach (var member in picture.Members) placed.Add(member.Id);
        }

        PlaceSources(graph, order.Where(n => !placed.Contains(n.Id)).ToList());

        // Was nicht zur Ausgabe fuehrt, steht darunter - sichtbar, aber aus dem Weg.
        var reached = order.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        double bottom = order.Select(n => n.Y + Height(n)).Max() + FrameGap * 3;
        double free = 0;

        foreach (var node in graph.Nodes.Where(n => !reached.Contains(n.Id)))
        {
            node.X = free;
            node.Y = bottom;
            free += ColumnStep;
        }
    }

    /// <summary>
    /// Wie viele Rahmen um den einer Gruppe liegen: bei einer Ebene in einer Gruppe oder an
    /// einem Traeger einer mehr als um diesen.
    /// </summary>
    public static Dictionary<string, int> Levels(IReadOnlyList<NodeGroup> groups)
    {
        var parents = groups.ToDictionary(g => g.Head.Id, g => g.Parent?.Id, StringComparer.Ordinal);
        var level = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            int depth = 0;

            for (string? id = group.Parent?.Id; id is not null && depth < groups.Count; id = parents.GetValueOrDefault(id))
                depth++;

            level[group.Head.Id] = depth;
        }

        return level;
    }

    /// <summary>
    /// Die Spalte jedes Knotens in seiner Gruppe: der laengste Weg bis zu ihrem Kopf, nur
    /// ueber Kabel in der Gruppe. Was den Kopf so nicht erreicht, steht gleich davor.
    /// </summary>
    private static void Columns(NodeGroup group, ILookup<string, NodeLink> readers, Dictionary<string, int> column)
    {
        var ids = group.Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        column[group.Head.Id] = 0;

        // Die Glieder stehen in Rechenreihenfolge - rueckwaerts kommt jeder Leser vor dem,
        // was er liest.
        for (int i = group.Members.Count - 1; i >= 0; i--)
        {
            var node = group.Members[i];
            if (ReferenceEquals(node, group.Head)) continue;

            int best = 0;

            foreach (var link in readers[node.Id])
                if (ids.Contains(link.To) && column.TryGetValue(link.To, out int c)) best = Math.Max(best, c + 1);

            column[node.Id] = Math.Max(1, best);
        }
    }

    /// <summary>
    /// Setzt eine Ebene: oben die Bahn des Bildes, darunter die der Masken, jede Spalte
    /// von oben nach unten. Gibt zurueck, wo die Gruppe unten endet.
    /// </summary>
    private static double PlaceGroup(NodeGroup group, Dictionary<string, int> column, double mixX, double top)
    {
        double laneTop = top;
        double bottom = top;

        // Das Mischen gehoert immer zum Bildweg, auch wenn es eine Maske waere.
        bool InMaskLane(Node node) => !ReferenceEquals(node, group.Head) && MaskLike(node);

        foreach (bool mask in new[] { false, true })
        {
            var lane = group.Members.Where(n => InMaskLane(n) == mask).ToList();
            if (lane.Count == 0) continue;

            double tallest = 0;

            foreach (var stack in lane.GroupBy(n => column[n.Id]))
            {
                double y = laneTop;

                foreach (var node in stack)
                {
                    node.X = mixX - stack.Key * ColumnStep;
                    node.Y = y;
                    y += Height(node) + Gap;
                }

                tallest = Math.Max(tallest, y - Gap - laneTop);
            }

            bottom = laneTop + tallest;
            laneTop = bottom + Gap;
        }

        return bottom;
    }

    /// <summary>
    /// Das Gesamtbild: sein Bildweg von links nach rechts bis zur Ausgabe, umbrochen nach
    /// <see cref="PictureColumns"/> Knoten. Was seitlich hineinfliesst - eine Maske, ein
    /// frei gebauter Zweig -, steht unter dem Knoten, der es liest.
    /// </summary>
    private static void PlacePicture(NodeGraph graph, NodeGroup picture, ILookup<string, NodeLink> readers,
                                     double left, double top)
    {
        var ids = picture.Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        // Der Bildweg, von der Ausgabe rueckwaerts ueber die Eingaenge, die ein Knoten durchreicht.
        var path = new List<Node>();

        for (Node? node = picture.Head; node is not null && ids.Contains(node.Id) && path.Count <= picture.Members.Count;)
        {
            path.Add(node);

            node = NodeEdits.Through(node).Input is { } input && graph.Into(node.Id, input) is { } link
                ? graph.Find(link.From)
                : null;
        }

        path.Reverse();

        var onPath = path.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        // Jeder Knoten neben dem Weg haengt an dem Knoten des Weges, in den er zuerst fliesst.
        var hanging = path.ToDictionary(n => n.Id, _ => new List<Node>(), StringComparer.Ordinal);

        foreach (var node in picture.Members.Where(n => !onPath.Contains(n.Id)))
        {
            string? anchor = null;
            var open = new Queue<string>(new[] { node.Id });
            var seen = new HashSet<string>(StringComparer.Ordinal);

            while (anchor is null && open.Count > 0)
            {
                string id = open.Dequeue();

                foreach (var link in readers[id])
                {
                    if (onPath.Contains(link.To)) { anchor = link.To; break; }
                    if (ids.Contains(link.To) && seen.Add(link.To)) open.Enqueue(link.To);
                }
            }

            hanging[anchor ?? picture.Head.Id].Add(node);
        }

        double y = top;

        for (int start = 0; start < path.Count; start += PictureColumns)
        {
            double tallest = 0;

            for (int i = start; i < Math.Min(path.Count, start + PictureColumns); i++)
            {
                var node = path[i];
                node.X = left + (i - start) * ColumnStep;
                node.Y = y;

                double below = y + Height(node) + Gap;

                foreach (var extra in hanging[node.Id])
                {
                    extra.X = node.X;
                    extra.Y = below;
                    below += Height(extra) + Gap;
                }

                tallest = Math.Max(tallest, below - Gap - y);
            }

            y += tallest + Gap * 2;
        }
    }

    /// <summary>
    /// Die Quellen am linken Rand: jede auf der Hoehe des obersten Knotens, der sie liest,
    /// ohne sich zu ueberdecken. Die Datei selbst zuerst, sie liest das Gesamtbild.
    /// </summary>
    private static void PlaceSources(NodeGraph graph, IReadOnlyList<Node> sources)
    {
        var wanted = new List<(Node Node, double Y)>();

        foreach (var source in sources)
        {
            double y = graph.Links.Where(l => l.From == source.Id)
                                  .Select(l => graph.Find(l.To))
                                  .OfType<Node>()
                                  .Where(n => !sources.Contains(n))
                                  .Select(n => n.Y)
                                  .DefaultIfEmpty(double.MaxValue)
                                  .Min();

            wanted.Add((source, y));
        }

        double next = double.MinValue;
        double last = 0;

        foreach (var (node, y) in wanted.OrderBy(w => w.Y))
        {
            double at = y == double.MaxValue ? last : y;
            at = Math.Max(at, next);

            node.X = 0;
            node.Y = at;

            next = at + Height(node) + Gap;
            last = next;
        }
    }
}
