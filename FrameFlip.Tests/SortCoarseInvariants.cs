using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Pixel Sort in der groben Vorschau (docs/Atelier-Arbeitsablauf.md, C7c): Beim Ziehen eines
/// Reglers sortiert es auf dem Gitter, mit im Verhaeltnis kuerzeren Laeufen - und sieht dabei
/// aus wie das volle Bild. Die Fehlerdiffusion bleibt beim Ziehen aus, wie bisher.
/// </summary>
public static class SortCoarseInvariants
{
    private const int W = 128, H = 16, Step = 4;

    public static void Run()
    {
        Check.Group("Pixel Sort beim Ziehen: grobe Fassung");

        // Jede Zeile ein Verlauf von hell nach dunkel - Sortieren kehrt ihn um.
        var full = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte v = (byte)(255 - x * 2);
                int i = (y * W + x) * 4;
                full[i] = v; full[i + 1] = v; full[i + 2] = v; full[i + 3] = 255;
            }

        // Laeufe von hoechstens 16 Bildpunkten: auf dem Gitter sind das 4 Punkte.
        var sort = new SortTool { Low = 0f, High = 1f, Longest = 16 };
        sort.Prepare();

        var sortedFull = (byte[])full.Clone();
        Pass(sortedFull, W, H, p => sort.Apply(p, W, H, W * 4));

        var grid = Subsample(full);
        var coarse = (byte[])grid.Clone();
        Pass(coarse, W / Step, H, p => sort.ApplyCoarse(p, W / Step, H, W / Step * 4, 0, Step));

        var unscaled = (byte[])grid.Clone();
        Pass(unscaled, W / Step, H, p => sort.Apply(p, W / Step, H, W / Step * 4));

        var expected = Subsample(sortedFull);
        double near = MeanDifference(coarse, expected), off = MeanDifference(unscaled, expected);
        double changed = MeanDifference(coarse, grid);

        // Innerhalb eines Laufs liegen Gitter und volles Bild bis zu drei Bildpunkte versetzt - bei
        // diesem Verlauf sind das hoechstens 6 Stufen. Ungekuerzte Laeufe laegen weit darueber.
        Check.That(changed > 5 && near <= 6 && off > near * 4,
                   "auf dem Gitter mit Laeufen im Verhaeltnis: fast das volle Ergebnis - mit ungekuerzten Laeufen deutlich anders",
                   $"sortiert {changed:0.0}, grob {near:0.0}, ungekuerzt {off:0.0}");

        Check.That(sort.Longest == 16, "die grobe Fassung laesst die Einstellung, wie sie war");

        // Im Stapel: Die grobe Vorschau zeigt Pixel Sort jetzt, die Fehlerdiffusion weiter nicht.
        var frame = FloatFrame.FromBgra32(full, W, H, W * 4);
        var view = new StandardViewTransform();
        byte[] plain = Draw(frame, view, new GradingStack(), Step);
        byte[] withSort = Draw(frame, view, new GradingStack { Frame = { new SortTool { Low = 0f, High = 1f } } }, Step);
        byte[] withDiffusion = Draw(frame, view, new GradingStack { Frame = { new DiffusionTool { Amount = 1f, Levels = 2 } } }, Step);

        Check.That(MeanDifference(withSort, plain) > 5, "Stapel, beim Ziehen: Pixel Sort ist in der groben Vorschau zu sehen");
        Check.That(MeanDifference(withDiffusion, plain) == 0, "die Fehlerdiffusion bleibt beim Ziehen aus - auf dem Gitter waere sie eine andere");

        // Im Knotenmodus dasselbe: ein Pixel-Sort-Knoten vor der Ausgabe, grob gerechnet.
        var sources = new Dictionary<string, FloatFrame> { [""] = frame };
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" } } };

        byte[] Graph(IFramePass? pass)
        {
            var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());

            if (pass is not null)
            {
                var node = graph.Add(new FramePassNode { Pass = pass });
                var output = graph.Output!;
                var into = graph.Into(output.Id, "Bild")!;
                var before = graph.Find(into.From)!;

                graph.Links.Remove(into);
                graph.Connect(before, into.Output, node, "Bild");
                graph.Connect(node, "Bild", output, "Bild");
            }

            var inputs = new GraphInputs
            {
                Sources = sources, Data = new Dictionary<PassNeed, FloatFrame?>(), View = view, Step = Step, Number = 0,
            };

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

        byte[] nodePlain = Graph(null);
        Check.That(MeanDifference(Graph(new SortTool { Low = 0f, High = 1f }), nodePlain) > 5,
                   "Knotenmodus, beim Ziehen: der Pixel-Sort-Knoten wirkt in der groben Vorschau");
        Check.That(MeanDifference(Graph(new DiffusionTool { Amount = 1f, Levels = 2 }), nodePlain) == 0,
                   "der Knoten der Fehlerdiffusion bleibt beim Ziehen aus");
    }

    private static byte[] Draw(FloatFrame frame, IViewTransform view, GradingStack stack, int step)
    {
        var pixels = new byte[W * H * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, view, stack.Prepare(), buffer, W * 4, step);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static unsafe void Pass(byte[] pixels, int width, int height, Action<IntPtr> run)
    {
        fixed (byte* start = pixels) run((IntPtr)start);
    }

    /// <summary>Jeder vierte Bildpunkt einer Zeile - das Gitter der groben Vorschau, ohne Rand.</summary>
    private static byte[] Subsample(byte[] full)
    {
        int gw = W / Step;
        var grid = new byte[gw * H * 4];

        for (int y = 0; y < H; y++)
            for (int gx = 0; gx < gw; gx++)
                Array.Copy(full, (y * W + gx * Step) * 4, grid, (y * gw + gx) * 4, 4);

        return grid;
    }

    private static double MeanDifference(byte[] a, byte[] b)
    {
        long sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / (double)a.Length;
    }
}
