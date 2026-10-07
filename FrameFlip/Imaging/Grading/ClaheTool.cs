using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Oertlicher Ausgleich, CLAHE (docs/Atelier-Werkzeugplan.md, W2e): Das Bild wird in Kacheln
/// geteilt, jede Kachel wird fuer sich ausgeglichen, und zwischen den Mitten der Kacheln wird
/// weich uebergeblendet. So holt es Zeichnung aus Tiefen und Lichtern, ohne dass eine grosse
/// Flaeche den Ausgleich des ganzen Bildes bestimmt.
///
/// Die Grenze ist das "contrast limited": Keine Helligkeit darf in einer Kachel mehr als das
/// <see cref="Limit"/>-fache ihres gleichen Anteils bekommen, der Rest wird auf alle verteilt.
/// Ohne sie verstaerkte eine ruhige Flaeche ihr eigenes Rauschen bis zum Rand.
///
/// Ein Durchgang ueber das fertige Bild, weil jede Kachel ihre Nachbarschaft kennen muss. Die
/// Kacheln sind eine Anzahl, keine Groesse - auf dem Gitter der groben Vorschau sind es dieselben,
/// und das grobe Ergebnis ist dasselbe, nur groeber.
/// </summary>
public sealed class ClaheTool : ICoarseFramePass
{
    public const string KindName = "clahe";

    public string Kind => KindName;

    /// <summary>Wie weit der Ausgleich wirkt, 0 bis 1.</summary>
    public float Amount { get; set; }

    /// <summary>Wie viele Kacheln an der langen Seite stehen.</summary>
    public int Tiles { get; set; } = 8;

    /// <summary>Die Grenze je Helligkeit, als Vielfaches des gleichen Anteils.</summary>
    public float Limit { get; set; } = 2.5f;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.001f;

    public void Prepare()
    {
    }

    public void ApplyCoarse(IntPtr pixels, int width, int height, int stride, int number, int step)
        => Apply(pixels, width, height, stride, number);

    public unsafe void Apply(IntPtr pixels, int width, int height, int stride, int number = 0)
    {
        if (IsNeutral || width < 2 || height < 2) return;

        float amount = Math.Clamp(Amount, 0f, 1f);
        float limit = Math.Clamp(Limit, 1f, 16f);
        int side = Math.Max(1, (int)MathF.Ceiling(Math.Max(width, height) / (float)Math.Clamp(Tiles, 1, 64)));
        int across = (width + side - 1) / side, down = (height + side - 1) / side;

        byte* start = (byte*)pixels;
        var luma = new byte[width * height];

        Parallel.For(0, height, y =>
        {
            byte* row = start + (long)y * stride;

            for (int x = 0; x < width; x++)
            {
                byte* p = row + x * 4;
                luma[y * width + x] = (byte)((54 * p[2] + 183 * p[1] + 19 * p[0] + 128) >> 8);
            }
        });

        // Je Kachel eine Abbildung der Helligkeit, 0 bis 255.
        var maps = new float[across * down * 256];

        Parallel.For(0, across * down, tile =>
        {
            int x0 = tile % across * side, y0 = tile / across * side;
            int x1 = Math.Min(width, x0 + side), y1 = Math.Min(height, y0 + side);
            Span<int> counts = stackalloc int[256];

            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    counts[luma[y * width + x]]++;

            TileMap(counts, (x1 - x0) * (y1 - y0), limit, maps.AsSpan(tile * 256, 256));
        });

        Parallel.For(0, height, y =>
        {
            byte* row = start + (long)y * stride;

            // Zwischen welchen Kachelmitten die Zeile liegt, und wie weit.
            float fy = (y + 0.5f) / side - 0.5f;
            int ty0 = Math.Clamp((int)MathF.Floor(fy), 0, down - 1), ty1 = Math.Min(ty0 + 1, down - 1);
            float wy = Math.Clamp(fy - ty0, 0f, 1f);

            for (int x = 0; x < width; x++)
            {
                float fx = (x + 0.5f) / side - 0.5f;
                int tx0 = Math.Clamp((int)MathF.Floor(fx), 0, across - 1), tx1 = Math.Min(tx0 + 1, across - 1);
                float wx = Math.Clamp(fx - tx0, 0f, 1f);

                int v = luma[y * width + x];
                float top = maps[(ty0 * across + tx0) * 256 + v] * (1 - wx) + maps[(ty0 * across + tx1) * 256 + v] * wx;
                float bottom = maps[(ty1 * across + tx0) * 256 + v] * (1 - wx) + maps[(ty1 * across + tx1) * 256 + v] * wx;
                float shift = (top * (1 - wy) + bottom * wy - v) * amount;

                byte* p = row + x * 4;
                p[0] = Clamp(p[0] + shift);
                p[1] = Clamp(p[1] + shift);
                p[2] = Clamp(p[2] + shift);
            }
        });
    }

    /// <summary>
    /// Die Abbildung einer Kachel: ihre Verteilung, gekappt bei der Grenze, der Ueberschuss gleich
    /// verteilt, bis zur Mitte jeder Stufe gezaehlt. Eine gleichmaessige Flaeche bleibt so fast, wie
    /// sie ist - sie hat nichts, was sich auszugleichen lohnte.
    /// </summary>
    public static void TileMap(ReadOnlySpan<int> counts, int total, float limit, Span<float> map)
    {
        if (total <= 0)
        {
            for (int v = 0; v < 256; v++) map[v] = v;
            return;
        }

        float cap = limit * total / 256f, excess = 0f;

        for (int v = 0; v < 256; v++) excess += MathF.Max(0f, counts[v] - cap);

        float share = excess / 256f, below = 0f;

        for (int v = 0; v < 256; v++)
        {
            float kept = MathF.Min(counts[v], cap) + share;

            map[v] = (below + kept / 2) / total * 255f;
            below += kept;
        }
    }

    private static byte Clamp(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);

    public ClaheTool Clone() => new() { Amount = Amount, Tiles = Tiles, Limit = Limit };
}
