using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Abbildung zwischen Maus und Bild - beide Richtungen.
///
/// Sie ist winzig und war bis hierher ungeprueft, obwohl zwei der aeltesten
/// Fehlereintraege genau an ihr hingen: "der Rahmen sitzt an der falschen Stelle"
/// und "Ziehen wirkt auf die falsche Ebene". Beides sieht gleich aus, wenn Klick und
/// Zeichnung dieselbe Formel zweimal geschrieben haben und eine der beiden Fassungen
/// abweicht - man zieht an einer Ecke, die nicht dort ist, wo sie aussieht.
///
/// Geprueft wird deshalb vor allem das eine: HIN UND ZURUECK muss derselbe Punkt
/// herauskommen. Ein Rundgang ist der einzige Test, der beide Richtungen zugleich
/// festhaelt; einzeln gemessene Werte lassen sich in beiden Fassungen gleichzeitig
/// verbiegen.
/// </summary>
public static class ImageHitInvariants
{
    public static void Run()
    {
        TheRoundTripHolds();
        FittingLeavesBorders();
        FullSizeOverflowsEvenly();
        NothingDividesByZero();
    }

    /// <summary>
    /// Vom Bildpunkt auf die Flaeche und zurueck - derselbe Punkt.
    ///
    /// In beiden Darstellungen, denn es gibt genau zwei: eingepasst mit Raendern, und
    /// hundert Prozent, wo ein Bildpunkt auf einem Punkt steht.
    /// </summary>
    private static void TheRoundTripHolds()
    {
        Check.Group("Hin und zurueck ergibt denselben Punkt");

        (double W, double H, int PW, int PH, bool Fit, string Name)[] cases =
        {
            (800, 600, 1920, 1080, true, "eingepasst, breiter als hoch"),
            (600, 800, 1920, 1080, true, "eingepasst, hoeher als breit"),
            (500, 500, 500, 500, true, "eingepasst, genau passend"),
            (800, 600, 1920, 1080, false, "hundert Prozent, Bild groesser"),
            (800, 600, 200, 150, false, "hundert Prozent, Bild kleiner"),
        };

        foreach (var (w, h, pw, ph, fit, name) in cases)
        {
            double worst = 0;

            // Auch AUSSERHALB des Bildes: Beim Ziehen laeuft eine Ebene ueber den
            // Rand, und dort muss die Umrechnung genauso stimmen wie in der Mitte.
            foreach (double ix in new[] { -50.0, 0.0, 1.0, pw / 3.0, pw - 1.0, pw + 40.0 })
            {
                foreach (double iy in new[] { -25.0, 0.0, ph / 2.0, ph - 1.0, ph + 17.0 })
                {
                    if (!ImageHit.PointAt(ix, iy, w, h, pw, ph, fit,
                                          out double sx, out double sy))
                    {
                        Check.That(false, $"{name}: die Hinrichtung antwortet");
                        continue;
                    }

                    if (!ImageHit.Exact(sx, sy, w, h, pw, ph, fit,
                                        out double bx, out double by))
                    {
                        Check.That(false, $"{name}: die Rueckrichtung antwortet");
                        continue;
                    }

                    worst = Math.Max(worst, Math.Max(Math.Abs(bx - ix), Math.Abs(by - iy)));
                }
            }

            Console.WriteLine($"         {name}: groesste Abweichung {worst:0.########}");

            Check.That(worst < 1e-9, $"{name} - der Rundgang schliesst sich",
                       $"{worst:0.########} Bildpunkte");
        }
    }

    /// <summary>
    /// Eingepasst heisst: verkleinert UND mittig, mit Raendern, die nicht zum Bild
    /// gehoeren.
    ///
    /// Ein Klick in den Rand ist KEIN Treffer. Ihn als Treffer auf die erste Spalte
    /// zu zaehlen waere der naheliegende Fehler - und eine Pipette, die neben dem
    /// Bild eine Farbe liest, ist schlimmer als eine, die schweigt.
    /// </summary>
    private static void FittingLeavesBorders()
    {
        Check.Group("Eingepasst bleiben Raender, und die gehoeren nicht zum Bild");

        // Quadratisches Bild in einer breiten Flaeche: links und rechts Rand.
        const double w = 1000, h = 500;
        const int pw = 400, ph = 400;

        double scale = ImageHit.Scale(w, h, pw, ph, uniform: true);

        Check.Near(scale, 500.0 / 400.0, 1e-9,
                   "der Massstab kommt aus der engeren Richtung");

        double side = (w - pw * scale) / 2;

        Check.That(side > 1, "und links bleibt ein Rand", $"{side:0.0} Punkte");

        Check.That(!ImageHit.PixelAt(side / 2, h / 2, w, h, pw, ph, true, out _, out _),
                   "ein Klick in den Rand ist kein Treffer");

        Check.That(ImageHit.PixelAt(side + 2, h / 2, w, h, pw, ph, true, out int x, out _),
                   "zwei Punkte weiter innen schon");

        Check.That(x == 1, "und zwar gleich am linken Bildrand", $"Spalte {x}");

        // Die Mitte der Flaeche ist die Mitte des Bildes - bei jeder Einpassung.
        Check.That(ImageHit.PixelAt(w / 2, h / 2, w, h, pw, ph, true, out int mx, out int my),
                   "die Mitte der Flaeche trifft");

        Check.That(Math.Abs(mx - pw / 2) <= 1 && Math.Abs(my - ph / 2) <= 1,
                   "und liegt in der Mitte des Bildes", $"{mx},{my}");

        // Knapp links des Bildes muss ABGERUNDET werden, nicht abgeschnitten: Sonst
        // ergaebe -0,4 eine Null und der Klick zaehlte als Treffer auf Spalte 0.
        ImageHit.Exact(side - scale / 2, h / 2, w, h, pw, ph, true, out double ex, out _);

        Check.That(ex is > -1 and < 0, "knapp daneben ist der Bildpunkt negativ",
                   $"{ex:0.00}");

        Check.That(!ImageHit.PixelAt(side - scale / 2, h / 2, w, h, pw, ph, true, out _, out _),
                   "und faellt damit heraus statt auf Spalte null");
    }

    /// <summary>
    /// Bei hundert Prozent steht ein Bildpunkt auf einem Punkt - und ein zu grosses
    /// Bild wird beschnitten, auf beiden Seiten gleich weit.
    /// </summary>
    private static void FullSizeOverflowsEvenly()
    {
        Check.Group("Hundert Prozent: ein Bildpunkt auf einem Punkt");

        const double w = 800, h = 600;
        const int pw = 1000, ph = 900;

        Check.Near(ImageHit.Scale(w, h, pw, ph, uniform: false), 1.0, 1e-9,
                   "der Massstab ist eins");

        ImageHit.Exact(0, 0, w, h, pw, ph, false, out double left, out double top);
        ImageHit.Exact(w, h, w, h, pw, ph, false, out double right, out double bottom);

        Console.WriteLine($"         sichtbar von {left:0.0} bis {right:0.0} von {pw}");

        // Ein zu grosses Bild wird bei hundert Prozent BESCHNITTEN, nicht angehaengt:
        // Die Flaeche ist ein Fenster auf seine Mitte, und links wie rechts faellt
        // gleich viel weg. Der linke Punkt der Flaeche ist deshalb nicht Bildpunkt
        // null, sondern der erste noch sichtbare.
        Check.Near(left, (pw - w) / 2, 1e-9, "links faellt die Haelfte des Ueberhangs weg");
        Check.Near(pw - right, (pw - w) / 2, 1e-9, "und rechts genauso viel");
        Check.Near(top, (ph - h) / 2, 1e-9, "oben ebenso");
        Check.Near(ph - bottom, (ph - h) / 2, 1e-9, "und unten");

        Check.Near(right - left, w, 1e-9,
                   "sichtbar ist genau so viel, wie die Flaeche breit ist");

        // Ein Punkt weiter rechts ist genau ein Bildpunkt weiter rechts.
        ImageHit.Exact(100, 100, w, h, pw, ph, false, out double ax, out _);
        ImageHit.Exact(101, 100, w, h, pw, ph, false, out double bx, out _);

        Check.Near(bx - ax, 1.0, 1e-9, "und ein Punkt ist ein Bildpunkt");
    }

    /// <summary>
    /// Ohne brauchbare Masse gibt es keine Abbildung - und keine Division durch null.
    ///
    /// Das ist kein erfundener Fall: Eine Flaeche hat vor ihrem ersten Aufbau die
    /// Groesse null, und genau dann laufen Anfasser und Zeichnung schon einmal durch.
    /// </summary>
    private static void NothingDividesByZero()
    {
        Check.Group("Ohne Masse gibt es keine Abbildung");

        (double W, double H, int PW, int PH, string Name)[] bad =
        {
            (0, 600, 100, 100, "Flaeche ohne Breite"),
            (800, 0, 100, 100, "Flaeche ohne Hoehe"),
            (800, 600, 0, 100, "Bild ohne Breite"),
            (800, 600, 100, 0, "Bild ohne Hoehe"),
            (-4, 600, 100, 100, "negative Breite"),
        };

        foreach (var (w, h, pw, ph, name) in bad)
        {
            foreach (bool fit in new[] { true, false })
            {
                Check.Near(ImageHit.Scale(w, h, pw, ph, fit), 0, 1e-12,
                           $"{name}: kein Massstab");

                Check.That(!ImageHit.Exact(10, 10, w, h, pw, ph, fit, out _, out _),
                           $"{name}: keine Umrechnung");

                Check.That(!ImageHit.PixelAt(10, 10, w, h, pw, ph, fit, out _, out _),
                           $"{name}: kein Treffer");

                Check.That(!ImageHit.PointAt(10, 10, w, h, pw, ph, fit, out _, out _),
                           $"{name}: kein Rueckweg");
            }
        }
    }
}
