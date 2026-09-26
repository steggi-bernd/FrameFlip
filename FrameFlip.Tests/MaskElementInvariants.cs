using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Masken als eigene Elemente (docs/Projekte-und-Masken.md, Punkt 3 und 4): loesen,
/// duplizieren, verbinden, und was eine Maske zeigt als eigene Ebene ausschneiden.
///
/// Der Massstab beim Ausschneiden ist das Bild selbst: Ohne weitere Aenderung muss es
/// Byte fuer Byte dasselbe bleiben - auch an weichen Maskenraendern, mit einer Mischart
/// darunter und mit einer Maske, die nirgends steckt.
/// </summary>
public static class MaskElementInvariants
{
    private const int Width = 240, Height = 160;

    private static readonly IViewTransform View = new StandardViewTransform();

    public static void Run()
    {
        TheCutoutKeepsThePicture();
        TheCutoutIsALayerOfItsOwn();
        TheCutoutPaintsInRegions();
        MasksKeepTheirId();
        TheGraphShowsMasks();
        ThePageHandlesMasks();
    }

    /// <summary>
    /// Masken im Graphen (Punkt 11): Maskenkabel erkennbar, ein Schild am Mischen mit Maske,
    /// "frei" und "-> n Ebenen" am Maskenknoten, und eine gewaehlte Maske hebt ihre Ebenen hervor.
    /// </summary>
    private static void TheGraphShowsMasks()
    {
        Check.Group("Masken: im Graphen sichtbar");

        string T(string key) => Localization.Strings.T(key);

        var graph = Graph(BlendMode.Normal);
        var mask = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
        var layer = LayerOf(graph, mask);
        var editor = new NodeEditor { Graph = graph, Translate = T, Title = NodeTitles.For, MaskTitle = NodeTitles.MaskName };

        var faktor = graph.Links.Single(l => l.From == mask.Id && l.Input == "Faktor");
        var image = graph.Into(layer.Id, "Oben")!;

        Check.That(MaskUse.CarriesMask(graph, faktor) && !MaskUse.CarriesMask(graph, image) &&
                   graph.Links.Where(l => l.To == mask.Id).All(l => !MaskUse.CarriesMask(graph, l)),
                   "das Kabel in den Faktor traegt eine Maske - das Bild der Ebene und was in die Maske fliesst nicht");

        Check.That(editor.ChipOf(layer) is var (source, name) && source == mask && name == T("S_MaskPainted"),
                   "das Mischen zeigt ein Schild mit seiner Maske und ihrem Namen", editor.ChipOf(layer)?.Name);
        Check.That(editor.ChipOf(graph.Nodes.OfType<MixNode>().First(m => m != layer)) is null, "ein Mischen ohne Maske keines");

        Check.That(editor.TagOf(mask) is null, "eine Maske an genau einer Ebene braucht kein Schild");

        var (_, cutMix) = LayerEdits.AddCutout(graph, layer, layer, "Bild", mask, "Maske")!.Value;
        Check.That(editor.TagOf(mask) == string.Format(T("S_NodeMaskShared"), 2),
                   "begrenzt sie eine Ebene und schneidet eine zweite aus: zwei Ebenen", editor.TagOf(mask));

        var marked = editor.MarkedBy(mask);
        Check.That(marked.Count == 2 && marked.Contains(layer) && marked.Contains(cutMix) && editor.MarkedBy(layer).Count == 0,
                   "gewaehlt hebt sie beide hervor - ein gewaehltes Mischen hebt nichts hervor");

        var copy = (MaskNode)NodeEdits.Duplicate(graph, mask)!;
        Check.That(editor.TagOf(copy) == T("S_NodeMaskFree") && MaskUse.FreeMasks(graph).SequenceEqual(new[] { copy }),
                   "eine Maske, die nirgends steckt, ist frei", editor.TagOf(copy));

        mask.Label = "Himmel";
        Check.That(editor.ChipOf(layer)?.Name == "Himmel", "hat die Maske einen Namen, steht er im Schild");
    }

    /// <summary>
    /// Ausschneiden an der Maske einer Ebene und mit einer freien Maske oben: vorher und
    /// nachher dasselbe Bild. Die Masken sind weich gemalt - an ihrem Rand verdoppelte eine
    /// zweite Kopie der Ebene sie.
    /// </summary>
    private static void TheCutoutKeepsThePicture()
    {
        Check.Group("Masken: Ausschneiden laesst das Bild, wie es ist");

        var sources = Sources();

        foreach (var mode in new[] { BlendMode.Normal, BlendMode.Screen, BlendMode.Multiply })
        {
            var graph = Graph(mode);
            var mask = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
            var layer = LayerOf(graph, mask);
            SoftStrokes(mask.Mask.PaintOn(0, Width, Height), new Random(3));

            byte[] before = Render(graph, sources);
            var made = LayerEdits.AddCutout(graph, layer, layer, "Bild", mask, "Maske");
            byte[] after = Render(graph, sources);

            Check.That(made is not null && graph.Into(made.Value.Mix.Id, "Unten")?.From == layer.Id &&
                       graph.Into(made.Value.Cutout.Id, "Bild")?.From == layer.Id && graph.Into(made.Value.Cutout.Id, "Maske")?.From == mask.Id,
                       $"{mode}: die neue Ebene liegt direkt ueber der Ebene der Maske und schneidet aus, was dort zu sehen ist");
            Check.That(before.AsSpan().SequenceEqual(after), $"{mode}: das Bild bleibt Byte fuer Byte gleich - auch am weichen Rand",
                       Differences(before, after));
            Check.That(graph.Links.Any(l => l.From == mask.Id && l.To == layer.Id && l.Input == "Faktor"),
                       $"{mode}: die Originalebene behaelt ihre Maske");
        }

        // Eine freie Maske oben auf den Ebenen.
        {
            var graph = Graph(BlendMode.Screen);
            var free = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Painted } });
            SoftStrokes(free.Mask.PaintOn(0, Width, Height), new Random(8));

            var top = NodeEdits.LayerTop(graph)!;
            byte[] before = Render(graph, sources);
            var made = LayerEdits.AddCutout(graph, top, top, NodeEdits.Through(top).Output!, free, "Maske");

            Check.That(made is not null && before.AsSpan().SequenceEqual(Render(graph, sources)),
                       "eine freie Maske oben auf den Ebenen: das Bild bleibt gleich");
        }
    }

    /// <summary>
    /// Die ausgeschnittene Ebene wirkt fuer sich: Aendert man sie, aendert sich das Bild nur,
    /// wo die Maske ist. Ohne Maske schneidet der Knoten nichts aus.
    /// </summary>
    private static void TheCutoutIsALayerOfItsOwn()
    {
        Check.Group("Masken: die ausgeschnittene Ebene ist eine eigene");

        var sources = Sources();
        var graph = Graph(BlendMode.Normal);
        var mask = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
        var layer = LayerOf(graph, mask);

        // Links gemalt, rechts nichts.
        var paint = mask.Mask.PaintOn(0, Width, Height);
        var stroke = new PaintStroke { Radius = 14, Flow = 1f, Hardness = 0.3f };
        stroke.Begin(paint, 30, 30);
        stroke.To(paint, 50, 130);
        paint.Keep();

        byte[] before = Render(graph, sources);
        var (cutout, mix) = LayerEdits.AddCutout(graph, layer, layer, "Bild", mask, "Maske")!.Value;
        mix.Mode = BlendMode.Multiply;
        byte[] after = Render(graph, sources);

        bool rightSame = true, leftChanged = false;

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int at = (y * Width + x) * 4;
                bool same = before[at] == after[at] && before[at + 1] == after[at + 1] && before[at + 2] == after[at + 2];

                if (x >= 110) rightSame &= same;
                else if (!same) leftChanged = true;
            }

        Check.That(leftChanged, "eine andere Mischart der neuen Ebene aendert das Bild, wo die Maske ist");
        Check.That(rightSame, "und nur dort - wo die Maske nichts traegt, bleibt es");

        // Ohne Maske schneidet der Knoten nichts aus: Die Ebene deckt ueberall.
        graph.Links.RemoveAll(l => l.To == cutout.Id && l.Input == "Maske");
        byte[] whole = Render(graph, sources);
        bool rightChanged = false;

        for (int y = 0; y < Height && !rightChanged; y++)
            for (int x = 110; x < Width && !rightChanged; x++)
                rightChanged = before[(y * Width + x) * 4] != whole[(y * Width + x) * 4];

        Check.That(rightChanged, "ohne Maske bleibt das Bild ganz - die Ebene deckt dann ueberall");

        // Und der Knoten ueberlebt Speichern und Laden.
        var loaded = NodeGraph.Load(graph.Save());
        Check.That(loaded?.Nodes.OfType<CutoutNode>().Count() == 1 && loaded.Nodes.OfType<CutoutNode>().Single().Id == cutout.Id,
                   "der Ausschneiden-Knoten ueberlebt Speichern und Laden");
    }

    /// <summary>
    /// Malen an einer Maske, die auch ausschneidet: Der Ausschnitt beim Malen ist Byte fuer
    /// Byte das ganze Bild - der Ausschneiden-Knoten steht auf der Liste der sicheren.
    /// </summary>
    private static void TheCutoutPaintsInRegions()
    {
        Check.Group("Masken: Malen mit Ausschnitt an einer ausgeschnittenen Ebene");

        var sources = Sources();
        var graph = Graph(BlendMode.Screen);
        var mask = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Painted);
        var layer = LayerOf(graph, mask);
        var (_, mix) = LayerEdits.AddCutout(graph, layer, layer, "Bild", mask, "Maske")!.Value;
        mix.Mode = BlendMode.Screen;

        var paint = mask.Mask.PaintOn(0, Width, Height);
        var random = new Random(21);
        var inputs = new GraphInputs { Sources = sources, View = View, Cache = new GraphCache(), Pool = new GridPool(), Focus = mask.Id };

        SoftStrokes(paint, random);
        byte[] shown = Render(graph, inputs);
        bool all = true, used = true;

        for (int round = 0; round < 4; round++)
        {
            var stroke = new PaintStroke { Radius = 8 + random.Next(20), Flow = 0.7f, Hardness = 0.2f };
            var touched = stroke.Begin(paint, random.Next(Width), random.Next(Height));
            touched = touched.Union(stroke.To(paint, random.Next(Width), random.Next(Height)));

            var region = GraphRegion.Around(touched.X0, touched.Y0, touched.X1, touched.Y1, 2 * PaintedMask.Coarse).Snapped(32);
            used &= RenderRegion(graph, inputs, region, shown);
            all &= shown.AsSpan().SequenceEqual(Render(graph, sources));
        }

        Check.That(used, "der Ausschnitt wird gerechnet");
        Check.That(all, "und ist Byte fuer Byte das ganze Bild");
    }

    /// <summary>Die Kennung einer Maske: vergeben, wenn jemand fragt, beim Kopieren behalten, beim Speichern auch.</summary>
    private static void MasksKeepTheirId()
    {
        Check.Group("Masken: die feste Kennung");

        var mask = new LayerMask { Kind = MaskKind.Painted };
        Check.That(mask.Id.Length == 0, "eine alte Maske hat noch keine");

        string id = mask.EnsureId();
        Check.That(id.Length > 0 && mask.EnsureId() == id, "sie bekommt eine, wenn jemand fragt - und behaelt sie");
        Check.That(mask.Clone().Id == id, "eine Kopie fuer den Export behaelt sie");

        var graph = new NodeGraph();
        graph.Add(new MaskNode { Mask = mask });
        Check.That(NodeGraph.Load(graph.Save())?.Nodes.OfType<MaskNode>().Single().Mask.Id == id, "und sie ueberlebt Speichern und Laden");
    }

    /// <summary>
    /// Auf der Seite, an der kleinen Kryptomattendatei der Probe: das Menue einer Maske,
    /// Ausschneiden, Duplizieren, Verbinden, Loesen - und "Objekt hier als Ebene" aus dem
    /// Bildmenue. Jeder Handgriff ist ein Schritt im Verlauf.
    /// </summary>
    private static void ThePageHandlesMasks()
    {
        Check.Group("Masken: Handgriffe auf der Seite");

        string T(string key) => Localization.Strings.T(key);

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-masken-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        byte[] Shown()
        {
            typeof(AtelierPage).GetMethod("Refresh", flags, new[] { typeof(bool), typeof(bool) })!.Invoke(page, new object[] { false, false });

            var display = (Image)page.FindName("Display");
            if (display.Source is not BitmapSource source) return Array.Empty<byte>();

            var pixels = new byte[source.PixelWidth * 4 * source.PixelHeight];
            source.CopyPixels(pixels, source.PixelWidth * 4, 0);
            return pixels;
        }

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "die Datei wird geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(0.4), () => false);
            page.ConvertToNodes();
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Nodes, notify: true);
            Pump(TimeSpan.FromSeconds(0.4), () => false);

            var sets = (IReadOnlyList<Decoding.Exr.CryptomatteSet>)typeof(AtelierPage).GetField("_cryptomattes", flags)!.GetValue(page)!;
            var frame = (FloatFrame)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page)!;
            var undo = (System.Collections.IList)typeof(AtelierPage).GetField("_undo", flags)!.GetValue(page)!;

            // Ein Objekt als Maskenebene, mit einer Belichtung, damit die Maske etwas tut.
            (int X, int Y)? spot = null;

            for (int y = 0; y < frame.Height && spot is null; y += 3)
                for (int x = 0; x < frame.Width && spot is null; x += 3)
                    if (page.MaskObjectAt(sets[0], x, y)) spot = (x, y);

            var graph = page.Graph!;
            var mask = graph.Nodes.OfType<MaskNode>().Last(m => m.Mask.Kind == MaskKind.Cryptomatte);
            var layer = page.LayersOf(mask).Single();
            var grade = (LayerGradeNode)graph.Find(graph.Into(layer.Id, "Oben")!.From)!;
            grade.Adjustments = new ImageAdjustments { Exposure = -1.5 };
            typeof(AtelierPage).GetMethod("OnGraphChanged", flags)!.Invoke(page, null);
            Pump(TimeSpan.FromSeconds(0.4), () => false);

            // Das Menue der Maske.
            page.ShowNodeMenu(mask, new Point(10, 10));
            Check.That(new[] { "S_MaskMenuExtract", "S_MaskMenuDetach", "S_MaskMenuDuplicate" }.All(k => page.NodeMenu!.Items.Contains(T(k))) &&
                       !page.NodeMenu!.Items.Contains(T("S_MaskMenuConnect")),
                       "das Menue einer Maske: ausschneiden, loesen, duplizieren - verbinden erst, wenn es eine zweite Ebene gibt",
                       string.Join(", ", page.NodeMenu!.Items));
            page.NodeMenu.Close();

            // Ausschneiden.
            byte[] before = Shown();
            int steps = undo.Count;
            int mixes = graph.Nodes.OfType<MixNode>().Count();
            var cut = page.ExtractLayer(mask);
            byte[] after = Shown();

            Check.That(cut is not null && graph.Nodes.OfType<CutoutNode>().Count() == 1 && graph.Nodes.OfType<MixNode>().Count() == mixes + 1 &&
                       graph.Into(cut.Id, "Unten")?.From == layer.Id && page.LayersOf(mask).Contains(layer) && undo.Count == steps + 1,
                       "Ausschneiden: eine neue Ebene direkt ueber der der Maske, die ihre Maske behaelt - ein Schritt im Verlauf");
            Check.That(before.Length > 0 && before.AsSpan().SequenceEqual(after), "und das Bild bleibt, wie es war", Differences(before, after));

            page.StepNodes(back: true);
            Check.That(page.Graph!.Nodes.OfType<CutoutNode>().Count() == 0, "Rueckgaengig nimmt sie wieder weg");

            graph = page.Graph!;
            mask = graph.Nodes.OfType<MaskNode>().Last(m => m.Mask.Kind == MaskKind.Cryptomatte);
            layer = page.LayersOf(mask).Single();

            // Duplizieren: eine freie Kopie mit neuer Kennung.
            string id = mask.Mask.EnsureId();
            var copy = page.DuplicateMask(mask);

            Check.That(copy is not null && copy.Mask.Id.Length > 0 && copy.Mask.Id != id && page.LayersOf(copy).Count == 0 &&
                       page.LayersOf(mask).Single() == layer,
                       "Duplizieren: eine Kopie mit eigener Kennung, frei - die Ebene behaelt das Original");

            // Die Liste: die freie Kopie im Abschnitt "Masken", die Ebene der gewaehlten Maske hervorgehoben.
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var editor = (NodeEditor)page.FindName("NodeView");
            editor.Select(mask);
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            Check.That(list.ShownMasks.Count == 1 && list.ShownMasks[0].Mask == copy,
                       "die Ebenenliste zeigt die freie Kopie im Abschnitt Masken", string.Join(", ", list.ShownMasks.Select(m => m.Name)));
            Check.That(list.MarkedRows.Count == 1 && list.MarkedRows[0].Mix == layer,
                       "und hebt die Ebene der gewaehlten Maske hervor");

            typeof(AtelierPage).GetMethod("ShowFreeMaskMenu", flags)!.Invoke(page, new object[] { copy! });
            Check.That(new[] { "S_LayerMenuShowInGraph", "S_MaskMenuExtract", "S_MaskMenuDuplicate", "S_HubDelete" }
                           .All(k => page.LayerMenu!.Items.Contains(T(k))) && !page.LayerMenu!.Items.Contains(T("S_MaskMenuDetach")),
                       "das Menue einer freien Maske in der Liste: zeigen, ausschneiden, duplizieren, loeschen - loesen nicht",
                       string.Join(", ", page.LayerMenu!.Items));
            page.LayerMenu.Close();

            // Verbinden: die Kopie an eine zweite Ebene.
            page.MaskObjectAt(sets[0], spot!.Value.X, spot.Value.Y);
            graph = page.Graph!;
            var second = graph.Nodes.OfType<MixNode>().Where(m => m != layer).Last(m => graph.Into(m.Id, "Faktor") is not null);

            page.ShowNodeMenu(copy!, new Point(10, 10));
            Check.That(page.NodeMenu!.Invoke(T("S_MaskMenuConnect")) && page.NodeMenu!.Items.Any(i => i.Contains(NodeTitles.For(second))),
                       "Verbinden fragt, mit welcher Ebene", string.Join(", ", page.NodeMenu!.Items));
            page.NodeMenu.Close();

            page.ConnectMask(copy!, second);
            Check.That(page.LayersOf(copy!).SequenceEqual(new[] { second }), "danach begrenzt die Kopie diese Ebene");

            page.ConnectMask(mask, second);
            Check.That(page.LayersOf(mask).Count == 2 && page.LayersOf(copy!).Count == 0,
                       "eine Maske an zwei Ebenen - die vorige an der zweiten ist frei");

            // Loesen.
            steps = undo.Count;
            page.DetachMask(mask);
            Check.That(page.LayersOf(mask).Count == 0 && graph.Nodes.Contains(mask) && undo.Count == steps + 1,
                       "Loesen: die Maske steht frei, die Ebenen wirken ueberall - ein Schritt im Verlauf");

            // Objekt hier als Ebene - aus dem Bildmenue.
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Move, notify: true);
            Pump(TimeSpan.FromSeconds(0.3), () => false);

            page.ShowPictureMenu(spot.Value.X, spot.Value.Y);
            Check.That(page.PictureMenu!.Items.Contains(Localization.Strings.T("S_PicMenuObjectLayer", sets[0].ShortName)),
                       "das Bildmenue im Knotenmodus: Objekt hier als Ebene", string.Join(", ", page.PictureMenu.Items));
            page.PictureMenu.Close();

            before = Shown();
            steps = undo.Count;
            int masks = graph.Nodes.OfType<MaskNode>().Count();

            bool made = page.ObjectAsLayerAt(sets[0], spot.Value.X, spot.Value.Y);
            after = Shown();

            var objectMask = graph.Nodes.OfType<MaskNode>().Last();
            var objectCut = graph.Nodes.OfType<CutoutNode>().SingleOrDefault(c => graph.Into(c.Id, "Maske")?.From == objectMask.Id);
            var objectMix = objectCut is null ? null : graph.Links.Where(l => l.From == objectCut.Id).Select(l => graph.Find(l.To)).OfType<MixNode>().FirstOrDefault();

            Check.That(made && graph.Nodes.OfType<MaskNode>().Count() == masks + 1 && objectMask.Mask.Kind == MaskKind.Cryptomatte &&
                       objectMask.Mask.Picks.Count == 1 && objectMask.Mask.Id.Length > 0 && objectMix?.Label == objectMask.Mask.Picks[0].Name,
                       "Objekt hier als Ebene: eine Kryptomatte fuer genau dieses Objekt, ausgeschnitten und nach ihm benannt", objectMix?.Label);
            Check.That(undo.Count == steps + 1, "ein einziger Schritt im Verlauf", $"{steps} -> {undo.Count}");
            Check.That(before.AsSpan().SequenceEqual(after), "und das Bild bleibt, wie es war", Differences(before, after));
            Check.That(!page.ObjectAsLayerAt(sets[0], -5, -5), "neben dem Bild: nichts");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Die Datei, ein Bild daruebergemischt und eine Einstellungsebene mit gemalter Maske -
    /// mit der Mischart <paramref name="mode"/>.
    /// </summary>
    private static NodeGraph Graph(BlendMode mode) => StackToGraph.Convert(new LayerStack
    {
        Layers =
        {
            new ImageLayer { Content = LayerContent.Pass, Source = "" },
            new ImageLayer { Content = LayerContent.Image, Source = "bild.png", FollowSequence = false, Mode = BlendMode.Screen, Opacity = 0.7f },
            new ImageLayer
            {
                Content = LayerContent.Adjustment,
                Mode = mode,
                Adjustments = new ImageAdjustments { Exposure = -1.2, Saturation = 0.4 },
                Mask = new LayerMask { Kind = MaskKind.Painted },
            },
        },
    }, new ImageAdjustments { Exposure = 0.3, Contrast = 1.1 }, new GradingStack());

    private static MixNode LayerOf(NodeGraph graph, MaskNode mask)
        => graph.Nodes.OfType<MixNode>().Single(m => graph.Into(m.Id, "Faktor")?.From == mask.Id);

    /// <summary>Weiche, halb deckende Striche - Raender, an denen eine Verdopplung auffiele.</summary>
    private static void SoftStrokes(PaintedMask paint, Random random)
    {
        for (int i = 0; i < 4; i++)
        {
            var stroke = new PaintStroke { Radius = 10 + random.Next(25), Flow = 0.4f + 0.4f * (float)random.NextDouble(), Hardness = 0.1f };
            stroke.Begin(paint, random.Next(Width), random.Next(Height));
            for (int step = 0; step < 3; step++) stroke.To(paint, random.Next(Width), random.Next(Height));
        }

        paint.Keep();
    }

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

    private static byte[] Render(NodeGraph graph, Dictionary<string, FloatFrame> sources)
        => Render(graph, new GraphInputs { Sources = sources, View = View });

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

    /// <summary>Wie viele Bytes abweichen, und wie weit hoechstens - fuer die Meldung.</summary>
    private static string Differences(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return $"Laenge {a.Length} gegen {b.Length}";

        int count = 0, most = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            if (d == 0) continue;
            count++;
            most = Math.Max(most, d);
        }

        return $"{count} Bytes abweichend, hoechstens um {most}";
    }

    private static bool Pump(TimeSpan timeout, Func<bool> until)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end)
        {
            if (until()) return true;

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(5);
        }

        return until();
    }
}
