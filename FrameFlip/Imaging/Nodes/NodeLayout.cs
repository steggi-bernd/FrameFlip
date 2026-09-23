namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ordnet die Knoten eines Graphen so an, dass man ihn lesen kann.
///
/// Die erste Fassung stellte jeden Knoten in die Spalte seines laengsten Wegs von der
/// Quelle - bei einem Stapel aus sechzehn Passen ergab das eine einzige Zeile von
/// dreissig Knoten, und "alles zeigen" hiess: alles winzig. Drei Dinge machen es jetzt
/// lesbar:
///
/// 1. **Spalten nach dem Abstand zur Ausgabe**, nicht zur Quelle. Damit steht der Zweig
///    einer Ebene unmittelbar vor dem Mischen, in das er muendet - statt dass alle
///    Platzieren-Knoten in der zweiten Spalte uebereinander stehen.
/// 2. **Umbrochen wie Text.** Nach einer festen Zahl Spalten beginnt eine neue Zeile
///    darunter. Ein langer Graph wird ein Rechteck statt eines Bandes.
/// 3. **In jeder Spalte nach den Kabeln sortiert**: Ein Knoten steht ungefaehr auf der
///    Hoehe dessen, was ihn liest. So kreuzen sich weniger Kabel.
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

    /// <summary>Senkrechter Abstand zwischen zwei Zeilen des Umbruchs.</summary>
    public const double BandGap = 90;

    /// <summary>Wie viele Spalten eine Zeile hat, bevor umbrochen wird.</summary>
    public const int Columns = 7;

    /// <summary>Wie hoch ein Knoten ist - dieselbe Rechnung, mit der der Editor ihn zeichnet.</summary>
    public static double Height(Node node)
    {
        int rows = Math.Max(1, Math.Max(node.Inputs.Count, node.Outputs.Count));

        return Header + Pad + rows * Row + Pad + (node.Preview ? PreviewHeight + Pad : 0);
    }

    /// <summary>Wie viel Platz eine Vorschau unter den Anschluessen braucht.</summary>
    public const double PreviewHeight = NodePreviews.Height;

    /// <summary>Wo die Vorschau eines Knotens steht - unter den Anschluessen, damit diese bleiben, wo sie sind.</summary>
    public static double PreviewTop(Node node)
    {
        int rows = Math.Max(1, Math.Max(node.Inputs.Count, node.Outputs.Count));

        return node.Y + Header + Pad + rows * Row + Pad;
    }

    public static void Arrange(NodeGraph graph)
    {
        var order = graph.Order();
        if (order is null || order.Count == 0) return;

        var inOrder = new HashSet<string>(order.Select(n => n.Id), StringComparer.Ordinal);

        var readers = order.ToDictionary(n => n.Id, _ => new List<Node>(), StringComparer.Ordinal);

        foreach (var link in graph.Links)
        {
            if (!inOrder.Contains(link.From) || !inOrder.Contains(link.To)) continue;
            if (graph.Find(link.To) is { } to && !readers[link.From].Contains(to)) readers[link.From].Add(to);
        }

        // Abstand zur Ausgabe: der laengste Weg, rueckwaerts gezaehlt.
        var distance = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            var after = readers[node.Id];

            distance[node.Id] = after.Count == 0 ? 0 : after.Max(r => distance.GetValueOrDefault(r.Id)) + 1;
        }

        int deepest = distance.Values.Max();
        var rank = order.ToDictionary(n => n.Id, n => deepest - distance[n.Id], StringComparer.Ordinal);

        // Von rechts nach links: Jede Spalte ordnet sich nach der Hoehe ihrer Leser.
        var slot = new Dictionary<string, double>(StringComparer.Ordinal);
        var columns = order.GroupBy(n => rank[n.Id]).OrderByDescending(g => g.Key);

        foreach (var column in columns)
        {
            var sorted = column
                .Select((node, index) => (node, index, key: readers[node.Id].Count == 0
                    ? index
                    : readers[node.Id].Average(r => slot.GetValueOrDefault(r.Id))))
                .OrderBy(entry => entry.key)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.node)
                .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                // Die Reihenfolge der Leser zaehlt, nicht ihre Nummer - sonst rueckte ein
                // Knoten mit einem Leser weit unten ganz nach unten.
                double wanted = readers[sorted[i].Id].Count == 0
                    ? i
                    : readers[sorted[i].Id].Average(r => slot.GetValueOrDefault(r.Id));

                slot[sorted[i].Id] = Math.Max(wanted, i > 0 ? slot[sorted[i - 1].Id] + 1 : wanted);
            }
        }

        // Umbrechen: Spalte -> Zeile und Stelle darin, dann von oben nach unten stapeln.
        double top = 0;

        foreach (var band in order.GroupBy(n => rank[n.Id] / Columns).OrderBy(g => g.Key))
        {
            double tallest = 0;

            foreach (var column in band.GroupBy(n => rank[n.Id]))
            {
                double y = top;

                foreach (var node in column.OrderBy(n => slot[n.Id]))
                {
                    node.X = rank[node.Id] % Columns * ColumnStep;
                    node.Y = y;
                    y += Height(node) + Gap;
                }

                tallest = Math.Max(tallest, y - top);
            }

            top += tallest + BandGap;
        }

        // Was nicht zur Ausgabe fuehrt, steht darunter - sichtbar, aber aus dem Weg.
        double x = 0;

        foreach (var node in graph.Nodes.Where(n => !inOrder.Contains(n.Id)))
        {
            node.X = x;
            node.Y = top;
            x += ColumnStep;
        }
    }
}
