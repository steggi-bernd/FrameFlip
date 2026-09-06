using System.Windows;
using FrameFlip.Views;
using Rect = System.Windows.Rect;

namespace FrameFlip.Tests;

/// <summary>
/// Der Regressionstest zum unerreichbaren Fenster.
///
/// Das Hauptfenster ging oben aus dem Bildschirm heraus. Die Titelleiste lag
/// ausserhalb, und damit war es weder zu verschieben noch zu schliessen - die
/// unangenehmste Art von Fehler, weil man nicht mehr herankommt, um ihn zu
/// beheben.
///
/// Der Fall ist leicht wiederherzustellen und leicht zu uebersehen: Eine gemerkte
/// Lage bleibt stehen, waehrend sich die Bildschirme aendern. Ein zweiter Monitor
/// wird abgezogen, die Aufloesung faellt, die Skalierung wird umgestellt - und beim
/// naechsten Start liegt der gemerkte Ort im Nichts.
/// </summary>
public static class PlacementRegression
{
    /// <summary>Ein gewoehnlicher Bildschirm mit Taskleiste unten.</summary>
    private static readonly Rect Area = new(0, 0, 1920, 1040);

    public static void Run()
    {
        Check.Group("Regression - das Fenster bleibt erreichbar");

        // Der eigentliche Fall: oberhalb des Bildschirms.
        var above = WindowPlacer.Clamp(new Rect(400, -300, 1080, 720), Area);

        Check.That(above.Top >= Area.Top, "oben herausgeragt wird nach unten geholt", above.Top.ToString("0"));
        Check.Near(above.Left, 400, 0.01, "waagerecht bleibt es, wo es war");

        // Nach unten DARF es hinausragen - die Titelleiste bleibt greifbar. Aber
        // nicht so weit, dass sie selbst unter dem Rand liegt.
        var below = WindowPlacer.Clamp(new Rect(400, 2000, 1080, 720), Area);

        Check.That(below.Top < Area.Bottom, "unten herausgeragt bleibt greifbar", below.Top.ToString("0"));

        var slightlyBelow = WindowPlacer.Clamp(new Rect(400, 800, 1080, 720), Area);

        Check.Near(slightlyBelow.Top, 800, 0.01,
                   "ein Fenster, das unten ueberhaengt, wird NICHT verschoben");

        // Seitlich: Von beiden Raendern muss ein Stueck sichtbar bleiben.
        var rightOut = WindowPlacer.Clamp(new Rect(3000, 100, 1080, 720), Area);
        Check.That(rightOut.Left < Area.Right, "rechts hinaus wird zurueckgeholt", rightOut.Left.ToString("0"));

        var leftOut = WindowPlacer.Clamp(new Rect(-2000, 100, 1080, 720), Area);
        Check.That(leftOut.Left + leftOut.Width > Area.Left, "links hinaus wird zurueckgeholt",
                   leftOut.Left.ToString("0"));

        // Ein Fenster, das groesser ist als der Bildschirm, wird kleiner gemacht
        // statt ueber den Rand geschoben.
        var huge = WindowPlacer.Clamp(new Rect(0, 0, 4000, 3000), Area);

        Check.That(huge.Width <= Area.Width, "zu breit wird schmaler", huge.Width.ToString("0"));
        Check.That(huge.Height <= Area.Height, "zu hoch wird niedriger", huge.Height.ToString("0"));

        // Ein Bildschirm, der nicht bei null anfaengt - der zweite Monitor links.
        var second = new Rect(-1920, 0, 1920, 1040);
        var onSecond = WindowPlacer.Clamp(new Rect(-1500, -200, 1080, 720), second);

        Check.That(onSecond.Top >= second.Top, "auch auf dem zweiten Schirm nach unten geholt");
        Check.Near(onSecond.Left, -1500, 0.01, "und waagerecht dort gelassen");

        // Und der Normalfall: Was passt, wird nicht angefasst.
        var fine = WindowPlacer.Clamp(new Rect(300, 150, 1080, 720), Area);

        Check.Near(fine.Left, 300, 0.01, "eine gueltige Lage bleibt unveraendert");
        Check.Near(fine.Top, 150, 0.01, "auch senkrecht");
        Check.Near(fine.Width, 1080, 0.01, "und die Groesse auch");
    }
}
