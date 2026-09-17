using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Der Greifrahmen im Bild: welche Ebene er zeigt und was ein Zug an ihr aendert.
///
/// Er ist kein zweiter Weg neben den Reglern, sondern derselbe: Was hier gezogen
/// wird, landet in derselben Platzierung, die die Regler im Streifen anzeigen - und
/// die Regler ziehen sofort nach. Zwei Wege, die auf verschiedene Werte schrieben,
/// waeren die Art Oberflaeche, bei der man irgendwann nicht mehr weiss, welche Zahl
/// gilt.
/// </summary>
public partial class AtelierPage
{
    /// <summary>
    /// Zeigt den Rahmen um die gewaehlte Ebene - oder blendet ihn aus.
    ///
    /// Der Rahmen gehoert dem VERSCHIEBEN-Werkzeug und nicht der Auswahl. Das ist
    /// der Unterschied, an dem zwei gemeldete Fehler hingen: Ein Rahmen, der
    /// erscheint, sobald irgendeine Ebene gewaehlt ist, sitzt zwangslaeufig
    /// irgendwann an einer Stelle, an der niemand ihn erwartet - und ein Zug ins
    /// Bild trifft dann die Ebene, die zuletzt angeklickt wurde, statt der, die
    /// jemand bewegen wollte. Beides verschwindet, sobald der Rahmen eine Betriebsart
    /// hat, die man sieht und abschalten kann.
    ///
    /// Ausgeblendet wird er ausserdem in drei Faellen, und jeder hat seinen Grund:
    /// Ohne Bild gibt es nichts zu platzieren. Waehrend das Original gezeigt wird,
    /// gehoert der Blick dem Vergleich. Und eine Korrektur oder Gruppe hat keine
    /// eigene Flaeche - ein Rahmen um sie waere ein Rahmen um etwas, das es nicht
    /// gibt.
    /// </summary>
    private void ShowPlacement()
    {
        var layer = Layers.Selection;

        // Zwei Werkzeuge benutzen denselben Rahmen: Verschieben und Zuschneiden.
        // Welches, sagt sein Modus - die Griffe sitzen an derselben Stelle und
        // schreiben auf andere Werte.
        Placement.Mode = _tool == AtelierTool.Crop ? AdornerMode.Crop : AdornerMode.Place;

        if (_frame is null || _showingOriginal ||
            _tool is not (AtelierTool.Move or AtelierTool.Crop) ||
            layer is null || Layers.Visibility != System.Windows.Visibility.Visible ||
            layer.Content is LayerContent.Adjustment or LayerContent.Group ||
            !_sources.TryGetValue(layer.Source, out var source))
        {
            Placement.Track(null, 0, 0, 0, 0, false);
            return;
        }

        // Die Leinwand ist das zusammengesetzte Bild, nicht die Ebene: Die
        // Platzierung rechnet in Anteilen davon.
        Placement.Track(layer.Place, source.Width, source.Height,
                        _frame.Width, _frame.Height,
                        Display.Stretch == System.Windows.Media.Stretch.Uniform);
    }

    /// <summary>Die zuletzt gezogene Lage, die noch nicht gerechnet ist.</summary>
    private LayerTransform? _pendingPlace;

    private bool _dragHooked;

    /// <summary>
    /// Am Rahmen wurde gezogen.
    ///
    /// Die Ebene bekommt die neuen Werte, und der Streifen zieht seine Regler nach.
    /// Erst dann wird gerechnet - beim Ziehen grob, beim Loslassen voll, genau wie
    /// bei jedem Regler auch.
    ///
    /// Gerechnet wird aber NICHT je Mausbewegung, und das ist der Unterschied
    /// zwischen "es zieht" und "es haengt hinterher". Windows liefert Mausbewegungen
    /// so schnell, wie das Programm sie abholt; wer in jeder davon ein Bild
    /// zusammensetzt, staut die Warteschlange, sobald ein Bild laenger dauert als
    /// der Abstand zweier Bewegungen. Der Zeiger laeuft dann sichtbar davon, und was
    /// man sieht, ist nicht der Ort von jetzt, sondern der von vor zehn Meldungen.
    ///
    /// Deshalb wird nur GEMERKT, was zuletzt gezogen wurde, und einmal je
    /// Bildwiederholung gerechnet. Der Rahmen selbst zeichnet sich sofort - er
    /// klebt an der Maus -, und das Bild darunter zieht mit einem Bild Verzoegerung
    /// nach. Das ist so schnell, wie es ueberhaupt sein kann.
    /// </summary>
    private void OnPlacementDragged(LayerTransform place, bool interim)
    {
        var layer = Layers.Selection;
        if (layer is null) return;

        layer.Place = place;
        _settings.Layers = Layers.Stack;

        if (!interim)
        {
            StopDragFrames();
            Layers.PlaceMovedOutside(false);

            return;
        }

        _pendingPlace = place;

        if (_dragHooked) return;

        _dragHooked = true;
        CompositionTarget.Rendering += OnDragFrame;
    }

    /// <summary>Eine Bildwiederholung: Was seither gezogen wurde, wird jetzt gerechnet.</summary>
    private void OnDragFrame(object? sender, EventArgs e)
    {
        if (_pendingPlace is null) return;

        _pendingPlace = null;
        Layers.PlaceMovedOutside(true);
    }

    /// <summary>
    /// Haengt den Zeittakt wieder aus.
    ///
    /// Eingehaengt laeuft er bei JEDER Bildwiederholung, auch wenn niemand zieht -
    /// sechzig Aufrufe je Sekunde fuer nichts, und ein Programm, das im Leerlauf
    /// Strom zieht.
    /// </summary>
    private void StopDragFrames()
    {
        _pendingPlace = null;

        if (!_dragHooked) return;

        _dragHooked = false;
        CompositionTarget.Rendering -= OnDragFrame;
    }
}
