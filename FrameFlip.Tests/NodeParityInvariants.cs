using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

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

    /// <summary>
    /// Ein Vorrat fuer alle Faelle zusammen - so wie die Seite einen fuer alle Bilder hat.
    /// Gibt der Auswerter ein Feld zu frueh zurueck, schreibt der naechste Fall hinein, und
    /// der Vergleich mit dem Stapel merkt es.
    /// </summary>
    private static readonly GridPool Pool = new();

    public static void Run()
    {
        var world = new World();

        TheComparisonCanFail(world);
        NamedCases(world);
        RandomCases(world);
        TheGraphSurvivesSaving(world);
        WiredCases(world);
        CachedCases(world);
        ThePoolLeavesNoTrace(world);
        HiddenLayers(world);
        OldGraphsGetTheirHiddenLayers(world);
    }

    /// <summary>
    /// Ein Graph, der umgewandelt wurde, als ausgeblendete Ebenen noch wegfielen - und an
    /// dem seitdem gebaut wurde. Bernds Fall: Grundbild aus, zwei Glare-Ebenen aus, ein
    /// Bild an, eine Einstellungsebene mit gemalter Maske, und zwei eigene Knoten hinter
    /// dem Rueckfall. Die fehlenden Ebenen kommen hinein, ohne dass sich etwas aendert.
    /// </summary>
    private static void OldGraphsGetTheirHiddenLayers(World world)
    {
        Check.Group("Knoten: ein alter Graph bekommt seine ausgeblendeten Ebenen");

        var painted = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true, Paint = PaintedMask.For(Width, Height) };
        var cover = painted.Paint!.Cover();
        for (int i = 0; i < cover.Length / 2; i++) cover[i] = 255;
        painted.Paint.Keep();

        ImageLayer Named(ImageLayer layer, string name, bool visible)
        {
            layer.Name = name;
            layer.Visible = visible;
            return layer;
        }

        var stack = new LayerStack
        {
            Layers =
            {
                Named(new ImageLayer { Content = LayerContent.Pass, Source = "" }, "Image", visible: false),
                Named(Image("ohne.png", BlendMode.Screen), "Glare_2", visible: false),
                Named(Image("logo.png", BlendMode.Add), "Glare_1", visible: false),
                Named(Image("bild.png"), "Image0035.png", visible: true),
                Named(Masked(Adjustment(Adjust(0.6, 1, 0, 1, 1)), painted), "Mask", visible: true),
            },
        };

        var picture = new GradingStack { Optics = { new VignetteTool { Amount = -0.3f } } };

        // Eigene Knoten hinter dem Rueckfall - wie aus der Palette eingesetzt.
        void Edit(NodeGraph graph)
        {
            var fallback = graph.Nodes.OfType<FallbackNode>().Single();
            NodeEdits.InsertAfter(graph, fallback, new PointToolNode { Tool = new VibranceTool { Amount = 0.4f } });
            NodeEdits.InsertAfter(graph, fallback, new LightNode { Exposure = 0.3 });
        }

        var fresh = StackToGraph.Convert(stack, ImageAdjustments.Neutral, picture);
        var old = Old(StackToGraph.Convert(stack, ImageAdjustments.Neutral, picture));
        Edit(old);

        var before = RenderGraph(world, old, 1, 0, false);
        var missing = FrameFlip.Imaging.Nodes.HiddenLayers.Missing(old, fresh);

        Check.That(missing.SequenceEqual(new[] { "Image", "Glare_2", "Glare_1" }),
                   "es fehlen die drei ausgeblendeten Ebenen, von unten nach oben", string.Join(", ", missing));
        Check.That(FrameFlip.Imaging.Nodes.HiddenLayers.CanAdopt(old, fresh), "und sie lassen sich hineinsetzen - die sichtbaren Ebenen sind noch dieselben");

        Check.That(FrameFlip.Imaging.Nodes.HiddenLayers.Adopt(old, fresh) && old.Problems().Count == 0, "hineingesetzt ist der Graph heil",
                   string.Join("; ", old.Problems()));
        Check.That(Diff(before, RenderGraph(world, old, 1, 0, false), 1).Differ == 0, "und das Bild dasselbe");
        Check.That(old.Nodes.OfType<LightNode>().Count(l => l.Exposure == 0.3) == 1 &&
                   old.Nodes.OfType<PointToolNode>().Any(p => p.Tool is VibranceTool { Amount: 0.4f }),
                   "die eigenen Knoten sind noch da");

        var names = NodeLayerList.Of(old).Select(l => l.Name).ToList();
        Check.That(names.SequenceEqual(new[] { "Mask", "Image0035.png", "Glare_1", "Glare_2", "Image" }),
                   "die Ebenenliste zeigt alle fuenf Ebenen des Stapels, mit ihren Namen", string.Join(" | ", names));
        Check.That(FrameFlip.Imaging.Nodes.HiddenLayers.Missing(old, fresh).Count == 0, "danach fehlt nichts mehr");

        // Eingeschaltet wirkt eine geholte Ebene wie im frisch umgewandelten Graphen.
        Edit(fresh);
        old.Nodes.OfType<MixNode>().Single(m => m.Label == "Glare_1").Muted = false;
        fresh.Nodes.OfType<MixNode>().Single(m => m.Label == "Glare_1").Muted = false;

        var (differOn, worstOn) = Diff(RenderGraph(world, fresh, 1, 0, false), RenderGraph(world, old, 1, 0, false), 1);

        Check.That(differOn == 0 && Diff(before, RenderGraph(world, old, 1, 0, false), 1).Differ > 0,
                   "eingeschaltet wirkt Glare_1 wie im frisch umgewandelten Graphen", $"{differOn} Bytes anders, bis {worstOn}");

        // Eine verstellte Ebene ist noch dieselbe Ebene.
        var fresh2 = StackToGraph.Convert(stack, ImageAdjustments.Neutral, picture);
        var tuned = Old(StackToGraph.Convert(stack, ImageAdjustments.Neutral, picture));
        foreach (var mix in tuned.Nodes.OfType<MixNode>()) { mix.Mode = BlendMode.Screen; mix.Opacity = 0.7f; }

        Check.That(FrameFlip.Imaging.Nodes.HiddenLayers.CanAdopt(tuned, fresh2),
                   "eine Ebene mit anderer Mischart und Deckkraft wird noch erkannt");

        // Ein Graph, dessen Ebenen nicht mehr die des Stapels sind, bleibt unberuehrt -
        // hier ist eine Ebene aus einer anderen Datei hinzugekommen, und eine andere fehlt.
        var rebuilt = Old(StackToGraph.Convert(stack, ImageAdjustments.Neutral, picture));
        var swapped = rebuilt.Nodes.OfType<PictureNode>().First();
        swapped.Path = "ohne.png";
        int nodes = rebuilt.Nodes.Count;

        bool can = FrameFlip.Imaging.Nodes.HiddenLayers.CanAdopt(rebuilt, fresh2);
        bool did = FrameFlip.Imaging.Nodes.HiddenLayers.Adopt(rebuilt, fresh2);

        Check.That(!can && !did && rebuilt.Nodes.Count == nodes,
                   "ist die Kette umgebaut, wird nichts hineingesetzt - dann bleibt der Neuaufbau",
                   $"kann {can}, getan {did}, Knoten {rebuilt.Nodes.Count} statt {nodes}, Kette " +
                   string.Join(",", FrameFlip.Imaging.Nodes.HiddenLayers.Chain(rebuilt).Select(n => n.GetType().Name)) + " gegen " +
                   string.Join(",", FrameFlip.Imaging.Nodes.HiddenLayers.Chain(fresh2).Select(n => n.GetType().Name + (n.Muted ? "~" : ""))));
    }

    /// <summary>
    /// Ein Graph, wie der Umwandler ihn frueher baute: ohne ausgeblendete Ebenen und ohne
    /// Namen. Die stummen Mischen kommen heraus, und was nur an ihnen hing, gleich mit.
    /// </summary>
    private static NodeGraph Old(NodeGraph graph)
    {
        foreach (var mix in graph.Nodes.OfType<MixNode>().Where(m => m.Muted).ToList())
            NodeEdits.Remove(graph, mix, reconnect: true);

        var live = graph.Order()!.Select(n => n.Id).ToHashSet();

        foreach (var node in graph.Nodes.Where(n => !live.Contains(n.Id)).ToList())
            NodeEdits.Remove(graph, node, reconnect: false);

        foreach (var node in graph.Nodes) node.Label = null;

        return graph;
    }

    // ------------------------------------------------------------ Ausgeblendete Ebenen

    /// <summary>
    /// Ausgeblendete Ebenen stehen stumm im Graphen: Das Bild bleibt Byte fuer Byte das
    /// des Stapels, ihre Dateien werden nicht gelesen, und eingeschaltet tun sie, was
    /// sie im Stapel taeten. So wie Bernds "Glare"-Ebenen, die beim Umwandeln verschwanden.
    /// </summary>
    private static void HiddenLayers(World world)
    {
        Check.Group("Knoten: ausgeblendete Ebenen stehen stumm im Graphen");

        ImageLayer Hide(ImageLayer layer, string name)
        {
            layer.Visible = false;
            layer.Name = name;
            return layer;
        }

        var painted = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true, Paint = PaintedMask.For(Width, Height) };
        var cover = painted.Paint!.Cover();
        for (int i = 0; i < cover.Length / 3; i++) cover[i] = 255;
        painted.Paint.Keep();

        var faint = Image("bild.png", BlendMode.Screen);
        faint.Opacity = 0f;
        faint.Name = "ohne Deckkraft";

        var stack = Base(
            Hide(Image("ohne.png", BlendMode.Add), "Glare A"),
            Placed("logo.png", 0.5f, 0.1f, 0f, 0f),
            Hide(Image("bild.png", BlendMode.Screen), "zwischen Traeger und Schnitt"),
            Clip(Image("bild.png")),
            Hide(Clip(Pass("P.a", BlendMode.Add)), "angeschnitten, aus"),
            Hide(Masked(Adjustment(Adjust(0.5, 1, 0, 1, 1)), painted), "Mask"),
            faint);

        foreach (int step in new[] { 1, 3 })
        {
            var (differ, worst) = Compare8(world, stack, ImageAdjustments.Neutral, new GradingStack(), step, number: 0);

            Check.That(differ == 0, $"das Bild bleibt das des Stapels{(step > 1 ? " (grob)" : "")}", $"{differ} Bytes anders, bis {worst}");
        }

        var (differ16, _) = Compare16(world, stack, ImageAdjustments.Neutral, new GradingStack(), number: 0);
        Check.That(differ16 == 0, "auch in 16 Bit", $"{differ16} Werte anders");

        var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());
        var silent = graph.Nodes.OfType<MixNode>().Where(m => m.Muted).ToList();

        Check.That(silent.Count == 5 && silent.Select(m => m.Label).OrderBy(l => l).SequenceEqual(
                       new[] { "angeschnitten, aus", "Glare A", "Mask", "ohne Deckkraft", "zwischen Traeger und Schnitt" }.OrderBy(l => l)),
                   "jede ausgeblendete Ebene steht als stummes Mischen da, mit ihrem Namen",
                   string.Join(", ", silent.Select(m => m.Label)));

        Check.That(!GraphEvaluator.Reads(graph).Any(r => r.Key == "ohne.png"),
                   "was nur eine ausgeblendete Ebene braucht, wird nicht gelesen");

        var (_, ran) = Counted(() => RenderGraph(world, graph, 1, 0, false));
        Check.That(!ran.OfType<PictureNode>().Any(p => p.Path == "ohne.png") && !ran.OfType<LayerGradeNode>().Any(),
                   "und nicht gerechnet", string.Join(", ", ran.Select(n => n.GetType().Name)));

        // Die Ebenenliste: Namen der Ebenen, stumme als ausgeblendet, die Maske daneben.
        var layers = NodeLayerList.Of(graph);
        var mask = layers.SingleOrDefault(l => l.Name == "Mask");

        Check.That(layers.Count(l => l.Mix?.Muted == true) == 5 && mask?.MaskSource is MaskNode { Mask.Kind: MaskKind.Painted },
                   "die Ebenenliste zeigt alle ausgeblendeten Ebenen - und bei der Maskenebene ihre gemalte Maske",
                   string.Join(" | ", layers.Select(l => l.Name)));
        Check.That(layers.Single(l => l.Name == "Glare A").Origin is { Node: PictureNode { Path: "ohne.png" } },
                   "eine ausgeblendete Ebene kennt ihre Quelle - fuer die Miniatur, die nicht gerechnet wird");

        // Eingeschaltet tut eine Ebene, was sie im Stapel taete.
        foreach (string name in new[] { "Glare A", "Mask", "angeschnitten, aus" })
        {
            var shown = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());
            shown.Nodes.OfType<MixNode>().Single(m => m.Label == name).Muted = false;

            var visible = stack.Clone();
            Find(visible.Layers, name)!.Visible = true;

            var (differOn, worstOn) = Diff(RenderStack(world, visible, ImageAdjustments.Neutral, new GradingStack(), 1, 0, false),
                                           RenderGraph(world, shown, 1, 0, false), 1);

            Check.That(differOn == 0, $"{name}: eingeschaltet wie im Stapel sichtbar", $"{differOn} Bytes anders, bis {worstOn}");
        }
    }

    private static ImageLayer? Find(IEnumerable<ImageLayer> layers, string name)
    {
        foreach (var layer in layers)
        {
            if (layer.Name == name) return layer;
            if (layer.Children is { } children && Find(children, name) is { } found) return found;
        }

        return null;
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

        // Ein Wertebereich an der Tiefe rechnet in der Grundstellung wie die Passmaske -
        // dieselbe Spanne, derselbe Schwarz- und Weisspunkt.
        foreach (int step in new[] { 1, 3 })
        {
            var ranged = StackToGraph.Convert(depth, ImageAdjustments.Neutral, new GradingStack());
            var passMask = ranged.Nodes.OfType<MaskNode>().Single();
            var feed = ranged.Into(passMask.Id, "Pass")!;
            var reader = ranged.Links.Single(l => l.From == passMask.Id);

            var map = ranged.Add(new MapRangeNode { FromLow = 0.1f, FromHigh = 0.8f });
            ranged.Connect(ranged.Find(feed.From)!, feed.Output, map, "Wert");
            ranged.Connect(map, "Wert", ranged.Find(reader.To)!, reader.Input);
            NodeEdits.Remove(ranged, passMask, reconnect: false);

            var (differRange, worstRange) = Diff(RenderStack(world, depth, ImageAdjustments.Neutral, new GradingStack(), step, 0, sixteen: false),
                                                 RenderGraph(world, ranged, step, 0, sixteen: false), 1);

            Check.That(differRange == 0, $"ein Wertebereich an der Tiefe rechnet wie die Passmaske{(step > 1 ? " (grob)" : "")}",
                       $"{differRange} Bytes anders, bis {worstRange} Stufen");
        }

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

    // ------------------------------------------------------------ Zwischenspeicher und Vorrat

    /// <summary>
    /// Der Zwischenspeicher liefert dasselbe Bild wie der Stapel - und laesst wirklich
    /// aus, was vor dem gewaehlten Knoten liegt. Beides zusammen: Ein Speicher, der nie
    /// trifft, rechnete auch richtig.
    /// </summary>
    private static void CachedCases(World world)
    {
        Check.Group("Knoten: der Zwischenspeicher rechnet nur hinter dem gewaehlten Knoten");

        var stack = Base(Pass("P.a", BlendMode.Add), Image("bild.png", BlendMode.Screen));
        var picture = new GradingStack
        {
            Optics = { new VignetteTool { Amount = -0.4f } },
            Local = { new ClarityTool { Amount = 0.4f, Reach = 4 } },
        };

        var graph = StackToGraph.Convert(stack, Adjust(0.2, 1.1, 0, 1, 1.05), picture);
        var tone = graph.Nodes.OfType<ToneNode>().Single();
        var cache = new GraphCache();

        var first = RenderGraph(world, graph, 1, 0, false, cache, tone.Id);

        Check.That(Diff(RenderStack(world, stack, Adjust(0.2, 1.1, 0, 1, 1.05), picture, 1, 0, false), first, 1).Differ == 0,
                   "beim ersten Mal rechnet alles, und das Bild stimmt");
        Check.That(cache.Count > 0, "danach ist gemerkt, was in den Tonwert fliesst", $"{cache.Count}");

        // Am Tonwert drehen: Nur er und was dahinter kommt, wird gerechnet.
        tone.Gamma = 1.3;
        var (again, ran) = Counted(() => RenderGraph(world, graph, 1, 0, false, cache, tone.Id));
        var expected = RenderStack(world, stack, Adjust(0.2, 1.1, 0, 1.3, 1.05), picture, 1, 0, false);

        Check.That(Diff(expected, again, 1).Differ == 0, "am Tonwert gedreht, stimmt das Bild mit dem Stapel",
                   $"{Diff(expected, again, 1).Differ} Bytes anders");
        Check.That(ran.OfType<ToneNode>().Any() && !ran.Any(n => n is MixNode or PlaceNode or LightNode or ViewNode),
                   "und gerechnet wurden nur der Tonwert und was dahinter kommt",
                   string.Join(", ", ran.Select(n => n.GetType().Name)));

        // Ziehen (grob), dann Loslassen (voll): das volle Davor ist noch gemerkt.
        tone.Gamma = 1.1;
        var coarse = RenderGraph(world, graph, 3, 0, false, cache, tone.Id);

        Check.That(Diff(RenderStack(world, stack, Adjust(0.2, 1.1, 0, 1.1, 1.05), picture, 3, 0, false), coarse, 1).Differ == 0,
                   "grob beim Ziehen stimmt es auch");

        tone.Gamma = 1.2;
        var (released, ranReleased) = Counted(() => RenderGraph(world, graph, 1, 0, false, cache, tone.Id));

        Check.That(Diff(RenderStack(world, stack, Adjust(0.2, 1.1, 0, 1.2, 1.05), picture, 1, 0, false), released, 1).Differ == 0 &&
                   !ranReleased.Any(n => n is MixNode or PlaceNode),
                   "und beim Loslassen rechnen die Ebenen nicht neu - das volle Davor war noch gemerkt",
                   string.Join(", ", ranReleased.Select(n => n.GetType().Name)));

        // Einen Knoten im Editor zu verschieben aendert kein Bild - und kostet keine Rechnung.
        foreach (var node in graph.Nodes) node.X += 100;
        tone.Gamma = 1.25;
        var (_, ranMoved) = Counted(() => RenderGraph(world, graph, 1, 0, false, cache, tone.Id));

        Check.That(!ranMoved.Any(n => n is MixNode or PlaceNode), "verschobene Knoten rechnen nicht neu",
                   string.Join(", ", ranMoved.Select(n => n.GetType().Name)));

        // Dazwischen andere Bilder durch denselben Vorrat: Was gemerkt ist, bleibt unberuehrt.
        for (int seed = 900; seed < 906; seed++)
            RenderGraph(world, StackToGraph.Convert(world.RandomStack(new Random(seed)), ImageAdjustments.Neutral, new GradingStack()), 1, 0, false);

        tone.Gamma = 1.3;
        Check.That(Diff(expected, RenderGraph(world, graph, 1, 0, false, cache, tone.Id), 1).Differ == 0,
                   "auch wenn der Vorrat inzwischen andere Bilder gerechnet hat, stimmt das gemerkte Davor");

        // Ein neu gelesenes Bild ist ein anderes - auch unter demselben Namen.
        var sources = new Dictionary<string, FloatFrame>(world.Sources, StringComparer.Ordinal)
        {
            [""] = Darker(world.Sources[""]),
        };

        var (fresh, ranFresh) = Counted(() => RenderGraph(world, graph, 1, 0, false, cache, tone.Id, sources));

        Check.That(ranFresh.Any(n => n is MixNode) &&
                   Diff(RenderGraph(world, graph, 1, 0, false, sources: sources, pooled: false), fresh, 1).Differ == 0 &&
                   Diff(expected, fresh, 1).Differ > 0,
                   "ein neu gelesenes Bild rechnet alles neu");

        PaintingIsSeen(world, picture);
    }

    /// <summary>
    /// Waehrend eines Pinselstrichs aendert sich nur das entpackte Feld der Maske - was
    /// gespeichert wuerde, kommt erst nach dem Strich. Der Speicher muss den Strich trotzdem
    /// sehen, sonst bliebe das Bild beim Malen stehen.
    /// </summary>
    private static void PaintingIsSeen(World world, GradingStack picture)
    {
        var painted = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true, Paint = PaintedMask.For(Width, Height) };
        var stack = Base(Masked(Image("bild.png"), painted));
        var graph = StackToGraph.Convert(stack, Adjust(0.2, 1.1, 0, 1, 1.05), picture);

        var tone = graph.Nodes.OfType<ToneNode>().Single();
        var mask = graph.Nodes.OfType<MaskNode>().Single();
        var cache = new GraphCache();

        RenderGraph(world, graph, 1, 0, false, cache, tone.Id);
        var before = RenderGraph(world, graph, 1, 0, false, cache, tone.Id);

        // Ein Strich, der noch laeuft: nur das entpackte Feld, kein Keep.
        var cover = mask.Mask.PaintFor(0)!.Cover();
        for (int i = 0; i < cover.Length / 2; i++) cover[i] = 255;

        var during = RenderGraph(world, graph, 1, 0, false, cache, tone.Id);
        var truth = RenderGraph(world, graph, 1, 0, false, pooled: false);

        Check.That(Diff(before, during, 1).Differ > 0 && Diff(truth, during, 1).Differ == 0,
                   "ein Pinselstrich vor dem gewaehlten Knoten wird gesehen, bevor er gespeichert ist",
                   $"{Diff(truth, during, 1).Differ} Bytes anders als ohne Speicher");
    }

    /// <summary>
    /// Ein Feld aus dem Vorrat traegt das Bild einer frueheren Rechnung. Wo ein Knoten
    /// eine Stelle nicht beschreibt - ausserhalb einer platzierten Ebene -, muss trotzdem
    /// dasselbe herauskommen wie in einem frischen Feld.
    /// </summary>
    private static void ThePoolLeavesNoTrace(World world)
    {
        Check.Group("Knoten: ein gebrauchtes Feld hinterlaesst keine Spur");

        foreach (var (name, between) in new (string, Func<Node>?)[]
        {
            ("platziert", null),
            ("platziert und belichtet", () => new ExposureTintNode { Exposure = 0.5f }),
            ("platziert und korrigiert", () => new LayerGradeNode { Adjustments = new ImageAdjustments { Exposure = 0.5 } }),
        })
        {
            var graph = new NodeGraph();
            var render = graph.Add(new RenderNode());
            var place = graph.Add(new PlaceNode { Place = new LayerTransform { Scale = 0.5f, OffsetX = 0.2f } });
            var output = graph.Add(new OutputNode());

            graph.Connect(render, RenderNode.Picture, place, "Bild");
            Node last = place;

            if (between is not null)
            {
                var node = graph.Add(between());
                graph.Connect(place, "Bild", node, "Bild");
                last = node;
            }

            graph.Connect(last, "Bild", output, "Bild");

            var clean = RenderGraph(world, graph, 1, 0, false, pooled: false);

            // Den Vorrat mit Bildern fuellen, die ueberall etwas tragen.
            for (int seed = 950; seed < 954; seed++)
                RenderGraph(world, StackToGraph.Convert(world.RandomStack(new Random(seed)), ImageAdjustments.Neutral, new GradingStack()), 1, 0, false);

            var reused = RenderGraph(world, graph, 1, 0, false);

            Check.That(Diff(clean, reused, 1).Differ == 0, $"{name}: aus dem Vorrat dasselbe wie frisch",
                       $"{Diff(clean, reused, 1).Differ} Bytes anders");
        }
    }

    /// <summary>Welche Einheiten waehrend einer Rechnung gerechnet wurden.</summary>
    private static (byte[] Pixels, List<Node> Ran) Counted(Func<byte[]> render)
    {
        var ran = new List<Node>();
        GraphEvaluator.Ran = ran.Add;

        try
        {
            return (render(), ran);
        }
        finally
        {
            GraphEvaluator.Ran = null;
        }
    }

    /// <summary>Dasselbe Bild noch einmal gelesen - ein anderes Objekt, etwas dunkler.</summary>
    private static FloatFrame Darker(FloatFrame frame) => new()
    {
        Width = frame.Width,
        Height = frame.Height,
        R = frame.R.Select(v => v * 0.8f).ToArray(),
        G = frame.G.Select(v => v * 0.8f).ToArray(),
        B = frame.B.Select(v => v * 0.8f).ToArray(),
        A = frame.A?.ToArray(),
        IsSceneReferred = frame.IsSceneReferred,
    };

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

    private static byte[] RenderGraph(World world, NodeGraph graph, int step, int number, bool sixteen,
                                      GraphCache? cache = null, string? focus = null,
                                      IReadOnlyDictionary<string, FloatFrame>? sources = null, bool pooled = true)
    {
        var inputs = new GraphInputs
        {
            Sources = sources ?? world.Sources,
            Cache = cache,
            Focus = focus,
            Data = new Dictionary<PassNeed, FloatFrame?>
            {
                [PassNeed.Depth] = world.Depth,
                [PassNeed.Motion] = world.Motion,
                [PassNeed.Normal] = world.Normal,
            },
            View = View,
            Step = step,
            Number = number,
            Pool = pooled ? Pool : null,
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
