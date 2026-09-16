using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Das Ziehen im Bild.
///
/// Eine Rechnung mit einer unangenehmen Fehlerart: Wenn sie danebenliegt, stuerzt
/// nichts ab und es sieht auch nicht falsch aus - es fuehlt sich nur komisch an. Eine
/// Ecke, die beim Ziehen wegwandert statt stehenzubleiben, schiebt man der Maus in
/// die Schuhe und nicht der Formel.
/// </summary>
public static class PlacementDragInvariants
{
    public static void Run()
    {
        TheBasisIsTheRestingSize();
        TheCentreIsTheOffset();
        CornersComeBeforeTheBody();
        DraggingMovesByTheMouse();
        TheOppositeCornerStaysPut();
        PullingOutwardGrows();
        NoMovementChangesNothing();
    }

    private static void TheBasisIsTheRestingSize()
    {
        Check.Group("Die Grundlage ist die Lage ohne Einstellung");

        // Gleich gross: die Ebene deckt die Leinwand ganz.
        PlacementDrag.Basis(16, 12, 16, 12, out float w, out float h);
        Check.Near(w, 1.0, 1e-5, "gleich gross deckt die ganze Breite");
        Check.Near(h, 1.0, 1e-5, "und die ganze Hoehe");

        // Ein Logo halber Kantenlaenge wird eingepasst und deckt danach ebenfalls
        // alles - eingepasst heisst: so gross wie moeglich, ohne hinauszulaufen.
        PlacementDrag.Basis(8, 6, 16, 12, out w, out h);
        Check.Near(w, 1.0, 1e-5, "ein gleich geformtes Logo wird auf volle Breite gepasst");

        // Ein schmales laesst seitlich Rand.
        PlacementDrag.Basis(8, 24, 16, 12, out w, out h);
        Check.Near(h, 1.0, 1e-5, "ein schmales fuellt die Hoehe");
        Check.That(w < 0.5f, "und laesst seitlich Rand", $"{w:0.###}");

        // Unsinnige Masse duerfen nicht werfen - beim Ausprobieren ist ein Bild
        // schon einmal null Punkte breit, bevor es steht.
        PlacementDrag.Basis(0, 0, 16, 12, out w, out h);
        Check.That(w > 0 && h > 0, "ohne Masse kommt trotzdem etwas Brauchbares heraus");
    }

    private static void TheCentreIsTheOffset()
    {
        Check.Group("Die Mitte ist der Versatz");

        PlacementDrag.Region(new LayerTransform(), 1f, 1f,
                             out float cx, out float cy, out float hw, out float hh);

        Check.Near(cx, 0.5, 1e-5, "ohne Versatz liegt sie in der Mitte");
        Check.Near(cy, 0.5, 1e-5, "in beiden Richtungen");
        Check.Near(hw, 0.5, 1e-5, "und reicht bis an beide Raender");

        PlacementDrag.Region(new LayerTransform { OffsetX = 0.25f, Scale = 0.5f }, 1f, 1f,
                             out cx, out _, out hw, out _);

        Check.Near(cx, 0.75, 1e-5, "ein Versatz verschiebt die Mitte genau um sich selbst");
        Check.Near(hw, 0.25, 1e-5, "und der Massstab halbiert die Kante");
    }

    private static void CornersComeBeforeTheBody()
    {
        Check.Group("Die Ecken gehen der Flaeche vor");

        var place = new LayerTransform { Scale = 0.5f };   // von 0,25 bis 0,75

        Check.That(PlacementDrag.HandleAt(place, 1f, 1f, 0.25f, 0.25f, 0.02f) == DragHandle.TopLeft,
                   "oben links ist eine Ecke");
        Check.That(PlacementDrag.HandleAt(place, 1f, 1f, 0.75f, 0.75f, 0.02f) == DragHandle.BottomRight,
                   "unten rechts auch");

        // Eine Ecke liegt AUF dem Rand der Flaeche. Wer dort zuerst die Flaeche
        // traefe, kaeme nie an eine Ecke - deshalb werden die Ecken zuerst geprueft.
        Check.That(PlacementDrag.HandleAt(place, 1f, 1f, 0.5f, 0.5f, 0.02f) == DragHandle.Body,
                   "in der Mitte ist es die Flaeche");

        Check.That(PlacementDrag.HandleAt(place, 1f, 1f, 0.1f, 0.1f, 0.02f) == DragHandle.None,
                   "daneben ist nichts");

        // Der Fangbereich fasst, auch wenn man knapp danebentrifft.
        Check.That(PlacementDrag.HandleAt(place, 1f, 1f, 0.26f, 0.24f, 0.02f) == DragHandle.TopLeft,
                   "knapp daneben faengt die Ecke trotzdem");
    }

    private static void DraggingMovesByTheMouse()
    {
        Check.Group("Verschieben folgt der Maus genau");

        var place = new LayerTransform { Scale = 0.5f };
        var drag = PlacementDrag.Begin(place, 1f, 1f, 0.5f, 0.5f, 0.02f);

        Check.That(drag.Handle == DragHandle.Body, "in der Mitte fasst man die Flaeche");

        var moved = drag.To(0.7f, 0.4f);

        Check.Near(moved.OffsetX, 0.2, 1e-5, "der Versatz folgt der Maus");
        Check.Near(moved.OffsetY, -0.1, 1e-5, "auch nach oben");
        Check.Near(moved.Scale, 0.5, 1e-5, "und die Groesse bleibt");

        // Ueber den Rand hinaus ist erlaubt: Ein Wasserzeichen darf halb draussen
        // liegen, und die Maus darf beim Ziehen aus dem Bild geraten.
        var beyond = drag.To(1.4f, 0.5f);
        Check.That(beyond.OffsetX > 0.8f, "ueber den Rand hinaus geht auch", $"{beyond.OffsetX:0.##}");
    }

    /// <summary>
    /// Die Probe, um die es geht: Beim Ziehen an einer Ecke bleibt die
    /// gegenueberliegende stehen.
    ///
    /// Das ist das Verhalten, das jeder erwartet, und es ist der Grund, warum sich
    /// dabei Massstab UND Versatz aendern muessen: Die Platzierung rechnet von der
    /// Mitte aus, und die Mitte wandert, wenn eine Ecke stehenbleibt. Wer nur den
    /// Massstab aendert, zieht die Ebene unter der Maus weg.
    /// </summary>
    private static void TheOppositeCornerStaysPut()
    {
        Check.Group("Die gegenueberliegende Ecke bleibt stehen");

        foreach (var corner in new[] { DragHandle.TopLeft, DragHandle.TopRight,
                                       DragHandle.BottomLeft, DragHandle.BottomRight })
        {
            var place = new LayerTransform { Scale = 0.5f, OffsetX = 0.1f, OffsetY = -0.05f };

            PlacementDrag.Region(place, 1f, 1f, out float cx, out float cy,
                                 out float hw, out float hh);

            // Die Ecke, die angefasst wird, und die, die stehenbleiben muss.
            bool left = corner is DragHandle.TopLeft or DragHandle.BottomLeft;
            bool top = corner is DragHandle.TopLeft or DragHandle.TopRight;

            float grabX = left ? cx - hw : cx + hw;
            float grabY = top ? cy - hh : cy + hh;

            float fixedX = left ? cx + hw : cx - hw;
            float fixedY = top ? cy + hh : cy - hh;

            var drag = PlacementDrag.Begin(place, 1f, 1f, grabX, grabY, 0.02f);
            Check.That(drag.Handle == corner, $"{corner} laesst sich fassen");

            // Ein Stueck nach aussen ziehen, in beide Richtungen.
            var pulled = drag.To(grabX + (left ? -0.1f : 0.1f), grabY + (top ? -0.08f : 0.08f));

            PlacementDrag.Region(pulled, 1f, 1f, out float nx, out float ny,
                                 out float nw, out float nh);

            float stillX = left ? nx + nw : nx - nw;
            float stillY = top ? ny + nh : ny - nh;

            Check.Near(stillX, fixedX, 1e-4, $"{corner}: die andere Ecke bleibt waagerecht stehen");
            Check.Near(stillY, fixedY, 1e-4, $"{corner}: und senkrecht");
        }
    }

    private static void PullingOutwardGrows()
    {
        Check.Group("Nach aussen ziehen macht groesser");

        var place = new LayerTransform { Scale = 0.5f };
        var drag = PlacementDrag.Begin(place, 1f, 1f, 0.75f, 0.75f, 0.02f);

        Check.That(drag.Handle == DragHandle.BottomRight, "unten rechts gefasst");

        var bigger = drag.To(0.9f, 0.9f);
        Check.That(bigger.Scale > place.Scale, "nach aussen wird sie groesser",
                   $"{bigger.Scale:0.###}");

        var smaller = drag.To(0.6f, 0.6f);
        Check.That(smaller.Scale < place.Scale, "nach innen kleiner", $"{smaller.Scale:0.###}");

        // Ganz ueber die feste Ecke hinaus darf sie nicht umklappen - eine Ebene mit
        // negativer Groesse waere spiegelverkehrt, und das hat niemand verlangt.
        var past = drag.To(0.1f, 0.1f);
        Check.That(past.Scale > 0f, "und kippt nicht ins Negative", $"{past.Scale:0.####}");
    }

    private static void NoMovementChangesNothing()
    {
        Check.Group("Ohne Bewegung aendert sich nichts");

        var place = new LayerTransform { Scale = 0.4f, OffsetX = -0.2f, OffsetY = 0.15f };

        PlacementDrag.Region(place, 1f, 1f, out float cx, out float cy, out float hw, out float hh);

        foreach (var (x, y, what) in new[]
                 {
                     (cx, cy, "in der Flaeche"),
                     (cx - hw, cy - hh, "an der Ecke oben links"),
                     (cx + hw, cy + hh, "an der Ecke unten rechts"),
                 })
        {
            var drag = PlacementDrag.Begin(place, 1f, 1f, x, y, 0.02f);
            var same = drag.To(x, y);

            Check.Near(same.OffsetX, place.OffsetX, 1e-4, $"{what}: der Versatz bleibt");
            Check.Near(same.OffsetY, place.OffsetY, 1e-4, $"{what}: in beiden Richtungen");
            Check.Near(same.Scale, place.Scale, 1e-4, $"{what}: und die Groesse");
        }

        // Ein Zug, der nirgends angefasst hat, aendert erst recht nichts.
        var nothing = PlacementDrag.Begin(place, 1f, 1f, 0.95f, 0.95f, 0.02f);
        Check.That(!nothing.IsActive, "neben der Ebene faengt kein Zug an");
    }
}
