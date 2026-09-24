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
        TheLineScreenCarriesBrightness();
        DiffusionKeepsTheAverage();
        EveryKernelIsItself();
        DiffusionLeavesCoverAlone();
        ACellIsSolid();
        TwoColoursKeepTheForm();
    }

    private static OpticsPlace Place => new(1920, 1080, 1);

    /// <summary>
    /// Das Linienraster: die Helligkeit steckt in der DICKE der Linie.
    ///
    /// Das ist seine ganze Aussage, und sie laesst sich genau nachrechnen: Ueber eine
    /// Periode quer zur Linie muss der Anteil der gesetzten Punkte der Helligkeit
    /// entsprechen. Ein halbes Grau faerbt eine halbe Periode.
    ///
    /// Genau treffen kann es das nicht - bei acht Bildpunkten Abstand gibt es acht
    /// moegliche Dicken, also Stufen von einem Achtel. Das ist keine Ungenauigkeit,
    /// sondern die Aufloesung des Verfahrens, und die Toleranz sagt genau das.
    ///
    /// Die zweite Probe ist die schaerfere: ENTLANG der Linie darf sich nichts
    /// aendern. Tut es das doch, ist es kein Linienraster, sondern ein Muster mit
    /// Linienanmutung.
    /// </summary>
    private static void TheLineScreenCarriesBrightness()
    {
        Check.Group("Das Linienraster traegt die Helligkeit in der Dicke");

        const int period = 8;

        var tool = new DitherTool
        {
            Levels = 2, Amount = 1f, Pattern = DitherPattern.Lines, Size = period, Angle = 0f,
        };

        tool.Prepare();

        double last = -1;

        foreach (float shown in new[] { 0.15f, 0.3f, 0.5f, 0.7f, 0.9f })
        {
            int on = 0, all = 0;

            for (int y = 0; y < period * 8; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    float r = Srgb.Decode(shown), g = r, b = r;

                    tool.Apply(Place, x, y, ref r, ref g, ref b);

                    if (Srgb.Encode(r) > 0.5f) on++;

                    all++;
                }
            }

            double covered = (double)on / all;

            Check.That(Math.Abs(covered - shown) <= 1.0 / period + 0.02,
                       $"bei {shown:0.00} deckt die Linie {covered:0.00} der Flaeche",
                       $"{covered:0.000}");

            Check.That(covered >= last, "und sie wird mit der Helligkeit nur dicker",
                       $"{covered:0.000} nach {last:0.000}");

            last = covered;
        }

        // Entlang der Linie - bei 0 Grad also waagerecht - darf sich nichts aendern.
        int varying = 0;

        for (int y = 0; y < period * 2; y++)
        {
            float first = 0f;

            for (int x = 0; x < 40; x++)
            {
                float r = Srgb.Decode(0.4f), g = r, b = r;

                tool.Apply(Place, x, y, ref r, ref g, ref b);

                if (x == 0) first = r;
                else if (MathF.Abs(r - first) > 0.001f) varying++;
            }
        }

        Check.That(varying == 0, "und entlang der Linie bleibt sie gleich",
                   $"{varying} Abweichungen");

        // Und gedreht laeuft sie quer: Bei 90 Grad aendert sich nichts mehr in y,
        // dafuer in x.
        var upright = new DitherTool
        {
            Levels = 2, Amount = 1f, Pattern = DitherPattern.Lines, Size = period, Angle = 90f,
        };

        upright.Prepare();

        int down = 0;
        float top = 0f;

        for (int y = 0; y < 40; y++)
        {
            float r = Srgb.Decode(0.4f), g = r, b = r;

            upright.Apply(Place, 3, y, ref r, ref g, ref b);

            if (y == 0) top = r;
            else if (MathF.Abs(r - top) > 0.001f) down++;
        }

        Check.That(down == 0, "bei neunzig Grad stehen die Linien senkrecht",
                   $"{down} Abweichungen");
    }

    // ------------------------------------------------------- die Fehlerdiffusion

    /// <summary>
    /// Baut eine gleichmaessige Flaeche, laesst die Diffusion darueber laufen und
    /// gibt zurueck, was daraus geworden ist.
    /// </summary>
    private static byte[] Diffused(int width, int height, byte value, int levels,
                                   float amount = 1f,
                                   DiffusionKernel kernel = DiffusionKernel.FloydSteinberg,
                                   int cell = 1, int tall = 0, bool place = false,
                                   DitherPattern pattern = DitherPattern.Ordered)
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

        var tool = new DiffusionTool
        {
            Levels = levels, Amount = amount, Kernel = kernel, Pixels = cell,
            PixelsTall = tall, Place = place, Pattern = pattern,
        };

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
    /// Jedes Schema muss etwas anderes ergeben - sonst waere die Auswahl eine Luege.
    ///
    /// Und eines davon muss etwas anderes ergeben als alle uebrigen: Atkinson reicht
    /// nur sechs Achtel des Fehlers weiter. Das fehlende Viertel ist kein Rundungsrest,
    /// sondern der Grund, warum es so aussieht - helle Flaechen laufen ins Weiss und
    /// dunkle ins Schwarz. Der Mittelwert bleibt dort also ABSICHTLICH nicht stehen,
    /// und ein Test, der ihn auch dort einfordert, haette das Werkzeug kaputtgemacht,
    /// statt es zu pruefen.
    /// </summary>
    private static void EveryKernelIsItself()
    {
        Check.Group("Jedes Streuschema ist es selbst");

        var faithful = new[]
        {
            DiffusionKernel.FloydSteinberg,
            DiffusionKernel.JarvisJudiceNinke,
            DiffusionKernel.Stucki,
            DiffusionKernel.Burkes,
            DiffusionKernel.Sierra,
        };

        double honest = 0;

        foreach (var kernel in faithful)
        {
            var pixels = Diffused(64, 64, 90, 2, kernel: kernel);

            double sum = 0;

            for (int i = 0; i < pixels.Length; i += 4) sum += pixels[i];

            double mean = sum / (pixels.Length / 4);

            Check.That(Math.Abs(mean - 90) < 3.0,
                       $"{kernel} behaelt die Helligkeit", $"{mean:0.0}");

            if (kernel == DiffusionKernel.FloydSteinberg) honest = mean;
        }

        // Atkinson dagegen verliert ein Viertel - und muss es auch.
        var sparse = Diffused(64, 64, 90, 2, kernel: DiffusionKernel.Atkinson);

        double dark = 0;

        for (int i = 0; i < sparse.Length; i += 4) dark += sparse[i];

        dark /= sparse.Length / 4;

        // Verglichen wird mit den TREUEN Schemata und nicht mit einer geratenen
        // Zahl: Die Aussage ist "Atkinson verliert etwas, die anderen nicht", und
        // genau die laesst sich so pruefen. Wieviel es auf einer gleichmaessigen
        // Flaeche ausmacht, haengt davon ab, wie sich der Fehler dort aufschaukelt -
        // das vorherzusagen waere geraten.
        Check.That(dark < honest - 3.0,
                   "Atkinson laesst die Flaeche dunkler werden - das fehlende Viertel",
                   $"{dark:0.0} gegen {honest:0.0}");

        Check.That(dark > 20, "aber nicht ins Nichts", $"{dark:0.0}");

        // Und alle muessen wirklich rastern.
        foreach (var kernel in Enum.GetValues<DiffusionKernel>())
        {
            var pixels = Diffused(32, 32, 120, 2, kernel: kernel);

            int between = 0;

            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i] > 1 && pixels[i] < 254) between++;

            Check.That(between == 0, $"{kernel} laesst bei zwei Stufen nichts dazwischen",
                       $"{between}");
        }

        // Zwei Schemata duerfen nicht dasselbe Bild ergeben.
        var one = Diffused(48, 48, 110, 2, kernel: DiffusionKernel.FloydSteinberg);
        var two = Diffused(48, 48, 110, 2, kernel: DiffusionKernel.Stucki);

        int differing = 0;

        for (int i = 0; i < one.Length; i += 4)
            if (one[i] != two[i]) differing++;

        Check.That(differing > 50,
                   "Floyd-Steinberg und Stucki ergeben verschiedene Bilder",
                   $"{differing} von {one.Length / 4} Punkten");
    }

    /// <summary>
    /// Ein Rasterpunkt muss EIN Wert sein - sonst ist er keiner.
    ///
    /// Das ist der Unterschied zwischen einem Raster und Gries, und er kostete eine
    /// Runde: Ein 4K-Bild auf zwei Stufen zu bringen ergibt Punkte von einem
    /// Bildpunkt Kantenlaenge, und das ist aus zwei Metern Abstand kein Muster mehr,
    /// sondern gleichmaessiges Rauschen.
    ///
    /// Geprueft wird deshalb die Eigenschaft, die das behebt: Innerhalb eines Blocks
    /// darf sich nichts unterscheiden. Und zwar fuer beide Arten - die Diffusion und
    /// die Ortsmuster, die ab Groesse zwei denselben Weg nehmen.
    /// </summary>
    private static void ACellIsSolid()
    {
        Check.Group("Ein Rasterpunkt ist ein Wert");

        const int cell = 4;

        foreach (bool place in new[] { false, true })
        {
            var pixels = Diffused(64, 64, 120, 2, cell: cell, place: place,
                                  pattern: DitherPattern.Ordered);

            int broken = 0;

            for (int by = 0; by < 64 / cell; by++)
            {
                for (int bx = 0; bx < 64 / cell; bx++)
                {
                    byte first = pixels[(by * cell * 64 + bx * cell) * 4];

                    for (int y = 0; y < cell; y++)
                    {
                        for (int x = 0; x < cell; x++)
                        {
                            int at = ((by * cell + y) * 64 + bx * cell + x) * 4;

                            if (pixels[at] != first) broken++;
                        }
                    }
                }
            }

            string what = place ? "Ortsmuster" : "Diffusion";

            Check.That(broken == 0, $"{what}: jeder Block ist einfarbig",
                       $"{broken} Abweichungen");

            // Und er muss trotzdem rastern - ein Block, der einfarbig ist, weil gar
            // nichts geschah, waere die billige Art, diesen Test zu bestehen.
            int between = 0;

            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i] > 1 && pixels[i] < 254) between++;

            Check.That(between == 0, $"{what}: und es bleibt nichts zwischen den Stufen",
                       $"{between}");
        }

        // Und der Fall, um den es eigentlich ging: ein BREITER, FLACHER Rasterpunkt.
        //
        // Ein quadratischer ergibt Punkte, ein breiter und flacher ergibt Striche -
        // und benachbarte Striche einer Zeile verschmelzen zu einer Linie. Ohne
        // getrennte Hoehe sieht ein Raster nie wie eine Schraffur aus, sondern immer
        // wie Rauschen mit groesseren Koernern.
        const int wide = 8;

        var dashes = Diffused(64, 64, 120, 2, cell: wide, tall: 1);

        int torn = 0;
        int rowsDiffer = 0;

        for (int y = 0; y < 64; y++)
        {
            for (int bx = 0; bx < 64 / wide; bx++)
            {
                byte first = dashes[(y * 64 + bx * wide) * 4];

                for (int x = 1; x < wide; x++)
                    if (dashes[(y * 64 + bx * wide + x) * 4] != first) torn++;

                // Und die Zeile darunter darf anders sein - sonst waere es ein Block
                // und kein Strich.
                if (y + 1 < 64 && dashes[((y + 1) * 64 + bx * wide) * 4] != first) rowsDiffer++;
            }
        }

        Check.That(torn == 0, "ein breiter Rasterpunkt bleibt in der Waagerechten ein Strich",
                   $"{torn} Bruchstellen");

        Check.That(rowsDiffer > 0,
                   "und die Zeile darunter entscheidet fuer sich - sonst waere es ein Block",
                   $"{rowsDiffer} Wechsel");

        // Die Helligkeit muss auch blockweise stehenbleiben.
        var coarse = Diffused(64, 64, 96, 2, cell: cell);

        double sum = 0;

        for (int i = 0; i < coarse.Length; i += 4) sum += coarse[i];

        double mean = sum / (coarse.Length / 4);

        Check.That(Math.Abs(mean - 96) < 4.0,
                   "und die Flaeche behaelt ihre Helligkeit auch in Bloecken", $"{mean:0.0}");
    }

    /// <summary>
    /// Zweifarbig: EINE Entscheidung je Rasterpunkt, und die Form bleibt lesbar.
    ///
    /// Ohne diesen Schalter entscheiden Rot, Gruen und Blau jeder fuer sich. Auf
    /// einem farbigen Bild laufen sie auseinander, und was als Silhouette erkennbar
    /// war, zerfaellt in drei unabhaengige Punktwolken. Genau das steht hier als Zahl:
    /// Bei einer FARBIGEN Flaeche muss zweifarbig gerastert genau zwei Werte je
    /// Bildpunkt kennen - Schwarz oder die gewaehlte Farbe -, waehrend die getrennte
    /// Rasterung acht Mischungen ergibt.
    /// </summary>
    private static void TwoColoursKeepTheForm()
    {
        Check.Group("Zweifarbig haelt die Form zusammen");

        static byte[] Coloured(bool duotone)
        {
            const int side = 48;

            int stride = side * 4;
            var pixels = new byte[stride * side];

            // Eine mitteltonige FARBE - dort laufen drei getrennte Kanaele am
            // weitesten auseinander.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 60;       // Blau
                pixels[i + 1] = 110;  // Gruen
                pixels[i + 2] = 170;  // Rot
                pixels[i + 3] = 255;
            }

            var tool = new DiffusionTool
            {
                Amount = 1f, Levels = 2, Pixels = 2, PixelsTall = 1,
                Duotone = duotone, Hue = 210f, Saturation = 0.8f,
            };

            tool.Prepare();

            var buffer = Marshal.AllocHGlobal(pixels.Length);

            try
            {
                Marshal.Copy(pixels, 0, buffer, pixels.Length);
                tool.Apply(buffer, side, side, stride);
                Marshal.Copy(buffer, pixels, 0, pixels.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return pixels;
        }

        static int Shades(byte[] pixels)
        {
            var seen = new HashSet<int>();

            for (int i = 0; i < pixels.Length; i += 4)
                seen.Add(pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16));

            return seen.Count;
        }

        int apart = Shades(Coloured(false));
        int together = Shades(Coloured(true));

        Console.WriteLine($"         getrennt {apart} Farben, zweifarbig {together}");

        Check.That(together == 2, "zweifarbig kennt genau zwei Farben", $"{together}");

        Check.That(apart > together,
                   "getrennt gerastert werden es mehr - die Kanaele laufen auseinander",
                   $"{apart}");

        // Und die helle Farbe muss die gewaehlte sein: bei Farbton 210 mehr Blau als
        // Rot. Andersherum waere der Regler eine Zierde.
        var lit = Coloured(true);

        int blue = 0, red = 0;

        for (int i = 0; i < lit.Length; i += 4)
        {
            if (lit[i] <= 1 && lit[i + 2] <= 1) continue;

            blue = Math.Max(blue, lit[i]);
            red = Math.Max(red, lit[i + 2]);
        }

        Check.That(blue > red, "und die helle Farbe ist die eingestellte",
                   $"blau {blue}, rot {red}");
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
