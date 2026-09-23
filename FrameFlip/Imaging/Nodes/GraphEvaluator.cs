using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Woher die Felder der Zwischenbilder kommen - und wohin sie zurueckgehen. Ohne
    /// Vorrat legt jede Rechnung ihre eigenen an; das ist richtig, nur langsamer.
    /// </summary>
    public GridPool? Pool { get; init; }

    /// <summary>Was von der letzten Rechnung gemerkt ist - ohne ihn wird alles gerechnet.</summary>
    public GraphCache? Cache { get; init; }

    /// <summary>
    /// Der Knoten, an dem gerade gearbeitet wird. Was in ihn hineinfliesst, merkt sich
    /// der Zwischenspeicher fuer die naechste Rechnung.
    /// </summary>
    public string? Focus { get; init; }

    /// <summary>Wohin die Vorschauen der Knoten gehen - und von welchen eine gewuenscht ist.</summary>
    public NodePreviews? Previews { get; init; }
}

/// <summary>
/// Rechnet einen Graphen - in dieselben Puffer, in die der Prozessor den Stapel rechnet.
///
/// Jeder Knoten rechnet in ein eigenes Feld, und ein Feld wird losgelassen, sobald der
/// letzte Knoten gelesen hat, der es braucht - mit Vorrat geht es dann zurueck in den
/// Vorrat. Zusammengefasst wird, was der Stapel zusammen rechnet (aufeinanderfolgende
/// oertliche Werkzeuge, aufeinanderfolgende Geometrie - getrennt kaeme ein anderes Bild
/// heraus), und Ketten von Punktknoten, die in einem Durchgang nur schneller werden.
/// Mit Zwischenspeicher wird nur gerechnet, was hinter dem gewaehlten Knoten liegt.
/// Siehe docs/Atelier-Nodes.md, Abschnitt 3.
/// </summary>
public static class GraphEvaluator
{
    /// <summary>
    /// Fuer die Probe: welche Einheit gerechnet wurde - ihr erster Knoten. Nur so laesst
    /// sich pruefen, dass der Zwischenspeicher wirklich etwas auslaesst, und nicht nur,
    /// dass das Bild stimmt.
    /// </summary>
    internal static Action<Node>? Ran;

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

        var order = Live(graph);
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
        var order = Live(graph);
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

    /// <summary>
    /// Die Knoten, die wirklich gerechnet werden - in Rechenreihenfolge. Das ist die
    /// Reihenfolge des Graphen ohne das, was nur in einen stummen Knoten fliesst, und zwar
    /// nicht in den Eingang, den er durchreicht: Eine ausgeblendete Ebene haengt an einem
    /// stummen Mischen, und ihr Bild wird weder gelesen noch gerechnet.
    /// </summary>
    internal static IReadOnlyList<Node> Live(NodeGraph graph)
    {
        var order = graph.Order();
        if (order is null || graph.Output is not { } output) return Array.Empty<Node>();

        var live = new HashSet<string>(StringComparer.Ordinal) { output.Id };
        var open = new Stack<Node>();
        open.Push(output);

        while (open.Count > 0)
        {
            var node = open.Pop();

            foreach (var link in graph.Links.Where(l => l.To == node.Id))
            {
                if (node.Muted && link.Input != node.Through) continue;

                if (graph.Find(link.From) is { } from && live.Add(from.Id)) open.Push(from);
            }
        }

        return order.Where(n => live.Contains(n.Id)).ToList();
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
    public static bool Render(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
        => Guarded(inputs, () => RenderLocked(graph, inputs, destination, stride));

    /// <summary>Ein Vorrat und ein Zwischenspeicher dienen einer Rechnung zur Zeit.</summary>
    private static bool Guarded(GraphInputs inputs, Func<bool> work)
    {
        object? pool = inputs.Pool, cache = inputs.Cache;
        bool poolTaken = false, cacheTaken = false;

        try
        {
            if (pool is not null) Monitor.Enter(pool, ref poolTaken);
            if (cache is not null) Monitor.Enter(cache, ref cacheTaken);

            return work();
        }
        finally
        {
            if (cacheTaken) Monitor.Exit(cache!);
            if (poolTaken) Monitor.Exit(pool!);
        }
    }

    private static unsafe bool RenderLocked(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
    {
        var (image, context) = Evaluate(graph, inputs, sixteen: false);
        if (image is null || context is null) return false;

        try
        {
            Write(image, context, destination, stride);
        }
        finally
        {
            context.Drop(image);
        }

        return true;
    }

    private static unsafe void Write(GridImage image, NodeContext context, IntPtr destination, int stride)
    {
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

            return;
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
    }

    /// <summary>
    /// Rechnet den Graphen nach Rgba64 - sechzehn Bit, fuer den Export. Immer im vollen
    /// Durchgang und ohne die Durchgaenge ueber das fertige Bild, wie im Stapel.
    /// </summary>
    public static bool Render16(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
        => Guarded(inputs, () => Render16Locked(graph, inputs, destination, stride));

    private static unsafe bool Render16Locked(NodeGraph graph, GraphInputs inputs, IntPtr destination, int stride)
    {
        var (image, context) = Evaluate(graph, inputs, sixteen: true);
        if (image is null || context is null) return false;

        try
        {
            Write16(image, context, destination, stride);
        }
        finally
        {
            context.Drop(image);
        }

        return true;
    }

    private static unsafe void Write16(GridImage image, NodeContext context, IntPtr destination, int stride)
    {
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
    }

    /// <summary>
    /// Rechnet den Graphen bis zur Ausgabe und gibt das Bild auf dem Gitter zurueck.
    ///
    /// Mit einem Vorrat haelt das Ergebnis seine Felder, bis der Aufrufer es mit
    /// <see cref="NodeContext.Drop"/> loslaesst. Mit einem Zwischenspeicher wird nur
    /// gerechnet, was dort nicht schon liegt - siehe <see cref="GraphCache"/>.
    /// </summary>
    internal static (GridImage? Image, NodeContext? Context) Evaluate(NodeGraph graph, GraphInputs inputs, bool sixteen)
    {
        if (graph.Order() is null) return (null, null);

        var order = Live(graph);
        if (order.Count == 0) return (null, null);

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
            Pool = inputs.Pool,
        };

        var cache = inputs.Cache;
        var previews = inputs.Previews;
        var units = Units(graph, order, cache is null ? null : inputs.Focus, previews?.Wanted);

        var unitOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int u = 0; u < units.Count; u++)
            foreach (var node in units[u])
                unitOf[node.Id] = u;

        var display = DisplaySide(graph, order);

        // Die Schluessel - nur mit Zwischenspeicher. Ohne ihn waeren sie Arbeit fuer nichts.
        var shapes = cache is null ? null : Shapes(graph, units, unitOf, inputs, context, sixteen, display);

        string Shape(int unit, string output) => shapes![unit] + "|" + output;
        string Key(int unit, string output) => step + "|" + Shape(unit, output);

        // Was gerechnet werden muss: von der Ausgabe rueckwaerts, bis ein Ergebnis im
        // Zwischenspeicher liegt. Was davor liegt, wird gar nicht erst angefasst.
        var needed = new bool[units.Count];
        var remembered = new Dictionary<(string, string), object?>();

        void Need(int unit)
        {
            if (needed[unit]) return;
            needed[unit] = true;

            var first = units[unit][0];

            foreach (var socket in first.Inputs)
            {
                if (graph.Into(first.Id, socket.Name) is not { } link || !unitOf.TryGetValue(link.From, out int from))
                    continue;

                if (cache is not null && cache.TryGet(Key(from, link.Output), out var value))
                {
                    remembered[(link.From, link.Output)] = value;
                    continue;
                }

                Need(from);
            }
        }

        Need(units.Count - 1);

        // Wie oft jeder Ausgang noch gelesen wird. Faellt die Zahl auf null, wird er
        // losgelassen - so haelt der Speicher die breiteste Stelle des Graphen, nicht
        // seine Laenge.
        var readers = new Dictionary<(string, string), int>();
        foreach (var link in graph.Links)
        {
            if (!unitOf.TryGetValue(link.To, out int to) || !unitOf.TryGetValue(link.From, out int from)) continue;

            // Innerhalb einer zusammengefassten Kette liest niemand von aussen; was aus
            // dem Zwischenspeicher kommt, wird nicht gerechnet und nicht losgelassen.
            if (to == from || !needed[to] || remembered.ContainsKey((link.From, link.Output))) continue;

            readers[(link.From, link.Output)] = readers.GetValueOrDefault((link.From, link.Output)) + 1;
        }

        var keep = cache is null ? null : Frontier(graph, units, unitOf, inputs.Focus, Shape);
        var next = cache is null ? null : new Dictionary<string, GraphCache.Entry>(StringComparer.Ordinal);

        var results = new Dictionary<(string, string), object?>();
        GridImage? output = null;

        for (int u = 0; u < units.Count; u++)
        {
            if (!needed[u]) continue;

            var unit = units[u];
            var first = unit[0];
            var last = unit[^1];

            var inputsOf = new Dictionary<string, object?>(StringComparer.Ordinal);
            var consumed = new List<(string, string)>();

            foreach (var socket in first.Inputs)
            {
                var link = graph.Into(first.Id, socket.Name);
                if (link is null) continue;

                if (remembered.TryGetValue((link.From, link.Output), out var known))
                {
                    inputsOf[socket.Name] = known;
                    continue;
                }

                inputsOf[socket.Name] = results.GetValueOrDefault((link.From, link.Output));
                consumed.Add((link.From, link.Output));
            }

            var run = new NodeRun(context, inputsOf) { Display = display.Contains(first.Id) };

            if (unit.Count > 1)
            {
                switch (first)
                {
                    case LocalNode: LocalNode.RunChain(unit.Cast<LocalNode>().ToList(), run); break;
                    case GeometryNode: GeometryNode.RunChain(unit.Cast<GeometryNode>().ToList(), run); break;
                    default: PointNode.RunChain(unit.Cast<PointNode>().ToList(), run); break;
                }
            }
            else if (first.Muted)
            {
                // Stumm: der durchgereichte Eingang unveraendert an jeden Ausgang seiner
                // Art - ein Bild an die Bildausgaenge, eine Maske an die Maskenausgaenge -,
                // alle anderen Ausgaenge leer.
                var through = first.Through is { } name ? first.Input(name) : null;

                object? passed = through?.Type switch
                {
                    SocketType.Image => run.Image(through.Value.Name),
                    SocketType.Value => run.Value(through.Value.Name),
                    _ => null,
                };

                foreach (var socket in first.Outputs)
                    run.Set(socket.Name, through is { } t && socket.Type == t.Type ? passed : null);
            }
            else
            {
                first.Run(run);
            }

            Ran?.Invoke(first);

            // Die Vorschau liest ab, was ohnehin dasteht - den ersten Ausgang.
            if (previews is not null && previews.Wanted.Contains(last.Id) && last.Outputs.Count > 0)
                previews.Capture(last.Id, run.Outputs.GetValueOrDefault(last.Outputs[0].Name), context, display.Contains(last.Id));

            if (last is OutputNode)
            {
                output = run.Outputs.GetValueOrDefault("Bild") as GridImage;
                context.Hold(output);
            }
            else
            {
                foreach (var socket in last.Outputs)
                {
                    var value = run.Outputs.GetValueOrDefault(socket.Name);

                    if (keep is not null && keep.Contains(Shape(u, socket.Name)))
                    {
                        // Gemerkt fuer die naechste Rechnung - und gehalten, solange es
                        // gemerkt ist: Seine Felder gehen nicht in den Vorrat zurueck.
                        next![Key(u, socket.Name)] = new GraphCache.Entry(value, Shape(u, socket.Name));
                        context.Hold(value);
                    }

                    if (readers.GetValueOrDefault((last.Id, socket.Name)) <= 0) continue;

                    results[(last.Id, socket.Name)] = value;
                    context.Hold(value);
                }
            }

            // Erst halten, was herauskam, dann loslassen, was gelesen wurde: Ein Ergebnis,
            // das die Deckung seines Eingangs weitergibt, haelt sie damit fest.
            foreach (var key in consumed)
            {
                int left = readers.GetValueOrDefault(key) - 1;
                readers[key] = left;

                if (left <= 0 && results.Remove(key, out var gone)) context.Drop(gone);
            }

            context.EndUnit();
        }

        if (cache is not null)
        {
            // Was schon gemerkt war und weiter in den gewaehlten Knoten fliesst, bleibt -
            // auch fuer das andere Raster, damit das Loslassen nach dem Ziehen nicht
            // wieder alles rechnet.
            foreach (var (key, entry) in cache.Entries)
                if (keep!.Contains(entry.Shape) && !next!.ContainsKey(key)) next[key] = entry;

            cache.Replace(next!);
        }

        return (output, context);
    }

    /// <summary>
    /// Was von ausserhalb in den gewaehlten Knoten und in alles hinter ihm fliesst - das,
    /// was beim naechsten Zug an diesem Knoten gleich bleibt. Ohne gewaehlten Knoten nichts.
    /// </summary>
    private static HashSet<string> Frontier(NodeGraph graph, List<List<Node>> units, Dictionary<string, int> unitOf,
                                            string? focus, Func<int, string, string> shape)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (focus is null || !unitOf.TryGetValue(focus, out int start)) return keep;

        // Die Einheiten stehen in Rechenreihenfolge - ein Gang nach vorn findet alles dahinter.
        var behind = new bool[units.Count];
        behind[start] = true;

        for (int u = start + 1; u < units.Count; u++)
        {
            var first = units[u][0];

            foreach (var socket in first.Inputs)
            {
                if (graph.Into(first.Id, socket.Name) is { } link &&
                    unitOf.TryGetValue(link.From, out int from) && behind[from])
                {
                    behind[u] = true;
                }
            }
        }

        for (int u = start; u < units.Count; u++)
        {
            if (!behind[u]) continue;

            var first = units[u][0];

            foreach (var socket in first.Inputs)
            {
                if (graph.Into(first.Id, socket.Name) is { } link &&
                    unitOf.TryGetValue(link.From, out int from) && !behind[from])
                {
                    keep.Add(shape(from, link.Output));
                }
            }
        }

        return keep;
    }

    /// <summary>
    /// Der Schluessel jeder Einheit, ohne das Raster: ihre Knoten, wie sie gespeichert
    /// wuerden, und die Schluessel dessen, was in sie hineinfliesst. Dazu fuer alle
    /// gemeinsam, was von aussen kommt - die Leinwand, die Bildnummer, die
    /// Sichtumwandlung und jedes gelesene Bild. Ein neu gelesenes Bild ist ein anderes
    /// Objekt und bekommt eine andere Nummer, auch unter demselben Namen.
    /// </summary>
    private static string[] Shapes(NodeGraph graph, List<List<Node>> units, Dictionary<string, int> unitOf,
                                   GraphInputs inputs, NodeContext context, bool sixteen, HashSet<string> display)
    {
        var world = new StringBuilder();

        world.Append(context.Width).Append('x').Append(context.Height)
             .Append('|').Append(context.Number)
             .Append('|').Append(sixteen)
             .Append('|').Append(inputs.View is StandardViewTransform ? "Standard" : inputs.View.Name + "#" + Identity(inputs.View));

        foreach (var (name, frame) in inputs.Sources.OrderBy(p => p.Key, StringComparer.Ordinal))
            world.Append("|s:").Append(name).Append('=').Append(Identity(frame));

        foreach (var (need, frame) in inputs.Data.OrderBy(p => p.Key))
            world.Append("|d:").Append(need).Append('=').Append(frame is null ? "-" : Identity(frame).ToString());

        string common = world.ToString();
        var shapes = new string[units.Count];

        for (int u = 0; u < units.Count; u++)
        {
            var text = new StringBuilder(common);

            foreach (var node in units[u]) text.Append('\n').Append(NodeGraph.Print(node, context.Number));

            var first = units[u][0];

            // Ein Farbverlauf rechnet vor der Anzeige anders als dahinter - und auf welcher
            // Seite er steht, haengt an dem, der ihn liest, nicht an dem, was in ihn fliesst.
            if (display.Contains(first.Id)) text.Append("\nAnzeige");

            foreach (var socket in first.Inputs)
            {
                if (graph.Into(first.Id, socket.Name) is { } link && unitOf.TryGetValue(link.From, out int from))
                    text.Append('\n').Append(socket.Name).Append('<').Append(shapes[from]).Append('.').Append(link.Output);
            }

            shapes[u] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        return shapes;
    }

    /// <summary>
    /// Welche Knoten ihr Bild auf der Anzeigeseite abgeben - hinter der Sichtumwandlung.
    ///
    /// Vorwaerts: Wer sein Bild von der Anzeigeseite bekommt, gibt es dort ab. Rueckwaerts
    /// fuer das, was keinen Bildweg hat - ein Farbverlauf macht sein Bild aus einer
    /// Maske -: Er steht auf der Seite dessen, der sein Bild liest. Ein Verlauf, der in
    /// ein Mischen hinter der Anzeige geht, rechnet in Anzeigewerten.
    /// </summary>
    internal static HashSet<string> DisplaySide(NodeGraph graph, IReadOnlyList<Node> order)
    {
        var display = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in order)
        {
            if (node is ViewNode)
            {
                display.Add(node.Id);
                continue;
            }

            var (input, _) = NodeEdits.Through(node);

            if (input is not null && graph.Into(node.Id, input) is { } link && display.Contains(link.From))
                display.Add(node.Id);
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];

            if (node is not ColorRampNode || display.Contains(node.Id)) continue;

            if (graph.Links.Any(l => l.From == node.Id && display.Contains(l.To))) display.Add(node.Id);
        }

        return display;
    }

    private static readonly ConditionalWeakTable<object, object> Identities = new();
    private static long _identities;

    /// <summary>Eine Nummer, die ein Objekt behaelt, solange es lebt.</summary>
    private static long Identity(object value)
        => (long)Identities.GetValue(value, _ => Interlocked.Increment(ref _identities));

    /// <summary>
    /// Fasst zusammen, was der Stapel zusammen rechnet: Ketten oertlicher Werkzeuge und
    /// Ketten von Geometrie - dort ergaebe getrennt gerechnet ein anderes Bild. Dazu
    /// Ketten von Punktknoten, bei denen es nur schneller wird. Ein Glied gehoert zur
    /// Kette, wenn sein Bild vom vorigen Glied kommt und niemand sonst dieses Bild liest.
    ///
    /// Eine Punktkette reisst am gewaehlten Knoten: Was vor ihm liegt, soll als eigenes
    /// Ergebnis im Zwischenspeicher liegen koennen. Und hinter einem Knoten mit Vorschau,
    /// dessen Bild sonst mitten in der Kette verschwaende. Eine oertliche oder
    /// geometrische Kette reisst nicht - sie getrennt zu rechnen gaebe ein anderes Bild.
    /// </summary>
    private static List<List<Node>> Units(NodeGraph graph, IReadOnlyList<Node> order, string? focus,
                                          IReadOnlySet<string>? previews)
    {
        var units = new List<List<Node>>();
        var unitOf = new Dictionary<string, List<Node>>(StringComparer.Ordinal);

        foreach (var node in order)
        {
            if (node is LocalNode or GeometryNode or PointNode &&
                !(node is PointNode && node.Id == focus) &&
                !(node is PointNode && previews is not null && unitOf.TryGetValue(graph.Into(node.Id, "Bild")?.From ?? "", out var before) &&
                  previews.Contains(before[^1].Id)) &&
                graph.Into(node.Id, "Bild") is { } link &&
                unitOf.TryGetValue(link.From, out var unit) &&
                ReferenceEquals(unit[^1], graph.Find(link.From)) &&
                Chains(unit[^1], node) &&
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

    /// <summary>Ob zwei Knoten in einer Kette zusammen gerechnet werden.</summary>
    private static bool Chains(Node previous, Node next) => (previous, next) switch
    {
        (LocalNode, LocalNode) => true,
        (GeometryNode, GeometryNode) => true,
        (PointNode, PointNode) => true,
        _ => false,
    };
}
