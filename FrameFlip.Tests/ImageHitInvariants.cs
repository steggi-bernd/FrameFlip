using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Vom Klick zum Bildpunkt.
///
/// Eine kleine Rechnung mit einer unangenehmen Eigenschaft: Wenn sie danebenliegt,
/// sieht es nicht nach einem Rechenfehler aus, sondern danach, dass die Kryptomatte
/// nicht funktioniert. Man waehlt das Objekt neben dem, auf das man gezeigt hat, und
/// sucht den Fehler an der falschen Stelle.
/// </summary>
public static class ImageHitInvariants
{
    public static void Run()
    {
        OneToOne();
        FittedWithBars();
        OutsideIsOutside();
    }

    /// <summary>Bei 100 Prozent steht ein Bildpunkt auf einem Punkt.</summary>
    private static void OneToOne()
    {
        Check.Group("Bei voller Groesse trifft es Punkt fuer Punkt");

        bool hit = ImageHit.PixelAt(0.5, 0.5, 16, 12, 16, 12, uniform: false, out int x, out int y);

        Check.That(hit, "die obere linke Ecke trifft");
        Check.That(x == 0 && y == 0, "und zwar den ersten Bildpunkt", $"{x}/{y}");

        ImageHit.PixelAt(15.5, 11.5, 16, 12, 16, 12, uniform: false, out x, out y);
        Check.That(x == 15 && y == 11, "die untere rechte den letzten", $"{x}/{y}");

        ImageHit.PixelAt(7.2, 3.9, 16, 12, 16, 12, uniform: false, out x, out y);
        Check.That(x == 7 && y == 3, "und dazwischen wird abgerundet", $"{x}/{y}");
    }

    /// <summary>
    /// Eingepasst liegen Raender daneben, und die gehoeren nicht zum Bild.
    ///
    /// Das ist der Fall, den man beim Bauen vergisst: Ein 16 zu 12 breites Bild in
    /// einem quadratischen Feld hat oben und unten je ein Achtel Rand. Wer ihn nicht
    /// abzieht, trifft ueberall zu weit oben.
    /// </summary>
    private static void FittedWithBars()
    {
        Check.Group("Eingepasst werden die Raender abgezogen");

        // 16x12 in ein Feld von 320x320: Massstab 20, also 240 hoch, 40 Rand je Seite.
        const double box = 320;

        Check.That(!ImageHit.PixelAt(160, 20, box, box, 16, 12, uniform: true, out _, out _),
                   "ein Klick in den oberen Rand trifft nichts");

        Check.That(!ImageHit.PixelAt(160, 300, box, box, 16, 12, uniform: true, out _, out _),
                   "in den unteren auch nicht");

        bool hit = ImageHit.PixelAt(10, 50, box, box, 16, 12, uniform: true, out int x, out int y);
        Check.That(hit, "die obere linke Ecke des Bildes trifft");
        Check.That(x == 0 && y == 0, "und zwar den ersten Bildpunkt", $"{x}/{y}");

        // Die Mitte des Feldes ist die Mitte des Bildes.
        ImageHit.PixelAt(160, 160, box, box, 16, 12, uniform: true, out x, out y);
        Check.That(x == 8 && y == 6, "die Mitte trifft die Mitte", $"{x}/{y}");

        // Und die letzte Spalte liegt am rechten Rand des Bildes, nicht des Feldes.
        ImageHit.PixelAt(310, 160, box, box, 16, 12, uniform: true, out x, out y);
        Check.That(x == 15, "die letzte Spalte liegt am Bildrand", $"{x}");

        // Hochkant herum: dann liegen die Raender links und rechts.
        Check.That(!ImageHit.PixelAt(5, 160, 320, 100, 16, 12, uniform: true, out _, out _),
                   "bei breitem Feld liegen die Raender seitlich");
    }

    /// <summary>
    /// Ausserhalb ist ausserhalb - und zwar auch knapp links davon.
    ///
    /// Hier haengt es am Abrunden statt am Schneiden: Bei -0,4 ergaebe das Schneiden
    /// eine Null, und der Klick zaehlte als Treffer auf die erste Spalte. Ein
    /// Bildpunkt Versatz an jeder linken Kante, und niemand kaeme darauf.
    /// </summary>
    private static void OutsideIsOutside()
    {
        Check.Group("Neben dem Bild trifft es nichts");

        Check.That(!ImageHit.PixelAt(-0.4, 5, 16, 12, 16, 12, uniform: false, out _, out _),
                   "knapp links daneben zaehlt nicht als erste Spalte");

        Check.That(!ImageHit.PixelAt(5, -0.4, 16, 12, 16, 12, uniform: false, out _, out _),
                   "knapp darueber ebenso");

        Check.That(!ImageHit.PixelAt(16.1, 5, 16, 12, 16, 12, uniform: false, out _, out _),
                   "und rechts hinaus");

        // Unmoegliche Groessen duerfen nicht werfen - ein Klick, bevor das Bild
        // steht, ist kein Ausnahmefall, sondern das, was beim Ausprobieren zuerst
        // passiert.
        Check.That(!ImageHit.PixelAt(5, 5, 0, 0, 16, 12, uniform: true, out _, out _),
                   "ohne Flaeche trifft nichts");

        Check.That(!ImageHit.PixelAt(5, 5, 100, 100, 0, 0, uniform: true, out _, out _),
                   "und ohne Bild auch nicht");
    }
}
