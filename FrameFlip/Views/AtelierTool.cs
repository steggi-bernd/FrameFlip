namespace FrameFlip.Views;

/// <summary>
/// Was die Maus im Bild tut.
///
/// Ein Werkzeug und nicht ein Zustand nebenbei. Bisher entschied die AUSWAHL im
/// Ebenenstreifen, ob ein Zug ins Bild etwas verschiebt, und ein Schalter im
/// Maskenbereich, ob ein Klick eine Kryptomatte waehlt. Beides war unsichtbar, beides
/// konnte gleichzeitig gelten, und niemand konnte es abschalten - daher der Rahmen,
/// der am falschen Platz sass, und der Zug, der die falsche Ebene erwischte. Das
/// waren keine zwei Fehler, sondern zweimal dieselbe fehlende Entscheidung.
///
/// Die Regel, die das aufloest: Das Bild tut, was das gewaehlte Werkzeug sagt, und
/// sonst nichts. Eine Ebene zu waehlen waehlt eine Ebene; es spannt nicht die Maus.
/// </summary>
public enum AtelierTool
{
    /// <summary>
    /// Verschieben, drehen, skalieren.
    ///
    /// Der Greifrahmen gehoert diesem Werkzeug und nicht der Auswahl: Er erscheint,
    /// solange es gewaehlt ist, und verschwindet sonst.
    /// </summary>
    Move,

    /// <summary>
    /// Waehlen: Ein Klick auf ein Objekt im Bild legt eine Kryptomatte an.
    ///
    /// Bisher lag das hinter einem Schalter im Maskenbereich - eine Betriebsart, die
    /// man einschalten konnte, ohne zu sehen, dass sie an ist.
    /// </summary>
    Select,

    /// <summary>Zuschneiden: an den Kanten ziehen, statt Zahlen einzutippen.</summary>
    Crop,

    /// <summary>Die Hand: den Ausschnitt schieben, statt an Rollbalken zu ziehen.</summary>
    Hand,

    /// <summary>
    /// Der Pinsel: eine Maske von Hand auftragen.
    ///
    /// Das einzige Werkzeug, mit dem sich "genau hier" sagen laesst. Alle anderen
    /// Maskenarten sind abgeleitet - aus einer Helligkeit, einem Pass, einem Objekt -
    /// und koennen die Frage deshalb nur ungefaehr beantworten.
    /// </summary>
    Brush,

    /// <summary>
    /// Die Pipette: einen Wert ablesen.
    ///
    /// Der Fall, der sie rechtfertigt, ist nicht das Ablesen einer Farbe, sondern die
    /// Scharfstellung: Die nachtraegliche Tiefenschaerfe verlangt eine Entfernung in
    /// Szeneneinheiten, und die ehrliche Art, sie zu beantworten, ist ein Klick auf
    /// das, was scharf sein soll.
    /// </summary>
    Pick,

    /// <summary>
    /// Knoten: Der Graph liegt ueber dem Bild. Ein anderes Werkzeug blendet ihn aus,
    /// und das Bild ist wieder frei - dieses holt ihn zurueck.
    /// </summary>
    Nodes,
}
