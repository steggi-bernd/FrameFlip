using System.IO;
using System.Text;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die beiden Werkzeuge, die auf Bereichen arbeiten: HSL je Farbbereich und die
/// Nachschlagetabelle. Beim ersten ist die Weichheit der Uebergaenge die ganze
/// Arbeit, beim zweiten das Verhalten, wenn die Datei fehlt.
/// </summary>
public static class GradingBandInvariants
{
    public static void Run()
    {
        HslBasics();
        HslNoEdges();
        HslWrapAround();
        HslRoundTrip();
        Lut();
    }

    private static void HslBasics()
    {
        Check.Group("HSL je Farbbereich");

        var tool = new HslTool();
        Check.That(tool.IsNeutral, "die Grundstellung ist neutral");
        Check.That(tool.Bands.Count == 8, "acht Bereiche", $"{tool.Bands.Count}");
        Check.That(tool.Stage == GradingStage.Display, "wirkt nach der Sichtumwandlung");

        // Nur Blau anfassen. Ein blauer Punkt muss sich aendern, ein roter nicht.
        var blueOnly = new HslTool();
        blueOnly.Bands[5].Saturation = -100;      // Index 5 ist Blau, 240 Grad
        blueOnly.Prepare();

        Check.That(!blueOnly.IsNeutral, "ein gesetztes Band macht das Werkzeug wirksam");

        float br = 0.2f, bg = 0.3f, bb = 0.9f;
        float beforeSpread = bb - br;
        blueOnly.Apply(ref br, ref bg, ref bb);
        Check.That(bb - br < beforeSpread * 0.5f, "Blau verliert seine Saettigung",
                   $"{br:0.##}/{bg:0.##}/{bb:0.##}");

        float rr = 0.9f, rg = 0.2f, rb = 0.2f;
        float redBefore = rr - rb;
        blueOnly.Apply(ref rr, ref rg, ref rb);
        Check.Near(rr - rb, redBefore, 0.02, "Rot bleibt dabei unberuehrt");

        // Grau hat keinen Farbton, den ein Bereich fuer sich beanspruchen koennte.
        var everything = new HslTool();
        foreach (var band in everything.Bands) band.Saturation = 100;
        everything.Prepare();

        float gr = 0.5f, gg = 0.5f, gb = 0.5f;
        everything.Apply(ref gr, ref gg, ref gb);
        Check.That(gr == gg && gg == gb, "Grau bleibt grau", $"{gr:0.###}/{gg:0.###}/{gb:0.###}");

        // Ein Rezept aus einer Fassung mit weniger Baendern darf nicht ins Leere
        // greifen.
        var short_ = new HslTool { Bands = new List<HslBand> { new(), new() } };
        short_.Prepare();
        Check.That(short_.Bands.Count == 8, "fehlende Baender werden ergaenzt", $"{short_.Bands.Count}");

        float sr = 0.8f, sg = 0.4f, sb = 0.2f;
        short_.Apply(ref sr, ref sg, ref sb);
        Check.That(!float.IsNaN(sr), "und das Werkzeug rechnet danach");
    }

    /// <summary>
    /// Der Test, fuer den die Ueberblendung gebaut ist.
    ///
    /// Wuerde jeder Bildpunkt dem naechstgelegenen Bereich zugeschlagen, entstuenden
    /// an den Grenzen Kanten quer durch Farbverlaeufe - ein Himmel bekaeme einen Riss
    /// dort, wo Blau in Aqua uebergeht. Der Test faehrt den ganzen Farbkreis ab und
    /// prueft, dass zwei benachbarte Farbtoene nach der Korrektur noch benachbart sind.
    /// </summary>
    private static void HslNoEdges()
    {
        Check.Group("HSL erzeugt keine Kanten");

        // Eine Einstellung, die von Bereich zu Bereich stark wechselt - der
        // schwierigste Fall fuer die Ueberblendung.
        var tool = new HslTool();
        for (int i = 0; i < 8; i++)
        {
            tool.Bands[i].Saturation = i % 2 == 0 ? 90 : -90;
            tool.Bands[i].Hue = i % 2 == 0 ? -60 : 60;
        }

        tool.Prepare();

        float worstStep = 0;
        float worstAt = 0;
        float previousR = 0, previousG = 0, previousB = 0;
        bool first = true;

        for (float hue = 0; hue < 360f; hue += 0.25f)
        {
            HslTool.HslToRgb(hue, 0.6f, 0.5f, out float r, out float g, out float b);
            tool.Apply(ref r, ref g, ref b);

            if (!first)
            {
                float step = MathF.Abs(r - previousR) + MathF.Abs(g - previousG) + MathF.Abs(b - previousB);
                if (step > worstStep)
                {
                    worstStep = step;
                    worstAt = hue;
                }
            }

            previousR = r;
            previousG = g;
            previousB = b;
            first = false;
        }

        // Ein Viertelgrad Farbtonunterschied darf sich nicht als sichtbarer Sprung
        // niederschlagen. Der Schwellwert liegt bei rund drei Stufen auf acht Bit,
        // verteilt ueber alle drei Kanaele.
        Check.That(worstStep < 0.04f, "benachbarte Farbtoene bleiben benachbart",
                   $"groesster Sprung {worstStep:0.####} bei {worstAt:0.#} Grad");

        // Die Gewichte muessen sich in jedem Punkt auf eins summieren, sonst wuerde
        // die Korrektur je nach Farbton unterschiedlich stark ausfallen.
        float worstSum = 0;
        for (float hue = 0; hue < 360f; hue += 0.5f)
        {
            HslTool.Weights(hue, out int a, out int b, out float blend);
            float sum = (1f - blend) + blend;

            worstSum = MathF.Max(worstSum, MathF.Abs(sum - 1f));

            if (a is < 0 or > 7 || b is < 0 or > 7)
            {
                Check.That(false, "die Gewichtung trifft gueltige Bereiche", $"{a}/{b} bei {hue}");
                return;
            }

            if (blend is < 0f or > 1f)
            {
                Check.That(false, "und bleibt zwischen null und eins", $"{blend} bei {hue}");
                return;
            }
        }

        Check.That(worstSum < 1e-5f, "die Gewichte summieren sich auf eins");
    }

    /// <summary>
    /// Der Umlauf ueber den Nullpunkt. Magenta liegt bei 300 Grad, Rot bei 0 - ohne
    /// den Anschluss ueber 360 hinweg bekaeme der Bereich dazwischen eine harte Kante
    /// oder gar keine Behandlung.
    /// </summary>
    private static void HslWrapAround()
    {
        Check.Group("HSL schliesst den Farbkreis");

        HslTool.Weights(330f, out int a, out int b, out float blend);
        Check.That(a == 7 && b == 0, "zwischen Magenta und Rot greifen beide", $"{a}/{b}");
        Check.Near(blend, 0.5, 0.05, "und auf halbem Weg zu gleichen Teilen");

        HslTool.Weights(359.9f, out a, out b, out blend);
        Check.That(a == 7 && b == 0, "kurz vor dem Nullpunkt ebenso", $"{a}/{b}");
        Check.That(blend > 0.9f, "fast ganz bei Rot", $"{blend:0.##}");

        HslTool.Weights(0.1f, out a, out b, out blend);
        Check.That(b == 0 || a == 0, "und kurz danach auch", $"{a}/{b}");

        // Rot ueber die Naht hinweg: eine Einstellung auf Rot muss beide Seiten
        // treffen, nicht nur die oberhalb von null.
        var tool = new HslTool();
        tool.Bands[0].Saturation = -100;
        tool.Prepare();

        HslTool.HslToRgb(358f, 0.7f, 0.5f, out float r1, out float g1, out float b1);
        float before = MathF.Max(r1, MathF.Max(g1, b1)) - MathF.Min(r1, MathF.Min(g1, b1));
        tool.Apply(ref r1, ref g1, ref b1);
        float after = MathF.Max(r1, MathF.Max(g1, b1)) - MathF.Min(r1, MathF.Min(g1, b1));

        Check.That(after < before * 0.6f, "Rot unterhalb der Naht wird mitgenommen",
                   $"{before:0.###} auf {after:0.###}");
    }

    private static void HslRoundTrip()
    {
        Check.Group("RGB und HSL hin und zurueck");

        int wrong = 0;
        float worst = 0;

        foreach (float r in new[] { 0f, 0.13f, 0.5f, 0.87f, 1f })
        {
            foreach (float g in new[] { 0f, 0.29f, 0.5f, 0.71f, 1f })
            {
                foreach (float b in new[] { 0f, 0.37f, 0.5f, 0.63f, 1f })
                {
                    HslTool.RgbToHsl(r, g, b, out float h, out float s, out float l);
                    HslTool.HslToRgb(h, s, l, out float br, out float bg, out float bb);

                    float error = MathF.Abs(br - r) + MathF.Abs(bg - g) + MathF.Abs(bb - b);
                    if (error > 0.002f) wrong++;
                    worst = MathF.Max(worst, error);
                }
            }
        }

        Check.That(wrong == 0, "die Umrechnung trifft wieder den Ausgangswert",
                   $"{wrong} Abweichungen, groesste {worst:0.#####}");

        // Grau hat keinen Farbton - und darf bei der Rueckrechnung keinen bekommen.
        HslTool.RgbToHsl(0.4f, 0.4f, 0.4f, out _, out float greySat, out float greyLum);
        Check.That(greySat == 0f, "Grau hat keine Saettigung");
        Check.Near(greyLum, 0.4, 1e-5, "und behaelt seine Helligkeit");
    }

    private static void Lut()
    {
        Check.Group("Nachschlagetabelle als Werkzeug");

        var tool = new LutTool();
        Check.That(tool.IsNeutral, "ohne Pfad ist sie neutral");
        Check.That(tool.Stage == GradingStage.Display, "und wirkt nach der Sichtumwandlung");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-lut-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            // Eine Tabelle, die Rot und Blau vertauscht - eindeutig nachprüfbar.
            string swap = Path.Combine(folder, "swap.cube");
            File.WriteAllText(swap, Build(2, (r, g, b) => (b, g, r)));

            var swapper = new LutTool { Path = swap };
            swapper.Prepare();

            Check.That(swapper.Error is null, "die Tabelle wurde gelesen", swapper.Error);
            Check.That(!swapper.IsNeutral, "und das Werkzeug ist wirksam");

            float r = 0.8f, g = 0.4f, b = 0.1f;
            swapper.Apply(ref r, ref g, ref b);

            Check.Near(r, 0.1, 0.01, "Rot bekommt den Blauwert");
            Check.Near(b, 0.8, 0.01, "und Blau den Rotwert");
            Check.Near(g, 0.4, 0.01, "Gruen bleibt");

            // Halbe Staerke mischt zur Haelfte.
            var half = new LutTool { Path = swap, Strength = 0.5f };
            half.Prepare();

            r = 0.8f; g = 0.4f; b = 0.1f;
            half.Apply(ref r, ref g, ref b);
            Check.Near(r, 0.45, 0.01, "halbe Staerke mischt zur Haelfte");

            // Staerke null ist neutral, ohne die Datei ueberhaupt anzufassen.
            var off = new LutTool { Path = swap, Strength = 0f };
            Check.That(off.IsNeutral, "Staerke null ist neutral");

            // Eine fehlende Datei darf nicht werfen - das Rezept soll sich oeffnen
            // lassen, auch wenn die Tabelle inzwischen woanders liegt.
            var missing = new LutTool { Path = Path.Combine(folder, "gibtsnicht.cube") };
            missing.Prepare();

            Check.That(missing.Error is not null, "eine fehlende Datei wird gemeldet");

            r = 0.8f; g = 0.4f; b = 0.1f;
            missing.Apply(ref r, ref g, ref b);
            Check.That(r == 0.8f && g == 0.4f && b == 0.1f, "und das Werkzeug tut dann nichts");

            // Eine kaputte Datei ebenso.
            string broken = Path.Combine(folder, "kaputt.cube");
            File.WriteAllText(broken, "LUT_3D_SIZE 2\n0 0 0\n");

            var bad = new LutTool { Path = broken };
            bad.Prepare();
            Check.That(bad.Error is not null, "eine unvollstaendige Tabelle wird gemeldet");

            // Ein Pfadwechsel muss neu laden, ein gleicher Pfad nicht.
            var reused = new LutTool { Path = swap };
            reused.Prepare();
            reused.Path = swap;
            reused.Prepare();
            r = 0.8f; g = 0.4f; b = 0.1f;
            reused.Apply(ref r, ref g, ref b);
            Check.Near(r, 0.1, 0.01, "derselbe Pfad wird nicht neu gelesen und wirkt weiter");

            string identity = Path.Combine(folder, "identity.cube");
            File.WriteAllText(identity, Build(2, (r2, g2, b2) => (r2, g2, b2)));

            reused.Path = identity;
            reused.Prepare();
            r = 0.8f; g = 0.4f; b = 0.1f;
            reused.Apply(ref r, ref g, ref b);
            Check.Near(r, 0.8, 0.01, "ein neuer Pfad wird gelesen");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static string Build(int size, Func<float, float, float, (float, float, float)> f)
    {
        var text = new StringBuilder();
        text.AppendLine($"LUT_3D_SIZE {size}");

        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var (r, g, b) = f(x / (size - 1f), y / (size - 1f), z / (size - 1f));
                    text.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                                  "{0} {1} {2}", r, g, b));
                }
            }
        }

        return text.ToString();
    }
}
