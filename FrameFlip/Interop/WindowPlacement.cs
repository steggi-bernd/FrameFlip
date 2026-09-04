namespace FrameFlip.Interop;

/// <summary>
/// Groesse und Position des Vorschaufensters nach QuickLook-Vorbild:
/// inhaltsangepasst, gedeckelt auf einen Anteil der Arbeitsflaeche, zentriert auf
/// dem Monitor des ausloesenden Fensters - und nie hochskaliert.
///
/// Gerechnet wird durchgehend in Geraetepixeln. Die Umrechnung nach DIP passiert
/// genau einmal, naemlich im Fenster selbst; auf Mischsetups mit unterschiedlicher
/// Skalierung je Monitor geht das sonst schief.
///
/// Bewusst ohne Win32-Aufrufe, damit die Rechnung ohne echten Bildschirm pruefbar
/// bleibt. Den Monitor ermittelt <see cref="NativeMethods.GetWorkArea"/>.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Hoehe der Kopfleiste in DIP - muss zu ViewerWindow.xaml passen.</summary>
    public const double HeaderHeightDip = 36.0;

    /// <summary>Anteil der Arbeitsflaeche, den das Fenster hoechstens einnimmt.</summary>
    public const double MaxWorkAreaRatio = 0.9;

    public const double MinWidthDip = 400.0;
    public const double MinHeightDip = 300.0;

    /// <param name="work">Arbeitsflaeche des Zielmonitors in Geraetepixeln.</param>
    /// <param name="scale">Skalierungsfaktor dieses Monitors, etwa 1,5 bei 150 %.</param>
    /// <param name="sourceWidth">Native Bildbreite in Pixeln.</param>
    /// <param name="sourceHeight">Native Bildhoehe in Pixeln.</param>
    public static PixelRect Compute(PixelRect work, double scale, int sourceWidth, int sourceHeight)
    {
        if (scale <= 0) scale = 1.0;
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);

        int header = (int)Math.Round(HeaderHeightDip * scale);
        int minWidth = (int)Math.Round(MinWidthDip * scale);
        int minHeight = (int)Math.Round(MinHeightDip * scale);

        // Der Deckel gilt fuer das ganze Fenster, die Kopfleiste geht also vom Platz
        // fuer das Bild ab. Ohne diesen Abzug ragt das Fenster bei bildschirmfuellenden
        // Sequenzen unten aus der Arbeitsflaeche heraus.
        double maxWidth = work.Width * MaxWorkAreaRatio;
        double maxHeight = work.Height * MaxWorkAreaRatio - header;

        // Nie hochskalieren: ein kleines Bild ergibt ein kleines Fenster. Genau das
        // laesst QuickLook leicht wirken.
        double fit = Math.Min(1.0, Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight));
        if (!(fit > 0)) fit = 1.0;

        int contentWidth = (int)Math.Round(sourceWidth * fit);
        int contentHeight = (int)Math.Round(sourceHeight * fit);

        int windowWidth = Math.Max(minWidth, contentWidth);
        int windowHeight = Math.Max(minHeight, contentHeight + header);

        // Die Mindestgroesse darf den Deckel nicht aushebeln - auf sehr kleinen
        // Bildschirmen gewinnt die Arbeitsflaeche.
        windowWidth = Math.Min(windowWidth, work.Width);
        windowHeight = Math.Min(windowHeight, work.Height);

        return new PixelRect(
            work.X + (work.Width - windowWidth) / 2,
            work.Y + (work.Height - windowHeight) / 2,
            windowWidth,
            windowHeight);
    }
}
