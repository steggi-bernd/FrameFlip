using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Mischungen.
///
/// Sie sind die Stelle, an der die Entscheidung "lineares Licht statt Anzeigewerte"
/// sichtbar wird, und damit auch die Stelle, an der sie schiefgehen kann. Geprueft
/// wird deshalb beides: dass unterhalb von Weiss dasselbe herauskommt wie in
/// Photoshop, und dass oberhalb nichts umkippt.
/// </summary>
public static class BlendInvariants
{
    public static void Run()
    {
        LikePhotoshopBelowWhite();
        MiddleGreyIsTheHinge();
        NothingGoesNegative();
        ScreenKeepsRising();
        OpacityIsALine();
        Reconstruction();
    }

    /// <summary>
    /// Zwischen 0 und 1 muessen die Formeln die gewohnten sein.
    ///
    /// Das ist die halbe Antwort auf "kann man das wie Photoshop machen?": Wer ein
    /// PNG bearbeitet oder zwei Passe mischt, die unter Weiss bleiben, soll genau
    /// das bekommen, was er kennt. Der Unterschied faengt erst dort an, wo Photoshop
    /// aufhoert.
    /// </summary>
    private static void LikePhotoshopBelowWhite()
    {
        Check.Group("Unter Weiss rechnet es wie Photoshop");

        int wrongMultiply = 0, wrongScreen = 0, wrongDarken = 0, wrongDifference = 0;

        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                float a = i / 20f, b = j / 20f;

                if (Off(Blending.Channel(BlendMode.Multiply, a, b), a * b)) wrongMultiply++;
                if (Off(Blending.Channel(BlendMode.Screen, a, b), a + b - a * b)) wrongScreen++;
                if (Off(Blending.Channel(BlendMode.Darken, a, b), MathF.Min(a, b))) wrongDarken++;
                if (Off(Blending.Channel(BlendMode.Difference, a, b), MathF.Abs(a - b))) wrongDifference++;
            }
        }

        Check.That(wrongMultiply == 0, "Multiplizieren ist das Produkt", $"{wrongMultiply} Abweichungen");
        Check.That(wrongScreen == 0, "Negativ multiplizieren ist a+b-ab", $"{wrongScreen} Abweichungen");
        Check.That(wrongDarken == 0, "Abdunkeln ist das Minimum", $"{wrongDarken} Abweichungen");
        Check.That(wrongDifference == 0, "Differenz ist der Betrag", $"{wrongDifference} Abweichungen");

        static bool Off(float actual, float expected) => MathF.Abs(actual - expected) > 1e-5f;
    }

    /// <summary>
    /// Der Drehpunkt der drei Kontrastmischungen.
    ///
    /// Overlay, Soft Light und Hard Light drehen in Photoshop um 0,5 - dort liegt
    /// die wahrgenommene Mitte einer Anzeige. In linearem Licht liegt sie bei 0,18,
    /// und wenn die geliehene Umrechnung stimmt, muss eine Ebene mit genau diesem
    /// Wert nichts tun. Das ist der Test, der die ganze Konstruktion traegt: Faellt
    /// er, steht der Drehpunkt woanders, und jede dieser Mischungen hellt auf oder
    /// dunkelt ab, ohne dass jemand daran gedreht haette.
    /// </summary>
    private static void MiddleGreyIsTheHinge()
    {
        Check.Group("Mittleres Grau dreht nichts");

        var hinged = new[] { BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight };

        foreach (var mode in hinged)
        {
            float worst = 0;

            // Ueber mehr als zwanzig Blendenstufen, bis weit ueber Weiss.
            foreach (float under in new[] { 0f, 0.001f, 0.02f, 0.18f, 0.5f, 1f, 4f, 20f, 100f })
            {
                // In allen dreien ist es die OBERE Ebene, die auf mittlerem Grau
                // stehen muss - auch bei Hard Light, das sonst nach der unteren
                // verzweigt: Die Ebene, die nichts tun soll, ist immer die obere.
                float result = Blending.Channel(mode, under, Blending.MiddleGrey);

                // Relativ gemessen: bei 100 waere eine absolute Schranke von 0,001
                // eine Genauigkeitsforderung, die keine Aussage mehr traegt.
                float off = MathF.Abs(result - under) / MathF.Max(1f, under);
                if (off > worst) worst = off;
            }

            Check.That(worst < 0.002f, $"{mode} laesst den Wert stehen", $"{worst:0.#####}");
        }

        // Und umgekehrt: Eine Ebene UNTER einer Overlay-Ebene aus mittlerem Grau
        // wird nicht veraendert - aber eine Overlay-Ebene ueber mittlerem Grau
        // faerbt durch. Das ist die Asymmetrie, die Overlay von Multiply trennt.
        float lifted = Blending.Channel(BlendMode.Overlay, Blending.MiddleGrey, 1f);
        Check.That(lifted > Blending.MiddleGrey * 1.5f,
                   "eine helle Ebene auf Overlay hellt das Grau auf", $"{lifted:0.###}");

        float lowered = Blending.Channel(BlendMode.Overlay, Blending.MiddleGrey, 0.02f);
        Check.That(lowered < Blending.MiddleGrey * 0.5f,
                   "eine dunkle dunkelt es ab", $"{lowered:0.###}");
    }

    /// <summary>
    /// Kein Weg darf aus Licht Dunkelheit machen.
    ///
    /// Die naheliegende Formel fuer Screen - a+b-ab - tut genau das: Zwei Passe mit
    /// Wert 3 ergaeben -3, also Schwarz. Wer einen Glanzpass mit Werten von 40 auf
    /// Screen stellt, saehe ein schwarzes Bild und hielte das Programm fuer kaputt.
    /// </summary>
    private static void NothingGoesNegative()
    {
        Check.Group("Keine Mischung faellt unter null");

        float[] values = { 0f, 0.05f, 0.18f, 0.5f, 1f, 2f, 5f, 40f, 300f };
        var modes = Enum.GetValues<BlendMode>();

        int negative = 0;
        int notFinite = 0;

        foreach (var mode in modes)
        {
            foreach (float a in values)
            {
                foreach (float b in values)
                {
                    float result = Blending.Channel(mode, a, b);

                    if (result < -1e-4f) negative++;
                    if (!float.IsFinite(result)) notFinite++;
                }
            }
        }

        Check.That(negative == 0, "aus Licht wird nie Dunkelheit", $"{negative} Faelle");
        Check.That(notFinite == 0, "und nie Unendlich", $"{notFinite} Faelle");
    }

    /// <summary>
    /// Screen muss in beiden Richtungen steigen.
    ///
    /// Eine Mischung, die mehr Licht bekommt und weniger liefert, ist nicht nur
    /// falsch, sondern unbedienbar: Der Regler tut dann an einer Stelle das
    /// Gegenteil dessen, was er zwei Zentimeter weiter tut.
    /// </summary>
    private static void ScreenKeepsRising()
    {
        Check.Group("Negativ multiplizieren steigt");

        int falling = 0;

        for (int i = 0; i < 60; i++)
        {
            float under = i * 0.5f;
            float previous = float.NegativeInfinity;

            for (int j = 0; j < 60; j++)
            {
                float over = j * 0.5f;
                float result = Blending.Channel(BlendMode.Screen, under, over);

                if (result < previous - 1e-4f) falling++;
                previous = result;
            }
        }

        Check.That(falling == 0, "mehr Licht ergibt nie weniger", $"{falling} Faelle");

        // Schwarz darueber laesst alles stehen - auch weit ueber Weiss, wo die
        // gewoehnliche Formel laengst umgekippt waere.
        Check.Near(Blending.Channel(BlendMode.Screen, 40f, 0f), 40.0, 1e-4, "Schwarz aendert nichts");
        Check.Near(Blending.Channel(BlendMode.Screen, 1f, 1f), 1.0, 1e-4, "Weiss auf Weiss bleibt Weiss");
    }

    /// <summary>
    /// Die Deckkraft mischt linear zwischen unten und dem Ergebnis - nicht zwischen
    /// unten und der oberen Ebene. Der Unterschied ist bei Multiply sofort zu sehen:
    /// eine halbdurchsichtige Multiply-Ebene soll schwaecher multiplizieren, nicht
    /// halb aufgetragen werden.
    /// </summary>
    private static void OpacityIsALine()
    {
        Check.Group("Deckkraft mischt zum Ergebnis hin");

        Blending.Mix(BlendMode.Multiply, 0f, 0.4f, 0.4f, 0.4f, 0.1f, 0.1f, 0.1f,
                     out float r0, out _, out _);
        Check.Near(r0, 0.4, 1e-5, "bei null bleibt die untere Ebene stehen");

        Blending.Mix(BlendMode.Multiply, 1f, 0.4f, 0.4f, 0.4f, 0.1f, 0.1f, 0.1f,
                     out float r1, out _, out _);
        Check.Near(r1, 0.04, 1e-5, "bei eins gilt das Produkt");

        Blending.Mix(BlendMode.Multiply, 0.5f, 0.4f, 0.4f, 0.4f, 0.1f, 0.1f, 0.1f,
                     out float half, out _, out _);
        Check.Near(half, 0.22, 1e-5, "bei der Haelfte genau dazwischen");

        // Und bei Add: eine halb gedeckte Ebene addiert die Haelfte.
        Blending.Mix(BlendMode.Add, 0.5f, 1f, 1f, 1f, 2f, 2f, 2f, out float added, out _, out _);
        Check.Near(added, 2.0, 1e-5, "auf Addieren zaehlt die Haelfte des Passes");
    }

    /// <summary>
    /// Der eigentliche Grund fuer lineares Licht.
    ///
    /// Blender zerlegt das Bild additiv. Alle Passe auf Addieren muessen deshalb
    /// wieder genau das ergeben, was gerendert wurde - und zwar auch dann, wenn
    /// einzelne Passe weit ueber Weiss liegen. Genau das geht verloren, sobald man
    /// die Passe vorher auf 1 beschneidet, wie Photoshop es taete.
    /// </summary>
    private static void Reconstruction()
    {
        Check.Group("Alle Passe auf Addieren ergeben das Bild");

        // Drei Passe mit Werten, wie sie ein Render liefert: ein diffuser Anteil
        // unter Weiss, ein Glanz weit darueber, eine schwache Emission.
        float[] passes = { 0.43f, 37.2f, 0.06f };
        float sum = passes.Sum();

        float built = 0f;
        foreach (float pass in passes)
            built = Blending.Channel(BlendMode.Add, built, pass);

        Check.Near(built, sum, 1e-3, "die Summe stimmt");
        Check.That(built > 30f, "und die Zeichnung ueber Weiss ist noch da", $"{built:0.##}");

        // Zum Vergleich: auf 1 beschnitten - was in Photoshops Bereich geschaehe -
        // bliebe von 37,7 gerade 1,5 uebrig. Die Rechnung steht hier, weil sie die
        // Entscheidung begruendet und nicht, weil der Code sie macht.
        float clipped = passes.Select(p => MathF.Min(p, 1f)).Sum();
        Check.That(clipped < built / 10f,
                   "beschnitten waere davon fast nichts uebrig", $"{clipped:0.##} statt {built:0.##}");
    }
}
