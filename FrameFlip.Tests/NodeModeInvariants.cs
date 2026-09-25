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

namespace FrameFlip.Tests;

/// <summary>
/// Der Knotenmodus auf der Atelierseite: umschalten, zeigen, einstellen, wiederkommen.
///
/// Die Rechnung selbst pruefen NodeParityInvariants und NodeExportInvariants. Hier geht
/// es um das, was die Seite drumherum tut - und was nur auffaellt, wenn man die Seite
/// wirklich aufmacht: ob das Bild beim Umschalten stehen bleibt, ob die Karten den
/// Knoten treffen und nicht den alten Stapel, ob der Modus nach einem Neustart noch da
/// ist.
///
/// Mit einem Testbild aus Bytes, keinem echten Material.
/// </summary>
public static class NodeModeInvariants
{
    private const int Width = 64;
    private const int Height = 40;

    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-knotenmodus-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            string picture = Png(Path.Combine(folder, "bild_0007.png"), 0);
            string logo = Png(Path.Combine(folder, "logo.png"), 1);

            var settings = new AppSettings
            {
                Layers = new LayerStack
                {
                    Layers =
                    {
                        new ImageLayer { Content = LayerContent.Pass, Source = "" },
                        new ImageLayer
                        {
                            Content = LayerContent.Image, Source = logo, FollowSequence = false,
                            Mode = BlendMode.Screen, Opacity = 0.8f, Place = new LayerTransform { Scale = 0.5f },
                        },
                        new ImageLayer
                        {
                            Content = LayerContent.Adjustment,
                            Adjustments = new ImageAdjustments { Exposure = 0.5 },
                            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 0 },
                        },
                        new ImageLayer
                        {
                            Content = LayerContent.Adjustment,
                            Adjustments = new ImageAdjustments { Exposure = -2 },
                            Mask = new LayerMask { Kind = MaskKind.Painted, Paint = PaintedMask.For(Width, Height) },
                        },
                    },
                },
                Adjustments = new ImageAdjustments { Exposure = 0.2 },
                Grading = new GradingStack { Optics = { new VignetteTool { Amount = -0.5f } } },
            };

            TheSwitchKeepsThePicture(settings, picture);
            TheToolsReachTheSelectedNode(settings, picture);
            ItComesBack(settings, picture);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static void TheSwitchKeepsThePicture(AppSettings settings, string picture)
    {
        Check.Group("Knotenmodus: umschalten, zeigen, einstellen");

        var (page, window) = Open(settings, picture);

        try
        {
            if (!Loaded(page))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            var dock = (DockHost)page.FindName("Dock");
            var nodes = (NodeEditor)page.FindName("NodeView");
            var offer = (FrameworkElement)page.FindName("NodeOffer");
            var tools = (ToolColumn)page.FindName("MouseTools");
            var colour = (GradingPanel)page.FindName("Tools");

            byte[] stack = Pixels(page);

            // Das Werkzeug "Knoten" bietet erst an - es wandelt nicht ungefragt um.
            tools.Select(AtelierTool.Nodes, notify: true);
            page.UpdateLayout();

            Check.That(!page.InNodes && offer.Visibility == Visibility.Visible && nodes.Visibility != Visibility.Visible,
                       "das Werkzeug bietet die Umwandlung an, statt sie zu tun");

            page.ConvertToNodes();
            Settle();

            Check.That(page.InNodes && page.Graph is not null, "umgewandelt ist das Atelier im Knotenmodus");
            Check.That(settings.AtelierNodes is { Length: > 0 }, "und der Graph steht in den Einstellungen");
            Check.That(nodes.Visibility == Visibility.Visible && offer.Visibility != Visibility.Visible,
                       "der Graph liegt ueber dem Bild");
            var layerList = (NodeLayerList)page.FindName("NodeLayers");

            Check.That(dock.IsAvailable("layers") && layerList.Visibility == Visibility.Visible &&
                       ((LayerPanel)page.FindName("Layers")).Visibility != Visibility.Visible && layerList.Shown.Count > 0,
                       "das Ebenenfeld zeigt jetzt die Ebenen des Graphen");

            byte[] converted = Pixels(page);

            Check.That(Same(stack, converted), "das Bild bleibt beim Umschalten Byte fuer Byte dasselbe",
                       $"{Differ(stack, converted)} Bytes anders");

            // Ein Mischknoten: Er hat keine Karte, sondern Felder - und die wirken.
            var mix = page.Graph!.Nodes.OfType<MixNode>().First(m => m.Mode == BlendMode.Screen);
            nodes.Select(mix);
            page.UpdateLayout();

            var fields = (StackPanel)colour.FindName("NodeFields");
            var opacity = Sliders(fields).FirstOrDefault();

            Check.That(fields.Visibility == Visibility.Visible && opacity is not null,
                       "ein Mischknoten zeigt seine Felder im Farbstreifen");

            if (opacity is not null)
            {
                opacity.Value = 0.1;
                Settle();

                Check.That(Math.Abs(mix.Opacity - 0.1f) < 1e-4, "der Regler stellt den Knoten ein");
                Check.That(!Same(converted, Pixels(page)), "und das Bild antwortet");
            }

            byte[] beforeVignette = Pixels(page);

            // Ein Werkzeugknoten zeigt seine Karte - und die bearbeitet das Werkzeug des
            // Knotens, nicht eine Kopie.
            var vignette = page.Graph.Nodes.OfType<OpticsNode>().First(n => n.Tool is VignetteTool);
            nodes.Select(vignette);
            page.UpdateLayout();

            var slider = (Slider)colour.FindName("VignetteSlider");

            Check.That(slider.IsVisible, "ein Werkzeugknoten zeigt seine Karte");
            Check.That(((FrameworkElement)colour.FindName("PaletteBar")).IsVisible &&
                       !((FrameworkElement)colour.FindName("TargetBar")).IsVisible,
                       "mit Palette, aber ohne Zielschalter - das Ziel ist der Knoten");

            slider.Value = -1;
            Settle();

            Check.That(((VignetteTool)vignette.Tool!).Amount < -0.9f, "die Karte stellt das Werkzeug des Knotens ein");
            Check.That(!Same(beforeVignette, Pixels(page)), "und das Bild antwortet");

            // Ein anderes Werkzeug blendet den Graphen aus - und das Bild ist frei.
            tools.Select(AtelierTool.Move, notify: true);
            page.UpdateLayout();

            Check.That(nodes.Visibility != Visibility.Visible, "ein anderes Werkzeug blendet die Knoten aus");

            tools.Select(AtelierTool.Nodes, notify: true);
            page.UpdateLayout();

            Check.That(nodes.Visibility == Visibility.Visible, "das Knotenwerkzeug holt sie zurueck");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verschieben und Pinsel wirken im Knotenmodus auf den gewaehlten Knoten: der Rahmen
    /// auf einen Platzieren-Knoten, der Pinsel auf eine gemalte Maske.
    ///
    /// Gezogen und gemalt wird hier nicht mit der Maus - die laesst sich im Test nicht
    /// vortaeuschen -, sondern ueber denselben Weg, den der Rahmen nach einem Zug nimmt.
    /// </summary>
    private static void TheToolsReachTheSelectedNode(AppSettings settings, string picture)
    {
        Check.Group("Knotenmodus: Verschieben und Pinsel wirken auf den gewaehlten Knoten");

        var (page, window) = Open(settings, picture);

        try
        {
            if (!Loaded(page) || !page.InNodes)
            {
                Check.That(false, "das Bild wird im Knotenmodus geladen");
                return;
            }

            var nodes = (NodeEditor)page.FindName("NodeView");
            var frame = (PlacementAdorner)page.FindName("Placement");
            var graph = page.Graph!;

            var place = graph.Nodes.OfType<PlaceNode>()
                .First(p => graph.Into(p.Id, "Bild") is { } link && graph.Find(link.From) is PictureNode);

            nodes.Select(place);
            page.HandleToolKey(System.Windows.Input.Key.V);
            page.UpdateLayout();

            Check.That(nodes.Visibility != Visibility.Visible, "Verschieben blendet die Knoten aus");
            Check.That(frame.IsHitTestVisible, "und der Rahmen greift den gewaehlten Platzieren-Knoten");

            byte[] before = Pixels(page);

            var moved = place.Place.Clone();
            moved.OffsetX += 0.3f;

            Drag(page, "OnPlacementDragged", moved, false);
            Settle();

            Check.That(Math.Abs(place.Place.OffsetX - moved.OffsetX) < 1e-5, "ein Zug am Rahmen versetzt den Knoten");
            Check.That(!Same(before, Pixels(page)), "und das Bild zieht mit");

            // Ein Knoten, der nichts platziert, hat keinen Rahmen.
            nodes.Select(graph.Nodes.OfType<ViewNode>().First());
            page.UpdateLayout();

            Check.That(!frame.IsHitTestVisible, "ohne Platzieren-Knoten gibt es keinen Rahmen");

            // Der Pinsel malt auf die gemalte Maske des gewaehlten Knotens.
            var mask = graph.Nodes.OfType<MaskNode>().First(m => m.Mask.Kind == MaskKind.Painted);
            nodes.Select(mask);
            page.HandleToolKey(System.Windows.Input.Key.B);
            page.UpdateLayout();

            Check.That(frame.Mode == AdornerMode.Paint && frame.IsHitTestVisible, "der Pinsel faengt auf der Maske des Knotens");

            before = Pixels(page);

            var paint = mask.Mask.PaintOn(0, Width, Height);
            paint.Stroke(Width / 2f, Height / 2f, 14f, 1f, 1f);

            Drag(page, "OnPainted", false);
            Settle();

            Check.That(!Same(before, Pixels(page)), "ein Strich aendert das Bild");

            nodes.Select(graph.Nodes.OfType<ViewNode>().First());
            page.UpdateLayout();

            // Ohne gemalte Maske faengt der Pinsel trotzdem - wie im Stapel legt der erste
            // Strich eine Maskenebene an. Frueher fing er nichts, und mit ihm waren Groesse
            // und Haerte am Bild tot.
            int count = graph.Nodes.Count;

            Check.That(frame.IsHitTestVisible && frame.MaskWanted is not null && graph.Nodes.Count == count,
                       "ohne gemalte Maske faengt der Pinsel trotzdem - und legt erst beim Strich etwas an");

            page.HandleToolKey(System.Windows.Input.Key.N);
            page.UpdateLayout();

            Check.That(nodes.Visibility == Visibility.Visible, "N holt die Knoten zurueck");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Ruft den Handler der Seite auf, den sonst der Rahmen nach einem Zug ruft.</summary>
    private static void Drag(AtelierPage page, string handler, params object[] arguments)
    {
        var method = typeof(AtelierPage).GetMethod(handler,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method!.Invoke(page, arguments);
    }

    /// <summary>Ein neuer Start mit denselben Einstellungen: Der Knotenmodus ist noch da, mit allem Eingestellten.</summary>
    private static void ItComesBack(AppSettings settings, string picture)
    {
        Check.Group("Knotenmodus: er kommt nach einem Neustart wieder");

        var (page, window) = Open(settings, picture);

        try
        {
            if (!Loaded(page))
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Check.That(page.InNodes, "der Knotenmodus ist wieder an");

            var mix = page.Graph?.Nodes.OfType<MixNode>().FirstOrDefault(m => m.Mode == BlendMode.Screen);
            var vignette = page.Graph?.Nodes.OfType<OpticsNode>().FirstOrDefault(n => n.Tool is VignetteTool);

            Check.That(mix is not null && Math.Abs(mix.Opacity - 0.1f) < 1e-4 &&
                       vignette?.Tool is VignetteTool { Amount: < -0.9f },
                       "mit allem, was eingestellt war");

            // Und das Bild ist das, was der Graph rechnet - nicht der alte Stapel.
            var direct = new byte[Width * Height * 4];

            unsafe
            {
                fixed (byte* start = direct)
                {
                    var inputs = GraphFrames.Read(page.Graph!, picture, new StandardViewTransform())!;
                    GraphEvaluator.Render(page.Graph!, inputs, (IntPtr)start, Width * 4);
                }
            }

            Check.That(Same(direct, Pixels(page)), "und die Seite zeigt, was der Graph rechnet",
                       $"{Differ(direct, Pixels(page))} Bytes anders");
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------------------ Hilfsmittel

    private static (AtelierPage, Window) Open(AppSettings settings, string picture)
    {
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });

        var window = new Window
        {
            Content = page,
            Width = 1100,
            Height = 750,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        window.Show();
        page.UpdateLayout();
        page.Open(picture);

        return (page, window);
    }

    private static bool Loaded(AtelierPage page)
    {
        var size = (TextBlock)page.FindName("SourceText");

        if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0)) return false;

        Settle();
        return true;
    }

    /// <summary>Lange genug fuer den Zeitgeber der Seite (180 ms) und das Nachlesen im Hintergrund.</summary>
    private static void Settle() => Pump(TimeSpan.FromSeconds(0.6), () => false);

    private static byte[] Pixels(AtelierPage page)
    {
        var display = (System.Windows.Controls.Image)page.FindName("Display");
        if (display.Source is not BitmapSource source) return Array.Empty<byte>();

        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        return pixels;
    }

    private static IEnumerable<Slider> Sliders(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Slider slider) yield return slider;

            foreach (var inner in Sliders(child)) yield return inner;
        }
    }

    private static bool Same(byte[] a, byte[] b) => a.Length > 0 && a.AsSpan().SequenceEqual(b);

    private static int Differ(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return Math.Max(a.Length, b.Length);

        int differ = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) differ++;

        return differ;
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

    private static string Png(string path, int variant)
    {
        int stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = y * stride + x * 4;
                pixels[at] = (byte)((x * 5 + variant * 90) % 256);
                pixels[at + 1] = (byte)((y * 7 + x) % 256);
                pixels[at + 2] = (byte)(180 - (x + y) % 100);
                pixels[at + 3] = (byte)(variant == 1 && x < 8 ? 60 : 255);
            }
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
