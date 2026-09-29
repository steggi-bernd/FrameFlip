namespace FrameFlip.Imaging;

/// <summary>Welches Messgeraet im Feld "Verteilung" steht (W2d).</summary>
public enum ScopeKind
{
    Histogram,
    Waveform,
    Parade,
    Vectorscope,
}

/// <summary>Ein gerechnetes Messbild - Bgra32, fertig zum Zeigen, und was es zeigt.</summary>
public sealed record ScopeImage(ScopeKind Kind, byte[] Pixels, int Width, int Height);

/// <summary>
/// Die Messgeraete des Ateliers (docs/Atelier-Werkzeugplan.md, W2d): Waveform, RGB-Parade und
/// Vektorskop, gerechnet aus dem angezeigten Bild - das, was man sieht, wie am Monitor eines
/// Farbkorrektors.
///
/// Gezaehlt wird jeder n-te Bildpunkt in beiden Richtungen; die Dichte wird logarithmisch
/// hell, damit ein grosser Anteil nicht alles andere im Dunkel verschwinden laesst.
/// </summary>
public static class Scopes
{
    /// <summary>Wie viele Stufen die Waveform hoch ist.</summary>
    public const int Levels = 128;

    /// <summary>Die Groesse des Vektorskops, quadratisch.</summary>
    public const int VectorSize = 128;

    /// <summary>Die Farbdifferenzen eines Anzeigewerts nach Rec. 709, je von -0,5 bis 0,5.</summary>
    public static (float Cb, float Cr) Chroma(byte red, byte green, byte blue)
    {
        float r = red / 255f, g = green / 255f, b = blue / 255f;
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;

        return ((b - y) / 1.8556f, (r - y) / 1.5748f);
    }

    /// <summary>
    /// Die Waveform: je Spalte des Bildes, wie sich die Werte ueber die Hoehe verteilen.
    /// <paramref name="channel"/> -1 ist die Helligkeit, 0 bis 2 Rot, Gruen, Blau. Die Zaehlungen
    /// liegen zeilenweise, die unterste Stufe (Schwarz) in der untersten Zeile.
    /// </summary>
    public static unsafe float[] Waveform(IntPtr pixels, int width, int height, int stride, int columns, int channel, int step)
    {
        var counts = new float[Levels * columns];
        byte* start = (byte*)pixels;
        step = Math.Max(1, step);

        for (int y = 0; y < height; y += step)
        {
            byte* row = start + (long)y * stride;

            for (int x = 0; x < width; x += step)
            {
                byte* p = row + x * 4;
                float value = channel switch
                {
                    0 => p[2],
                    1 => p[1],
                    2 => p[0],
                    _ => 0.2126f * p[2] + 0.7152f * p[1] + 0.0722f * p[0],
                } / 255f;

                int column = Math.Min(columns - 1, x * columns / width);
                int level = Math.Clamp((int)(value * (Levels - 1) + 0.5f), 0, Levels - 1);

                counts[(Levels - 1 - level) * columns + column]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Das Vektorskop: wie sich die Farbe verteilt - die Richtung ist der Farbton, der Abstand
    /// von der Mitte die Saettigung. Rot liegt oben links, wie an einem Vektorskop ueblich.
    /// </summary>
    public static unsafe float[] Vectorscope(IntPtr pixels, int width, int height, int stride, int step)
    {
        var counts = new float[VectorSize * VectorSize];
        byte* start = (byte*)pixels;
        step = Math.Max(1, step);

        for (int y = 0; y < height; y += step)
        {
            byte* row = start + (long)y * stride;

            for (int x = 0; x < width; x += step)
            {
                byte* p = row + x * 4;
                var (cb, cr) = Chroma(p[2], p[1], p[0]);
                var (u, v) = VectorAt(cb, cr);

                counts[v * VectorSize + u]++;
            }
        }

        return counts;
    }

    /// <summary>Wo eine Farbdifferenz im Vektorskop liegt - Spalte und Zeile.</summary>
    public static (int X, int Y) VectorAt(float cb, float cr)
    {
        int x = Math.Clamp((int)((cb + 0.5f) * (VectorSize - 1) + 0.5f), 0, VectorSize - 1);
        int y = Math.Clamp((int)((0.5f - cr) * (VectorSize - 1) + 0.5f), 0, VectorSize - 1);

        return (x, y);
    }

    /// <summary>
    /// Ein Messbild aus dem angezeigten Bild. Jeder n-te Bildpunkt, so dass es um die
    /// hunderttausend werden - genug fuer ein ruhiges Bild, schnell genug fuer jeden Durchgang.
    /// </summary>
    public static ScopeImage Measure(ScopeKind kind, IntPtr pixels, int width, int height, int stride)
    {
        int step = Math.Max(1, (int)MathF.Sqrt(width * (float)height / 100_000f));

        switch (kind)
        {
            case ScopeKind.Vectorscope:
                return new ScopeImage(kind, Paint(Vectorscope(pixels, width, height, stride, step), VectorSize, VectorSize, (150, 230, 160)),
                                      VectorSize, VectorSize);

            case ScopeKind.Parade:
            {
                const int Each = 128;
                var image = new byte[Each * 3 * Levels * 4];

                for (int channel = 0; channel < 3; channel++)
                {
                    var tint = channel switch { 0 => ((byte)235, (byte)80, (byte)80), 1 => ((byte)90, (byte)225, (byte)90), _ => ((byte)95, (byte)140, (byte)255) };
                    var part = Paint(Waveform(pixels, width, height, stride, Each, channel, step), Each, Levels, tint);

                    for (int row = 0; row < Levels; row++)
                        Array.Copy(part, row * Each * 4, image, (row * Each * 3 + channel * Each) * 4, Each * 4);
                }

                return new ScopeImage(kind, image, Each * 3, Levels);
            }

            default:
            {
                const int Columns = 256;
                return new ScopeImage(kind, Paint(Waveform(pixels, width, height, stride, Columns, -1, step), Columns, Levels, (150, 230, 160)),
                                      Columns, Levels);
            }
        }
    }

    /// <summary>Die Dichte als Bild: logarithmisch hell, in einer Farbe, auf Schwarz.</summary>
    public static byte[] Paint(float[] counts, int width, int height, (byte R, byte G, byte B) tint)
    {
        var image = new byte[width * height * 4];
        float peak = 0f;

        foreach (float count in counts) peak = MathF.Max(peak, count);

        float scale = peak > 0f ? 1f / MathF.Log(1f + peak) : 0f;

        for (int i = 0; i < width * height; i++)
        {
            float level = counts[i] <= 0f ? 0f : MathF.Min(1f, 0.25f + 0.75f * MathF.Log(1f + counts[i]) * scale);

            image[i * 4] = (byte)(tint.B * level);
            image[i * 4 + 1] = (byte)(tint.G * level);
            image[i * 4 + 2] = (byte)(tint.R * level);
            image[i * 4 + 3] = 255;
        }

        return image;
    }
}
