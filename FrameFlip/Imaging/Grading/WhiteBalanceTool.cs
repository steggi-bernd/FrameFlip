using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Weissabgleich ueber Farbtemperatur und Tendenz.
///
/// Gerechnet wird als echte chromatische Anpassung nach Bradford, nicht als
/// Verschiebung der drei Kanaele gegeneinander. Der Unterschied faellt dort auf, wo
/// es zaehlt: Ein Rotstich, den man mit einem blauen Kanalversatz herausnimmt,
/// hinterlaesst in gesaettigten Toenen einen Farbdreh, den niemand bestellt hat.
/// Die Anpassung skaliert stattdessen die drei Zapfenantworten, also das, was das
/// Auge beim Wechsel der Beleuchtung selbst tut.
///
/// **Zur Richtung des Reglers**, die regelmaessig fuer Verwirrung sorgt: Der Wert
/// gibt an, unter welchem Licht die Aufnahme entstanden ist, und korrigiert auf
/// neutral. Eine hohe Zahl heisst also "das Licht war kalt" - und die Korrektur
/// macht das Bild waermer. Nach rechts wird es gelber, wie man es aus Lightroom
/// kennt, auch wenn die Zahl daneben physikalisch das Gegenteil benennt.
/// </summary>
public sealed class WhiteBalanceTool : IGradingTool
{
    public const string KindName = "whitebalance";

    public string Kind => KindName;

    /// <summary>
    /// Vor der Sichtumwandlung. Der Weissabgleich beschreibt die Beleuchtung einer
    /// Szene, also eine Groesse des Lichts - hinter der Umwandlung waere es eine
    /// Einfaerbung des fertigen Bildes.
    /// </summary>
    public GradingStage Stage => GradingStage.SceneLinear;

    /// <summary>Die Referenz: D65, der Weisspunkt von Rec.709.</summary>
    public const float NeutralKelvin = 6500f;

    private float _kelvin = NeutralKelvin;
    private float _tint;
    private float[]? _matrix;

    /// <summary>Farbtemperatur in Kelvin, 1667 bis 25000. 6500 ist unveraendert.</summary>
    public float Kelvin
    {
        get => _kelvin;
        set => _kelvin = Math.Clamp(value, 1667f, 25000f);
    }

    /// <summary>
    /// Tendenz senkrecht zur Temperatur, -100 bis 100. Negativ zieht nach Gruen,
    /// positiv nach Magenta - die Achse, auf der Leuchtstofflicht danebenliegt,
    /// ohne dass die Temperatur daran etwas aendern koennte.
    /// </summary>
    public float Tint
    {
        get => _tint;
        set => _tint = Math.Clamp(value, -100f, 100f);
    }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(_kelvin - NeutralKelvin) < 1f && MathF.Abs(_tint) < 0.5f;

    public void Prepare()
    {
        if (IsNeutral)
        {
            _matrix = null;
            return;
        }

        var (x, y) = Colorimetry.PlanckianXy(_kelvin);

        // Die Tendenz verschiebt die Farbart quer zur Temperaturachse. Der Faktor
        // ist so gewaehlt, dass der volle Ausschlag etwa dem entspricht, was an
        // Leuchtstofflicht auszugleichen ist - eine Konvention, keine Naturkonstante.
        y += _tint * 0.00025f;

        var sourceWhite = Colorimetry.XyToXyz(x, y);

        // Von der Beleuchtung der Aufnahme auf die Referenz: das ist die Richtung,
        // die aus "das Licht war kalt" ein waermeres Bild macht.
        _matrix = Colorimetry.Adaptation(sourceWhite, Colorimetry.D65);
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        var m = _matrix;
        if (m is null) return;

        float nr = m[0] * r + m[1] * g + m[2] * b;
        float ng = m[3] * r + m[4] * g + m[5] * b;
        float nb = m[6] * r + m[7] * g + m[8] * b;

        // Eine Anpassung kann einzelne Kanaele unter null druecken, wenn die Farbe
        // ausserhalb des Zielraums liegt. Negatives Licht gibt es nicht, und die
        // Sichtumwandlung koennte damit nichts anfangen.
        r = nr > 0 ? nr : 0;
        g = ng > 0 ? ng : 0;
        b = nb > 0 ? nb : 0;
    }
}
