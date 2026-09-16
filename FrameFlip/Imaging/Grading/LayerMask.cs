using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Woher eine Maske ihren Wert nimmt.</summary>
public enum MaskKind
{
    /// <summary>Keine. Die Ebene wirkt ueberall gleich.</summary>
    None,

    /// <summary>Die Helligkeit der Ebene selbst - "nur wo dieser Pass hell ist".</summary>
    Luminance,

    /// <summary>Die Helligkeit dessen, was schon darunter liegt - "nur in den Schatten des Bildes".</summary>
    Underlying,

    /// <summary>Ein anderer Pass der Datei: Nebel, Verschattung, eine Indexmaske.</summary>
    Pass,

    /// <summary>Ein Verlauf ueber das Bild.</summary>
    Gradient,

    /// <summary>
    /// Eine Kryptomatte - Objekte oder Materialien, ueber die ganze Sequenz hinweg
    /// dieselben.
    /// </summary>
    Cryptomatte,
}

/// <summary>
/// Die Maske einer Ebene: ein Wert zwischen 0 und 1 je Bildpunkt, der sagt, wie
/// stark die Ebene dort wirkt.
///
/// Sie greift an genau einer Stelle an - sie multipliziert die Deckkraft. Damit gilt
/// fuer jede Mischung, jede Schnittmaske und jede Ebene dieselbe Regel, und es gibt
/// keinen Fall, in dem eine Maske "anders" wirkt. Eine Maske, die je nach Mischung
/// etwas anderes bedeutete, waere nicht zu erklaeren.
///
/// Flach statt als Typhierarchie: Die Arten teilen sich fast alle Felder - der
/// Bereich mit weichen Kanten gilt fuer Helligkeit, Untergrund und Pass gleichermassen.
/// Fuenf Klassen fuer acht Felder waeren mehr Geruest als Inhalt, und gespeichert
/// wird es so ohne Typkennung.
/// </summary>
public sealed class LayerMask
{
    public MaskKind Kind { get; set; }

    /// <summary>Dreht die Maske um. Aus "nur in den Lichtern" wird "ueberall ausser in den Lichtern".</summary>
    public bool Invert { get; set; }

    // ---------------------------------------------------------------- der Bereich

    /// <summary>Untere Grenze des Bereichs, der durchlaesst. 0 bis 1.</summary>
    public float Low { get; set; }

    /// <summary>Obere Grenze. 1 heisst: nach oben offen.</summary>
    public float High { get; set; } = 1f;

    /// <summary>
    /// Wie weit die Kanten ausgefranst sind, nach aussen gerechnet.
    ///
    /// Nach AUSSEN und nicht nach innen, damit die Grundstellung nichts tut: Bei 0
    /// bis 1 liegen beide Rampen ausserhalb des Wertebereichs, und die Maske laesst
    /// alles durch. Waeren sie nach innen gerichtet, waere schon die Grundstellung
    /// eine Abdunklung an beiden Enden - und niemand suchte den Grund bei der Maske.
    /// </summary>
    public float Softness { get; set; } = 0.1f;

    // ----------------------------------------------------------------- der Verlauf

    /// <summary>Richtung in Grad. 0 laeuft von links nach rechts, 90 von oben nach unten.</summary>
    public float Angle { get; set; } = 90f;

    /// <summary>Wo die Mitte des Uebergangs liegt, 0 bis 1 laengs der Richtung.</summary>
    public float Centre { get; set; } = 0.5f;

    /// <summary>Wie breit der Uebergang ist. 0 ist eine Kante.</summary>
    public float Width { get; set; } = 0.5f;

    // ------------------------------------------------------------------ die Quelle

    /// <summary>
    /// Der Pass, aus dem die Maske kommt - bei <see cref="MaskKind.Pass"/> und
    /// <see cref="MaskKind.Cryptomatte"/>. Sonst leer.
    /// </summary>
    public string Source { get; set; } = "";

    [JsonIgnore]
    public bool IsNeutral => Kind == MaskKind.None;

    /// <summary>Ob die Maske einen eigenen Pass braucht, der gelesen werden muss.</summary>
    [JsonIgnore]
    public bool NeedsSource
        => Kind is MaskKind.Pass or MaskKind.Cryptomatte && Source.Length > 0;

    public LayerMask Clone() => new()
    {
        Kind = Kind,
        Invert = Invert,
        Low = Low,
        High = High,
        Softness = Softness,
        Angle = Angle,
        Centre = Centre,
        Width = Width,
        Source = Source,
    };
}

/// <summary>Die Rechnung hinter einer Maske.</summary>
public static class Masking
{
    /// <summary>
    /// Ein Bereich mit weichen Kanten.
    ///
    /// Die Rampen liegen AUSSERHALB von [low, high] und laufen nach aussen auf null.
    /// Damit laesst die Grundstellung 0 bis 1 alles durch, egal wie weich sie
    /// eingestellt ist - und "nur die Lichter" ist ein Griff an einen Regler und
    /// nicht ein Abgleich zweier.
    /// </summary>
    public static float Band(float value, float low, float high, float softness)
    {
        softness = MathF.Max(0f, softness);

        float lower = value >= low
            ? 1f
            : softness <= 0f ? 0f : Smooth((value - (low - softness)) / softness);

        float upper = value <= high
            ? 1f
            : softness <= 0f ? 0f : Smooth(((high + softness) - value) / softness);

        return lower * upper;
    }

    /// <summary>
    /// Ein Schwarz- und ein Weisspunkt auf einem fertigen Maskenpass.
    ///
    /// Das ist bewusst NICHT <see cref="Band"/>. Ein Nebel- oder Verschattungspass
    /// ist bereits eine Maske - sein Wert IST der Anteil, und er gehoert
    /// durchgereicht, nicht durch ein Fenster gesehen. Mit dem Bereichsfenster
    /// darauf taete eine frisch gewaehlte Passmaske in Grundstellung gar nichts,
    /// weil jeder Wert zwischen 0 und 1 im Fenster 0 bis 1 liegt: eine Einstellung,
    /// die sich einschalten laesst und schweigt.
    ///
    /// Linear und nicht geglaettet, weil ein Schwarz- und ein Weisspunkt genau das
    /// sind, was jeder kennt, der schon einmal eine Maske angezogen hat - und weil
    /// 0 bis 1 dann wirklich unveraendert durchreicht.
    /// </summary>
    public static float Levels(float value, float low, float high)
    {
        if (high <= low) return value >= low ? 1f : 0f;

        return Math.Clamp((value - low) / (high - low), 0f, 1f);
    }

    /// <summary>
    /// Ein Verlauf ueber das Bild.
    ///
    /// Die Richtung wird in Bildkoordinaten gerechnet, in denen y nach UNTEN
    /// waechst - deshalb heisst 90 Grad "von oben nach unten" und nicht umgekehrt.
    /// Das ist die Richtung, in der jemand den Himmel abdunkelt, und damit die, die
    /// man erwartet.
    /// </summary>
    public static float Gradient(int x, int y, int width, int height,
                                 float cos, float sin, float from, float to)
    {
        float u = (x + 0.5f) / width - 0.5f;
        float v = (y + 0.5f) / height - 0.5f;

        float t = u * cos + v * sin + 0.5f;

        if (to <= from) return t >= from ? 1f : 0f;

        return Smooth((t - from) / (to - from));
    }

    /// <summary>
    /// Lineares Licht in einen Wert zwischen 0 und 1, auf dem sich "Schatten" und
    /// "Lichter" sagen laesst.
    ///
    /// Dieselbe Abbildung, die auch die Kontrastmischungen benutzen: mittleres Grau
    /// landet genau auf 0,5. Eine Maske "nur die Lichter" meint damit dasselbe, was
    /// das Auge meint, und nicht "alles ueber 0,5 Lichtmenge" - das waere schon fast
    /// das ganze Bild.
    /// </summary>
    public static float Perceptual(float light)
        => light <= 0f ? 0f : light / (light + Blending.MiddleGrey);

    /// <summary>Die glatte Stufe. Ohne sie haette jede Maske eine sichtbare Kante.</summary>
    private static float Smooth(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;

        return t * t * (3f - 2f * t);
    }
}
