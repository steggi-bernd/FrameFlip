using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Der Export im Knotenmodus schreibt dieselben Dateien wie der Stapel, aus dem der
/// Graph kam.
///
/// Das ist die Zusage, die den Knotenmodus erst benutzbar macht: Wer umschaltet und
/// exportiert, darf keine anderen Bilder bekommen als vorher. Und die Stelle, an der
/// es am ehesten schiefgeht, ist nicht die Rechnung - die prueft NodeParityInvariants
/// -, sondern der Weg drumherum: welche Dateien gelesen werden, welche Bildnummer das
/// Korn bekommt, und dass mehrere Bilder zugleich gerechnet werden, jedes mit seiner
/// eigenen Kopie des Graphen.
/// </summary>
public static class NodeExportInvariants
{
    private const int Width = 40;
    private const int Height = 28;

    public static void Run()
    {
        Check.Group("Knoten: der Export schreibt, was der Stapel schriebe");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-knotenexport-" + Guid.NewGuid().ToString("N")[..8]);
        string input = Path.Combine(root, "quelle");
        Directory.CreateDirectory(input);

        try
        {
            var frames = Enumerable.Range(1, 5)
                .Select(i => Png(Path.Combine(input, $"bild_{i:0000}.png"), i))
                .ToArray();

            string logo = Png(Path.Combine(input, "logo.png"), 99);

            var stack = new LayerStack
            {
                Layers =
                {
                    new ImageLayer { Content = LayerContent.Pass, Source = "" },
                    new ImageLayer
                    {
                        Content = LayerContent.Image, Source = logo, FollowSequence = false,
                        Mode = BlendMode.Screen, Opacity = 0.7f,
                        Place = new LayerTransform { Scale = 0.5f, OffsetX = 0.15f },
                    },
                    new ImageLayer
                    {
                        Content = LayerContent.Adjustment,
                        Adjustments = new ImageAdjustments { Exposure = 0.4, Saturation = 0.7 },
                    },
                },
            };

            var adjust = new ImageAdjustments { Exposure = 0.2, Contrast = 1.15 };

            // Das Korn haengt an der Bildnummer - kaeme sie im Knotenmodus anders an,
            // saehe jedes Bild anders aus.
            var picture = new GradingStack
            {
                Optics = { new VignetteTool { Amount = -0.4f }, new GrainTool { Amount = 0.5f } },
                Local = { new ClarityTool { Amount = 0.4f, Reach = 4 } },
            };

            foreach (var format in new[] { GradeOutputFormat.Png8, GradeOutputFormat.Png16 })
            {
                string viaStack = Path.Combine(root, $"stapel-{format}");
                string viaGraph = Path.Combine(root, $"knoten-{format}");

                var request = new GradeBatchRequest
                {
                    Frames = frames,
                    OutputDirectory = viaStack,
                    Format = format,
                    Adjustments = adjust,
                    Grading = picture.Clone(),
                    Layers = stack.Clone(),
                    View = new StandardViewTransform(),
                    MaxWorkers = 3,
                };

                var fromStack = GradeBatch.Run(request);

                var fromGraph = GradeBatch.Run(request with
                {
                    OutputDirectory = viaGraph,
                    Graph = StackToGraph.Convert(stack, adjust, picture),

                    // Im Knotenmodus gilt der Graph allein - was hier noch steht, darf
                    // nicht ein zweites Mal gerechnet werden.
                    Layers = null,
                    Grading = new GradingStack(),
                    Adjustments = ImageAdjustments.Neutral,
                });

                Check.That(fromStack.Complete && fromStack.Written == frames.Length &&
                           fromGraph.Complete && fromGraph.Written == frames.Length,
                           $"{format}: beide Laeufe schreiben jedes Bild",
                           $"Stapel {fromStack.Written}, Knoten {fromGraph.Written} {string.Join("; ", fromGraph.Failures)}");

                int differ = 0;

                foreach (string frame in frames)
                {
                    string name = Path.GetFileName(GradeBatch.TargetFor(frame, request));
                    string a = Path.Combine(viaStack, name), b = Path.Combine(viaGraph, name);

                    if (!File.Exists(a) || !File.Exists(b) || !Pixels(a).AsSpan().SequenceEqual(Pixels(b)))
                        differ++;
                }

                Check.That(differ == 0, $"{format}: und die Bilder sind dieselben", $"{differ} verschieden");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>Ein farbiges Bild mit weicher Deckung, je Nummer ein wenig anders.</summary>
    private static string Png(string path, int number)
    {
        int stride = Width * 4;
        var pixels = new byte[stride * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = y * stride + x * 4;
                pixels[at] = (byte)((x * 9 + number * 13) % 256);
                pixels[at + 1] = (byte)((y * 11 + x * 3) % 256);
                pixels[at + 2] = (byte)(200 - (x + y + number) % 120);
                pixels[at + 3] = (byte)(x < 6 ? 90 : 255);
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

    private static byte[] Pixels(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

        int stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        return pixels;
    }
}
