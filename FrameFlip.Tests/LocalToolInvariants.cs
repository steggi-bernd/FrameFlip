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
        SharpFlatStaysFlat();
        SharpEdges();
        SharpKeepsTheHue();
        SharpThreshold();
        NoiseTakesGrainAway();
        NoiseKeepsEdges();
        ColourGoesFurther();
        NothingBelowTheThreshold();
        TheOverbrightsAreWhatGlows();
        GlowOnlyAdds();
        HalationIsWarm();
        TheVeilIsFoundInTheDarkChannel();
        WhiteStaysWhiteWhenTheHazeGoes();
        TextureWorksWhereClarityLetsGo();
        TextureGoesBothWays();
        TheSidesAreSplit();
        TheExportTakesTheSameWay();
        TheOrderIsFixed();
        EachRadiusGetsItsOwn();
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

    // ---------------------------------------------------------------- Schaerfe

    /// <summary>
    /// Dieselbe Probe wie bei der Klarheit, aus demselben Grund.
    ///
    /// Sie ist bei der Schaerfe sogar noch wichtiger: Ihr Radius ist klein, und ein
    /// Versatz von einem halben Bildpunkt faellt bei Radius zwei viel eher auf als
    /// bei Radius vierzig.
    /// </summary>
    private static void SharpFlatStaysFlat()
    {
        Check.Group("Schaerfe laesst eine gleichmaessige Flaeche in Ruhe");

        var frame = Flat(48, 32, 0.45f);

        var without = Draw(frame, new GradingStack());
        var sharp = Draw(frame, Sharp(2f, 2, 0f));

        Check.That(Same(without, sharp), "voll aufgedreht und ohne Schwelle aendert sich nichts");
    }

    private static void SharpEdges()
    {
        Check.Group("Schaerfe zieht die Kante zusammen");

        var frame = Halves(64, 32, 0.35f, 0.55f);

        var plain = Draw(frame, new GradingStack());
        var sharp = Draw(frame, Sharp(1.5f, 2, 0f));

        int stride = 64 * 4;

        // Unmittelbar an der Kante: links dunkler, rechts heller. Das ist der
        // Ueberschwinger, den Schaerfe ausmacht.
        int left = 16 * stride + 31 * 4 + 2;
        int right = 16 * stride + 32 * 4 + 2;

        Check.That(sharp[left] < plain[left], "links wird es dunkler",
                   $"{sharp[left]} statt {plain[left]}");
        Check.That(sharp[right] > plain[right], "rechts heller",
                   $"{sharp[right]} statt {plain[right]}");

        // Und nur dort: Fuenf Punkte weiter ist der Radius zu Ende.
        int far = 16 * stride + 20 * 4 + 2;
        Check.That(Math.Abs(sharp[far] - plain[far]) <= 1, "weiter weg bleibt es stehen",
                   $"{sharp[far]} gegen {plain[far]}");
    }

    /// <summary>
    /// Der Farbton bleibt.
    ///
    /// Geschaerft wird auf der Helligkeit, und derselbe Zuschlag geht auf alle drei
    /// Kanaele. Je Kanal gerechnet liefen sie an einer farbigen Kante auseinander,
    /// und um die Kante laege ein bunter Saum - der Fehler, den man an fremden
    /// Bildern sieht, ohne ihn benennen zu koennen.
    /// </summary>
    private static void SharpKeepsTheHue()
    {
        Check.Group("Schaerfe laesst den Farbton, wo er ist");

        var tool = new SharpenTool { Amount = 1.2f, Reach = 2, Threshold = 0f };
        tool.Prepare();

        float r = 0.60f, g = 0.40f, b = 0.30f;
        float beforeRg = r - g, beforeGb = g - b;

        // Die Umgebung ist dieselbe Farbe, nur dunkler - eine reine Helligkeitskante.
        tool.Apply(ref r, ref g, ref b, 0.50f, 0.30f, 0.20f);

        Check.Near(r - g, beforeRg, 1e-5, "der Abstand von Rot zu Gruen bleibt");
        Check.Near(g - b, beforeGb, 1e-5, "der von Gruen zu Blau auch");
        Check.That(r > 0.60f, "und heller ist es geworden", $"{r:0.###}");
    }

    private static void SharpThreshold()
    {
        Check.Group("Die Schwelle laesst feines Rauschen liegen");

        var tool = new SharpenTool { Amount = 1f, Reach = 2, Threshold = 0.05f };
        tool.Prepare();

        // Ein kleiner Unterschied - so gross wie Korn.
        float small = 0.505f, sg = 0.505f, sb = 0.505f;
        tool.Apply(ref small, ref sg, ref sb, 0.5f, 0.5f, 0.5f);
        Check.That(small - 0.505f < 0.002f, "ein Unterschied unter der Schwelle bleibt fast liegen",
                   $"{(small - 0.505f):0.#####}");

        // Ein grosser - eine Kante.
        float large = 0.7f, lg = 0.7f, lb = 0.7f;
        tool.Apply(ref large, ref lg, ref lb, 0.5f, 0.5f, 0.5f);
        Check.That(large > 0.85f, "eine Kante darueber wird voll zugeschlagen", $"{large:0.###}");

        // Ohne Schwelle wirkt auch das Kleine voll - sonst waere die Schwelle nicht
        // der Grund, sondern etwas anderes.
        var open = new SharpenTool { Amount = 1f, Reach = 2, Threshold = 0f };
        open.Prepare();

        float free = 0.505f, fg = 0.505f, fb = 0.505f;
        open.Apply(ref free, ref fg, ref fb, 0.5f, 0.5f, 0.5f);
        Check.Near(free, 0.51, 1e-4, "ohne Schwelle wird auch das Kleine verdoppelt");
    }

    // -------------------------------------------------------- Rauschminderung

    private static void NoiseTakesGrainAway()
    {
        Check.Group("Rauschminderung nimmt Korn weg");

        var frame = Grain(96, 96, 0.45f, 0.03f, colour: true, seed: 7);

        var plain = Draw(frame, new GradingStack());
        var cleaned = Draw(frame, Denoise(1f, 1f, 0.04f));

        double before = Deviation(plain);
        double after = Deviation(cleaned);

        Check.That(after < before * 0.7, "die Streuung faellt deutlich",
                   $"{before:0.0} auf {after:0.0}");

        // Die Helligkeit bleibt dabei, wo sie war - geglaettet ist nicht abgedunkelt.
        Check.Near(Average(cleaned), Average(plain), 1.5, "und das Bild bleibt gleich hell");
    }

    /// <summary>
    /// Die Kante ueberlebt.
    ///
    /// Daran haengt der Unterschied zwischen Rauschminderung und Weichzeichnen:
    /// Glaetten, wo nichts ist, und stehenlassen, wo etwas ist. Eine Weichzeichnung
    /// nimmt beides mit, und dafuer braeuchte es kein eigenes Werkzeug.
    /// </summary>
    private static void NoiseKeepsEdges()
    {
        Check.Group("Die Kante ueberlebt die Rauschminderung");

        var frame = Halves(64, 32, 0.30f, 0.60f);

        var plain = Draw(frame, new GradingStack());
        var cleaned = Draw(frame, Denoise(1f, 1f, 0.04f));

        int stride = 64 * 4;
        int left = 16 * stride + 31 * 4 + 2;
        int right = 16 * stride + 32 * 4 + 2;

        int before = plain[right] - plain[left];
        int after = cleaned[right] - cleaned[left];

        Check.That(after > before * 0.85, "der Sprung bleibt fast ganz stehen",
                   $"{after} von {before}");
    }

    /// <summary>
    /// Farbe wird haerter angefasst als Helligkeit.
    ///
    /// Bei gleichem Unterschied muss von der Farbe mehr verschwinden. Der Grund
    /// steht am Werkzeug: Farbrauschen ist das haessliche, und feine Farbzeichnung
    /// ist selten - beides zeigt in dieselbe Richtung.
    /// </summary>
    private static void ColourGoesFurther()
    {
        Check.Group("Farbe wird staerker geglaettet als Helligkeit");

        var tool = new NoiseTool { Luminance = 1f, Colour = 1f, Threshold = 0.02f, Reach = 2 };
        tool.Prepare();

        // Ein reiner Helligkeitsunterschied: alle drei Kanaele gleich weit weg.
        float lr = 0.53f, lg = 0.53f, lb = 0.53f;
        tool.Apply(ref lr, ref lg, ref lb, 0.5f, 0.5f, 0.5f);
        double lumaLeft = Math.Abs(lr - 0.5f) / 0.03f;

        // Ein reiner Farbunterschied: derselbe Betrag, aber gegenlaeufig verteilt,
        // sodass die Helligkeit gleich bleibt.
        float cr = 0.53f, cg = 0.50f, cb = 0.47f;
        tool.Apply(ref cr, ref cg, ref cb, 0.5f, 0.5f, 0.5f);
        double colourLeft = Math.Abs(cr - 0.5f) / 0.03f;

        Check.That(colourLeft < lumaLeft, "vom Farbunterschied bleibt weniger uebrig",
                   $"{colourLeft:0.###} gegen {lumaLeft:0.###}");
        Check.That(colourLeft < 0.25, "und zwar deutlich weniger", $"{colourLeft:0.###}");
    }

    // ------------------------------------------------------ Glanz und Halation

    /// <summary>
    /// Unter der Schwelle geschieht nichts. Das ist das ganze Werkzeug.
    ///
    /// Ohne Schwelle waere Glanz das Bild, weichgezeichnet und dazugezaehlt - ein
    /// Schleier ueber allem. Genau so sehen Nachbearbeitungen aus, die "weich"
    /// aussehen sollen und stattdessen milchig sind.
    /// </summary>
    private static void NothingBelowTheThreshold()
    {
        Check.Group("Unter der Schwelle leuchtet nichts");

        var frame = Halves(64, 48, 0.3f, 0.8f);

        var plain = Draw(frame, new GradingStack());
        var glow = Draw(frame, Glow(2f, 1f, 150));

        Check.That(Same(plain, glow), "ein Bild ganz unter der Schwelle bleibt, wie es ist");

        // Und darueber sehr wohl.
        var bright = Halves(64, 48, 0.3f, 4f);

        var plainBright = Draw(bright, new GradingStack());
        var glowBright = Draw(bright, Glow(2f, 1f, 150));

        Check.That(!Same(plainBright, glowBright), "mit einem Ueberhellen darin nicht mehr");
    }

    /// <summary>
    /// Die Probe, um die es bei diesem Werkzeug geht.
    ///
    /// Eine Lampe und die Sonne sind auf der Anzeige beide weiss. Wenn der Glanz
    /// hinter der Sichtumwandlung rechnete, waeren sie auch im Glanz gleich - und
    /// das Werkzeug waere auf einem EXR keinen Deut besser als auf einem JPEG.
    /// Zwei Bilder, die als Byte identisch sind und verschieden leuchten, sind der
    /// Beweis, dass er davor rechnet.
    /// </summary>
    private static void TheOverbrightsAreWhatGlows()
    {
        Check.Group("Was ueberhell ist, leuchtet - und man sieht es nur davor");

        var lamp = Spot(128, 128, 0f, 1f, 8);
        var sun = Spot(128, 128, 0f, 8f, 8);

        var plainLamp = Draw(lamp, new GradingStack());
        var plainSun = Draw(sun, new GradingStack());

        Check.That(Same(plainLamp, plainSun), "ohne Glanz sind beide Bilder dasselbe Byte fuer Byte");

        var glowLamp = Draw(lamp, Glow(1f, 0.9f, 150));
        var glowSun = Draw(sun, Glow(1f, 0.9f, 150));

        Check.That(!Same(glowLamp, glowSun), "mit Glanz nicht mehr");

        // Zwoelf Punkte neben dem Fleck - draussen, aber im Umkreis.
        int at = (64 * 128 + 76) * 4 + 2;

        Check.That(glowSun[at] > glowLamp[at] + 20, "die Sonne leuchtet weiter als die Lampe",
                   $"{glowSun[at]} gegen {glowLamp[at]}");

        Check.That(glowLamp[at] > plainLamp[at], "und auch die Lampe leuchtet ueberhaupt",
                   $"{glowLamp[at]} statt {plainLamp[at]}");
    }

    private static void GlowOnlyAdds()
    {
        Check.Group("Glanz zaehlt dazu und nimmt nichts weg");

        var frame = Spot(128, 128, 0.2f, 6f, 8);

        var plain = Draw(frame, new GradingStack());
        var glow = Draw(frame, Glow(1f, 1f, 150));

        int darker = 0;

        for (int i = 0; i < plain.Length; i++)
        {
            if (i % 4 == 3) continue;
            if (glow[i] < plain[i]) darker++;
        }

        Check.That(darker == 0, "kein Bildpunkt wird dunkler", $"{darker} waeren es");
    }

    private static void HalationIsWarm()
    {
        Check.Group("Halation ist warm");

        var frame = Spot(128, 128, 0f, 6f, 8);
        var drawn = Draw(frame, Halo(1f, 0.9f, 150, tint: 0.35f));

        // Neben dem Fleck, im Saum.
        int at = (64 * 128 + 76) * 4;

        int blue = drawn[at], green = drawn[at + 1], red = drawn[at + 2];

        Check.That(red > green && green > blue, "der Saum ist rot ueber gruen ueber blau",
                   $"r {red}, g {green}, b {blue}");
        Check.That(red > 20, "und er ist ueberhaupt da", $"{red}");

        // Die Faerbung verschiebt sich, aber sie bleibt warm.
        var deep = new HalationTool { Amount = 1f, Threshold = 1f, Tint = 0f };
        var orange = new HalationTool { Amount = 1f, Threshold = 1f, Tint = 1f };

        deep.Prepare();
        orange.Prepare();

        float dr = 0f, dg = 0f, db = 0f;
        deep.Apply(ref dr, ref dg, ref db, 1f, 1f, 1f);

        float orr = 0f, og = 0f, ob = 0f;
        orange.Apply(ref orr, ref og, ref ob, 1f, 1f, 1f);

        Check.That(og > dg, "gegen eins wird es oranger", $"{og:0.###} gegen {dg:0.###}");
        Check.That(dr > dg && orr > og, "und rot bleibt in beiden Faellen vorn");
    }

    /// <summary>
    /// Die beiden Seiten kommen getrennt heraus.
    ///
    /// Der Bildprozessor rechnet sie an zwei verschiedenen Stellen - eine Liste
    /// fuer beide hiesse, die Verzweigung in die innere Schleife zu tragen, und bei
    /// 4K sind das 25 Millionen Mal.
    /// </summary>
    private static void TheSidesAreSplit()
    {
        Check.Group("Licht- und Anzeigeseite stehen getrennt");

        var stack = new GradingStack
        {
            Local =
            {
                new SharpenTool { Amount = 1f },
                new BloomTool { Amount = 0.5f },
                new ClarityTool { Amount = 0.5f },
                new HalationTool { Amount = 0.5f },
                new TextureTool { Amount = 0.5f },
                new NoiseTool { Colour = 0.5f },
                new DehazeTool { Amount = 0.5f },
            },
        };

        var prepared = stack.Prepare();

        Check.That(prepared.LocalLight.Length == 3, "drei auf der Lichtseite",
                   $"{prepared.LocalLight.Length}");
        Check.That(prepared.LocalLight[0] is DehazeTool,
                   "der Dunst zuerst - ein Schleier, der erst leuchtet, leuchtet immer noch");
        Check.That(prepared.LocalLight.Skip(1).All(tool => tool is BloomTool or HalationTool),
                   "dahinter Glanz und Halation");

        Check.That(prepared.Local.Length == 4, "vier auf der Anzeigeseite", $"{prepared.Local.Length}");
        Check.That(prepared.Local[0] is NoiseTool && prepared.Local[1] is ClarityTool &&
                   prepared.Local[2] is TextureTool && prepared.Local[3] is SharpenTool,
                   "dort in ihrer festen Reihenfolge");

        Check.That(prepared.HasLocal, "und der Stapel weiss, dass er den zweiten Weg braucht");
        Check.That(!PreparedGrading.None.HasLocal, "ein leerer weiss das Gegenteil");

        // Ein Lichtwerkzeug allein zaehlt auch.
        var onlyLight = new GradingStack { Local = { new BloomTool { Amount = 0.5f } } };

        Check.That(onlyLight.Prepare().HasLocal, "ein Glanz allein reicht dafuer");
        Check.That(onlyLight.Prepare().Local.Length == 0, "und die Anzeigeseite bleibt leer");
    }

    // ------------------------------------------------------- Dunst und Textur

    /// <summary>
    /// Der Schleier wird ueber den dunklen Kanal geschaetzt.
    ///
    /// Die Annahme: An fast jeder Stelle eines Bildes liegt einer der drei Kanaele
    /// nahe null. Wo auch das Dunkelste noch hell ist, liegt Dunst darueber. Der
    /// Auszug muss deshalb das Kleinste der drei nehmen - naehme er die Helligkeit,
    /// haette eine saftig rote Flaeche denselben Schleier wie eine milchige.
    /// </summary>
    private static void TheVeilIsFoundInTheDarkChannel()
    {
        Check.Group("Der Dunst sucht im dunklen Kanal");

        var tool = new DehazeTool { Amount = 1f, Reach = 80 };
        tool.Prepare();

        // Eine kraeftige Farbe: Ein Kanal liegt tief, also ist dort kein Schleier.
        float r = 0.9f, g = 0.2f, b = 0.05f;
        tool.Extract(ref r, ref g, ref b);

        Check.Near(r, 0.05, 1e-5, "der Auszug nimmt das Kleinste der drei");
        Check.That(r == g && g == b, "und setzt alle drei gleich - der Schleier ist eine Menge");

        // Eine milchige Flaeche: Auch das Dunkelste ist hell.
        float mr = 0.7f, mg = 0.75f, mb = 0.8f;
        tool.Extract(ref mr, ref mg, ref mb);

        Check.Near(mr, 0.7, 1e-5, "bei einer milchigen Flaeche steht er hoch");

        // Und im Bild: Ein Feld, das nur aus Schleier besteht, hat danach nichts mehr.
        var veil = Flat(64, 48, 0.25f);

        var plain = Draw(veil, new GradingStack());
        var cleared = Draw(veil, Haze(1f, 200));

        Check.That(plain[2] > 100, "das Feld ist vorher grau", $"{plain[2]}");
        Check.That(cleared[2] < 8, "und danach schwarz - es war nichts darin als Dunst",
                   $"{cleared[2]}");
    }

    /// <summary>
    /// Weiss bleibt Weiss. Das ist die Probe auf die Rechnung.
    ///
    /// Abgezogen wird der Schleier, und dann wird durch das geteilt, was uebrig
    /// bleibt - denn was durch den Dunst kam, kam geschwaecht an. Wer nur abzieht,
    /// macht das Bild dunkler statt klarer, und das Weiss waere hinterher grau.
    /// </summary>
    private static void WhiteStaysWhiteWhenTheHazeGoes()
    {
        Check.Group("Weiss bleibt weiss, und der Kontrast kommt zurueck");

        var tool = new DehazeTool { Amount = 1f };
        tool.Prepare();

        float wr = 1f, wg = 1f, wb = 1f;
        tool.Apply(ref wr, ref wg, ref wb, 0.2f, 0.2f, 0.2f);

        Check.Near(wr, 1.0, 1e-5, "eine weisse Flaeche bleibt weiss");

        // Zwei Werte mit demselben Schleier: Ihr Abstand muss groesser werden.
        float lowR = 0.4f, lowG = 0.4f, lowB = 0.4f;
        tool.Apply(ref lowR, ref lowG, ref lowB, 0.2f, 0.2f, 0.2f);

        float highR = 0.6f, highG = 0.6f, highB = 0.6f;
        tool.Apply(ref highR, ref highG, ref highB, 0.2f, 0.2f, 0.2f);

        Check.Near(lowR, 0.25, 1e-5, "der dunklere faellt");
        Check.Near(highR, 0.5, 1e-5, "der hellere auch, aber weniger");
        Check.That(highR - lowR > 0.2f, "und der Abstand zwischen ihnen waechst",
                   $"{(highR - lowR):0.###} statt 0,2");
    }

    /// <summary>
    /// Die Begruendung dafuer, dass Textur ein eigenes Werkzeug ist.
    ///
    /// Im Entwurf stand, sie sei die Klarheit mit kleinem Radius. Das stimmt in der
    /// Mitte und wird an den Enden falsch: Die Gewichtung der Klarheit laeuft bei
    /// Schwarz und Weiss auf null, und genau dort liegen helle Haut und dunkler
    /// Stoff - die Flaechen, um die es bei Textur ueberhaupt geht.
    /// </summary>
    private static void TextureWorksWhereClarityLetsGo()
    {
        Check.Group("Textur wirkt dort, wo die Klarheit loslaesst");

        var clarity = new ClarityTool { Amount = 1f, Reach = 8 };
        var texture = new TextureTool { Amount = 1f, Reach = 8 };

        clarity.Prepare();
        texture.Prepare();

        // Hoch oben, wo eine helle Flaeche liegt: eine dunkle Stelle darin, damit
        // die Begrenzung bei Weiss die Messung nicht abschneidet.
        float cr = 0.95f, cg = 0.95f, cb = 0.95f;
        clarity.Apply(ref cr, ref cg, ref cb, 0.97f, 0.97f, 0.97f);

        float tr = 0.95f, tg = 0.95f, tb = 0.95f;
        texture.Apply(ref tr, ref tg, ref tb, 0.97f, 0.97f, 0.97f);

        double fromClarity = Math.Abs(cr - 0.95);
        double fromTexture = Math.Abs(tr - 0.95);

        Check.That(fromClarity < 0.005, "die Klarheit tut dort fast nichts",
                   $"{fromClarity:0.####}");
        Check.That(fromTexture > 0.015, "die Textur sehr wohl", $"{fromTexture:0.####}");
        Check.That(fromTexture > fromClarity * 5, "um ein Vielfaches",
                   $"{fromTexture:0.####} gegen {fromClarity:0.####}");

        // In der Mitte sind sie sich einig - dort ist die Klarheit voll da.
        float mr = 0.55f, mg = 0.55f, mb = 0.55f;
        clarity.Apply(ref mr, ref mg, ref mb, 0.5f, 0.5f, 0.5f);

        float nr = 0.55f, ng = 0.55f, nb = 0.55f;
        texture.Apply(ref nr, ref ng, ref nb, 0.5f, 0.5f, 0.5f);

        Check.That(Math.Abs(mr - nr) < 0.02f, "in den Mitten liegen sie nah beieinander",
                   $"{mr:0.###} gegen {nr:0.###}");
    }

    private static void TextureGoesBothWays()
    {
        Check.Group("Textur geht in beide Richtungen");

        var frame = Flat(48, 32, 0.5f);

        var plain = Draw(frame, new GradingStack());
        var strong = Draw(frame, Texture(1f, 8));

        Check.That(Same(plain, strong), "eine gleichmaessige Flaeche bleibt in Ruhe");

        var tool = new TextureTool { Amount = -1f, Reach = 8 };
        tool.Prepare();

        // Negativ wandert der Wert auf die Umgebung zu.
        float r = 0.6f, g = 0.6f, b = 0.6f;
        tool.Apply(ref r, ref g, ref b, 0.5f, 0.5f, 0.5f);

        Check.Near(r, 0.5, 1e-5, "negativ glaettet sie bis auf die Umgebung");

        // Und der Farbton bleibt, wie bei der Schaerfe - gerechnet wird auf der
        // Helligkeit.
        var up = new TextureTool { Amount = 0.8f, Reach = 8 };
        up.Prepare();

        float ur = 0.6f, ug = 0.45f, ub = 0.3f;
        float beforeRg = ur - ug;

        up.Apply(ref ur, ref ug, ref ub, 0.5f, 0.35f, 0.2f);

        Check.Near(ur - ug, beforeRg, 1e-5, "der Abstand der Kanaele bleibt");
    }

    /// <summary>
    /// Der Sechzehn-Bit-Ausgang muss dasselbe zeigen wie die Vorschau.
    ///
    /// Er hat seinen eigenen ersten Durchgang - ein Bild ohne Gitter, Deckung als
    /// Byte daneben -, und genau dort koennten die beiden Wege auseinanderlaufen.
    /// Faende man das nicht hier, faende man es am fertigen Film: Der Export saehe
    /// anders aus als das, was beim Einstellen auf dem Schirm stand.
    /// </summary>
    private static void TheExportTakesTheSameWay()
    {
        Check.Group("Der Export nimmt denselben Weg");

        var frame = Spot(128, 128, 0.15f, 6f, 8);

        var preview = Draw(frame, Glow(1f, 0.9f, 150));
        var exported = Draw16(frame, Glow(1f, 0.9f, 150));

        int worst = 0;

        for (int i = 0; i < preview.Length; i += 4)
        {
            // Bgra gegen Rgba: der rote Kanal liegt verschieden.
            int fromPreview = preview[i + 2];
            int fromExport = exported[i / 4 * 4] >> 8;

            worst = Math.Max(worst, Math.Abs(fromPreview - fromExport));
        }

        Check.That(worst <= 1, "Vorschau und Export stimmen bis auf die Rundung ueberein",
                   $"groesster Unterschied {worst} von 255");

        // Und unter der Schwelle geschieht auch dort nichts.
        var flat = Halves(64, 48, 0.2f, 0.7f);

        var plain = Draw16(flat, new GradingStack());
        var glow = Draw16(flat, Glow(2f, 1f, 150));

        Check.That(Same16(plain, glow), "unter der Schwelle bleibt auch der Export unberuehrt");
    }

    // ------------------------------------------------- Reihenfolge und Gruppen

    /// <summary>
    /// Die Reihenfolge steht am Werkzeug, nicht in der Liste.
    ///
    /// Sonst entschiede darueber, in welcher Reihenfolge jemand an den Reglern war -
    /// und zwei gleich eingestellte Rezepte saehen verschieden aus, ohne dass man
    /// den Unterschied irgendwo ablesen koennte.
    /// </summary>
    private static void TheOrderIsFixed()
    {
        Check.Group("Die Reihenfolge der oertlichen Werkzeuge steht fest");

        var stack = new GradingStack
        {
            Local =
            {
                new SharpenTool { Amount = 1f },
                new ClarityTool { Amount = 0.5f },
                new NoiseTool { Luminance = 0.5f },
            },
        };

        var order = stack.Prepare().Local;

        Check.That(order.Length == 3, "alle drei sind dabei", $"{order.Length}");
        Check.That(order[0] is NoiseTool, "erst die Rauschminderung");
        Check.That(order[1] is ClarityTool, "dann die Klarheit");
        Check.That(order[2] is SharpenTool, "und zuletzt die Schaerfe");
    }

    /// <summary>
    /// Jeder Radius bekommt seine eigene Weichzeichnung - und sie steht auf dem
    /// Stand, den die vorigen Werkzeuge hinterlassen haben.
    ///
    /// Das ist der Grund, warum Rauschminderung und Schaerfe zusammen etwas anderes
    /// ergeben als jede fuer sich: Die Schaerfe sieht das aufgeraeumte Bild, und das
    /// weggenommene Korn kommt nicht zurueck. Teilten sich beide eine Weichzeichnung
    /// vom Anfang, holte die Schaerfe genau das wieder hoch, was die
    /// Rauschminderung gerade entfernt hat.
    /// </summary>
    private static void EachRadiusGetsItsOwn()
    {
        Check.Group("Jeder Radius bekommt seine eigene Unschaerfe");

        const int width = 32, height = 24;

        // Zwei Sonden: Die erste hebt alles um einen festen Betrag, die zweite
        // schreibt nur auf, was sie zu sehen bekommt.
        var first = new Probe(radius: 12, lift: 0.1f);
        var second = new Probe(radius: 3, lift: 0f);

        var scratch = new LocalPass.Scratch();
        scratch.Hold(width * height);

        for (int i = 0; i < width * height * 3; i++) scratch.Values[i] = 0.4f;

        LocalPass.Run(scratch, new ILocalTool[] { first, second }, width, height, 1920, step: 1);

        Check.Near(first.Seen, 0.4, 1e-4, "die erste sieht den Stand von vorher");
        Check.Near(second.Seen, 0.5, 1e-4, "die zweite den, den die erste hinterlassen hat");

        // Gleicher Radius heisst: eine Weichzeichnung fuer beide. Dann sieht die
        // zweite denselben Stand wie die erste, obwohl die erste dazwischen etwas
        // geaendert hat.
        var one = new Probe(radius: 5, lift: 0.1f);
        var two = new Probe(radius: 5, lift: 0f);

        for (int i = 0; i < width * height * 3; i++) scratch.Values[i] = 0.4f;

        LocalPass.Run(scratch, new ILocalTool[] { one, two }, width, height, 1920, step: 1);

        Check.Near(one.Seen, 0.4, 1e-4, "bei gleichem Radius sehen beide dasselbe");
        Check.Near(two.Seen, 0.4, 1e-4, "eine Weichzeichnung fuer die Gruppe");

        // Ein Lichtwerkzeug bleibt trotz gleichen Radius fuer sich: Sein Eingang ist
        // nicht der Wert, sondern das, was es daraus zieht. Kaeme es in dieselbe
        // Gruppe, bekaeme das andere Werkzeug die Lichter statt des Bildes.
        var extracting = new HighlightProbe(radius: 5);
        var plain = new Probe(radius: 5, lift: 0f);

        for (int i = 0; i < width * height * 3; i++) scratch.Values[i] = 0.4f;

        LocalPass.Run(scratch, new ILocalTool[] { extracting, plain }, width, height, 1920, step: 1);

        Check.Near(extracting.Seen, 0.1, 1e-4, "das Lichtwerkzeug sieht seinen eigenen Auszug");
        Check.Near(plain.Seen, 0.4, 1e-4, "das andere weiterhin den Wert");
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

        // Auch eine Rauschminderung in Grundstellung zaehlt nicht mit - sie hat zwei
        // Regler, und beide muessen unten stehen.
        stack.Local.Add(new NoiseTool { Luminance = 0f, Colour = 0f });
        Check.That(stack.Prepare().Local.Length == 1, "eine stumme Rauschminderung zaehlt nicht");

        stack.Local[^1] = new NoiseTool { Colour = 0.4f };
        Check.That(stack.Prepare().Local.Length == 2, "eine aufgedrehte schon");

        // Jedes Werkzeug behaelt seinen eigenen Radius. Frueher galt hier der groesste
        // fuer alle - das war richtig, solange die Klarheit allein hier stand, und
        // machte aus der Schaerfe eine zweite Klarheit, sobald sie dazukam.
        stack.Local.Add(new SharpenTool { Amount = 0.8f, Reach = 2 });

        var radii = stack.Prepare().Local.Select(tool => tool.Radius).ToArray();
        Check.That(radii.Length == 3, "drei Werkzeuge, drei Eintraege", $"{radii.Length}");
        Check.That(radii[0] == 2 && radii[1] == 40 && radii[2] == 2,
                   "jedes mit seinem eigenen Radius", string.Join(", ", radii));
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
            Local =
            {
                new ClarityTool { Amount = -0.45f, Reach = 77 },
                new BloomTool { Amount = 0.8f, Threshold = 2.5f, Reach = 90 },
                new HalationTool { Amount = 0.3f, Threshold = 1.5f, Reach = 9, Tint = 0.7f },
                new DehazeTool { Amount = 0.6f, Reach = 120 },
                new TextureTool { Amount = -0.7f, Reach = 14 },
            },
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

        // Die Lichtwerkzeuge genauso - jedes Werkzeug ohne Kopierweg wuerde geteilt
        // statt kopiert, und der Fehler faellt erst auf, wenn eine festgehaltene
        // Einstellung sich mitbewegt.
        var glow = read.Local.OfType<BloomTool>().FirstOrDefault();
        Check.That(glow is not null, "der Glanz ueberlebt auch");
        if (glow is not null)
        {
            Check.Near(glow.Threshold, 2.5, 1e-5, "mit seiner Schwelle");
            Check.That(glow.Reach == 90, "und seinem Radius", $"{glow.Reach}");
        }

        var halo = read.Local.OfType<HalationTool>().FirstOrDefault();
        Check.That(halo is not null, "die Halation ebenso");
        if (halo is not null) Check.Near(halo.Tint, 0.7, 1e-5, "mitsamt Faerbung");

        var haze = read.Local.OfType<DehazeTool>().FirstOrDefault();
        Check.That(haze is not null, "der Dunst auch");
        if (haze is not null) Check.That(haze.Reach == 120, "mit seinem Radius", $"{haze.Reach}");

        var texture = read.Local.OfType<TextureTool>().FirstOrDefault();
        Check.That(texture is not null, "und die Textur");
        if (texture is not null) Check.Near(texture.Amount, -0.7, 1e-5, "mitsamt Vorzeichen");

        var copiedGlow = copy.Local.OfType<BloomTool>().First();
        stack.Local.OfType<BloomTool>().First().Amount = 0.1f;

        Check.Near(copiedGlow.Amount, 0.8, 1e-5, "und auch dort bewegt die Kopie sich nicht mit");
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

        // Alle drei zusammen sind der teuerste Fall: drei Radien, drei
        // Weichzeichnungen.
        var everything = new GradingStack
        {
            Local =
            {
                new NoiseTool { Luminance = 0.5f, Colour = 0.5f, Reach = 2 },
                new ClarityTool { Amount = 0.6f, Reach = 40 },
                new SharpenTool { Amount = 0.8f, Reach = 4 },
                new BloomTool { Amount = 0.5f, Reach = 60 },
                new DehazeTool { Amount = 0.4f, Reach = 80 },
                new TextureTool { Amount = 0.4f, Reach = 8 },
            },
        };

        Draw(frame, everything);

        double without = Fastest(() => Draw(frame, plain));
        double with = Fastest(() => Draw(frame, clear));
        double all = Fastest(() => Draw(frame, everything));
        double coarse = Fastest(() => Draw(frame, clear, ImageAdjustments.Neutral, step: 4));
        double coarseAll = Fastest(() => Draw(frame, everything, ImageAdjustments.Neutral, step: 4));

        Console.WriteLine($"         1080p: ohne {without:0.0} ms, mit Klarheit {with:0.0} ms, " +
                          $"alle sechs ueber beide Seiten {all:0.0} ms");
        Console.WriteLine($"         beim Ziehen: Klarheit {coarse:0.0} ms, alle sechs {coarseAll:0.0} ms");

        Check.Timing(with < 400, "der volle Durchgang bleibt im Rahmen", $"{with:0.0} ms");
        Check.Timing(coarse < 40, "beim Ziehen bleibt es bedienbar", $"{coarse:0.0} ms");
        Check.Timing(coarseAll < 80, "auch mit allen sechsen", $"{coarseAll:0.0} ms");
    }

    // ------------------------------------------------------------------- Handwerk

    private static GradingStack Stack(float clarity)
        => new() { Local = { new ClarityTool { Amount = clarity, Reach = 40 } } };

    private static GradingStack Sharp(float amount, int reach, float threshold)
        => new() { Local = { new SharpenTool { Amount = amount, Reach = reach, Threshold = threshold } } };

    private static GradingStack Haze(float amount, int reach)
        => new() { Local = { new DehazeTool { Amount = amount, Reach = reach } } };

    private static GradingStack Texture(float amount, int reach)
        => new() { Local = { new TextureTool { Amount = amount, Reach = reach } } };

    private static GradingStack Glow(float amount, float threshold, int reach)
        => new() { Local = { new BloomTool { Amount = amount, Threshold = threshold, Reach = reach } } };

    private static GradingStack Halo(float amount, float threshold, int reach, float tint)
        => new()
        {
            Local =
            {
                new HalationTool { Amount = amount, Threshold = threshold, Reach = reach, Tint = tint },
            },
        };

    private static GradingStack Denoise(float luminance, float colour, float threshold)
        => new()
        {
            Local = { new NoiseTool { Luminance = luminance, Colour = colour, Threshold = threshold, Reach = 2 } },
        };

    /// <summary>
    /// Ein Werkzeug, das nur aufschreibt, was es zu sehen bekommt - und wahlweise
    /// den Wert anhebt, damit sich nachweisen laesst, auf welchem Stand die naechste
    /// Weichzeichnung ansetzt.
    /// </summary>
    private sealed class Probe : ILocalTool
    {
        private readonly int _radius;
        private readonly float _lift;

        public Probe(int radius, float lift)
        {
            _radius = radius;
            _lift = lift;
        }

        /// <summary>Die Umgebung, die das Werkzeug gesehen hat.</summary>
        public float Seen { get; private set; }

        public string Kind => "probe";

        public LocalStage Stage => LocalStage.Contrast;

        public bool IsNeutral => false;

        public int Radius => _radius;

        public void Prepare()
        {
        }

        public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
        {
            Seen = br;

            r += _lift;
            g += _lift;
            b += _lift;
        }
    }

    /// <summary>
    /// Ein Werkzeug, das seinen eigenen Auszug bekommt - hier schlicht ein Viertel
    /// des Werts, damit sich nachweisen laesst, dass die Weichzeichnung davon
    /// ausgeht und nicht vom Wert.
    ///
    /// Seine Stufe ist Contrast und nicht Light, weil der Test den Durchgang direkt
    /// ruft; die Stufe entscheidet erst im Stapel ueber die Seite.
    /// </summary>
    private sealed class HighlightProbe : IExtractTool
    {
        private readonly int _radius;

        public HighlightProbe(int radius) => _radius = radius;

        public float Seen { get; private set; }

        public string Kind => "highlight-probe";

        public LocalStage Stage => LocalStage.Contrast;

        public bool IsNeutral => false;

        public int Radius => _radius;

        public void Prepare()
        {
        }

        public void Extract(ref float r, ref float g, ref float b)
        {
            r *= 0.25f;
            g *= 0.25f;
            b *= 0.25f;
        }

        public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
            => Seen = br;
    }

    /// <summary>Ein heller Fleck in der Mitte einer dunklen Flaeche.</summary>
    private static FloatFrame Spot(int width, int height, float background, float value, int size)
    {
        int count = width * height;
        var values = new float[count];

        Array.Fill(values, background);

        int left = width / 2 - size / 2;
        int top = height / 2 - size / 2;

        for (int y = top; y < top + size; y++)
            for (int x = left; x < left + size; x++)
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

    /// <summary>Eine gleichmaessige Flaeche mit feinem Korn darauf.</summary>
    private static FloatFrame Grain(int width, int height, float value, float amount,
                                    bool colour, int seed)
    {
        int count = width * height;

        var r = new float[count];
        var g = new float[count];
        var b = new float[count];

        // Fest ausgesaet: Ein Test, der jedes Mal ein anderes Rauschen bekommt,
        // schlaegt irgendwann einmal fehl und niemand weiss, warum.
        var random = new Random(seed);

        for (int i = 0; i < count; i++)
        {
            float shared = (float)(random.NextDouble() - 0.5) * 2f * amount;

            r[i] = value + shared + Extra();
            g[i] = value + shared + Extra();
            b[i] = value + shared + Extra();

            float Extra() => colour ? (float)(random.NextDouble() - 0.5) * 2f * amount : 0f;
        }

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = r,
            G = g,
            B = b,
            A = Enumerable.Repeat(1f, count).ToArray(),
            IsSceneReferred = false,
        };
    }

    /// <summary>Die Streuung des Rotkanals - das Mass fuer Korn.</summary>
    private static double Deviation(byte[] pixels)
    {
        double average = Average(pixels);
        double sum = 0;
        int count = 0;

        for (int i = 2; i < pixels.Length; i += 4)
        {
            double difference = pixels[i] - average;
            sum += difference * difference;
            count++;
        }

        return Math.Sqrt(sum / Math.Max(1, count));
    }

    private static double Average(byte[] pixels)
    {
        double sum = 0;
        int count = 0;

        for (int i = 2; i < pixels.Length; i += 4)
        {
            sum += pixels[i];
            count++;
        }

        return sum / Math.Max(1, count);
    }

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

    /// <summary>Dasselbe Bild ueber den Sechzehn-Bit-Ausgang, als Rgba64.</summary>
    private static ushort[] Draw16(FloatFrame frame, GradingStack stack)
    {
        int stride = frame.Width * 8;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.ApplyRgba64(frame, ImageAdjustments.Neutral,
                                            new StandardViewTransform(), stack.Prepare(),
                                            buffer, stride);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var values = new ushort[pixels.Length / 2];
        Buffer.BlockCopy(pixels, 0, values, 0, pixels.Length);

        return values;
    }

    private static bool Same16(ushort[] first, ushort[] second)
    {
        if (first.Length != second.Length) return false;

        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return false;

        return true;
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
