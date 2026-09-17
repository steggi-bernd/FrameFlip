using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Rastern - und die eine Eigenschaft, an der sich entscheidet, ob es eines ist.
///
/// Ein Bild auf acht Stufen zu runden kann jeder. Der Unterschied zwischen Rastern
/// und Verstuemmeln ist, dass die Flaeche IM MITTEL ihre Helligkeit behaelt: Wo ein
/// Verlauf zwischen zwei Stufen liegt, bekommt ein Teil der Punkte die untere und ein
/// Teil die obere, und zwar im richtigen Verhaeltnis. Das Auge mittelt sie wieder
/// zusammen, und deshalb sieht man den Verlauf weiter.
///
/// Genau das steht hier als Zahl. Alles andere - Stufen, Muster, Staerke - ist
/// Beiwerk, das ohne diese Eigenschaft nichts wert waere.
/// </summary>
public static class DitherInvariants
{
    public static void Run()
    {
        TheMatrixIsAProperOne();
        OffChangesNothing();
        ItReallyQuantises();
        TheAverageSurvives();
        OverbrightsAreLeftAlone();
        BothPatternsHoldUp();
        DiffusionKeepsTheAverage();
        DiffusionLeavesCoverAlone();
    }

    private static OpticsPlace Place => new(1920, 1080, 1);

    // ------------------------------------------------------- die Fehlerdiffusion

    /// <summary>
    /// Baut eine gleichmaessige Flaeche, laesst die Diffusion darueber laufen und
    /// gibt zurueck, was daraus geworden ist.
    /// </summary>
    private static byte[] Diffused(int width, int height, byte value, int levels,
                                   float amount = 1f)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 200;
        }

        var tool = new DiffusionTool { Levels = levels, Amount = amount };

        tool.Prepare();

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            Marshal.Copy(pixels, 0, buffer, pixels.Length);

            tool.Apply(buffer, width, height, stride);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    /// <summary>
    /// Dieselbe Probe wie beim geordneten Rastern, und sie ist hier noch schaerfer:
    /// Fehlerdiffusion verliert NICHTS, weil jeder Rest weitergereicht wird.
    ///
    /// Auf einer gleichmaessigen Flaeche muss der Mittelwert deshalb fast genau
    /// stehenbleiben - nicht nur ungefaehr wie bei einer Matrix, die sich alle acht
    /// Punkte wiederholt.
    /// </summary>
    private static void DiffusionKeepsTheAverage()
    {
        Check.Group("Fehlerdiffusion behaelt die Helligkeit");

        foreach (byte value in new byte[] { 40, 128, 200 })
        {
            foreach (int levels in new[] { 2, 4 })
            {
                var pixels = Diffused(64, 64, value, levels);

                double sum = 0;

                for (int i = 0; i < pixels.Length; i += 4) sum += pixels[i];

                double mean = sum / (pixels.Length / 4);

                Check.That(Math.Abs(mean - value) < 2.0,
                           $"{value} bei {levels} Stufen bleibt im Mittel stehen",
                           $"{mean:0.00}");

                // Und es muss wirklich gerastert haben - sonst waere der Mittelwert
                // trivialerweise richtig, weil nichts geschehen ist.
                int between = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] > 1 && pixels[i] < 254 && levels == 2) between++;

                if (levels == 2)
                {
                    Check.That(between == 0, $"{value}: bei zwei Stufen bleibt nichts dazwischen",
                               $"{between} Punkte");
                }
            }
        }

        // Aus heisst aus.
        var quiet = Diffused(32, 32, 100, 2, amount: 0f);

        Check.That(quiet[0] == 100 && quiet[4] == 100, "bei Staerke null bleibt alles stehen",
                   $"{quiet[0]}/{quiet[4]}");
    }

    /// <summary>
    /// Die Deckung ist keine Farbe.
    ///
    /// Sie mitzurastern hiesse, eine halbdurchsichtige Stelle auf ganz oder gar nicht
    /// zu werfen - und das sieht man nicht im Bild, sondern erst dort, wo es
    /// weiterverwendet wird.
    /// </summary>
    private static void DiffusionLeavesCoverAlone()
    {
        Check.Group("Die Deckung bleibt unberuehrt");

        var pixels = Diffused(16, 16, 128, 2);

        int changed = 0;

        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 200) changed++;

        Check.That(changed == 0, "kein einziger Deckungswert wurde angefasst",
                   $"{changed} von {pixels.Length / 4}");
    }

    private static DitherTool Made(int levels, float amount = 1f,
                                   DitherPattern pattern = DitherPattern.Ordered)
    {
        var tool = new DitherTool { Levels = levels, Amount = amount, Pattern = pattern, Size = 1 };

        tool.Prepare();

        return tool;
    }

    /// <summary>
    /// Die Bayer-Matrix muss jede Zahl von 0 bis 63 GENAU EINMAL enthalten.
    ///
    /// Das ist keine Formalie: Eine Matrix mit Luecken oder Doppelten haette Stellen,
    /// die nie umspringen, und andere, die es doppelt so oft tun. Im Bild waere das
    /// ein Muster im Muster - und es faellt erst an einem sehr weichen Verlauf auf,
    /// also spaet.
    /// </summary>
    private static void TheMatrixIsAProperOne()
    {
        Check.Group("Die Rastermatrix ist vollstaendig");

        var seen = new int[64];
        var tool = Made(2);

        // Ueber die Schwellen: Jede Stelle liefert (Wert + 0,5) / 64.
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                float threshold = Threshold(tool, x, y);
                int value = (int)MathF.Round(threshold * 64f - 0.5f);

                if (value is >= 0 and < 64) seen[value]++;
            }
        }

        int missing = seen.Count(n => n != 1);

        Check.That(missing == 0, "jede der vierundsechzig Stufen kommt genau einmal vor",
                   $"{missing} Stellen stimmen nicht");
    }

    /// <summary>Die Schwelle an einer Stelle - ueber das Ergebnis zurueckgerechnet.</summary>
    private static float Threshold(DitherTool tool, int x, int y)
    {
        // Bei zwei Stufen springt ein Wert genau dann auf Weiss, wenn Wert + Schwelle
        // die Eins erreicht. Wir suchen den Wert, bei dem das kippt.
        float low = 0f, high = 1f;

        for (int step = 0; step < 24; step++)
        {
            float mid = (low + high) / 2f;
            float r = Srgb.Decode(mid), g = r, b = r;

            tool.Apply(Place, x, y, ref r, ref g, ref b);

            if (Srgb.Encode(r) > 0.5f) high = mid;
            else low = mid;
        }

        return 1f - (low + high) / 2f;
    }

    private static void OffChangesNothing()
    {
        Check.Group("Aus geraestert nichts");

        var tool = Made(4, amount: 0f);

        float r = 0.37f, g = 0.21f, b = 0.64f;
        float wasR = r;

        tool.Apply(Place, 3, 5, ref r, ref g, ref b);

        Check.Near(r, wasR, 0.0001, "der Wert bleibt, wie er war");
    }

    private static void ItReallyQuantises()
    {
        Check.Group("Es bleiben wirklich nur die Stufen uebrig");

        var tool = Made(2);

        int other = 0;

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float v = Srgb.Decode((x + y) / 30f);
                float r = v, g = v, b = v;

                tool.Apply(Place, x, y, ref r, ref g, ref b);

                float shown = Srgb.Encode(r);

                if (shown > 0.001f && shown < 0.999f) other++;
            }
        }

        Check.That(other == 0, "bei zwei Stufen gibt es nur Schwarz und Weiss",
                   $"{other} Punkte dazwischen");
    }

    /// <summary>
    /// Die Probe, um die es geht: Die Flaeche behaelt ihre Helligkeit.
    ///
    /// Gemessen ueber eine ganze Rasterzelle, weil genau dort die Aufteilung
    /// stattfindet. Ein einzelner Punkt ist nach dem Rastern weit weg von seinem Wert
    /// - das ist kein Fehler, sondern der Zweck.
    /// </summary>
    private static void TheAverageSurvives()
    {
        Check.Group("Die Flaeche behaelt ihre Helligkeit");

        foreach (int levels in new[] { 2, 3, 6 })
        {
            var tool = Made(levels);

            foreach (float shown in new[] { 0.2f, 0.5f, 0.75f })
            {
                double sum = 0;

                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        float r = Srgb.Decode(shown), g = r, b = r;

                        tool.Apply(Place, x, y, ref r, ref g, ref b);

                        sum += Srgb.Encode(r);
                    }
                }

                double mean = sum / 64;

                Check.That(Math.Abs(mean - shown) < 0.02,
                           $"bei {levels} Stufen bleibt {shown:0.00} im Mittel stehen",
                           $"{mean:0.000}");
            }
        }
    }

    private static void OverbrightsAreLeftAlone()
    {
        Check.Group("Ueber Weiss wird nicht gerastert");

        var tool = Made(2);

        float r = 40f, g = 40f, b = 40f;

        tool.Apply(Place, 1, 1, ref r, ref g, ref b);

        Check.Near(r, 40.0, 0.001, "ein Glanzpass mit Wert vierzig bleibt vierzig");
    }

    /// <summary>
    /// Beide Muster muessen dasselbe LEISTEN, auch wenn sie anders aussehen.
    ///
    /// Und beide muessen FEST sein: Dieselbe Stelle, dieselbe Schwelle. Ein Muster,
    /// das bei jedem Durchgang wuerfelt, flimmerte schon beim Ziehen an einem Regler.
    /// </summary>
    private static void BothPatternsHoldUp()
    {
        Check.Group("Geordnet und zufaellig - beide fest, beide brauchbar");

        foreach (var pattern in new[] { DitherPattern.Ordered, DitherPattern.Noise })
        {
            var tool = Made(2, pattern: pattern);

            float first = 0f, second = 0f;

            for (int pass = 0; pass < 2; pass++)
            {
                double sum = 0;

                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        float r = Srgb.Decode(0.5f), g = r, b = r;

                        tool.Apply(Place, x, y, ref r, ref g, ref b);

                        sum += Srgb.Encode(r);
                    }
                }

                if (pass == 0) first = (float)(sum / 1024);
                else second = (float)(sum / 1024);
            }

            Check.Near(first, second, 0.0001, $"{pattern}: derselbe Durchgang, dasselbe Ergebnis");

            Check.That(Math.Abs(first - 0.5f) < 0.06,
                       $"{pattern}: ein halbes Grau bleibt im Mittel halb", $"{first:0.000}");
        }
    }
}
