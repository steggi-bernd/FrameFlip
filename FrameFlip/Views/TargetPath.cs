using FrameFlip.Atelier;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>Ein Glied der Zielzeile: was dasteht, wohin ein Klick fuehrt (null: nirgendwohin), ob es das Ziel selbst ist.</summary>
public sealed record TargetStep(string Text, EditingTarget? GoTo, bool Current);

/// <summary>
/// "Woran arbeite ich gerade?" als Zeile ueber dem Bild (docs/Atelier-Arbeitsablauf.md, C1):
/// "Ebene: Beauty › Maske: Auto, Rad" oder "Gesamtbild › Knoten: Tonwert".
///
/// Abgeleitet aus dem Bearbeitungsziel der Sitzung, nicht zusaetzlich gepflegt - die Zeile kann
/// darum nicht etwas anderes sagen als das, wohin der naechste Regler geht.
/// </summary>
public static class TargetPath
{
    /// <summary>Die Glieder der Zeile und ein Hinweis dahinter - oder keiner.</summary>
    public static (IReadOnlyList<TargetStep> Steps, string? Note) For(EditingTarget target, NodeGraph? graph)
    {
        switch (target)
        {
            case EditingTarget.StackLayer { Layer: var layer, Tools: var tools }:
            {
                var steps = new[] { new TargetStep(Strings.T("S_TargetLayer", layer.Name), null, true) };

                // Die Ebene ist gewaehlt, der Farbstreifen gilt aber dem Bild: Rahmen und Pinsel
                // wirken auf die Ebene, die Regler auf alles. Das muss dastehen.
                return (steps, tools ? null : Strings.T("S_TargetColourOnPicture"));
            }

            case EditingTarget.GraphNode { Node: var node } when graph is not null:
                return (ForNode(node, graph), null);

            default:
                return (new[] { new TargetStep(Strings.T("S_TargetPicture"), null, true) }, null);
        }
    }

    private static IReadOnlyList<TargetStep> ForNode(Node node, NodeGraph graph)
    {
        var layers = NodeLayerList.Of(graph);

        // Die Ebene, der der Knoten gehoert - die innerste, wenn er in einer Gruppe steckt.
        NodeLayer? owner = null;
        int ownerSize = int.MaxValue;

        foreach (var layer in layers)
        {
            if (ReferenceEquals(layer.Target, node))
            {
                owner = layer;
                ownerSize = 0;
                break;
            }

            if (layer.Mix is not { } mix) continue;

            var branch = LayerEdits.Branch(graph, mix);
            if (branch.Contains(node.Id) && branch.Count < ownerSize)
            {
                owner = layer;
                ownerSize = branch.Count;
            }
        }

        if (owner is null)
        {
            return new[]
            {
                new TargetStep(Strings.T("S_TargetPicture"), EditingTarget.Picture, false),
                new TargetStep(Strings.T("S_TargetNode", NodeTitles.For(node)), null, true),
            };
        }

        // Die Ebene selbst: Ein Klick waehlt sie wie in der Liste.
        var layerTarget = owner.Target is { } chosen ? new EditingTarget.GraphNode(chosen, owner.Mix is not null) : null;

        if (ReferenceEquals(owner.Target, node))
            return new[] { new TargetStep(Strings.T("S_TargetLayer", owner.Name), null, true) };

        var head = new TargetStep(Strings.T("S_TargetLayer", owner.Name), layerTarget, false);

        // Zur Maske gehoert, was in sie fliesst und nicht auch ins Bild der Ebene. Masken lesen das
        // Bild der Ebene als Eingang - die Korrektur der Ebene liegt also auch "vor" ihrer Maske,
        // bleibt aber ein Knoten der Ebene.
        bool inPicture = owner.Mix is { } layerMix && graph.Into(layerMix.Id, "Oben") is { } picture &&
                         (picture.From == node.Id || Upstream(graph, picture.From).Contains(node.Id));

        if (owner.MaskSource is { } mask && !inPicture && (ReferenceEquals(mask, node) || Upstream(graph, mask.Id).Contains(node.Id)))
        {
            var maskStep = ReferenceEquals(mask, node)
                ? new TargetStep(Strings.T("S_TargetMask", NodeTitles.MaskName(mask)), null, true)
                : new TargetStep(Strings.T("S_TargetMask", NodeTitles.MaskName(mask)), new EditingTarget.GraphNode(mask, false), false);

            return ReferenceEquals(mask, node)
                ? new[] { head, maskStep }
                : new[] { head, maskStep, new TargetStep(Strings.T("S_TargetNode", NodeTitles.For(node)), null, true) };
        }

        return new[] { head, new TargetStep(Strings.T("S_TargetNode", NodeTitles.For(node)), null, true) };
    }

    /// <summary>Alles, was in einen Knoten fliesst - ohne ihn selbst.</summary>
    private static HashSet<string> Upstream(NodeGraph graph, string id)
    {
        var into = graph.Links.ToLookup(l => l.To, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var open = new Stack<string>(into[id].Select(l => l.From));

        while (open.Count > 0)
        {
            string next = open.Pop();
            if (!found.Add(next)) continue;

            foreach (var up in into[next]) open.Push(up.From);
        }

        return found;
    }
}
