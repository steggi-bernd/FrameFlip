using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Sechzehn-Bit-Ausgabe rechnet dasselbe Bild wie die Vorschau.
///
/// Es gibt zwei Ausgaenge aus dem Prozessor - acht Bit fuer die Anzeige und den
/// PNG-Export, sechzehn fuer die genaue Ausgabe -, und jeder hat seine eigene
/// Verzweigung, wann der Puffer mit dem zweiten Durchgang noetig ist. Genau dort lief
/// es auseinander: Der Sechzehn-Bit-Weg teilte nur bei Glanz und Halation, und ohne
/// die beiden liefen Verzeichnung, Farbsaum, Tiefenschaerfe, Bewegungsunschaerfe und
/// Verschiebung dort ueberhaupt nicht. Die Vorschau zeigte sie, die Datei hatte sie
/// nicht - ohne Meldung.
///
/// Geprueft wird deshalb jede Art Werkzeug einzeln und nicht eine Auswahl: Der Fehler
/// sass in der Frage, WELCHE Arten den Puffer brauchen, und eine Liste von Beispielen
/// haette genau die fehlende Art auslassen koennen. Die Durchgaenge ueber das fertige
/// Bild bleiben aussen vor - sie laufen im Sechzehn-Bit-Weg mit Absicht nicht, siehe
/// dort.
/// </summary>
public static class ExportParityInvariants
{
    private const int Width = 96;
    private const int Height = 64;

    public static void Run()
    {
        Check.Group("Die Sechzehn-Bit-Ausgabe rechnet, was die Vorschau zeigt");

        var frame = Scene();
        var depth = Depth();
        var motion = Motion();

        foreach (var (name, stack, data) in Cases(depth, motion))
        {
            var eight = Draw8(frame, stack, data);
            var sixteen = Draw16(frame, stack, data);

            int worst = Worst(eight, sixteen);

            Check.That(worst <= 1, $"{name}: hoechstens eine Stufe Abstand", $"{worst} Stufen");
        }
    }

    /// <summary>Je Art ein Werkzeug - und eine Mischung aus den Arten, die sich den Puffer teilen.</summary>
    private static IEnumerable<(string Name, GradingStack Stack, FloatFrame?[]? Data)> Cases(
        FloatFrame depth, FloatFrame motion)
    {
        yield return ("Weissabgleich (punktweise)",
                      new GradingStack { Tools = { new WhiteBalanceTool { Kelvin = 4200 } } }, null);

        yield return ("Vignette (Linse)",
                      new GradingStack { Optics = { new VignetteTool { Amount = -0.6f } } }, null);

        yield return ("Korn (Film)",
                      new GradingStack { Optics = { new GrainTool { Amount = 0.4f } } }, null);

        yield return ("Klarheit (oertlich)",
                      new GradingStack { Local = { new ClarityTool { Amount = 0.6f, Reach = 6 } } }, null);

        yield return ("Glanz (Licht)",
                      new GradingStack { Local = { new BloomTool { Amount = 0.8f, Threshold = 0.9f, Reach = 8 } } },
                      null);

        yield return ("Verzeichnung (Geometrie)",
                      new GradingStack { Geometry = { new DistortionTool { Amount = 0.5f } } }, null);

        yield return ("Farbsaum (Geometrie)",
                      new GradingStack { Geometry = { new ChromaticTool { Amount = 0.8f } } }, null);

        yield return ("Tiefenschaerfe (Renderdaten)",
                      new GradingStack { Data = { new DepthFieldTool { Aperture = 0.8f, Focus = 2f } } },
                      new FloatFrame?[] { depth });

        yield return ("Bewegungsunschaerfe (Renderdaten)",
                      new GradingStack { Data = { new MotionBlurTool { Shutter = 1f } } },
                      new FloatFrame?[] { motion });

        yield return ("Verschiebung (Renderdaten, ohne Pass)",
                      new GradingStack { Data = { new DisplaceTool { Amount = 6f, From = DisplaceFrom.Screen, Wavelength = 20f } } },
                      new FloatFrame?[] { null });

        yield return ("Verzeichnung, Klarheit und Korn zusammen",
                      new GradingStack
                      {
                          Geometry = { new DistortionTool { Amount = 0.4f } },
                          Local = { new ClarityTool { Amount = 0.5f, Reach = 6 } },
                          Optics = { new GrainTool { Amount = 0.3f } },
                      },
                      null);
    }

    /// <summary>
    /// Ein Bild mit etwas zum Verschieben und Weichzeichnen: Verlaeufe, harte Kanten und
    /// ein paar Lichter ueber Weiss fuer den Glanz.
    /// </summary>
    private static FloatFrame Scene()
    {
        int count = Width * Height;
        var r = new float[count];
        var g = new float[count];
        var b = new float[count];
        var a = new float[count];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;

                // Eine Deckung mit Kante, damit ein Werkzeug, das Punkte verschiebt,
                // sie sichtbar mitnehmen muss.
                a[i] = x < Width / 3 ? 0.25f : 1f;

                r[i] = 0.05f + 0.6f * x / Width;
                g[i] = 0.05f + 0.5f * y / Height;
                b[i] = (x / 8 + y / 8) % 2 == 0 ? 0.08f : 0.45f;

                if (Math.Abs(x - 60) < 4 && Math.Abs(y - 20) < 4) r[i] = g[i] = b[i] = 6f;
            }
        }

        return new FloatFrame { Width = Width, Height = Height, R = r, G = g, B = b, A = a };
    }

    /// <summary>Tiefe von einem bis zehn Meter, von links nach rechts.</summary>
    private static FloatFrame Depth()
    {
        int count = Width * Height;
        var z = new float[count];

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                z[y * Width + x] = 1f + 9f * x / (Width - 1);

        return new FloatFrame { Width = Width, Height = Height, R = z, G = z, B = z };
    }

    /// <summary>Eine gleichmaessige Bewegung nach rechts unten.</summary>
    private static FloatFrame Motion()
    {
        int count = Width * Height;
        var dx = Enumerable.Repeat(4f, count).ToArray();
        var dy = Enumerable.Repeat(2f, count).ToArray();
        var zero = new float[count];

        return new FloatFrame { Width = Width, Height = Height, R = dx, G = dy, B = zero, A = zero };
    }

    private static byte[] Draw8(FloatFrame frame, GradingStack stack, FloatFrame?[]? data)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                      stack.Prepare(), buffer, stride, data: data);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static ushort[] Draw16(FloatFrame frame, GradingStack stack, FloatFrame?[]? data)
    {
        int stride = frame.Width * 8;
        var pixels = new byte[stride * frame.Height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.ApplyRgba64(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                            stack.Prepare(), buffer, stride, data: data);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var values = new ushort[pixels.Length / 2];
        Buffer.BlockCopy(pixels, 0, values, 0, pixels.Length);

        return values;
    }

    /// <summary>
    /// Der groesste Abstand in Achtbitstufen. Die Vorschau liegt als B, G, R, A vor,
    /// die Ausgabe als R, G, B, A - verglichen wird alles, auch die Deckung: Ein
    /// Werkzeug, das Punkte verschiebt, muss sie in beiden Wegen mitnehmen.
    /// </summary>
    private static int Worst(byte[] eight, ushort[] sixteen)
    {
        int worst = 0;

        for (int p = 0; p < Width * Height; p++)
        {
            for (int c = 0; c < 4; c++)
            {
                int preview = eight[p * 4 + (c == 3 ? 3 : 2 - c)];
                int export = (int)Math.Round(sixteen[p * 4 + c] / 257.0);

                worst = Math.Max(worst, Math.Abs(preview - export));
            }
        }

        return worst;
    }
}
