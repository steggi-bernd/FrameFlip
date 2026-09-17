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
        TheMiddleStandsStill();
        TheLensBendsLines();
        TheChannelsDrift();
        GrainSurvivesTheLens();
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

    // ---------------------------------------------------------- Geometrie

    /// <summary>
    /// Die Mitte bleibt, wo sie ist - bei beiden Werkzeugen.
    ///
    /// Beide sind radial, und radial heisst: in der Mitte null. Waere das nicht so,
    /// waere es keine Optik, sondern eine Verschiebung, und die faellt sofort auf -
    /// das ganze Bild rutscht.
    /// </summary>
    private static void TheMiddleStandsStill()
    {
        Check.Group("In der Mitte verschiebt sich nichts");

        // Ein Muster, in dem jede Verschiebung sichtbar wird.
        var frame = Checker(128, 128, 0.2f, 0.8f, 8);

        var plain = Draw(frame, new GradingStack());
        var bent = Draw(frame, Distortion(1f));
        var fringed = Draw(frame, Chromatic(1f));

        int middle = At(128, 64, 64);

        Check.That(Math.Abs(bent[middle] - plain[middle]) <= 1,
                   "die Verzeichnung laesst die Mitte stehen",
                   $"{bent[middle]} gegen {plain[middle]}");
        Check.That(Math.Abs(fringed[middle] - plain[middle]) <= 1,
                   "die Farbsaeume auch",
                   $"{fringed[middle]} gegen {plain[middle]}");

        // Und weiter aussen sehr wohl.
        Check.That(!Same(plain, bent), "aussen aendert die Verzeichnung etwas");
        Check.That(!Same(plain, fringed), "und die Farbsaeume ebenfalls");
    }

    /// <summary>
    /// Die Verzeichnung greift nach aussen oder nach innen - und der Massstab hebt
    /// genau das wieder auf.
    /// </summary>
    private static void TheLensBendsLines()
    {
        Check.Group("Die Verzeichnung holt von weiter draussen");

        // Ein Bild, das nach aussen hin heller wird: Dann sagt die Helligkeit an
        // einer Stelle, wie weit draussen sie herkommt.
        var frame = Ramp(128, 128);

        var plain = Draw(frame, new GradingStack());
        var outward = Draw(frame, Distortion(1f));
        var inward = Draw(frame, Distortion(-1f));

        // Auf halbem Weg zur Ecke.
        int at = At(128, 96, 96);

        Check.That(outward[at] > plain[at], "positiv holt sie von weiter aussen",
                   $"{outward[at]} gegen {plain[at]}");
        Check.That(inward[at] < plain[at], "negativ von weiter innen",
                   $"{inward[at]} gegen {plain[at]}");

        // Der Massstab allein vergroessert, ohne zu woelben.
        var enlarged = Draw(frame, Distortion(0f, scale: 0.8f));
        Check.That(enlarged[at] < plain[at], "ein kleinerer Massstab vergroessert",
                   $"{enlarged[at]} gegen {plain[at]}");

        // Und in Grundstellung bleibt alles, wie es war - Byte fuer Byte.
        var untouched = Draw(frame, Distortion(0f, scale: 1f));
        Check.That(Same(plain, untouched), "in Grundstellung ruehrt sie nichts an");

        // Beim Reglerzug wird auf einem Gitter abgetastet. Der Ort muss dabei im BILD
        // gerechnet werden, nicht im Gitter - sonst saesse die Mitte der Verzeichnung
        // beim Ziehen an einer anderen Stelle als im fertigen Bild, und das Bild
        // spraenge beim Loslassen.
        var rough = Draw(frame, Distortion(1f), step: 4);

        int worst = 0;
        double sum = 0;

        for (int i = 0; i < outward.Length; i++)
        {
            int apart = Math.Abs(outward[i] - rough[i]);

            worst = Math.Max(worst, apart);
            sum += apart;
        }

        double average = sum / outward.Length;

        // Der Durchschnitt ist die aussagekraeftige Zahl: Ein Versatz zeigte sich in
        // ihm sofort. Der groesste Einzelwert steht an der steilsten Stelle und ist
        // die Interpolation zwischen zwei Gitterpunkten, nicht ein falscher Ort.
        Check.That(average < 2, "grob gerechnet landet sie an derselben Stelle",
                   $"im Mittel {average:0.00} von 255");
        Check.That(worst < 20, "und nirgends weit daneben", $"hoechstens {worst}");
    }

    /// <summary>
    /// Die drei Kanaele laufen auseinander, und Gruen bleibt stehen.
    ///
    /// Gruen traegt fast die ganze Helligkeit. Wanderte es mit, waere das Bild
    /// verschoben statt farbgesaeumt, und niemand koennte sagen, warum es unscharf
    /// aussieht.
    /// </summary>
    private static void TheChannelsDrift()
    {
        Check.Group("Farbsaeume ziehen Rot und Blau auseinander");

        // Ein graues Schachbrett: Alles Farbige darin kann nur von der Aberration
        // kommen. Auf einem Verlauf waere der Versatz zwar da, aber nicht zu messen -
        // ein Prozent auf einer flachen Rampe sind weniger als ein Byte.
        var frame = Checker(128, 128, 0.08f, 0.9f, 6);

        var plain = Draw(frame, new GradingStack());
        var fringed = Draw(frame, Chromatic(1f));

        int coloured = 0;

        for (int i = 0; i < fringed.Length; i += 4)
            if (Math.Abs(fringed[i] - fringed[i + 2]) > 8) coloured++;

        Check.That(coloured > 200, "auf einem grauen Bild entstehen farbige Saeume",
                   $"{coloured} Punkte");

        // Und sie wachsen nach aussen. Ganz verschwinden sie in der Mitte nicht: An
        // einer harten Kante zeigt schon ein Zehntel Bildpunkt Versatz eine Farbe,
        // und ein Schachbrett besteht aus harten Kanten. Was zaehlt, ist das
        // Verhaeltnis - radial heisst, dass aussen mehr ist als innen.
        double inner = Fringe(fringed, 128, 64, 64, 20);
        double outer = Fringe(fringed, 128, 108, 108, 20);

        Check.That(outer > inner * 3, "und sie wachsen nach aussen",
                   $"aussen {outer:0.0}, innen {inner:0.0}");

        // Gruen bleibt ueberall stehen - es traegt fast die ganze Helligkeit, und ein
        // wanderndes Gruen waere eine Verschiebung statt eines Saums.
        int movedGreen = 0;
        for (int i = 1; i < plain.Length; i += 4)
            if (plain[i] != fringed[i]) movedGreen++;

        Check.That(movedGreen == 0, "Gruen bleibt, wo es war", $"{movedGreen} Punkte");

        // Und ohne Aberration ist das Bild ueberall grau.
        int already = 0;
        for (int i = 0; i < plain.Length; i += 4)
            if (Math.Abs(plain[i] - plain[i + 2]) > 8) already++;

        Check.That(already == 0, "vorher war nichts davon da", $"{already} Punkte");
    }

    /// <summary>
    /// Das Korn wird nicht mitverschoben.
    ///
    /// Es sitzt im Film, und der liegt hinter der Linse. Waere es vorher da,
    /// verzoege die Verzeichnung es mit, und die chromatische Aberration gaebe ihm
    /// einen farbigen Saum - ein Korn mit Farbsaum ist kein Korn, sondern ein Abdruck
    /// davon.
    ///
    /// Geprueft wird es an der einzigen Stelle, an der es sich zeigt: Mit Geometrie
    /// UND Korn muss das Korn dasselbe sein wie ohne Geometrie - an einer Stelle, die
    /// die Geometrie sonst leer laesst.
    /// </summary>
    private static void GrainSurvivesTheLens()
    {
        Check.Group("Das Korn sitzt hinter der Linse");

        var frame = Flat(128, 128, 0.4f);

        var grainOnly = new GradingStack { Optics = { new GrainTool { Amount = 1f, Size = 2 } } };

        var withLens = new GradingStack
        {
            Optics = { new GrainTool { Amount = 1f, Size = 2 } },
            Geometry = { new ChromaticTool { Amount = 1f } },
        };

        var plain = Draw(frame, grainOnly, number: 9);
        var bent = Draw(frame, withLens, number: 9);

        // Auf einer gleichmaessigen Flaeche verschiebt die Geometrie nichts, was zu
        // sehen waere - bliebe das Korn davor, saehe man es trotzdem.
        Check.That(Same(plain, bent), "auf gleichmaessiger Flaeche aendert die Linse am Korn nichts");
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

        stack.Geometry.Add(new DistortionTool { Amount = 0.5f, Scale = 0.9f });
        stack.Geometry.Add(new ChromaticTool { Amount = -0.3f });

        var read = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var lens = read.Geometry.OfType<DistortionTool>().FirstOrDefault();
        Check.That(lens is not null, "die Verzeichnung mit ihrem Typ");
        if (lens is not null) Check.Near(lens.Scale, 0.9, 1e-5, "mit ihrem Massstab");

        var fringe = read.Geometry.OfType<ChromaticTool>().FirstOrDefault();
        Check.That(fringe is not null, "die Farbsaeume ebenso");
        if (fringe is not null) Check.Near(fringe.Amount, -0.3, 1e-5, "mitsamt Vorzeichen");

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

        stack.Geometry.OfType<DistortionTool>().First().Amount = 0.05f;
        Check.Near(copy.Geometry.OfType<DistortionTool>().First().Amount, 0.5, 1e-5,
                   "und auch die Geometrie nicht");
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

    private static GradingStack Distortion(float amount, float scale = 1f)
        => new() { Geometry = { new DistortionTool { Amount = amount, Scale = scale } } };

    private static GradingStack Chromatic(float amount)
        => new() { Geometry = { new ChromaticTool { Amount = amount } } };

    private static GradingStack Grain(float amount, int size = 2, float colour = 0.2f)
        => new() { Optics = { new GrainTool { Amount = amount, Size = size, Colour = colour } } };

    /// <summary>
    /// Wie farbig ein Ausschnitt ist: der mittlere Abstand zwischen Rot und Blau.
    /// Auf einem grauen Bild ist das genau der Farbsaum und sonst nichts.
    /// </summary>
    private static double Fringe(byte[] pixels, int width, int centreX, int centreY, int size)
    {
        double sum = 0;
        int count = 0;

        for (int y = centreY - size / 2; y < centreY + size / 2; y++)
        {
            for (int x = centreX - size / 2; x < centreX + size / 2; x++)
            {
                int at = At(width, x, y);
                sum += Math.Abs(pixels[at] - pixels[at - 2]);
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }

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

    /// <summary>Ein Schachbrett - jede Verschiebung wird darin sichtbar.</summary>
    private static FloatFrame Checker(int width, int height, float dark, float light, int size)
    {
        int count = width * height;
        var values = new float[count];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                values[y * width + x] = (x / size + y / size) % 2 == 0 ? dark : light;

        return Frame(width, height, values);
    }

    /// <summary>
    /// Ein Verlauf, der nach aussen hin heller wird.
    ///
    /// Damit sagt die Helligkeit an einer Stelle, wie weit draussen der Punkt
    /// herkommt - und eine radiale Verschiebung laesst sich an einer einzigen Zahl
    /// ablesen.
    /// </summary>
    private static FloatFrame Ramp(int width, int height)
    {
        int count = width * height;
        var values = new float[count];

        float centreX = width / 2f;
        float centreY = height / 2f;
        float reach = MathF.Sqrt(centreX * centreX + centreY * centreY);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = x + 0.5f - centreX;
                float dy = y + 0.5f - centreY;

                values[y * width + x] = MathF.Sqrt(dx * dx + dy * dy) / reach * 0.8f;
            }
        }

        return Frame(width, height, values);
    }

    private static FloatFrame Frame(int width, int height, float[] values)
        => new()
        {
            Width = width,
            Height = height,
            R = values,
            G = (float[])values.Clone(),
            B = (float[])values.Clone(),
            A = Enumerable.Repeat(1f, width * height).ToArray(),
            IsSceneReferred = false,
        };

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
