namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Kanten verfeinern (docs/Atelier-Werkzeugplan.md, W1): Eine grob gemalte Maske legt sich
/// an die Kanten des Bildes. Wo sie ueber eine Kante hinausragt, zieht sie sich zurueck; wo
/// sie davor aufhoert, waechst sie bis an die Kante. Wo das Bild keine Kante hat, bleibt sie.
///
/// Ein lokales Matting mit zwei Klassen, auf dem Raster der Maske (ein Punkt je 4 mal 4
/// Bildpunkte), gefuehrt von der Helligkeit des Bildes:
///
/// 1. Um den Rand der Maske liegt ein unsicherer Streifen, so breit wie die Weite. Was
///    innerhalb dieses Abstands nur Maske sieht, ist sicher innen; was keine sieht, sicher
///    aussen.
/// 2. Fuer jeden Punkt im Streifen: die mittlere Helligkeit des sicher Inneren und des
///    sicher Aeusseren in seiner Naehe (doppelte Weite). Liegt er naeher am Inneren, gehoert
///    er dazu, dazwischen anteilig - eine weiche Kante im Bild ergibt eine weiche Maske.
/// 3. Unterscheiden sich Innen und Aussen dort kaum, sagt das Bild nichts - der Punkt
///    behaelt seinen Wert.
///
/// Erst versucht war der gefuehrte Filter nach He, Sun und Tang. Er macht Kanten weich,
/// rastet aber nicht ein: Ragt eine Maske in eine gleichmaessig helle Flaeche, kann er
/// dort nichts unterscheiden und laesst Zwischenwerte stehen.
///
/// Das Ergebnis haengt am Bild. Der Strich, der es in den Maskenverlauf traegt, nimmt es
/// deshalb gepackt mit (<see cref="PaintStroke.Result"/>) - so spielt er auch ohne die
/// Datei genau nach.
/// </summary>
public static class MaskRefine
{
    /// <summary>
    /// Wie verschieden Innen und Aussen mindestens sein muessen, damit das Bild etwas sagt -
    /// auf der gestauchten Helligkeit von 0 bis 1.
    /// </summary>
    public const float MinContrast = 0.02f;

    /// <summary>
    /// Die Fuehrung: je Maskenpunkt die mittlere Helligkeit seines Feldes, gestaucht auf 0
    /// bis 1 (l / (1 + l), dann die Wurzel). Ein Render ist szenenbezogen - ein Glanzlicht
    /// bei 40 wuerde alle Kanten im Schatten zu nichts machen.
    /// </summary>
    public static float[] Guide(FloatFrame frame, int cols, int rows, int coarse)
    {
        var guide = new float[cols * rows];

        Parallel.For(0, rows, my =>
        {
            int y0 = my * coarse, y1 = Math.Min(frame.Height, y0 + coarse);

            for (int mx = 0; mx < cols; mx++)
            {
                int x0 = mx * coarse, x1 = Math.Min(frame.Width, x0 + coarse);

                double sum = 0;
                int count = 0;

                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * frame.Width + x;
                        float light = 0.2126f * frame.R[i] + 0.7152f * frame.G[i] + 0.0722f * frame.B[i];
                        if (!float.IsFinite(light) || light < 0f) light = 0f;

                        sum += MathF.Sqrt(light / (1f + light));
                        count++;
                    }
                }

                guide[my * cols + mx] = count == 0 ? 0f : (float)(sum / count);
            }
        });

        return guide;
    }

    /// <summary>
    /// Legt eine Deckung an die Kanten der Fuehrung. <paramref name="radius"/> in
    /// Maskenpunkten: wie breit der unsichere Streifen um den Rand ist.
    /// </summary>
    public static byte[] Refine(byte[] cover, float[] guide, int cols, int rows, int radius)
    {
        int n = cols * rows;
        radius = Math.Max(1, radius);

        var inside = new float[n];
        for (int i = 0; i < n; i++) inside[i] = cover[i] >= 128 ? 1f : 0f;

        // Wie viel Maske ein Punkt im Abstand der Weite sieht: alles - sicher innen, nichts - sicher aussen.
        var seen = Mean(inside, cols, rows, radius);

        var sureIn = new float[n];
        var sureOut = new float[n];
        var lightIn = new float[n];
        var lightOut = new float[n];

        for (int i = 0; i < n; i++)
        {
            sureIn[i] = seen[i] >= 1f - 1e-6f ? 1f : 0f;
            sureOut[i] = seen[i] <= 1e-6f ? 1f : 0f;
            lightIn[i] = guide[i] * sureIn[i];
            lightOut[i] = guide[i] * sureOut[i];
        }

        int reach = radius * 2;
        var countIn = Mean(sureIn, cols, rows, reach);
        var countOut = Mean(sureOut, cols, rows, reach);
        var sumIn = Mean(lightIn, cols, rows, reach);
        var sumOut = Mean(lightOut, cols, rows, reach);

        var refined = (byte[])cover.Clone();

        for (int i = 0; i < n; i++)
        {
            // Nur der Streifen - was sicher ist, bleibt, auch mit seiner weichen Innenseite.
            if (sureIn[i] > 0f || sureOut[i] > 0f) continue;
            if (countIn[i] <= 0f || countOut[i] <= 0f) continue;

            float meanIn = sumIn[i] / countIn[i];
            float meanOut = sumOut[i] / countOut[i];
            float contrast = meanIn - meanOut;

            if (MathF.Abs(contrast) < MinContrast) continue;

            float share = Math.Clamp((guide[i] - meanOut) / contrast, 0f, 1f);
            refined[i] = (byte)MathF.Round(share * 255f);
        }

        return refined;
    }

    /// <summary>
    /// Der Mittelwert im Fenster von (2r+1) mal (2r+1) Punkten - am Rand ueber die Punkte, die
    /// es gibt. Erst Zeilensummen, dann Spaltensummen, laufend und in double: Das Ergebnis
    /// haengt nicht davon ab, in welcher Reihenfolge die Zeilen gerechnet werden.
    /// </summary>
    private static float[] Mean(float[] source, int cols, int rows, int r)
    {
        var across = new double[source.Length];

        Parallel.For(0, rows, y =>
        {
            int row = y * cols;
            double sum = 0;

            for (int x = 0; x <= Math.Min(cols - 1, r); x++) sum += source[row + x];

            for (int x = 0; x < cols; x++)
            {
                across[row + x] = sum;

                if (x + r + 1 < cols) sum += source[row + x + r + 1];
                if (x - r >= 0) sum -= source[row + x - r];
            }
        });

        var mean = new float[source.Length];

        Parallel.For(0, cols, x =>
        {
            int wide = Math.Min(cols - 1, x + r) - Math.Max(0, x - r) + 1;
            double sum = 0;

            for (int y = 0; y <= Math.Min(rows - 1, r); y++) sum += across[y * cols + x];

            for (int y = 0; y < rows; y++)
            {
                int high = Math.Min(rows - 1, y + r) - Math.Max(0, y - r) + 1;
                mean[y * cols + x] = (float)(sum / (wide * high));

                if (y + r + 1 < rows) sum += across[(y + r + 1) * cols + x];
                if (y - r >= 0) sum -= across[(y - r) * cols + x];
            }
        });

        return mean;
    }
}
