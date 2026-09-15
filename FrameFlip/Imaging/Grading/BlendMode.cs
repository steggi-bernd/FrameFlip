namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Wie eine Ebene auf das wirkt, was unter ihr liegt.
///
/// Gerechnet wird in linearem Licht, nicht in Anzeigewerten - und das ist der eine
/// Unterschied zu Photoshop, der zaehlt. Dort ist jede Ebene ein fertiges Bild
/// zwischen 0 und 1, und die Mischungen sind darauf gebaut. Ein Glanzpass hat Werte
/// von 40; auf 1 beschnitten liesse er sich nicht einmal mehr mit den uebrigen zum
/// Ausgangsbild zusammensetzen.
///
/// Die Namen bleiben die gewohnten, und zwischen 0 und 1 rechnen sie auch dasselbe.
/// </summary>
public enum BlendMode
{
    /// <summary>Die obere Ebene ersetzt die untere, nach Deckkraft gemischt.</summary>
    Normal,

    /// <summary>
    /// Summe. Die natuerliche Zusammensetzung von Renderpassen - Blender hat das
    /// Bild additiv zerlegt, und so fuegt es sich wieder.
    /// </summary>
    Add,

    /// <summary>Produkt. Abschwaechung, wie sie ein Verschattungspass meint.</summary>
    Multiply,

    /// <summary>Umgekehrtes Produkt - zwei Lichter, die einander nicht ausloeschen.</summary>
    Screen,

    Darken,
    Lighten,
    Difference,

    /// <summary>Kontrastmischung: unten abdunkeln, oben aufhellen.</summary>
    Overlay,

    /// <summary>Wie <see cref="Overlay"/>, aber sanft und ohne harte Kante.</summary>
    SoftLight,

    /// <summary>Wie <see cref="Overlay"/> mit vertauschten Rollen.</summary>
    HardLight,
}

/// <summary>
/// Die Mischungen selbst.
///
/// Drei von ihnen - Overlay, Soft Light, Hard Light - brauchen ein Weiss, um das
/// sie drehen koennen. In linearem Licht gibt es keines: nach oben ist offen. Sie
/// bekommen deshalb einen eigenen Weg, siehe <see cref="ToDisplay"/>.
/// </summary>
public static class Blending
{
    /// <summary>
    /// Mittleres Grau.
    ///
    /// 0,18 ist die Zahl, auf die sich Belichtungsmesser, Graukarten und
    /// Renderwerkzeuge geeinigt haben: die Lichtmenge, die das Auge als "mitten
    /// zwischen Schwarz und Weiss" sieht. Sie ist hier der Drehpunkt der
    /// Kontrastmischungen - dort, wo Photoshop 0,5 benutzt.
    /// </summary>
    public const float MiddleGrey = 0.18f;

    /// <summary>
    /// Mischt die obere Ebene auf die untere.
    ///
    /// Die Deckkraft wirkt zum Schluss und linear: Bei 0 bleibt die untere Ebene
    /// stehen, bei 1 gilt das Ergebnis der Mischung. Das ist dieselbe Regel wie in
    /// Photoshop, und sie ist der Grund, warum eine halbdurchsichtige Multiply-Ebene
    /// sich verhaelt wie eine schwaechere Multiply-Ebene und nicht wie eine halb
    /// aufgetragene.
    /// </summary>
    public static void Mix(BlendMode mode, float opacity,
                           float ur, float ug, float ub,
                           float or_, float og, float ob,
                           out float r, out float g, out float b)
    {
        float mr = Channel(mode, ur, or_);
        float mg = Channel(mode, ug, og);
        float mb = Channel(mode, ub, ob);

        if (opacity >= 0.999f)
        {
            r = mr;
            g = mg;
            b = mb;
            return;
        }

        r = ur + (mr - ur) * opacity;
        g = ug + (mg - ug) * opacity;
        b = ub + (mb - ub) * opacity;
    }

    /// <summary>Ein Kanal, ohne Deckkraft. <paramref name="under"/> liegt unten.</summary>
    public static float Channel(BlendMode mode, float under, float over) => mode switch
    {
        BlendMode.Normal => over,
        BlendMode.Add => under + over,
        BlendMode.Multiply => under * over,

        // Photoshops Formel ist 1-(1-a)(1-b), also a+b-ab. Ueber Weiss laeuft sie ins
        // Negative - zwei Passe mit Wert 3 ergaeben -3, also schwarz. Der Produktterm
        // wird deshalb bei 1 gedeckelt: Unterhalb von Weiss ist es Zeichen fuer
        // Zeichen Photoshops Screen, darueber waechst es weiter, statt umzukippen.
        BlendMode.Screen => under + over - MathF.Min(under, 1f) * MathF.Min(over, 1f),

        BlendMode.Darken => MathF.Min(under, over),
        BlendMode.Lighten => MathF.Max(under, over),
        BlendMode.Difference => MathF.Abs(under - over),

        BlendMode.Overlay => Contrast(under, over, hard: false),
        BlendMode.HardLight => Contrast(over, under, hard: false),
        BlendMode.SoftLight => Contrast(under, over, hard: true),

        _ => over,
    };

    // ----------------------------------------------------- die drei mit Drehpunkt

    /// <summary>
    /// Rechnet eine Lichtmenge in einen anzeigeaehnlichen Wert zwischen 0 und 1.
    ///
    /// Overlay, Soft Light und Hard Light sind ihrem Wesen nach Anzeigerechnungen:
    /// Sie fragen "ist dieser Wert heller oder dunkler als die Mitte?", und eine
    /// Mitte gibt es nur zwischen zwei Enden. In linearem Licht fehlt das obere.
    ///
    /// Statt die Formeln zu verbiegen, bekommen sie hier ein Weiss geliehen. Die
    /// Abbildung x/(x+g) bildet 0 auf 0 ab, mittleres Grau genau auf 0,5 und
    /// Unendlich auf 1 - der Drehpunkt liegt damit exakt dort, wo Photoshop ihn hat,
    /// und nichts wird unterwegs beschnitten. Danach geht es denselben Weg zurueck.
    /// </summary>
    private static float ToDisplay(float light)
        => light <= 0f ? 0f : light / (light + MiddleGrey);

    /// <summary>Der Rueckweg von <see cref="ToDisplay"/>.</summary>
    private static float ToLight(float display)
    {
        // Bei 1 waere der Rueckweg unendlich. Knapp darunter abzufangen kostet nichts
        // und haelt eine Division durch null aus der inneren Schleife heraus.
        if (display <= 0f) return 0f;
        if (display >= 0.999999f) display = 0.999999f;

        return MiddleGrey * display / (1f - display);
    }

    /// <summary>
    /// Overlay und Soft Light, gerechnet im geliehenen Anzeigebereich.
    ///
    /// Beide Formeln stehen hier so, wie sie auch in der Beschreibung des
    /// PDF-Formats und in Photoshop stehen - der einzige Unterschied ist, dass die
    /// Werte vorher hin- und hinterher zurueckgerechnet werden. Hard Light braucht
    /// keine eigene Formel: es ist Overlay mit vertauschten Rollen, und zwei
    /// getrennt gepflegte Fassungen liefen frueher oder spaeter auseinander.
    /// </summary>
    private static float Contrast(float underLight, float overLight, bool hard)
    {
        float a = ToDisplay(underLight);
        float b = ToDisplay(overLight);

        float result;

        if (hard)
        {
            // Soft Light nach der Formel des W3C - stetig, ohne Knick bei 0,5.
            float d = a <= 0.25f ? ((16f * a - 12f) * a + 4f) * a : MathF.Sqrt(a);

            result = b <= 0.5f
                ? a - (1f - 2f * b) * a * (1f - a)
                : a + (2f * b - 1f) * (d - a);
        }
        else
        {
            result = a <= 0.5f
                ? 2f * a * b
                : 1f - 2f * (1f - a) * (1f - b);
        }

        return ToLight(result);
    }

    /// <summary>
    /// Die Auswahl, in der Reihenfolge der Aufzaehlung. Der Schluessel zeigt auf den
    /// uebersetzten Namen.
    /// </summary>
    public static readonly (BlendMode Mode, string Key)[] All =
    {
        (BlendMode.Normal, "S_BlendNormal"),
        (BlendMode.Add, "S_BlendAdd"),
        (BlendMode.Multiply, "S_BlendMultiply"),
        (BlendMode.Screen, "S_BlendScreen"),
        (BlendMode.Darken, "S_BlendDarken"),
        (BlendMode.Lighten, "S_BlendLighten"),
        (BlendMode.Difference, "S_BlendDifference"),
        (BlendMode.Overlay, "S_BlendOverlay"),
        (BlendMode.SoftLight, "S_BlendSoftLight"),
        (BlendMode.HardLight, "S_BlendHardLight"),
    };
}
