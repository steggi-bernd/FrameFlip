namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Die Deckung von Kryptomatte-Objekten als kleines Feld - fuer die Rueckmeldung beim Waehlen
/// (docs/Atelier-Arbeitsablauf.md, C3): das Objekt unter dem Zeiger hell ueberlagert, die
/// gewaehlten umrandet.
///
/// Gerechnet wird nur jeder <c>step</c>-te Bildpunkt, in der Mitte seines Feldes. Die Anzeige
/// ist eine Rueckmeldung und keine Maske - die Maske selbst rechnet weiter jeden Punkt.
/// </summary>
public static class CryptoCoverage
{
    /// <summary>Die Deckung der Kennungen je Feld, 0 bis 255 - Zeile fuer Zeile, <paramref name="columns"/> breit.</summary>
    public static byte[] Build(IReadOnlyList<FloatFrame> levels, IReadOnlyList<float> ids, int step, out int columns, out int rows)
    {
        step = Math.Max(1, step);

        int width = levels.Count > 0 ? levels[0].Width : 0;
        int height = levels.Count > 0 ? levels[0].Height : 0;

        int cols = columns = Math.Max(1, (width + step - 1) / step);
        rows = Math.Max(1, (height + step - 1) / step);

        var cover = new byte[cols * rows];
        if (width == 0 || height == 0 || ids.Count == 0) return cover;

        Parallel.For(0, rows, y =>
        {
            int py = Math.Min(height - 1, y * step + step / 2);

            for (int x = 0; x < cols; x++)
            {
                int px = Math.Min(width - 1, x * step + step / 2);
                cover[y * cols + x] = (byte)MathF.Round(Masking.Coverage(levels, ids, py * width + px) * 255f);
            }
        });

        return cover;
    }

    /// <summary>
    /// Der Umriss: Felder, die mehr als halb gedeckt sind und an ein weniger gedecktes grenzen -
    /// oder am Rand des Bildes liegen. 255 auf dem Umriss, sonst 0.
    /// </summary>
    public static byte[] Outline(byte[] cover, int columns, int rows)
    {
        var edge = new byte[cover.Length];

        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < columns && y < rows && cover[y * columns + x] >= 128;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (!Inside(x, y)) continue;

                bool border = x == 0 || y == 0 || x == columns - 1 || y == rows - 1 ||
                              !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);

                if (border) edge[y * columns + x] = 255;
            }
        }

        return edge;
    }
}
