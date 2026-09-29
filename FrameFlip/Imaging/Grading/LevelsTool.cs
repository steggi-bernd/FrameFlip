using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Tonwertkorrektur (W2 im Werkzeugplan): Schwarz-, Grau- und Weisspunkt des Eingangs und der
/// Bereich des Ausgangs - fuer alle Kanaele gemeinsam und je Kanal, bedient an Anfassern
/// direkt unter dem Histogramm.
///
/// Dasselbe liesse sich mit einer Kurve biegen, aber nicht so ablesen: Hier sieht man, wo
/// das Bild anfaengt und aufhoert, und zieht den Punkt genau dorthin. Wie bei den Kurven
/// erst die gemeinsame Einstellung, dann die je Kanal - ein Farbstich, der ueber einen Kanal
/// ausgeglichen wurde, bleibt, wenn man danach den gemeinsamen Kontrast aendert.
/// </summary>
public sealed class LevelsTool : IGradingTool
{
    public const string KindName = "levels";

    public string Kind => KindName;

    /// <summary>
    /// Nach der Sichtumwandlung, wie die Kurven: Schwarz- und Weisspunkt sind Punkte auf dem
    /// Weg von Schwarz nach Weiss, und den gibt es erst in der Anzeige.
    /// </summary>
    public GradingStage Stage => GradingStage.Display;

    public LevelsChannel Master { get; set; } = new();

    public LevelsChannel Red { get; set; } = new();

    public LevelsChannel Green { get; set; } = new();

    public LevelsChannel Blue { get; set; } = new();

    [JsonIgnore]
    public bool IsNeutral => Master.IsNeutral && Red.IsNeutral && Green.IsNeutral && Blue.IsNeutral;

    public void Prepare()
    {
        Master.Prepare();
        Red.Prepare();
        Green.Prepare();
        Blue.Prepare();
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        r = Master.Map(r);
        g = Master.Map(g);
        b = Master.Map(b);

        r = Red.Map(r);
        g = Green.Map(g);
        b = Blue.Map(b);
    }

    /// <summary>Der Kanal 0 gemeinsam, 1 Rot, 2 Gruen, 3 Blau.</summary>
    public LevelsChannel Channel(int index) => index switch
    {
        1 => Red,
        2 => Green,
        3 => Blue,
        _ => Master,
    };

    public LevelsTool Clone() => new()
    {
        Master = Master.Clone(),
        Red = Red.Clone(),
        Green = Green.Clone(),
        Blue = Blue.Clone(),
    };
}

/// <summary>
/// Ein Kanal der Tonwertkorrektur. Alle Werte auf der Skala 0 bis 1 der Anzeige; die Karte
/// zeigt sie als 0 bis 255, wie man es aus jedem Bildprogramm kennt.
/// </summary>
public sealed class LevelsChannel
{
    /// <summary>Was darunter liegt, wird Schwarz.</summary>
    public float InBlack { get; set; }

    /// <summary>Was darueber liegt, wird Weiss.</summary>
    public float InWhite { get; set; } = 1;

    /// <summary>
    /// Die Mitten: 1 laesst sie, groesser hellt auf, kleiner dunkelt ab - wie der mittlere Wert
    /// in Photoshop. Der Grauregler zeigt dasselbe als Lage zwischen Schwarz und Weiss.
    /// </summary>
    public float Gamma { get; set; } = 1;

    /// <summary>Wohin Schwarz faellt - hoeher heisst ein flacheres, milchiges Schwarz.</summary>
    public float OutBlack { get; set; }

    /// <summary>Wohin Weiss faellt.</summary>
    public float OutWhite { get; set; } = 1;

    /// <summary>Die kleinste Spanne zwischen Schwarz und Weiss - sonst teilte die Rechnung durch null.</summary>
    public const float MinimumSpan = 1f / 255f;

    [JsonIgnore]
    public bool IsNeutral => InBlack == 0 && InWhite == 1 && Gamma == 1 && OutBlack == 0 && OutWhite == 1;

    private bool _neutral = true;
    private float _scale = 1, _power = 1;

    public void Prepare()
    {
        _neutral = IsNeutral;
        _scale = 1f / Math.Max(MinimumSpan, InWhite - InBlack);
        _power = 1f / Math.Clamp(Gamma, 0.05f, 20f);
    }

    /// <summary>Ein Wert durch diesen Kanal. <see cref="Prepare"/> muss vorher gelaufen sein.</summary>
    public float Map(float value)
    {
        if (_neutral) return value;

        float t = Math.Clamp((value - InBlack) * _scale, 0f, 1f);
        if (_power != 1f) t = MathF.Pow(t, _power);

        return OutBlack + t * (OutWhite - OutBlack);
    }

    /// <summary>
    /// Die Lage des Graureglers zwischen Schwarz und Weiss, 0 bis 1, zu einem Gamma: der
    /// Eingang, der nach der Korrektur mittleres Grau wird. 0,5 heisst Gamma 1.
    /// </summary>
    public static float GrayPosition(float gamma) => MathF.Pow(0.5f, Math.Clamp(gamma, 0.05f, 20f));

    /// <summary>Das Gamma zu einer Lage des Graureglers - die Umkehrung von <see cref="GrayPosition"/>.</summary>
    public static float GammaAt(float position)
        => Math.Clamp(MathF.Log(Math.Clamp(position, 0.01f, 0.99f)) / MathF.Log(0.5f), 0.05f, 20f);

    public LevelsChannel Clone() => new()
    {
        InBlack = InBlack,
        InWhite = InWhite,
        Gamma = Gamma,
        OutBlack = OutBlack,
        OutWhite = OutWhite,
    };
}
