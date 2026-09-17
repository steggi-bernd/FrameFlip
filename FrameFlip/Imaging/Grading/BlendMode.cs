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

    // ---- die Nachzuegler aus Photoshop -------------------------------------
    //
    // Sie waren nicht von Anfang an da, weil der Stapel fuer Passe gebaut wurde und
    // ein Pass mit "Strahlendes Licht" nichts anfangen kann. Wer Bilder uebereinander
    // legt, vermisst sie sofort - und die Namen sind gelernt, nicht erfunden.

    /// <summary>Farbig abwedeln: hellt auf, indem es den Untergrund aufreisst.</summary>
    ColourDodge,

    /// <summary>Farbig nachbelichten: das Gegenstueck, dunkelt ueber die Tiefen.</summary>
    ColourBurn,

    /// <summary>Linear nachbelichten: abdunkeln durch Abziehen statt durch Teilen.</summary>
    LinearBurn,

    /// <summary>Lineares Licht: nachbelichten und abwedeln in einem, geradlinig.</summary>
    LinearLight,

    /// <summary>Strahlendes Licht: dasselbe ueber Abwedeln und Nachbelichten.</summary>
    VividLight,

    /// <summary>Lichtpunkt: ersetzt, was zu hell oder zu dunkel ist.</summary>
    PinLight,

    /// <summary>Ausschluss: wie Differenz, nur weicher in der Mitte.</summary>
    Exclusion,

    /// <summary>Subtrahieren. In linearem Licht das Wegnehmen einer Lichtmenge.</summary>
    Subtract,

    /// <summary>Dividieren. Das Gegenstueck zu Multiplizieren.</summary>
    Divide,

    // ---- die vier, die alle drei Kanaele zugleich brauchen ------------------

    /// <summary>Farbton der oberen, Saettigung und Helligkeit der unteren Ebene.</summary>
    Hue,

    /// <summary>Saettigung der oberen, Farbton und Helligkeit der unteren.</summary>
    Saturation,

    /// <summary>Farbton und Saettigung der oberen, Helligkeit der unteren.</summary>
    Colour,

    /// <summary>Helligkeit der oberen, Farbe der unteren. Das meistgebrauchte der vier.</summary>
    Luminosity,
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
        float mr, mg, mb;

        if (IsColourwise(mode))
        {
            // Die vier Farbmischungen brauchen alle drei Kanaele zugleich - und ein
            // Weiss, um "Helligkeit" ueberhaupt sagen zu koennen. Also geliehen, wie
            // bei den Kontrastmischungen.
            Colourwise(mode,
                       ToDisplay(ur), ToDisplay(ug), ToDisplay(ub),
                       ToDisplay(or_), ToDisplay(og), ToDisplay(ob),
                       out mr, out mg, out mb);

            mr = ToLight(mr);
            mg = ToLight(mg);
            mb = ToLight(mb);
        }
        else
        {
            mr = Channel(mode, ur, or_);
            mg = Channel(mode, ug, og);
            mb = Channel(mode, ub, ob);
        }

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

    /// <summary>
    /// Dieselbe Mischung, aber im ANZEIGERAUM gerechnet - so, wie Photoshop es tut.
    ///
    /// Der Unterschied ist nicht klein, und er faellt bei SCHWACHEN Beitraegen auf.
    /// Lineares Licht heisst dekodieren, mischen, wieder kodieren; die sRGB-Kurve
    /// staucht den dunklen Bereich und die Rueckkodierung dehnt ihn. Ein Beitrag mit
    /// kleiner Deckkraft kommt dadurch ungefaehr mit a hoch 0,42 heraus statt mit a -
    /// bei vier von 255 ist das der Faktor zehn. Eine freigestellte Ebene, deren
    /// durchsichtiger Bereich nicht genau durchsichtig ist, sieht in Photoshop
    /// deshalb sauber aus und in linearem Licht verrauscht.
    ///
    /// WAS ES KOSTET: Dieser Weg kennt kein Weiss darueber. Was unter der Ebene liegt,
    /// wird beim Kodieren auf 1 beschnitten - ein Glanzpass mit Wert 40 kommt als
    /// Weiss heraus und nicht als Glanzpass. Fuer eine Bildebene ueber einem Bild ist
    /// das genau richtig, fuer einen Pass ueber einem Pass ist es das Ende der
    /// Rekonstruktion. Deshalb entscheidet es JEDE EBENE fuer sich.
    /// </summary>
    public static void MixDisplay(BlendMode mode, float opacity,
                                  float ur, float ug, float ub,
                                  float or_, float og, float ob,
                                  out float r, out float g, out float b)
    {
        MixOn(mode, opacity,
              Srgb.Encode(ur), Srgb.Encode(ug), Srgb.Encode(ub),
              Srgb.Encode(or_), Srgb.Encode(og), Srgb.Encode(ob),
              out float mr, out float mg, out float mb);

        r = Srgb.Decode(mr);
        g = Srgb.Decode(mg);
        b = Srgb.Decode(mb);
    }

    /// <summary>
    /// Die Mischung auf Werten, die SCHON Anzeigewerte sind.
    ///
    /// Gebraucht an zwei Stellen: von <see cref="MixDisplay"/>, das nur den Hin- und
    /// Rueckweg drumherum legt, und von den Ebenen, die obenauf liegen - die rechnen
    /// ohnehin am fertigen Bild. Letztere riefen frueher die lineare Fassung auf und
    /// schickten Anzeigewerte hinein; die Kontrastmischungen bildeten sie dann ein
    /// zweites Mal ab, und ein Wasserzeichen auf Ineinanderkopieren sass daneben.
    /// </summary>
    public static void MixOn(BlendMode mode, float opacity,
                             float ar, float ag, float ab,
                             float br, float bg, float bb,
                             out float r, out float g, out float b)
    {
        float mr, mg, mb;

        if (IsColourwise(mode))
        {
            Colourwise(mode, ar, ag, ab, br, bg, bb, out mr, out mg, out mb);
        }
        else
        {
            mr = OnDisplay(mode, ar, br);
            mg = OnDisplay(mode, ag, bg);
            mb = OnDisplay(mode, ab, bb);
        }

        // Die Deckkraft wirkt hier ebenfalls im Anzeigeraum - das ist der ganze Sinn
        // des Schalters. Sie danach anzuwenden waere wieder die lineare Rechnung.
        if (opacity < 0.999f)
        {
            mr = ar + (mr - ar) * opacity;
            mg = ag + (mg - ag) * opacity;
            mb = ab + (mb - ab) * opacity;
        }

        r = Math.Clamp(mr, 0f, 1f);
        g = Math.Clamp(mg, 0f, 1f);
        b = Math.Clamp(mb, 0f, 1f);
    }

    // ------------------------------------------------ die vier mit allen Kanaelen

    /// <summary>Die Helligkeit eines Tripels, nach der Gewichtung des PDF-Formats.</summary>
    private static float Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

    /// <summary>
    /// Setzt die Helligkeit eines Tripels auf einen Zielwert - und haelt es dabei im
    /// Bereich.
    ///
    /// Das Verschieben allein reicht nicht: Eine gesaettigte Farbe, die aufgehellt
    /// wird, laeuft in einem Kanal ueber eins hinaus. Sie wird dann zur Helligkeit
    /// hin zusammengezogen, statt beschnitten zu werden - beschneiden aenderte den
    /// Farbton, und den soll diese Rechnung gerade erhalten.
    /// </summary>
    private static void SetLum(ref float r, ref float g, ref float b, float target)
    {
        float d = target - Lum(r, g, b);

        r += d;
        g += d;
        b += d;

        float l = target;
        float low = MathF.Min(r, MathF.Min(g, b));
        float high = MathF.Max(r, MathF.Max(g, b));

        if (low < 0f && l - low > 1e-6f)
        {
            float scale = l / (l - low);

            r = l + (r - l) * scale;
            g = l + (g - l) * scale;
            b = l + (b - l) * scale;
        }

        if (high > 1f && high - l > 1e-6f)
        {
            float scale = (1f - l) / (high - l);

            r = l + (r - l) * scale;
            g = l + (g - l) * scale;
            b = l + (b - l) * scale;
        }
    }

    /// <summary>Die Spanne zwischen dem hellsten und dem dunkelsten Kanal.</summary>
    private static float Sat(float r, float g, float b)
        => MathF.Max(r, MathF.Max(g, b)) - MathF.Min(r, MathF.Min(g, b));

    /// <summary>
    /// Zieht ein Tripel auf eine vorgegebene Spanne, ohne den Farbton zu aendern.
    ///
    /// Der mittlere Kanal behaelt dabei seine Lage zwischen den beiden anderen - das
    /// ist es, was "derselbe Farbton" hier heisst.
    /// </summary>
    private static void SetSat(ref float r, ref float g, ref float b, float target)
    {
        float low = MathF.Min(r, MathF.Min(g, b));
        float high = MathF.Max(r, MathF.Max(g, b));

        if (high - low < 1e-6f)
        {
            r = g = b = 0f;
            return;
        }

        float span = high - low;

        r = (r - low) / span * target;
        g = (g - low) / span * target;
        b = (b - low) / span * target;
    }

    /// <summary>
    /// Farbton, Saettigung, Farbe und Helligkeit - die vier Mischungen, die nicht
    /// Kanal fuer Kanal gehen.
    ///
    /// Die Formeln stehen in der Beschreibung des PDF-Formats und sind dieselben, die
    /// Photoshop benutzt. Sie lesen sich als Saetze: "Nimm den Farbton der oberen, die
    /// Saettigung und Helligkeit der unteren."
    /// </summary>
    private static void Colourwise(BlendMode mode,
                                   float ar, float ag, float ab,
                                   float br, float bg, float bb,
                                   out float r, out float g, out float b)
    {
        switch (mode)
        {
            case BlendMode.Hue:
                r = br; g = bg; b = bb;
                SetSat(ref r, ref g, ref b, Sat(ar, ag, ab));
                SetLum(ref r, ref g, ref b, Lum(ar, ag, ab));
                break;

            case BlendMode.Saturation:
                r = ar; g = ag; b = ab;
                SetSat(ref r, ref g, ref b, Sat(br, bg, bb));
                SetLum(ref r, ref g, ref b, Lum(ar, ag, ab));
                break;

            case BlendMode.Colour:
                r = br; g = bg; b = bb;
                SetLum(ref r, ref g, ref b, Lum(ar, ag, ab));
                break;

            default:
                r = ar; g = ag; b = ab;
                SetLum(ref r, ref g, ref b, Lum(br, bg, bb));
                break;
        }
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

        // Zwei, die in linearem Licht MEHR Sinn ergeben als im Anzeigeraum: Eine
        // Lichtmenge abzuziehen oder zu teilen ist eine Rechnung am Licht. Negatives
        // Licht gibt es nicht, deshalb der Boden bei null.
        BlendMode.Subtract => MathF.Max(0f, under - over),
        BlendMode.Divide => over <= 1e-4f ? under * 1e4f : under / over,

        // Alles Uebrige dreht sich um ein Weiss, und in linearem Licht gibt es
        // keines. Sie bekommen es geliehen - siehe ToDisplay - und rechnen dort mit
        // genau denselben Formeln wie im Anzeigeraum. Eine Formelsammlung, zwei
        // Eingaenge: Zwei Fassungen liefen frueher oder spaeter auseinander.
        _ => ToLight(OnDisplay(mode, ToDisplay(under), ToDisplay(over))),
    };

    /// <summary>
    /// Photoshops Formeln, auf Werten zwischen 0 und 1.
    ///
    /// Hier stehen sie so, wie sie in der Beschreibung des PDF-Formats und in
    /// Photoshop stehen. Wer im Anzeigeraum mischt, ruft sie direkt; wer in linearem
    /// Licht mischt, kommt ueber die geliehene Abbildung hierher.
    /// </summary>
    public static float OnDisplay(BlendMode mode, float a, float b) => mode switch
    {
        BlendMode.Normal => b,
        BlendMode.Add => a + b,
        BlendMode.Multiply => a * b,
        BlendMode.Screen => a + b - a * b,
        BlendMode.Darken => MathF.Min(a, b),
        BlendMode.Lighten => MathF.Max(a, b),
        BlendMode.Difference => MathF.Abs(a - b),

        BlendMode.Overlay => Hard(b, a),
        BlendMode.HardLight => Hard(a, b),
        BlendMode.SoftLight => Soft(a, b),

        BlendMode.ColourDodge => Dodge(a, b),
        BlendMode.ColourBurn => Burn(a, b),
        BlendMode.LinearBurn => a + b - 1f,
        BlendMode.LinearLight => a + 2f * b - 1f,

        // Strahlendes Licht ist nachbelichten unter der Mitte und abwedeln darueber -
        // dieselben zwei Formeln, nur mit verdoppeltem Regelweg.
        BlendMode.VividLight => b <= 0.5f ? Burn(a, 2f * b) : Dodge(a, 2f * (b - 0.5f)),

        BlendMode.PinLight => b <= 0.5f ? MathF.Min(a, 2f * b) : MathF.Max(a, 2f * b - 1f),
        BlendMode.Exclusion => a + b - 2f * a * b,

        BlendMode.Subtract => a - b,
        BlendMode.Divide => b <= 1e-4f ? (a > 0f ? 1f : 0f) : a / b,

        _ => b,
    };

    /// <summary>Abwedeln: Der Untergrund wird durch das Gegenstueck der oberen geteilt.</summary>
    private static float Dodge(float a, float b)
        => a <= 0f ? 0f : b >= 1f ? 1f : MathF.Min(1f, a / (1f - b));

    /// <summary>Nachbelichten: dasselbe von der anderen Seite.</summary>
    private static float Burn(float a, float b)
        => a >= 1f ? 1f : b <= 0f ? 0f : 1f - MathF.Min(1f, (1f - a) / b);

    /// <summary>Hartes Licht: Multiplizieren unter der Mitte, negativ multiplizieren darueber.</summary>
    private static float Hard(float a, float b)
        => b <= 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);

    /// <summary>Weiches Licht nach der Formel des W3C - stetig, ohne Knick bei 0,5.</summary>
    private static float Soft(float a, float b)
    {
        float d = a <= 0.25f ? ((16f * a - 12f) * a + 4f) * a : MathF.Sqrt(a);

        return b <= 0.5f
            ? a - (1f - 2f * b) * a * (1f - a)
            : a + (2f * b - 1f) * (d - a);
    }

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
    /// <remarks>
    /// Oeffentlich, weil dieselbe Abbildung inzwischen an drei Stellen gebraucht
    /// wird: hier fuer die Kontrastmischungen, bei den Helligkeitsmasken und bei den
    /// Einstellungsebenen. Sie dreimal aufzuschreiben hiesse, dass mittleres Grau
    /// eines Tages an zwei Stellen woanders liegt.
    /// </remarks>
    public static float ToDisplay(float light)
        => light <= 0f ? 0f : light / (light + MiddleGrey);

    /// <summary>Der Rueckweg von <see cref="ToDisplay"/>.</summary>
    public static float ToLight(float display)
    {
        // Bei 1 waere der Rueckweg unendlich. Knapp darunter abzufangen kostet nichts
        // und haelt eine Division durch null aus der inneren Schleife heraus.
        if (display <= 0f) return 0f;
        if (display >= 0.999999f) display = 0.999999f;

        return MiddleGrey * display / (1f - display);
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
        (BlendMode.ColourDodge, "S_BlendColourDodge"),
        (BlendMode.ColourBurn, "S_BlendColourBurn"),
        (BlendMode.LinearBurn, "S_BlendLinearBurn"),
        (BlendMode.LinearLight, "S_BlendLinearLight"),
        (BlendMode.VividLight, "S_BlendVividLight"),
        (BlendMode.PinLight, "S_BlendPinLight"),
        (BlendMode.Exclusion, "S_BlendExclusion"),
        (BlendMode.Subtract, "S_BlendSubtract"),
        (BlendMode.Divide, "S_BlendDivide"),
        (BlendMode.Hue, "S_BlendHue"),
        (BlendMode.Saturation, "S_BlendSaturation"),
        (BlendMode.Colour, "S_BlendColour"),
        (BlendMode.Luminosity, "S_BlendLuminosity"),
    };

    /// <summary>
    /// Ob diese Mischung alle drei Kanaele zugleich braucht.
    ///
    /// Farbton, Saettigung, Farbe und Helligkeit lassen sich nicht Kanal fuer Kanal
    /// rechnen: "der Farbton der oberen Ebene" ist eine Aussage ueber das Tripel. Sie
    /// gehen deshalb einen eigenen Weg durch die Mischung.
    /// </summary>
    public static bool IsColourwise(BlendMode mode)
        => mode is BlendMode.Hue or BlendMode.Saturation or BlendMode.Colour or BlendMode.Luminosity;
}
