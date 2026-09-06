using System.Windows;
using WinForms = System.Windows.Forms;

namespace FrameFlip.Views;

/// <summary>
/// Dafuer sorgen, dass ein Fenster dort aufgeht, wo man es auch bedienen kann.
///
/// Anlass war ein Fenster, das oben aus dem Bildschirm ragte: Die Titelleiste lag
/// ausserhalb, und damit war es weder zu verschieben noch zu schliessen. Das ist
/// die unangenehmste Art von Fehler - man kommt nicht mehr heran, um ihn zu
/// beheben.
///
/// Zwei Faelle fuehren dahin, und beide werden hier behandelt:
///
/// Eine gemerkte Lage kann ungueltig geworden sein - ein zweiter Bildschirm wurde
/// abgezogen, die Aufloesung geaendert, die Skalierung umgestellt. Deshalb wird
/// beim Anzeigen geprueft, nicht beim Speichern.
///
/// Und die Titelleiste muss sichtbar sein, nicht nur irgendein Teil des Fensters.
/// Ein Fenster, von dem nur die untere Haelfte im Bild ist, laesst sich anklicken,
/// aber nicht bewegen.
/// </summary>
public static class WindowPlacer
{
    /// <summary>Soviel vom Fenster muss waagerecht sichtbar sein, damit man es fassen kann.</summary>
    private const double MinVisible = 120;

    /// <summary>
    /// Eine gemerkte Lage anwenden - oder zentrieren, wenn sie nicht taugt.
    /// </summary>
    public static void Restore(Window window, double? left, double? top, double width, double height, bool maximized)
    {
        if (width > 0 && !double.IsNaN(width)) window.Width = Math.Max(window.MinWidth, width);
        if (height > 0 && !double.IsNaN(height)) window.Height = Math.Max(window.MinHeight, height);

        if (left is not double x || top is not double y || double.IsNaN(x) || double.IsNaN(y))
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = x;
        window.Top = y;

        EnsureVisible(window);

        // Erst danach maximieren: Sonst merkt sich Windows die ungueltige Lage als
        // Ort fuer das Wiederherstellen.
        if (maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Das Fenster in einen Bereich schieben, in dem man es anfassen kann.
    ///
    /// Gerechnet wird in Geraetepixeln, weil die Bildschirmgrenzen so vorliegen.
    /// Auf einem Bildschirm mit 150 % Skalierung sind WPF-Einheiten etwas anderes,
    /// und der Unterschied ist genau die Groessenordnung, in der ein Fenster aus
    /// dem Bild rutscht.
    /// </summary>
    public static void EnsureVisible(Window window)
    {
        try
        {
            double scale = ScaleFor(window);

            // Der Bildschirm, auf dem das Fenster ueberwiegend liegt. Auf einem
            // Rechner mit zwei Schirmen ist das nicht zwingend der primaere, und
            // dessen Arbeitsflaeche waere hier die falsche Grundlage.
            var bounds = WinForms.Screen.FromRectangle(new System.Drawing.Rectangle(
                (int)(window.Left * scale), (int)(window.Top * scale),
                (int)(window.Width * scale), (int)(window.Height * scale))).WorkingArea;

            var area = new Rect(bounds.Left / scale, bounds.Top / scale,
                                bounds.Width / scale, bounds.Height / scale);

            var fixedRect = Clamp(new Rect(window.Left, window.Top, window.Width, window.Height), area);

            window.Left = fixedRect.Left;
            window.Top = fixedRect.Top;
            window.Width = fixedRect.Width;
            window.Height = fixedRect.Height;
        }
        catch (Exception)
        {
            // Ohne verlaessliche Bildschirmangaben lieber zentrieren als raten.
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>
    /// Die eigentliche Rechnung, ohne Fenster und ohne Bildschirm.
    ///
    /// Getrennt, damit sie pruefbar ist: Der Fehler, der das hier ausgeloest hat -
    /// ein Fenster oberhalb des Bildschirms, unerreichbar - laesst sich als
    /// Zusicherung hinschreiben, sobald keine Fensterklasse mehr im Weg steht.
    /// </summary>
    public static Rect Clamp(Rect window, Rect area)
    {
        double width = Math.Min(window.Width, area.Width);
        double height = Math.Min(window.Height, area.Height);

        double left = window.Left;
        double top = window.Top;

        // Nach UNTEN darf ein Fenster hinausragen - die Titelleiste bleibt oben und
        // damit greifbar. Nach oben nicht: Dann ist sie weg, und das Fenster laesst
        // sich weder verschieben noch schliessen. Genau das war der Fehler.
        if (top < area.Top) top = area.Top;
        if (top > area.Bottom - MinVisible) top = Math.Max(area.Top, area.Bottom - MinVisible);

        // Waagerecht muss ein Stueck sichtbar bleiben, damit man es fassen kann -
        // egal von welcher Seite es hinausragt.
        if (left + MinVisible > area.Right) left = area.Right - MinVisible;
        if (left + width - MinVisible < area.Left) left = area.Left - width + MinVisible;

        return new Rect(left, top, width, height);
    }

    /// <summary>Geraetepixel je WPF-Einheit. 1 bei 100 %, 1,5 bei 150 %.</summary>
    private static double ScaleFor(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        double factor = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        return factor > 0 ? factor : 1.0;
    }
}
