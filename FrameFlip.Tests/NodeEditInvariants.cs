using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Am Graphen bauen: verbinden, trennen, einfuegen, loeschen, rueckgaengig machen.
///
/// Die Pruefungen haben einen Massstab, der nicht erfunden ist: den Stapel. Einen
/// Effekt aus dem Graphen zu loeschen muss dasselbe Bild ergeben wie der Stapel ohne
/// ihn - Byte fuer Byte. Einen einzufuegen dasselbe wie der Stapel mit ihm. So prueft
/// die Probe nicht nur, dass ein Kabel irgendwo steckt, sondern dass das Bild stimmt.
/// </summary>
public static class NodeEditInvariants
{
    private const int Width = 40;
    private const int Height = 28;

    private static readonly IViewTransform View = new StandardViewTransform();

    public static void Run()
    {
        var sources = Sources();

        TheChecksSayNo(sources);
        RemovingClosesTheGap(sources);
        InsertingMatchesTheStack(sources);
        TheEditorWires(sources);
        DuplicatingKeepsTheInputs(sources);
        ThePageBuildsAndUndoes();
        ThePageWiresPassesAndPicks();
    }

    // ------------------------------------------------------------ Modell

    private static void TheChecksSayNo(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: was nicht passt, wird nicht verbunden");

        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());

        var mix = graph.Nodes.OfType<MixNode>().Last();
        var place = graph.Nodes.OfType<PlaceNode>().First();
        var light = graph.Nodes.OfType<LightNode>().Single();
        var tone = graph.Nodes.OfType<ToneNode>().Single();
        var mask = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Luminance } });

        Check.That(NodeEdits.CannotConnect(graph, mask, "Maske", mix, "Oben") == "S_NodeWhyType",
                   "eine Maske ist kein Bild");
        Check.That(NodeEdits.CannotConnect(graph, mix, "Bild", place, "Bild") == "S_NodeWhySource",
                   "Platzieren braucht ein gelesenes Bild, kein gerechnetes");
        Check.That(NodeEdits.CannotConnect(graph, tone, "Bild", light, "Bild") == "S_NodeWhyLoop",
                   "ein Kreis wird abgelehnt");
        Check.That(NodeEdits.CannotConnect(graph, light, "Bild", light, "Bild") == "S_NodeWhySelf",
                   "ein Knoten speist sich nicht selbst");
        // Ein Bild, das vor dem Mischen steht - hinter ihm waere es ein Kreis.
        Check.That(NodeEdits.CannotConnect(graph, place, "Bild", mix, "Faktor") is null,
                   "ein Bild darf als Maske dienen - mit seiner Helligkeit");
        Check.That(NodeEdits.CannotConnect(graph, light, "Bild", mix, "Faktor") == "S_NodeWhyLoop",
                   "aber nicht eines, das erst hinter dem Mischen entsteht");

        Check.That(NodeEdits.Connect(graph, mask, "Maske", mix, "Faktor") &&
                   graph.Into(mix.Id, "Faktor")?.From == mask.Id,
                   "was passt, wird verbunden");

        var second = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient } });
        NodeEdits.Connect(graph, second, "Maske", mix, "Faktor");

        Check.That(graph.Links.Count(l => l.To == mix.Id && l.Input == "Faktor") == 1 &&
                   graph.Into(mix.Id, "Faktor")?.From == second.Id,
                   "ein Eingang nimmt genau ein Kabel - das neue ersetzt das alte");

        Check.That(!NodeEdits.Remove(graph, graph.Output!), "die Ausgabe laesst sich nicht loeschen");
        Check.That(graph.Problems().Count == 0, "und der Graph bleibt rechenbar", string.Join("; ", graph.Problems()));
    }

    private static void RemovingClosesTheGap(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: Loeschen schliesst die Luecke");

        var with = StackToGraph.Convert(Stack(), Adjust(), Vignette());
        var without = StackToGraph.Convert(Stack(), Adjust(), new GradingStack());

        var vignette = with.Nodes.OfType<OpticsNode>().Single();

        Check.That(NodeEdits.Remove(with, vignette), "der Effekt laesst sich herausnehmen");
        Check.That(with.Problems().Count == 0, "und der Graph bleibt rechenbar");
        Check.That(Render(with, sources).AsSpan().SequenceEqual(Render(without, sources)),
                   "das Bild ist dasselbe wie der Stapel ohne ihn - Byte fuer Byte");
    }

    private static void InsertingMatchesTheStack(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: Einfuegen ergibt das Bild des Stapels");

        var expected = Render(StackToGraph.Convert(Stack(), Adjust(), Vignette()), sources);

        // Hinter einen Knoten gesetzt - der Weg der Palette.
        var graph = StackToGraph.Convert(Stack(), Adjust(), new GradingStack());
        var light = graph.Nodes.OfType<LightNode>().Single();
        var node = new OpticsNode { Tool = new VignetteTool { Amount = -0.6f } };

        Check.That(NodeEdits.InsertAfter(graph, light, node), "hinter einen Knoten gesetzt");
        Check.That(Render(graph, sources).AsSpan().SequenceEqual(expected),
                   "rechnet es wie der Stapel mit dem Effekt");

        // Auf ein Kabel gelegt - der Weg, wenn man einen freien Knoten auf eine Verbindung zieht.
        var again = StackToGraph.Convert(Stack(), Adjust(), new GradingStack());
        var view = again.Nodes.OfType<ViewNode>().Single();
        var link = again.Into(view.Id, "Bild")!;
        var dropped = again.Add(new OpticsNode { Tool = new VignetteTool { Amount = -0.6f } });

        Check.That(NodeEdits.InsertInto(again, link, dropped), "auf ein Kabel gelegt");
        Check.That(Render(again, sources).AsSpan().SequenceEqual(expected), "ebenso");

        // Platz machen: Was dahinter kommt, rueckt nach rechts.
        var tone = again.Nodes.OfType<ToneNode>().Single();
        var output = again.Output!;

        dropped.X = 100;
        dropped.Y = 50;
        tone.X = 300;
        tone.Y = 50;
        output.X = 50;
        output.Y = 50;

        NodeEdits.MakeRoom(again, dropped, 240);

        Check.That(Math.Abs(tone.X - 540) < 1e-6, "was dahinter und rechts davon liegt, rueckt zur Seite", $"{tone.X}");
        Check.That(Math.Abs(output.X - 50) < 1e-6, "was links davon liegt, bleibt stehen", $"{output.X}");
    }

    // ------------------------------------------------------------ Editor

    private static void TheEditorWires(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: der Editor verbindet, loest und lehnt ab");

        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());
        var editor = new NodeEditor { Graph = graph, Translate = key => key };
        var window = Window(editor);

        try
        {
            editor.Frame();
            editor.UpdateLayout();

            int editing = 0, changed = 0;
            editor.Editing += () => editing++;
            editor.GraphChanged += () => changed++;

            var light = graph.Nodes.OfType<LightNode>().Single();
            var view = graph.Nodes.OfType<ViewNode>().Single();
            var mix = graph.Nodes.OfType<MixNode>().Last();

            var output = editor.ScreenOf(light, "Bild", input: false);

            Check.That(editor.SocketAt(output) is { Input: false } hit && ReferenceEquals(hit.Node, light),
                       "ein Anschluss wird an seiner Stelle getroffen");

            // Ein Kabel am Eingang packen und ins Leere fallen lassen: getrennt.
            var empty = new Point(editor.ActualWidth - 5, editor.ActualHeight - 5);

            Check.That(editor.BeginWire(editor.ScreenOf(view, "Bild", input: true)), "am Eingang gepackt");
            editor.FinishWire(empty);

            Check.That(graph.Into(view.Id, "Bild") is null, "ins Leere fallen gelassen, ist die Verbindung weg");
            Check.That(editing == 1 && changed == 1, "mit einem Stand fuer Rueckgaengig davor", $"{editing}/{changed}");

            // Rueckwaerts: vom freien Eingang zu einem Ausgang.
            editor.BeginWire(editor.ScreenOf(view, "Bild", input: true));
            editor.FinishWire(output);

            Check.That(graph.Into(view.Id, "Bild")?.From == light.Id, "vom Eingang zum Ausgang verbindet es auch");

            // Eine Maske an einen Bildeingang: abgelehnt, mit Grund, nichts geaendert.
            var mask = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient } });
            mask.X = light.X;
            mask.Y = light.Y + 200;
            editor.InvalidateVisual();

            int links = graph.Links.Count;

            editor.BeginWire(editor.ScreenOf(mask, "Maske", input: false));
            editor.FinishWire(editor.ScreenOf(mix, "Oben", input: true));

            Check.That(graph.Links.Count == links && editor.Warning == "S_NodeWhyType",
                       "was nicht passt, wird abgelehnt - und der Grund steht da", editor.Warning);

            editor.BeginWire(editor.ScreenOf(mask, "Maske", input: false));
            editor.FinishWire(editor.ScreenOf(mix, "Faktor", input: true));

            Check.That(graph.Into(mix.Id, "Faktor")?.From == mask.Id, "was passt, wird angesteckt");

            // Loeschen im Editor: die Luecke schliesst sich.
            var tone = graph.Nodes.OfType<ToneNode>().Single();
            var after = graph.Links.First(l => l.From == tone.Id).To;

            editor.Remove(tone);

            Check.That(graph.Find(tone.Id) is null && graph.Links.Any(l => l.From == view.Id && l.To == after),
                       "geloescht, und das Bild fliesst am Knoten vorbei weiter");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DuplicatingKeepsTheInputs(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: verdoppeln");

        var graph = StackToGraph.Convert(Stack(), Adjust(), Vignette());
        var before = Render(graph, sources);

        var vignette = graph.Nodes.OfType<OpticsNode>().Single();
        var copy = NodeEdits.Duplicate(graph, vignette);

        Check.That(copy is OpticsNode { Tool: VignetteTool { Amount: -0.6f } } c && !ReferenceEquals(c.Tool, vignette.Tool),
                   "die Kopie hat dieselbe Einstellung - in einem eigenen Werkzeug");
        Check.That(copy!.Id != vignette.Id && graph.Problems().Count == 0, "und eine eigene Kennung",
                   string.Join("; ", graph.Problems()));

        var inputs = graph.Links.Where(l => l.To == vignette.Id).Select(l => (l.From, l.Output, l.Input)).ToList();
        var copied = graph.Links.Where(l => l.To == copy.Id).Select(l => (l.From, l.Output, l.Input)).ToList();

        Check.That(inputs.Count > 0 && inputs.SequenceEqual(copied), "sie liest dasselbe wie das Original");
        Check.That(!graph.Links.Any(l => l.From == copy.Id), "aber niemand liest sie");
        Check.That(Render(graph, sources).AsSpan().SequenceEqual(before), "und das Bild bleibt, wie es war");

        ((VignetteTool)vignette.Tool!).Amount = 0.3f;
        Check.That(copy is OpticsNode { Tool: VignetteTool { Amount: -0.6f } }, "das Original zu aendern laesst die Kopie in Ruhe");

        Check.That(NodeEdits.Duplicate(graph, graph.Output!) is null &&
                   NodeEdits.Duplicate(graph, graph.Nodes.OfType<RenderNode>().Single()) is null,
                   "Ausgabe und Datei gibt es nur einmal");
    }

    // ------------------------------------------------------------ Seite

    /// <summary>
    /// Passe und Kryptomatten auf der Seite - an der kleinen Kryptomattendatei der
    /// Probe, nicht an einem echten Projekt.
    /// </summary>
    private static void ThePageWiresPassesAndPicks()
    {
        Check.Group("Knoten bauen: Passe, Kryptomatte und Verdoppeln auf der Seite");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-knotenpass-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = Window(page);

        try
        {
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "die Datei wird geladen");
                return;
            }

            Settle();

            page.ConvertToNodes();
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Nodes, notify: true);
            Settle();

            var editor = (NodeEditor)page.FindName("NodeView");
            var colour = (GradingPanel)page.FindName("Tools");
            byte[] plain = Pixels(page);

            var passes = (IReadOnlyList<(string Name, string Label)>)Call(page, "NodePasses")!;

            Check.That(passes.Any(p => p.Label == "DiffCol") && passes.Any(p => p.Label == "Mist"),
                       "die Passe der Datei stehen zur Wahl", string.Join(", ", passes.Select(p => p.Label)));
            Check.That(!passes.Any(p => p.Label.StartsWith("Crypto", StringComparison.Ordinal)),
                       "die Stufen der Kryptomatten nicht - sie sind Kennungen, kein Licht");

            // Ein Pass als Ebene: Datei, Platzieren, Mischen in einem Griff.
            string diffuse = passes.First(p => p.Label == "DiffCol").Name;

            editor.Select(null);
            Call(page, "AddPassLayer", diffuse);
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(plain));

            var file = page.Graph!.Nodes.OfType<RenderNode>().Single();

            Check.That(file.Passes.Contains(diffuse) && editor.Selected is MixNode { Mode: BlendMode.Add } mix &&
                       page.Graph.Into(mix.Id, "Oben") is { } over && page.Graph.Find(over.From) is PlaceNode,
                       "Pass als Ebene legt einen Ausgang, Platzieren und Mischen auf Addieren an");
            Check.That(!Pixels(page).AsSpan().SequenceEqual(plain), "und das Bild wird heller");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(plain));

            Check.That(!page.Graph!.Nodes.OfType<RenderNode>().Single().Passes.Contains(diffuse) &&
                       Pixels(page).AsSpan().SequenceEqual(plain),
                       "Rueckgaengig nimmt alles wieder heraus");

            // Ein Pass als Ausgang, am Schalter der Datei.
            editor.Select(page.Graph.Nodes.OfType<RenderNode>().Single());

            var fields = (StackPanel)colour.FindName("NodeFields");
            var mist = fields.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()
                             .FirstOrDefault(t => (string)t.Content == "Mist");

            Check.That(mist is { IsChecked: false }, "die Datei zeigt ihre Passe als Schalter");

            if (mist is not null)
            {
                mist.IsChecked = true;
                mist.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Settle();
            }

            string mistName = passes.First(p => p.Label == "Mist").Name;

            Check.That(page.Graph!.Nodes.OfType<RenderNode>().Single().Output(mistName) is not null,
                       "eingeschaltet wird der Pass ein Ausgang");
            Check.That(Pixels(page).AsSpan().SequenceEqual(plain), "der nichts aendert, solange kein Kabel steckt");

            page.StepNodes(back: true);

            Check.That(page.Graph!.Nodes.OfType<RenderNode>().Single().Output(mistName) is null,
                       "und auch das laesst sich zuruecknehmen");

            // Kryptomatte: waehlen durch Klicken, mit Rueckgaengig.
            var sets = (IReadOnlyList<Decoding.Exr.CryptomatteSet>)Field(page, "_cryptomattes")!;
            var all = (IReadOnlyList<Decoding.Exr.ExrPass>)Field(page, "_passes")!;
            var set = sets.First(s => s.ShortName == "CryptoObject");

            var crypto = new MaskNode
            {
                Mask = new LayerMask
                {
                    Kind = MaskKind.Cryptomatte,
                    Source = set.Prefix,
                    Levels = Decoding.Exr.Cryptomatte.Levels(all, set.Prefix).ToList(),
                },
            };

            Call(page, "Place", crypto, new Point(0, 0));

            // Die Stufen kommen nach - gewaehlt werden kann, sobald die unterste da ist.
            var loaded = (System.Collections.IDictionary)Field(page, "_sources")!;
            Pump(TimeSpan.FromSeconds(3), () => loaded.Contains(crypto.Mask.Levels[0]));

            Check.That(ReferenceEquals(editor.Selected, crypto), "der Kryptomatte-Knoten steht da und ist gewaehlt");

            int px = -1, py = -1;

            for (int y = 0; y < CryptoSample.Height && px < 0; y++)
                for (int x = 0; x < CryptoSample.Width && px < 0; x++)
                    if (page.PickAt(x, y)) (px, py) = (x, y);

            var picked = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Cryptomatte);

            Check.That(px >= 0 && picked.Mask.Picks is [{ Name.Length: > 0 }],
                       "ein Klick ins Bild nimmt das Objekt dort auf - mit Namen",
                       string.Join(", ", picked.Mask.Picks.Select(p => p.Name)));

            var info = fields.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Check.That(info.Any(t => t.Contains(picked.Mask.Picks.FirstOrDefault()?.Name ?? "\u0000")),
                       "und der Streifen nennt es", string.Join(" | ", info));

            if (px >= 0) page.PickAt(px, py);

            Check.That(picked.Mask.Picks.Count == 0, "ein zweiter Klick auf dasselbe nimmt es wieder heraus");

            page.StepNodes(back: true);

            // Ohne Wahl davor naehme Rueckgaengig den Knoten selbst weg - deshalb kein Single.
            Check.That(page.Graph!.Nodes.OfType<MaskNode>().FirstOrDefault(m => m.Mask.Kind == MaskKind.Cryptomatte)?.Mask.Picks.Count == 1,
                       "Rueckgaengig holt die Wahl zurueck");

            // Verdoppeln auf der Seite: Umschalt+D gibt es im Editor, hier der Weg dahinter.
            // Dass das Bild dabei bleibt, prueft das Modell oben.
            var light = page.Graph!.Nodes.OfType<LightNode>().Single();

            editor.Duplicate(light);

            Check.That(page.Graph.Nodes.OfType<LightNode>().Count() == 2 && editor.Selected is LightNode chosen &&
                       !ReferenceEquals(chosen, light),
                       "verdoppelt, und die Kopie ist gewaehlt");

            page.StepNodes(back: true);

            Check.That(page.Graph!.Nodes.OfType<LightNode>().Count() == 1, "und Rueckgaengig nimmt sie wieder weg");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static object? Call(object target, string method, params object[] arguments)
        => target.GetType()
                 .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                 .Invoke(target, arguments);

    private static object? Field(object target, string name)
        => target.GetType()
                 .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                 .GetValue(target);

    private static void ThePageBuildsAndUndoes()
    {
        Check.Group("Knoten bauen: Palette, Rueckgaengig und eine leere Ausgabe auf der Seite");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-knotenbau-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = Window(page);

        try
        {
            page.Open(Png(Path.Combine(folder, "bild_0003.png")));

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Settle();

            page.ConvertToNodes();
            ((ToolColumn)page.FindName("MouseTools")).Select(AtelierTool.Nodes, notify: true);
            Settle();

            var editor = (NodeEditor)page.FindName("NodeView");
            var colour = (GradingPanel)page.FindName("Tools");
            byte[] plain = Pixels(page);

            // Die Palette setzt den Effekt hinter den gewaehlten Knoten.
            editor.Select(page.Graph!.Nodes.OfType<LightNode>().Single());
            Invoke(page, "InsertFromPalette", "Vignette");
            Settle();

            var vignette = page.Graph!.Nodes.OfType<OpticsNode>().SingleOrDefault();

            Check.That(vignette is not null && ReferenceEquals(editor.Selected, vignette),
                       "ein Klick auf die Palette setzt den Effekt als Knoten ein und waehlt ihn");
            Check.That(((Slider)colour.FindName("VignetteSlider")).IsVisible, "und seine Karte steht da");

            ((Slider)colour.FindName("VignetteSlider")).Value = -1;
            Settle();

            byte[] darker = Pixels(page);
            Check.That(!darker.AsSpan().SequenceEqual(plain), "die Karte wirkt auf den neuen Knoten");

            page.StepNodes(back: true);
            Settle();

            Check.That(!page.Graph!.Nodes.OfType<OpticsNode>().Any(), "Rueckgaengig nimmt ihn wieder heraus");
            Check.That(Pixels(page).AsSpan().SequenceEqual(plain), "und das Bild ist wieder das von vorher");

            page.StepNodes(back: false);
            Settle();

            Check.That(page.Graph!.Nodes.OfType<OpticsNode>().Single().Tool is VignetteTool { Amount: < -0.9f },
                       "Wiederholen holt ihn zurueck - samt Einstellung");
            Check.That(Pixels(page).AsSpan().SequenceEqual(darker), "und das Bild dazu");

            // Die Ausgabe abziehen: kein Bild, und der Editor sagt es.
            var output = page.Graph.Output!;
            editor.UpdateLayout();
            editor.BeginWire(editor.ScreenOf(output, "Bild", input: true));
            editor.FinishWire(new Point(4, editor.ActualHeight - 4));
            Settle();

            Check.That(editor.Warning is { Length: > 0 }, "ohne verbundene Ausgabe steht ein Hinweis da");
            Check.That(Pixels(page).All(b => b == 0), "und das Bild ist leer - nicht der alte Stapel");

            page.StepNodes(back: true);
            Settle();

            Check.That(editor.Warning is null && Pixels(page).AsSpan().SequenceEqual(darker),
                       "Rueckgaengig steckt sie wieder an");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------ Hilfsmittel

    private static LayerStack Stack() => new()
    {
        Layers =
        {
            new ImageLayer { Content = LayerContent.Pass, Source = "" },
            new ImageLayer { Content = LayerContent.Image, Source = "bild.png", FollowSequence = false, Mode = BlendMode.Screen },
        },
    };

    private static ImageAdjustments Adjust() => new() { Exposure = 0.3, Contrast = 1.1 };

    private static GradingStack Vignette() => new() { Optics = { new VignetteTool { Amount = -0.6f } } };

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
    {
        var pixels = new byte[Width * Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            GraphEvaluator.Render(graph, new GraphInputs { Sources = sources, View = View }, buffer, Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static Window Window(UIElement content)
    {
        var window = new Window
        {
            Content = content,
            Width = 1100,
            Height = 750,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        window.Show();
        return window;
    }

    private static void Invoke(object target, string method, params object[] arguments)
        => target.GetType()
                 .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                 .Invoke(target, arguments);

    private static byte[] Pixels(AtelierPage page)
    {
        var display = (System.Windows.Controls.Image)page.FindName("Display");
        if (display.Source is not BitmapSource source) return Array.Empty<byte>();

        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        return pixels;
    }

    private static void Settle() => Pump(TimeSpan.FromSeconds(0.6), () => false);

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

    private static string Png(string path)
    {
        int stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (int i = 0; i < Width * Height; i++)
        {
            pixels[i * 4] = (byte)(i * 7 % 256);
            pixels[i * 4 + 1] = (byte)(i * 3 % 256);
            pixels[i * 4 + 2] = 200;
            pixels[i * 4 + 3] = 255;
        }

        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);

        return path;
    }
}
