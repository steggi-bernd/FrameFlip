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
        Check.That(tool.Longest == 120, "und die Laengengrenze", $"{tool.Longest}");

        // Eine Kopie darf sich nicht mitbewegen.
        var copy = stack.Clone();

        copy.Frame.OfType<SortTool>().First().Low = 0.9f;

        Check.Near(stack.Frame.OfType<SortTool>().First().Low, 0.2, 1e-5,
                   "eine Kopie bewegt sich nicht mit");
    }
}
