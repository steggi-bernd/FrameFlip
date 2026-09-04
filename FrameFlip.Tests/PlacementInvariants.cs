using FrameFlip.Interop;

namespace FrameFlip.Tests;

/// <summary>Fenstergroesse und -position nach QuickLook-Vorbild.</summary>
public static class PlacementInvariants
{
    public static void Run()
    {
        Check.Group("Fensterplatzierung");

        // 1080p-Arbeitsflaeche ohne Skalierung, Taskleiste abgezogen.
        var work = new PixelRect(0, 0, 1920, 1040);

        // Ein kleines Bild ergibt ein kleines Fenster - nie hochskalieren.
        var small = WindowPlacement.Compute(work, 1.0, 640, 360);
        Check.That(small.Width == 640, "kleines Bild wird nicht aufgeblasen", $"{small.Width} px breit");
        Check.That(small.Height == 360 + 36, "Kopfleiste kommt zur Bildhoehe dazu", $"{small.Height} px hoch");

        // Ein grosses Bild wird gedeckelt, aber das Seitenverhaeltnis bleibt.
        var large = WindowPlacement.Compute(work, 1.0, 3840, 2160);
        Check.That(large.Width <= work.Width * 0.9 + 1,
            "Breite bleibt unter 90 % der Arbeitsflaeche", $"{large.Width} von {work.Width}");
        Check.That(large.Height <= work.Height * 0.9 + 1,
            "Hoehe bleibt unter 90 % der Arbeitsflaeche", $"{large.Height} von {work.Height}");

        double ratioIn = 3840 / 2160.0;
        double ratioOut = large.Width / (double)(large.Height - 36);
        Check.Near(ratioOut, ratioIn, 0.01, "Seitenverhaeltnis bleibt erhalten");

        // Winzige Bilder bekommen die Mindestgroesse.
        var tiny = WindowPlacement.Compute(work, 1.0, 64, 64);
        Check.That(tiny.Width >= 400, "Mindestbreite 400 DIP", $"{tiny.Width} px");
        Check.That(tiny.Height >= 300, "Mindesthoehe 300 DIP", $"{tiny.Height} px");

        // Zentriert auf der Arbeitsflaeche des betreffenden Monitors, nicht auf dem
        // Primaerbildschirm: ein Monitor links daneben hat negative Koordinaten.
        var secondary = new PixelRect(-1920, 0, 1920, 1040);
        var placed = WindowPlacement.Compute(secondary, 1.0, 1920, 1080);
        Check.Near(placed.X + placed.Width / 2.0, secondary.X + secondary.Width / 2.0, 1.0,
            "horizontal auf dem Zielmonitor zentriert");
        Check.Near(placed.Y + placed.Height / 2.0, secondary.Y + secondary.Height / 2.0, 1.0,
            "vertikal auf dem Zielmonitor zentriert");
        Check.That(placed.X < 0, "Fenster liegt auf dem linken Monitor", $"X = {placed.X}");

        // Skalierter Monitor: Mindestgroesse und Kopfleiste sind DIP-Angaben und
        // muessen in Geraetepixeln mitwachsen.
        var scaled = WindowPlacement.Compute(new PixelRect(0, 0, 2880, 1560), 1.5, 64, 64);
        Check.That(scaled.Width >= 400 * 1.5, "Mindestbreite skaliert mit der Bildschirmskalierung",
            $"{scaled.Width} px bei 150 %");
        Check.That(scaled.Height >= 300 * 1.5, "Mindesthoehe skaliert mit der Bildschirmskalierung",
            $"{scaled.Height} px bei 150 %");

        var scaledLarge = WindowPlacement.Compute(new PixelRect(0, 0, 2880, 1560), 1.5, 1920, 1080);
        Check.That(scaledLarge.Width == 1920,
            "ein Bild, das bei 150 % noch hineinpasst, behaelt seine Pixelgroesse",
            $"{scaledLarge.Width} px");

        // Sehr kleine Arbeitsflaeche: der Deckel schlaegt die Mindestgroesse, sonst
        // ragt das Fenster aus dem Bildschirm.
        var cramped = WindowPlacement.Compute(new PixelRect(0, 0, 320, 240), 1.0, 1920, 1080);
        Check.That(cramped.Width <= 320 && cramped.Height <= 240,
            "Fenster bleibt auch auf winzigen Bildschirmen sichtbar",
            $"{cramped.Width}x{cramped.Height}");
        Check.That(cramped.X >= 0 && cramped.Y >= 0, "und liegt nicht ausserhalb der Arbeitsflaeche");
    }
}
