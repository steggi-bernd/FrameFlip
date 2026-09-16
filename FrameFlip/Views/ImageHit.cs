namespace FrameFlip.Views;

/// <summary>
/// Wo auf dem Bild ein Klick gelandet ist.
///
/// Steht als eigene Rechnung da und nicht in der Seite, weil sie die Art Fehler
/// beherbergt, die man beim Ausprobieren fuer etwas anderes haelt: Liegt die
/// Umrechnung daneben, waehlt man das Objekt neben dem, auf das man gezeigt hat -
/// und sucht den Fehler bei der Kryptomatte statt bei der Geometrie.
/// </summary>
public static class ImageHit
{
    /// <summary>
    /// Rechnet einen Punkt auf dem Bildelement in einen Bildpunkt um. False, wenn
    /// er neben dem Bild liegt.
    /// </summary>
    /// <param name="uniform">
    /// True, wenn das Bild eingepasst gezeigt wird. Dann ist es verkleinert UND
    /// mittig gesetzt - links und rechts (oder oben und unten) liegen Raender, die
    /// nicht zum Bild gehoeren. Bei 100 Prozent steht ein Bildpunkt auf einem Punkt,
    /// und es gibt keine Raender.
    /// </param>
    public static bool PixelAt(double pointX, double pointY,
                               double elementWidth, double elementHeight,
                               int pixelWidth, int pixelHeight,
                               bool uniform,
                               out int x, out int y)
    {
        x = y = 0;

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (elementWidth <= 0 || elementHeight <= 0) return false;

        double scale = 1.0;

        if (uniform)
        {
            // Der kleinere der beiden Faktoren: Das Bild passt ganz hinein, und in
            // der anderen Richtung bleibt Rand.
            scale = Math.Min(elementWidth / pixelWidth, elementHeight / pixelHeight);
            if (scale <= 0) return false;
        }

        double left = (elementWidth - pixelWidth * scale) / 2;
        double top = (elementHeight - pixelHeight * scale) / 2;

        // Abrunden und nicht schneiden: Bei einem Punkt links des Bildes ergaebe das
        // Schneiden eine Null, und der Klick zaehlte als Treffer auf die erste Spalte.
        x = (int)Math.Floor((pointX - left) / scale);
        y = (int)Math.Floor((pointY - top) / scale);

        return x >= 0 && y >= 0 && x < pixelWidth && y < pixelHeight;
    }
}
