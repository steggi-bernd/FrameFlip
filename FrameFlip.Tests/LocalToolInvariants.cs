using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Werkzeuge mit oertlicher Wirkung - der zweite Weg durch den Bildprozessor.
///
/// Was hier zaehlt, ist nicht, DASS Klarheit etwas tut, sondern was sie NICHT tut:
/// eine gleichmaessige Flaeche in Ruhe lassen. Genau daran unterscheidet sie sich
/// von einem Kontrastregler, und genau das geht kaputt, wenn die Unschaerfe aus einer
/// anderen Stufe der Kette stammt als der Wert, mit dem sie verglichen wird.
/// </summary>
public static class LocalToolInvariants
{
    public static void Run()
    {
        TheBlurKeepsFlatAreasFlat();
        TheBlurSpreads();
        FlatStaysFlat();
        EdgesGetStronger();
        TheEndsAreSpared();
        NoToolMeansNoBuffer();
        TheGridMatchesTheFullPass();
        Persistence();
        WhatItCosts();
    }

    // ------------------------------------------------------------ die Unschaerfe

    /// <summary>
    /// Eine gleichmaessige Flaeche bleibt nach der Weichzeichnung dieselbe Flaeche.
    ///
    /// Klingt selbstverstaendlich und ist der Pruefstein fuer die Raender: Die
    /// laufende Summe faengt am Rand mit gespiegelten Werten an. Ohne das liefe die
    /// Kante gegen Schwarz, und eine gleichmaessige Flaeche bekaeme rundum einen
    /// dunklen Saum - der dann als oertlicher Kontrast verstaerkt wuerde.
    /// </summary>
    private static void TheBlurKeepsFlatAreasFlat()
    {
        Check.Group("Eine gleichmaessige Flaeche bleibt nach der Unschaerfe gleich");

        const int width = 24, height = 16;

        var values = new float[width * height * 3];
        Array.Fill(values, 0.42f);

        Blur.Apply(values, width, height, 5, new float[values.Length]);

        float worst = 0f;
        foreach (float value in values) worst = MathF.Max(worst, MathF.Abs(value - 0.42f));

        Check.That(worst < 1e-4f, "auch an den Raendern", $"groesste Abweichung {worst:0.#####}");
    }

    private static void TheBlurSpreads()
    {
        Check.Group("Die Unschaerfe verteilt");

        const int width = 41, height = 41;

        var values = new float[width * height * 3];
        int middle = (20 * width + 20) * 3;
        values[middle] = values[middle + 1] = values[middle + 2] = 1f;

        Blur.Apply(values, width, height, 4, new float[values.Length]);

        Check.That(values[middle] < 0.05f, "der Punkt selbst wird schwaecher",
                   $"{values[middle]:0.####}");

        int beside = (20 * width + 22) * 3;
        Check.That(values[beside] > 0f, "und die Nachbarn bekommen etwas ab",
                   $"{values[beside]:0.####}");

        // Nichts geht verloren: Die Summe bleibt, was sie war.
        float sum = 0f;
        for (int i = 0; i < values.Length; i += 3) sum += values[i];

        Check.Near(sum, 1.0, 0.02, "und die Summe bleibt erhalten");

        // Weit weg ist nichts angekommen - der Radius ist ein Radius.
        int far = (2 * width + 2) * 3;
        Check.Near(values[far], 0.0, 1e-5, "weit weg bleibt es dunkel");
    }

    // --------------------------------------------------------------- Klarheit

    /// <summary>
    /// Die Probe, um die es geht.
    ///
    /// Auf einer gleichmaessigen Flaeche ist der Wert gleich der Umgebung, die
    /// Differenz also null - und null mal irgendetwas bleibt null. Kaeme hier etwas
    /// anderes heraus, stammte die Unschaerfe aus einer anderen Stufe der Kette, und
    /// Klarheit waere ein verkappter Helligkeitsregler.
    /// </summary>
    private static void FlatStaysFlat()
    {
        Check.Group("Klarheit laesst eine gleichmaessige Flaeche in Ruhe");

        var frame = Flat(48, 32, 0.25f);

        var strong = Stack(0.9f);
        var none = Stack(0f);

        var withTool = Draw(frame, strong);
        var without = Draw(frame, none);

        Check.That(Same(withTool, without), "voll aufgedreht aendert sich nichts");

        // Und zur Gegenprobe: Kontrast aendert dieselbe Flaeche sehr wohl.
        var contrast = Draw(frame, none, new ImageAdjustments { Contrast = 1.6 });
        Check.That(!Same(without, contrast), "ein Kontrastregler dagegen schon");
    }

    private static void EdgesGetStronger()
    {
        Check.Group("Klarheit verstaerkt eine Kante");

        // Zwei Haelften, beide in den Mitten - dort wirkt Klarheit am staerksten.
        var frame = Halves(64, 32, 0.35f, 0.55f);

        var plain = Draw(frame, Stack(0f));
        var clear = Draw(frame, Stack(0.9f));

        int stride = 64 * 4;

        // Direkt links der Kante wird es dunkler, direkt rechts heller.
        int left = 16 * stride + 30 * 4 + 2;      // Rotkanal
        int right = 16 * stride + 33 * 4 + 2;

        Check.That(clear[left] < plain[left], "links der Kante wird es dunkler",
                   $"{clear[left]} statt {plain[left]}");
        Check.That(clear[right] > plain[right], "rechts heller",
                   $"{clear[right]} statt {plain[right]}");

        // Weit von der Kante entfernt bleibt es, wie es war - der Radius reicht nicht
        // ueber das ganze Bild.
        int far = 16 * stride + 2 * 4 + 2;
        Check.That(Math.Abs(clear[far] - plain[far]) <= 1, "weit weg bleibt es stehen",
                   $"{clear[far]} gegen {plain[far]}");

        // Negativ geht es andersherum: oertlicher Kontrast weg.
        var soft = Draw(frame, Stack(-0.9f));
        Check.That(soft[left] > plain[left], "negativ wird links heller statt dunkler",
                   $"{soft[left]} statt {plain[left]}");
    }

    /// <summary>
    /// An den Enden tut Klarheit nichts.
    ///
    /// Die Gewichtung nach der Umgebungshelligkeit laeuft bei Schwarz und bei Weiss
    /// auf null. Ohne sie brennen die Lichter aus und die Schatten laufen zu - und
    /// zwar genau dort, wo ohnehin kein Platz mehr ist.
    /// </summary>
    private static void TheEndsAreSpared()
    {
        Check.Group("An den Enden laesst Klarheit los");

        var tool = new ClarityTool { Amount = 1f, Reach = 40 };
        tool.Prepare();

        // In den Mitten: volle Wirkung.
        float mid = 0.6f, g1 = 0.6f, b1 = 0.6f;
        tool.Apply(ref mid, ref g1, ref b1, 0.5f, 0.5f, 0.5f);
        Check.That(mid > 0.65f, "in den Mitten wirkt sie voll", $"{mid:0.###}");

        // Ganz oben: nichts mehr.
        float high = 1f, g2 = 1f, b2 = 1f;
        tool.Apply(ref high, ref g2, ref b2, 1f, 1f, 1f);
        Check.Near(high, 1.0, 1e-5, "bei Weiss bleibt sie still");

        float low = 0f, g3 = 0f, b3 = 0f;
        tool.Apply(ref low, ref g3, ref b3, 0f, 0f, 0f);
        Check.Near(low, 0.0, 1e-5, "bei Schwarz auch");

        // Und nichts laeuft ueber den Rand hinaus.
        float over = 0.98f, g4 = 0.98f, b4 = 0.98f;
        tool.Apply(ref over, ref g4, ref b4, 0.5f, 0.5f, 0.5f);
        Check.That(over <= 1f, "und nichts laeuft ueber Weiss hinaus", $"{over:0.####}");
    }

    private static void NoToolMeansNoBuffer()
    {
        Check.Group("Ohne oertliches Werkzeug bleibt der gerade Weg");

        var stack = new GradingStack();
        Check.That(stack.Prepare().Local.Length == 0, "ein leerer Stapel hat keines");

        // Ein Werkzeug in Grundstellung zaehlt nicht mit - sonst zoege eine Klarheit
        // von null jedes Bild durch den Puffer.
        stack.Local.Add(new ClarityTool { Amount = 0f });
        Check.That(stack.Prepare().Local.Length == 0, "eines in Grundstellung auch nicht");
        Check.That(stack.IsNeutral, "und der Stapel gilt als neutral");

        stack.Local[0] = new ClarityTool { Amount = 0.5f };
        Check.That(stack.Prepare().Local.Length == 1, "erst ein aufgedrehtes zaehlt");
        Check.That(!stack.IsNeutral, "und der Stapel nicht mehr");

        // Der Radius, den die Weichzeichnung braucht, ist der groesste aller
        // Werkzeuge - zwei Puffer waeren doppelt so teuer wie einer.
        stack.Local.Add(new ClarityTool { Amount = 0.3f, Reach = 120 });
        Check.That(stack.Prepare().Reach == 120, "der groesste Radius gilt",
                   $"{stack.Prepare().Reach}");
    }

    /// <summary>
    /// Der grobe Durchgang muss dasselbe Gitter meinen wie der volle.
    ///
    /// Beim Reglerzug wird auf einem Gitter gerechnet und dazwischen interpoliert -
    /// auch mit oertlichem Werkzeug. Dieselbe Bedingung wie beim Composer: Rechnet
    /// einer einen Streifen, den ein anderer nie liest, sieht das Bild an einer Kante
    /// falsch aus, und man haelt es fuer die grobe Vorschau.
    /// </summary>
    private static void TheGridMatchesTheFullPass()
    {
        Check.Group("Grob und voll meinen dasselbe Gitter");

        // Ungerade Masse mit Absicht.
        Check.That(LocalPass.Grid(61, 1).Length == 61, "bei Schrittweite eins alle Punkte");
        Check.That(LocalPass.Grid(61, 4)[^1] == 60, "die letzte Stelle liegt auf dem Rand",
                   $"{LocalPass.Grid(61, 4)[^1]}");

        // Der Radius schrumpft mit der Schrittweite - ein verkleinertes Bild mit
        // kleinerem Radius ist dasselbe wie das grosse mit grossem.
        int full = LocalPass.RadiusFor(40, 1920, 1);
        int coarse = LocalPass.RadiusFor(40, 1920, 4);

        Check.That(full == 40, "bei 1080p ist der Radius die eingestellte Zahl", $"{full}");
        Check.That(coarse == 10, "und auf dem Gitter ein Viertel davon", $"{coarse}");

        // Und er waechst mit der Bildgroesse: derselbe Regler, dieselbe Wirkung.
        Check.That(LocalPass.RadiusFor(40, 3840, 1) == 80,
                   "auf 4K ist er doppelt so gross", $"{LocalPass.RadiusFor(40, 3840, 1)}");

        // Nie null - ein Radius von null waere keine Weichzeichnung, und das Werkzeug
        // taete dann gar nichts, ohne dass man den Grund saehe.
        Check.That(LocalPass.RadiusFor(2, 320, 16) >= 1, "und nie null");

        // Grob gezeichnet muss dem vollen nahekommen. Nicht gleich - dazwischen wird
        // interpoliert -, aber ohne Sprung.
        var frame = Halves(64, 48, 0.35f, 0.55f);

        var fine = Draw(frame, Stack(0.8f));
        var rough = Draw(frame, Stack(0.8f), ImageAdjustments.Neutral, step: 4);

        int worst = 0;
        for (int i = 0; i < fine.Length; i++)
            worst = Math.Max(worst, Math.Abs(fine[i] - rough[i]));

        Check.That(worst < 60, "der grobe Durchgang bleibt nahe am vollen", $"{worst} von 255");
    }

    private static void Persistence()
    {
        Check.Group("Klarheit ueberlebt das Speichern");

        var stack = new GradingStack
        {
            Local = { new ClarityTool { Amount = -0.45f, Reach = 77 } },
        };

        var read = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var tool = read.Local.OfType<ClarityTool>().FirstOrDefault();
        Check.That(tool is not null, "mit seinem Typ");
        Check.Near(tool!.Amount, -0.45, 1e-5, "die Staerke bleibt");
        Check.That(tool.Reach == 77, "und der Radius", $"{tool.Reach}");

        // Kopieren muss tief sein - sonst zoege ein Reglerzug waehrend des
        // Stapellaufs die laufende Ausgabe mit.
        var copy = stack.Clone();
        stack.Local.OfType<ClarityTool>().First().Amount = 0.9f;

        Check.Near(copy.Local.OfType<ClarityTool>().First().Amount, -0.45, 1e-5,
                   "eine Kopie bewegt sich nicht mit");
    }

    /// <summary>
    /// Was der zweite Durchgang kostet.
    ///
    /// Er laeuft bei jedem Reglerzug. Waere er teuer, waere die ganze Bauart falsch,
    /// und man merkte es erst beim Bedienen.
    /// </summary>
    private static void WhatItCosts()
    {
        Check.Group("Der oertliche Weg bleibt bedienbar");

        var frame = Halves(1920, 1080, 0.3f, 0.6f);

        var plain = Stack(0f);
        var clear = Stack(0.6f);

        // Warmlaufen.
        Draw(frame, clear);
        Draw(frame, plain);

        double without = Fastest(() => Draw(frame, plain));
        double with = Fastest(() => Draw(frame, clear));
        double coarse = Fastest(() => Draw(frame, clear, ImageAdjustments.Neutral, step: 4));

        Console.WriteLine($"         1080p: ohne {without:0.0} ms, mit Klarheit {with:0.0} ms, " +
                          $"beim Ziehen {coarse:0.0} ms");

        Check.That(with < 400, "der volle Durchgang bleibt im Rahmen", $"{with:0.0} ms");
        Check.That(coarse < 40, "beim Ziehen bleibt es bedienbar", $"{coarse:0.0} ms");
    }

    // ------------------------------------------------------------------- Handwerk

    private static GradingStack Stack(float clarity)
        => new() { Local = { new ClarityTool { Amount = clarity, Reach = 40 } } };

    private static byte[] Draw(FloatFrame frame, GradingStack stack,
                               ImageAdjustments? adjustments = null, int step = 1)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, adjustments ?? ImageAdjustments.Neutral,
                                      new StandardViewTransform(), stack.Prepare(),
                                      buffer, stride, step);

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

    /// <summary>Zwei Haelften mit einer senkrechten Kante in der Mitte.</summary>
    private static FloatFrame Halves(int width, int height, float left, float right)
    {
        int count = width * height;
        var values = new float[count];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                values[y * width + x] = x < width / 2 ? left : right;

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
