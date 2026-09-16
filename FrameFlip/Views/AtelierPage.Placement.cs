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
    /// Ausgeblendet wird er in drei Faellen, und jeder hat seinen Grund: Ohne Bild
    /// gibt es nichts zu platzieren. Waehrend das Original gezeigt wird oder eine
    /// Kryptomatte gewaehlt wird, gehoert der Klick jemand anderem. Und eine
    /// Korrektur oder Gruppe hat keine eigene Flaeche - ein Rahmen um sie waere ein
    /// Rahmen um etwas, das es nicht gibt.
    /// </summary>
    private void ShowPlacement()
    {
        var layer = Layers.Selection;

        if (_frame is null || _showingOriginal || _picking ||
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

    /// <summary>
    /// Am Rahmen wurde gezogen.
    ///
    /// Die Ebene bekommt die neuen Werte, und der Streifen zieht seine Regler nach.
    /// Erst dann wird gerechnet - beim Ziehen grob, beim Loslassen voll, genau wie
    /// bei jedem Regler auch.
    /// </summary>
    private void OnPlacementDragged(LayerTransform place, bool interim)
    {
        var layer = Layers.Selection;
        if (layer is null) return;

        layer.Place = place;

        _settings.Layers = Layers.Stack;
        Layers.PlaceMovedOutside(interim);
    }
}
