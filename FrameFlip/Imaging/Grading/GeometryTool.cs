using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Werkzeug, das Bildpunkte VERSCHIEBT statt sie umzurechnen.
///
/// Die vierte und letzte Art. Alles andere im Atelier fragt "welche Farbe hat dieser
/// Punkt"; diese beiden fragen "welcher Punkt gehoert hierher". Damit brauchen sie
/// das ganze Bild als Quelle und koennen nicht an Ort und Stelle rechnen - wer den
/// Punkt schon ueberschrieben hat, den ein anderer gleich lesen will, hat ihn
/// verloren.
///
/// Beide sind radial: Sie ziehen zur Mitte hin oder von ihr weg, und zwar umso mehr,
/// je weiter aussen ein Punkt liegt. Das ist kein Zufall, sondern Optik - eine Linse
/// ist rund. Deshalb reicht als Auskunft eines Werkzeugs EIN FAKTOR je Kanal: Wo
/// dieser Punkt greift, ist derselbe Winkel und ein anderer Abstand.
///
/// Und deshalb genuegt ein Durchgang fuer beide. Die Faktoren mehrerer Werkzeuge
/// werden multipliziert, und erst dann wird einmal abgetastet. Zweimal abtasten
/// hiesse zweimal weichzeichnen - jede Abtastung kostet Schaerfe, und zwei davon
/// kosten sie doppelt, ohne dass irgendetwas dazukaeme.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(DistortionTool), DistortionTool.KindName)]
[JsonDerivedType(typeof(ChromaticTool), ChromaticTool.KindName)]
public interface IGeometryTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    bool IsNeutral { get; }

    void Prepare();

    /// <summary>
    /// Wie weit ein Punkt im Abstand <paramref name="radius"/> nach aussen greift -
    /// je Kanal einer. Eins heisst: genau hier.
    /// </summary>
    /// <param name="radius">Abstand von der Mitte: 0 in der Mitte, 1 in der Ecke.</param>
    void Factors(float radius, ref float red, ref float green, ref float blue);
}

/// <summary>
/// Linsenverzeichnung: die Tonne und das Kissen.
///
/// Ein Weitwinkel woelbt gerade Linien nach aussen, ein Tele zieht sie nach innen.
/// Beides ist derselbe Ausdruck mit anderem Vorzeichen, und beides wirkt auf alle
/// drei Kanaele gleich - es ist Geometrie, keine Farbe.
///
/// Der zweite Regler raeumt auf, was der erste anrichtet: Wer eine Tonne
/// geradezieht, holt den Rand ins Bild und hat aussen nichts mehr. Ein Massstab
/// unter eins vergroessert so weit, dass die leeren Ecken herausfallen. Das ist kein
/// Zusatz, sondern der zweite Handgriff derselben Korrektur - deshalb steht er
/// daneben und nicht in einem anderen Abschnitt.
/// </summary>
public sealed class DistortionTool : IGeometryTool
{
    public const string KindName = "distortion";

    public string Kind => KindName;

    /// <summary>-1 bis 1. Positiv zieht eine Tonne gerade, negativ ein Kissen.</summary>
    public float Amount { get; set; }

    /// <summary>Der Massstab. Unter eins vergroessert, ueber eins holt mehr ins Bild.</summary>
    public float Scale { get; set; } = 1f;

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.005f && MathF.Abs(Scale - 1f) < 0.005f;

    private float _amount;
    private float _scale;

    public void Prepare()
    {
        // Ein Drittel als voller Ausschlag: Darueber faltet sich das Bild in der
        // Ecke auf sich selbst, und dann ist nicht mehr zu sehen, was man tut.
        _amount = Math.Clamp(Amount, -1f, 1f) * 0.33f;
        _scale = Math.Clamp(Scale, 0.5f, 1.5f);
    }

    public void Factors(float radius, ref float red, ref float green, ref float blue)
    {
        float factor = (1f + _amount * radius * radius) * _scale;

        red *= factor;
        green *= factor;
        blue *= factor;
    }
}

/// <summary>
/// Chromatische Aberration: der farbige Saum an den Raendern.
///
/// Eine Linse bricht kurzes Licht staerker als langes, also treffen Blau und Rot
/// nicht an derselben Stelle auf. In der Mitte faellt das nicht auf, weil dort alle
/// Strahlen gerade durchgehen; nach aussen hin waechst der Versatz. Genau das tut ein
/// Faktor je Kanal von selbst: Der Versatz ist Abstand mal (Faktor minus eins) und
/// waechst mit dem Abstand.
///
/// Hinzufuegen ist ein Blick, wegnehmen eine Reparatur - mit demselben Regler in die
/// andere Richtung. Eine eigene Angabe fuer die Richtung braucht es nicht: Das
/// Vorzeichen IST die Richtung.
/// </summary>
public sealed class ChromaticTool : IGeometryTool
{
    public const string KindName = "chromatic";

    /// <summary>
    /// Ein volles Prozent bei vollem Ausschlag.
    ///
    /// Das klingt wenig und ist viel: Auf 1920 Punkten sind das am Rand zehn Punkte
    /// Versatz zwischen Rot und Blau. Echte Objektive liegen weit darunter, und wer
    /// hier ans Ende dreht, will einen Effekt und keine Linse.
    /// </summary>
    private const float FullSwing = 0.01f;

    public string Kind => KindName;

    /// <summary>-1 bis 1. Positiv schiebt Rot nach aussen, negativ nach innen.</summary>
    public float Amount { get; set; }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.005f;

    private float _amount;

    public void Prepare() => _amount = Math.Clamp(Amount, -1f, 1f) * FullSwing;

    public void Factors(float radius, ref float red, ref float green, ref float blue)
    {
        // Gruen bleibt stehen. Es traegt fast die ganze Helligkeit, und ein Bild, in
        // dem die Helligkeit wandert, waere verschoben statt farbgesaeumt.
        red *= 1f + _amount;
        blue *= 1f - _amount;
    }
}
