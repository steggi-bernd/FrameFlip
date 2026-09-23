namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Eine Maske, fertig zum Fragen: einmal je Bild vorbereitet, je Bildpunkt gefragt.
///
/// Steht fuer sich, weil zwei Wege sie fragen - der Composer des Ebenenstapels und der
/// Maskenknoten im Graphen. Zweimal abgeschrieben liefe sie beim naechsten Maskentyp
/// auseinander, und dann saehe eine in Knoten umgewandelte Einstellung anders aus als
/// der Stapel, aus dem sie kam. Das waere genau die Art Unterschied, die man erst nach
/// dreihundert Bildern bemerkt.
/// </summary>
internal readonly struct MaskSampler
{
    // Rec.-709-Luminanz, dieselben Gewichte wie im uebrigen Bildweg.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Welche Maske gefragt wird - nach der Vorbereitung. Fehlt ihr Pass oder hat eine
    /// Kryptomatte nichts ausgewaehlt, steht hier <see cref="MaskKind.None"/>.
    /// </summary>
    public MaskKind Kind { get; }

    /// <summary>Ob die Maske die Sichtbarkeit oder die Korrektur begrenzt.</summary>
    public MaskScope Scope { get; }

    private readonly FloatFrame? MaskFrame;
    private readonly FloatFrame[]? MaskLevels;
    private readonly float[]? MaskIds;
    private readonly bool MaskInvert;

    /// <summary>Der Anstrich, der fuer dieses Bild gilt - siehe LayerMask.</summary>
    private readonly PaintedMask? Paint;

    private readonly float MaskLow, MaskHigh, MaskSoftness;

    /// <summary>Der gesuchte Farbton und seine Weite - nur fuer die Farbbereichsmaske.</summary>
    private readonly float MaskHue, MaskSpread;

    private readonly float MaskFloor, MaskSpan;
    private readonly float GradientCos, GradientSin, GradientFrom, GradientTo;

    private MaskSampler(LayerMask mask, MaskKind kind, FloatFrame? maskFrame, FloatFrame[]? maskLevels,
                        float[]? maskIds, float maskFloor, float maskSpan, PaintedMask? paint)
    {
        Kind = kind;
        Scope = mask.Scope;
        MaskFrame = maskFrame;
        MaskLevels = maskLevels;
        MaskIds = maskIds;
        MaskFloor = maskFloor;
        MaskSpan = maskSpan;
        Paint = paint;

        MaskInvert = mask.Invert;
        MaskHue = mask.Hue;
        MaskSpread = mask.Spread;
        MaskLow = mask.Low;
        MaskHigh = mask.High;
        MaskSoftness = mask.Softness;

        // Winkel und Breite einmal je Bild in das umrechnen, was die innere
        // Schleife braucht - bei 4K waeren es sonst 25 Millionen Sinusse.
        float radians = mask.Angle * MathF.PI / 180f;
        GradientCos = MathF.Cos(radians);
        GradientSin = MathF.Sin(radians);

        float half = MathF.Max(0f, mask.Width) / 2f;
        GradientFrom = mask.Centre - half;
        GradientTo = mask.Centre + half;
    }

    /// <summary>
    /// Bereitet eine Maske fuer ein Bild dieser Groesse vor.
    /// </summary>
    /// <param name="number">
    /// Die Bildnummer - fuer gemalte Masken, die je Bild einen eigenen Anstrich haben.
    /// </param>
    public static MaskSampler Prepare(LayerMask mask, IReadOnlyDictionary<string, FloatFrame> sources,
                                      int width, int height, int number)
    {
        // Der Pass, aus dem die Maske liest. Fehlt er, faellt die Maske weg -
        // eine Ebene ganz verschwinden zu lassen, weil ihre Maske nicht gelesen
        // werden konnte, waere die falsche Antwort auf eine fehlende Datei.
        FloatFrame? maskFrame = null;
        FloatFrame[]? maskLevels = null;
        float[]? maskIds = null;
        var maskKind = mask.Kind;

        float maskFloor = 0f, maskSpan = 1f;

        if (maskKind == MaskKind.Pass)
        {
            if (sources.TryGetValue(mask.Source, out var found) &&
                found.Width == width && found.Height == height)
            {
                maskFrame = found;

                // Ein Pass, der ohnehin zwischen 0 und 1 liegt, geht unveraendert
                // ein - er IST die Maske. Einer, der darueber hinausgeht, ist
                // eine Groesse in eigenen Einheiten: eine Tiefe in Metern. Der
                // wird auf seine eigene Spanne bezogen, sonst waere alles ueber
                // eins voll gedeckt und der Regler ohne Wirkung.
                var (low, high) = found.MaskRange;

                if (high > 1.0001f || low < -0.0001f)
                {
                    maskFloor = low;
                    maskSpan = MathF.Max(1e-6f, high - low);
                }
            }
            else
            {
                maskKind = MaskKind.None;
            }
        }
        else if (maskKind == MaskKind.Cryptomatte)
        {
            var levels = new List<FloatFrame>();

            foreach (string level in mask.Levels)
            {
                if (sources.TryGetValue(level, out var found) &&
                    found.Width == width && found.Height == height)
                {
                    levels.Add(found);
                }
            }

            maskLevels = levels.ToArray();
            maskIds = mask.Picks.Select(p => p.Id).ToArray();

            // Ohne Stufen oder ohne Auswahl gibt es nichts zu maskieren. Die
            // Maske fallen zu lassen ist hier die richtige Antwort: Eine leere
            // Auswahl liesse die Ebene ganz verschwinden, und das saehe aus wie
            // ein Fehler statt wie "es ist noch nichts ausgewaehlt".
            if (maskLevels.Length == 0 || maskIds.Length == 0) maskKind = MaskKind.None;
        }

        return new MaskSampler(mask, maskKind, maskFrame, maskLevels, maskIds, maskFloor, maskSpan,
                               mask.PaintFor(number));
    }

    /// <summary>
    /// Der Maskenwert eines Bildpunkts, zwischen 0 und 1.
    /// </summary>
    /// <param name="lr">
    /// Die Ebene selbst, bereits mit Belichtung und Farbe - bei einer
    /// Einstellungsebene also das korrigierte Ergebnis. Eine Helligkeitsmaske auf ihr
    /// fragt damit "wo ist es NACH der Korrektur hell", und das ist die Frage, die
    /// man beim Hinsehen stellt.
    /// </param>
    /// <param name="ur">Was an dieser Stelle schon darunter liegt.</param>
    public float Factor(int x, int y, int width, int height, int i,
                        float lr, float lg, float lb,
                        float ur, float ug, float ub)
    {
        float value;

        switch (Kind)
        {
            case MaskKind.Luminance:
                value = Masking.Perceptual(LumaR * lr + LumaG * lg + LumaB * lb);
                break;

            case MaskKind.Underlying:
                value = Masking.Perceptual(LumaR * ur + LumaG * ug + LumaB * ub);
                break;

            case MaskKind.Pass:
                // Ein anderer Weg als bei der Helligkeit, und das mit Absicht.
                //
                // Nebel, Verschattung und Indexmasken sind bereits Masken: Ihr Wert
                // IST der Anteil. Er wird durchgereicht und bekommt nur einen
                // Schwarz- und einen Weisspunkt, wie jede Maske, die man anzieht.
                // Durch das Bereichsfenster der Helligkeitsmaske geschickt taete er
                // in Grundstellung nichts - jeder Wert zwischen 0 und 1 liegt im
                // Fenster 0 bis 1.
                //
                // Ueber die Luminanz und nicht ueber Rot allein: Bei einem
                // Graustufenpass sind beide identisch, bei einem farbigen waere Rot
                // eine willkuerliche Wahl.
                var m = MaskFrame!;
                float raw = LumaR * m.R[i] + LumaG * m.G[i] + LumaB * m.B[i];

                // Auf die eigene Spanne bezogen, wenn der Pass keine ist. Bei einem
                // Nebelpass ist der Boden null und die Spanne eins - dann steht hier
                // derselbe Wert wie vorher.
                raw = (raw - MaskFloor) / MaskSpan;

                return Fit(Masking.Levels(raw, MaskLow, MaskHigh), MaskInvert);

            case MaskKind.Cryptomatte:
                // Dieselbe Behandlung wie beim Pass: Die Deckung IST der Anteil, und
                // Schwarz- und Weisspunkt ziehen ihn an - damit laesst sich eine
                // weiche Kante wegnehmen oder stehenlassen.
                float coverage = Masking.Coverage(MaskLevels!, MaskIds!, i);

                return Fit(Masking.Levels(coverage, MaskLow, MaskHigh), MaskInvert);

            case MaskKind.Painted:
                // Der Anstrich IST der Anteil - wie beim Pass und bei der Kryptomatte.
                // Schwarz- und Weisspunkt ziehen ihn an, damit sich eine weiche
                // Pinselkante nachtraeglich haerten oder weiter aufweichen laesst.
                if (Paint is not { } paint) return MaskInvert ? 1f : 0f;

                return Fit(Masking.Levels(paint.At(x, y), MaskLow, MaskHigh),
                           MaskInvert);

            case MaskKind.Colour:
            {
                // Gelesen wird der UNTERGRUND, nicht die Ebene selbst - und das ist
                // der Unterschied zur Helligkeitsmaske.
                //
                // Zwei Gruende. Eine weisse Flaeche hat keinen Farbton; eine
                // Farbbereichsmaske auf ihr waere immer leer, und niemand kaeme
                // darauf, warum. Und eine Einstellungsebene, die den Farbton
                // verschiebt, jagte ihrem eigenen Ergebnis hinterher: Die Maske
                // waehlt Blau, die Korrektur macht daraus Gruen, die Maske waehlt es
                // nicht mehr. "Wo das Bild blau IST" ist die Frage, die man stellt.
                //
                // Der Farbton des Punktes auf dem Farbkreis - und wie weit er vom
                // gesuchten entfernt liegt. Die Entfernung geht ueber den KUERZEREN
                // Weg: Rot liegt bei 0 und bei 360, und ein Bereich um Rot, der bei
                // 350 aufhoert, waere keiner.
                float high = MathF.Max(ur, MathF.Max(ug, ub));
                float low = MathF.Min(ur, MathF.Min(ug, ub));
                float chroma = high - low;

                // Grau hat keinen Farbton, den man treffen koennte. Nicht "weit weg",
                // sondern "gar nicht dabei" - sonst faenge jede Maske das halbe Bild.
                if (chroma <= 1e-6f || high <= 1e-6f) return Fit(0f, MaskInvert);

                float hue = high == ur
                    ? 60f * (((ug - ub) / chroma) % 6f)
                    : high == ug
                        ? 60f * ((ub - ur) / chroma + 2f)
                        : 60f * ((ur - ug) / chroma + 4f);

                if (hue < 0f) hue += 360f;

                float away = MathF.Abs(hue - MaskHue);
                if (away > 180f) away = 360f - away;

                // Weich ueber die Kante hinaus. Der Weichzeichner ist derselbe Regler
                // wie bei der Helligkeitsmaske und zaehlt hier in halben Kreisen.
                float edge = MaskSoftness * 180f;

                float inside = edge <= 1e-4f
                    ? away <= MaskSpread ? 1f : 0f
                    : 1f - Math.Clamp((away - MaskSpread) / edge, 0f, 1f);

                // Je blasser die Farbe, desto weniger gehoert sie dazu. Ein fast
                // graues Blau IST kaum blau - und wer es doch will, zieht den
                // Weisspunkt herunter.
                float pure = Math.Clamp(chroma / high, 0f, 1f);

                return Fit(Masking.Levels(inside * pure, MaskLow, MaskHigh),
                           MaskInvert);
            }

            case MaskKind.Gradient:
                return Fit(Masking.Gradient(x, y, width, height,
                                            GradientCos, GradientSin,
                                            GradientFrom, GradientTo), MaskInvert);

            default:
                return 1f;
        }

        return Fit(Masking.Band(value, MaskLow, MaskHigh, MaskSoftness),
                   MaskInvert);
    }

    private static float Fit(float factor, bool invert) => invert ? 1f - factor : factor;
}
