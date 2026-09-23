using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Der Stapel und sein Graph rechnen dasselbe Bild - Byte fuer Byte.
///
/// Das ist das Netz unter dem Knotenmodus. Der Weg dorthin geht nur in eine Richtung:
/// Wer umschaltet, bekommt den Graphen, und der Stapel ist weg. Rechnete der Graph auch
/// nur an einer Stelle anders, saehe man es nach dem Umschalten - und koennte nicht
/// mehr zurueck, um zu vergleichen.
///
/// Geprueft wird deshalb nicht eine Auswahl, sondern der Raum: viele zufaellige Stapel
/// aus allen Ebenenarten, allen Masken, Gruppen, Schnittmasken, platzierten Bildern und
/// jedem Werkzeug - im vollen Durchgang, auf dem groben Raster und in sechzehn Bit.
/// Dazu benannte Einzelfaelle, damit ein Fehler einen Namen hat und nicht nur eine
/// Zufallszahl.
/// </summary>
public static class NodeParityInvariants
{
    private const int Width = 48;
    private const int Height = 32;

    private static readonly IViewTransform View = new StandardViewTransform();

    public static void Run()
    {
        var world = new World();

        TheComparisonCanFail(world);
        NamedCases(world);
        RandomCases(world);
        TheGraphSurvivesSaving(world);
        WiredCases(world);
    }

    /// <summary>
    /// Die Gegenprobe zur ganzen Pruefung: Ein Vergleich, der nie anschlaegt, prueft
    /// nichts. Zwei Wege, die beide nichts liefern, waeren sich auch "gleich".
    /// </summary>
    private static void TheComparisonCanFail(World world)
    {
        Check.Group("Knoten: der Vergleich merkt eine Abweichung");

        var stack = Base(Image("bild.png"));
        var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());

        var expected = RenderStack(world, stack, ImageAdjustments.Neutral, new GradingStack(), 1, 0, sixteen: false);
        var same = RenderGraph(world, graph, 1, 0, sixteen: false);

        Check.That(expected.Any(b => b != 0) && expected.Distinct().Count() > 16,
                   "das Bild ist kein leeres Feld", $"{expected.Distinct().Count()} verschiedene Werte");

        Check.That(Diff(expected, same, 1).Differ == 0, "unveraendert umgewandelt stimmt es");

        foreach (var mix in graph.Nodes.OfType<MixNode>()) mix.Opacity *= 0.5f;

        var changed = RenderGraph(world, graph, 1, 0, sixteen: false);

        Check.That(Diff(expected, changed, 1).Differ > 100,
                   "und mit halber Deckkraft im Graphen stimmt es nicht mehr",
                   $"{Diff(expected, changed, 1).Differ} Bytes anders");
    }

    // ------------------------------------------------------------ benannte Faelle

    private static void NamedCases(World world)
    {
        Check.Group("Knoten: jede Art fuer sich rechnet wie der Stapel");

        var cases = new List<(string Name, LayerStack Stack, ImageAdjustments Adjust, GradingStack Picture)>
        {
            ("nur das Bild", new LayerStack(), ImageAdjustments.Neutral, new GradingStack()),
            ("das Bild mit Grundkorrektur", Base(), Adjust(0.7, 1.3, 0.02, 1.1, 1.2), new GradingStack()),
            ("zwei Passe auf Addieren", Base(Pass("P.a", BlendMode.Add)), ImageAdjustments.Neutral, new GradingStack()),
            ("ein Bild mit Freistellung", Base(Image("bild.png")), ImageAdjustments.Neutral, new GradingStack()),
            ("ein Bild ohne Alpha", Base(Image("ohne.png")), ImageAdjustments.Neutral, new GradingStack()),
            ("ein platziertes Logo", Base(Placed("logo.png", 0.5f, 0.1f, -0.2f, 15f)), ImageAdjustments.Neutral, new GradingStack()),
            ("an das Logo angeschnitten", Base(Placed("logo.png", 0.5f, 0f, 0f, 0f), Clip(Image("bild.png"))), ImageAdjustments.Neutral, new GradingStack()),
            ("eine Einstellungsebene", Base(Adjustment(Adjust(0.5, 0.6, 0, 1, 1.2))), ImageAdjustments.Neutral, new GradingStack()),
            ("eine Einstellung, angeschnitten", Base(Image("bild.png"), Clip(Adjustment(Adjust(1, 1, 0, 1, 1)))), ImageAdjustments.Neutral, new GradingStack()),
            ("eine Gruppe mit Deckkraft", Base(Group(0.6f, BlendMode.Screen, Pass("P.b", BlendMode.Normal), Image("bild.png"))), ImageAdjustments.Neutral, new GradingStack()),
            ("Belichtung und Toenung einer Ebene", Base(Tinted(Pass("P.a", BlendMode.Add))), ImageAdjustments.Neutral, new GradingStack()),
            ("Mischen im Anzeigeraum", Base(Display(Image("bild.png", BlendMode.Overlay))), ImageAdjustments.Neutral, new GradingStack()),
            ("Mattegrenze und Aufdecken", Base(Revealed(Image("bild.png"))), ImageAdjustments.Neutral, new GradingStack()),
            ("ein Wasserzeichen obenauf", Base(OnTop(Placed("logo.png", 0.4f, 0.2f, 0.2f, 0f))), ImageAdjustments.Neutral, new GradingStack()),
            ("Korrektur nur unter der Maske", Base(Scoped(Image("bild.png"))), ImageAdjustments.Neutral, new GradingStack()),
        };

        foreach (var kind in Enum.GetValues<MaskKind>().Where(k => k is not MaskKind.None and not MaskKind.Cryptomatte))
        {
            var layer = Image("bild.png");
            layer.Mask = world.Mask(kind, new Random((int)kind));
            cases.Add(($"Maske: {kind}", Base(layer), ImageAdjustments.Neutral, new GradingStack()));
        }

        foreach (var mode in Enum.GetValues<BlendMode>())
            cases.Add(($"Mischung: {mode}", Base(Image("bild.png", mode)), ImageAdjustments.Neutral, new GradingStack()));

        foreach (var (name, picture) in PictureTools())
            cases.Add(($"am Bild: {name}", Base(Image("bild.png")), Adjust(0.2, 1.1, 0, 1, 1.05), picture));

        foreach (var (name, stack, adjust, picture) in cases)
        {
            foreach (int step in new[] { 1, 3 })
            {
                var (differ, worst) = Compare8(world, stack, adjust, picture, step, number: 7);

                Check.That(differ == 0, $"{name}{(step > 1 ? " (grob)" : "")}",
                           $"{differ} Bytes anders, bis {worst} Stufen");
            }

            var (differ16, worst16) = Compare16(world, stack, adjust, picture, number: 7);

            Check.That(differ16 == 0, $"{name} (16 Bit)", $"{differ16} Werte anders, bis {worst16}");
        }
    }

    /// <summary>Jede Art Werkzeug am fertigen Bild, einzeln.</summary>
    private static IEnumerable<(string, GradingStack)> PictureTools()
    {
        yield return ("Weissabgleich", new GradingStack { Tools = { new WhiteBalanceTool { Kelvin = 4300, Tint = 10 } } });
        yield return ("Lift/Gamma/Gain", new GradingStack { Tools = { new LiftGammaGainTool { Lift = new ColourTriplet(0.02f, 0, -0.01f), Gain = new ColourTriplet(1.1f, 1, 0.9f) } } });
        yield return ("Dynamik", new GradingStack { Tools = { new VibranceTool { Amount = 0.5f } } });
        yield return ("Vignette", new GradingStack { Optics = { new VignetteTool { Amount = -0.5f } } });
        yield return ("Korn", new GradingStack { Optics = { new GrainTool { Amount = 0.4f } } });
        yield return ("Raster", new GradingStack { Optics = { new DitherTool { Amount = 0.8f, Levels = 3 } } });
        yield return ("Klarheit und Schaerfe", new GradingStack { Local = { new ClarityTool { Amount = 0.5f, Reach = 4 }, new SharpenTool { Amount = 0.6f, Reach = 4 } } });
        yield return ("Rauschen", new GradingStack { Local = { new NoiseTool { Luminance = 0.5f, Colour = 0.4f } } });
        yield return ("Glanz und Halation", new GradingStack { Local = { new BloomTool { Amount = 0.8f, Threshold = 0.8f, Reach = 6 }, new HalationTool { Amount = 0.6f, Threshold = 0.9f, Reach = 4 } } });
        yield return ("Dunst und Struktur", new GradingStack { Local = { new DehazeTool { Amount = 0.5f, Reach = 8 }, new TextureTool { Amount = 0.4f, Reach = 4 } } });
        yield return ("Verzeichnung und Farbsaum", new GradingStack { Geometry = { new DistortionTool { Amount = 0.4f }, new ChromaticTool { Amount = 0.6f } } });
        yield return ("Tiefenschaerfe", new GradingStack { Data = { new DepthFieldTool { Aperture = 0.8f, Focus = 3f } } });
        yield return ("Bewegungsunschaerfe", new GradingStack { Data = { new MotionBlurTool { Shutter = 1f } } });
        yield return ("Verschiebung mit Normalen", new GradingStack { Data = { new DisplaceTool { Amount = 5f, From = DisplaceFrom.Normal } } });
        yield return ("Verschiebung ohne Pass", new GradingStack { Data = { new DisplaceTool { Amount = 5f, From = DisplaceFrom.Screen, Wavelength = 12f } } });
        yield return ("Rasterdiffusion", new GradingStack { Frame = { new DiffusionTool { Amount = 1f, Levels = 3 } } });
        yield return ("Pixelsortierung", new GradingStack { Frame = { new SortTool { Low = 0.2f, High = 0.8f } } });
        yield return ("ausgeschaltet", new GradingStack { Local = { new ClarityTool { Amount = 0.8f, Reach = 4 } }, Bypassed = { ClarityTool.KindName } });
    }

    // ------------------------------------------------------------ Zufall

    private static void RandomCases(World world)
    {
        Check.Group("Knoten: zufaellige Stapel rechnen wie der Stapel");

        int cases = 0, failed = 0;
        string first = "";

        for (int seed = 1; seed <= 220; seed++)
        {
            var random = new Random(seed);

            var stack = world.RandomStack(random);
            var adjust = RandomAdjust(random);
            var picture = RandomPicture(random);
            int number = random.Next(0, 40);
            int step = seed % 4 == 0 ? 1 + random.Next(1, 4) : 1;

            var (differ, worst) = Compare8(world, stack, adjust, picture, step, number);
            cases++;

            if (differ > 0)
            {
                failed++;
                if (first.Length == 0) first = $"Fall {seed} (Schritt {step}): {differ} Bytes, bis {worst} Stufen";
            }

            if (seed % 5 == 0)
            {
                var (differ16, worst16) = Compare16(world, stack, adjust, picture, number);
                cases++;

                if (differ16 > 0)
                {
                    failed++;
                    if (first.Length == 0) first = $"Fall {seed} (16 Bit): {differ16} Werte, bis {worst16}";
                }
            }
        }

        Check.That(failed == 0, $"{cases} zufaellige Rechnungen, alle byte-gleich", first);
    }

    private static ImageAdjustments RandomAdjust(Random random) => random.Next(3) == 0
        ? ImageAdjustments.Neutral
        : new ImageAdjustments
        {
            Exposure = random.NextDouble() * 2 - 1,
            Saturation = 0.5 + random.NextDouble(),
            BlackPoint = random.NextDouble() * 0.05,
            WhitePoint = 0.9 + random.NextDouble() * 0.1,
            Gamma = 0.8 + random.NextDouble() * 0.4,
            Contrast = 0.8 + random.NextDouble() * 0.4,
        };

    private static GradingStack RandomPicture(Random random)
    {
        var stack = new GradingStack();
        var all = PictureTools().Select(p => p.Item2).ToList();

        int count = random.Next(0, 4);

        for (int i = 0; i < count; i++)
        {
            var pick = all[random.Next(all.Count)].Clone();

            stack.Tools.AddRange(pick.Tools);
            stack.Optics.AddRange(pick.Optics);
            stack.Local.AddRange(pick.Local);
            stack.Geometry.AddRange(pick.Geometry);
            stack.Data.AddRange(pick.Data);
            stack.Frame.AddRange(pick.Frame);
        }

        if (random.Next(4) == 0 && stack.Local.Count > 0) stack.Bypassed.Add(stack.Local[0].Kind);

        return stack;
    }

    // ------------------------------------------------------------ Speichern

    private static void TheGraphSurvivesSaving(World world)
    {
        Check.Group("Knoten: ein gespeicherter Graph rechnet wie vorher");

        int broken = 0, problems = 0;
        string first = "";

        for (int seed = 500; seed < 540; seed++)
        {
            var random = new Random(seed);
            var stack = world.RandomStack(random);
            var picture = RandomPicture(random);
            var adjust = RandomAdjust(random);

            var graph = StackToGraph.Convert(stack, adjust, picture);

            if (graph.Problems().Count > 0)
            {
                problems++;
                if (first.Length == 0) first = $"Fall {seed}: {graph.Problems()[0]}";
                continue;
            }

            var copy = NodeGraph.Load(graph.Save());

            if (copy is null)
            {
                broken++;
                if (first.Length == 0) first = $"Fall {seed}: nicht lesbar";
                continue;
            }

            var before = RenderGraph(world, graph, step: 1, number: 3, sixteen: false);
            var after = RenderGraph(world, copy, step: 1, number: 3, sixteen: false);

            if (!before.AsSpan().SequenceEqual(after))
            {
                broken++;
                if (first.Length == 0) first = $"Fall {seed}: rechnet nach dem Lesen anders";
            }
        }

        Check.That(problems == 0, "jeder umgewandelte Graph ist rechenbar", first);
        Check.That(broken == 0, "und rechnet nach Speichern und Lesen dasselbe", first);
    }

    // ------------------------------------------------------------ Kabel

    /// <summary>
    /// Was man im Graphen selbst baut, rechnet wie das, was man im Stapel gebaut haette:
    /// ein Pass als Ebene, eine Passmaske an einem anderen Pass. Ein Griff, der im Graphen
    /// etwas anderes ergibt als die Ebene, die er nachbildet, waere ein Griff, dem man
    /// nicht trauen kann.
    /// </summary>
    private static void WiredCases(World world)
    {
        Check.Group("Knoten: Passe als Kabel rechnen wie im Stapel");

        var adjust = Adjust(0.2, 1.1, 0, 1, 1.05);
        var picture = new GradingStack { Optics = { new VignetteTool { Amount = -0.5f } } };

        // Ein Pass als Ebene - obenauf, vor den Werkzeugen am Bild.
        foreach (var (name, below) in new[]
        {
            ("auf das Bild", Base()),
            ("auf eine Ebene mit Maske", Base(Masked(Image("bild.png"), new LayerMask { Kind = MaskKind.Gradient, Angle = 20, Width = 0.7f }))),
        })
        {
            var withLayer = below.Clone();
            withLayer.Layers.Add(Pass("P.a", BlendMode.Add));

            var graph = StackToGraph.Convert(below, adjust, picture);
            var render = graph.Nodes.OfType<RenderNode>().Single();
            var top = NodeEdits.LayerTop(graph);

            bool built = top is not null &&
                         NodeEdits.ShowPass(graph, render, "P.a", on: true) &&
                         NodeEdits.AddLayer(graph, top, render, "P.a", BlendMode.Add) is not null;

            Check.That(built && graph.Problems().Count == 0, $"Pass als Ebene {name}: der Graph ist rechenbar",
                       string.Join("; ", graph.Problems()));

            foreach (int step in new[] { 1, 3 })
            {
                var (differ, worst) = Diff(RenderStack(world, withLayer, adjust, picture, step, 5, sixteen: false),
                                           RenderGraph(world, graph, step, 5, sixteen: false), 1);

                Check.That(differ == 0, $"Pass als Ebene {name}{(step > 1 ? " (grob)" : "")} gleicht der Ebene im Stapel",
                           $"{differ} Bytes anders, bis {worst} Stufen");
            }

            var (differ16, _) = Diff(RenderStack(world, withLayer, adjust, picture, 1, 5, sixteen: true),
                                     RenderGraph(world, graph, 1, 5, sixteen: true), 2);

            Check.That(differ16 == 0, $"Pass als Ebene {name} (16 Bit)", $"{differ16} Werte anders");
        }

        // Eine Passmaske haengt am Kabel - und ein anderer Pass am Kabel ist eine andere Maske.
        var mist = Base(Masked(Image("bild.png"), new LayerMask { Kind = MaskKind.Pass, Source = "mist", Low = 0.1f, High = 0.8f }));
        var depth = Base(Masked(Image("bild.png"), new LayerMask { Kind = MaskKind.Pass, Source = "depth", Low = 0.1f, High = 0.8f }));

        var wired = StackToGraph.Convert(mist, ImageAdjustments.Neutral, new GradingStack());
        var mask = wired.Nodes.OfType<MaskNode>().Single();
        var file = wired.Nodes.OfType<RenderNode>().Single();

        Check.That(wired.Into(mask.Id, "Pass") is { Output: "mist" } link && link.From == file.Id,
                   "die umgewandelte Passmaske liest ihren Pass ueber ein Kabel von der Datei");

        Check.That(GraphEvaluator.Reads(wired).Count(r => r.Key == "mist") == 1,
                   "und der Pass wird einmal gelesen, nicht zweimal");

        var expectedMist = RenderStack(world, mist, ImageAdjustments.Neutral, new GradingStack(), 1, 0, sixteen: false);
        var expectedDepth = RenderStack(world, depth, ImageAdjustments.Neutral, new GradingStack(), 1, 0, sixteen: false);

        Check.That(Diff(expectedMist, expectedDepth, 1).Differ > 50, "Nebel und Tiefe ergeben verschiedene Masken",
                   "sonst prueft das Umstecken nichts");

        NodeEdits.ShowPass(wired, file, "depth", on: true);
        Check.That(NodeEdits.Connect(wired, file, "depth", mask, "Pass"), "ein anderer Pass laesst sich anstecken");

        var (differDepth, worstDepth) = Diff(expectedDepth, RenderGraph(world, wired, 1, 0, sixteen: false), 1);

        Check.That(differDepth == 0, "umgesteckt rechnet die Maske mit dem neuen Pass",
                   $"{differDepth} Bytes anders, bis {worstDepth} Stufen");

        NodeEdits.ShowPass(wired, file, "depth", on: false);

        Check.That(wired.Into(mask.Id, "Pass") is null && wired.Problems().Count == 0,
                   "ein abgeschalteter Ausgang nimmt sein Kabel mit, und der Graph bleibt heil",
                   string.Join("; ", wired.Problems()));

        Check.That(Diff(expectedMist, RenderGraph(world, wired, 1, 0, sixteen: false), 1).Differ == 0,
                   "ohne Kabel liest die Maske wieder den Pass, den sie beim Namen nennt");

        Check.That(!NodeEdits.ShowPass(wired, file, RenderNode.Depth, on: true) &&
                   !file.Passes.Contains(RenderNode.Depth),
                   "ein Pass mit dem Namen eines festen Ausgangs wird kein zweiter Ausgang");

        // Eine Verzweigung: dieselbe Ebene zweimal, einmal als Kopie mit eigener Mischung.
        var branch = StackToGraph.Convert(Base(Pass("P.a", BlendMode.Add)), ImageAdjustments.Neutral, new GradingStack());
        var before = RenderGraph(world, branch, 1, 0, sixteen: false);
        var place = branch.Nodes.OfType<PlaceNode>().Last();
        var copy = NodeEdits.Duplicate(branch, place);

        Check.That(copy is PlaceNode && Diff(before, RenderGraph(world, branch, 1, 0, sixteen: false), 1).Differ == 0,
                   "eine Kopie ohne Leser aendert das Bild nicht");

        var twice = Base(Pass("P.a", BlendMode.Add), Pass("P.a", BlendMode.Add));
        var mix = branch.Nodes.OfType<MixNode>().Last();

        bool branched = copy is not null && NodeEdits.AddLayer(branch, mix, copy, "Bild", BlendMode.Add) is { } added &&
                        NodeEdits.Remove(branch, added.Place, reconnect: false) &&
                        NodeEdits.Connect(branch, copy, "Bild", added.Mix, "Oben");

        var (differTwice, worstTwice) = Diff(RenderStack(world, twice, ImageAdjustments.Neutral, new GradingStack(), 1, 0, sixteen: false),
                                             RenderGraph(world, branch, 1, 0, sixteen: false), 1);

        Check.That(branched && differTwice == 0, "die Kopie als zweite Ebene gleicht derselben Ebene zweimal im Stapel",
                   $"{differTwice} Bytes anders, bis {worstTwice} Stufen");
    }

    private static ImageLayer Masked(ImageLayer layer, LayerMask mask)
    {
        layer.Mask = mask;
        return layer;
    }

    // ------------------------------------------------------------ Rechnen

    private static (int Differ, int Worst) Compare8(World world, LayerStack stack, ImageAdjustments adjust,
                                                     GradingStack picture, int step, int number)
    {
        var graph = StackToGraph.Convert(stack, adjust, picture);

        var expected = RenderStack(world, stack, adjust, picture, step, number, sixteen: false);
        var actual = RenderGraph(world, graph, step, number, sixteen: false);

        return Diff(expected, actual, 1);
    }

    private static (int Differ, int Worst) Compare16(World world, LayerStack stack, ImageAdjustments adjust,
                                                      GradingStack picture, int number)
    {
        var graph = StackToGraph.Convert(stack, adjust, picture);

        var expected = RenderStack(world, stack, adjust, picture, 1, number, sixteen: true);
        var actual = RenderGraph(world, graph, 1, number, sixteen: true);

        return Diff(expected, actual, 2);
    }

    private static (int Differ, int Worst) Diff(byte[] expected, byte[] actual, int size)
    {
        int differ = 0, worst = 0;

        for (int i = 0; i < expected.Length; i += size)
        {
            int e = size == 1 ? expected[i] : BitConverter.ToUInt16(expected, i);
            int a = size == 1 ? actual[i] : BitConverter.ToUInt16(actual, i);

            if (e == a) continue;

            differ++;
            worst = Math.Max(worst, Math.Abs(e - a));
        }

        return (differ, worst);
    }

    /// <summary>
    /// Der Stapel, so wie die Seite ihn rechnet: zusammensetzen - oder das Bild, wenn
    /// nichts beitraegt -, dann die Werkzeuge mit den Renderdaten, die sie brauchen.
    /// </summary>
    private static byte[] RenderStack(World world, LayerStack stack, ImageAdjustments adjust,
                                      GradingStack picture, int step, int number, bool sixteen)
    {
        stack = stack.Clone();
        picture = picture.Clone();

        var frame = stack.IsPassThrough
            ? world.Sources[""]
            : LayerComposer.Compose(stack, world.Sources, null, step, number) ?? world.Sources[""];

        var overlays = Overlays.Prepare(stack, world.Sources, frame.Width, frame.Height);
        var prepared = picture.Prepare();

        var data = prepared.Data.Select(t => t.Needs switch
        {
            PassNeed.Depth => world.Depth,
            PassNeed.Motion => world.Motion,
            PassNeed.Normal => world.Normal,
            _ => null,
        }).ToArray();

        int stride = Width * (sixteen ? 8 : 4);
        var pixels = new byte[stride * Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            if (sixteen)
                FloatFrameProcessor.ApplyRgba64(frame, adjust, View, prepared, buffer, stride, overlays, number, data);
            else
                FloatFrameProcessor.Apply(frame, adjust, View, prepared, buffer, stride, step, overlays, number, data);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static byte[] RenderGraph(World world, NodeGraph graph, int step, int number, bool sixteen)
    {
        var inputs = new GraphInputs
        {
            Sources = world.Sources,
            Data = new Dictionary<PassNeed, FloatFrame?>
            {
                [PassNeed.Depth] = world.Depth,
                [PassNeed.Motion] = world.Motion,
                [PassNeed.Normal] = world.Normal,
            },
            View = View,
            Step = step,
            Number = number,
        };

        int stride = Width * (sixteen ? 8 : 4);
        var pixels = new byte[stride * Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            bool done = sixteen
                ? GraphEvaluator.Render16(graph, inputs, buffer, stride)
                : GraphEvaluator.Render(graph, inputs, buffer, stride);

            if (done) Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    // ------------------------------------------------------------ Bausteine

    private static LayerStack Base(params ImageLayer[] above)
    {
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };
        stack.Layers.AddRange(above);
        return stack;
    }

    private static ImageLayer Pass(string source, BlendMode mode = BlendMode.Normal)
        => new() { Content = LayerContent.Pass, Source = source, Mode = mode };

    private static ImageLayer Image(string source, BlendMode mode = BlendMode.Normal)
        => new() { Content = LayerContent.Image, Source = source, Mode = mode, FollowSequence = false };

    private static ImageLayer Placed(string source, float scale, float x, float y, float rotation)
    {
        var layer = Image(source);
        layer.Place = new LayerTransform { Scale = scale, OffsetX = x, OffsetY = y, Rotation = rotation };
        return layer;
    }

    private static ImageLayer Clip(ImageLayer layer)
    {
        layer.Clipped = true;
        return layer;
    }

    private static ImageLayer Tinted(ImageLayer layer)
    {
        layer.Exposure = 0.7f;
        layer.Tint = new ColourTriplet(1f, 0.8f, 0.6f);
        return layer;
    }

    private static ImageLayer Display(ImageLayer layer)
    {
        layer.BlendInDisplay = true;
        layer.Opacity = 0.7f;
        return layer;
    }

    private static ImageLayer Revealed(ImageLayer layer)
    {
        layer.MatteFloor = 0.2f;
        layer.Reveal = 0.3f;
        return layer;
    }

    private static ImageLayer OnTop(ImageLayer layer)
    {
        layer.OnTop = true;
        return layer;
    }

    private static ImageLayer Scoped(ImageLayer layer)
    {
        layer.Adjustments = Adjust(1, 0.3, 0, 1, 1);
        layer.Mask = new LayerMask { Kind = MaskKind.Gradient, Scope = MaskScope.Colour, Angle = 30, Width = 0.6f };
        return layer;
    }

    private static ImageLayer Adjustment(ImageAdjustments adjust)
        => new()
        {
            Content = LayerContent.Adjustment,
            Adjustments = adjust,
            Tools = new GradingStack { Tools = { new WhiteBalanceTool { Kelvin = 5000 } } },
        };

    private static ImageLayer Group(float opacity, BlendMode mode, params ImageLayer[] children)
    {
        var group = new ImageLayer
        {
            Content = LayerContent.Group,
            Opacity = opacity,
            Mode = mode,
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 0, Width = 0.8f },
        };

        group.Children.AddRange(children);
        return group;
    }

    private static ImageAdjustments Adjust(double exposure, double saturation, double black, double gamma, double contrast)
        => new() { Exposure = exposure, Saturation = saturation, BlackPoint = black, Gamma = gamma, Contrast = contrast };

    /// <summary>
    /// Die Welt, in der gerechnet wird: das Bild der Datei, Passe, Bilddateien und
    /// Renderdaten - klein, aber mit allem, was eine Rechnung unterscheiden kann:
    /// Werte ueber Weiss, Deckung null, weiche Kanten, ein Bild ohne Alpha.
    /// </summary>
    private sealed class World
    {
        public readonly Dictionary<string, FloatFrame> Sources = new(StringComparer.Ordinal);
        public readonly FloatFrame Depth;
        public readonly FloatFrame Motion;
        public readonly FloatFrame Normal;

        public World()
        {
            Sources[""] = Frame(Width, Height, scene: true, alpha: true, (x, y) =>
                (0.1f + 2.5f * x / Width, 0.05f + 1.2f * y / Height, (x + y) % 7 == 0 ? 4f : 0.3f,
                 y < 6 ? 0f : x < 10 ? 0.5f : 1f));

            Sources["P.a"] = Frame(Width, Height, scene: true, alpha: false, (x, y) =>
                (0.4f * ((x / 6) % 2), 0.2f + 0.3f * MathF.Sin(x * 0.4f + y * 0.2f), 0.6f, 1f));

            Sources["P.b"] = Frame(Width, Height, scene: true, alpha: true, (x, y) =>
                (0.8f, 0.3f * y / Height, 0.1f + 0.05f * x, x > 30 ? 0.2f : 0.9f));

            Sources["mist"] = Frame(Width, Height, scene: true, alpha: false, (x, y) =>
                ((float)x / (Width - 1), (float)x / (Width - 1), (float)x / (Width - 1), 1f));

            Sources["depth"] = Frame(Width, Height, scene: true, alpha: false, (x, y) =>
                (1f + 11f * y / (Height - 1), 1f + 11f * y / (Height - 1), 1f + 11f * y / (Height - 1), 1f));

            Sources["bild.png"] = Frame(Width, Height, scene: false, alpha: true, (x, y) =>
            {
                float dx = (x - 30f) / 14f, dy = (y - 14f) / 10f;
                float blob = Math.Clamp(1.2f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
                return (0.9f * blob + 0.05f, 0.4f, 0.8f - 0.5f * blob, blob);
            });

            Sources["ohne.png"] = Frame(Width, Height, scene: false, alpha: false, (x, y) =>
                (0.3f + 0.4f * ((x / 4 + y / 4) % 2), 0.5f, 0.2f, 1f));

            Sources["logo.png"] = Frame(12, 8, scene: false, alpha: true, (x, y) =>
                (1f, 0.9f, 0.2f, x == 0 || y == 0 || x == 11 || y == 7 ? 0.1f : 1f));

            Depth = Frame(Width, Height, scene: true, alpha: false, (x, y) =>
                (1f + 9f * x / (Width - 1), 0f, 0f, 1f));

            Motion = Frame(Width, Height, scene: true, alpha: true, (x, y) =>
                (3f, x > 24 ? 2f : -1f, 0f, 0f));

            Normal = Frame(Width, Height, scene: true, alpha: false, (x, y) =>
                (MathF.Sin(x * 0.3f), MathF.Cos(y * 0.25f), 0.5f, 1f));
        }

        private static FloatFrame Frame(int width, int height, bool scene, bool alpha,
                                        Func<int, int, (float R, float G, float B, float A)> at)
        {
            int count = width * height;
            var r = new float[count];
            var g = new float[count];
            var b = new float[count];
            var a = alpha ? new float[count] : null;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (vr, vg, vb, va) = at(x, y);
                    int i = y * width + x;

                    r[i] = vr;
                    g[i] = vg;
                    b[i] = vb;
                    if (a is not null) a[i] = va;
                }
            }

            return new FloatFrame { Width = width, Height = height, R = r, G = g, B = b, A = a, IsSceneReferred = scene };
        }

        public LayerMask Mask(MaskKind kind, Random random)
        {
            var mask = new LayerMask
            {
                Kind = kind,
                Invert = random.Next(4) == 0,
                Scope = random.Next(3) == 0 ? MaskScope.Colour : MaskScope.Visibility,
                Low = (float)random.NextDouble() * 0.3f,
                High = 0.7f + (float)random.NextDouble() * 0.3f,
                Softness = (float)random.NextDouble() * 0.5f,
                Hue = (float)random.NextDouble() * 360f,
                Spread = 10f + (float)random.NextDouble() * 80f,
                Angle = (float)random.NextDouble() * 180f,
                Centre = 0.3f + (float)random.NextDouble() * 0.4f,
                Width = 0.2f + (float)random.NextDouble() * 0.6f,
                Source = random.Next(3) switch { 0 => "mist", 1 => "depth", _ => "fehlt" },
            };

            if (kind == MaskKind.Painted)
            {
                var paint = PaintedMask.For(Width, Height);

                for (int s = 0; s < 3; s++)
                    paint.Stroke(random.Next(Width), random.Next(Height), 4f + random.Next(8), 1f, 1f,
                                 (float)random.NextDouble());

                mask.Paint = paint;
                mask.PaintLocked = true;
            }

            return mask;
        }

        public LayerStack RandomStack(Random random)
        {
            var stack = new LayerStack();

            if (random.Next(8) != 0) stack.Layers.Add(new ImageLayer { Content = LayerContent.Pass, Source = "" });

            stack.Layers.AddRange(RandomLayers(random, depth: 0));

            return stack;
        }

        private List<ImageLayer> RandomLayers(Random random, int depth)
        {
            var layers = new List<ImageLayer>();
            int count = random.Next(1, depth == 0 ? 6 : 4);

            for (int i = 0; i < count; i++) layers.Add(RandomLayer(random, depth));

            return layers;
        }

        private ImageLayer RandomLayer(Random random, int depth)
        {
            var modes = Enum.GetValues<BlendMode>();
            int roll = random.Next(100);

            var layer = new ImageLayer
            {
                Visible = random.Next(20) != 0,
                Mode = modes[random.Next(modes.Length)],
                BlendInDisplay = random.Next(5) == 0,
                Clipped = random.Next(4) == 0,
                FollowSequence = false,
            };

            layer.Opacity = random.Next(10) switch
            {
                0 => 0f,
                1 or 2 => (float)random.NextDouble(),
                _ => 1f,
            };

            if (random.Next(3) == 0) layer.Exposure = (float)(random.NextDouble() * 3 - 1.5);
            if (random.Next(5) == 0) layer.Tint = new ColourTriplet(0.6f + (float)random.NextDouble() * 0.8f, 1f, 0.7f);
            if (random.Next(6) == 0) layer.MatteFloor = (float)random.NextDouble() * 0.95f;
            if (random.Next(6) == 0) layer.Reveal = (float)random.NextDouble();

            if (random.Next(3) != 0)
            {
                var kinds = new[] { MaskKind.Luminance, MaskKind.Underlying, MaskKind.Pass, MaskKind.Painted,
                                    MaskKind.Colour, MaskKind.Gradient };
                layer.Mask = Mask(kinds[random.Next(kinds.Length)], random);
            }

            if (roll < 30)
            {
                layer.Content = LayerContent.Pass;
                layer.Source = random.Next(3) switch { 0 => "", 1 => "P.a", _ => "P.b" };
            }
            else if (roll < 55)
            {
                layer.Content = LayerContent.Image;
                layer.Source = random.Next(4) switch { 0 => "logo.png", 1 => "ohne.png", _ => "bild.png" };

                if (random.Next(5) == 0) layer.OnTop = true;

                if (layer.Source == "logo.png" || random.Next(3) == 0)
                {
                    layer.Place = new LayerTransform
                    {
                        Scale = 0.3f + (float)random.NextDouble() * 1.2f,
                        OffsetX = (float)random.NextDouble() * 0.6f - 0.3f,
                        OffsetY = (float)random.NextDouble() * 0.6f - 0.3f,
                        Rotation = (float)random.NextDouble() * 60f - 30f,
                        CropLeft = random.Next(4) == 0 ? 0.1f : 0f,
                    };
                }
            }
            else if (roll < 82 || depth >= 2)
            {
                layer.Content = LayerContent.Adjustment;
            }
            else
            {
                layer.Content = LayerContent.Group;
                layer.Children.AddRange(RandomLayers(random, depth + 1));
            }

            if (layer.Content != LayerContent.Group && random.Next(3) == 0)
            {
                layer.Adjustments = new ImageAdjustments
                {
                    Exposure = random.NextDouble() * 2 - 1,
                    Saturation = 0.3 + random.NextDouble() * 1.2,
                    Gamma = 0.8 + random.NextDouble() * 0.4,
                    Contrast = 0.8 + random.NextDouble() * 0.5,
                };
            }

            if (layer.Content != LayerContent.Group && random.Next(4) == 0)
            {
                layer.Tools = new GradingStack
                {
                    Tools =
                    {
                        new WhiteBalanceTool { Kelvin = 3500 + random.Next(4000) },
                        new VibranceTool { Amount = (float)random.NextDouble() - 0.3f },
                    },
                };
            }

            return layer;
        }
    }
}
