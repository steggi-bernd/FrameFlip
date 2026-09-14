using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Auf welcher Seite der Sichtumwandlung ein Werkzeug arbeitet.
///
/// Das ist die Frage, die bei Belichtung und Kontrast schon einmal anstand, und sie
/// stellt sich bei jedem weiteren Werkzeug erneut. Sie einmal je Werkzeug zu
/// beantworten und die Antwort mitzufuehren, ist billiger, als sie im Prozessor
/// siebenmal zu verzweigen - und sie steht damit dort, wo auch die Begruendung steht.
/// </summary>
public enum GradingStage
{
    /// <summary>
    /// Vor der Sichtumwandlung, auf linearem Szenenlicht mit Werten ueber 1.
    ///
    /// Hierher gehoert, was eine Lichtmenge meint: Belichtung, Weissabgleich. Solche
    /// Groessen sind Multiplikationen am Licht; hinter der Umwandlung angewandt
    /// wuerden sie ein fertiges Bild bearbeiten statt die Szene.
    /// </summary>
    SceneLinear,

    /// <summary>
    /// Nach der Sichtumwandlung, auf Anzeigewerten von 0 bis 1.
    ///
    /// Hierher gehoert, was eine Kurve biegt: Gradationskurven, Lift/Gamma/Gain,
    /// HSL, eine Look-Tabelle. Diese Werkzeuge brauchen ein definiertes Weiss, und
    /// das gibt es vor der Umwandlung nicht.
    /// </summary>
    Display,
}

/// <summary>
/// Ein Werkzeug der Farbkorrektur, das jeden Bildpunkt fuer sich behandelt.
///
/// Punktweise ist eine Einschraenkung und keine Nachlaessigkeit: Werkzeuge mit
/// oertlicher Wirkung - Klarheit, Schaerfe, Rauschminderung, Glanz - brauchen die
/// Nachbarschaft eines Punktes und passen nicht in diese Form. Sie bekommen einen
/// eigenen Weg, wenn sie an der Reihe sind. Alles, was hier hineinpasst, laesst sich
/// dagegen ohne Zwischenpuffer und ohne Reihenfolgeproblem hintereinanderschalten.
///
/// Die Kennung im gespeicherten Rezept entscheidet beim Lesen ueber den Typ. Sie
/// darf sich deshalb nie aendern, auch wenn die Klasse spaeter anders heisst -
/// sonst liest eine neue Fassung die Rezepte der alten nicht mehr.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(CurvesTool), CurvesTool.KindName)]
public interface IGradingTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    GradingStage Stage { get; }

    /// <summary>
    /// True, wenn nichts zu rechnen ist. Ein Werkzeug in Grundstellung darf den
    /// Durchgang nicht kosten - bei sieben Werkzeugen im Stapel summiert sich das
    /// sonst zu einem Vielfachen dessen, was tatsaechlich benutzt wird.
    /// </summary>
    bool IsNeutral { get; }

    /// <summary>
    /// Bereitet vor, was sich einmal je Bild statt einmal je Punkt rechnen laesst -
    /// eine Nachschlagetabelle, eine Matrix, vorberechnete Faktoren.
    ///
    /// Wird vor jedem Durchgang aufgerufen, nie waehrenddessen. Danach muss
    /// <see cref="Apply"/> von mehreren Threads gleichzeitig aufgerufen werden
    /// koennen, ohne dass etwas geschrieben wird.
    /// </summary>
    void Prepare();

    /// <summary>
    /// Rechnet einen Bildpunkt um. Muss nach <see cref="Prepare"/> threadsicher sein
    /// und darf nichts am Werkzeug veraendern.
    /// </summary>
    void Apply(ref float r, ref float g, ref float b);
}
