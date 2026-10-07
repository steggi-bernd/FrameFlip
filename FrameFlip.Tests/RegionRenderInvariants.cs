using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Der Ausschnitt beim Malen: Was <see cref="GraphEvaluator.RenderRegion"/> an seine
/// Stelle schreibt, muss Byte fuer Byte das sein, was der volle Durchgang dort schriebe -
/// und wo das nicht sicher ist, lehnt er ab und laesst das Bild unberuehrt.
/// </summary>
public static class RegionRenderInvariants
{
    private const int Width = 320, Height = 200;

    private static readonly IViewTransform View = new StandardViewTransform();

    public static void Run()
    {
        TheRegionMatchesTheWhole();
        SpatialNodesSayNo();
        ThePagePaintsOnlyTheStroke();
    }

    /// <summary>
    /// Auf der Seite: Ein Pinseltakt im Knotenmodus rechnet nur den Ausschnitt - und das
    /// Bild danach ist dasselbe wie nach einem ganzen, vollen Durchgang.
    /// </summary>
    private static void ThePagePaintsOnlyTheStroke()
    {
        Check.Group("Ausschnitt: der Pinsel auf der Seite");

        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frameflip-ausschnitt-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, "bild.png");

        const int W = 900, H = 600;
        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            bytes[i * 4] = (byte)(i % W * 255 / W);
            bytes[i * 4 + 1] = (byte)(i / W * 255 / H);
            bytes[i * 4 + 2] = 140;
            bytes[i * 4 + 3] = 255;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(
            W, H, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bytes, W * 4)));
        using (var file = System.IO.File.Create(path)) encoder.Save(file);

        var page = new Views.AtelierPage(Decoding.FrameDecoderRegistry.CreateDefault(() => null), new Configuration.AppSettings(), _ => { });
        var window = new System.Windows.Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        byte[] Shown()
        {
            var surface = (System.Windows.Media.Imaging.WriteableBitmap)typeof(Views.AtelierPage).GetField("_surface", flags)!.GetValue(page)!;
            var pixels = new byte[surface.PixelWidth * surface.PixelHeight * 4];
            surface.CopyPixels(pixels, surface.PixelWidth * 4, 0);
            return pixels;
        }

        void Pump(double seconds)
        {
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Thread.Sleep(5);
            }
        }

        try
        {
            window.Show();
            page.Open(path);

            var size = (System.Windows.Controls.TextBlock)page.FindName("SourceText");
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < until && size.Text.Length == 0) Pump(0.05);

            page.ConvertToNodes();
            Pump(0.4);

            var mask = (PaintedMask)typeof(Views.AtelierPage).GetMethod("MakeNodeMask", flags)!.Invoke(page, null)!;
            Pump(0.6);

            // Die neue Maskenebene ist neutral - erst eine Belichtung macht einen Strich sichtbar.
            var graph = page.Graph!;
            var maskNode = graph.Nodes.OfType<Imaging.Nodes.MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var mix = graph.Nodes.OfType<Imaging.Nodes.MixNode>().Single(m => graph.Into(m.Id, "Faktor")?.From == maskNode.Id);
            var grade = (Imaging.Nodes.LayerGradeNode)graph.Find(graph.Into(mix.Id, "Oben")!.From)!;
            grade.Adjustments = new Imaging.ImageAdjustments { Exposure = -1.5 };

            // Der Pinsel ist gewaehlt und haelt die Maske - wie beim Malen.
            ((Views.ToolColumn)page.FindName("MouseTools")).Select(Views.AtelierTool.Brush, notify: true);
            Pump(0.3);

            // Ein ganzes, scharfes Bild als Ausgangspunkt - wie nach dem Waehlen des Pinsels.
            typeof(Views.AtelierPage).GetMethod("Refresh", flags, new[] { typeof(bool), typeof(bool) })!.Invoke(page, new object[] { false, false });

            var adorner = (Views.PlacementAdorner)page.FindName("Placement");
            var touched = typeof(Views.PlacementAdorner).GetMethod("Touched", flags, new[] { typeof(PaintBounds) })!;
            var dragFrame = typeof(Views.AtelierPage).GetMethod("OnDragFrame", flags)!;
            var painted = typeof(Views.AtelierPage).GetMethod("OnPainted", flags)!;
            var coarse = typeof(Views.AtelierPage).GetField("_coarse", flags)!;

            var random = new Random(9);
            bool regional = true, same = true, visible = true;

            Check.That(ReferenceEquals(adorner.GetType().GetField("_mask", flags)!.GetValue(adorner), mask),
                       "der Pinsel haelt die Maske der neuen Maskenebene");

            for (int round = 0; round < 4; round++)
            {
                byte[] before = Shown();

                var stroke = new PaintStroke { Radius = 25 + random.Next(30), Flow = 0.8f };
                var bounds = stroke.Begin(mask, random.Next(W), random.Next(H));
                bounds = bounds.Union(stroke.To(mask, random.Next(W), random.Next(H)));

                touched.Invoke(adorner, new object[] { bounds });
                painted.Invoke(page, new object[] { true });
                dragFrame.Invoke(page, new object?[] { null, EventArgs.Empty });

                // Kein grobes Bild: Der Takt hat nur den Ausschnitt gerechnet.
                regional &= !(bool)coarse.GetValue(page)!;

                byte[] region = Shown();
                visible &= !region.AsSpan().SequenceEqual(before);

                typeof(Views.AtelierPage).GetMethod("Refresh", flags, new[] { typeof(bool), typeof(bool) })!.Invoke(page, new object[] { false, false });
                same &= region.AsSpan().SequenceEqual(Shown());
            }

            typeof(Views.AtelierPage).GetMethod("StopDragFrames", flags)!.Invoke(page, null);

            // Ein ganzer Strich bis zum Loslassen: viele Mausmeldungen, ein Schritt im
            // Verlauf - und der letzte Teil, fuer den kein Takt mehr kam, steht beim
            // Loslassen trotzdem scharf im Bild. Das ganze Bild wartet, bis der Pinsel ruht.
            var undo = (System.Collections.IList)typeof(Views.AtelierPage).GetField("_undo", flags)!.GetValue(page)!;
            int steps = undo.Count;

            var last = new PaintStroke { Radius = 40, Flow = 0.9f };
            touched.Invoke(adorner, new object[] { last.Begin(mask, W / 3f, H / 2f) });

            for (int i = 1; i <= 20; i++)
            {
                touched.Invoke(adorner, new object[] { last.To(mask, W / 3f + i * 12, H / 2f + i * 3) });
                painted.Invoke(page, new object[] { true });
                if (i % 5 == 0) dragFrame.Invoke(page, new object?[] { null, EventArgs.Empty });
            }

            Check.That(undo.Count == steps, "waehrend des Strichs entsteht kein Schritt im Verlauf", $"{steps} -> {undo.Count}");

            touched.Invoke(adorner, new object[] { last.To(mask, W / 3f + 300, H / 2f + 90) });
            mask.Keep();
            painted.Invoke(page, new object[] { false });

            Check.That(undo.Count == steps + 1, "beim Loslassen genau einer", $"{steps} -> {undo.Count}");
            Check.That(page.SettlingAfterPainting && !(bool)coarse.GetValue(page)!,
                       "das ganze Bild wartet, bis der Pinsel ruht - kein Stillstand zwischen zwei Strichen");

            byte[] released = Shown();
            typeof(Views.AtelierPage).GetMethod("Refresh", flags, new[] { typeof(bool), typeof(bool) })!.Invoke(page, new object[] { false, false });

            Check.That(released.AsSpan().SequenceEqual(Shown()),
                       "und das Bild beim Loslassen ist schon das ganze, mit dem letzten Stueck des Strichs");

            Pump(1.0);
            Check.That(!page.SettlingAfterPainting, "ruht der Pinsel, kommt das ganze Bild nach");

            // Fiel ein Takt auf das grobe Bild zurueck, kommt das scharfe gleich beim Loslassen.
            typeof(Views.AtelierPage).GetField("_regionFailed", flags)!.SetValue(page, true);
            painted.Invoke(page, new object[] { true });
            painted.Invoke(page, new object[] { false });

            Check.That(!page.SettlingAfterPainting && !(bool)coarse.GetValue(page)!,
                       "nach einem groben Takt rechnet das Loslassen sofort scharf");

            Check.That(visible, "jeder Strich aendert das Bild schon waehrend des Malens");
            Check.That(regional, "ein Pinseltakt rechnet nur den Ausschnitt - kein grobes ganzes Bild");
            Check.That(same, "und das Bild danach ist Byte fuer Byte das eines ganzen, vollen Durchgangs");
        }
        finally
        {
            window.Close();
            try { System.IO.Directory.Delete(folder, recursive: true); } catch (System.IO.IOException) { }
        }
    }

    private static void TheRegionMatchesTheWhole()
    {
        Check.Group("Ausschnitt: dasselbe wie das ganze Bild");

        var sources = Sources();
        var random = new Random(11);

        // Gewaehlt ist die Maske, das Mischen, das sie begrenzt, oder die Ausgabe - von dort
        // aus muss der Ausschnitt jeweils anderes selbst rechnen. Und mit Vignette und Korn
        // in der Maskenebene und am Ende: Sie brauchen den Ort, aber keine Nachbarn.
        foreach (var (chosen, optics) in new[] { ("mask", false), ("mix", false), ("output", false), ("mask", true), ("output", true) })
        {
            var picture = optics
                ? new GradingStack { Optics = { new VignetteTool { Amount = -0.6f }, new GrainTool { Amount = 0.5f } } }
                : new GradingStack();
            var graph = StackToGraph.Convert(Stack(optics), Adjust(), picture);
            string label = chosen + (optics ? " mit Vignette und Korn" : "");
            var maskNode = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var mixNode = graph.Nodes.OfType<MixNode>().Single(m => graph.Into(m.Id, "Faktor")?.From == maskNode.Id);
            var paint = maskNode.Mask.PaintOn(0, Width, Height);

            string focus = chosen switch { "mask" => maskNode.Id, "mix" => mixNode.Id, _ => graph.Output!.Id };

            var cache = new GraphCache();
            var pool = new GridPool();
            var regionPool = new GridPool(sizes: 16);
            GraphInputs Inputs(GridPool? from = null) => new() { Sources = sources, View = View, Cache = cache, Pool = from ?? pool, Focus = focus };

            Stroke(paint, random, out _);
            byte[] shown = Render(graph, Inputs());

            bool all = true, used = true, changed = false;

            for (int round = 0; round < 6; round++)
            {
                Stroke(paint, random, out var touched);

                // Wie auf der Seite: gerundet, aus dem eigenen Vorrat - der dabei Felder
                // wiederverwendet, in denen noch ein frueherer Ausschnitt steht.
                var region = GraphRegion.Around(touched.X0, touched.Y0, touched.X1, touched.Y1, 2 * PaintedMask.Coarse).Snapped(32);
                byte[] before = shown.ToArray();
                bool done = RenderRegion(graph, Inputs(regionPool), region, shown);
                changed |= !shown.AsSpan().SequenceEqual(before);

                // Die Wahrheit ohne Zwischenspeicher: der volle Durchgang.
                byte[] whole = Render(graph, new GraphInputs { Sources = sources, View = View });

                used &= done;
                all &= shown.AsSpan().SequenceEqual(whole);

                // Wie beim Malen: Ein ganzes Bild kommt erst, wenn der Strich zu Ende ist.
                if (round % 3 == 2) shown = Render(graph, Inputs());
            }

            Check.That(used && changed, $"gewaehlt: {label} - der Ausschnitt wird gerechnet und aendert das Bild");
            Check.That(all, $"gewaehlt: {label} - Ausschnitt ins alte Bild geschrieben ist Byte fuer Byte das ganze neue");

            // Und der Zwischenspeicher hat unter den Ausschnitten nicht gelitten.
            Check.That(Render(graph, Inputs()).AsSpan().SequenceEqual(Render(graph, new GraphInputs { Sources = sources, View = View })),
                       $"gewaehlt: {label} - danach rechnet der volle Durchgang mit demselben Zwischenspeicher richtig");
        }
    }

    private static void SpatialNodesSayNo()
    {
        Check.Group("Ausschnitt: nur wo er genau ist");

        var sources = Sources();
        var random = new Random(5);

        NodeGraph With(GradingStack picture) => StackToGraph.Convert(Stack(), Adjust(), picture);

        (string What, NodeGraph Graph)[] cases =
        {
            ("eine Schaerfe hinter der Maske", With(new GradingStack { Local = { new SharpenTool { Amount = 0.8f } } })),
        };

        foreach (var (what, graph) in cases)
        {
            var maskNode = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var paint = maskNode.Mask.PaintOn(0, Width, Height);
            var inputs = new GraphInputs { Sources = sources, View = View, Cache = new GraphCache(), Pool = new GridPool(), Focus = maskNode.Id };

            Stroke(paint, random, out _);
            byte[] shown = Render(graph, inputs);
            byte[] before = shown.ToArray();

            Stroke(paint, random, out var touched);
            bool done = RenderRegion(graph, inputs, GraphRegion.Around(touched.X0, touched.Y0, touched.X1, touched.Y1, 8), shown);

            Check.That(!done && shown.AsSpan().SequenceEqual(before), $"{what}: abgelehnt, das Bild bleibt unberuehrt");
        }

        // Ohne gewaehlten Knoten gibt es nichts Gemerktes, auf das ein Ausschnitt bauen koennte.
        var plain = StackToGraph.Convert(Stack(), Adjust(), new GradingStack());
        var noFocus = new GraphInputs { Sources = sources, View = View, Cache = new GraphCache(), Pool = new GridPool() };
        byte[] image = Render(plain, noFocus);

        Check.That(!RenderRegion(plain, noFocus, new GraphRegion(10, 10, 60, 60), image), "ohne gewaehlten Knoten: abgelehnt");
        var focused = new GraphInputs { Sources = sources, View = View, Cache = new GraphCache(), Pool = new GridPool(), Focus = plain.Output!.Id };
        Check.That(!RenderRegion(plain, focused, new GraphRegion(-50, -50, -10, -10), image),
                   "ein Rechteck neben dem Bild: abgelehnt");
    }

    /// <summary>Ein zufaelliger Strich auf der Maske - und was er beruehrt hat.</summary>
    private static void Stroke(PaintedMask paint, Random random, out PaintBounds touched)
    {
        var stroke = new PaintStroke
        {
            Radius = 6 + random.Next(30),
            Flow = 0.3f + (float)random.NextDouble() * 0.7f,
            Hardness = (float)random.NextDouble(),
            Erase = random.Next(4) == 0,
        };

        touched = stroke.Begin(paint, random.Next(Width), random.Next(Height));
        for (int i = 0; i < 5; i++) touched = touched.Union(stroke.To(paint, random.Next(Width), random.Next(Height)));
    }

    private static LayerStack Stack(bool optics = false) => new()
    {
        Layers =
        {
            new ImageLayer { Content = LayerContent.Pass, Source = "" },
            new ImageLayer { Content = LayerContent.Image, Source = "bild.png", FollowSequence = false, Mode = BlendMode.Screen },
            new ImageLayer
            {
                Content = LayerContent.Adjustment,
                Mode = BlendMode.Normal,
                Adjustments = new ImageAdjustments { Exposure = -1.2, Saturation = 0.4 },
                Tools = optics ? new GradingStack { Optics = { new VignetteTool { Amount = 0.4f } } } : new GradingStack(),
                Mask = new LayerMask { Kind = MaskKind.Painted },
            },
        },
    };

    private static ImageAdjustments Adjust() => new() { Exposure = 0.3, Contrast = 1.1 };

    private static Dictionary<string, FloatFrame> Sources()
    {
        FloatFrame Frame(bool scene, Func<int, int, float> v) => new()
        {
            Width = Width,
            Height = Height,
            R = Enumerable.Range(0, Width * Height).Select(i => v(i % Width, i / Width)).ToArray(),
            G = Enumerable.Range(0, Width * Height).Select(i => 0.5f * v(i % Width, i / Width)).ToArray(),
            B = Enumerable.Range(0, Width * Height).Select(i => 0.2f + 0.1f * (i % 7)).ToArray(),
            A = Enumerable.Range(0, Width * Height).Select(i => i % Width < 6 ? 0.4f : 1f).ToArray(),
            IsSceneReferred = scene,
        };

        return new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            [""] = Frame(true, (x, y) => 0.1f + 1.5f * x / Width),
            ["bild.png"] = Frame(false, (x, y) => (float)y / Height),
        };
    }

    private static byte[] Render(NodeGraph graph, GraphInputs inputs)
    {
        var pixels = new byte[Width * Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            GraphEvaluator.Render(graph, inputs, buffer, Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    /// <summary>Schreibt den Ausschnitt in ein vorhandenes Bild - wie auf die Leinwand der Seite.</summary>
    private static bool RenderRegion(NodeGraph graph, GraphInputs inputs, GraphRegion region, byte[] pixels)
    {
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            Marshal.Copy(pixels, 0, buffer, pixels.Length);
            bool done = GraphEvaluator.RenderRegion(graph, inputs, region, buffer, Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
            return done;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
