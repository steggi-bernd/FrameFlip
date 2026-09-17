using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Werkzeuge, die den Ort eines Bildpunktes brauchen: Vignette und Korn.
///
/// Zwei Dinge stehen hier auf dem Pruefstand, und beide sind Behauptungen aus dem
/// Entwurf. Erstens, dass eine Vignette auf der linearen Seite keinen Lichterschutz
/// braucht - eine Lampe soll weiss bleiben, statt zur grauen Scheibe zu werden.
/// Zweitens, dass Korn sich je Bild aendert; ein Korn, das stehenbleibt, ist Schmutz
/// auf der Linse und der genaue Gegensatz zur Regel, dass eine Messung einfrieren
/// muss.
/// </summary>
public static class OpticsInvariants
{
    public static void Run()
    {
        TheCornersGoDark();
        TheShapeFollowsTheImage();
        ALampStaysALamp();
        TheRoundnessReaches();
        GrainMovesWithTheFrame();
        GrainLeavesBlackAlone();
        GrainSizeAndColour();
        TheOrderIsLensThenFilm();
        TheCoarsePassKnowsWhereItIs();
        Persistence();
        WhatItCosts();
    }

    // -------------------------------------------------------------- Vignette

    private static void TheCornersGoDark()
    {
        Check.Group("Die Vignette dunkelt die Ecken");

        var frame = Flat(64, 64, 0.5f);

        var plain = Draw(frame, new GradingStack());
        var dark = Draw(frame, Vignette(1f));

        int middle = At(64, 32, 32);
        int corner = At(64, 1, 1);

        Check.That(plain[middle] == dark[middle], "in der Mitte bleibt alles, wie es war",
                   $"{dark[middle]} gegen {plain[middle]}");
        Check.That(dark[corner] < plain[corner] / 2, "in der Ecke ist es deutlich dunkler",
                   $"{dark[corner]} gegen {plain[corner]}");

        // Negativ geht es andersherum - manchmal will jemand den Randabfall einer
        // Aufnahme wegnehmen statt ihn hinzuzufuegen.
        var bright = Draw(frame, Vignette(-1f));
        Check.That(bright[corner] > plain[corner], "negativ hellt sie die Ecke auf",
                   $"{bright[corner]} gegen {plain[corner]}");
    }

    /// <summary>
    /// Der Abstand zaehlt in Bildpunkten, nicht in Anteilen der jeweiligen Kante.
    ///
    /// Sonst waere die Vignette auf einem breiten Bild ein Oval mit falschem
    /// Verhaeltnis - waagerecht dieselbe Abdunklung wie senkrecht, obwohl die eine
    /// Strecke doppelt so lang ist. Zwei Punkte im gleichen Abstand von der Mitte
    /// muessen dasselbe abbekommen, egal in welche Richtung.
    /// </summary>
    private static void TheShapeFollowsTheImage()
    {
        Check.Group("Die Vignette folgt dem Bild, nicht der Kante");

        var frame = Flat(96, 48, 0.5f);
        var drawn = Draw(frame, Vignette(1f));

        // Zwoelf Punkte nach rechts und zwoelf nach unten - dieselbe Strecke.
        int across = At(96, 48 + 12, 24);
        int down = At(96, 48, 24 + 12);

        Check.That(Math.Abs(drawn[across] - drawn[down]) <= 1,
                   "gleiche Strecke, gleiche Abdunklung",
                   $"{drawn[across]} gegen {drawn[down]}");
    }

    /// <summary>
    /// Die Begruendung dafuer, dass es hier keinen Lichterschutz gibt.
    ///
    /// Auf einem fertigen Bild macht Abdunkeln aus Weiss ein Grau, und aus einer
    /// Lampe wird eine graue Scheibe - dagegen brauchen andere Programme einen
    /// eigenen Regler. Vor der Sichtumwandlung wird eine Lampe mit 8 auf 4 gedaempft,
    /// und die ist immer noch weiss. Der Regler fehlt nicht, die Frage stellt sich
    /// nicht.
    /// </summary>
    private static void ALampStaysALamp()
    {
        Check.Group("Eine Lampe bleibt weiss");

        // In der Ecke eine Lampe, daneben eine weisse Flaeche.
        var lamp = Spot(64, 64, 0.5f, 8f, 4, 6, 6);
        var white = Spot(64, 64, 0.5f, 1f, 4, 6, 6);

        var dimmedLamp = Draw(lamp, Vignette(0.5f));
        var dimmedWhite = Draw(white, Vignette(0.5f));

        int at = At(64, 7, 7);

        Check.That(dimmedLamp[at] == 255, "die Lampe ist nach der Vignette immer noch weiss",
                   $"{dimmedLamp[at]}");
        Check.That(dimmedWhite[at] < 230, "die weisse Flaeche daneben wird dunkler",
                   $"{dimmedWhite[at]}");
    }

    private static void TheRoundnessReaches()
    {
        Check.Group("Die Rundung entscheidet, was die Ecke abbekommt");

        var frame = Flat(64, 64, 0.5f);

        var circle = Draw(frame, Vignette(1f, roundness: 0f));
        var rectangle = Draw(frame, Vignette(1f, roundness: 1f));
        var diamond = Draw(frame, Vignette(1f, roundness: -1f));

        int corner = At(64, 2, 2);

        Check.That(rectangle[corner] > circle[corner],
                   "das Rechteck reicht in die Ecke und laesst sie heller",
                   $"{rectangle[corner]} gegen {circle[corner]}");
        Check.That(diamond[corner] < circle[corner] + 1,
                   "die Raute laesst die Ecke ganz fallen",
                   $"{diamond[corner]} gegen {circle[corner]}");

        // In der Mitte der Kante sind sich alle drei einig - dort liegen Kreis,
        // Rechteck und Raute auf demselben Abstand.
        int edge = At(64, 1, 32);

        Check.That(Math.Abs(rectangle[edge] - circle[edge]) <= 1,
                   "an der Kantenmitte unterscheiden sie sich nicht",
                   $"{rectangle[edge]} gegen {circle[edge]}");
    }

    // ------------------------------------------------------------------ Korn

    /// <summary>
    /// Die Probe aus Abschnitt 10: Korn MUSS sich je Bild aendern.
    ///
    /// Und es muss sich zugleich reproduzieren lassen - dasselbe Bild zweimal
    /// gerechnet ergibt dasselbe Korn, sonst flimmerte schon die Vorschau, und ein
    /// Export waere nie mit einem zweiten vergleichbar.
    /// </summary>
    private static void GrainMovesWithTheFrame()
    {
        Check.Group("Korn wandert mit der Bildnummer");

        var frame = Flat(64, 64, 0.4f);
        var stack = Grain(1f);

        var first = Draw(frame, stack, number: 12);
        var again = Draw(frame, stack, number: 12);
        var next = Draw(frame, stack, number: 13);

        Check.That(Same(first, again), "dasselbe Bild ergibt dasselbe Korn");
        Check.That(!Same(first, next), "das naechste ein anderes");

        // Und es ist ueberhaupt etwas passiert.
        var plain = Draw(frame, new GradingStack());
        Check.That(!Same(plain, first), "ohne Korn sieht die Flaeche anders aus");

        int changed = 0;
        for (int i = 0; i < first.Length; i += 4)
            if (first[i + 2] != next[i + 2]) changed++;

        Check.That(changed > first.Length / 4 / 2, "und der Wechsel betrifft das halbe Bild",
                   $"{changed} von {first.Length / 4}");
    }

    private static void GrainLeavesBlackAlone()
    {
        Check.Group("Korn laesst Schwarz schwarz");

        var frame = Flat(48, 48, 0f);
        var drawn = Draw(frame, Grain(1f), number: 3);

        int worst = 0;
        for (int i = 0; i < drawn.Length; i += 4) worst = Math.Max(worst, drawn[i + 2]);

        Check.That(worst == 0, "eine schwarze Flaeche bleibt schwarz", $"{worst}");

        // Der Grund steht am Werkzeug: multipliziert, nicht dazugezaehlt. Ein
        // addiertes Rauschen haette die Schatten aufgehellt.
        var tool = new GrainTool { Amount = 1f };
        tool.Prepare();

        float r = 0f, g = 0f, b = 0f;
        tool.Apply(in Place, 17, 23, ref r, ref g, ref b);

        Check.Near(r, 0.0, 1e-6, "und das gilt auch fuer den einzelnen Punkt");
    }

    private static void GrainSizeAndColour()
    {
        Check.Group("Korngroesse und Farbe");

        // Breit genug, damit die Korngroesse ueberhaupt ankommt: Sie ist auf 1080p
        // bezogen, und auf einem schmalen Testbild faellt jede Groesse auf denselben
        // Mindestwert von einem Bildpunkt zusammen.
        var frame = Flat(960, 64, 0.4f);

        double fine = Roughness(Draw(frame, Grain(1f, size: 1), number: 5));
        double coarse = Roughness(Draw(frame, Grain(1f, size: 12), number: 5));

        Check.That(coarse < fine * 0.6, "grosses Korn wechselt langsamer von Punkt zu Punkt",
                   $"{coarse:0.0} gegen {fine:0.0}");

        // Ohne Farbe bekommen alle drei Kanaele denselben Faktor - auf einer grauen
        // Flaeche muss das Ergebnis grau bleiben.
        var square = Flat(128, 128, 0.4f);
        var grey = Draw(square, Grain(1f, colour: 0f), number: 5);

        int apart = 0;
        for (int i = 0; i < grey.Length; i += 4)
            if (grey[i] != grey[i + 2]) apart++;

        Check.That(apart == 0, "ohne Farbe bleibt graues Korn grau", $"{apart} Punkte bunt");

        var coloured = Draw(square, Grain(1f, colour: 1f), number: 5);

        int bunt = 0;
        for (int i = 0; i < coloured.Length; i += 4)
            if (coloured[i] != coloured[i + 2]) bunt++;

        Check.That(bunt > coloured.Length / 4 / 4, "mit Farbe laufen die Kanaele auseinander",
                   $"{bunt}");
    }

    // -------------------------------------------------------- Reihenfolge

    private static void TheOrderIsLensThenFilm()
    {
        Check.Group("Erst die Linse, dann der Film");

        var stack = new GradingStack
        {
            Optics =
            {
                new GrainTool { Amount = 0.5f },
                new VignetteTool { Amount = 0.5f },
            },
        };

        var prepared = stack.Prepare();

        Check.That(prepared.Optics.Length == 2, "beide sind dabei", $"{prepared.Optics.Length}");
        Check.That(prepared.Optics[0] is VignetteTool, "die Vignette zuerst");
        Check.That(prepared.Optics[1] is GrainTool, "das Korn darueber");

        // In Grundstellung zaehlt keines mit.
        var quiet = new GradingStack
        {
            Optics = { new GrainTool { Amount = 0f }, new VignetteTool { Amount = 0f } },
        };

        Check.That(quiet.Prepare().Optics.Length == 0, "abgedrehte Werkzeuge zaehlen nicht");
        Check.That(quiet.IsNeutral, "und der Stapel gilt als neutral");
        Check.That(quiet.Prepare().IsEmpty, "der vorbereitete Stapel ist leer");
        Check.That(!stack.Prepare().IsEmpty, "der andere nicht");
    }

    /// <summary>
    /// Beim Reglerzug wird auf einem Gitter gerechnet - und ein Ortswerkzeug muss
    /// dort wissen, wo im BILD es steht, nicht wo im Gitter.
    ///
    /// Verwechselt man die beiden, sitzt die Vignette im oberen linken Viertel. Das
    /// ist der Fehler, der beim Bauen am naechsten liegt, und im stehenden Bild
    /// faellt er nicht auf, weil das volle Bild richtig rechnet.
    /// </summary>
    private static void TheCoarsePassKnowsWhereItIs()
    {
        Check.Group("Auch grob weiss die Vignette, wo sie ist");

        var frame = Flat(96, 96, 0.5f);

        var fine = Draw(frame, Vignette(1f));
        var rough = Draw(frame, Vignette(1f), step: 4);

        int worst = 0;
        for (int i = 0; i < fine.Length; i++) worst = Math.Max(worst, Math.Abs(fine[i] - rough[i]));

        Check.That(worst < 12, "der grobe Durchgang bleibt nahe am vollen", $"{worst} von 255");

        // Und die Mitte ist in beiden Faellen die Mitte.
        int middle = At(96, 48, 48);
        Check.That(Math.Abs(fine[middle] - rough[middle]) <= 1, "die Mitte liegt in der Mitte",
                   $"{rough[middle]} gegen {fine[middle]}");
    }

    private static void Persistence()
    {
        Check.Group("Vignette und Korn ueberleben das Speichern");

        var stack = new GradingStack
        {
            Optics =
            {
                new VignetteTool { Amount = 0.7f, Midpoint = 0.3f, Roundness = -0.4f, Feather = 0.8f },
                new GrainTool { Amount = 0.25f, Size = 5, Roughness = 0.9f, Colour = 0.6f },
            },
        };

        var read = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var vignette = read.Optics.OfType<VignetteTool>().FirstOrDefault();
        Check.That(vignette is not null, "die Vignette mit ihrem Typ");
        if (vignette is not null)
        {
            Check.Near(vignette.Midpoint, 0.3, 1e-5, "mit ihrem Ansatz");
            Check.Near(vignette.Roundness, -0.4, 1e-5, "und ihrer Rundung");
        }

        var grain = read.Optics.OfType<GrainTool>().FirstOrDefault();
        Check.That(grain is not null, "das Korn ebenso");
        if (grain is not null)
        {
            Check.That(grain.Size == 5, "mit seiner Groesse", $"{grain.Size}");
            Check.Near(grain.Colour, 0.6, 1e-5, "und seiner Farbe");
        }

        // Kopieren muss tief sein - sonst zoege ein Reglerzug waehrend des
        // Stapellaufs die laufende Ausgabe mit.
        var copy = stack.Clone();
        stack.Optics.OfType<VignetteTool>().First().Amount = 0.1f;

        Check.Near(copy.Optics.OfType<VignetteTool>().First().Amount, 0.7, 1e-5,
                   "eine Kopie bewegt sich nicht mit");
    }

    /// <summary>
    /// Was die Ortswerkzeuge kosten.
    ///
    /// Sie brauchen keinen Puffer, aber das Korn braucht Wuerfe: vier je Bildpunkt,
    /// acht mit der zweiten Lage. Waere das teuer, muesste man es wissen, bevor es
    /// in einem Stapellauf ueber dreihundert Bilder steht.
    /// </summary>
    private static void WhatItCosts()
    {
        Check.Group("Die Ortswerkzeuge bleiben bezahlbar");

        var frame = Flat(1920, 1080, 0.4f);

        var plain = new GradingStack();
        var vignette = Vignette(0.6f);
        var grain = Grain(0.5f);

        var both = new GradingStack
        {
            Optics =
            {
                new VignetteTool { Amount = 0.6f },
                new GrainTool { Amount = 0.5f },
            },
        };

        Draw(frame, both);

        double without = Fastest(() => Draw(frame, plain));
        double withVignette = Fastest(() => Draw(frame, vignette));
        double withGrain = Fastest(() => Draw(frame, grain));
        double withBoth = Fastest(() => Draw(frame, both));

        Console.WriteLine($"         1080p: ohne {without:0.0} ms, Vignette {withVignette:0.0} ms, " +
                          $"Korn {withGrain:0.0} ms, beide {withBoth:0.0} ms");

        Check.That(withBoth < without + 60, "beide zusammen bleiben im Rahmen",
                   $"{withBoth:0.0} gegen {without:0.0} ms");
    }

    private static double Fastest(Action action)
    {
        double best = double.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            action();
            watch.Stop();

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    // ------------------------------------------------------------------- Handwerk

    private static readonly OpticsPlace Place = new(64, 64, 1);

    private static GradingStack Vignette(float amount, float roundness = 0f)
        => new() { Optics = { new VignetteTool { Amount = amount, Roundness = roundness } } };

    private static GradingStack Grain(float amount, int size = 2, float colour = 0.2f)
        => new() { Optics = { new GrainTool { Amount = amount, Size = size, Colour = colour } } };

    /// <summary>Der Platz des roten Kanals eines Bildpunktes im Bgra-Feld.</summary>
    private static int At(int width, int x, int y) => (y * width + x) * 4 + 2;

    /// <summary>Wie stark sich benachbarte Punkte unterscheiden - das Mass fuer Korngroesse.</summary>
    private static double Roughness(byte[] pixels)
    {
        double sum = 0;
        int count = 0;

        for (int i = 2; i + 4 < pixels.Length; i += 4)
        {
            sum += Math.Abs(pixels[i + 4] - pixels[i]);
            count++;
        }

        return count > 0 ? sum / count * 100 : 0;
    }

    private static byte[] Draw(FloatFrame frame, GradingStack stack, int step = 1, int number = 0)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                      stack.Prepare(), buffer, stride, step, null, number);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static bool Same(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;

        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return false;

        return true;
    }

    private static FloatFrame Flat(int width, int height, float value)
    {
        int count = width * height;

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = Enumerable.Repeat(value, count).ToArray(),
            G = Enumerable.Repeat(value, count).ToArray(),
            B = Enumerable.Repeat(value, count).ToArray(),
            A = Enumerable.Repeat(1f, count).ToArray(),
            IsSceneReferred = false,
        };
    }

    /// <summary>Eine Flaeche mit einem hellen Quadrat an einer gewaehlten Stelle.</summary>
    private static FloatFrame Spot(int width, int height, float background, float value,
                                   int size, int left, int top)
    {
        int count = width * height;
        var values = new float[count];

        Array.Fill(values, background);

        for (int y = top; y < top + size && y < height; y++)
            for (int x = left; x < left + size && x < width; x++)
                values[y * width + x] = value;

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = values,
            G = (float[])values.Clone(),
            B = (float[])values.Clone(),
            A = Enumerable.Repeat(1f, count).ToArray(),
            IsSceneReferred = false,
        };
    }
}
