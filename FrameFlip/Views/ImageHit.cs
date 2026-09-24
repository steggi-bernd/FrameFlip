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

        if (!Exact(pointX, pointY, elementWidth, elementHeight,
                   pixelWidth, pixelHeight, uniform, out double fx, out double fy))
        {
            return false;
        }

        // Abrunden und nicht schneiden: Bei einem Punkt links des Bildes ergaebe das
        // Schneiden eine Null, und der Klick zaehlte als Treffer auf die erste Spalte.
        x = (int)Math.Floor(fx);
        y = (int)Math.Floor(fy);

        return x >= 0 && y >= 0 && x < pixelWidth && y < pixelHeight;
    }

    /// <summary>
    /// Der Massstab: wieviele Punkte der Flaeche ein Bildpunkt einnimmt.
    ///
    /// Er steht hier und nicht an den drei Stellen, die ihn brauchen. Dreimal
    /// dieselbe Formel ist dreimal dieselbe Gelegenheit, sie auseinanderlaufen zu
    /// lassen - und wenn Klick und Zeichnung auseinanderlaufen, zieht man an einer
    /// Ecke, die nicht dort ist, wo sie aussieht. Genau danach sah der alte Eintrag
    /// "der Rahmen sitzt an der falschen Stelle" aus.
    ///
    /// Null heisst: es gibt keinen brauchbaren Massstab, also auch keine Abbildung.
    /// </summary>
    public static double Scale(double elementWidth, double elementHeight,
                               int pixelWidth, int pixelHeight, bool uniform)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return 0;
        if (elementWidth <= 0 || elementHeight <= 0) return 0;

        // Ohne Einpassung steht ein Bildpunkt auf einem Punkt - hundert Prozent, und
        // das Bild sitzt mittig, notfalls ueber den Rand hinaus.
        if (!uniform) return 1.0;

        // Der kleinere der beiden Faktoren: Das Bild passt ganz hinein, und in der
        // anderen Richtung bleibt Rand.
        double scale = Math.Min(elementWidth / pixelWidth, elementHeight / pixelHeight);

        return scale > 0 ? scale : 0;
    }

    /// <summary>
    /// Wie <see cref="PixelAt"/>, aber ungerundet und OHNE Grenze.
    ///
    /// Gebraucht beim Ziehen: Eine Ebene darf ueber den Rand hinauslaufen, und die
    /// Maus darf dabei aus dem Bild geraten. Ein Treffertest waere dort die falsche
    /// Frage - es wird nicht gefragt, worauf gezeigt wird, sondern wohin gezogen.
    /// </summary>
    public static bool Exact(double pointX, double pointY,
                             double elementWidth, double elementHeight,
                             int pixelWidth, int pixelHeight,
                             bool uniform,
                             out double x, out double y)
    {
        x = y = 0;

        double scale = Scale(elementWidth, elementHeight, pixelWidth, pixelHeight, uniform);
        if (scale <= 0) return false;

        x = (pointX - (elementWidth - pixelWidth * scale) / 2) / scale;
        y = (pointY - (elementHeight - pixelHeight * scale) / 2) / scale;

        return true;
    }

    /// <summary>
    /// Der Rueckweg: von einem Ort im Bild zu einem Punkt auf dem Bildelement.
    ///
    /// Gebraucht zum Zeichnen. Beide Richtungen muessen genau zueinander passen -
    /// sonst liegt der Rahmen um eine Ebene woanders als die Ebene, und man zieht an
    /// einer Ecke, die nicht da ist, wo sie aussieht.
    /// </summary>
    public static bool PointAt(double imageX, double imageY,
                               double elementWidth, double elementHeight,
                               int pixelWidth, int pixelHeight,
                               bool uniform,
                               out double x, out double y)
    {
        x = y = 0;

        double scale = Scale(elementWidth, elementHeight, pixelWidth, pixelHeight, uniform);
        if (scale <= 0) return false;

        x = (elementWidth - pixelWidth * scale) / 2 + imageX * scale;
        y = (elementHeight - pixelHeight * scale) / 2 + imageY * scale;

        return true;
    }
}
