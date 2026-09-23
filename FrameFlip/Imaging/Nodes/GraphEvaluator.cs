using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>Was ein Graph zum Rechnen braucht - fuer ein Bild der Folge.</summary>
public sealed class GraphInputs
{
    /// <summary>
    /// Die gelesenen Quellen. Unter dem leeren Schluessel das Bild der Datei, sonst Passe
    /// nach Namen und Bilddateien nach Pfad - dieselben Schluessel wie im Stapel, siehe
    /// <see cref="GraphEvaluator.Reads"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, FloatFrame> Sources { get; init; }

    /// <summary>Die Renderdaten dieser Datei, nach Bedeutung - fehlende fehlen.</summary>
    public IReadOnlyDictionary<PassNeed, FloatFrame?> Data { get; init; } = new Dictionary<PassNeed, FloatFrame?>();

    public required IViewTransform View { get; init; }

    /// <summary>Jeder wievielte Bildpunkt gerechnet wird - beim Ziehen eines Reglers mehr als einer.</summary>
    public int Step { get; init; } = 1;

    /// <summary>Die Bildnummer - fuer Korn, gemalte Masken und alles, was sich ueber die Folge bewegt.</summary>
    public int Number { get; init; }
}

/// <summary>
/// Rechnet einen Graphen - in dieselben Puffer, in die der Prozessor den Stapel rechnet.
///
/// Richtig vor schnell: Jeder Knoten rechnet in ein eigenes Feld, und ein Feld wird
/// losgelassen, sobald der letzte Knoten gelesen hat, der es braucht. Zwei Dinge fasst
/// der Auswerter trotzdem zusammen, weil der Stapel sie zusammen rechnet und ein anderes
/// Bild herauskaeme: aufeinanderfolgende oertliche Werkzeuge und aufeinanderfolgende
/// Geometrie. Siehe docs/Atelier-Nodes.md, Abschnitt 3.
/// </summary>
public static class GraphEvaluator
{
    /// <summary>
    /// Was gelesen werden muss - mit denselben Schluesseln wie im Stapel, damit derselbe
    /// Lesepfad (<see cref="LayeredFrameLoader.Read"/>) beide bedient.
    ///
    /// Das Bild der Datei wird immer gelesen: Es ist die Leinwand, und es ist die
    /// Antwort, wenn keine Ebene etwas beitraegt.
    /// </summary>
    public static IReadOnlyList<LayerRead> Reads(NodeGraph graph)
    {
        var reads = new List<LayerRead> { new("", LayerContent.Pass, false) };

        void Add(LayerRead read)
        {
            if (!reads.Any(r => r.Key.Equals(read.Key, StringComparison.Ordinal))) reads.Add(read);
        }

        var order = graph.Order() ?? Array.Empty<Node>();
        var used = Used(graph, order);

        foreach (var node in order)
        {
            switch (node)
            {
                case RenderNode render:
                    foreach (string pass in render.Passes)
                        if (used.Contains((render.Id, pass))) Add(new LayerRead(pass, LayerContent.Pass, false));
                    break;

                case PictureNode picture:
                    Add(picture.Path.Length == 0
                        ? new LayerRead("", LayerContent.Pass, false)
                        : new LayerRead(picture.Path, LayerContent.Image, picture.FollowSequence));
                    break;

                case MaskNode mask when !mask.Muted:
                    // Eine Passmaske mit Kabel liest, was am Kabel ankommt - den Pass,
                    // den sie beim Namen nennt, braucht sie dann nicht.
                    if (mask.Mask.Kind == MaskKind.Pass && graph.Into(mask.Id, "Pass") is not null) break;

                    foreach (string source in mask.Mask.Sources()) Add(new LayerRead(source, LayerContent.Pass, false));
                    break;
            }
        }

        return reads;
    }

    /// <summary>Welche Renderdaten der Graph braucht - Tiefe, Bewegung, Normalen.</summary>
    public static IReadOnlyList<PassNeed> Needs(NodeGraph graph)
    {
        var order = graph.Order() ?? Array.Empty<Node>();
        var used = Used(graph, order);
        var needs = new List<PassNeed>();

        foreach (var render in order.OfType<RenderNode>())
        {
            foreach (string output in new[] { RenderNode.Depth, RenderNode.Motion, RenderNode.Normal })
            {
                if (used.Contains((render.Id, output)) && RenderNode.NeedFor(output) is { } need && !needs.Contains(need))
                    needs.Add(need);
            }
        }

        return needs;
    }

    /// <summary>Welche Ausgaenge von Knoten gelesen werden, die selbst gerechnet werden.</summary>
    private static HashSet<(string, string)> Used(NodeGraph graph, IReadOnlyList<Node> order)
    {
        var inOrder = new HashSet<string>(order.Select(n => n.Id), StringComparer.Ordinal);

        return graph.Links.Where(l => inOrder.Contains(l.To)).Select(l => (l.From, l.Output)).ToHashSet();
    }

    /// <summary>
    /// Die Leinwand: so gross wie das Bild der Datei. Fehlt es, der erste gelesene Pass,
    /// dann das groesste gelesene Bild - dieselbe Reihenfolge wie im Composer.
    /// </summary>
    private static (int Width, int Height) Canvas(IReadOnlyDictionary<string, FloatFrame> sources)
    {
        if (sources.TryGetValue("", out var picture)) return (picture.Width, picture.Height);

        int width = 0, height = 0;

        foreach (var frame in sources.Values)
        {
            if ((long)frame.Width * frame.Height <= (long)width * height) continue;

            width = frame.Width;
            height = frame.Height;
        }

        return (width, height);
    }

    /// <summary>
    /// Rechnet den Graphen nach Bgra32 - acht Bit, wie der Prozessor fuer die Anzeige.
    /// False, wenn er sich nicht rechnen laesst oder nichts herauskommt.
    /// </summary>
    public static unsafe bool Render(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
    {
        var (image, context) = Evaluate(graph, inputs, sixteen: false);
        if (image is null || context is null) return false;

        byte* target = (byte*)destination.ToPointer();
        var rgb = image.Rgb;
        var a = image.A;

        if (context.Step == 1)
        {
            int width = context.Width;

            Parallel.For(0, context.Height, NodeContext.Parallel, y =>
            {
                byte* row = target + (long)y * stride;

                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    byte* pixel = row + x * 4;

                    pixel[0] = FloatFrameProcessor.ToByte(rgb[i * 3 + 2]);
                    pixel[1] = FloatFrameProcessor.ToByte(rgb[i * 3 + 1]);
                    pixel[2] = FloatFrameProcessor.ToByte(rgb[i * 3]);
                    pixel[3] = FloatFrameProcessor.ToByte(Math.Clamp(a[i], 0f, 1f));
                }
            });

            return true;
        }

        // Grob: erst ins Bytegitter, dann dazwischen auffuellen - derselbe Weg und
        // dieselbe Funktion wie im Prozessor.
        int gridWidth = context.GridWidth, gridHeight = context.GridHeight;
        var grid = new byte[gridWidth * gridHeight * 4];

        for (int i = 0; i < gridWidth * gridHeight; i++)
        {
            grid[i * 4] = FloatFrameProcessor.ToByte(rgb[i * 3 + 2]);
            grid[i * 4 + 1] = FloatFrameProcessor.ToByte(rgb[i * 3 + 1]);
            grid[i * 4 + 2] = FloatFrameProcessor.ToByte(rgb[i * 3]);
            grid[i * 4 + 3] = FloatFrameProcessor.ToByte(Math.Clamp(a[i], 0f, 1f));
        }

        fixed (byte* gridPtr = grid)
            FloatFrameProcessor.Expand(gridPtr, gridWidth, gridHeight, target, stride,
                                       context.Width, context.Height, context.Step);

        return true;
    }

    /// <summary>
    /// Rechnet den Graphen nach Rgba64 - sechzehn Bit, fuer den Export. Immer im vollen
    /// Durchgang und ohne die Durchgaenge ueber das fertige Bild, wie im Stapel.
    /// </summary>
    public static unsafe bool Render16(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
    {
        var (image, context) = Evaluate(graph, inputs, sixteen: true);
        if (image is null || context is null) return false;

        var rgb = image.Rgb;
        var a = image.A;
        int width = context.Width;

        Parallel.For(0, context.Height, NodeContext.Parallel, y =>
        {
            ushort* row = (ushort*)((byte*)destination.ToPointer() + (long)y * stride);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                ushort* pixel = row + x * 4;

                pixel[0] = FloatFrameProcessor.ToUShort(rgb[i * 3]);
                pixel[1] = FloatFrameProcessor.ToUShort(rgb[i * 3 + 1]);
                pixel[2] = FloatFrameProcessor.ToUShort(rgb[i * 3 + 2]);
                pixel[3] = FloatFrameProcessor.ToUShort(Math.Clamp(a[i], 0f, 1f));
            }
        });

        return true;
    }

    /// <summary>
    /// Rechnet den Graphen bis zur Ausgabe und gibt das Bild auf dem Gitter zurueck.
    /// </summary>
    internal static (GridImage? Image, NodeContext? Context) Evaluate(NodeGraph graph, GraphInputs inputs, bool sixteen)
    {
        var order = graph.Order();
        if (order is null) return (null, null);

        var (width, height) = Canvas(inputs.Sources);
        if (width == 0 || height == 0) return (null, null);

        int step = sixteen ? 1 : Math.Clamp(inputs.Step, 1, 16);

        var context = new NodeContext
        {
            Width = width,
            Height = height,
            Step = step,
            Columns = LocalPass.Grid(width, step),
            Rows = LocalPass.Grid(height, step),
            Number = inputs.Number,
            View = inputs.View,
            Sources = inputs.Sources,
            Data = inputs.Data,
            SkipFramePasses = sixteen || step != 1,
        };

        var units = Units(graph, order);

        // Wie oft jeder Ausgang noch gelesen wird. Faellt die Zahl auf null, wird er
        // losgelassen - so haelt der Speicher die breiteste Stelle des Graphen, nicht
        // seine Laenge.
        var inUnit = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int u = 0; u < units.Count; u++)
            foreach (var node in units[u])
                inUnit[node.Id] = u;

        var readers = new Dictionary<(string, string), int>();
        foreach (var link in graph.Links)
        {
            if (!inUnit.ContainsKey(link.To) || !inUnit.ContainsKey(link.From)) continue;

            // Innerhalb einer zusammengefassten Kette liest niemand von aussen.
            if (inUnit[link.To] == inUnit[link.From]) continue;

            readers[(link.From, link.Output)] = readers.GetValueOrDefault((link.From, link.Output)) + 1;
        }

        var results = new Dictionary<(string, string), object?>();
        GridImage? output = null;

        foreach (var unit in units)
        {
            var first = unit[0];
            var last = unit[^1];

            var inputsOf = new Dictionary<string, object?>(StringComparer.Ordinal);
            var consumed = new List<(string, string)>();

            foreach (var socket in first.Inputs)
            {
                var link = graph.Into(first.Id, socket.Name);
                if (link is null) continue;

                inputsOf[socket.Name] = results.GetValueOrDefault((link.From, link.Output));
                consumed.Add((link.From, link.Output));
            }

            var run = new NodeRun(context, inputsOf);

            if (unit.Count > 1)
            {
                if (first is LocalNode) LocalNode.RunChain(unit.Cast<LocalNode>().ToList(), run);
                else GeometryNode.RunChain(unit.Cast<GeometryNode>().ToList(), run);
            }
            else if (first.Muted)
            {
                // Stumm: das erste Bild unveraendert durch, alle anderen Ausgaenge leer.
                string? through = first.Through;
                var passed = through is null ? null : run.Image(through);

                foreach (var socket in first.Outputs)
                    run.Set(socket.Name, socket.Type == SocketType.Image ? passed : null);
            }
            else
            {
                first.Run(run);
            }

            if (last is OutputNode)
            {
                output = run.Outputs.GetValueOrDefault("Bild") as GridImage;
            }
            else
            {
                foreach (var socket in last.Outputs)
                {
                    if (readers.GetValueOrDefault((last.Id, socket.Name)) > 0)
                        results[(last.Id, socket.Name)] = run.Outputs.GetValueOrDefault(socket.Name);
                }
            }

            foreach (var key in consumed)
            {
                int left = readers.GetValueOrDefault(key) - 1;
                readers[key] = left;

                if (left <= 0) results.Remove(key);
            }
        }

        return (output, context);
    }

    /// <summary>
    /// Fasst zusammen, was der Stapel zusammen rechnet: Ketten oertlicher Werkzeuge und
    /// Ketten von Geometrie. Ein Glied gehoert zur Kette, wenn sein Bild vom vorigen
    /// Glied kommt und niemand sonst dieses Bild liest.
    /// </summary>
    private static List<List<Node>> Units(NodeGraph graph, IReadOnlyList<Node> order)
    {
        var units = new List<List<Node>>();
        var unitOf = new Dictionary<string, List<Node>>(StringComparer.Ordinal);

        foreach (var node in order)
        {
            if (node is LocalNode or GeometryNode &&
                graph.Into(node.Id, "Bild") is { } link &&
                unitOf.TryGetValue(link.From, out var unit) &&
                ReferenceEquals(unit[^1], graph.Find(link.From)) &&
                unit[^1].GetType() == node.GetType() &&
                graph.Links.Count(l => l.From == link.From && l.Output == link.Output) == 1)
            {
                unit.Add(node);
                unitOf[node.Id] = unit;
                continue;
            }

            var fresh = new List<Node> { node };
            units.Add(fresh);
            unitOf[node.Id] = fresh;
        }

        return units;
    }
}
