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

namespace FrameFlip.Tests;

/// <summary>
/// "Original" zeigt dieselben Ebenen, eingemischt wie sonst, ohne die eigenen Korrekturen
/// (<see cref="Untouched"/>). Rueckmeldung vom 7. Oktober: Vorher stand das Bild der Datei
/// da, ohne die Passe - mit einer ganz anderen Farbe als das eigene Bild ohne Korrektur.
///
/// Mit Testbildern aus Bytes, keinem echten Material.
/// </summary>
public static class OriginalViewInvariants
{
    private const int Width = 48;
    private const int Height = 24;

    private static readonly IViewTransform View = new StandardViewTransform();

    public static void Run()
    {
        TheModelKeepsTheMixing();
        ThePageShowsTheLayersWithoutCorrections();
    }

    // ------------------------------------------------------------ Modell

    private static void TheModelKeepsTheMixing()
    {
        Check.Group("Original: dieselben Ebenen ohne eigene Korrekturen - im Modell");

        var stack = Corrected();
        var plain = Untouched.Of(stack);

        var glow = plain.Layers[1];
        var shade = plain.Layers[2];
        var child = plain.Layers[3].Children[0];

        Check.That(glow.Mode == BlendMode.Screen && Math.Abs(glow.Opacity - 0.8f) < 1e-6 && glow.Mask.Kind == MaskKind.Gradient &&
                   glow.Place.Scale == 0.9f && glow.MatteFloor == 0.1f,
                   "Mischart, Deckkraft, Maske, Lage und Deckungsschleier bleiben");
        Check.That(glow.Exposure == 0f && glow.Tint.Near(1f) && glow.Adjustments is null && glow.Tools is null,
                   "Belichtung, Toenung und Korrektur der Ebene fallen weg");
        Check.That(shade.Content == LayerContent.Adjustment && shade.Visible && shade.Adjustments is { IsNeutral: true } &&
                   shade.Tools is null && shade.Mask.Kind == MaskKind.Gradient,
                   "eine Einstellungsebene bleibt stehen, mit ihrer Maske, aber neutral");
        Check.That(child.Exposure == 0f && child.Mode == BlendMode.Multiply, "auch in Gruppen");
        Check.That(stack.Layers[1].Exposure == 1f && stack.Layers[2].Adjustments is { IsNeutral: false },
                   "der Stapel selbst bleibt, wie er war");

        var sources = Sources();

        var original = Pixels(LayerComposer.Compose(plain, sources, null, 1, 0)!);
        var corrected = Pixels(LayerComposer.Compose(stack, sources, null, 1, 0)!);
        var file = Pixels(sources[""]);

        Check.That(Differ(original, corrected) > 50, "die Gegenprobe: mit Korrekturen kommt etwas anderes heraus");
        Check.That(Differ(original, file) > 50, "und das Original ist nicht das Bild der Datei - die Ebenen sind eingemischt");

        // Im Knotenmodus dasselbe: der Graph mit stummen Korrekturen rechnet wie der Stapel ohne sie.
        var graph = StackToGraph.Convert(stack, new ImageAdjustments { Exposure = 0.6, Contrast = 1.2 },
                                         new GradingStack { Optics = { new VignetteTool { Amount = -0.5f } } });

        var fromGraph = Render(Untouched.Of(graph), sources);
        var fromStack = Render(StackToGraph.Convert(plain, ImageAdjustments.Neutral, new GradingStack()), sources);
        var editedGraph = Render(graph, sources);

        Check.That(Worst(fromGraph, fromStack) <= 1, "im Knotenmodus ergibt das Original dasselbe wie im Stapel",
                   $"bis {Worst(fromGraph, fromStack)} Stufen");
        Check.That(Differ(fromGraph, editedGraph) > 50, "und nicht das bearbeitete Bild");
        Check.That(graph.Nodes.All(n => !n.Muted), "der Graph selbst bleibt, wie er war");
    }

    // ------------------------------------------------------------ Seite

    /// <summary>
    /// Auf der Seite: Das Original eines bearbeiteten Stapels ist das Bild, das derselbe Stapel
    /// ohne Korrekturen zeigt - im Stapel und im Knotenmodus. Und nicht das Bild der Datei allein.
    /// </summary>
    private static void ThePageShowsTheLayersWithoutCorrections()
    {
        Check.Group("Original: dieselben Ebenen ohne eigene Korrekturen - auf der Seite");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-original-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        try
        {
            string picture = Png(Path.Combine(root, "render_0001.png"));

            // Derselbe Aufbau: das Bild, darueber dasselbe Bild auf Multiplizieren. Einmal mit
            // Korrekturen an der Ebene und am Gesamtbild, einmal ohne, einmal ohne die Ebene.
            var withCorrections = Shown(root, picture, Stack(exposure: 1f), new ImageAdjustments { Exposure = 0.5 }, nodes: false);
            var withoutCorrections = Shown(root, picture, Stack(exposure: 0f), new ImageAdjustments(), nodes: false);
            var fileOnly = Shown(root, picture, Stack(exposure: 0f, multiply: false), new ImageAdjustments(), nodes: false);
            var inNodes = Shown(root, picture, Stack(exposure: 1f), new ImageAdjustments { Exposure = 0.5 }, nodes: true);

            if (withCorrections is null || withoutCorrections is null || fileOnly is null || inNodes is null)
            {
                Check.That(false, "das Bild wird geladen");
                return;
            }

            Check.That(Differ(withCorrections.Value.Result, withoutCorrections.Value.Result) > 50,
                       "die Gegenprobe: die Korrekturen aendern das Bild");
            Check.That(Worst(withCorrections.Value.Original, withoutCorrections.Value.Result) <= 1,
                       "das Original ist das Bild ohne eigene Korrekturen",
                       $"bis {Worst(withCorrections.Value.Original, withoutCorrections.Value.Result)} Stufen");
            Check.That(Differ(withCorrections.Value.Original, fileOnly.Value.Result) > 50,
                       "mit den Ebenen eingemischt - nicht das Bild der Datei allein");
            Check.That(Worst(inNodes.Value.Original, withoutCorrections.Value.Result) <= 1 &&
                       Differ(inNodes.Value.Result, inNodes.Value.Original) > 50,
                       "im Knotenmodus ebenso",
                       $"bis {Worst(inNodes.Value.Original, withoutCorrections.Value.Result)} Stufen");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Oeffnet das Bild mit diesem Rezept in einer eigenen Konfiguration und liefert, was die
    /// Seite zeigt - das Ergebnis und, eingerastet, das Original. Null, wenn nichts geladen wurde.
    /// </summary>
    private static (byte[] Result, byte[] Original)? Shown(string root, string picture, LayerStack stack,
                                                             ImageAdjustments adjustments, bool nodes)
    {
        string config = Path.Combine(root, Guid.NewGuid().ToString("N")[..8], "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", config);

        // Ohne Projektdatei vom letzten Mal: Das Rezept soll aus den Einstellungen kommen.
        string projects = Path.Combine(root, Atelier.AtelierProjectStore.FolderName);
        Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
        if (Directory.Exists(projects)) Directory.Delete(projects, true);

        var settings = new AppSettings { Layers = stack, Adjustments = adjustments, AtelierImage = picture };
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();
            page.Open(picture);

            if (!Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0, 10)) return null;
            Pump(() => false, 0.6);

            if (nodes)
            {
                page.ConvertToNodes();
                Pump(() => false, 0.6);
            }

            var result = Display(page);

            page.ToggleCompare();
            Pump(() => false, 0.3);

            var original = Display(page);
            page.ToggleCompare();

            return result.Length > 0 && original.Length == result.Length ? (result, original) : null;
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
        }
    }

    // ------------------------------------------------------------ Hilfsmittel

    /// <summary>Ein Stapel mit allem, was man selbst dreht - und allem, was die Mischung ausmacht.</summary>
    private static LayerStack Corrected()
    {
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Image, Source = "bild.png", FollowSequence = false,
            Mode = BlendMode.Screen, Opacity = 0.8f, Exposure = 1f, Tint = new ColourTriplet(1f, 0.7f, 0.5f),
            MatteFloor = 0.1f, Place = new LayerTransform { Scale = 0.9f },
            Adjustments = new ImageAdjustments { Saturation = 0.3 },
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 30, Width = 0.6f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Adjustment,
            Adjustments = new ImageAdjustments { Exposure = -1.2 },
            Tools = new GradingStack(),
            Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 90, Width = 0.7f },
        });

        var group = new ImageLayer { Content = LayerContent.Group, Opacity = 0.7f };
        group.Children.Add(new ImageLayer { Content = LayerContent.Pass, Source = "", Mode = BlendMode.Multiply, Exposure = 0.5f });
        stack.Layers.Add(group);

        return stack;
    }

    private static LayerStack Stack(float exposure, bool multiply = true)
    {
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

        if (multiply)
            stack.Layers.Add(new ImageLayer { Content = LayerContent.Pass, Source = "", Mode = BlendMode.Multiply, Exposure = exposure });

        return stack;
    }

    private static Dictionary<string, FloatFrame> Sources()
    {
        FloatFrame Frame(bool scene, Func<int, int, float> v) => new()
        {
            Width = Width,
            Height = Height,
            R = Enumerable.Range(0, Width * Height).Select(i => v(i % Width, i / Width)).ToArray(),
            G = Enumerable.Range(0, Width * Height).Select(i => 0.6f * v(i % Width, i / Width)).ToArray(),
            B = Enumerable.Range(0, Width * Height).Select(i => 0.3f + 0.2f * v(i % Width, i / Width)).ToArray(),
            A = Enumerable.Range(0, Width * Height).Select(i => i % Width < 6 ? 0.4f : 1f).ToArray(),
            IsSceneReferred = scene,
        };

        return new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            [""] = Frame(true, (x, y) => 0.1f + 1.4f * x / Width),
            ["bild.png"] = Frame(false, (x, y) => (float)y / Height),
        };
    }

    private static byte[] Pixels(FloatFrame frame)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, View, PreparedGrading.None, buffer, frame.Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
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

    private static int Differ(byte[] a, byte[] b) => a.Length != b.Length ? int.MaxValue : a.Where((v, i) => v != b[i]).Count();

    private static int Worst(byte[] a, byte[] b)
        => a.Length != b.Length ? int.MaxValue : a.Select((v, i) => Math.Abs(v - b[i])).DefaultIfEmpty(0).Max();

    private static byte[] Display(AtelierPage page)
    {
        var display = (System.Windows.Controls.Image)page.FindName("Display");
        if (display.Source is not BitmapSource source) return Array.Empty<byte>();

        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        return pixels;
    }

    private static string Png(string path)
    {
        int stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = y * stride + x * 4;
                pixels[at] = (byte)(40 + x * 4);
                pixels[at + 1] = (byte)(90 + y * 3);
                pixels[at + 2] = (byte)(200 - x * 2);
                pixels[at + 3] = 255;
            }
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride)));

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
}
