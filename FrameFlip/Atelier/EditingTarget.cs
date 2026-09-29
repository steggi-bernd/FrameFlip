using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Atelier;

/// <summary>
/// Woran gerade gearbeitet wird - das Bearbeitungsziel (docs/Atelier-Arbeitsablauf.md,
/// Abschnitt 5; Refactoring-Studio, S2 zweiter Teil).
///
/// Vorher bestimmte jede Stelle ihr Ziel selbst: der Farbstreifen aus der gewaehlten Ebene und
/// dem Schalter "Bild/Ebene", der Pinsel, das Waehlen im Bild und die Werkzeugleiste aus dem
/// gewaehlten Knoten und einer zweiten, in der Ebenenliste gemerkten Wahl. Jede dieser Wahlen
/// brauchte ihren eigenen Rueckweg beim Wechsel. Jetzt steht das Ziel an einer Stelle, in der
/// Sitzung neben dem Rezept, und ein neues Rezept setzt es zurueck.
///
/// Ebene und Knoten stehen als Objekt darin, nicht als Kennung. Knoten heissen in jedem Graphen
/// n1, n2, ... - ein Ziel nach Kennung truege ueber einen Graphwechsel hinweg auf einen anderen
/// Knoten, den es zufaellig unter demselben Namen gibt.
/// </summary>
public abstract record EditingTarget
{
    private EditingTarget()
    {
    }

    /// <summary>Das fertige Bild: seine Korrektur laeuft hinter allen Ebenen.</summary>
    public static EditingTarget Picture { get; } = new PictureTarget();

    /// <summary>Das fertige Bild.</summary>
    public sealed record PictureTarget : EditingTarget;

    /// <summary>
    /// Eine Ebene des Stapels.
    /// </summary>
    /// <param name="Layer">Die gewaehlte Ebene.</param>
    /// <param name="Tools">
    /// Ob die Farbwerkzeuge ihr gelten - bei einer Einstellungsebene immer, sonst nach dem
    /// Schalter im Farbstreifen. Sonst gehoeren sie weiter dem Bild, und die Ebene ist nur fuer
    /// Rahmen, Pinsel und Waehlen im Bild gewaehlt.
    /// </param>
    public sealed record StackLayer(ImageLayer Layer, bool Tools) : EditingTarget;

    /// <summary>
    /// Ein Knoten des Graphen.
    /// </summary>
    /// <param name="Node">Der gewaehlte Knoten.</param>
    /// <param name="FromLayerList">
    /// In der Ebenenliste gewaehlt, als Ebene: Ein Effekt aus der Werkzeugleiste kommt dann in
    /// den Zweig dieser Ebene, vor ihr Mischen - im Editor gewaehlt dahinter.
    /// </param>
    public sealed record GraphNode(Node Node, bool FromLayerList) : EditingTarget;

    /// <summary>
    /// Kryptomatte-Objekte, noch ohne Maske (C3b): im Bild gewaehlt, bevor eine Aktion aus ihnen
    /// eine Maskenebene macht - "Auswahl -> Aktion -> Ergebnis".
    /// </summary>
    /// <param name="Set">Die Kryptomatte, an der gewaehlt wurde - ihr Praefix.</param>
    /// <param name="Picks">Die gewaehlten Objekte.</param>
    public sealed record CryptoSelection(string Set, IReadOnlyList<CryptoPick> Picks) : EditingTarget;
}
