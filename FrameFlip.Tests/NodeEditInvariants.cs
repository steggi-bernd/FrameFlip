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
        DroppingFallsIntoWires(sources);
        DuplicatingKeepsTheInputs(sources);
        ThePageBuildsAndUndoes();
        ThePageWiresPassesAndPicks();
        ThePageFetchesHiddenLayers();
        ThePageSurvivesWiring();
        ThePageHubAndMenus();
    }

    /// <summary>
    /// Der Hub statt des alten Menues, und das Menue einer Zeile in der Ebenenliste - auf
    /// der Seite, an der kleinen Kryptomattendatei der Probe.
    /// </summary>
    private static void ThePageHubAndMenus()
    {
        Check.Group("Knoten bauen: der Hub und das Menue einer Ebene");

        string T(string key) => Localization.Strings.T(key);

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-hub-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
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
            editor.Frame();
            editor.UpdateLayout();

            // Ein Rechtsklick auf das Kabel in die Ausgabe: Der Hub sagt, wo Neues hinkommt,
            // und die Suche findet die Vignette.
            var output = page.Graph!.Output!;
            var wire = page.Graph.Into(output.Id, "Bild")!;
            var from = page.Graph.Find(wire.From)!;
            var middle = new Point((editor.ScreenOf(from, wire.Output, input: false).X + editor.ScreenOf(output, "Bild", input: true).X) / 2,
                                   (editor.ScreenOf(from, wire.Output, input: false).Y + editor.ScreenOf(output, "Bild", input: true).Y) / 2);

            editor.Select(null);
            page.ShowNodeHub(editor.ToGraph(middle));
            var hub = page.Hub;

            Check.That(hub is not null && editor.NodeAt(middle) is null && editor.LinkAt(middle) == wire,
                       "Rechtsklick auf ein Kabel oeffnet den Hub - dort, wo nur das Kabel ist");

            hub!.Search("vignet");
            Check.That(hub.Visible.Count == 1 && hub.Visible[0].Title == T("S_Vignette"), "die Suche findet die Vignette",
                       string.Join(", ", hub.Visible.Select(v => v.Title)));

            hub.ActivateHighlighted();
            Settle();

            var vignette = page.Graph!.Nodes.OfType<OpticsNode>().FirstOrDefault(n => n.Tool is VignetteTool);

            Check.That(page.Hub is null && vignette is not null &&
                       page.Graph.Into(vignette.Id, "Bild")?.From == from.Id && page.Graph.Into(output.Id, "Bild")?.From == vignette.Id,
                       "Enter setzt sie ein - ins Kabel, auf dem der Rechtsklick war, und der Hub geht zu");

            page.StepNodes(back: true);
            Settle();

            // Ebenen: die Passe der Datei - Klick als Ebene, Umschalt+Klick als Maske.
            var passes = (IReadOnlyList<(string Name, string Label)>)Call(page, "NodePasses")!;
            page.ShowNodeHub(editor.ToGraph(new Point(40, 40)), "layers");
            hub = page.Hub!;

            var tiles = hub.Visible.Where(v => passes.Any(p => p.Label == v.Title) && v.Alternate is not null).ToList();
            Check.That(hub.Category == "layers" && tiles.Count == passes.Count,
                       "bei den Ebenen stehen alle Passe der Datei als Kacheln", $"{tiles.Count} von {passes.Count}");

            int mixes = page.Graph!.Nodes.OfType<MixNode>().Count();
            tiles[0].Activate();
            Settle();

            Check.That(page.Graph!.Nodes.OfType<MixNode>().Count() == mixes + 1, "ein Klick holt den Pass als Ebene");

            page.ShowNodeHub(editor.ToGraph(new Point(40, 40)), "layers");
            var second = page.Hub!.Visible.First(v => v.Title == passes[^1].Label && v.Alternate is not null);
            second.Alternate!();
            Settle();

            Check.That(page.Graph!.Nodes.OfType<MaskNode>().Any(m => m.Mask.Kind == MaskKind.Pass && m.Mask.Source == passes[^1].Name),
                       "Umschalt+Klick holt ihn als Maske");

            page.ShowNodeHub(editor.ToGraph(new Point(40, 40)));
            var recent = page.Hub!.CategoryByKey("recent");

            Check.That(recent is not null && recent.Groups[0].Entries.Take(2).Select(e => e.Title)
                                                  .SequenceEqual(new[] { passes[^1].Label, passes[0].Label }),
                       "unter Zuletzt steht, was zuletzt genommen wurde - das Neueste zuerst",
                       string.Join(", ", recent?.Groups[0].Entries.Select(e => e.Title) ?? Array.Empty<string>()));

            page.Hub!.Choose(recent);
            Check.That(page.Hub.Category == "recent", "und der Hub oeffnet sie");

            // Die Leiste unten wirkt auf den gewaehlten Knoten.
            var light = page.Graph!.Nodes.OfType<LightNode>().FirstOrDefault() ?? (Node)page.Graph.Nodes.OfType<ViewNode>().First();
            editor.Select(light);
            page.ShowNodeHub(editor.ToGraph(new Point(40, 40)));
            page.Hub!.Actions.First(a => a.Label == T("S_HubMute")).Act();
            Settle();

            Check.That(light.Muted, "Stumm in der Leiste schaltet den gewaehlten Knoten stumm");

            light.Muted = false;

            // Das Menue einer Zeile in der Ebenenliste.
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var top = list.Shown.First(l => l.Mix is not null);

            Call(page, "ShowLayerMenu", top);
            var menu = page.LayerMenu!;

            Check.That(new[] { "S_LayerMenuRename", "S_LayerMenuVisible", "S_LayerMenuShowInGraph", "S_LayerMenuViewAlone",
                               "S_DuplicateLayer", "S_RemoveLayer", "S_LayerMenuClip" }.All(k => menu.Items.Contains(T(k))),
                       "das Menue einer Ebene bietet an, was sich mit ihr tun laesst", string.Join(", ", menu.Items));

            menu.Invoke(T("S_LayerMenuRename"));
            menu.Commit("Glanz");

            Check.That(top.Mix!.Label == "Glanz" && list.Shown.Any(l => l.Name == "Glanz"), "Umbenennen gibt dem Mischen den Namen",
                       top.Mix.Label);

            mixes = page.Graph!.Nodes.OfType<MixNode>().Count();
            Call(page, "ShowLayerMenu", list.Shown.First(l => l.Mix is not null));
            page.LayerMenu!.Invoke(T("S_DuplicateLayer"));
            Settle();

            Check.That(page.Graph!.Nodes.OfType<MixNode>().Count() == mixes + 1, "Verdoppeln verdoppelt die Ebene samt Zweig");

            Call(page, "ShowLayerMenu", list.Shown.First(l => l.Mix is not null));
            page.LayerMenu!.Invoke(T("S_LayerMenuViewAlone"));
            Settle();

            var viewed = ((NodeEditor)page.FindName("NodeView")).Viewed;
            Check.That(viewed is not null && page.Graph.Links.Any(l => l.From == viewed.Value.Node.Id && l.To == list.Shown.First(s => s.Mix is not null).Mix!.Id && l.Input == "Oben"),
                       "Allein im Betrachter zeigt, was in das Mischen oben hineinfliesst");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Kabel ziehen wie jemand, der ausprobiert: jeder Knoten an sich selbst, dann
    /// zufaellig von irgendeinem Anschluss zu irgendeinem - auf der Seite, mit Rechnen,
    /// Vorschauen und Ebenenliste dahinter. Nichts davon darf eine Ausnahme werfen.
    /// </summary>
    private static void ThePageSurvivesWiring()
    {
        Check.Group("Knoten bauen: Ausprobieren bringt die Seite nicht zum Absturz");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-kabel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = Window(page);
        var crashes = new List<string>();

        void Caught(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            crashes.Add(e.Exception.GetType().Name + ": " + e.Exception.Message);
            e.Handled = true;
        }

        Dispatcher.CurrentDispatcher.UnhandledException += Caught;

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
            var passes = (IReadOnlyList<(string Name, string Label)>)Call(page, "NodePasses")!;

            // Etwas zum Verbinden: zwei Passe als Ebenen und eine Maske.
            foreach (var (name, _) in passes.Take(2))
            {
                editor.Select(null);
                Call(page, "AddPassLayer", name, null!);
            }

            Settle();
            editor.Frame();
            editor.UpdateLayout();

            void Step(Action act, string what)
            {
                try
                {
                    act();
                    Pump(TimeSpan.FromMilliseconds(60), () => false);
                }
                catch (Exception e)
                {
                    var inner = e is System.Reflection.TargetInvocationException { InnerException: { } real } ? real : e;
                    string where = inner.StackTrace?.Split(Environment.NewLine).FirstOrDefault()?.Trim() ?? "";
                    crashes.Add($"{what}: {inner.GetType().Name}: {inner.Message} @ {where}");
                }
            }

            // Jeder Knoten an sich selbst - in beide Richtungen.
            int links = page.Graph!.Links.Count;

            foreach (var node in page.Graph.Nodes.ToList())
            {
                foreach (var output in node.Outputs)
                {
                    foreach (var input in node.Inputs)
                    {
                        Step(() =>
                        {
                            if (editor.BeginWire(editor.ScreenOf(node, output.Name, input: false)))
                                editor.FinishWire(editor.ScreenOf(node, input.Name, input: true));
                        }, $"{node.GetType().Name}.{output.Name} an sich selbst");

                        Step(() =>
                        {
                            if (editor.BeginWire(editor.ScreenOf(node, input.Name, input: true)))
                                editor.FinishWire(editor.ScreenOf(node, output.Name, input: false));
                        }, $"{node.GetType().Name}.{input.Name} rueckwaerts an sich selbst");
                    }
                }
            }

            Check.That(crashes.Count == 0, "jeder Knoten an sich selbst - keine Ausnahme", string.Join(" | ", crashes.Take(3)));

            // Wer ein Kabel am eigenen Eingang packt und es auf den eigenen Ausgang fallen
            // laesst, hat danach nicht weniger Kabel: Ein Anschluss, der nicht passt, legt
            // das Kabel zurueck. Nur ins Leere geworfen ist es weg.
            Check.That(page.Graph!.Links.Count == links, "an sich selbst gesteckt, geht auch kein Kabel verloren",
                       $"{links} vorher, {page.Graph.Links.Count} nachher");
            Check.That(page.Graph!.Order() is not null && !page.Graph.Links.Any(l => l.From == l.To),
                       "und keine Verbindung eines Knotens mit sich selbst",
                       string.Join(", ", page.Graph.Links.Where(l => l.From == l.To).Select(l => $"{l.From}.{l.Output}->{l.Input}")));

            // Und wenn doch einmal das Rechnen wirft - hier ein Pass, dessen Bild kuerzer ist,
            // als es sagt, wie nach einer halb gelesenen Datei: Der Fehler steht im Editor,
            // und die Seite rechnet weiter, sobald er weg ist. Dass es wirklich wirft, prueft
            // erst das Modell.
            var sourcesOnPage = (Dictionary<string, FloatFrame>)Field(page, "_sources")!;
            string passKey = sourcesOnPage.Keys.First(k => k.Length > 0 && page.Graph!.Nodes.OfType<RenderNode>().Single().Passes.Contains(k) &&
                                                           page.Graph.Links.Any(l => l.Output == k));
            var intact = sourcesOnPage[passKey];
            var torn = new FloatFrame
            {
                Width = intact.Width,
                Height = intact.Height,
                R = new float[7],
                G = new float[7],
                B = new float[7],
                IsSceneReferred = intact.IsSceneReferred,
            };

            var probeSources = new Dictionary<string, FloatFrame>(sourcesOnPage, StringComparer.Ordinal) { [passKey] = torn };
            var probeBuffer = Marshal.AllocHGlobal(sourcesOnPage[""].Width * sourcesOnPage[""].Height * 4);
            bool throws = false;

            try
            {
                GraphEvaluator.Render(page.Graph!, new GraphInputs { Sources = probeSources, View = View }, probeBuffer, sourcesOnPage[""].Width * 4);
            }
            catch (Exception)
            {
                throws = true;
            }
            finally
            {
                Marshal.FreeHGlobal(probeBuffer);
            }

            Check.That(throws, "ein zerrissenes Passbild wirft beim Rechnen - der Fall fuer das Netz");

            sourcesOnPage[passKey] = torn;
            Step(() => Call(page, "Refresh", false, false), "zerrissener Pass");
            Settle();

            Check.That(crashes.Count == 0 && editor.Warning is { } failed &&
                       failed.StartsWith(Localization.Strings.T("S_NodeWarnFailed", "").Split(':')[0], StringComparison.Ordinal),
                       "was beim Rechnen wirft, steht als Fehler im Editor - ohne dass die Seite abstuerzt",
                       editor.Warning + " | " + string.Join(" | ", crashes.Take(2)));

            sourcesOnPage[passKey] = intact;
            Step(() => Call(page, "Refresh", false, false), "Pass wieder heil");
            Settle();

            Check.That(crashes.Count == 0 && editor.Warning is null && Pixels(page).Any(b => b != 0),
                       "ist der Fehler weg, rechnet die Seite wieder, und die Meldung verschwindet", editor.Warning);

            // Dann wild: von irgendeinem Anschluss zu irgendeinem.
            var random = new Random(4711);

            for (int step = 0; step < 80 && crashes.Count == 0; step++)
            {
                var sockets = page.Graph!.Nodes
                    .SelectMany(n => n.Inputs.Select(s => (Node: n, s.Name, Input: true))
                                     .Concat(n.Outputs.Select(s => (Node: n, s.Name, Input: false))))
                    .ToList();

                var a = sockets[random.Next(sockets.Count)];
                var b = sockets[random.Next(sockets.Count)];

                Step(() =>
                {
                    if (editor.BeginWire(editor.ScreenOf(a.Node, a.Name, a.Input)))
                        editor.FinishWire(editor.ScreenOf(b.Node, b.Name, b.Input));
                }, $"{a.Node.GetType().Name}.{a.Name} -> {b.Node.GetType().Name}.{b.Name}");

                if (random.Next(8) == 0)
                    Step(() => page.StepNodes(back: true), "Rueckgaengig");
            }

            Settle();

            Check.That(crashes.Count == 0, "80 zufaellige Kabel mit Rechnen dahinter - keine Ausnahme", string.Join(" | ", crashes.Take(3)));
            Check.That(page.Graph!.Order() is not null, "und der Graph hat keinen Kreis");

        }
        finally
        {
            Dispatcher.CurrentDispatcher.UnhandledException -= Caught;
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Ein Graph aus der Zeit, als ausgeblendete Ebenen beim Umwandeln wegfielen: Die
    /// Ebenenliste sagt, was fehlt, und holt es mit einem Knopf - Bernds Glare-Ebenen.
    /// </summary>
    private static void ThePageFetchesHiddenLayers()
    {
        Check.Group("Knoten bauen: ein alter Graph holt seine ausgeblendeten Ebenen");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-glare-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string picture = Png(Path.Combine(folder, "Image0035.png"));
        string glare = Png(Path.Combine(folder, "Glare_10035.png"));

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Image", Visible = false },
                new ImageLayer { Content = LayerContent.Image, Source = glare, Name = "Glare_1", Visible = false, Mode = BlendMode.Screen },
                new ImageLayer { Content = LayerContent.Image, Source = picture, Name = "Image0035.png", FollowSequence = false },
            },
        };

        // Der Graph, wie der Umwandler ihn frueher baute: ohne die ausgeblendeten Ebenen.
        var old = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());

        foreach (var mix in old.Nodes.OfType<MixNode>().Where(m => m.Muted).ToList()) NodeEdits.Remove(old, mix, reconnect: true);

        var live = old.Order()!.Select(n => n.Id).ToHashSet();
        foreach (var node in old.Nodes.Where(n => !live.Contains(n.Id)).ToList()) NodeEdits.Remove(old, node, reconnect: false);
        foreach (var node in old.Nodes) node.Label = null;

        var settings = new AppSettings { Layers = stack, AtelierNodes = old.Save() };
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = Window(page);

        try
        {
            page.Open(picture);

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Settle();

            var list = (NodeLayerList)page.FindName("NodeLayers");

            Check.That(page.InNodes && list.Missing.SequenceEqual(new[] { "Image", "Glare_1" }),
                       "die Ebenenliste nennt die ausgeblendeten Ebenen, die dem Graphen fehlen",
                       string.Join(", ", list.Missing));

            byte[] before = Pixels(page);
            Call(page, "AdoptHiddenLayers");
            Settle();

            Check.That(list.Missing.Count == 0 && list.Shown.Any(l => l.Name == "Glare_1" && l.Mix?.Muted == true),
                       "ein Klick holt sie - stumm, mit ihrem Namen", string.Join(" | ", list.Shown.Select(l => l.Name)));
            Check.That(Pixels(page).AsSpan().SequenceEqual(before), "und das Bild bleibt, wie es war");

            page.StepNodes(back: true);
            Settle();

            Check.That(list.Missing.Count == 2, "Rueckgaengig nimmt sie wieder heraus - und der Hinweis ist wieder da");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
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

    /// <summary>
    /// Ein freier Knoten, ueber ein Kabel gezogen, faellt hinein - wie in Blender. Es
    /// reicht, dass das Kabel unter seinem Koerper hindurchlaeuft; die Titelzeile muss
    /// nicht darauf liegen.
    /// </summary>
    private static void DroppingFallsIntoWires(Dictionary<string, FloatFrame> sources)
    {
        Check.Group("Knoten bauen: ein freier Knoten faellt in ein Kabel");

        var graph = StackToGraph.Convert(Stack(), Adjust(), new GradingStack());
        var editor = new NodeEditor { Graph = graph, Translate = key => key };
        var window = Window(editor);

        try
        {
            editor.Frame();
            editor.UpdateLayout();

            // Ein Kabel, das vorwaerts laeuft - die Anordnung bricht lange Graphen in
            // Reihen um, und dann springt eines zurueck an den Anfang der naechsten.
            var forward = graph.Links.First(l => graph.Find(l.From) is PointNode from && graph.Find(l.To) is PointNode to &&
                                                 to.X > from.X + NodeEditor.NodeWidth);
            var view = graph.Find(forward.From)!;
            var tone = graph.Find(forward.To)!;

            // Die Mitte des Kabels zwischen den beiden.
            Point Middle(Node from, Node to)
            {
                var a = editor.ScreenOf(from, "Bild", input: false);
                var b = editor.ScreenOf(to, "Bild", input: true);
                return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            }

            // Knapp ueber der Unterkante des Koerpers liegt das Kabel - weit weg von der Titelzeile.
            void Over(Node node, Point middle)
            {
                var at = editor.ToGraph(middle);
                node.X = Math.Round(at.X - NodeEditor.NodeWidth / 2);
                node.Y = Math.Round(at.Y - NodeLayout.Height(node) + 6);
            }

            var middle = Middle(view, tone);
            var light = graph.Add(new LightNode { Exposure = 0.5 });
            Over(light, middle);

            var landing = editor.LandingFor(light, middle);

            Check.That(landing is { } found && found.From == view.Id && found.To == tone.Id,
                       "das Kabel unter dem Koerper ist gemeint, nicht nur eines unter der Titelzeile");

            int changed = 0;
            editor.GraphChanged += () => changed++;

            Check.That(landing is not null && editor.Land(light, landing) &&
                       graph.Into(light.Id, "Bild")?.From == view.Id && graph.Into(tone.Id, "Bild")?.From == light.Id &&
                       changed == 1,
                       "losgelassen, faellt er hinein - und der Graph meldet sich");
            Check.That(tone.X >= light.X + NodeLayout.ColumnStep - 0.5, "was dahinter liegt, rueckt so weit, dass er Platz hat",
                       $"{tone.X} gegen {light.X}");

            // Eine Maske ist kein Bild - sie faellt in kein Bildkabel.
            var mask = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient } });
            var between = Middle(light, tone);
            Over(mask, between);

            Check.That(editor.LandingFor(mask, between) is null, "eine Maske faellt in kein Bildkabel");

            // Ein Knoten, an dem schon die Tiefe steckt, hat einen freien Bildweg.
            var depth = graph.Add(new DataNode { Tool = new DepthFieldTool { Aperture = 0.5f } });
            NodeCatalog.WireData(graph, depth);
            Over(depth, between);

            Check.That(graph.Links.Any(l => l.To == depth.Id) && editor.LandingFor(depth, between) is { } withData &&
                       withData.From == light.Id,
                       "ein Knoten mit Renderdaten faellt trotzdem - sein Bildweg ist frei");

            // Einer, dessen Bild schon jemand liest, bleibt, wo er ist.
            Check.That(editor.LandingFor(light, Middle(view, light)) is null, "ein Knoten, dessen Bild schon gelesen wird, faellt nicht");
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
            Call(page, "AddPassLayer", diffuse, null!);
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(plain));

            var file = page.Graph!.Nodes.OfType<RenderNode>().Single();

            Check.That(file.Passes.Contains(diffuse) && editor.Selected is MixNode { Mode: BlendMode.Add } mix &&
                       page.Graph.Into(mix.Id, "Oben") is { } over && page.Graph.Find(over.From) is PlaceNode,
                       "Pass als Ebene legt einen Ausgang, Platzieren und Mischen auf Addieren an");
            Check.That(!Pixels(page).AsSpan().SequenceEqual(plain), "und das Bild wird heller");

            // Die Ebenenliste: oben die neue Ebene, darunter das Bild der Datei - mit Miniaturen.
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var shownPreviews = (NodePreviews)Field(page, "_previews")!;

            Check.That(list.Shown.Count == 2 && list.Shown[0].Name == "DiffCol" && list.Shown[1].Name == Localization.Strings.T("S_NodeRender"),
                       "die Ebenenliste zeigt die neue Ebene oben, benannt nach ihrem Pass",
                       string.Join(" | ", list.Shown.Select(l => l.Name)));
            Check.That(list.Shown.All(l => l.Source is not null && shownPreviews.For(l.Source.Id) is not null),
                       "und zu jeder Ebene eine Miniatur");

            var top = list.Shown[0];
            Call(page, "OnLayerChosen", top.Target!);

            Check.That(ReferenceEquals(editor.Selected, top.Mix), "ein Klick in der Liste waehlt das Mischen der Ebene");

            var ground = list.Shown[1];
            Call(page, "OnLayerChosen", ground.Target!);

            Check.That(ground.Mix is null && ground.Source is RenderNode && ReferenceEquals(editor.Selected, ground.Source),
                       "die Grundlage darunter ist die Datei selbst - ein Klick waehlt sie");

            byte[] withLayer = Pixels(page);
            Call(page, "OnLayerMuted", top.Mix!);
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(withLayer));

            Check.That(page.Graph!.Nodes.OfType<MixNode>().Any(m => m.Muted) && Pixels(page).AsSpan().SequenceEqual(plain),
                       "das Auge schaltet die Ebene stumm - das Bild ist wieder das ohne sie");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(withLayer));

            Check.That(!page.Graph!.Nodes.OfType<MixNode>().Any(m => m.Muted), "und Rueckgaengig zeigt sie wieder");

            // Ein Mischen umbenennen: der Name steht im Titel und in der Ebenenliste.
            var mixNode = list.Shown[0].Mix!;
            editor.Select(mixNode);

            var context = (NodeFieldContext)Call(page, "FieldContext")!;
            var nameField = NodeFields.For(mixNode, context).OfType<TextField>().Single();
            nameField.Set("Glare");

            var named = page.Graph!.Nodes.OfType<MixNode>().First(m => m.Id == mixNode.Id);
            var layerList = (NodeLayerList)page.FindName("NodeLayers");

            Check.That(named.Label == "Glare" && NodeTitles.For(named).StartsWith("Glare", StringComparison.Ordinal) &&
                       layerList.Shown.Any(l => l.Name == "Glare"),
                       "ein umbenanntes Mischen heisst im Titel und in der Ebenenliste so",
                       string.Join(" | ", layerList.Shown.Select(l => l.Name)));

            page.StepNodes(back: true);

            Check.That(page.Graph!.Nodes.OfType<MixNode>().All(m => m.Label is null), "und Rueckgaengig nimmt den Namen wieder weg");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(plain));

            Check.That(!page.Graph!.Nodes.OfType<RenderNode>().Single().Passes.Contains(diffuse) &&
                       Pixels(page).AsSpan().SequenceEqual(plain),
                       "Rueckgaengig nimmt alles wieder heraus");

            // Die Ebenenliste zum Arbeiten: eine Ebene und eine Einstellungsebene darueber -
            // ziehen, die Knoepfe darunter, Mischung und Deckkraft.
            editor.Select(null);
            Call(page, "AddPassLayer", diffuse, null!);
            var diffMix = (MixNode)editor.Selected!;

            Call(page, "AddAdjustmentLayer", diffMix);
            var grade = (LayerGradeNode)editor.Selected!;
            var adjustMix = page.Graph!.Nodes.OfType<MixNode>().Single(m => page.Graph.Into(m.Id, "Oben")?.From == grade.Id);

            grade.Adjustments = new ImageAdjustments { Exposure = -1.5 };
            Call(page, "Refresh", false, false);
            Settle();

            byte[] layered = Pixels(page);
            string Names() => string.Join(" | ", layerList.Shown.Select(l => l.Name));
            MixNode Mix(string id) => page.Graph!.Nodes.OfType<MixNode>().Single(m => m.Id == id);
            void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Check.That(layerList.Shown.Count == 3 && layerList.Shown[0].Mix == adjustMix && layerList.Shown[1].Mix == diffMix &&
                       !layered.AsSpan().SequenceEqual(plain),
                       "eine Einstellungsebene aus der Liste liegt ueber der gewaehlten Ebene", Names());

            // Gezogen unter den Pass: Sie dunkelt nur noch die Grundlage ab.
            Check.That(!layerList.DropOn(adjustMix, layerList.Shown[2], upper: false), "unter die Grundlage laesst sich nichts legen");
            Check.That(layerList.DropOn(adjustMix, layerList.Shown[1], upper: false), "unter die Ebene darunter schon");
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(layered));
            Settle();

            byte[] moved = Pixels(page);

            Check.That(layerList.Shown[0].Mix?.Id == diffMix.Id && layerList.Shown[1].Mix?.Id == adjustMix.Id &&
                       !moved.AsSpan().SequenceEqual(layered),
                       "gezogen liegt sie darunter - und das Bild ist ein anderes", Names());

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(layered));

            Check.That(layerList.Shown[0].Mix?.Id == adjustMix.Id && Pixels(page).AsSpan().SequenceEqual(layered),
                       "Rueckgaengig legt sie zurueck", Names());

            // Dasselbe mit dem Knopf: einen Platz tiefer.
            var buttons = layerList.Buttons;

            editor.Select(Mix(adjustMix.Id));
            Settle();

            Check.That(!buttons.Up.IsEnabled && buttons.Down.IsEnabled && buttons.Remove.IsEnabled,
                       "die oberste Ebene kann nur tiefer");

            Press(buttons.Down);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(moved));

            Check.That(Pixels(page).AsSpan().SequenceEqual(moved) && editor.Selected?.Id == adjustMix.Id,
                       "der Knopf tut dasselbe wie das Ziehen - und die Ebene bleibt gewaehlt");

            page.StepNodes(back: true);
            Settle();

            // Verdoppeln und loeschen - samt Zweig, die Datei bleibt.
            int mixes = page.Graph!.Nodes.OfType<MixNode>().Count();
            int nodes = page.Graph.Nodes.Count;

            editor.Select(Mix(diffMix.Id));
            Settle();
            Press(buttons.Duplicate);
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(layered));

            Check.That(page.Graph!.Nodes.OfType<MixNode>().Count() == mixes + 1 && page.Graph.Nodes.Count == nodes + 2 &&
                       layerList.Shown.Count == 4 && !Pixels(page).AsSpan().SequenceEqual(layered),
                       "Verdoppeln legt Mischen und Platzieren noch einmal an - der Pass kommt zweimal dazu", Names());

            page.StepNodes(back: true);
            Settle();

            editor.Select(Mix(diffMix.Id));
            Settle();
            Press(buttons.Remove);
            Settle();

            Check.That(page.Graph!.Nodes.Count == nodes - 2 && !page.Graph.Nodes.Any(n => n.Id == diffMix.Id) &&
                       page.Graph.Nodes.OfType<RenderNode>().Count() == 1 && editor.Selected is null,
                       "Loeschen nimmt Mischen und Platzieren heraus - nichts Geloeschtes bleibt gewaehlt");

            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(layered));

            Check.That(Pixels(page).AsSpan().SequenceEqual(layered), "und Rueckgaengig holt die Ebene zurueck");

            // Mischung und Deckkraft der gewaehlten Ebene, direkt in der Liste.
            editor.Select(Mix(diffMix.Id));
            Settle();

            var (modeBox, opacity, controls) = layerList.Controls;

            Check.That(controls.Visibility == Visibility.Visible && Math.Abs(opacity.Value - 1) < 1e-9 &&
                       modeBox.SelectedItem is ComboBoxItem { Tag: BlendMode.Add },
                       "die gewaehlte Ebene zeigt ihre Mischung und Deckkraft");

            modeBox.SelectedItem = modeBox.Items.OfType<ComboBoxItem>().First(i => (BlendMode)i.Tag! == BlendMode.Screen);
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(layered));
            Settle();

            byte[] screened = Pixels(page);

            Check.That(Mix(diffMix.Id).Mode == BlendMode.Screen && !screened.AsSpan().SequenceEqual(layered) &&
                       layerList.Shown[1].Detail.StartsWith(Localization.Strings.T(NodeTitles.BlendKey(BlendMode.Screen)), StringComparison.Ordinal),
                       "die Mischung wechselt am Mischen - im Bild und in der Zeile", layerList.Shown[1].Detail);

            opacity.Value = 0.5;
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(screened));
            Settle();

            Check.That(Math.Abs(Mix(diffMix.Id).Opacity - 0.5f) < 1e-4 && !Pixels(page).AsSpan().SequenceEqual(screened),
                       "die Deckkraft ebenso");

            page.StepNodes(back: true);
            page.StepNodes(back: true);
            Pump(TimeSpan.FromSeconds(3), () => Pixels(page).AsSpan().SequenceEqual(plain));

            Check.That(Pixels(page).AsSpan().SequenceEqual(plain) && !page.Graph!.Nodes.OfType<MixNode>().Any(),
                       "Rueckgaengig raeumt beide Ebenen wieder ab");

            // Ein Pass als Ausgang, am Schalter der Datei.
            editor.Select(page.Graph.Nodes.OfType<RenderNode>().Single());

            var fields = (StackPanel)colour.FindName("NodeFields");

            System.Windows.Controls.Primitives.ToggleButton? Tile(string label)
                => fields.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>()
                         .SelectMany(g => g.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>())
                         .FirstOrDefault(t => (string)t.Tag == label);

            var mist = Tile("Mist");

            Check.That(mist is { IsChecked: false }, "die Datei zeigt ihre Passe als Kacheln");

            // Die Miniaturen entstehen im Hintergrund - danach stehen sie auf den Kacheln.
            var thumbs = (System.Collections.IDictionary)Field(page, "_passThumbs")!;
            Pump(TimeSpan.FromSeconds(5), () => thumbs.Count > 0);
            Settle();

            Check.That(thumbs.Count >= passes.Count - 1 &&
                       Tile("Mist")?.Content is StackPanel { Children: [Border { Child: System.Windows.Controls.Image { Source: not null } }, ..] },
                       "und jede Kachel bekommt eine Miniatur ihres Passes", $"{thumbs.Count} von {passes.Count}");

            mist = Tile("Mist");

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

            Call(page, "PlaceAt", crypto, new Point(0, 0), null!);

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

            // Aus der Palette hereingezogen und auf das Kabel vor der Ausgabe gelegt.
            var output = page.Graph!.Output!;
            var last = page.Graph.Find(page.Graph.Into(output.Id, "Bild")!.From)!;

            editor.UpdateLayout();
            var a = editor.ScreenOf(last, NodeEdits.Through(last).Output!, input: false);
            var b = editor.ScreenOf(output, "Bild", input: true);
            byte[] beforeDrop = Pixels(page);

            editor.DropSection("Vignette", new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            Pump(TimeSpan.FromSeconds(3), () => !Pixels(page).AsSpan().SequenceEqual(beforeDrop));

            var dropped = page.Graph.Nodes.OfType<OpticsNode>().SingleOrDefault();

            Check.That(dropped is not null && page.Graph.Into(output.Id, "Bild")?.From == dropped.Id &&
                       page.Graph.Into(dropped.Id, "Bild")?.From == last.Id && ReferenceEquals(editor.Selected, dropped),
                       "ein Effekt aus der Palette, auf das Kabel gezogen, faellt hinein und ist gewaehlt");

            page.StepNodes(back: true);

            Check.That(!page.Graph!.Nodes.OfType<OpticsNode>().Any(), "und Rueckgaengig nimmt ihn wieder heraus");

            // Neben jedes Kabel gezogen, steht er frei da.
            editor.DropSection("Vignette", new Point(8, 8));

            Check.That(page.Graph!.Nodes.OfType<OpticsNode>().SingleOrDefault() is { } loose &&
                       !page.Graph.Links.Any(l => l.From == loose.Id || l.To == loose.Id),
                       "ins Leere gezogen, steht er frei");

            page.StepNodes(back: true);

            // Verdoppeln auf der Seite: Umschalt+D gibt es im Editor, hier der Weg dahinter.
            // Dass das Bild dabei bleibt, prueft das Modell oben.
            var light = page.Graph!.Nodes.OfType<LightNode>().Single();

            editor.Duplicate(light);

            Check.That(page.Graph.Nodes.OfType<LightNode>().Count() == 2 && editor.Selected is LightNode chosen &&
                       !ReferenceEquals(chosen, light),
                       "verdoppelt, und die Kopie ist gewaehlt");

            page.StepNodes(back: true);

            Check.That(page.Graph!.Nodes.OfType<LightNode>().Count() == 1, "und Rueckgaengig nimmt sie wieder weg");

            // Die neuen Knoten zeigen ihre Felder - ein Stil mit falschem Ziel fiele erst hier auf.
            var colourFields = (StackPanel)colour.FindName("NodeFields");
            var ramp = new ColorRampNode();

            foreach (var node in new Node[] { new MaskMathNode(), new MapRangeNode { Auto = false }, ramp, new MaskShapeNode() })
            {
                Call(page, "PlaceAt", node, new Point(40, 400), null!);

                Check.That(ReferenceEquals(editor.Selected, node) && colourFields.Children.Count > 1,
                           $"{node.GetType().Name}: der Knoten steht da, und seine Felder auch");

                if (node is MapRangeNode)
                    Check.That(colourFields.Children.OfType<StackPanel>().Any(p => p.Children.OfType<TextBox>().Any()),
                               "ohne eigene Spanne laesst sich Von und Bis eintippen");
            }

            editor.Select(ramp);
            var rampEditor = colourFields.Children.OfType<RampEditor>().SingleOrDefault();

            Check.That(rampEditor is not null, "der Farbverlauf zeigt seinen Verlauf");

            if (rampEditor is not null)
            {
                rampEditor.AddAt(0.5);
                bool added = ramp.Stops.Count == 3 && ReferenceEquals(rampEditor.Selected, ramp.Stops[^1]);
                rampEditor.Remove();

                Check.That(added && ramp.Stops.Count == 2, "ein Klick setzt einen Stopp, Entfernen nimmt ihn wieder heraus");
            }
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

            // Der Betrachter: das Licht vor der Vignette im grossen Bild, mit Schild und Markierung.
            var badge = (Border)page.FindName("ViewerBadge");
            var lit = page.Graph!.Nodes.OfType<LightNode>().Single();

            Call(page, "OnViewWanted", lit);
            Settle();

            Check.That(badge.Visibility == Visibility.Visible && editor.Viewed is { } shown && ReferenceEquals(shown.Node, lit) &&
                       !Pixels(page).AsSpan().SequenceEqual(darker),
                       "Strg+Umschalt+Klick zeigt den Knoten im grossen Bild - und das Schild sagt es");

            // Der Knoten hat nur einen Ausgang - der naechste Klick geht zurueck zur Ausgabe.
            Call(page, "OnViewWanted", lit);
            Settle();

            Check.That(badge.Visibility != Visibility.Visible && editor.Viewed is null && Pixels(page).AsSpan().SequenceEqual(darker),
                       "nach dem letzten Ausgang ist wieder die Ausgabe zu sehen");

            // Die Datei hat vier Ausgaenge - der Klick geht sie der Reihe nach durch.
            var file = page.Graph!.Nodes.OfType<RenderNode>().Single();
            var seen = new List<string>();

            for (int k = 0; k < 4; k++)
            {
                Call(page, "OnViewWanted", file);
                if (editor.Viewed is { } now) seen.Add(now.Output);
            }

            Call(page, "OnViewWanted", file);
            Settle();

            Check.That(seen.SequenceEqual(file.Outputs.Select(o => o.Name)) && editor.Viewed is null,
                       "bei mehreren Ausgaengen geht der Betrachter sie der Reihe nach durch", string.Join(", ", seen));

            // Aus dem Stapel neu aufbauen: der frische Graph - und Rueckgaengig holt den alten.
            Call(page, "RebuildFromStack");
            Settle();

            Check.That(!page.Graph!.Nodes.OfType<OpticsNode>().Any() && Pixels(page).AsSpan().SequenceEqual(plain),
                       "neu aus dem Stapel aufgebaut, ist der Graph der frisch umgewandelte");

            page.StepNodes(back: true);
            Settle();

            Check.That(page.Graph!.Nodes.OfType<OpticsNode>().Any() && Pixels(page).AsSpan().SequenceEqual(darker),
                       "und Rueckgaengig holt den gebauten zurueck");

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
