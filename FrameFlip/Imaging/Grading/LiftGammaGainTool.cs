using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Ein Farbwert je Kanal, wie ihn ein Farbrad liefert.</summary>
public sealed class ColourTriplet
{
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }

    public ColourTriplet() { }

    public ColourTriplet(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    public bool Near(float reference, float tolerance = 0.001f)
        => MathF.Abs(R - reference) < tolerance &&
           MathF.Abs(G - reference) < tolerance &&
           MathF.Abs(B - reference) < tolerance;

    public ColourTriplet Clone() => new(R, G, B);
}

/// <summary>
/// Lift, Gamma und Gain - Schatten, Mitten und Lichter je fuer sich, und je Kanal
/// farbig.
///
/// Das Werkzeug der Farbkorrektur im Bewegtbild, und fuer Renderbilder brauchbarer
/// als die Tonwertkorrektur aus der Fotografie: Es trennt nicht nach Helligkeit mit
/// harten Grenzen, sondern gewichtet ueber den ganzen Bereich. Lift wirkt voll bei
/// Schwarz und gar nicht bei Weiss, Gain umgekehrt, Gamma dazwischen - drei Griffe,
/// die einander nicht ins Gehege kommen.
///
/// Damit lassen sich Dinge sagen, die eine Kurve nur umstaendlich ausdrueckt:
/// "Schatten kuehler, Lichter waermer" ist hier zwei Handgriffe und in Kurven sechs
/// Stuetzpunkte auf drei Kanaelen.
/// </summary>
public sealed class LiftGammaGainTool : IGradingTool
{
    public const string KindName = "liftgammagain";

    public string Kind => KindName;

    /// <summary>
    /// Nach der Sichtumwandlung. Lift und Gain beziehen sich auf Schwarz und Weiss,
    /// und beides gibt es erst dort - im linearen Szenenlicht ist nach oben offen.
    /// </summary>
    public GradingStage Stage => GradingStage.Display;

    /// <summary>Hebt die Schatten. 0 ist unveraendert; negativ senkt sie ab.</summary>
    public ColourTriplet Lift { get; set; } = new(0, 0, 0);

    /// <summary>Biegt die Mitten. 1 ist unveraendert.</summary>
    public ColourTriplet Gamma { get; set; } = new(1, 1, 1);

    /// <summary>Skaliert die Lichter. 1 ist unveraendert.</summary>
    public ColourTriplet Gain { get; set; } = new(1, 1, 1);

    [JsonIgnore]
    public bool IsNeutral => Lift.Near(0f) && Gamma.Near(1f) && Gain.Near(1f);

    private float _ir, _ig, _ib;   // Kehrwerte des Gammas, einmal je Bild

    public void Prepare()
    {
        _ir = 1f / MathF.Max(0.01f, Gamma.R);
        _ig = 1f / MathF.Max(0.01f, Gamma.G);
        _ib = 1f / MathF.Max(0.01f, Gamma.B);
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        r = Channel(r, Lift.R, Gain.R, _ir);
        g = Channel(g, Lift.G, Gain.G, _ig);
        b = Channel(b, Lift.B, Gain.B, _ib);
    }

    /// <summary>
    /// Die uebliche Form: Lift verschiebt mit abnehmendem Gewicht nach oben hin,
    /// Gain skaliert, Gamma biegt zuletzt.
    ///
    /// Der Faktor (1 - v) vor dem Lift ist das, was ihn zum Schattenregler macht -
    /// ohne ihn waere er ein Helligkeitsversatz ueber das ganze Bild und Weiss
    /// liefe sofort aus dem Bereich.
    /// </summary>
    private static float Channel(float v, float lift, float gain, float inverseGamma)
    {
        v = v + lift * (1f - v);
        v *= gain;
        v = Math.Clamp(v, 0f, 1f);

        return inverseGamma == 1f ? v : MathF.Pow(v, inverseGamma);
    }

    public LiftGammaGainTool Clone() => new()
    {
        Lift = Lift.Clone(),
        Gamma = Gamma.Clone(),
        Gain = Gain.Clone(),
    };
}

/// <summary>
/// Dynamik: hebt die Saettigung dort, wo noch wenig ist, und laesst kraeftige Farben
/// weitgehend in Ruhe.
///
/// Der Unterschied zur gewoehnlichen Saettigung ist der, den man im Bild sieht: Ein
/// Saettigungsregler treibt ohnehin kraeftige Flaechen als erstes aus dem
/// darstellbaren Bereich, waehrend die blassen Stellen, um die es eigentlich ging,
/// kaum vorankommen. Die Gewichtung dreht das um.
/// </summary>
public sealed class VibranceTool : IGradingTool
{
    public const string KindName = "vibrance";

    public string Kind => KindName;

    public GradingStage Stage => GradingStage.Display;

    private float _amount;

    /// <summary>-1 bis 1. 0 ist unveraendert, negativ nimmt zurueck.</summary>
    public float Amount
    {
        get => _amount;
        set => _amount = Math.Clamp(value, -1f, 1f);
    }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(_amount) < 0.001f;

    public void Prepare() { }

    public void Apply(ref float r, ref float g, ref float b)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));

        // Saettigung im Verhaeltnis zum staerksten Kanal, nicht als blosser Abstand.
        // Der Unterschied zaehlt in dunklen Bildteilen: ein tiefes Rot wie
        // (0,10 / 0,02 / 0,02) hat nur 0,08 Abstand und sieht damit blass aus,
        // waehrend es in Wahrheit fast voll gesaettigt ist. Ohne den Bezug wuerde
        // die Dynamik ausgerechnet dort am staerksten zulangen, wo am wenigsten
        // Platz ist.
        float saturation = max > 0.001f ? (max - min) / max : 0f;

        // Quadratisch gewichtet, nicht linear. Linear bleibt bei einer kraeftigen
        // Farbe noch genug Faktor uebrig, dass sie in absoluten Zahlen MEHR
        // dazugewinnt als eine blasse - sie startet ja viel hoeher. Damit waere das
        // Werkzeug eine umstaendliche Saettigung und nicht das, was es verspricht.
        float weight = 1f - saturation;
        weight *= weight;

        float factor = 1f + _amount * weight;

        float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;

        r = Math.Clamp(luma + (r - luma) * factor, 0f, 1f);
        g = Math.Clamp(luma + (g - luma) * factor, 0f, 1f);
        b = Math.Clamp(luma + (b - luma) * factor, 0f, 1f);
    }
}
