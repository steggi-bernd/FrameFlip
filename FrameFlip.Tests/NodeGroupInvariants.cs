using System.Windows;
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
