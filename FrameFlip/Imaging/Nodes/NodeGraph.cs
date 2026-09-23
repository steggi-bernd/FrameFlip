using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Nodes;

/// <summary>Was an einem Anschluss fliesst.</summary>
public enum SocketType
{
    /// <summary>
    /// Farbe samt Deckung - und die Angabe, ob die Deckung eine Freistellung ist. Siehe
    /// docs/Atelier-Nodes.md, Abschnitt 1.
    /// </summary>
    Image,

    /// <summary>Eine Zahl je Bildpunkt: eine Maske, ein Anteil.</summary>
    Value,

    /// <summary>Ein Pass, als Daten gelesen - Tiefe, Bewegung, Normalen.</summary>
    Data,
}

/// <summary>Ein Anschluss eines Knotens.</summary>
/// <param name="Source">
/// Bei einem Ausgang: Hier kommt ein gelesenes Bild in voller Aufloesung heraus. Bei
/// einem Eingang: Hier MUSS eines ankommen - der Knoten liest an Stellen, die nicht auf
/// dem Gitter liegen (Platzieren, Renderdaten, das Obenauf).
/// </param>
public readonly record struct Socket(string Name, SocketType Type, bool Source = false);

/// <summary>Eine Verbindung: ein Ausgang auf einen Eingang.</summary>
public sealed class NodeLink
{
    public string From { get; set; } = "";
    public string Output { get; set; } = "";
    public string To { get; set; } = "";
    public string Input { get; set; } = "";
}

/// <summary>
/// Ein Knoten im Graphen.
///
/// Die Kennung im gespeicherten Graphen entscheidet beim Lesen ueber den Typ - wie bei
/// den Werkzeugen. Sie darf sich deshalb nie aendern, auch wenn die Klasse spaeter
/// anders heisst.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "node",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(RenderNode), RenderNode.KindName)]
[JsonDerivedType(typeof(PictureNode), PictureNode.KindName)]
[JsonDerivedType(typeof(BlackNode), BlackNode.KindName)]
[JsonDerivedType(typeof(PlaceNode), PlaceNode.KindName)]
[JsonDerivedType(typeof(ExposureTintNode), ExposureTintNode.KindName)]
[JsonDerivedType(typeof(MaskNode), MaskNode.KindName)]
[JsonDerivedType(typeof(LayerGradeNode), LayerGradeNode.KindName)]
[JsonDerivedType(typeof(RestrictNode), RestrictNode.KindName)]
[JsonDerivedType(typeof(MixNode), MixNode.KindName)]
[JsonDerivedType(typeof(FallbackNode), FallbackNode.KindName)]
[JsonDerivedType(typeof(LightNode), LightNode.KindName)]
[JsonDerivedType(typeof(PointToolNode), PointToolNode.KindName)]
[JsonDerivedType(typeof(OpticsNode), OpticsNode.KindName)]
[JsonDerivedType(typeof(LocalNode), LocalNode.KindName)]
[JsonDerivedType(typeof(GeometryNode), GeometryNode.KindName)]
[JsonDerivedType(typeof(DataNode), DataNode.KindName)]
[JsonDerivedType(typeof(ViewNode), ViewNode.KindName)]
[JsonDerivedType(typeof(ToneNode), ToneNode.KindName)]
[JsonDerivedType(typeof(OverlayNode), OverlayNode.KindName)]
[JsonDerivedType(typeof(FramePassNode), FramePassNode.KindName)]
[JsonDerivedType(typeof(OutputNode), OutputNode.KindName)]
public abstract class Node
{
    /// <summary>Eindeutig im Graphen. Die Verbindungen nennen den Knoten ueber sie.</summary>
    public string Id { get; set; } = "";

    /// <summary>Wo der Knoten im Editor steht - fuer das Rechnen ohne Bedeutung.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Stummgeschaltet: Der Knoten reicht sein erstes Bild unveraendert durch und
    /// behaelt seine Einstellung. Dasselbe wie der Ausschalter einer Karte im Stapel.
    /// </summary>
    public bool Muted { get; set; }

    [JsonIgnore]
    public abstract IReadOnlyList<Socket> Inputs { get; }

    [JsonIgnore]
    public abstract IReadOnlyList<Socket> Outputs { get; }

    /// <summary>Rechnet den Knoten - siehe <see cref="NodeRun"/>.</summary>
    internal abstract void Run(NodeRun run);

    /// <summary>Der Eingang, der bei einem stummen Knoten durchgereicht wird.</summary>
    internal virtual string? Through => Inputs.FirstOrDefault(s => s.Type == SocketType.Image).Name;

    public Socket? Input(string name)
    {
        foreach (var socket in Inputs)
            if (socket.Name == name) return socket;

        return null;
    }

    public Socket? Output(string name)
    {
        foreach (var socket in Outputs)
            if (socket.Name == name) return socket;

        return null;
    }
}

/// <summary>
/// Ein Graph aus Knoten und Verbindungen - die Alternative zum Stapel, siehe
/// docs/Atelier-Nodes.md.
///
/// Das Modell weiss nichts vom Rechnen und nichts von der Oberflaeche. Was es
/// zusichert: <see cref="Problems"/> nennt alles, was einen Graphen unrechenbar macht,
/// und <see cref="Order"/> liefert die Reihenfolge, in der gerechnet wird - oder null,
/// wenn es keine gibt.
/// </summary>
public sealed class NodeGraph
{
    public int Version { get; set; } = 1;

    public List<Node> Nodes { get; set; } = new();

    public List<NodeLink> Links { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Fuegt einen Knoten hinzu und gibt ihm eine freie Kennung, falls noetig.</summary>
    public T Add<T>(T node) where T : Node
    {
        if (node.Id.Length == 0 || Nodes.Any(n => n.Id == node.Id))
        {
            int number = Nodes.Count + 1;
            while (Nodes.Any(n => n.Id == $"n{number}")) number++;

            node.Id = $"n{number}";
        }

        Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Verbindet einen Ausgang mit einem Eingang. Ein Eingang nimmt genau eine
    /// Verbindung - eine vorhandene wird ersetzt, wie im Compositor von Blender.
    /// </summary>
    public void Connect(Node from, string output, Node to, string input)
    {
        Links.RemoveAll(l => l.To == to.Id && l.Input == input);
        Links.Add(new NodeLink { From = from.Id, Output = output, To = to.Id, Input = input });
    }

    public Node? Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>Die Verbindung, die in diesen Eingang fuehrt - oder null.</summary>
    public NodeLink? Into(string node, string input)
        => Links.FirstOrDefault(l => l.To == node && l.Input == input);

    /// <summary>Der Ausgabeknoten - der eine, an dem das Bild herauskommt.</summary>
    [JsonIgnore]
    public OutputNode? Output => Nodes.OfType<OutputNode>().FirstOrDefault();

    /// <summary>
    /// Was diesen Graphen unrechenbar macht. Leer heisst: Er laesst sich rechnen.
    ///
    /// Die Liste ist fuer Menschen geschrieben - sie steht spaeter im Editor, wo ein
    /// Kabel nicht passt.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        int outputs = Nodes.Count(n => n is OutputNode);
        if (outputs != 1) problems.Add($"{outputs} Ausgaben statt einer");

        foreach (var group in Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1))
            problems.Add($"Kennung {group.Key} kommt {group.Count()} Mal vor");

        foreach (var link in Links)
        {
            var from = Find(link.From);
            var to = Find(link.To);

            if (from is null || to is null)
            {
                problems.Add($"Verbindung {link.From} -> {link.To} fuehrt ins Leere");
                continue;
            }

            var output = from.Output(link.Output);
            var input = to.Input(link.Input);

            if (output is null || input is null)
            {
                problems.Add($"Anschluss {link.From}.{link.Output} -> {link.To}.{link.Input} gibt es nicht");
                continue;
            }

            if (!Fits(output.Value.Type, input.Value.Type))
                problems.Add($"{link.From}.{link.Output} ({output.Value.Type}) passt nicht auf {link.To}.{link.Input} ({input.Value.Type})");

            if (input.Value.Source && !output.Value.Source)
                problems.Add($"{link.To}.{link.Input} braucht ein gelesenes Bild, {link.From}.{link.Output} liefert ein gerechnetes");
        }

        foreach (var group in Links.GroupBy(l => (l.To, l.Input)).Where(g => g.Count() > 1))
            problems.Add($"In {group.Key.To}.{group.Key.Input} fuehren {group.Count()} Verbindungen");

        if (problems.Count == 0 && Order() is null) problems.Add("Der Graph hat einen Kreis");

        return problems;
    }

    /// <summary>
    /// Welche Anschlussarten zusammenpassen. Ein Bild darf in eine Maske (seine
    /// Helligkeit) und in einen Datenanschluss (ein Pass ist ein Bild); eine Maske darf
    /// nicht zum Bild werden - das waere eine Umwandlung, die niemand gemeint hat.
    /// </summary>
    public static bool Fits(SocketType output, SocketType input)
        => output == input ||
           (output == SocketType.Image && input is SocketType.Value or SocketType.Data);

    /// <summary>
    /// Die Knoten, die zur Ausgabe beitragen, in einer Reihenfolge, in der jeder nach
    /// allem kommt, was er liest. Null, wenn es keine Ausgabe gibt oder einen Kreis.
    ///
    /// Was nicht zur Ausgabe fuehrt, wird nicht gerechnet - ein liegengelassener Knoten
    /// im Editor kostet nichts.
    /// </summary>
    public IReadOnlyList<Node>? Order()
    {
        var output = Output;
        if (output is null) return null;

        var order = new List<Node>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(Node node)
        {
            if (done.Contains(node.Id)) return true;
            if (!visiting.Add(node.Id)) return false;

            foreach (var link in Links.Where(l => l.To == node.Id))
            {
                var from = Find(link.From);
                if (from is not null && !Visit(from)) return false;
            }

            visiting.Remove(node.Id);
            done.Add(node.Id);
            order.Add(node);

            return true;
        }

        return Visit(output) ? order : null;
    }

    public string Save() => JsonSerializer.Serialize(this, Options);

    public static NodeGraph? Load(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NodeGraph>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Eine eigene Kopie - Knoten und Werkzeuge sind danach andere Objekte.</summary>
    public NodeGraph Clone() => Load(Save())!;
}
