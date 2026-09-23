using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Die Knoten, die Werte und Masken verrechnen - Masken verrechnen, Wertebereich,
/// Farbverlauf, Maske formen.
///
/// Sie haben keinen Stapel, an dem man sie messen koennte; gemessen wird an ihrer
/// Formel, Punkt fuer Punkt, auf kleinen Feldern mit bekannten Werten. Wo es doch einen
/// Massstab gibt - der Wertebereich rechnet in der Grundstellung wie die Passmaske -,
/// steht die Probe dafuer bei den anderen Gleichstandsproben.
/// </summary>
public static class NodeValueInvariants
{
    public static void Run()
    {
        MaskMath();
        MapRange();
        ColorRamp();
        MaskShape();
        InTheGraph();
    }

    // ------------------------------------------------------------ Masken verrechnen

    private static void MaskMath()
    {
        Check.Group("Masken verrechnen: jede Verrechnung wie ihre Formel");

        var context = Context(4, 1);
        var a = Mask(0f, 0.25f, 0.5f, 1f);
        var b = Mask(1f, 0.5f, 0.5f, 0f);

        foreach (var operation in Enum.GetValues<MaskOperation>())
        {
            var node = new MaskMathNode { Operation = operation, Clamp = false };
            var result = (GridValue)Run(node, context, new() { ["A"] = a, ["B"] = b }, "Maske")!;

            bool right = true;

            for (int i = 0; i < 4; i++)
                right &= result.V[i] == MaskMathNode.Apply(operation, a.V[i], b.V[i]);

            Check.That(right, $"{operation}", string.Join(" ", result.V));
        }

        var clamped = (GridValue)Run(new MaskMathNode { Operation = MaskOperation.Subtract }, context,
                                     new() { ["A"] = a, ["B"] = b }, "Maske")!;

        Check.That(clamped.V.SequenceEqual(new[] { 0f, 0f, 0f, 1f }), "begrenzt faellt nichts unter null",
                   string.Join(" ", clamped.V));

        var inverted = (GridValue)Run(new MaskMathNode { Operation = MaskOperation.Maximum, Invert = true }, context,
                                      new() { ["A"] = a, ["B"] = b }, "Maske")!;

        Check.That(inverted.V.SequenceEqual(new[] { 0f, 0.5f, 0.5f, 0f }), "umgekehrt ist es eins weniger",
                   string.Join(" ", inverted.V));

        var halved = (GridValue)Run(new MaskMathNode { Operation = MaskOperation.Multiply, ValueB = 0.5f }, context,
                                    new() { ["A"] = a }, "Maske")!;

        Check.That(halved.V.SequenceEqual(new[] { 0f, 0.125f, 0.25f, 0.5f }), "ohne Kabel an B zaehlt der feste Wert",
                   string.Join(" ", halved.V));
    }

    // ------------------------------------------------------------ Wertebereich

    private static void MapRange()
    {
        Check.Group("Wertebereich: abbilden wie Schwarz- und Weisspunkt, auf Wunsch weich");

        var values = new[] { -1f, 0f, 0.25f, 0.4f, 0.5f, 0.75f, 1f, 2f };
        var context = Context(values.Length, 1);
        var input = Mask(values);

        var node = new MapRangeNode { Auto = false, FromLow = 0.25f, FromHigh = 0.75f };
        var mapped = (GridValue)Run(node, context, new() { ["Wert"] = input }, "Wert")!;

        Check.That(mapped.V.Select((v, i) => v == Masking.Levels(values[i], 0.25f, 0.75f)).All(x => x),
                   "begrenzt auf 0 bis 1 ist es genau Schwarz- und Weisspunkt", string.Join(" ", mapped.V));

        var random = new Random(3);
        bool same = true;

        for (int k = 0; k < 2000; k++)
        {
            float v = (float)(random.NextDouble() * 3 - 1), low = (float)random.NextDouble(), high = (float)random.NextDouble();
            same &= MapRangeNode.Map(v, low, high, 0f, 1f, clamp: true, smooth: false) == Masking.Levels(v, low, high);
        }

        Check.That(same, "auch bei zufaelligen Grenzen - und bei Von gleich oder ueber Bis");

        node.Smooth = true;
        var smooth = (GridValue)Run(node, context, new() { ["Wert"] = input }, "Wert")!;

        Check.Near(smooth.V[3], 0.3f * 0.3f * (3 - 2 * 0.3f), 1e-6, "weich laeuft es ein");
        Check.Near(smooth.V[4], 0.5, 1e-6, "und ist in der Mitte bei der Haelfte");

        var reversed = (GridValue)Run(new MapRangeNode { Auto = false, ToLow = 1f, ToHigh = 0f }, context,
                                      new() { ["Wert"] = input }, "Wert")!;

        Check.That(reversed.V[2] == 0.75f && reversed.V[0] == 1f && reversed.V[7] == 0f,
                   "ein Ergebnis von 1 nach 0 kehrt um", string.Join(" ", reversed.V));

        // Eine Tiefe in Metern: auf ihre eigene Spanne bezogen.
        var depth = Mask(2f, 4.5f, 7f, 12f, 1e10f);
        var auto = (GridValue)Run(new MapRangeNode(), Context(5, 1), new() { ["Wert"] = depth }, "Wert")!;

        Check.That(auto.V[0] == 0f && Math.Abs(auto.V[1] - 0.25f) < 1e-6 && Math.Abs(auto.V[2] - 0.5f) < 1e-6 &&
                   auto.V[3] == 1f && auto.V[4] == 1f,
                   "eine Tiefe wird auf ihre Spanne bezogen - das Unendliche des Hintergrunds zaehlt nicht mit",
                   string.Join(" ", auto.V));
    }

    // ------------------------------------------------------------ Farbverlauf

    private static void ColorRamp()
    {
        Check.Group("Farbverlauf: Stopps, Uebergaenge und die Seite der Anzeige");

        var context = Context(3, 1);
        var factor = Mask(0f, 0.5f, 1f);

        var ramp = new ColorRampNode();
        var display = (GridImage)Run(ramp, context, new() { ["Faktor"] = factor }, "Bild", display: true)!;

        Check.That(display.Rgb[0] == 0f && display.Rgb[3] == 0.5f && display.Rgb[6] == 1f && display.A.All(x => x == 1f) &&
                   !display.Matte && display.Contributed,
                   "hinter der Anzeige gehen die Farben so hinaus, wie sie da stehen", string.Join(" ", display.Rgb));

        var light = (GridImage)Run(ramp, context, new() { ["Faktor"] = factor }, "Bild", display: false)!;

        Check.Near(light.Rgb[3], ColorRampNode.ToLight(0.5f), 1e-6, "davor werden sie in Licht umgerechnet");
        Check.Near(light.Rgb[3], 0.214, 0.001, "ein mittleres Grau ist dann etwa ein Fuenftel Licht");

        ramp.Blend = RampBlend.Constant;
        var stepped = (GridImage)Run(ramp, context, new() { ["Faktor"] = factor }, "Bild", display: true)!;

        Check.That(stepped.Rgb[3] == 0f && stepped.Rgb[6] == 1f, "stufig gilt die Farbe des Stopps davor, bis zum naechsten");

        ramp.Blend = RampBlend.Linear;
        var inserted = ramp.Insert(0.25f);

        Check.That(ramp.Stops.Count == 3 && Math.Abs(inserted.Colour.R - 0.25f) < 1e-6,
                   "ein neuer Stopp bekommt die Farbe, die dort schon steht");

        inserted.Colour = new ColourTriplet(1f, 0f, 0f);
        var coloured = (GridImage)Run(ramp, Context(1, 1), new() { ["Faktor"] = Mask(0.125f) }, "Bild", display: true)!;

        Check.Near(coloured.Rgb[0], 0.5, 1e-6, "zwischen Schwarz und Rot steht bei halbem Weg halbes Rot");
        Check.Near(coloured.Rgb[1], 0.0, 1e-6, "und kein Gruen");

        var unwired = (GridImage)Run(new ColorRampNode { Factor = 1f }, Context(1, 1), new(), "Bild", display: true)!;

        Check.That(unwired.Rgb[0] == 1f, "ohne Kabel gilt der feste Faktor");
    }

    // ------------------------------------------------------------ Maske formen

    private static void MaskShape()
    {
        Check.Group("Maske formen: ausdehnen, schrumpfen, weichzeichnen");

        const int Size = 11;
        var context = Context(Size, Size);

        var dot = new float[Size * Size];
        dot[5 * Size + 5] = 1f;

        var grown = (GridValue)Run(new MaskShapeNode { Grow = 2 }, context, new() { ["Maske"] = new GridValue { V = dot } }, "Maske")!;

        bool square = true;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                square &= grown.V[y * Size + x] == (Math.Abs(x - 5) <= 2 && Math.Abs(y - 5) <= 2 ? 1f : 0f);

        Check.That(square, "ausgedehnt um 2 wird ein Punkt ein Quadrat von 5 mal 5");

        var shrunk = (GridValue)Run(new MaskShapeNode { Grow = -1 }, context, new() { ["Maske"] = grown }, "Maske")!;

        Check.That(shrunk.V.Count(v => v == 1f) == 9 && shrunk.V[5 * Size + 5] == 1f,
                   "geschrumpft um 1 bleibt ein Quadrat von 3 mal 3", $"{shrunk.V.Count(v => v == 1f)}");

        var edge = new float[Size * Size];
        edge[0] = 1f;
        var atEdge = (GridValue)Run(new MaskShapeNode { Grow = 3 }, context, new() { ["Maske"] = new GridValue { V = edge } }, "Maske")!;

        Check.That(atEdge.V.Count(v => v == 1f) == 16, "am Rand ist das Fenster einfach kuerzer", $"{atEdge.V.Count(v => v == 1f)}");

        // Radius 1: Bei ihm verliert auch der Rand, der sich selbst wiederholt, keine Menge.
        var soft = (GridValue)Run(new MaskShapeNode { Soften = 1 }, context, new() { ["Maske"] = grown }, "Maske")!;

        Check.That(soft.V[5 * Size + 5] < 1f && soft.V[5 * Size + 5] > 0.5f && soft.V[5 * Size + 9] > 0f,
                   "weichgezeichnet wird die Mitte etwas dunkler und der Rand weich");
        Check.Near(soft.V.Sum(), grown.V.Sum(), 0.01, "und die Menge bleibt, solange nichts ueber den Rand laeuft");

        // Im groben Durchgang schrumpft der Radius mit: 4 Bildpunkte sind bei Schritt 2 zwei Gitterpunkte.
        var coarse = Context(21, 21, step: 2);
        var coarseDot = new float[coarse.Count];
        coarseDot[5 * coarse.GridWidth + 5] = 1f;

        var coarseGrown = (GridValue)Run(new MaskShapeNode { Grow = 4 }, coarse, new() { ["Maske"] = new GridValue { V = coarseDot } }, "Maske")!;

        Check.That(coarseGrown.V.Count(v => v == 1f) == 25, "grob gerechnet waechst die Maske um dieselbe Strecke im Bild",
                   $"{coarseGrown.V.Count(v => v == 1f)}");

        var untouched = new GridValue { V = dot };
        Check.That(ReferenceEquals(Run(new MaskShapeNode(), context, new() { ["Maske"] = untouched }, "Maske"), untouched),
                   "ohne Einstellung geht die Maske unberuehrt durch");
    }

    // ------------------------------------------------------------ Im Graphen

    private static void InTheGraph()
    {
        Check.Group("Die neuen Knoten im Graphen: stumm, gespeichert, verbunden, auf ihrer Seite");

        var graph = new NodeGraph();
        var render = graph.Add(new RenderNode());
        var black = graph.Add(new BlackNode());
        var place = graph.Add(new PlaceNode());
        var mask = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 0, Width = 0.8f } });
        var shape = graph.Add(new MaskShapeNode { Soften = 6, Muted = true });
        var mix = graph.Add(new MixNode());
        var output = graph.Add(new OutputNode());

        graph.Connect(render, RenderNode.Picture, place, "Bild");
        graph.Connect(black, "Bild", mix, "Unten");
        graph.Connect(place, "Bild", mix, "Oben");
        graph.Connect(mask, "Maske", shape, "Maske");
        graph.Connect(shape, "Maske", mix, "Faktor");
        graph.Connect(mix, "Bild", output, "Bild");

        var sources = new Dictionary<string, FloatFrame> { [""] = Frame(24, 16) };
        var muted = Render(graph, sources);

        graph.Connect(mask, "Maske", mix, "Faktor");
        var direct = Render(graph, sources);

        Check.That(muted.AsSpan().SequenceEqual(direct) && muted.Any(b => b != 0),
                   "stumm reicht Maske formen die Maske unveraendert durch");

        Check.That(NodeEdits.CannotConnect(graph, render, RenderNode.Depth, new MapRangeNode(), "Wert") is null,
                   "die Tiefe der Datei laesst sich an einen Wertebereich stecken");
        Check.That(NodeEdits.CannotConnect(graph, mask, "Maske", place, "Bild") is not null,
                   "eine Maske wird trotzdem nicht zum Bild");

        // Gespeichert und gelesen: dieselben Einstellungen.
        var all = new NodeGraph();
        all.Add(new OutputNode());
        all.Add(new MaskMathNode { Operation = MaskOperation.Difference, ValueA = 0.3f, Invert = true });
        all.Add(new MapRangeNode { Auto = false, FromLow = 5, FromHigh = 20, Smooth = true });
        var saved = all.Add(new ColorRampNode { Blend = RampBlend.Smooth });
        saved.Insert(0.4f).Colour = new ColourTriplet(0.9f, 0.2f, 0.1f);
        all.Add(new MaskShapeNode { Grow = -3, Soften = 2.5f });

        var loaded = NodeGraph.Load(all.Save());

        Check.That(loaded is not null && loaded.Nodes.Count == all.Nodes.Count &&
                   loaded.Nodes.Zip(all.Nodes).All(p => NodeGraph.Print(p.First, 0) == NodeGraph.Print(p.Second, 0)),
                   "gespeichert und gelesen haben die neuen Knoten dieselben Einstellungen");

        // Die Seite der Anzeige: ein Verlauf, der hinter die Anzeige gemischt wird, rechnet dort.
        var sided = new NodeGraph();
        var file = sided.Add(new RenderNode());
        var view = sided.Add(new ViewNode());
        var ramp = sided.Add(new ColorRampNode());
        var tint = sided.Add(new MixNode { Mode = BlendMode.Multiply });
        var end = sided.Add(new OutputNode());

        sided.Connect(file, RenderNode.Picture, view, "Bild");
        sided.Connect(view, "Bild", tint, "Unten");
        sided.Connect(ramp, "Bild", tint, "Oben");
        sided.Connect(tint, "Bild", end, "Bild");

        var side = GraphEvaluator.DisplaySide(sided, sided.Order()!);

        Check.That(side.Contains(ramp.Id) && side.Contains(tint.Id), "ein Verlauf, der hinter der Anzeige gelesen wird, steht dort");

        sided.Connect(file, RenderNode.Picture, tint, "Unten");
        side = GraphEvaluator.DisplaySide(sided, sided.Order()!);

        Check.That(!side.Contains(ramp.Id), "vor der Anzeige gelesen, steht er in Licht");
    }

    // ------------------------------------------------------------ Hilfsmittel

    private static NodeContext Context(int width, int height, int step = 1) => new()
    {
        Width = width,
        Height = height,
        Step = step,
        Columns = LocalPass.Grid(width, step),
        Rows = LocalPass.Grid(height, step),
        Number = 0,
        View = new StandardViewTransform(),
        Sources = new Dictionary<string, FloatFrame>(),
        Data = new Dictionary<PassNeed, FloatFrame?>(),
        SkipFramePasses = false,
    };

    private static GridValue Mask(params float[] values) => new() { V = values };

    private static object? Run(Node node, NodeContext context, Dictionary<string, object?> inputs, string output,
                               bool display = false)
    {
        var run = new NodeRun(context, inputs) { Display = display };
        node.Run(run);

        return run.Outputs.GetValueOrDefault(output);
    }

    private static FloatFrame Frame(int width, int height) => new()
    {
        Width = width,
        Height = height,
        R = Enumerable.Range(0, width * height).Select(i => 0.2f + 0.6f * (i % width) / width).ToArray(),
        G = Enumerable.Range(0, width * height).Select(i => 0.5f).ToArray(),
        B = Enumerable.Range(0, width * height).Select(i => 0.1f + 0.8f * (i / width) / (float)height).ToArray(),
        IsSceneReferred = true,
    };

    private static byte[] Render(NodeGraph graph, Dictionary<string, FloatFrame> sources)
    {
        var frame = sources[""];
        var pixels = new byte[frame.Width * frame.Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            GraphEvaluator.Render(graph, new GraphInputs { Sources = sources, View = new StandardViewTransform() },
                                  buffer, frame.Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }
}
