using System.IO;
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
/// Der Graph in Gruppen (docs/Atelier-Knoten-Gruppen.md): links die Quellen, je Ebene ein
/// Rahmen, die Mischen in einer Spalte, rechts das Gesamtbild - und Rahmen, die man am
/// Kopf packt und samt Inhalt verschiebt.
///
/// Dass ein Graph mit eigenen Quellen dasselbe Bild rechnet, prueft NodeParityInvariants;
/// dass ein alter Graph beim Oeffnen umgestellt wird, NodeModeInvariants.
/// </summary>
public static class NodeGroupInvariants
{
    public static void Run()
    {
        TheGroupsFollowTheLayers();
        TheLayoutReadsLeftToRight();
        TheFramesCanBeMoved();
        TheNodesReadTheRightWay();
        MasksGoToTheChosenLayer();
    }

    // ------------------------------------------------------------ Rueckmeldung vom 7. Oktober

    /// <summary>
    /// Am Mischen steht "Oben" oben und "Unten" darunter - frueher umgekehrt. Der Bildweg und
    /// das, was ein stummes Mischen durchreicht, bleibt "Unten". Eine Maske zeigt nur die
    /// Eingaenge, die sie liest, und ist kleiner.
    /// </summary>
    private static void TheNodesReadTheRightWay()
    {
        Check.Group("Knoten in Gruppen: Oben oben, Masken knapp");

        var mix = new MixNode();

        Check.That(mix.Inputs[0].Name == "Oben" && mix.Inputs[1].Name == "Unten" && mix.Inputs[2].Name == "Faktor",
                   "am Mischen steht Oben oben, darunter Unten, dann der Faktor");
        Check.That(NodeEdits.Through(mix) == ("Unten", "Bild"), "der Bildweg fuehrt weiter durch Unten");

        var gradient = new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient }, Preview = true };
        var luminance = new MaskNode { Mask = new LayerMask { Kind = MaskKind.Luminance }, Preview = true };
        var underlying = new MaskNode { Mask = new LayerMask { Kind = MaskKind.Underlying }, Preview = true };
        var pass = new MaskNode { Mask = new LayerMask { Kind = MaskKind.Pass, Source = "mist" }, Preview = true };

        Check.That(NodeLayout.ShownInputs(gradient).Count == 0 &&
                   NodeLayout.ShownInputs(luminance).Select(s => s.Name).SequenceEqual(new[] { "Ebene" }) &&
                   NodeLayout.ShownInputs(underlying).Select(s => s.Name).SequenceEqual(new[] { "Untergrund" }) &&
                   NodeLayout.ShownInputs(pass).Select(s => s.Name).SequenceEqual(new[] { "Pass" }),
                   "eine Maske zeigt nur die Eingaenge, die sie liest");

        double before = NodeLayout.Header + 2 * NodeLayout.Pad + 2 * NodeLayout.Row + NodeLayout.PreviewHeight + NodeLayout.Pad;

        Check.That(NodeLayout.Height(gradient) < before * 0.7,
                   "und ist um rund ein Drittel kleiner als vorher", $"{NodeLayout.Height(gradient)} statt {before}");

        // Ein verborgener Eingang behaelt sein Kabel - es tut, was es vorher tat.
        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());
        var masked = graph.Nodes.OfType<MaskNode>().First(m => m.Mask.Kind == MaskKind.Gradient);

        Check.That(graph.Into(masked.Id, "Untergrund") is not null && graph.Problems().Count == 0,
                   "das Kabel an einem verborgenen Eingang steckt weiter, der Graph bleibt rechenbar");
    }

    /// <summary>
    /// Eine neue Maske trifft die gewaehlte Ebene - auch wenn ihr Pass links oder ein Knoten ihrer
    /// Gruppe gewaehlt ist - und kommt in ihren Faktor, statt als neue Maskenebene oben auf den
    /// Stapel. Eine zweite kommt dazu. Ohne Wahl bleibt es die Maskenebene.
    /// </summary>
    private static void MasksGoToTheChosenLayer()
    {
        Check.Group("Knoten in Gruppen: eine neue Maske kommt an die gewaehlte Ebene");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-gruppen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string picture = Png(Path.Combine(root, "render_0001.png"), 0);
        string logo = Png(Path.Combine(root, "logo.png"), 1);

        var settings = new AppSettings
        {
            Layers = new LayerStack
            {
                Layers =
                {
                    new ImageLayer { Content = LayerContent.Pass, Source = "" },
                    new ImageLayer { Content = LayerContent.Image, Source = logo, FollowSequence = false, Mode = BlendMode.Screen, Name = "Logo" },
                },
            },
            AtelierImage = picture,
        };

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        object? Call(string method, params object[] arguments) => typeof(AtelierPage).GetMethod(method, flags)!.Invoke(page, arguments);

        try
        {
            window.Show();
            page.UpdateLayout();
            page.Open(picture);

            if (!Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0, 10))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Pump(() => false, 0.6);
            page.ConvertToNodes();
            Pump(() => false, 0.4);

            var graph = page.Graph!;
            var editor = (NodeEditor)page.FindName("NodeView");
            var mix = graph.Nodes.OfType<MixNode>().Single(m => m.Label == "Logo");
            var source = graph.Nodes.OfType<PictureNode>().Single(n => n.Path == logo);
            var place = graph.Nodes.OfType<PlaceNode>().Single(n => graph.Into(n.Id, "Bild")?.From == source.Id);
            int mixes = graph.Nodes.OfType<MixNode>().Count();

            // Die Quelle links gewaehlt, ein erster Pinselstrich: die Maske kommt an die Ebene.
            editor.Select(source);
            Call("MakeNodeMask");
            graph = page.Graph!;

            var painted = graph.Into(mix.Id, "Faktor") is { } factor ? graph.Find(factor.From) as MaskNode : null;

            Check.That(painted is { Mask.Kind: MaskKind.Painted } && graph.Nodes.OfType<MixNode>().Count() == mixes,
                       "die Quelle gewaehlt, der Pinsel legt seine Maske in den Faktor ihrer Ebene - keine neue Ebene");
            Check.That(NodeGroups.Of(graph).Single(g => g.Head == mix).Members.Contains(painted!),
                       "und sie steht in deren Gruppe");

            // Das Platzieren gewaehlt, eine Verlaufsmaske dazu: Sie kommt hinzu, die gemalte bleibt.
            editor.Select(place);
            Call("AddNodeMaskLayer", new LayerMask { Kind = MaskKind.Gradient, Angle = 30 }, "Verlauf", true);
            graph = page.Graph!;

            var union = graph.Into(mix.Id, "Faktor") is { } both ? graph.Find(both.From) as MaskMathNode : null;

            Check.That(union is { Operation: MaskOperation.Maximum } && graph.Into(union.Id, "A")?.From == painted!.Id &&
                       graph.Find(graph.Into(union.Id, "B")!.From) is MaskNode { Mask.Kind: MaskKind.Gradient } &&
                       graph.Nodes.OfType<MixNode>().Count() == mixes,
                       "eine zweite Maske kommt dazu - die Ebene ist sichtbar, wo eine der beiden es sagt");

            // Der Pinsel findet die gemalte Maske auch hinter der Rechnung wieder.
            editor.Select(mix);
            int masks = graph.Nodes.OfType<MaskNode>().Count();
            Call("MakeNodeMask");

            Check.That(page.Graph!.Nodes.OfType<MaskNode>().Count() == masks, "weitermalen malt in die gemalte Maske der Ebene");

            // Nichts gewaehlt: wie bisher eine eigene Maskenebene.
            editor.Select(null);
            Call("AddNodeMaskLayer", new LayerMask { Kind = MaskKind.Gradient }, "Maske", true);

            Check.That(page.Graph!.Nodes.OfType<MixNode>().Count() == mixes + 1, "ohne gewaehlte Ebene entsteht eine Maskenebene");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static string Png(string path, int variant)
    {
        const int w = 48, h = 24;
        var pixels = new byte[w * h * 4];

        for (int i = 0; i < w * h; i++)
        {
            pixels[i * 4] = (byte)((i % w * 5 + variant * 90) % 256);
            pixels[i * 4 + 1] = (byte)(100 + variant * 60);
            pixels[i * 4 + 2] = (byte)(180 - i / w * 3);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4)));

        using (var file = File.Create(path)) encoder.Save(file);
        return path;
    }

    private static bool Pump(Func<bool> until, double seconds)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);

        while (DateTime.UtcNow < end)
        {
            if (until()) return true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        return until();
    }

    // ------------------------------------------------------------ Gruppen

    private static void TheGroupsFollowTheLayers()
    {
        Check.Group("Knoten in Gruppen: eine Gruppe je Ebene, die Quellen fuer sich");

        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());
        var groups = NodeGroups.Of(graph);
        var layers = groups.Where(g => !g.IsPicture).ToList();
        var order = graph.Order()!;

        Check.That(layers.Count == graph.Nodes.OfType<MixNode>().Count() && groups[^1].IsPicture && groups[^1].Head is OutputNode,
                   "je Mischen eine Gruppe, zuletzt das Gesamtbild bis zur Ausgabe",
                   $"{layers.Count} Gruppen, {graph.Nodes.OfType<MixNode>().Count()} Mischen");

        // Jeder gerechnete Knoten steht genau einmal: in einer Gruppe oder links als Quelle.
        var counted = order.ToDictionary(n => n.Id, _ => 0);

        foreach (var each in groups)
            foreach (var member in each.Members)
                counted[member.Id]++;

        var stray = order.Where(n => counted[n.Id] != (NodeGroups.IsSource(n) || n is BlackNode && counted[n.Id] == 0 ? 0 : 1)).ToList();

        Check.That(stray.Count == 0, "jeder Knoten gehoert zu genau einer Gruppe - Quellen und die Leinwand zu keiner",
                   string.Join(", ", stray.Select(n => $"{n.GetType().Name} {counted[n.Id]}x")));

        // Die Reihenfolge der Ebenenliste, oben zuerst.
        var listed = NodeLayerList.Of(graph).Where(l => l.Mix is not null).Select(l => l.Mix!.Id).ToList();

        Check.That(layers.Select(g => g.Head.Id).SequenceEqual(listed), "die Gruppen stehen in der Folge der Ebenenliste");

        // Was nur einer Ebene zuarbeitet, liegt in ihrer Gruppe.
        var masked = layers.Single(g => g.Head is MixNode { Mode: BlendMode.Add });
        var scoped = layers.Single(g => g.Head is MixNode { Mode: BlendMode.Screen });

        Check.That(masked.Members.OfType<PlaceNode>().Count() == 1 && masked.Members.OfType<MaskNode>().Count() == 1,
                   "eine Ebene mit Maske: Platzieren und Maske in ihrer Gruppe");
        Check.That(scoped.Members.OfType<LayerGradeNode>().Any() && scoped.Members.OfType<RestrictNode>().Any() &&
                   scoped.Members.OfType<MaskNode>().Any(),
                   "eine Ebene, deren Maske die Korrektur begrenzt: Korrektur, Begrenzen und Maske in ihrer Gruppe");

        // Eine Gruppe im Stapel: ihre Kinder sind eigene Gruppen in ihrem Rahmen, das
        // Angeschnittene liegt im Rahmen seines Traegers.
        var group = layers.Single(g => g.Head is MixNode { Opacity: < 0.9f and > 0.7f });
        var children = layers.Where(g => g.Parent?.Id == group.Head.Id).ToList();
        var clipped = layers.Single(g => g.Head is MixNode { Clip: true });

        Check.That(children.Count >= 1 && children.All(c => layers.IndexOf(c) > layers.IndexOf(group)),
                   "die Kinder einer Gruppe stehen in ihrem Rahmen, darunter");
        Check.That(clipped.Parent is not null && layers.Any(g => g.Head.Id == clipped.Parent.Id),
                   "was angeschnitten ist, liegt im Rahmen seines Traegers");

        // Quellen: je Pass eine, auch wenn ihn mehrere Ebenen lesen.
        var passes = graph.Nodes.OfType<RenderNode>().Where(r => r.Only is not null).Select(r => r.Only!).ToList();

        Check.That(passes.Count == passes.Distinct().Count() && passes.Contains("P.a") && passes.Contains("P.b"),
                   "jeder Pass hat eine Quelle - zwei Ebenen aus demselben Pass lesen dieselbe", string.Join(", ", passes));
        Check.That(graph.Nodes.Where(NodeGroups.IsSource).All(n => n.Preview),
                   "jede Quelle zeigt ihre Vorschau");
    }

    // ------------------------------------------------------------ Anordnung

    private static void TheLayoutReadsLeftToRight()
    {
        Check.Group("Knoten in Gruppen: links die Quellen, die Mischen in einer Spalte, rechts das Gesamtbild");

        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());
        var groups = NodeGroups.Of(graph);
        var layers = groups.Where(g => !g.IsPicture).ToList();
        var picture = groups.Single(g => g.IsPicture);
        var order = graph.Order()!;
        var grouped = groups.SelectMany(g => g.Members).Select(n => n.Id).ToHashSet();

        Check.That(graph.Layout == NodeLayout.Grouped, "der umgewandelte Graph ist in Gruppen angeordnet");

        var sources = order.Where(n => !grouped.Contains(n.Id)).ToList();

        Check.That(sources.All(n => n.X == 0) && sources.Any(n => n is RenderNode { Only: not null }),
                   "die Quellen stehen ganz links", string.Join(", ", sources.Select(n => $"{n.GetType().Name}@{n.X}")));
        Check.That(groups.SelectMany(g => g.Members).All(n => n.X >= NodeLayout.ColumnStep + NodeLayout.SourceGap - 0.5),
                   "alles andere rechts davon, mit Platz fuer die Kabel");

        double mixX = layers[0].Head.X;

        Check.That(layers.All(g => Math.Abs(g.Head.X - mixX) < 0.5),
                   "alle Mischen stehen in einer Spalte - dort verbinden sich die Gruppen");
        Check.That(layers.Zip(layers.Skip(1)).All(p => p.First.Head.Y < p.Second.Head.Y),
                   "die Gruppen stehen untereinander, die oberste Ebene oben");
        Check.That(layers.All(g => g.Members.All(m => m.X <= g.Head.X + 0.5)),
                   "in einer Gruppe laeuft der Bildweg von links auf das Mischen zu");
        Check.That(picture.Members.All(n => n.X > mixX + 0.5), "das Gesamtbild steht rechts der Mischen");

        // Nichts ueberdeckt etwas anderes.
        var overlaps = new List<string>();

        foreach (var a in graph.Nodes)
            foreach (var b in graph.Nodes)
                if (string.CompareOrdinal(a.Id, b.Id) < 0 && Overlap(NodeEditor.Bounds(a), NodeEditor.Bounds(b)))
                    overlaps.Add($"{a.GetType().Name}/{b.GetType().Name}");

        Check.That(overlaps.Count == 0, "kein Knoten liegt auf einem anderen", string.Join(", ", overlaps.Take(5)));

        // Und ein Graph, an dem gebaut wurde, laesst sich wieder so anordnen.
        var extra = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Gradient } });
        NodeLayout.Arrange(graph);

        Check.That(graph.Nodes.Where(n => !ReferenceEquals(n, extra)).All(n => graph.Nodes.Count(o => !ReferenceEquals(o, n) &&
                       Overlap(NodeEditor.Bounds(n), NodeEditor.Bounds(o))) == 0),
                   "ein freier Knoten steht nach dem Anordnen unter allem, ohne etwas zu verdecken");
    }

    // ------------------------------------------------------------ Rahmen

    private static void TheFramesCanBeMoved()
    {
        Check.Group("Knoten in Gruppen: Rahmen um die Ebenen, am Kopf zu verschieben");

        var graph = StackToGraph.Convert(Stack(), ImageAdjustments.Neutral, new GradingStack());
        var editor = new NodeEditor { Graph = graph, Translate = key => key, Title = n => n.GetType().Name };
        var window = Window(editor);

        try
        {
            editor.UpdateLayout();
            editor.Frame();

            var frames = editor.Frames();
            var groups = NodeGroups.Of(graph);

            Check.That(frames.Count == groups.Count, "je Gruppe ein Rahmen", $"{frames.Count} Rahmen, {groups.Count} Gruppen");

            // Ein Rahmen umgibt seine Knoten; einer Gruppe im Stapel umgibt auch ihre Kinder.
            Check.That(frames.All(f => f.Group.Members.All(m => f.Box.Contains(NodeEditor.Bounds(m)))),
                       "jeder Rahmen umgibt seine Knoten");

            var boxes = frames.ToDictionary(f => f.Group.Head.Id, f => f.Box);
            var nested = frames.Where(f => f.Group.Parent is not null).ToList();

            Check.That(nested.Count > 0 && nested.All(f => boxes[f.Group.Parent!.Id].Contains(f.Box)),
                   "der Rahmen einer Gruppe umschliesst die Rahmen ihrer Ebenen");

            // Rahmen nebeneinander - nicht ineinander - ueberdecken sich nicht.
            bool Inside(string inner, string outer)
            {
                for (var group = groups.First(g => g.Head.Id == inner); group.Parent is { } parent;
                     group = groups.First(g => g.Head.Id == parent.Id))
                {
                    if (parent.Id == outer) return true;
                }

                return false;
            }

            var clashes = new List<string>();

            foreach (var a in frames)
            {
                foreach (var b in frames)
                {
                    string ia = a.Group.Head.Id, ib = b.Group.Head.Id;

                    if (string.CompareOrdinal(ia, ib) >= 0 || Inside(ia, ib) || Inside(ib, ia)) continue;
                    if (Overlap(a.Box, b.Box)) clashes.Add($"{ia}/{ib}");
                }
            }

            Check.That(clashes.Count == 0, "Rahmen nebeneinander ueberdecken sich nicht", string.Join(", ", clashes));

            // Am Kopf packen: Die Ebene wird gewaehlt, und die ganze Gruppe wandert mit - nur sie.
            var target = frames.First(f => !f.Group.IsPicture && f.Group.Parent is null &&
                                           !frames.Any(o => o.Group.Parent?.Id == f.Group.Head.Id));
            var before = graph.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));
            var head = editor.ToScreen(new Point(target.Box.X + target.Box.Width / 2, target.Box.Y + 8));

            int editing = 0, layout = 0;
            editor.Editing += () => editing++;
            editor.LayoutChanged += () => layout++;

            Check.That(editor.BeginFrame(head) && ReferenceEquals(editor.Selected, target.Group.Head),
                       "ein Klick auf den Kopf eines Rahmens waehlt seine Ebene");

            editor.DragFrame(head + new Vector(60 * editor.Zoom, 30 * editor.Zoom));
            editor.FinishFrame();

            var members = target.Group.Members.Select(m => m.Id).ToHashSet();

            Check.That(target.Group.Members.All(m => Math.Abs(m.X - before[m.Id].X - 60) < 0.6 && Math.Abs(m.Y - before[m.Id].Y - 30) < 0.6),
                       "gezogen wandern alle Knoten der Gruppe um dasselbe Stueck");
            Check.That(graph.Nodes.Where(n => !members.Contains(n.Id)).All(n => n.X == before[n.Id].X && n.Y == before[n.Id].Y),
                       "und nichts sonst");
            Check.That(editing == 1 && layout == 1, "mit einem Stand fuer Rueckgaengig davor, und die Lage wird gespeichert",
                       $"{editing}/{layout}");

            // Ein Klick ohne Zug waehlt nur.
            editing = layout = 0;
            var still = graph.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

            editor.Frames();
            editor.BeginFrame(head + new Vector(60 * editor.Zoom, 30 * editor.Zoom));
            editor.FinishFrame();

            Check.That(graph.Nodes.All(n => (n.X, n.Y) == still[n.Id]) && editing == 0 && layout == 0,
                       "ein Klick auf den Kopf verschiebt nichts",
                       $"{editing}/{layout}, verschoben: {string.Join(", ", graph.Nodes.Where(n => (n.X, n.Y) != still[n.Id]).Select(n => n.GetType().Name))}");

            // Neben jedem Kopf: kein Rahmen.
            Check.That(editor.FrameAt(new Point(-5000, -5000)) is null, "wo kein Kopf ist, packt man keinen Rahmen");
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------ Hilfsmittel

    /// <summary>
    /// Ein Stapel mit allem, was eine Gruppe ausmacht: Passe mit und ohne Maske, ein Pass
    /// zweimal, eine Einstellungsebene, eine Gruppe mit Kind und Angeschnittenem, ein Bild.
    /// </summary>
    private static LayerStack Stack()
    {
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Pass, Source = "P.a", Mode = BlendMode.Add, Name = "Glanz",
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 30, Width = 0.6f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Pass, Source = "P.b", Mode = BlendMode.Screen, Name = "Licht",
            Adjustments = new ImageAdjustments { Exposure = 0.4 },
            Mask = new LayerMask { Kind = MaskKind.Gradient, Scope = MaskScope.Colour, Angle = 0, Width = 0.5f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Adjustment, Name = "Korrektur",
            Adjustments = new ImageAdjustments { Exposure = -0.3 },
            Tools = new GradingStack(),
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 90 },
        });

        var group = new ImageLayer { Content = LayerContent.Group, Name = "Gruppe", Opacity = 0.8f, Mode = BlendMode.Normal };
        group.Children.Add(new ImageLayer { Content = LayerContent.Pass, Source = "P.a", Mode = BlendMode.Multiply, Name = "Schatten" });
        group.Children.Add(new ImageLayer { Content = LayerContent.Pass, Source = "P.b", Mode = BlendMode.Normal, Clipped = true, Name = "Rand" });
        stack.Layers.Add(group);

        stack.Layers.Add(new ImageLayer { Content = LayerContent.Image, Source = "logo.png", FollowSequence = false, Mode = BlendMode.Overlay, Name = "Logo" });

        return stack;
    }

    private static bool Overlap(Rect a, Rect b)
    {
        var both = Rect.Intersect(a, b);
        return !both.IsEmpty && both.Width > 0.5 && both.Height > 0.5;
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
}
