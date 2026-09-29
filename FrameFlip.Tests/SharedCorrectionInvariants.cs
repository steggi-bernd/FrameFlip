using System.IO;
using System.Reflection;
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
/// Eine gemeinsame Korrektur ueber mehreren Ebenen (docs/Atelier-Arbeitsablauf.md, C2b): eine
/// Gruppe fuer sich, obenauf eine Einstellungsebene. Das Zusammenlegen aendert das Bild nicht,
/// die Korrektur trifft nur diese Ebenen - im Stapel und im Graphen gleich.
/// </summary>
public static class SharedCorrectionInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    private const int W = 8, H = 2;

    public static void Run()
    {
        TheComposerKeepsItToThem();
        ThePageGroupsThem();
    }

    private static FloatFrame Flat(float value, float[]? alpha = null)
    {
        var frame = new FloatFrame
        {
            Width = W, Height = H, R = new float[W * H], G = new float[W * H], B = new float[W * H], A = alpha,
        };

        Array.Fill(frame.R, value);
        Array.Fill(frame.G, value);
        Array.Fill(frame.B, value);
        return frame;
    }

    private static void TheComposerKeepsItToThem()
    {
        Check.Group("Gemeinsame Korrektur: Zusammensetzen");

        var sources = new Dictionary<string, FloatFrame> { [""] = Flat(0.2f), ["a"] = Flat(0.1f), ["b"] = Flat(0.05f) };

        LayerStack Passes(bool isolated, float exposure)
        {
            var a = new ImageLayer { Content = LayerContent.Pass, Source = "a", Mode = BlendMode.Normal };
            var b = new ImageLayer { Content = LayerContent.Pass, Source = "b", Mode = BlendMode.Add };
            var correction = new ImageLayer { Content = LayerContent.Adjustment, Exposure = exposure };

            return new LayerStack
            {
                Layers =
                {
                    new ImageLayer { Content = LayerContent.Pass, Source = "" },
                    new ImageLayer { Content = LayerContent.Group, Mode = BlendMode.Add, Isolated = isolated, Children = { a, b, correction } },
                },
            };
        }

        var plain = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer { Content = LayerContent.Pass, Source = "a", Mode = BlendMode.Add },
                new ImageLayer { Content = LayerContent.Pass, Source = "b", Mode = BlendMode.Add },
            },
        };

        float before = LayerComposer.Compose(plain, sources)!.R[0];
        float grouped = LayerComposer.Compose(Passes(isolated: true, exposure: 0f), sources)!.R[0];
        float corrected = LayerComposer.Compose(Passes(isolated: true, exposure: 1f), sources)!.R[0];
        // Zum Vergleich eine Gruppe, wie sie bisher jede war: Sie wirkt hindurch, ihre Passe addieren
        // sich auf das, was darunter liegt, und die Korrektur darin sieht alles.
        var passThrough = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Children =
                    {
                        new ImageLayer { Content = LayerContent.Pass, Source = "a", Mode = BlendMode.Add },
                        new ImageLayer { Content = LayerContent.Pass, Source = "b", Mode = BlendMode.Add },
                        new ImageLayer { Content = LayerContent.Adjustment, Exposure = 1f },
                    },
                },
            },
        };
        float through = LayerComposer.Compose(passThrough, sources)!.R[0];

        Check.That(Near(before, 0.35f) && Near(grouped, before), "Passe in einer Gruppe fuer sich: dasselbe Bild wie vorher", $"{before} / {grouped}");
        Check.That(Near(corrected, 0.5f), "eine Blende heller darin: nur die beiden Passe werden heller (0,2 + 2 · 0,15)", $"{corrected}");
        Check.That(Near(through, 0.7f), "zum Vergleich eine Gruppe, die hindurchwirkt: dort traefe es auch die Ebene darunter", $"{through}");

        // Bildebenen mit Deckung: halb deckend links, gar nicht rechts.
        var alpha = new float[W * H];
        for (int i = 0; i < W * H; i++) alpha[i] = i % W < W / 2 ? 0.5f : 0f;

        var images = new Dictionary<string, FloatFrame> { [""] = Flat(0.2f), ["c"] = Flat(0.8f, alpha) };

        LayerStack Images(bool grouped, float exposure)
        {
            var c = new ImageLayer { Content = LayerContent.Image, Source = "c", Mode = BlendMode.Normal };
            var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

            if (!grouped) stack.Layers.Add(c);
            else stack.Layers.Add(new ImageLayer
            {
                Content = LayerContent.Group, Isolated = true,
                Children = { c, new ImageLayer { Content = LayerContent.Adjustment, Exposure = exposure } },
            });

            return stack;
        }

        var loose = LayerComposer.Compose(Images(false, 0f), images)!;
        var inGroup = LayerComposer.Compose(Images(true, 0f), images)!;
        var lit = LayerComposer.Compose(Images(true, 1f), images)!;

        Check.That(Near(loose.R[0], 0.5f) && Near(inGroup.R[0], loose.R[0]) && Near(inGroup.R[W - 1], 0.2f),
                   "eine halb deckende Bildebene in einer Gruppe fuer sich: dasselbe Bild, auch am Rand der Deckung",
                   $"{loose.R[0]} / {inGroup.R[0]}");
        Check.That(Near(lit.R[0], 0.2f + (1.6f - 0.2f) * 0.5f) && Near(lit.R[W - 1], 0.2f),
                   "die Korrektur darin trifft die Ebene, wo sie deckt - daneben bleibt das Bild darunter", $"{lit.R[0]} / {lit.R[W - 1]}");

        // Im Graphen dasselbe: Umwandeln und Rechnen ergibt Bildpunkt fuer Bildpunkt dasselbe Bild.
        foreach (var (name, stack, frames) in new[]
                 {
                     ("Passe", Passes(isolated: true, exposure: 1f), sources),
                     ("Bildebene", Images(true, 1f), images),
                 })
        {
            var view = new StandardViewTransform();
            byte[] composed = Draw(LayerComposer.Compose(stack, frames)!, view);
            byte[] graph = Render(StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack()), frames, view);

            int worst = 0;
            for (int i = 0; i < composed.Length; i++) worst = Math.Max(worst, Math.Abs(composed[i] - graph[i]));

            Check.That(worst <= 1, $"{name}: Stapel und Knoten rechnen dieselbe Gruppe fuer sich", $"groesste Abweichung {worst}");
        }
    }

    private static void ThePageGroupsThem()
    {
        Check.Group("Gemeinsame Korrektur: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-shared-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int PW = 40, PH = 20;
        string basePath = Png(Path.Combine(root, "render_0001.png"), PW, PH, x => (60, 60, 60, 255));
        string left = Png(Path.Combine(root, "links.png"), PW, PH, x => x < PW / 2 ? ((byte)200, (byte)40, (byte)40, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        string middle = Png(Path.Combine(root, "mitte.png"), PW, PH, x => x is >= 10 and < 20 ? ((byte)40, (byte)200, (byte)40, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        string far = Png(Path.Combine(root, "weit.png"), PW, PH, x => x is >= 30 ? ((byte)40, (byte)40, (byte)200, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        FloatFrame Whole() => (FloatFrame)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page)!;
        void Correct() => typeof(AtelierPage).GetMethod("OnTargetCorrect", flags)!.Invoke(page, new object[] { page, new RoutedEventArgs() });

        try
        {
            window.Show();
            page.Open(basePath);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);

            layers.AddImage(left);
            layers.AddImage(middle);
            layers.AddImage(far);
            Pump(() => false, 0.5);

            var all = layers.Stack.Layers.ToList();
            var (l, m, f) = (all[^3], all[^2], all[^1]);

            // Nicht nebeneinander: links und weit - dazwischen liegt die Mitte.
            layers.SelectLayers(new[] { l, f });
            Correct();
            Check.That(layers.Stack.Layers.Count == all.Count && ((TextBlock)page.FindName("TargetNote")).Text == T("S_SharedCorrectionNeighbours"),
                       "Ebenen, die nicht nebeneinander liegen: keine Gruppe, und die Zeile sagt warum");

            var before = (float[])Whole().R.Clone();

            layers.SelectLayers(new[] { l, m });
            Check.That(layers.SelectedLayers.Count == 2, "(Vorbedingung: zwei Ebenen gewaehlt)");
            Correct();
            Pump(() => false, 0.5);

            var group = layers.Stack.Layers.FirstOrDefault(x => x.Content == LayerContent.Group);
            var correction = group?.Children.LastOrDefault();
            Check.That(group is { Isolated: true } && group.Children.Count == 3 && ReferenceEquals(group.Children[0], l) &&
                       ReferenceEquals(group.Children[1], m) && correction?.Content == LayerContent.Adjustment &&
                       ReferenceEquals(layers.Selection, correction) && layers.Stack.Layers.Contains(f),
                       "+ Korrektur mit zwei Ebenen: eine Gruppe fuer sich an ihrem Platz, obenauf die Korrektur, und sie ist gewaehlt");

            var after = Whole().R;
            float worst = 0f;
            for (int i = 0; i < before.Length; i++) worst = MathF.Max(worst, MathF.Abs(before[i] - after[i]));
            Check.That(worst < 1e-4f, "das Zusammenlegen aendert das Bild nicht", $"{worst}");

            correction!.Exposure = 1f;
            layers.SetVisible(correction, true);
            Pump(() => false, 0.5);

            var lit = Whole().R;
            int row = PH / 2 * PW;
            Check.That(lit[row + 5] > before[row + 5] * 1.5f && MathF.Abs(lit[row + 25] - before[row + 25]) < 1e-4f &&
                       MathF.Abs(lit[row + 35] - before[row + 35]) < 1e-4f,
                       "eine Blende heller: links (die beiden Ebenen) heller, das Grundbild und die weite Ebene nicht",
                       $"{before[row + 5]:0.000}->{lit[row + 5]:0.000}, {before[row + 25]:0.000}->{lit[row + 25]:0.000}");

            layers.ShowRowMenu(group!);
            bool offered = layers.RowMenu!.Items.Contains(T("S_GroupIsolated"));
            layers.RowMenu.Close();
            Check.That(offered, "am Menue der Gruppe: der Schalter fuer sich");

            layers.AddAdjustment();
            var adjustment = layers.Selection!;
            layers.SelectLayers(new[] { f, adjustment });
            Correct();
            Check.That(((TextBlock)page.FindName("TargetNote")).Text == T("S_SharedCorrectionNoAdjustment"),
                       "mit einer Einstellungsebene: keine Gruppe - sie traegt kein eigenes Bild");
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

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.002f;

    private static byte[] Draw(FloatFrame frame, IViewTransform view)
    {
        var pixels = new byte[W * H * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, view, PreparedGrading.None, buffer, W * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static byte[] Render(NodeGraph graph, Dictionary<string, FloatFrame> sources, IViewTransform view)
    {
        var inputs = new GraphInputs { Sources = sources, Data = new Dictionary<PassNeed, FloatFrame?>(), View = view, Step = 1, Number = 0 };
        var pixels = new byte[W * H * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            GraphEvaluator.Render(graph, inputs, buffer, W * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static string Png(string path, int width, int height, Func<int, (byte R, byte G, byte B, byte A)> at)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            var (r, g, b, a) = at(i % width);
            pixels[i * 4] = b;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = r;
            pixels[i * 4 + 3] = a;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
