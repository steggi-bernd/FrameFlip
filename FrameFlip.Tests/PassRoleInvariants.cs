using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Der Mischmodus einer neuen Ebene: aus dem Namen des Passes oder der Datei, sonst aus
/// dem Inhalt - und nur beim Anlegen, nie danach.
/// </summary>
public static class PassRoleInvariants
{
    public static void Run()
    {
        TheNameTells();
        TheContentTellsWhenTheNameDoesNot();
        OnlyWhenCreated();
    }

    private static void TheNameTells()
    {
        Check.Group("Passrollen: der Name");

        (string Name, bool Linear, BlendMode? Want)[] cases =
        {
            ("ViewLayer.Glare", true, BlendMode.Add),
            ("ViewLayer.FogGlow", true, BlendMode.Add),
            ("ViewLayer.DiffDir", true, BlendMode.Add),
            ("ViewLayer.Emit", true, BlendMode.Add),
            ("ViewLayer.Environment", true, BlendMode.Add),
            ("ViewLayer.GlossCol", true, BlendMode.Multiply),
            ("ViewLayer.AO", true, BlendMode.Multiply),
            ("ViewLayer.Shadow", true, BlendMode.Multiply),
            (@"D:\fx\glare_0012.png", false, BlendMode.Screen),
            (@"D:\fx\Bloom.0012.jpg", false, BlendMode.Screen),
            (@"D:\fx\bloom_0012.exr", true, BlendMode.Add),
            ("ViewLayer.Mist", true, null),
            ("ViewLayer.Depth", true, null),
            (@"D:\renders\Collection_0001.png", false, null),
            (@"D:\renders\render_0001.png", false, null),
        };

        foreach (var (name, linear, want) in cases)
            Check.That(PassRoles.ByName(name, linear) == want, $"{name} -> {want?.ToString() ?? "keine Aussage"}",
                       PassRoles.ByName(name, linear)?.ToString() ?? "keine Aussage");
    }

    private static void TheContentTellsWhenTheNameDoesNot()
    {
        Check.Group("Passrollen: der Inhalt, wenn der Name schweigt");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-roles-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string Write(string name, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
        {
            const int W = 320, H = 180;
            var bytes = new byte[W * H * 4];

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var (r, g, b, a) = pixel(x, y);
                    int i = (y * W + x) * 4;
                    bytes[i] = b; bytes[i + 1] = g; bytes[i + 2] = r; bytes[i + 3] = a;
                }

            string path = Path.Combine(folder, name);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, bytes, W * 4)));
            using (var file = File.Create(path)) encoder.Save(file);
            return path;
        }

        // Ein heller Fleck auf Schwarz, ohne Deckung - wie ein Glare ohne sprechenden Namen.
        string spot = Write("fx_0001.png", (x, y) =>
        {
            double d = Math.Sqrt((x - 160) * (x - 160) + (y - 90) * (y - 90));
            byte v = (byte)Math.Clamp(255 - d * 6, 0, 255);
            return (v, v, v, 255);
        });

        string photo = Write("shot_0001.png", (x, y) => ((byte)(x * 255 / 320), (byte)(y * 255 / 180), 90, 255));
        string logo = Write("logo.png", (x, y) => x > 120 && x < 200 && y > 60 && y < 120 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        string bloom = Write("bloom_0001.png", (x, y) => ((byte)(x * 255 / 320), 120, 200, 255));

        try
        {
            Check.That(ImageProbe.ModeFor(spot) == BlendMode.Screen, "Licht auf Schwarz ohne Deckung: negativ multiplizieren");
            Check.That(ImageProbe.ModeFor(photo) == BlendMode.Normal, "ein Bild mit Inhalt bleibt Normal");
            Check.That(ImageProbe.ModeFor(logo) == BlendMode.Normal, "ein Logo mit Deckung bleibt Normal, auch wenn es meist leer ist");
            Check.That(ImageProbe.ModeFor(bloom) == BlendMode.Screen, "der Name geht vor dem Inhalt");
            Check.That(ImageProbe.ModeFor(Path.Combine(folder, "fehlt.png")) == BlendMode.Normal, "eine fehlende Datei ist Normal, kein Fehler");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static void OnlyWhenCreated()
    {
        Check.Group("Passrollen: nur beim Anlegen");

        var panel = new LayerPanel();
        var window = new Window
        {
            Content = panel, Width = 360, Height = 700, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();

            var add = typeof(LayerPanel).GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                                                  new[] { typeof(ExrPass?) })!;

            ExrPass Pass(string leaf) => new("ViewLayer." + leaf, "ViewLayer." + leaf + ".R", "ViewLayer." + leaf + ".G", "ViewLayer." + leaf + ".B", null, false);

            add.Invoke(panel, new object?[] { Pass("DiffDir") });
            add.Invoke(panel, new object?[] { Pass("Glare") });
            add.Invoke(panel, new object?[] { Pass("AO") });

            var layers = panel.Stack.Layers;

            Check.That(layers[0].Mode == BlendMode.Normal && layers[1].Mode == BlendMode.Add && layers[2].Mode == BlendMode.Multiply,
                       "die unterste traegt, Glare addiert, AO multipliziert",
                       string.Join(", ", layers.Select(l => l.Mode)));

            // Von Hand umgestellt - und dann alles, was den Modus nicht anfassen darf.
            layers[1].Mode = BlendMode.Overlay;

            var json = System.Text.Json.JsonSerializer.Serialize(panel.Stack);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<LayerStack>(json)!;

            Check.That(loaded.Layers[1].Mode == BlendMode.Overlay, "nach Speichern und Laden bleibt der Modus, den jemand gewaehlt hat");

            var graph = StackToGraph.Convert(loaded, null, null);
            var mixes = graph.Nodes.OfType<MixNode>().ToList();

            Check.That(mixes.Count(m => m.Mode == BlendMode.Overlay) == 1 && mixes.Any(m => m.Mode == BlendMode.Multiply),
                       "und nach dem Umwandeln in Knoten", string.Join(", ", mixes.Select(m => m.Mode)));

            var copy = loaded.Layers[1].Clone();
            Check.That(copy.Mode == BlendMode.Overlay, "ein Duplikat uebernimmt den aktuellen Modus");

            var mix = mixes.First(m => m.Mode == BlendMode.Overlay);
            var twin = NodeEdits.Duplicate(graph, mix) as MixNode;
            Check.That(twin?.Mode == BlendMode.Overlay, "auch im Graphen");
        }
        finally
        {
            window.Close();
        }
    }
}
