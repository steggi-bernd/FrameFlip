using System.Windows;
using System.Windows.Media;
using FrameFlip.Views;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>Invarianten 3, 4 und 5 der Referenzliste.</summary>
public static class ZoomInvariants
{
    private const int NativeW = 1920;
    private const int NativeH = 1080;

    public static void Run()
    {
        AnchorStaysPut();
        SmallContentIsCentred();
        RefineDoesNotJump();
        RangeAndModes();
    }

    private static ZoomController Make(int contentW, int contentH, double dpi = 1.0,
                                       double viewportW = 800, double viewportH = 600)
    {
        var view = new ZoomController();
        view.SetNativeSize(NativeW, NativeH);
        view.SetDpi(dpi);
        view.SetViewport(new Size(viewportW, viewportH));
        view.SetContent(new Size(contentW, contentH), preserveZoom: false);
        return view;
    }

    /// <summary>Bildpunkt unter einer Viewport-Koordinate, in Inhaltspixeln.</summary>
    private static Point ContentPointAt(ZoomController view, Point anchor)
    {
        var inverse = view.Matrix;
        inverse.Invert();
        return inverse.Transform(anchor);
    }

    // ---------------------------------------------------------------- Invariante 3

    /// <summary>
    /// Nach dem Zoomen liegt derselbe Bildpunkt unter dem Anker wie vorher.
    /// Geprueft wird nur ausserhalb der Anschlaege - am Rand verschiebt die
    /// Begrenzung den Punkt zwangslaeufig, und genau das ist gewollt.
    /// </summary>
    private static void AnchorStaysPut()
    {
        Check.Group("Invariante 3 - der Punkt unter dem Zeiger bleibt stehen");

        foreach (double dpi in new[] { 1.0, 1.5 })
        {
            foreach (var anchor in new[] { new Point(400, 300), new Point(210, 120), new Point(650, 480) })
            {
                var view = Make(1280, 720, dpi);

                // Erst weit genug hineinzoomen, damit der Inhalt groesser als der
                // Viewport ist und die Begrenzung nicht mehr eingreift.
                view.SetZoom(1.5, new Point(400, 300));

                var before = ContentPointAt(view, anchor);
                double displayBefore = view.Content.Width * view.Matrix.M11;

                view.ZoomBy(anchor, 1.2);

                var after = ContentPointAt(view, anchor);
                double displayAfter = view.Content.Width * view.Matrix.M11;

                // Nur werten, wenn der Zoom auch wirklich stattgefunden hat.
                if (Math.Abs(displayAfter - displayBefore) < 0.01) continue;

                double drift = Math.Max(Math.Abs(after.X - before.X), Math.Abs(after.Y - before.Y));
                Check.That(drift <= 0.5,
                    $"Anker ({anchor.X},{anchor.Y}) bei DPI {dpi} bleibt ortsfest",
                    $"Abweichung {drift:0.###} px");
            }
        }
    }

    // ---------------------------------------------------------------- Invariante 4

    /// <summary>Ist der Inhalt kleiner als der Viewport, steht er exakt mittig.</summary>
    private static void SmallContentIsCentred()
    {
        Check.Group("Invariante 4 - kleiner Inhalt wird exakt zentriert");

        foreach (double dpi in new[] { 1.0, 1.25, 2.0 })
        {
            var view = new ZoomController();
            view.SetNativeSize(640, 360);          // kleiner als der Viewport
            view.SetDpi(dpi);
            view.SetViewport(new Size(800, 600));
            view.SetContent(new Size(640, 360), preserveZoom: false);

            double contentW = view.Content.Width * view.Matrix.M11;
            double contentH = view.Content.Height * view.Matrix.M22;

            Check.Near(view.Matrix.OffsetX, (800 - contentW) / 2, 1e-9,
                $"OffsetX exakt zentriert bei DPI {dpi}");
            Check.Near(view.Matrix.OffsetY, (600 - contentH) / 2, 1e-9,
                $"OffsetY exakt zentriert bei DPI {dpi}");
            Check.That(!view.CanPan, $"kein Verschieben moeglich bei DPI {dpi}");

            // Ein Verschiebeversuch darf daran nichts aendern.
            view.Pan(new Vector(120, 80));
            Check.Near(view.Matrix.OffsetX, (800 - contentW) / 2, 1e-9,
                $"Verschieben aendert nichts bei DPI {dpi}");
        }
    }

    // ---------------------------------------------------------------- Invariante 5

    /// <summary>
    /// Vor und nach dem Nachschaerfen ist die Anzeigegroesse identisch. Das ist die
    /// Bedingung dafuer, dass die hoehere Aufloesung ohne sichtbaren Sprung eintrifft.
    /// </summary>
    private static void RefineDoesNotJump()
    {
        Check.Group("Invariante 5 - Nachschaerfen ohne Sprung");

        foreach (double dpi in new[] { 1.0, 1.5 })
        {
            var view = Make(1232, 693, dpi);
            view.SetZoom(1.4, new Point(400, 300));

            double displayBefore = view.Content.Width * view.Matrix.M11;
            double zoomBefore = view.Zoom;
            double offsetXBefore = view.Matrix.OffsetX;
            double offsetYBefore = view.Matrix.OffsetY;

            // Der Nachschaerf-Schritt: mehr Pixel im Puffer, gleicher Massstab.
            view.SetContent(new Size(NativeW, NativeH), preserveZoom: true);

            double displayAfter = view.Content.Width * view.Matrix.M11;

            Check.Near(displayAfter, displayBefore, 1e-6,
                $"Anzeigebreite unveraendert bei DPI {dpi}");
            Check.Near(view.Zoom, zoomBefore, 1e-9,
                $"absoluter Massstab unveraendert bei DPI {dpi}");
            Check.Near(view.Matrix.OffsetX, offsetXBefore, 1e-6,
                $"Bildposition X unveraendert bei DPI {dpi}");
            Check.Near(view.Matrix.OffsetY, offsetYBefore, 1e-6,
                $"Bildposition Y unveraendert bei DPI {dpi}");
        }

        // Auch der umgekehrte Weg - herauszoomen laesst den Puffer schrumpfen.
        var back = Make(NativeW, NativeH);
        back.SetZoom(0.9, new Point(400, 300));
        double before = back.Content.Width * back.Matrix.M11;
        back.SetContent(new Size(1232, 693), preserveZoom: true);
        Check.Near(back.Content.Width * back.Matrix.M11, before, 1e-6,
            "Anzeigebreite auch beim Schrumpfen des Puffers unveraendert");
    }

    // ---------------------------------------------------------------- Bedienvorgaben

    private static void RangeAndModes()
    {
        Check.Group("Zoombereich und Betriebsarten");

        var view = Make(NativeW, NativeH);

        Check.That(view.IsFit, "startet in der Einpassung");
        Check.Near(view.Zoom, view.FitZoom, 1e-9, "Startmassstab ist der Einpassmassstab");
        Check.That(view.FitZoom <= 1.0, "Einpassung skaliert nie hoch");

        // Obergrenze 800 %.
        for (int i = 0; i < 40; i++) view.ZoomBy(new Point(400, 300), 1.2);
        Check.Near(view.Zoom, ZoomController.MaxZoom, 1e-6, "Obergrenze liegt bei 800 %");

        // Untergrenze: nicht weiter heraus als die Einpassung.
        for (int i = 0; i < 60; i++) view.ZoomBy(new Point(400, 300), 1 / 1.2);
        Check.Near(view.Zoom, view.MinZoom, 1e-6, "Untergrenze ist die Einpassung");

        // Doppelklick wechselt zwischen Einpassung und 100 % und wieder zurueck.
        var toggle = Make(NativeW, NativeH);
        toggle.ToggleFitAndActual(new Point(400, 300));
        Check.Near(toggle.Zoom, 1.0, 1e-6, "Doppelklick fuehrt auf 100 %");
        toggle.ToggleFitAndActual(new Point(400, 300));
        Check.Near(toggle.Zoom, toggle.FitZoom, 1e-6, "erneuter Doppelklick fuehrt zurueck zur Einpassung");

        // Ein kleines Bild wird nicht aufgeblasen, und der Wechsel bleibt sinnvoll.
        var small = new ZoomController();
        small.SetNativeSize(320, 200);
        small.SetDpi(1.0);
        small.SetViewport(new Size(800, 600));
        small.SetContent(new Size(320, 200), preserveZoom: false);
        Check.Near(small.Zoom, 1.0, 1e-9, "kleines Bild bleibt bei 100 %");
        Check.That(!small.CanPan, "kleines Bild laesst sich nicht verschieben");

        // Der Massstab ueberlebt einen Frame-Wechsel gleicher Groesse.
        var stable = Make(1232, 693);
        stable.SetZoom(2.0, new Point(400, 300));
        var matrixBefore = stable.Matrix;
        stable.SetContent(new Size(1232, 693), preserveZoom: true);
        Check.That(stable.Matrix.Equals(matrixBefore), "Zoomzustand ueberlebt den Frame-Wechsel");
    }
}
