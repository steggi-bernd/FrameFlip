using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Pixel Sorting - und die eine Zusage, die es von allem anderen unterscheidet.
///
/// Ein Raster entscheidet je Bildpunkt, ein Verzug holt je Bildpunkt woanders her.
/// Beides erfindet Werte. Umsortieren nicht: Es verschiebt Bildpunkte entlang einer
/// Linie, ohne einen einzigen zu erfinden oder wegzuwerfen. Was herauskommt, enthaelt
/// GENAU DIESELBEN Farben wie vorher, nur in anderer Ordnung.
///
/// Das ist die Zusage, die hier gemessen wird, und sie ist streng genug, um fast
/// jeden denkbaren Fehler zu fangen: Wer beim Zurueckschreiben danebengreift,
/// verdoppelt einen Punkt und verliert einen anderen; wer ohne Zwischenkopie
/// arbeitet, ueberschreibt, was er noch braucht. Beides faellt sofort auf, wenn man
/// die Punkte zaehlt.
/// </summary>
public static class SortInvariants
{
    public static void Run()
    {
        NothingIsInventedOrLost();
        TheWindowDecidesWhatMoves();
        RunsStayWithinTheirLimit();
        ColumnsSortDownward();
        EveryAngleKeepsEveryPixel();
        OldRecipesSortAsBefore();
        EdgesHoldTheOutline();
        SkipAndChanceBehave();
        Persistence();
        WhatItCosts();
    }

    private const int Width = 64;
    private const int Height = 24;

    /// <summary>
    /// Ein Bild aus lauter verschiedenen Grauwerten - damit sich jeder Punkt
    /// wiedererkennen laesst.
    /// </summary>
    private static byte[] Noise(int seed = 7)
    {
        var pixels = new byte[Width * Height * 4];
        int state = seed;

        for (int i = 0; i < Width * Height; i++)
        {
            state = state * 1103515245 + 12345;

            byte grey = (byte)((state >> 16) & 0xFF);

            pixels[i * 4] = grey;
            pixels[i * 4 + 1] = grey;
            pixels[i * 4 + 2] = grey;
            pixels[i * 4 + 3] = 255;
        }

        return pixels;
    }

    /// <summary>Laesst den Durchgang auf einem Puffer laufen und gibt ihn zurueck.</summary>
    private static byte[] Sorted(byte[] pixels, SortTool tool)
    {
        var copy = (byte[])pixels.Clone();

        tool.Prepare();

        var buffer = Marshal.AllocHGlobal(copy.Length);

        try
        {
            Marshal.Copy(copy, 0, buffer, copy.Length);

            tool.Apply(buffer, Width, Height, Width * 4);

            Marshal.Copy(buffer, copy, 0, copy.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return copy;
    }

    /// <summary>Wieviele Punkte es von jeder Farbe gibt - je Zeile.</summary>
    private static Dictionary<int, int> Tally(byte[] pixels, int row)
    {
        var count = new Dictionary<int, int>();

        for (int x = 0; x < Width; x++)
        {
            int at = (row * Width + x) * 4;
            int colour = pixels[at] | (pixels[at + 1] << 8) | (pixels[at + 2] << 16);

            count[colour] = count.GetValueOrDefault(colour) + 1;
        }

        return count;
    }

    private static void NothingIsInventedOrLost()
    {
        Check.Group("Umsortieren erfindet nichts und verliert nichts");

        var before = Noise();

        var after = Sorted(before, new SortTool { Low = 0f, High = 1f });

        int wrong = 0;

        for (int y = 0; y < Height; y++)
        {
            var was = Tally(before, y);
            var now = Tally(after, y);

            foreach (var (colour, times) in was)
                if (now.GetValueOrDefault(colour) != times) wrong++;
        }

        Check.That(wrong == 0,
                   "jede Zeile fuehrt hinterher genau dieselben Farben, gleich oft",
                   $"{wrong} Abweichungen");

        // Und sie sind wirklich sortiert - sonst waere die Zusage oben auch von einem
        // Werkzeug erfuellt, das gar nichts tut.
        int rising = 0;

        for (int y = 0; y < Height; y++)
        {
            bool ordered = true;

            for (int x = 1; x < Width; x++)
                if (after[(y * Width + x) * 4] < after[(y * Width + x - 1) * 4]) ordered = false;

            if (ordered) rising++;
        }

        Check.That(rising == Height, "und jede Zeile steigt von links nach rechts",
                   $"{rising} von {Height}");

        // Absteigend genau andersherum.
        var down = Sorted(before, new SortTool { Low = 0f, High = 1f, Descending = true });

        bool falling = true;

        for (int x = 1; x < Width; x++)
            if (down[x * 4] > down[(x - 1) * 4]) falling = false;

        Check.That(falling, "absteigend faellt sie");

        // Nichts eingestellt heisst nichts getan.
        var quiet = new SortTool();

        Check.That(quiet.IsNeutral, "ein geschlossenes Fenster gilt als neutral");

        var untouched = Sorted(before, quiet);

        Check.That(untouched.SequenceEqual(before),
                   "und laesst den Puffer Byte fuer Byte in Ruhe");
    }

    /// <summary>
    /// Die Schwelle ist das ganze Werkzeug: Was ausserhalb liegt, bleibt STEHEN.
    ///
    /// Wer jede Zeile vollstaendig sortiert, bekommt einen Farbverlauf und kein Bild.
    /// Dass die Punkte ausserhalb des Fensters an ihrem Platz bleiben, ist der
    /// Unterschied zwischen einem Effekt und einer Zerstoerung.
    /// </summary>
    private static void TheWindowDecidesWhatMoves()
    {
        Check.Group("Das Fenster entscheidet, was sich bewegt");

        var before = Noise();

        // Nur das obere Drittel der Helligkeit.
        var after = Sorted(before, new SortTool { Low = 0.66f, High = 1f });

        int moved = 0, stayed = 0, wrongly = 0;

        for (int i = 0; i < Width * Height; i++)
        {
            int at = i * 4;

            float key = (0.2126f * before[at + 2] + 0.7152f * before[at + 1] +
                         0.0722f * before[at]) / 255f;

            bool inside = key is >= 0.66f and <= 1f;

            if (before[at] == after[at])
            {
                stayed++;
                continue;
            }

            moved++;

            // Ein Punkt AUSSERHALB des Fensters darf sich nicht veraendert haben.
            if (!inside) wrongly++;
        }

        Console.WriteLine($"         bewegt {moved}, gleich geblieben {stayed}, " +
                          $"davon zu Unrecht bewegt {wrongly}");

        Check.That(moved > 0, "im Fenster wird umsortiert", $"{moved}");

        Check.That(wrongly == 0,
                   "und ausserhalb bleibt jeder Punkt, wo er war",
                   $"{wrongly} Punkte");

        // Ein geschlossenes Fenster in der Mitte bewegt gar nichts.
        var narrow = Sorted(before, new SortTool { Low = 0.5f, High = 0.5f });

        Check.That(narrow.SequenceEqual(before),
                   "ein Fenster ohne Breite bewegt nichts");

        // Und die Grenzen duerfen verdreht angegeben werden, ohne dass es kippt.
        var flipped = new SortTool { Low = 1f, High = 0.66f };

        flipped.Prepare();

        var same = Sorted(before, flipped);
        var right = Sorted(before, new SortTool { Low = 0.66f, High = 1f });

        Check.That(same.SequenceEqual(right),
                   "verdreht angegebene Grenzen ergeben dasselbe");
    }

    private static void RunsStayWithinTheirLimit()
    {
        Check.Group("Eine Laengengrenze haelt");

        var before = Noise();

        const int limit = 8;

        var after = Sorted(before, new SortTool { Low = 0f, High = 1f, Longest = limit });

        // Das Fenster steht offen, also ist die ganze Zeile ein Lauf - und die
        // Abschnitte liegen damit auf Vielfachen der Grenze. Das laesst sich genau
        // pruefen, statt nur "irgendwo faellt es wieder".
        //
        // Gemessen wird INNERHALB der Abschnitte: Zwei benachbarte Abschnitte duerfen
        // zufaellig auch aneinander anschliessen, ohne dass etwas falsch waere.
        int inside = 0, breaks = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 1; x < Width; x++)
            {
                bool border = x % limit == 0;

                if (border)
                {
                    if (after[(y * Width + x) * 4] < after[(y * Width + x - 1) * 4]) breaks++;
                    continue;
                }

                if (after[(y * Width + x) * 4] < after[(y * Width + x - 1) * 4]) inside++;
            }
        }

        Console.WriteLine($"         Rueckschritte innerhalb der Abschnitte {inside}, " +
                          $"an den Grenzen {breaks}");

        Check.That(inside == 0,
                   "innerhalb eines Abschnitts steigt es durchgehend",
                   $"{inside} Rueckschritte");

        Check.That(breaks > 0,
                   "und an den Grenzen faengt es wieder von vorn an",
                   $"{breaks}");

        // Ohne Grenze ist die ganze Zeile ein Abschnitt.
        var whole = Sorted(before, new SortTool { Low = 0f, High = 1f });

        int wholeBreaks = 0;

        for (int x = 1; x < Width; x++)
            if (whole[x * 4] < whole[(x - 1) * 4]) wholeBreaks++;

        Check.That(wholeBreaks == 0, "ohne Grenze bleibt sie ein Stueck", $"{wholeBreaks}");
    }

    private static void ColumnsSortDownward()
    {
        Check.Group("Auf Spalten gestellt laufen die Laeufe nach unten");

        var before = Noise();

        var after = Sorted(before, new SortTool { Low = 0f, High = 1f, Vertical = true });

        // Jede SPALTE steigt, und die Zeilen tun es nicht mehr.
        int columns = 0;

        for (int x = 0; x < Width; x++)
        {
            bool ordered = true;

            for (int y = 1; y < Height; y++)
                if (after[((y * Width) + x) * 4] < after[(((y - 1) * Width) + x) * 4]) ordered = false;

            if (ordered) columns++;
        }

        Check.That(columns == Width, "jede Spalte steigt von oben nach unten",
                   $"{columns} von {Width}");

        // Und auch hier: nichts erfunden, nichts verloren - diesmal ueber das ganze
        // Bild gezaehlt, weil die Punkte jetzt zwischen den Zeilen wandern.
        static Dictionary<int, int> All(byte[] pixels)
        {
            var count = new Dictionary<int, int>();

            for (int i = 0; i < Width * Height; i++)
            {
                int at = i * 4;
                int colour = pixels[at] | (pixels[at + 1] << 8) | (pixels[at + 2] << 16);

                count[colour] = count.GetValueOrDefault(colour) + 1;
            }

            return count;
        }

        var was = All(before);
        var now = All(after);

        int wrong = was.Count(e => now.GetValueOrDefault(e.Key) != e.Value);

        Check.That(wrong == 0, "und das Bild fuehrt dieselben Farben wie vorher",
                   $"{wrong} Abweichungen");
    }

    /// <summary>
    /// Was es kostet - bei 1080p und bei 4K.
    ///
    /// Dieser Durchgang laeuft auf EINEM Faden und in voller Aufloesung; das ist keine
    /// Nachlaessigkeit, sondern das Verfahren. Er faellt beim Ziehen aus und kommt
    /// beim Loslassen dazu - und genau dort wird er bemerkt. Eine Sekunde Warten nach
    /// jedem Regler waere unbrauchbar, auch wenn das Ergebnis stimmt.
    /// </summary>
    private static void WhatItCosts()
    {
        Check.Group("Pixel Sorting bleibt bezahlbar");

        static double Fastest(Action work)
        {
            work();

            double best = double.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();

                work();

                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        static double Cost(int width, int height, SortTool tool)
        {
            var pixels = new byte[width * height * 4];
            int state = 11;

            for (int i = 0; i < width * height; i++)
            {
                state = state * 1103515245 + 12345;

                byte grey = (byte)((state >> 16) & 0xFF);

                pixels[i * 4] = grey;
                pixels[i * 4 + 1] = grey;
                pixels[i * 4 + 2] = grey;
                pixels[i * 4 + 3] = 255;
            }

            tool.Prepare();

            var buffer = Marshal.AllocHGlobal(pixels.Length);

            try
            {
                Marshal.Copy(pixels, 0, buffer, pixels.Length);

                return Fastest(() => tool.Apply(buffer, width, height, width * 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // Der teuerste Fall: das Fenster ganz offen, keine Laengengrenze - also eine
        // einzige Sortierung ueber die volle Zeilenbreite.
        var worst = new SortTool { Low = 0f, High = 1f };

        double full = Cost(1920, 1080, worst);
        double uhd = Cost(3840, 2160, worst);

        Console.WriteLine($"         1080p {full:0.0} ms, 4K {uhd:0.0} ms - Fenster ganz offen");

        Check.That(full < 400, "1080p bleibt im Rahmen", $"{full:0.0} ms");
        Check.That(uhd < 1600, "und 4K bleibt benutzbar", $"{uhd:0.0} ms");

        // Und der uebliche Fall ist deutlich billiger: ein schmales Fenster trifft
        // wenige Punkte, und kurze Laeufe sortieren sich schneller als lange.
        double usual = Cost(1920, 1080, new SortTool { Low = 0.6f, High = 0.8f, Longest = 120 });

        Console.WriteLine($"         1080p {usual:0.0} ms - schmales Fenster, Laufgrenze 120");

        Check.That(usual < full,
                   "ein schmales Fenster kostet weniger als ein offenes",
                   $"{usual:0.0} gegen {full:0.0} ms");

        // Schraeg und kreuzweise ist der teuerste neue Fall: zwei Durchgaenge, und die
        // Linien laufen nicht mehr den Speicher entlang.
        double diagonal = Cost(3840, 2160, new SortTool { Low = 0f, High = 1f, Angle = 30f, Cross = true });

        Console.WriteLine($"         4K {diagonal:0.0} ms - 30 Grad, kreuzweise, Fenster ganz offen");

        Check.That(diagonal < 1600, "auch schraeg und kreuzweise bleibt 4K benutzbar",
                   $"{diagonal:0.0} ms");
    }

    /// <summary>Wieviele Punkte es von jeder Farbe gibt - ueber das ganze Bild.</summary>
    private static Dictionary<int, int> Whole(byte[] pixels)
    {
        var count = new Dictionary<int, int>();

        for (int i = 0; i < pixels.Length / 4; i++)
        {
            int at = i * 4;
            int colour = pixels[at] | (pixels[at + 1] << 8) | (pixels[at + 2] << 16);

            count[colour] = count.GetValueOrDefault(colour) + 1;
        }

        return count;
    }

    private static bool Same(Dictionary<int, int> a, Dictionary<int, int> b)
        => a.Count == b.Count && a.All(e => b.GetValueOrDefault(e.Key) == e.Value);

    /// <summary>
    /// Jeder Winkel behaelt jeden Bildpunkt.
    ///
    /// Die Zusage, auf die es beim schraegen Sortieren ankommt. Eine gedrehte Linie
    /// mit gerundeten Koordinaten trifft manche Punkte zweimal und manche nie - und
    /// dann verdoppelt das Sortieren den einen und verliert den anderen. Gezaehlt wird
    /// ueber das GANZE Bild, weil Punkte jetzt zwischen den Zeilen wandern.
    /// </summary>
    private static void EveryAngleKeepsEveryPixel()
    {
        Check.Group("Jeder Winkel behaelt jeden Bildpunkt");

        var before = Noise(3);
        var counted = Whole(before);

        int broken = 0, idle = 0;

        foreach (float angle in new[] { 0f, 17f, 30f, 45f, 60f, 90f, 120f, 135f, 180f, 211f, 270f, 333f })
        {
            var after = Sorted(before, new SortTool { Low = 0f, High = 1f, Angle = angle });

            if (!Same(counted, Whole(after))) broken++;
            if (after.SequenceEqual(before)) idle++;
        }

        Check.That(broken == 0, "bei keinem von zwoelf Winkeln geht ein Punkt verloren oder doppelt",
                   $"{broken} Winkel");
        Check.That(idle == 0, "und bei jedem wird wirklich umsortiert", $"{idle} ohne Wirkung");

        // Und die Punkte wandern ENTLANG der Linie: Ein einzelner heller Punkt in
        // dunklem Grund wandert bei 45 Grad aufsteigend an das Ende seiner Diagonale -
        // und bleibt dabei auf ihr.
        var dot = new byte[Width * Height * 4];

        for (int i = 0; i < Width * Height; i++) dot[i * 4 + 3] = 255;

        int sx = 10, sy = 5;
        int start = (sy * Width + sx) * 4;

        dot[start] = dot[start + 1] = dot[start + 2] = 250;

        var slid = Sorted(dot, new SortTool { Low = 0f, High = 1f, Angle = 45f });

        int ex = -1, ey = -1;

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (slid[(y * Width + x) * 4] == 250) (ex, ey) = (x, y);

        Console.WriteLine($"         45 Grad: heller Punkt von ({sx},{sy}) nach ({ex},{ey})");

        Check.That(ex - ey == sx - sy, "bei 45 Grad bleibt er auf seiner Diagonale",
                   $"{sx - sy} gegen {ex - ey}");
        Check.That(ex > sx && ey > sy, "und wandert nach rechts unten, ans Ende",
                   $"({ex},{ey})");

        // Kreuzweise: erst in der Richtung, dann quer dazu - genau so, als liefe man
        // beide Durchgaenge nacheinander.
        var crossed = Sorted(before, new SortTool { Low = 0f, High = 1f, Angle = 30f, Cross = true });
        var stepwise = Sorted(Sorted(before, new SortTool { Low = 0f, High = 1f, Angle = 30f }),
                              new SortTool { Low = 0f, High = 1f, Angle = 120f, Seed = 7919 });

        Check.That(crossed.SequenceEqual(stepwise),
                   "kreuzweise ist dasselbe wie zwei Durchgaenge nacheinander");
        Check.That(Same(counted, Whole(crossed)), "und behaelt ebenso jeden Punkt");
    }

    /// <summary>
    /// Alte Rezepte sortieren wie vorher.
    ///
    /// Der Schalter "Spalten" ist jetzt ein Winkel von 90 Grad. Ein Rezept, das ihn
    /// noch traegt, muss Byte fuer Byte dasselbe ergeben - sonst sieht jemandes
    /// gespeicherter Look nach dem Update anders aus, ohne dass er etwas getan hat.
    /// </summary>
    private static void OldRecipesSortAsBefore()
    {
        Check.Group("Alte Rezepte sortieren wie vorher");

        var before = Noise(5);

        var legacy = Sorted(before, new SortTool { Low = 0.2f, High = 0.9f, Vertical = true, Longest = 7 });
        var angled = Sorted(before, new SortTool { Low = 0.2f, High = 0.9f, Angle = 90f, Longest = 7 });

        Check.That(legacy.SequenceEqual(angled), "Spalten ist dasselbe wie 90 Grad");

        // Und ohne alles ist es die alte Zeilensortierung: jede Zeile steigt.
        var rows = Sorted(before, new SortTool { Low = 0f, High = 1f });

        bool rising = true;

        for (int y = 0; y < Height; y++)
            for (int x = 1; x < Width; x++)
                if (rows[(y * Width + x) * 4] < rows[(y * Width + x - 1) * 4]) rising = false;

        Check.That(rising, "ohne Winkel wird zeilenweise sortiert wie vorher");
    }

    /// <summary>
    /// Kanten halten den Umriss.
    ///
    /// Links dunkles Rauschen, rechts helles, mit einer harten Grenze dazwischen.
    /// Absteigend sortiert und ohne Kanten wandert das Helle ueber die Grenze nach
    /// links - das Bild schmilzt. Mit Kanten bleibt jede Seite bei sich: Es schmelzen
    /// die Flaechen, nicht das Bild.
    /// </summary>
    private static void EdgesHoldTheOutline()
    {
        Check.Group("Kanten halten den Umriss");

        var halves = new byte[Width * Height * 4];
        int state = 17;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                state = state * 1103515245 + 12345;
                int noise = (state >> 16) & 0x1F;

                byte grey = (byte)(x < Width / 2 ? 10 + noise : 200 + noise);
                int at = (y * Width + x) * 4;

                halves[at] = halves[at + 1] = halves[at + 2] = grey;
                halves[at + 3] = 255;
            }
        }

        int Crossed(byte[] pixels)
        {
            int wrong = 0;

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width / 2; x++)
                    if (pixels[(y * Width + x) * 4] > 128) wrong++;

            return wrong;
        }

        var melted = Sorted(halves, new SortTool { Low = 0f, High = 1f, Descending = true });
        var held = Sorted(halves, new SortTool
        {
            Low = 0f, High = 1f, Descending = true, Interval = SortInterval.Edges, Edge = 0.3f,
        });

        Console.WriteLine($"         helle Punkte links der Grenze: ohne Kanten {Crossed(melted)}, " +
                          $"mit Kanten {Crossed(held)}");

        Check.That(Crossed(melted) > 0, "ohne Kanten wandert das Helle ueber die Grenze");
        Check.That(Crossed(held) == 0, "mit Kanten bleibt jede Seite bei sich", $"{Crossed(held)}");

        // Und innerhalb jeder Seite wird trotzdem sortiert - sonst hielte die Kante
        // nur, weil gar nichts geschieht.
        Check.That(!held.SequenceEqual(halves), "und innerhalb der Seiten wird trotzdem sortiert");
        Check.That(Same(Whole(halves), Whole(held)), "wobei kein Punkt verloren geht");
    }

    /// <summary>
    /// Auslassen, Zufall und Zeit.
    ///
    /// Alles Zufaellige muss sich wie eine Einstellung verhalten: derselbe Startwert,
    /// dasselbe Bild - bei jedem Rechnen, im Export ebenso wie auf dem Schirm.
    /// </summary>
    private static void SkipAndChanceBehave()
    {
        Check.Group("Auslassen, Zufall und Zeit");

        var before = Noise(9);

        var all = new SortTool { Low = 0f, High = 1f, Longest = 10 };
        var none = new SortTool { Low = 0f, High = 1f, Longest = 10, Skip = 1f };
        var some = new SortTool { Low = 0f, High = 1f, Longest = 10, Skip = 0.5f };

        Check.That(Sorted(before, none).SequenceEqual(before), "alles ausgelassen heisst nichts sortiert");

        var partly = Sorted(before, some);
        var fully = Sorted(before, all);

        int untouchedRuns = 0, sortedRuns = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int run = 0; run < Width / 10; run++)
            {
                bool same = true;

                for (int x = run * 10; x < run * 10 + 10; x++)
                    if (partly[(y * Width + x) * 4] != before[(y * Width + x) * 4]) same = false;

                if (same) untouchedRuns++; else sortedRuns++;
            }
        }

        Console.WriteLine($"         Auslassen 0,5: {untouchedRuns} Laeufe stehen, {sortedRuns} sortiert");

        Check.That(untouchedRuns > 0 && sortedRuns > 0,
                   "bei der Haelfte bleibt ein Teil stehen und ein Teil wird sortiert");

        Check.That(!partly.SequenceEqual(fully), "und das ist etwas anderes als alles zu sortieren");

        // Zufallslaengen: derselbe Startwert dasselbe Bild, ein anderer ein anderes.
        var randomA = new SortTool { Low = 0f, High = 1f, Interval = SortInterval.Random, Longest = 12 };
        var randomB = new SortTool { Low = 0f, High = 1f, Interval = SortInterval.Random, Longest = 12, Seed = 4 };

        Check.That(Sorted(before, randomA).SequenceEqual(Sorted(before, randomA)),
                   "Zufallslaengen: derselbe Startwert gibt dasselbe Bild");
        Check.That(!Sorted(before, randomA).SequenceEqual(Sorted(before, randomB)),
                   "ein anderer ein anderes");
        Check.That(Same(Whole(before), Whole(Sorted(before, randomA))),
                   "und kein Punkt geht verloren");

        // Die Laufe streuen wirklich: Bei festen Laengen steigen alle Abschnitte gleich
        // lang, bei Zufallslaengen verschieden. Gemessen an den Bruchstellen einer
        // aufsteigend sortierten Zeile.
        static List<int> Pieces(byte[] pixels, int row)
        {
            var lengths = new List<int>();
            int run = 1;

            for (int x = 1; x < Width; x++)
            {
                if (pixels[(row * Width + x) * 4] >= pixels[(row * Width + x - 1) * 4])
                {
                    run++;
                    continue;
                }

                lengths.Add(run);
                run = 1;
            }

            return lengths;
        }

        var spread = Enumerable.Range(0, Height)
                               .SelectMany(y => Pieces(Sorted(before, randomA), y))
                               .Distinct().Count();

        Console.WriteLine($"         Zufallslaengen: {spread} verschiedene Abschnittslaengen");

        Check.That(spread > 5, "die Laeufe sind verschieden lang", $"{spread}");

        // Zeit: Mit Tempo wechselt das Zufaellige ueber die Sequenz, ohne nicht.
        var moving = new SortTool
        {
            Low = 0f, High = 1f, Interval = SortInterval.Random, Longest = 12, Speed = 1f,
        };

        var still = new SortTool { Low = 0f, High = 1f, Interval = SortInterval.Random, Longest = 12 };

        Check.That(!SortedAt(before, moving, 0).SequenceEqual(SortedAt(before, moving, 1)),
                   "mit Tempo sortiert Bild 1 anders als Bild 0");
        Check.That(SortedAt(before, still, 0).SequenceEqual(SortedAt(before, still, 1)),
                   "ohne Tempo gleich");

        // Die zwei neuen Schluessel sortieren ebenso, ohne etwas zu verlieren.
        foreach (var key in new[] { SortKey.Intensity, SortKey.Minimum })
        {
            var sorted = Sorted(before, new SortTool { Low = 0f, High = 1f, Key = key });

            Check.That(Same(Whole(before), Whole(sorted)) && !sorted.SequenceEqual(before),
                       $"{key} sortiert und behaelt jeden Punkt");
        }
    }

    /// <summary>Wie Sorted, aber mit einer Bildnummer.</summary>
    private static byte[] SortedAt(byte[] pixels, SortTool tool, int number)
    {
        var copy = (byte[])pixels.Clone();

        tool.Prepare();

        var buffer = Marshal.AllocHGlobal(copy.Length);

        try
        {
            Marshal.Copy(copy, 0, buffer, copy.Length);

            tool.Apply(buffer, Width, Height, Width * 4, number);

            Marshal.Copy(buffer, copy, 0, copy.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return copy;
    }

    private static void Persistence()
    {
        Check.Group("Pixel Sorting ueberlebt das Speichern");

        var stack = new GradingStack
        {
            Frame =
            {
                new SortTool
                {
                    Key = SortKey.Saturation, Vertical = true,
                    Low = 0.2f, High = 0.8f, Descending = true, Longest = 120,
                    Interval = SortInterval.Edges, Angle = 33f, Cross = true,
                    Edge = 0.2f, Skip = 0.4f, Seed = 12, Speed = 0.25f,
                },
            },
        };

        var read = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack));
        var tool = read?.Frame.OfType<SortTool>().FirstOrDefault();

        Check.That(tool is not null, "es laesst sich wieder lesen");
        if (tool is null) return;

        Check.That(tool.Key == SortKey.Saturation, "der Schluessel bleibt", tool.Key.ToString());
        Check.That(tool.Vertical, "die Richtung");
        Check.That(tool.Descending, "die Reihenfolge");
        Check.Near(tool.Low, 0.2, 1e-5, "die Untergrenze");
        Check.Near(tool.High, 0.8, 1e-5, "die Obergrenze");
        Check.That(tool.Longest == 120, "die Laengengrenze", $"{tool.Longest}");
        Check.That(tool.Interval == SortInterval.Edges, "die Art der Laeufe");
        Check.Near(tool.Angle, 33, 1e-5, "der Winkel");
        Check.That(tool.Cross, "kreuzweise");
        Check.Near(tool.Edge, 0.2, 1e-5, "die Kantenschwelle");
        Check.Near(tool.Skip, 0.4, 1e-5, "das Auslassen");
        Check.That(tool.Seed == 12, "der Startwert");
        Check.Near(tool.Speed, 0.25, 1e-5, "und das Tempo");

        // Eine Kopie darf sich nicht mitbewegen.
        var copy = stack.Clone();

        copy.Frame.OfType<SortTool>().First().Low = 0.9f;

        Check.Near(stack.Frame.OfType<SortTool>().First().Low, 0.2, 1e-5,
                   "eine Kopie bewegt sich nicht mit");
    }
}
