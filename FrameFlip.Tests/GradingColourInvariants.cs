using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Farbwerkzeuge: Weissabgleich, Lift/Gamma/Gain und Dynamik. Jedes hat eine
/// Eigenheit, an der sich ein Fehler zeigt, bevor er im Bild auffaellt.
/// </summary>
public static class GradingColourInvariants
{
    public static void Run()
    {
        WhiteBalance();
        LiftGammaGain();
        Vibrance();
        Colorimetrics();
    }

    /// <summary>
    /// Der Weissabgleich, und vor allem die Richtung des Reglers.
    ///
    /// Sie ist die haeufigste Verwirrung an diesem Werkzeug: Die Zahl benennt die
    /// Beleuchtung der Aufnahme, nicht die Wirkung. Eine hohe Zahl heisst "das Licht
    /// war kalt" - und die Korrektur macht das Bild waermer. Wer das vertauscht, baut
    /// ein Werkzeug, das sich genau falsch herum anfuehlt, und merkt es erst, wenn
    /// jemand es benutzt.
    /// </summary>
    private static void WhiteBalance()
    {
        Check.Group("Weissabgleich");

        var neutral = new WhiteBalanceTool();
        Check.That(neutral.IsNeutral, "6500 K ohne Tendenz ist die Grundstellung");
        Check.That(neutral.Stage == GradingStage.SceneLinear, "und wirkt vor der Sichtumwandlung");

        neutral.Prepare();
        float r = 0.5f, g = 0.5f, b = 0.5f;
        neutral.Apply(ref r, ref g, ref b);
        Check.That(r == 0.5f && g == 0.5f && b == 0.5f, "in Grundstellung passiert nichts");

        // Hohe Zahl: kaltes Licht angenommen, also waermer korrigiert.
        var warm = new WhiteBalanceTool { Kelvin = 9500 };
        warm.Prepare();
        r = 0.5f; g = 0.5f; b = 0.5f;
        warm.Apply(ref r, ref g, ref b);

        Check.That(r > b, "eine hohe Zahl macht das Bild waermer", $"R {r:0.###} gegen B {b:0.###}");

        // Niedrige Zahl: warmes Licht angenommen, also kuehler korrigiert.
        var cool = new WhiteBalanceTool { Kelvin = 3200 };
        cool.Prepare();
        float r2 = 0.5f, g2 = 0.5f, b2 = 0.5f;
        cool.Apply(ref r2, ref g2, ref b2);

        Check.That(b2 > r2, "eine niedrige Zahl macht es kuehler", $"B {b2:0.###} gegen R {r2:0.###}");

        // Entgegengesetzt, nicht nur verschieden - ein Vorzeichenfehler faende sich
        // sonst nicht.
        Check.That(r > 0.5f && r2 < 0.5f, "und zwar in entgegengesetzte Richtungen");

        // Die Helligkeit soll dabei ungefaehr stehenbleiben. Eine Anpassung, die das
        // Bild nebenbei abdunkelt, wuerde man dem Belichtungsregler anlasten.
        float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        Check.Near(luma, 0.5, 0.08, "die Helligkeit bleibt ungefaehr stehen");

        // Tendenz: die zweite Achse, auf der die Temperatur nichts ausrichtet.
        var magenta = new WhiteBalanceTool { Tint = 80 };
        magenta.Prepare();
        r = 0.5f; g = 0.5f; b = 0.5f;
        magenta.Apply(ref r, ref g, ref b);
        Check.That(r > g && b > g, "positive Tendenz zieht nach Magenta",
                   $"{r:0.###}/{g:0.###}/{b:0.###}");

        var green = new WhiteBalanceTool { Tint = -80 };
        green.Prepare();
        r = 0.5f; g = 0.5f; b = 0.5f;
        green.Apply(ref r, ref g, ref b);
        Check.That(g > r && g > b, "negative nach Gruen", $"{r:0.###}/{g:0.###}/{b:0.###}");

        // Kein Kanal darf negativ werden - die Sichtumwandlung koennte damit nichts
        // anfangen, und ein negativer Wert liefe als NaN weiter.
        var extreme = new WhiteBalanceTool { Kelvin = 1667, Tint = -100 };
        extreme.Prepare();
        r = 0.02f; g = 0.9f; b = 0.02f;
        extreme.Apply(ref r, ref g, ref b);
        Check.That(r >= 0 && g >= 0 && b >= 0, "auch im Extrem bleibt nichts negativ",
                   $"{r:0.###}/{g:0.###}/{b:0.###}");

        var clamped = new WhiteBalanceTool { Kelvin = 99999, Tint = 500 };
        Check.That(clamped.Kelvin <= 25000 && clamped.Tint <= 100, "unsinnige Werte werden begrenzt");

        // Ueberstrahlung ueberlebt: das Werkzeug sitzt vor der Sichtumwandlung, wo
        // Werte ueber 1 der Normalfall sind.
        var mild = new WhiteBalanceTool { Kelvin = 7500 };
        mild.Prepare();
        r = 6f; g = 6f; b = 6f;
        mild.Apply(ref r, ref g, ref b);
        Check.That(r > 1f && g > 1f && b > 1f, "Werte ueber Weiss bleiben ueber Weiss",
                   $"{r:0.##}/{g:0.##}/{b:0.##}");
    }

    private static void LiftGammaGain()
    {
        Check.Group("Lift, Gamma und Gain");

        var tool = new LiftGammaGainTool();
        Check.That(tool.IsNeutral, "die Grundstellung ist neutral");
        Check.That(tool.Stage == GradingStage.Display, "und wirkt nach der Sichtumwandlung");

        // Lift hebt die Schatten und laesst Weiss in Ruhe - das ist der Faktor
        // (1 - v), der ihn erst zum Schattenregler macht. Ohne ihn waere es ein
        // Helligkeitsversatz ueber das ganze Bild.
        var lift = new LiftGammaGainTool { Lift = new ColourTriplet(0.2f, 0.2f, 0.2f) };
        lift.Prepare();

        float r = 0f, g = 0f, b = 0f;
        lift.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.2, 0.001, "Lift hebt Schwarz");

        r = 1f; g = 1f; b = 1f;
        lift.Apply(ref r, ref g, ref b);
        Check.Near(r, 1.0, 0.001, "und laesst Weiss, wo es ist");

        r = 0.5f; g = 0.5f; b = 0.5f;
        lift.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.6, 0.001, "die Mitte wird anteilig mitgenommen");

        var gain = new LiftGammaGainTool { Gain = new ColourTriplet(0.5f, 0.5f, 0.5f) };
        gain.Prepare();

        r = 0f; g = 0f; b = 0f;
        gain.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.0, 0.001, "Gain laesst Schwarz, wo es ist");

        r = 1f; g = 1f; b = 1f;
        gain.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.5, 0.001, "und halbiert Weiss");

        var gamma = new LiftGammaGainTool { Gamma = new ColourTriplet(2f, 2f, 2f) };
        gamma.Prepare();

        r = 0f; g = 0f; b = 0f;
        gamma.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.0, 0.001, "Gamma laesst Schwarz");

        r = 1f; g = 1f; b = 1f;
        gamma.Apply(ref r, ref g, ref b);
        Check.Near(r, 1.0, 0.001, "und Weiss");

        r = 0.25f; g = 0.25f; b = 0.25f;
        gamma.Apply(ref r, ref g, ref b);
        Check.Near(r, 0.5, 0.001, "und hebt die Mitte");

        // Je Kanal getrennt: kuehle Schatten, warme Lichter - der Griff, fuer den es
        // das Werkzeug ueberhaupt gibt, und in Kurven sechs Stuetzpunkte.
        var split = new LiftGammaGainTool
        {
            Lift = new ColourTriplet(0f, 0f, 0.15f),
            Gain = new ColourTriplet(1.1f, 1f, 0.9f),
        };

        split.Prepare();

        float dr = 0.1f, dg = 0.1f, db = 0.1f;
        split.Apply(ref dr, ref dg, ref db);
        Check.That(db > dr, "Schatten werden kuehler", $"{dr:0.###}/{dg:0.###}/{db:0.###}");

        float hr = 0.9f, hg = 0.9f, hb = 0.9f;
        split.Apply(ref hr, ref hg, ref hb);
        Check.That(hr > hb, "Lichter waermer", $"{hr:0.###}/{hg:0.###}/{hb:0.###}");

        var wild = new LiftGammaGainTool
        {
            Lift = new ColourTriplet(0.9f, 0.9f, 0.9f),
            Gain = new ColourTriplet(4f, 4f, 4f),
            Gamma = new ColourTriplet(0.01f, 0.01f, 0.01f),
        };

        wild.Prepare();
        r = 0.7f; g = 0.7f; b = 0.7f;
        wild.Apply(ref r, ref g, ref b);
        Check.That(r is >= 0 and <= 1 && !float.IsNaN(r), "auch im Extrem bleibt es im Bereich", $"{r}");

        // Gamma null waere eine Division durch null - der Regler faengt das ab.
        var zero = new LiftGammaGainTool { Gamma = new ColourTriplet(0f, 0f, 0f) };
        zero.Prepare();
        r = 0.5f; g = 0.5f; b = 0.5f;
        zero.Apply(ref r, ref g, ref b);
        Check.That(!float.IsNaN(r) && !float.IsInfinity(r), "Gamma null ergibt keinen Unsinn", $"{r}");
    }

    private static void Vibrance()
    {
        Check.Group("Dynamik");

        var tool = new VibranceTool();
        Check.That(tool.IsNeutral, "die Grundstellung ist neutral");

        var lift = new VibranceTool { Amount = 0.6f };
        lift.Prepare();

        // Eine blasse und eine kraeftige Farbe. Die blasse muss deutlich mehr
        // dazugewinnen - das ist der ganze Unterschied zur Saettigung, die beide
        // gleich behandelt und die kraeftige als erste aus dem Bereich treibt.
        float pr = 0.55f, pg = 0.50f, pb = 0.45f;
        float sr = 0.95f, sg = 0.20f, sb = 0.10f;

        float paleBefore = pr - pb;
        float strongBefore = sr - sb;

        lift.Apply(ref pr, ref pg, ref pb);
        lift.Apply(ref sr, ref sg, ref sb);

        float paleGain = (pr - pb) - paleBefore;
        float strongGain = (sr - sb) - strongBefore;

        Check.That(paleGain > 0, "die blasse Farbe gewinnt an Saettigung", $"{paleGain:0.####}");
        Check.That(paleGain > strongGain, "und zwar mehr als die kraeftige",
                   $"blass {paleGain:0.####} gegen kraeftig {strongGain:0.####}");

        // Grau hat keine Saettigung, die sich anheben liesse.
        float gr = 0.4f, gg = 0.4f, gb = 0.4f;
        lift.Apply(ref gr, ref gg, ref gb);
        Check.That(gr == gg && gg == gb, "Grau bleibt grau", $"{gr:0.###}/{gg:0.###}/{gb:0.###}");

        var drop = new VibranceTool { Amount = -0.8f };
        drop.Prepare();
        float ur = 0.8f, ug = 0.3f, ub = 0.2f;
        float before = ur - ub;
        drop.Apply(ref ur, ref ug, ref ub);

        Check.That(ur - ub < before, "negative Dynamik nimmt Saettigung zurueck");
        Check.That(ur is >= 0 and <= 1 && ug is >= 0 and <= 1, "und bleibt im Bereich");
    }

    /// <summary>
    /// Die Farbraummathematik darunter. Ein Fehler hier faerbt gleichmaessig und
    /// sieht damit nach Absicht aus - er faellt ohne Pruefung nicht auf.
    /// </summary>
    private static void Colorimetrics()
    {
        Check.Group("Farbraummathematik");

        // Hin und zurueck muss die Einheitsmatrix ergeben.
        var roundTrip = Colorimetry.Multiply(Colorimetry.XyzToRec709, Colorimetry.Rec709ToXyz);
        float worst = 0;
        for (int i = 0; i < 9; i++)
        {
            float expected = i % 4 == 0 ? 1f : 0f;
            worst = MathF.Max(worst, MathF.Abs(roundTrip[i] - expected));
        }

        Check.That(worst < 1e-4f, "Rec.709 nach XYZ und zurueck ergibt die Einheit", $"{worst:0.#######}");

        // Die Zeilensummen der Matrix sind der Weisspunkt - so laesst sich die
        // Richtung pruefen, ohne eine zweite Quelle zu befragen.
        float wx = Colorimetry.Rec709ToXyz[0] + Colorimetry.Rec709ToXyz[1] + Colorimetry.Rec709ToXyz[2];
        float wy = Colorimetry.Rec709ToXyz[3] + Colorimetry.Rec709ToXyz[4] + Colorimetry.Rec709ToXyz[5];
        float wz = Colorimetry.Rec709ToXyz[6] + Colorimetry.Rec709ToXyz[7] + Colorimetry.Rec709ToXyz[8];

        Check.Near(wx, Colorimetry.D65[0], 0.001, "die Zeilensummen ergeben D65 in X");
        Check.Near(wy, Colorimetry.D65[1], 0.001, "in Y");
        Check.Near(wz, Colorimetry.D65[2], 0.001, "und in Z");

        // 6500 K liegt nahe D65 - nicht exakt darauf, weil D65 ein Tageslichtspektrum
        // ist und kein schwarzer Strahler, aber nah genug, dass eine Verwechslung
        // der Formeln auffiele.
        var (x, y) = Colorimetry.PlanckianXy(6500);
        Check.Near(x, 0.313, 0.006, "6500 K liegt nahe am Weisspunkt in x");
        Check.Near(y, 0.323, 0.010, "und in y");

        // Waermer heisst: mehr Rot, also groesseres x.
        var (warmX, _) = Colorimetry.PlanckianXy(2800);
        var (coolX, _) = Colorimetry.PlanckianXy(12000);
        Check.That(warmX > x && coolX < x, "niedrige Temperatur liegt roter, hohe blauer",
                   $"{warmX:0.###} / {x:0.###} / {coolX:0.###}");

        // Eine Anpassung auf denselben Weisspunkt darf nichts tun.
        var same = Colorimetry.Adaptation(Colorimetry.D65, Colorimetry.D65);
        worst = 0;
        for (int i = 0; i < 9; i++)
        {
            float expected = i % 4 == 0 ? 1f : 0f;
            worst = MathF.Max(worst, MathF.Abs(same[i] - expected));
        }

        Check.That(worst < 1e-4f, "eine Anpassung auf denselben Punkt ist die Einheit", $"{worst:0.#######}");

        Check.Throws<InvalidOperationException>(
            () => Colorimetry.Invert(new float[9]),
            "eine nicht umkehrbare Matrix wird gemeldet");
    }
}
