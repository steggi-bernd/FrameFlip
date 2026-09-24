using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging;

/// <summary>
/// Der Durchgang, der Bildpunkte verschiebt: Linsenverzeichnung und chromatische
/// Aberration.
///
/// Er laeuft rueckwaerts, und das ist der einzige Weg, der ohne Loecher auskommt:
/// Nicht "wohin geht dieser Punkt", sondern fuer jeden Zielpunkt "woher kommt er".
/// Vorwaerts gerechnet trifft nicht jeder Zielpunkt einen Quellpunkt, und das
/// Ergebnis waere ein Sieb.
///
/// Abgetastet wird bilinear. Der naechste Nachbar waere billiger und zeigte an jeder
/// schraegen Kante eine Treppe, die mit dem Bild nichts zu tun hat - man haelt sie
/// fuer ein Artefakt der Datei.
///
/// Am Rand wird geklemmt statt schwarz gefuellt: Ein schwarzer Saum sieht aus wie ein
/// Fehler, eine gedehnte Kante wie ein Bildrand. Wer die Dehnung nicht will, nimmt
/// den Massstab der Verzeichnung und holt sie aus dem Bild.
/// </summary>
public static class GeometryPass
{
    /// <summary>
    /// Verschiebt den Puffer. Die Quelle bleibt unangetastet, das Ergebnis landet im
    /// Arbeitsfeld - und danach tauschen beide die Rolle.
    /// </summary>
    /// <param name="step">
    /// Die Schrittweite des Gitters. Der Ort wird im BILD gerechnet und erst zum
    /// Abtasten auf das Gitter umgerechnet: Sonst saesse die Mitte der Verzeichnung
    /// beim Reglerzug woanders als im fertigen Bild.
    /// </param>
    public static void Run(LocalPass.Scratch scratch, IGeometryTool[] tools, OpticsPlace place,
                           int gridWidth, int gridHeight, int step)
    {
        if (tools.Length == 0) return;

        var source = scratch.Values;
        var target = scratch.Work;

        var alpha = scratch.Alpha;
        var alphaTarget = scratch.AlphaWork;

        float unit = place.Unit;
        float spread = place.Spread;
        float centreX = place.CentreX;
        float centreY = place.CentreY;

        Parallel.For(0, gridHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            // Die Stelle im Bild, nicht im Gitter.
            float ny = (gy * step + 0.5f - centreY) * unit;
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                float nx = (gx * step + 0.5f - centreX) * unit;

                float radius = MathF.Sqrt(nx * nx + ny * ny) * spread;

                float red = 1f, green = 1f, blue = 1f;
                for (int t = 0; t < tools.Length; t++) tools[t].Factors(radius, ref red, ref green, ref blue);

                int at = (row + gx) * 3;

                target[at] = Sample(source, 0, nx, ny, red, in place, gridWidth, gridHeight, step);
                target[at + 1] = Sample(source, 1, nx, ny, green, in place, gridWidth, gridHeight, step);
                target[at + 2] = Sample(source, 2, nx, ny, blue, in place, gridWidth, gridHeight, step);

                // Die Deckung folgt dem gruenen Kanal. Sie liegt sonst neben dem
                // Bild, das sie freistellen soll - am sichtbarsten bei einem
                // Wasserzeichen mit weichem Rand.
                alphaTarget[row + gx] = SampleAlpha(alpha, nx, ny, green, in place,
                                                    gridWidth, gridHeight, step);
            }
        });

        // Tauschen statt zurueckkopieren: Bei 4K waeren das dreihundert Megabyte,
        // einmal hin und einmal her, fuer nichts.
        scratch.Values = target;
        scratch.Work = source;

        scratch.Alpha = alphaTarget;
        scratch.AlphaWork = alpha;
    }

    /// <summary>
    /// Ein Kanal an der verschobenen Stelle - bilinear und am Rand geklemmt.
    /// </summary>
    private static float Sample(float[] source, int channel, float nx, float ny, float factor,
                                in OpticsPlace place, int gridWidth, int gridHeight, int step)
    {
        // Zurueck in Bildpunkte, dann auf das Gitter.
        float x = (place.CentreX + nx * factor / place.Unit - 0.5f) / step;
        float y = (place.CentreY + ny * factor / place.Unit - 0.5f) / step;

        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float tx = x - x0;
        float ty = y - y0;

        int left = Math.Clamp(x0, 0, gridWidth - 1);
        int right = Math.Clamp(x0 + 1, 0, gridWidth - 1);
        int top = Math.Clamp(y0, 0, gridHeight - 1);
        int bottom = Math.Clamp(y0 + 1, 0, gridHeight - 1);

        float upper = Mix(source[(top * gridWidth + left) * 3 + channel],
                          source[(top * gridWidth + right) * 3 + channel], tx);

        float lower = Mix(source[(bottom * gridWidth + left) * 3 + channel],
                          source[(bottom * gridWidth + right) * 3 + channel], tx);

        return Mix(upper, lower, ty);
    }

    private static byte SampleAlpha(byte[] source, float nx, float ny, float factor,
                                    in OpticsPlace place, int gridWidth, int gridHeight, int step)
    {
        float x = (place.CentreX + nx * factor / place.Unit - 0.5f) / step;
        float y = (place.CentreY + ny * factor / place.Unit - 0.5f) / step;

        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float tx = x - x0;
        float ty = y - y0;

        int left = Math.Clamp(x0, 0, gridWidth - 1);
        int right = Math.Clamp(x0 + 1, 0, gridWidth - 1);
        int top = Math.Clamp(y0, 0, gridHeight - 1);
        int bottom = Math.Clamp(y0 + 1, 0, gridHeight - 1);

        float upper = Mix(source[top * gridWidth + left], source[top * gridWidth + right], tx);
        float lower = Mix(source[bottom * gridWidth + left], source[bottom * gridWidth + right], tx);

        return (byte)Math.Clamp(MathF.Round(Mix(upper, lower, ty)), 0f, 255f);
    }

    private static float Mix(float from, float to, float at) => from + (to - from) * at;
}
