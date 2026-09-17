namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Die Farbkorrektur einer Einstellungsebene, fertig zum Rechnen.
///
/// Dieselbe Kette wie am Ende des Bildes - Belichtung, Saettigung, die linearen
/// Werkzeuge, dann Tonwerte und die uebrigen Werkzeuge - mit einem Unterschied, und
/// der ist wichtig genug, um hier zu stehen:
///
/// **Die Sichtumwandlung ist hier eine geliehene.** Am Ende des Bildes steht AgX: ein
/// 3D-Gitter, das einen Weg hat und keinen zurueck. Eine Einstellungsebene sitzt
/// aber MITTEN im Stapel - was sie ausgibt, wird darueber weiterverrechnet, und es
/// muss deshalb wieder lineares Licht sein. Sie benutzt darum dieselbe Abbildung wie
/// die Kontrastmischungen und die Helligkeitsmasken: x/(x+0,18) hinein, das
/// Ergebnis zurueck. Mittleres Grau liegt darin genau auf 0,5, und der Rueckweg ist
/// exakt - bis auf einen Deckel ganz oben, siehe <see cref="Cap"/>.
///
/// Folge, die man wissen muss: Eine Kurve auf einer Einstellungsebene greift auf
/// einer anderen Skala als dieselbe Kurve im Streifen darunter. Beide sind Kurven auf
/// einer Anzeigedarstellung, aber die eine wirkt waehrend des Zusammensetzens und die
/// andere am fertigen Bild. Das ist kein Mangel, sondern derselbe Unterschied, den
/// jedes Programm mit Knoten auch hat - nur wird er hier ausgesprochen.
/// </summary>
public readonly struct LayerGrade
{
    private LayerGrade(float gain, float saturation, float black, float span,
                       float inverseGamma, float contrast, bool needsTone,
                       IGradingTool[] linear, IGradingTool[] display)
    {
        Gain = gain;
        Saturation = saturation;
        Black = black;
        Span = span;
        InverseGamma = inverseGamma;
        Contrast = contrast;
        NeedsTone = needsTone;
        Linear = linear;
        Display = display;
    }

    private readonly float Gain, Saturation, Black, Span, InverseGamma, Contrast;
    private readonly bool NeedsTone;
    private readonly IGradingTool[] Linear, Display;

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    public bool IsNeutral
        => !NeedsTone && Same(Gain, 1f) && Same(Saturation, 1f) &&
           (Linear is null || Linear.Length == 0) &&
           (Display is null || Display.Length == 0);

    /// <summary>
    /// Bereitet die Kette vor. Danach darf <see cref="Apply"/> von mehreren Threads
    /// gerufen werden - derselbe Vertrag, den die Werkzeuge ohnehin zusichern.
    /// </summary>
    public static LayerGrade Prepare(ImageAdjustments? adjustments, GradingStack? stack)
    {
        var settings = (adjustments ?? ImageAdjustments.Neutral).Clamped();
        var prepared = stack?.Prepare() ?? PreparedGrading.None;

        float black = (float)settings.BlackPoint;
        float white = (float)settings.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)settings.Gamma);
        float contrast = (float)settings.Contrast;

        bool needsTone = !Same(black, 0f) || !Same(white, 1f) ||
                         !Same(inverseGamma, 1f) || !Same(contrast, 1f);

        return new LayerGrade((float)Math.Pow(2.0, settings.Exposure), (float)settings.Saturation,
                              black, span, inverseGamma, contrast, needsTone,
                              prepared.SceneLinear ?? Array.Empty<IGradingTool>(),
                              prepared.Display ?? Array.Empty<IGradingTool>());
    }

    /// <summary>Rechnet einen Bildpunkt um - lineares Licht hinein, lineares Licht heraus.</summary>
    public void Apply(ref float r, ref float g, ref float b)
    {
        // --- lineare Seite ---

        if (Gain != 1f)
        {
            r *= Gain;
            g *= Gain;
            b *= Gain;
        }

        if (!Same(Saturation, 1f))
        {
            float luma = LumaR * r + LumaG * g + LumaB * b;
            r = luma + (r - luma) * Saturation;
            g = luma + (g - luma) * Saturation;
            b = luma + (b - luma) * Saturation;

            // Uebersaettigung kann unter null druecken; negatives Licht gibt es nicht.
            if (r < 0) r = 0;
            if (g < 0) g = 0;
            if (b < 0) b = 0;
        }

        var linear = Linear;
        for (int t = 0; t < linear.Length; t++) linear[t].Apply(ref r, ref g, ref b);

        var display = Display;
        if (!NeedsTone && display.Length == 0) return;

        // --- geliehene Anzeigeseite ---

        // Was der Punkt vorher an Licht hatte - gebraucht fuer den Deckel am Ende.
        float wasR = r, wasG = g, wasB = b;

        r = Blending.ToDisplay(r);
        g = Blending.ToDisplay(g);
        b = Blending.ToDisplay(b);

        if (NeedsTone)
        {
            r = Tone(r);
            g = Tone(g);
            b = Tone(b);
        }

        for (int t = 0; t < display.Length; t++) display[t].Apply(ref r, ref g, ref b);

        r = Cap(Blending.ToLight(r), wasR);
        g = Cap(Blending.ToLight(g), wasG);
        b = Cap(Blending.ToLight(b), wasB);
    }

    /// <summary>
    /// Der Deckel auf dem Rueckweg - und der Grund, warum es ihn geben muss.
    ///
    /// Ein Anzeigewerkzeug klemmt bei 1, und das ist sein gutes Recht: Dort ist
    /// Weiss. Die geliehene Abbildung kennt aber kein Weiss - sie bildet Unendlich
    /// auf 1 ab, und der Rueckweg 0,18*d/(1-d) laeuft entsprechend davon. Wer die
    /// Lichter einer Einstellungsebene verdoppelt, bekommt aus mittlerem Grau nicht
    /// das Doppelte, sondern das Hunderttausendfache.
    ///
    /// Sichtbar wird das nicht sofort - Weiss bleibt Weiss -, sondern ueberall dort,
    /// wo spaeter gemittelt wird: Ein einziger solcher Punkt zieht einen Glanzschein
    /// ueber das halbe Bild, faerbt jede Unschaerfe und verschiebt die
    /// Tonwertabbildung. Die Ebene sieht dann kaputt aus statt heller.
    ///
    /// Sechs Blenden ueber dem, was der Punkt vorher hatte - mindestens ueber
    /// mittlerem Grau, sonst koennte eine dunkle Stelle nie hell werden. Was ein
    /// Werkzeug NICHT anfasst, geht unveraendert durch: Eine Sonne bleibt eine Sonne.
    /// </summary>
    private static float Cap(float light, float before)
        => MathF.Min(light, MathF.Max(before, Blending.MiddleGrey) * Headroom);

    /// <summary>Sechs Blenden. Genug fuer jede Aufhellung, endlich genug zum Rechnen.</summary>
    private const float Headroom = 64f;

    private float Tone(float v)
    {
        v = Math.Clamp((v - Black) / Span, 0f, 1f);
        if (!Same(InverseGamma, 1f)) v = MathF.Pow(v, InverseGamma);
        if (!Same(Contrast, 1f)) v = (v - 0.5f) * Contrast + 0.5f;

        return v;
    }

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    private static bool Same(float value, float reference) => MathF.Abs(value - reference) < 0.001f;
}
