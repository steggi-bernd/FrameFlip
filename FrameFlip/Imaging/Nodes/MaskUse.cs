namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Wer eine Maske benutzt - fuer das Menue einer Maske, die Darstellung im Graphen und die
/// Ebenenliste (docs/Projekte-und-Masken.md, Punkt 3 und 11). Alle drei fragen hier, damit
/// "geteilt", "frei" und die hervorgehobenen Ebenen ueberall dasselbe heissen.
/// </summary>
public static class MaskUse
{
    /// <summary>Die Ebenen, die diese Maske begrenzt - die Mischen, in deren Faktor sie steckt.</summary>
    public static List<MixNode> Limited(NodeGraph graph, Node mask)
        => graph.Links
                .Where(l => l.From == mask.Id && l.Input == "Faktor")
                .Select(l => graph.Find(l.To))
                .OfType<MixNode>()
                .Distinct()
                .ToList();

    /// <summary>
    /// Alle Ebenen, die sie benutzen: die sie begrenzt, und die aus ihr ausgeschnitten sind -
    /// deren Mischen oben ein Ausschneiden mit dieser Maske liest.
    /// </summary>
    public static List<MixNode> Users(NodeGraph graph, Node mask)
    {
        var users = Limited(graph, mask);

        foreach (var cut in graph.Links.Where(l => l.From == mask.Id && l.Input == "Maske").Select(l => graph.Find(l.To)).OfType<CutoutNode>())
        {
            foreach (var mix in graph.Links.Where(l => l.From == cut.Id && l.Input == "Oben").Select(l => graph.Find(l.To)).OfType<MixNode>())
                if (!users.Contains(mix)) users.Add(mix);
        }

        return users;
    }

    /// <summary>Eine Maske, die nirgends steckt.</summary>
    public static bool Free(NodeGraph graph, MaskNode mask) => !graph.Links.Any(l => l.From == mask.Id);

    /// <summary>Die Masken, die nirgends stecken - in der Reihenfolge des Graphen.</summary>
    public static List<MaskNode> FreeMasks(NodeGraph graph)
        => graph.Nodes.OfType<MaskNode>().Where(m => Free(graph, m)).ToList();

    /// <summary>
    /// Ob ein Kabel eine Maske traegt: aus einem Maskenknoten, oder dorthin, wo eine Maske
    /// wirkt - in den Faktor eines Mischens, die Maske eines Ausschneidens oder Begrenzens.
    /// </summary>
    public static bool CarriesMask(NodeGraph graph, NodeLink link)
        => graph.Find(link.From) is MaskNode || graph.Find(link.To) switch
        {
            MixNode => link.Input == "Faktor",
            CutoutNode or RestrictNode => link.Input == "Maske",
            _ => false,
        };
}
