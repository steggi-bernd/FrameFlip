using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Kommt jedes Rasterverfahren auch WIRKLICH im Bild an?
///
/// Die Rechnung jedes einzelnen steht schon in <see cref="DitherInvariants"/>, und
/// sie stimmt. Das ist aber nicht dieselbe Frage: Zwischen dem Werkzeug und dem
/// Bildpunkt auf dem Schirm liegen der Stapel, das Vorbereiten, die Trennung nach
/// Passart und der Prozessor. Ein Werkzeug, das richtig rechnet und nirgends
/// aufgerufen wird, faellt in keinem Rechentest auf - nur im Fenster, als "hat
/// keinen Effekt".
///
/// Deshalb wird hier der ganze Weg genommen: Stapel bauen, vorbereiten, durch den
/// Prozessor schicken, Bytes vergleichen.
/// </summary>
public static class DitherPipelineInvariants
{
    public static void Run() => EveryPatternReachesThePicture();

    private const int Width = 1920;
    private const int Height = 32;

    /// <summary>Ein waagerechter Verlauf - darauf muss jedes Verfahren etwas tun.</summary>
    private static FloatFrame Ramp()
    {
        int count = Width * Height;

        var r = new float[count];
        var g = new float[count];
        var b = new float[count];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float shown = 0.1f + 0.8f * (x / (float)(Width - 1));
                float light = Srgb.Decode(shown);

                int i = y * Width + x;

                r[i] = light;
                g[i] = light;
                b[i] = light;
            }
        }

        return new FloatFrame
        {
            Width = Width, Height = Height, R = r, G = g, B = b, IsSceneReferred = false,
        };
    }

    /// <summary>Zeichnet den Frame mit dieser Korrektur und gibt die Bytes zurueck.</summary>
    private static byte[] Drawn(FloatFrame frame, GradingStack stack)
    {
        int stride = Width * 4;
        var pixels = new byte[stride * Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                      stack.Prepare(), buffer, stride);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static double Apart(byte[] a, byte[] b)
    {
        long sum = 0;

        for (int i = 0; i < a.Length; i += 4) sum += Math.Abs(a[i] - b[i]);

        return (double)sum / (a.Length / 4);
    }

    /// <summary>
    /// Jedes der neun Verfahren muss das Bild veraendern - und zwar sichtbar.
    ///
    /// Geprueft wird gegen das UNGERASTERTE Bild. Ein Verfahren, das nichts tut,
    /// faellt damit auf, ganz gleich, an welcher Stelle des Weges es verlorengeht.
    /// </summary>
    private static void EveryPatternReachesThePicture()
    {
        Check.Group("Jedes Rasterverfahren kommt im Bild an");

        var frame = Ramp();
        var plain = Drawn(frame, new GradingStack());

        foreach (var pattern in new[]
                 {
                     DitherPattern.Ordered, DitherPattern.Noise, DitherPattern.Lines,
                 })
        {
            var stack = new GradingStack();

            stack.Optics.Add(new DitherTool
            {
                Amount = 1f, Levels = 2, Pattern = pattern, Size = 4,
            });

            double apart = Apart(plain, Drawn(frame, stack));

            Console.WriteLine($"         {pattern,-18} {apart:0.0}");

            Check.That(apart > 5.0, $"{pattern} veraendert das Bild", $"{apart:0.0}");
        }

        foreach (var kernel in Enum.GetValues<DiffusionKernel>())
        {
            var stack = new GradingStack();

            stack.Frame.Add(new DiffusionTool { Amount = 1f, Levels = 2, Kernel = kernel });

            double apart = Apart(plain, Drawn(frame, stack));

            Console.WriteLine($"         {kernel,-18} {apart:0.0}");

            Check.That(apart > 5.0, $"{kernel} veraendert das Bild", $"{apart:0.0}");
        }

        BothWaysThroughTheProcessor(frame);
    }

    /// <summary>
    /// Und dasselbe auf dem ZWEITEN Weg durch den Prozessor.
    ///
    /// Es gibt zwei volle Wege: den geraden, und den, den ein oertliches Werkzeug
    /// erzwingt - Glanz, Klarheit, Schaerfe brauchen die Nachbarschaft und damit einen
    /// zweiten Durchgang. Beide schreiben am Ende Bytes, und beide muessen den
    /// Durchgang ueber den Rahmen laufen lassen.
    ///
    /// Genau das war der gemeldete Fehler: Er stand nur im geraden Weg. Wer Glanz
    /// eingeschaltet hatte - und wer rastert, will meistens Glanz -, bekam keine
    /// Fehlerdiffusion mehr, ohne dass irgendetwas davon berichtet haette.
    ///
    /// Geprueft wird gegen das Bild MIT dem oertlichen Werkzeug, aber ohne Raster:
    /// Sonst maesse man den Glanz statt der Diffusion.
    /// </summary>
    private static void BothWaysThroughTheProcessor(FloatFrame frame)
    {
        Check.Group("Auch mit einem oertlichen Werkzeug kommt das Raster an");

        static GradingStack WithLocal()
        {
            var stack = new GradingStack();

            stack.Local.Add(new ClarityTool { Amount = 0.6f, Reach = 8 });

            return stack;
        }

        var soft = Drawn(frame, WithLocal());

        foreach (var kernel in new[] { DiffusionKernel.FloydSteinberg, DiffusionKernel.Atkinson })
        {
            var stack = WithLocal();

            stack.Frame.Add(new DiffusionTool { Amount = 1f, Levels = 2, Kernel = kernel });

            double apart = Apart(soft, Drawn(frame, stack));

            Console.WriteLine($"         mit Klarheit: {kernel,-16} {apart:0.0}");

            Check.That(apart > 5.0,
                       $"{kernel} wirkt auch neben einem oertlichen Werkzeug", $"{apart:0.0}");
        }

        // Und die Gegenprobe: Das geordnete Raster konnte es schon immer - waere es
        // hier still, laege der Fehler woanders.
        var ordered = WithLocal();

        ordered.Optics.Add(new DitherTool { Amount = 1f, Levels = 2, Size = 4 });

        Check.That(Apart(soft, Drawn(frame, ordered)) > 5.0,
                   "und das geordnete Raster ebenso");
    }
}
