using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Das Ziehen im Bild.
///
/// Eine Rechnung mit einer unangenehmen Fehlerart: Wenn sie danebenliegt, stuerzt
/// nichts ab und es sieht auch nicht falsch aus - es fuehlt sich nur komisch an. Eine
/// Ecke, die beim Ziehen wegwandert statt stehenzubleiben, schiebt man der Maus in
/// die Schuhe und nicht der Formel.
///
/// Gerechnet wird durchgehend in Bildpunkten der Leinwand. Die erste Fassung rechnete
/// in Anteilen, und das ging nur so lange gut, wie es keine Drehung gab - siehe
/// <see cref="RotationDoesNotShear"/>.
/// </summary>
public static class PlacementDragInvariants
{
    /// <summary>Eine Leinwand, die ausdruecklich NICHT quadratisch ist.</summary>
    private const int Wide = 160;

    private const int High = 90;

    public static void Run()
    {
        TheBasisIsTheRestingSize();
        TheCentreIsTheOffset();
        CornersComeBeforeTheBody();
        DraggingMovesByTheMouse();
        TheOppositeCornerStaysPut();
        PullingOutwardGrows();
        NoMovementChangesNothing();
        TurningKeepsTheCentre();
        TurningSnapsNearTheQuarters();
        RotationDoesNotShear();
    }

    private static void TheBasisIsTheRestingSize()
    {
        Check.Group("Die Grundlage ist die Lage ohne Einstellung");

        Check.Near(PlacementDrag.Basis(Wide, High, Wide, High), 1.0, 1e-5,
                   "gleich gross heisst Massstab eins");

        // Ein Logo halber Kantenlaenge wird eingepasst - also verdoppelt.
        Check.Near(PlacementDrag.Basis(Wide / 2, High / 2, Wide, High), 2.0, 1e-5,
                   "ein gleich geformtes Logo wird eingepasst");

        // Ein schmales passt an der Hoehe an und laesst seitlich Rand.
        float narrow = PlacementDrag.Basis(20, 90, Wide, High);
        Check.Near(narrow, 1.0, 1e-5, "ein schmales passt an der Hoehe an");

        // Unsinnige Masse duerfen nicht werfen - beim Ausprobieren ist ein Bild
        // schon einmal null Punkte breit, bevor es steht.
        Check.That(PlacementDrag.Basis(0, 0, Wide, High) > 0f,
                   "ohne Masse kommt trotzdem etwas Brauchbares heraus");
    }

    private static void TheCentreIsTheOffset()
    {
        Check.Group("Die Mitte ist der Versatz");

        var box = PlacementDrag.Region(new LayerTransform(), Wide, High, Wide, High);

        Check.Near(box.CentreX, Wide / 2.0, 1e-4, "ohne Versatz liegt sie in der Mitte");
        Check.Near(box.CentreY, High / 2.0, 1e-4, "in beiden Richtungen");
        Check.Near(box.HalfWidth, Wide / 2.0, 1e-4, "und reicht bis an beide Raender");

        var moved = PlacementDrag.Region(new LayerTransform { OffsetX = 0.25f, Scale = 0.5f },
                                         Wide, High, Wide, High);

        Check.Near(moved.CentreX, Wide * 0.75, 1e-4, "ein Versatz verschiebt genau um seinen Anteil");
        Check.Near(moved.HalfWidth, Wide / 4.0, 1e-4, "und der Massstab halbiert die Kante");
    }

    private static void CornersComeBeforeTheBody()
    {
        Check.Group("Die Ecken gehen der Flaeche vor");

        var place = new LayerTransform { Scale = 0.5f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        box.Corners(out float x0, out float y0, out _, out _,
                    out float x2, out float y2, out _, out _);

        Check.That(PlacementDrag.HandleAt(box, x0, y0, 4f) == DragHandle.TopLeft,
                   "oben links ist eine Ecke");
        Check.That(PlacementDrag.HandleAt(box, x2, y2, 4f) == DragHandle.BottomRight,
                   "unten rechts auch");

        // Eine Ecke liegt AUF dem Rand der Flaeche. Wer dort zuerst die Flaeche
        // traefe, kaeme nie an eine Ecke - deshalb werden die Ecken zuerst geprueft.
        Check.That(PlacementDrag.HandleAt(box, box.CentreX, box.CentreY, 4f) == DragHandle.Body,
                   "in der Mitte ist es die Flaeche");

        Check.That(PlacementDrag.HandleAt(box, 5f, 5f, 4f) == DragHandle.None,
                   "daneben ist nichts");

        // Der Drehgriff sitzt ausserhalb, ueber der oberen Kante.
        box.RotateGrip(PlacementDrag.RotateDistance, out float rx, out float ry);
        Check.That(PlacementDrag.HandleAt(box, rx, ry, 4f) == DragHandle.Rotate,
                   "und der Drehgriff liegt darueber");
    }

    private static void DraggingMovesByTheMouse()
    {
        Check.Group("Verschieben folgt der Maus genau");

        var place = new LayerTransform { Scale = 0.5f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        var drag = PlacementDrag.Begin(place, box, box.CentreX, box.CentreY, 4f);
        Check.That(drag.Handle == DragHandle.Body, "in der Mitte fasst man die Flaeche");

        var moved = drag.To(box.CentreX + 32f, box.CentreY - 9f, Wide, High);

        Check.Near(moved.OffsetX, 32.0 / Wide, 1e-4, "der Versatz folgt der Maus");
        Check.Near(moved.OffsetY, -9.0 / High, 1e-4, "auch nach oben");
        Check.Near(moved.Scale, 0.5, 1e-5, "und die Groesse bleibt");

        // Ueber den Rand hinaus ist erlaubt: Ein Wasserzeichen darf halb draussen
        // liegen, und die Maus darf beim Ziehen aus dem Bild geraten.
        var beyond = drag.To(Wide * 1.4f, box.CentreY, Wide, High);
        Check.That(beyond.OffsetX > 0.8f, "ueber den Rand hinaus geht auch", $"{beyond.OffsetX:0.##}");
    }

    /// <summary>
    /// Die Probe, um die es geht: Beim Ziehen an einer Ecke bleibt die
    /// gegenueberliegende stehen - auch bei gedrehter Ebene.
    /// </summary>
    private static void TheOppositeCornerStaysPut()
    {
        Check.Group("Die gegenueberliegende Ecke bleibt stehen");

        foreach (float turn in new[] { 0f, 30f, -70f })
        {
            foreach (var corner in new[] { DragHandle.TopLeft, DragHandle.TopRight,
                                           DragHandle.BottomLeft, DragHandle.BottomRight })
            {
                var place = new LayerTransform
                {
                    Scale = 0.5f, OffsetX = 0.1f, OffsetY = -0.05f, Rotation = turn,
                };

                var box = PlacementDrag.Region(place, Wide, High, Wide, High);

                bool left = corner is DragHandle.TopLeft or DragHandle.BottomLeft;
                bool top = corner is DragHandle.TopLeft or DragHandle.TopRight;

                box.ToCanvas(left ? -box.HalfWidth : box.HalfWidth,
                             top ? -box.HalfHeight : box.HalfHeight,
                             out float grabX, out float grabY);

                box.ToCanvas(left ? box.HalfWidth : -box.HalfWidth,
                             top ? box.HalfHeight : -box.HalfHeight,
                             out float fixedX, out float fixedY);

                var drag = PlacementDrag.Begin(place, box, grabX, grabY, 4f);
                Check.That(drag.Handle == corner, $"{turn:0}°/{corner}: laesst sich fassen");

                // Ein Stueck nach aussen ziehen, laengs der Diagonale.
                float pullX = grabX + (grabX - fixedX) * 0.3f;
                float pullY = grabY + (grabY - fixedY) * 0.3f;

                var pulled = drag.To(pullX, pullY, Wide, High);
                var after = PlacementDrag.Region(pulled, Wide, High, Wide, High);

                after.ToCanvas(left ? after.HalfWidth : -after.HalfWidth,
                               top ? after.HalfHeight : -after.HalfHeight,
                               out float stillX, out float stillY);

                Check.Near(stillX, fixedX, 0.01, $"{turn:0}°/{corner}: die andere Ecke bleibt waagerecht");
                Check.Near(stillY, fixedY, 0.01, $"{turn:0}°/{corner}: und senkrecht");
            }
        }
    }

    private static void PullingOutwardGrows()
    {
        Check.Group("Nach aussen ziehen macht groesser");

        var place = new LayerTransform { Scale = 0.5f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        box.Corners(out _, out _, out _, out _, out float cx, out float cy, out _, out _);

        var drag = PlacementDrag.Begin(place, box, cx, cy, 4f);
        Check.That(drag.Handle == DragHandle.BottomRight, "unten rechts gefasst");

        var bigger = drag.To(cx + 20f, cy + 12f, Wide, High);
        Check.That(bigger.Scale > place.Scale, "nach aussen wird sie groesser", $"{bigger.Scale:0.###}");

        var smaller = drag.To(cx - 20f, cy - 12f, Wide, High);
        Check.That(smaller.Scale < place.Scale, "nach innen kleiner", $"{smaller.Scale:0.###}");

        // Ganz ueber die feste Ecke hinaus darf sie nicht umklappen - eine Ebene mit
        // negativer Groesse waere spiegelverkehrt, und das hat niemand verlangt.
        var past = drag.To(0f, 0f, Wide, High);
        Check.That(past.Scale > 0f, "und kippt nicht ins Negative", $"{past.Scale:0.####}");
    }

    private static void NoMovementChangesNothing()
    {
        Check.Group("Ohne Bewegung aendert sich nichts");

        var place = new LayerTransform { Scale = 0.4f, OffsetX = -0.2f, OffsetY = 0.15f, Rotation = 20f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        box.Corners(out float x0, out float y0, out _, out _, out float x2, out float y2, out _, out _);
        box.RotateGrip(PlacementDrag.RotateDistance, out float rx, out float ry);

        foreach (var (x, y, what) in new[]
                 {
                     (box.CentreX, box.CentreY, "in der Flaeche"),
                     (x0, y0, "an der Ecke oben links"),
                     (x2, y2, "an der Ecke unten rechts"),
                     (rx, ry, "am Drehgriff"),
                 })
        {
            var drag = PlacementDrag.Begin(place, box, x, y, 4f);
            var same = drag.To(x, y, Wide, High);

            Check.Near(same.OffsetX, place.OffsetX, 1e-3, $"{what}: der Versatz bleibt");
            Check.Near(same.OffsetY, place.OffsetY, 1e-3, $"{what}: in beiden Richtungen");
            Check.Near(same.Scale, place.Scale, 1e-3, $"{what}: und die Groesse");
        }

        // Ein Zug, der nirgends angefasst hat, aendert erst recht nichts.
        var nothing = PlacementDrag.Begin(place, box, 2f, 2f, 4f);
        Check.That(!nothing.IsActive, "neben der Ebene faengt kein Zug an");
    }

    /// <summary>
    /// Beim Drehen bleibt die Mitte der Drehpunkt - sie darf nicht mitwandern.
    /// </summary>
    private static void TurningKeepsTheCentre()
    {
        Check.Group("Drehen laesst die Mitte, wo sie ist");

        var place = new LayerTransform { Scale = 0.5f, OffsetX = 0.2f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        box.RotateGrip(PlacementDrag.RotateDistance, out float rx, out float ry);

        var drag = PlacementDrag.Begin(place, box, rx, ry, 4f);
        Check.That(drag.Handle == DragHandle.Rotate, "der Drehgriff laesst sich fassen");

        // Auf die rechte Seite ziehen - aus "oben" wird "rechts", also 90 Grad.
        var turned = drag.To(box.CentreX + 40f, box.CentreY, Wide, High);

        Check.Near(turned.OffsetX, place.OffsetX, 1e-4, "der Versatz bleibt");
        Check.Near(turned.OffsetY, place.OffsetY, 1e-4, "in beiden Richtungen");
        Check.Near(turned.Scale, place.Scale, 1e-4, "und die Groesse auch");
        Check.Near(turned.Rotation, 90.0, 0.5, "nur der Winkel aendert sich");
    }

    private static void TurningSnapsNearTheQuarters()
    {
        Check.Group("Drehen rastet nahe an geraden Winkeln ein");

        var place = new LayerTransform { Scale = 0.5f };
        var box = PlacementDrag.Region(place, Wide, High, Wide, High);

        box.RotateGrip(PlacementDrag.RotateDistance, out float rx, out float ry);
        var drag = PlacementDrag.Begin(place, box, rx, ry, 4f);

        // Knapp neben 90 Grad: der Griff soll einrasten.
        double radians = (-90 + 0.8) * Math.PI / 180.0;   // von "oben" aus gerechnet
        float x = box.CentreX + (float)(Math.Cos(radians) * 40);
        float y = box.CentreY + (float)(Math.Sin(radians) * 40);

        var nearly = drag.To(x, y, Wide, High);
        Check.Near(nearly.Rotation % 15f, 0.0, 0.01, "knapp daneben rastet es ein");

        // Weit genug daneben bleibt der Winkel, wie er ist - wer sieben Grad will,
        // soll sieben Grad bekommen.
        radians = (-90 + 7.0) * Math.PI / 180.0;
        x = box.CentreX + (float)(Math.Cos(radians) * 40);
        y = box.CentreY + (float)(Math.Sin(radians) * 40);

        var free = drag.To(x, y, Wide, High);
        Check.That(MathF.Abs(free.Rotation % 15f) > 0.5f, "weiter weg bleibt er frei",
                   $"{free.Rotation:0.##}");
    }

    /// <summary>
    /// Die Probe, die den ersten Entwurf verworfen hat.
    ///
    /// Gerechnet wurde anfangs in ANTEILEN der Leinwand. Solange nichts gedreht wird,
    /// faellt das nicht auf - aber die beiden Achsen sind darin verschieden lang, und
    /// eine Drehung in einem solchen Bezug SCHERT, statt zu drehen: Ein Quadrat kaeme
    /// als Raute heraus, und zwar umso schiefer, je weiter das Bild vom Quadrat
    /// entfernt ist.
    ///
    /// Geprueft wird deshalb auf einer ausdruecklich nicht quadratischen Leinwand,
    /// dass ein gedrehtes Quadrat ein Quadrat bleibt: Alle vier Kanten gleich lang,
    /// die Diagonalen gleich lang.
    /// </summary>
    private static void RotationDoesNotShear()
    {
        Check.Group("Drehen schert nicht");

        // Eine quadratische Ebene auf einer Leinwand im Verhaeltnis 16 zu 9.
        var place = new LayerTransform { Scale = 0.5f, Rotation = 37f };
        var box = PlacementDrag.Region(place, 64, 64, Wide, High);

        box.Corners(out float x0, out float y0, out float x1, out float y1,
                    out float x2, out float y2, out float x3, out float y3);

        double a = Distance(x0, y0, x1, y1);
        double b = Distance(x1, y1, x2, y2);
        double c = Distance(x2, y2, x3, y3);
        double d = Distance(x3, y3, x0, y0);

        Check.Near(b, a, 0.01, "alle vier Kanten sind gleich lang");
        Check.Near(c, a, 0.01, "auch die gegenueberliegende");
        Check.Near(d, a, 0.01, "und die vierte");

        // Die Diagonalen entscheiden: Bei einer Raute sind sie verschieden lang.
        Check.Near(Distance(x0, y0, x2, y2), Distance(x1, y1, x3, y3), 0.01,
                   "und die Diagonalen ebenfalls - es bleibt ein Quadrat");

        // Und die Kantenlaenge stimmt: gedreht wird, nicht vergroessert.
        var straight = PlacementDrag.Region(new LayerTransform { Scale = 0.5f }, 64, 64, Wide, High);
        Check.Near(a, straight.HalfWidth * 2, 0.01, "und ebenso lang wie ungedreht");

        static double Distance(float ax, float ay, float bx, float by)
            => Math.Sqrt((ax - bx) * (double)(ax - bx) + (ay - by) * (double)(ay - by));
    }
}
