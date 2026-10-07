using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Refactoring-Studio, S0, Befund 2: Die Bildnummer muss bis in die Komposition reichen,
/// auch beim Export. Eine je Bild gemalte Maske (entsperrt) waehlt ihren Anstrich nach
/// dieser Nummer - fehlt sie, nimmt jedes Bild den Anstrich von Bild 0.
/// </summary>
public static class ExportFrameNumberInvariants
{
    private const int Width = 64, Height = 32;

    public static void Run()
    {
        Check.Group("Export: jedes Bild mit seinem eigenen Anstrich");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-nummer-" + Guid.NewGuid().ToString("N")[..8]);
        string input = Path.Combine(root, "quelle");
        Directory.CreateDirectory(input);

        try
        {
            // Zwei gleiche Bilder - was sie im Export unterscheidet, kommt allein aus der Maske.
            var frames = new[] { Path.Combine(input, "f_0001.png"), Path.Combine(input, "f_0002.png") };
            foreach (string frame in frames) WriteGrey(frame);

            foreach (bool nodes in new[] { false, true })
            {
                string output = Path.Combine(root, nodes ? "knoten" : "stapel");
                Directory.CreateDirectory(output);

                var stack = Stack();
                var request = new GradeBatchRequest
                {
                    Frames = frames,
                    OutputDirectory = output,
                    Format = GradeOutputFormat.Png8,
                    Adjustments = ImageAdjustments.Neutral,
                    Grading = new GradingStack(),
                    View = new StandardViewTransform(),
                    Layers = nodes ? null : stack,
                    Graph = nodes ? StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack()) : null,
                    MaxWorkers = 1,
                };

                var result = GradeBatch.Run(request);
                string way = nodes ? "Knoten" : "Stapel";

                if (result.Written != 2)
                {
                    Check.That(false, $"{way}: beide Bilder geschrieben", $"{result.Written}, {string.Join("; ", result.Failures)}");
                    continue;
                }

                var (left1, right1) = Halves(Path.Combine(output, "f_0001.png"));
                var (left2, right2) = Halves(Path.Combine(output, "f_0002.png"));

                Check.That(left1 < right1 - 20 && right2 < left2 - 20,
                           $"{way}: Bild 1 ist links dunkel, Bild 2 rechts - wie gemalt",
                           $"Bild 1: {left1:0}/{right1:0}, Bild 2: {left2:0}/{right2:0}");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Das Bild, dazu eine abdunkelnde Einstellungsebene mit entsperrter, je Bild gemalter Maske.</summary>
    private static LayerStack Stack()
    {
        var mask = new LayerMask { Kind = MaskKind.Painted, PaintLocked = false };

        var first = mask.PaintOn(1, Width, Height);
        var second = mask.PaintOn(2, Width, Height);

        // Bild 1 links, Bild 2 rechts - hart und voll, damit die Haelften eindeutig sind.
        for (float y = 0; y < Height; y += 4)
        {
            for (float x = 0; x < Width / 2f - 6; x += 4)
            {
                first.Stroke(x, y, 6f, 1f, 1f, hardness: 1f);
                second.Stroke(Width - 1 - x, y, 6f, 1f, 1f, hardness: 1f);
            }
        }

        first.Keep();
        second.Keep();

        return new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "" },
                new ImageLayer
                {
                    Content = LayerContent.Adjustment,
                    Mode = BlendMode.Normal,
                    Adjustments = new ImageAdjustments { Exposure = -2 },
                    Tools = new GradingStack(),
                    Mask = mask,
                },
            },
        };
    }

    /// <summary>Mittlere Helligkeit der linken und der rechten Viertelflaeche - ohne die Mitte.</summary>
    private static (double Left, double Right) Halves(string path)
    {
        using var stream = File.OpenRead(path);
        var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var image = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);

        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);

        double Mean(int x0, int x1)
        {
            double sum = 0;
            int count = 0;

            for (int y = 0; y < image.PixelHeight; y++)
                for (int x = x0; x < x1; x++, count++)
                    sum += pixels[(y * image.PixelWidth + x) * 4 + 1];

            return sum / Math.Max(1, count);
        }

        return (Mean(0, image.PixelWidth / 4), Mean(image.PixelWidth * 3 / 4, image.PixelWidth));
    }

    private static void WriteGrey(string path)
    {
        var pixels = Enumerable.Repeat((byte)170, Width * Height * 4).ToArray();
        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var file = File.Create(path);
        encoder.Save(file);
    }
}
