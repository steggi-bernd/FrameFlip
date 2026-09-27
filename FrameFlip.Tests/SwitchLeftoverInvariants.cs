using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Was beim Wechsel von Bild oder Projekt nicht mitkommen darf (docs/Atelier-Arbeitsablauf.md,
/// 4.1). Nachgestellt nach dem Bericht "beim Pinsel taucht eine Maske aus einem anderen
/// Projekt auf": Ein Strich nach dem Wechsel landet im neuen Projekt, der objektgebundene
/// Pinsel liest das Objekt der offenen Datei, eine wartende Pipette endet mit dem Bild, und
/// die Vorschauen der Knoten gehoeren zu ihrem Projekt.
/// </summary>
public static class SwitchLeftoverInvariants
{
    private const int Width = 240, Height = 160;

    public static void Run()
    {
        StrokeStaysInItsProject();
        ObjectLimitReadsTheOpenFile();
        LevelsPickEndsWithTheImage();
        PreviewsBelongToTheProject();
    }

    /// <summary>Ein Strich ueber die echten Tasten des Rahmens, nach einem Projektwechsel.</summary>
    private static void StrokeStaysInItsProject()
    {
        Check.Group("Wechsel: ein Pinselstrich nimmt keine Maske mit");

        WithPage((page, root) =>
        {
            string first = Png(root, "eins", "render_0001.png");
            string second = Png(root, "zwei", "shot_0001.png");
            string third = Png(root, "drei", "neu_0001.png");

            var frame = (PlacementAdorner)page.FindName("Placement");
            var layers = (LayerPanel)page.FindName("Layers");

            // Erstes Projekt im Knotenmodus: Ein Strich links oben legt eine gemalte Maske an.
            Show(page, first);
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            page.HandleToolKey(Key.B);
            page.UpdateLayout();

            Stroke(frame, 30, 30, 60, 40);
            Pump(() => false, 0.3);

            var nodeMask = page.Graph?.Nodes.OfType<MaskNode>().FirstOrDefault(m => m.Mask.Kind == MaskKind.Painted);
            Check.That(nodeMask?.Mask.PaintFor(1) is { } painted && painted.At(40, 32) > 0.5f,
                       "Vorbereitung: im ersten Projekt steht links oben eine gemalte Maske");

            // Ein neues Bild, noch ohne Projekt, im Stapel. Der Pinsel bleibt in der Hand.
            Show(page, second);
            Check.That(!page.InNodes, "Vorbereitung: das neue Bild rechnet im Stapel");

            Stroke(frame, 180, 120, 210, 130);
            Pump(() => false, 0.3);

            var paint = PaintedIn(layers);
            Check.That(paint.Count == 1 && paint[0].At(195, 125) > 0.5f,
                       "der Strich im neuen Bild landet in einer Maskenebene des neuen Projekts", $"{paint.Count} gemalte Masken");
            Check.That(paint.All(p => p.At(40, 32) < 0.01f), "die Maske des alten Projekts ist nicht mitgekommen");
            Check.That(nodeMask?.Mask.PaintFor(1) is not { } old || old.At(195, 125) < 0.01f,
                       "und der neue Strich ist nicht in die Maske des alten Projekts gegangen");

            // Dasselbe aus dem Stapel heraus: eine gemalte Maskenebene, dann ein neues Bild.
            Show(page, third);
            Stroke(frame, 180, 120, 210, 130);
            Pump(() => false, 0.3);

            paint = PaintedIn(layers);
            Check.That(paint.Count == 1 && paint.All(p => p.At(40, 32) < 0.01f),
                       "aus dem Stapel in ein neues Bild: nur die eigene Maske", $"{paint.Count}");
        });
    }

    /// <summary>
    /// Zwei Renders derselben Szene, dieselbe Bildnummer, dasselbe Objekt - einmal links, einmal
    /// rechts. Kryptomatte-Kennungen sind Hashes der Namen, im zweiten Render steht also dieselbe
    /// Kennung an anderer Stelle. Der Pinsel muss die Deckung aus der offenen Datei lesen.
    /// </summary>
    private static void ObjectLimitReadsTheOpenFile()
    {
        Check.Group("Wechsel: der objektgebundene Pinsel liest das Objekt der offenen Datei");

        WithPage((page, root) =>
        {
            string left = Exr(root, "version1", sphereLeft: true);
            string right = Exr(root, "version2", sphereLeft: false);

            // 8 x 5 Bildpunkte, Maskenpunkte zu je vier: zwei Felder, links und rechts.
            const int cells = 2;

            Show(page, left);
            var before = page.ObjectLimitAt(1, 2) is { } a ? PaintedMask.Unpack(a, cells) : null;
            Check.That(before is [255, 0], "Vorbereitung: im ersten Render deckt die Kugel links",
                       before is null ? "keine Deckung" : string.Join(",", before));

            Show(page, right);
            var after = page.ObjectLimitAt(6, 2) is { } b ? PaintedMask.Unpack(b, cells) : null;
            Check.That(after is [0, 255], "im zweiten Render deckt dieselbe Kugel rechts - nicht die gemerkte von links",
                       after is null ? "keine Deckung" : string.Join(",", after));
        });
    }

    /// <summary>Eine Pipette des Tonwerts wartet, dann kommt ein anderes Bild.</summary>
    private static void LevelsPickEndsWithTheImage()
    {
        Check.Group("Wechsel: eine wartende Tonwert-Pipette endet mit dem Bild");

        WithPage((page, root) =>
        {
            string first = Png(root, "eins", "render_0001.png");
            string second = Png(root, "zwei", "shot_0001.png");

            var panel = (GradingPanel)page.FindName("Tools");
            var column = (ToolColumn)page.FindName("MouseTools");

            Show(page, first);
            var black = (ToggleButton)panel.FindName("LevelsPickBlack");
            black.IsChecked = true;
            Pump(() => false, 0.1);
            Check.That(page.LevelsPicking && column.Tool == AtelierTool.Pick, "Vorbereitung: die Pipette wartet");

            Show(page, second);
            Check.That(!page.LevelsPicking && black.IsChecked != true,
                       "nach dem Wechsel wartet sie nicht mehr - sie gehoerte zum Tonwert des alten Bildes");
            Check.That(column.Tool == AtelierTool.Move, "und die Maus ist wieder, was sie vor der Pipette war", $"{column.Tool}");
        });
    }

    /// <summary>
    /// Knoten heissen in jedem Graphen n1, n2, ... - die Vorschauen des alten Projekts duerfen
    /// im neuen nicht unter denselben Namen stehen bleiben.
    /// </summary>
    private static void PreviewsBelongToTheProject()
    {
        Check.Group("Wechsel: Knotenvorschauen gehoeren zu ihrem Projekt");

        WithPage((page, root) =>
        {
            string first = Png(root, "eins", "render_0001.png");
            string second = Png(root, "zwei", "shot_0001.png");

            Show(page, first);
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.5);

            var previews = (NodePreviews)typeof(AtelierPage).GetField("_previews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var ids = page.Graph!.Nodes.Select(n => n.Id).ToList();
            var shown = ids.Where(id => previews.For(id) is not null).ToList();
            Check.That(shown.Count > 0, "Vorbereitung: im ersten Projekt haben Knoten ihre Vorschau", $"{shown.Count}");

            Show(page, second);
            var left = ids.Where(id => previews.For(id) is not null).ToList();
            Check.That(left.Count == 0, "nach dem Wechsel steht keine Vorschau des alten Projekts mehr unter seinen Knotennamen",
                       string.Join(", ", left));
        });
    }

    // ------------------------------------------------------------ Bausteine

    private static void WithPage(Action<AtelierPage, string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "frameflip-wechsel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            test(page, root);
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void Show(AtelierPage page, string path)
    {
        string name = Path.GetFileName(path);
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("FileText")).Text == name &&
                   ((TextBlock)page.FindName("SourceText")).Text.Length > 0 &&
                   page.Projects.Current is { } key && key.Equals(SequenceKey.Of(path)));
        Pump(() => false, 0.3);
    }

    private static List<PaintedMask> PaintedIn(LayerPanel layers)
        => layers.Stack.Layers.Where(l => l.Mask.Kind == MaskKind.Painted)
                 .Select(l => l.Mask.PaintFor(1)).OfType<PaintedMask>().ToList();

    /// <summary>Druecken, Ziehen, Loslassen - ueber die Tasten des Rahmens, wie die Maus sie ausloest.</summary>
    private static void Stroke(PlacementAdorner frame, float x0, float y0, float x1, float y1)
    {
        var type = typeof(PlacementAdorner);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var down = type.GetMethod("PaintDown", flags)!;
        var move = type.GetMethod("PaintMove", flags)!;
        var up = type.GetMethod("PaintUp", flags)!;

        MouseButtonEventArgs Args(RoutedEvent routed)
            => new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed };

        down.Invoke(frame, new object[] { Args(UIElement.MouseLeftButtonDownEvent), x0, y0, false });

        for (int i = 1; i <= 8; i++)
            move.Invoke(frame, new object[] { x0 + (x1 - x0) * i / 8f, y0 + (y1 - y0) * i / 8f, 1f });

        up.Invoke(frame, new object[] { Args(UIElement.MouseLeftButtonUpEvent) });
    }

    private static string Png(string root, string folder, string name)
    {
        string path = Path.Combine(root, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var pixels = new byte[Width * Height * 4];
        for (int i = 0; i < Width * Height; i++)
        {
            pixels[i * 4] = (byte)(i % Width);
            pixels[i * 4 + 1] = 90;
            pixels[i * 4 + 2] = (byte)(i / Width);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4)));
        using var file = File.Create(path);
        encoder.Save(file);

        return path;
    }

    /// <summary>Ein Render mit Kryptomatte: die Kugel links oder rechts, daneben der Boden.</summary>
    private static string Exr(string root, string folder, bool sphereLeft)
    {
        string path = Path.Combine(root, folder, "render_0001.exr");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var manifest = """{"Kugel":"3f800000","Boden":"40000000"}""";

        File.WriteAllBytes(path, ExrMultipartInvariants.Build(new[]
        {
            new ExrMultipartInvariants.Part("Bild", "scanlineimage", new[] { "B", "G", "R" }, (c, x, y) => 0.2f + c * 0.1f,
                new Dictionary<string, string>
                {
                    ["cryptomatte/abc1234/name"] = "CryptoObject",
                    ["cryptomatte/abc1234/hash"] = "MurmurHash3_32",
                    ["cryptomatte/abc1234/conversion"] = "uint32_to_float32",
                    ["cryptomatte/abc1234/manifest"] = manifest,
                }),
            new ExrMultipartInvariants.Part("CryptoObject00", "scanlineimage",
                new[] { "CryptoObject00.a", "CryptoObject00.b", "CryptoObject00.g", "CryptoObject00.r" },
                (c, x, y) => c == 3 ? ((x < 4) == sphereLeft ? 1f : 2f) : c == 2 ? 1f : 0f),
        }));

        return path;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
