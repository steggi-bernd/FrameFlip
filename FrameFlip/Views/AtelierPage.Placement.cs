using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

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

        // Drei Werkzeuge benutzen denselben Rahmen: Verschieben, Zuschneiden, Malen.
        // Welches, sagt sein Modus - sie sitzen alle in derselben Flaeche und teilen
        // sich die Umrechnung zwischen Schirm, Leinwand und Bildpunkt.
        Placement.Mode = _tool switch
        {
            AtelierTool.Crop => AdornerMode.Crop,
            AtelierTool.Brush => AdornerMode.Paint,
            _ => AdornerMode.Place,
        };

        if (_tool == AtelierTool.Brush)
        {
            ShowBrush();
            return;
        }

        Placement.Paint(null, 0, 0, false);

        if (InNodes)
        {
            ShowNodePlacement();
            return;
        }

        if (_frame is null || _showingOriginal ||
            _tool is not (AtelierTool.Move or AtelierTool.Crop) ||
            layer is null || !_layersShown ||
            layer.Content is LayerContent.Adjustment or LayerContent.Group ||
            !_sources.TryGetValue(layer.Source, out var source))
        {
            Placement.Track(null, 0, 0, 0, 0, false);

            if (!_dragHooked) _placing = null;

            return;
        }

        // Wem der Rahmen gehoert - gemerkt, solange kein Zug laeuft.
        //
        // Waehrend eines Zuges NICHT: Diese Stelle laeuft je Bildwiederholung, und
        // die Auswahl kann sich dazwischen verschieben - der Streifen baut sich neu
        // auf und setzt sie auf die oberste Ebene, wenn eine Ebene dazukommt oder
        // verschwindet. Der Zug muesste dann mitten in der Bewegung die Ebene
        // wechseln. Genau das war "Ziehen wirkt auf die falsche Ebene": Man fasste
        // eine an und bewegte eine andere.
        if (!_dragHooked) _placing = layer;

        // Die Leinwand ist das zusammengesetzte Bild, nicht die Ebene: Die
        // Platzierung rechnet in Anteilen davon.
        Placement.Track(layer.Place, source.Width, source.Height,
                        _frame.Width, _frame.Height,
                        Display.Stretch == System.Windows.Media.Stretch.Uniform);
    }

    /// <summary>
    /// Haengt den Pinsel an die Maske der gewaehlten Ebene.
    ///
    /// Ohne gewaehlte Ebene oder ohne gemalte Maske gibt es nichts zu bemalen - dann
    /// bleibt die Flaeche durchlaessig, statt Klicks zu schlucken. Ein Pinsel, der
    /// auf nichts malt und trotzdem die Maus nimmt, ist der unangenehmste Zustand von
    /// allen.
    /// </summary>
    private void ShowBrush()
    {
        if (InNodes)
        {
            ShowNodeBrush();
            return;
        }

        // Im Stapel wird kein Verlauf aufgezeichnet - also auch kein Knopf dafuer.
        Properties.ShowBrushHistory(false);

        // Im Stapel legt der erste Strich eine Maskenebene an, wenn es noch keine gibt.
        Placement.MaskWanted = MakeMaskLayer;

        var layer = Layers.Selection;

        if (_frame is null)
        {
            Placement.Paint(null, 0, 0, false);
            Display.Cursor = null;

            return;
        }

        // Noch keine gemalte Maske? Dann faengt der Pinsel trotzdem - und legt beim
        // ERSTEN Strich eine Maskenebene an. Siehe MakeMaskLayer.
        if (layer is null || layer.Mask.Kind != MaskKind.Painted)
        {
            Placement.Paint(null, _frame.Width, _frame.Height,
                            Display.Stretch == System.Windows.Media.Stretch.Uniform);

            Display.Cursor = System.Windows.Input.Cursors.None;

            UseBrushSettings();

            return;
        }

        var mask = layer.Mask.PaintOn(_number, _frame.Width, _frame.Height);

        Placement.Paint(mask, _frame.Width, _frame.Height,
                        Display.Stretch == System.Windows.Media.Stretch.Uniform);

        // Der Zeiger weicht dem Ring - aber erst jetzt, wo feststeht, dass gemalt
        // werden kann. Ihn vorher auszublenden hiesse, ihn auch dort wegzunehmen, wo
        // der Ring gar nicht erscheint.
        Display.Cursor = System.Windows.Input.Cursors.None;

        UseBrushSettings();
    }

    /// <summary>
    /// Uebernimmt die Pinseleinstellungen aus der Eigenschaftsspalte.
    ///
    /// Sie leben dort und nicht an der Ebene, weil sie zum WERKZEUG gehoeren: Sie
    /// gelten weiter, wenn man die Ebene wechselt, und waeren an der Ebene Zahlen,
    /// die bei jedem Klick woanders verschwinden.
    /// </summary>
    private void UseBrushSettings()
    {
        Placement.BrushRadius = Properties.BrushRadius;
        Placement.BrushHardness = Properties.BrushHardness;
        Placement.BrushFlow = Properties.BrushFlow;
        Placement.BrushOpacity = Properties.BrushOpacity;
        Placement.BrushSpacing = Properties.BrushSpacing;

        Placement.InvalidateVisual();
    }

    /// <summary>
    /// Strg und das Rad beim Pinsel: der Abstand der Tupfer. Der Regler in der
    /// Eigenschaftsleiste zieht ueber <see cref="PlacementAdorner.BrushAdjusted"/> nach.
    /// </summary>
    private void StepBrushSpacing(double notches)
        => Placement.StepSpacing(notches, System.Windows.Input.Mouse.GetPosition(Placement));

    /// <summary>
    /// Liefert die Maske fuer den ersten Strich - und legt dafuer eine EIGENE EBENE an.
    ///
    /// Das Bild wird nie bemalt. Wer den Pinsel nimmt, meint eine Maske und keine
    /// Aenderung am Bild selbst; eine Maske auf der untersten Ebene waere aber genau
    /// das - sie schnitte das Bild an, und rueckgaengig ginge es nur ueber denselben
    /// Pinsel.
    ///
    /// Angelegt wird eine EINSTELLUNGSEBENE. Sie bringt nichts mit und aendert nichts,
    /// solange niemand einen Regler anfasst - genau das, was eine Maskenebene sein
    /// soll: ein Ort, an dem "hier" steht, und der wartet, bis jemand sagt, was dort
    /// geschehen soll. Wegwerfen heisst eine Zeile loeschen.
    ///
    /// Und erst beim Strich, nicht beim Waehlen des Werkzeugs: Wer den Pinsel nur
    /// anfasst, um zu sehen, was er tut, soll keine Ebene erzeugt haben.
    /// </summary>
    private PaintedMask? MakeMaskLayer()
    {
        if (_frame is null) return null;

        // Im Knotenmodus: die Maske des gewaehlten Knotens - oder eine neue Maskenebene.
        if (InNodes) return MakeNodeMask();

        var layer = Layers.Selection;

        if (layer is not null && layer.Mask.Kind == MaskKind.Painted)
            return layer.Mask.PaintOn(_number, _frame.Width, _frame.Height);

        Layers.AddAdjustment();

        var made = Layers.Selection;
        if (made is null) return null;

        made.Mask.Kind = MaskKind.Painted;
        made.Name = FrameFlip.Localization.Strings.T("S_MaskLayerName");

        return made.Mask.PaintOn(_number, _frame.Width, _frame.Height);
    }

    /// <summary>
    /// Es wurde gemalt.
    ///
    /// Beim Ziehen grob rechnen, beim Loslassen voll und festhalten - dieselbe
    /// Zweiteilung wie bei jedem Regler. Der Schleier ueber dem Bild zeichnet sich
    /// sofort; das Bild darunter zieht nach.
    /// </summary>
    private void OnPainted(bool interim)
    {
        if (InNodes)
        {
            OnNodePainted(interim);
            return;
        }

        var layer = Layers.Selection;
        if (layer is null) return;

        if (!interim)
        {
            StopDragFrames();

            _recipe.Layers = Layers.Stack;
            Layers.PlaceMovedOutside(false);

            return;
        }

        // NICHT je Mausmeldung rechnen - das ist der Unterschied zwischen einem
        // Pinsel, der an der Maus klebt, und einem, der hinterherzieht.
        //
        // Windows liefert Mausbewegungen so schnell, wie das Programm sie abholt.
        // Wer in jeder davon ein 4K-Bild zusammensetzt, staut die Warteschlange,
        // sobald ein Durchgang laenger dauert als der Abstand zweier Bewegungen; der
        // Strich laeuft dann sichtbar davon. Der Schleier ueber dem Bild zeichnet
        // sich sofort - er klebt an der Maus -, und das Bild darunter zieht mit einem
        // Bild Verzoegerung nach.
        //
        // Derselbe Griff wie beim Verschieben, und derselbe Zeittakt.
        _pendingPaint = true;

        if (_dragHooked) return;

        _dragHooked = true;
        CompositionTarget.Rendering += OnDragFrame;
    }

    /// <summary>Ein Strich, der noch nicht gerechnet ist.</summary>
    private bool _pendingPaint;

    /// <summary>Die Ebene, der der Greifrahmen gerade gehoert. Null: keiner zu sehen.</summary>
    private ImageLayer? _placing;

    /// <summary>
    /// Wen der Greifrahmen bewegt. Oeffentlich fuer die Probe - der Zug und der
    /// Rahmen muessen dieselbe Ebene meinen, und das laesst sich sonst nur ansehen.
    /// </summary>
    public ImageLayer? Placing => _placing;

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
        if (InNodes)
        {
            OnNodePlacementDragged(place, interim);
            return;
        }

        // Die Ebene, fuer die der Rahmen gebaut wurde - siehe ShowPlacement. Die
        // AKTUELLE Auswahl zu nehmen war der Fehler: Sie kann sich waehrend des
        // Zuges verschoben haben, und dann bewegt sich etwas anderes als das, was
        // unter dem Zeiger liegt.
        var layer = _placing ?? Layers.Selection;
        if (layer is null) return;

        layer.Place = place;
        _recipe.Layers = Layers.Stack;

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
        if (_pendingPlace is null && !_pendingPaint) return;

        bool placing = _pendingPlace is not null;

        _pendingPlace = null;
        _pendingPaint = false;

        // Im Knotenmodus gibt es keinen Stapel, dem man es melden muesste - der Graph
        // rechnet gleich selbst. Beim Malen nur den Teil, den der Pinsel beruehrt hat,
        // voll aufgeloest; geht das nicht, das ganze Bild grob wie bisher.
        if (InNodes)
        {
            if (!placing && PaintRegion()) return;

            // Einmal grob gerechnet, ist das Bild dieses Strichs grob - am Ende muss das
            // scharfe sofort nachkommen.
            if (!placing) _regionFailed = true;

            Refresh(interim: true, recompose: false);
        }
        else
        {
            Layers.PlaceMovedOutside(true);
        }
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
        _pendingPaint = false;

        if (!_dragHooked) return;

        _dragHooked = false;
        CompositionTarget.Rendering -= OnDragFrame;
    }
}
