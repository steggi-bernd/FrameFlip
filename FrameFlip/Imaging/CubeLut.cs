using System.Globalization;
using System.IO;

namespace FrameFlip.Imaging;

/// <summary>
/// Eine dreidimensionale Nachschlagetabelle im .cube-Format.
///
/// Das Format ist Text: ein kurzer Kopf, danach je Zeile ein RGB-Tripel, und zwar
/// so geordnet, dass Rot am schnellsten laeuft. Blenders AgX-Tabelle hat 57 Stufen
/// je Achse - das sind 185 193 Zeilen, die einmal gelesen und danach behalten werden.
/// </summary>
public sealed class CubeLut
{
    private readonly float[] _values;   // Size^3 * 3, Reihenfolge R am schnellsten

    public int Size { get; }

    public float DomainMin { get; }

    public float DomainMax { get; }

    private CubeLut(int size, float[] values, float domainMin, float domainMax)
    {
        Size = size;
        _values = values;
        DomainMin = domainMin;
        DomainMax = domainMax;
    }

    /// <summary>
    /// Groesste Tabelle, die eingelesen wird. 129 Stufen sind 2,1 Millionen Eintraege
    /// und rund 25 MB - darueber ist etwas faul an der Datei, nicht an der Absicht.
    /// </summary>
    private const int MaxSize = 129;

    public static CubeLut Load(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    public static CubeLut Parse(TextReader reader)
    {
        int size = 0;
        float domainMin = 0f;
        float domainMax = 1f;
        float[]? values = null;
        int filled = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var span = line.AsSpan().Trim();
            if (span.Length == 0 || span[0] == '#') continue;

            // Die Schluesselwoerter stehen alle am Zeilenanfang; alles andere ist
            // ein Zahlentripel.
            if (span.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase)) continue;

            if (span.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                size = ParseInt(span[11..]);
                if (size is < 2 or > MaxSize)
                    throw new InvalidDataException($"LUT_3D_SIZE {size} liegt ausserhalb des Erwartbaren.");

                values = new float[size * size * size * 3];
                continue;
            }

            if (span.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Eindimensionale .cube-Tabellen werden hier nicht gelesen.");

            if (span.StartsWith("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            {
                domainMin = ParseFirstFloat(span[10..]);
                continue;
            }

            if (span.StartsWith("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                domainMax = ParseFirstFloat(span[10..]);
                continue;
            }

            if (values is null)
                throw new InvalidDataException("Werte stehen vor der Groessenangabe.");

            if (filled + 3 > values.Length)
                throw new InvalidDataException("Die Datei enthaelt mehr Werte als angekuendigt.");

            ParseTriple(span, values, filled);
            filled += 3;
        }

        if (values is null) throw new InvalidDataException("Keine Groessenangabe in der Datei.");
        if (filled != values.Length)
            throw new InvalidDataException($"{filled / 3} Eintraege statt {values.Length / 3}.");

        if (domainMax <= domainMin) throw new InvalidDataException("DOMAIN_MAX liegt nicht ueber DOMAIN_MIN.");

        return new CubeLut(size, values, domainMin, domainMax);
    }

    /// <summary>
    /// Tetraedrische Interpolation, wie die Konfiguration sie verlangt.
    ///
    /// Der Unterschied zur einfachen trilinearen Variante ist nicht kosmetisch: die
    /// trilineare mittelt ueber acht Ecken und zieht dabei Farben zur Wuerfeldiagonale
    /// hin, was sich bei AgX als Verfaerbung in den Lichtern zeigt. Die tetraedrische
    /// zerlegt den Wuerfel in sechs Tetraeder und nimmt nur die vier Ecken desjenigen,
    /// in dem der Punkt wirklich liegt - und trifft damit die Graugerade exakt.
    /// </summary>
    public void Apply(ref float r, ref float g, ref float b)
    {
        float scale = (Size - 1) / (DomainMax - DomainMin);

        float x = Math.Clamp((r - DomainMin) * scale, 0, Size - 1);
        float y = Math.Clamp((g - DomainMin) * scale, 0, Size - 1);
        float z = Math.Clamp((b - DomainMin) * scale, 0, Size - 1);

        int x0 = (int)x, y0 = (int)y, z0 = (int)z;
        if (x0 >= Size - 1) x0 = Size - 2;
        if (y0 >= Size - 1) y0 = Size - 2;
        if (z0 >= Size - 1) z0 = Size - 2;

        // Bei Size == 1 gaebe es keinen Wuerfel; das faengt die Groessenpruefung ab.
        float fx = x - x0, fy = y - y0, fz = z - z0;

        // Die Ecken, die in jedem Fall gebraucht werden.
        Fetch(x0, y0, z0, out float c000r, out float c000g, out float c000b);
        Fetch(x0 + 1, y0 + 1, z0 + 1, out float c111r, out float c111g, out float c111b);

        // Welcher der sechs Tetraeder es ist, entscheidet die Rangfolge von fx, fy, fz.
        float w0, w1, w2, w3;
        int ax, ay, az, bx, by, bz;

        if (fx > fy)
        {
            if (fy > fz)       { ax = 1; ay = 0; az = 0; bx = 1; by = 1; bz = 0; w1 = fx - fy; w2 = fy - fz; w3 = fz; }
            else if (fx > fz)  { ax = 1; ay = 0; az = 0; bx = 1; by = 0; bz = 1; w1 = fx - fz; w2 = fz - fy; w3 = fy; }
            else               { ax = 0; ay = 0; az = 1; bx = 1; by = 0; bz = 1; w1 = fz - fx; w2 = fx - fy; w3 = fy; }
        }
        else
        {
            if (fz > fy)       { ax = 0; ay = 0; az = 1; bx = 0; by = 1; bz = 1; w1 = fz - fy; w2 = fy - fx; w3 = fx; }
            else if (fz > fx)  { ax = 0; ay = 1; az = 0; bx = 0; by = 1; bz = 1; w1 = fy - fz; w2 = fz - fx; w3 = fx; }
            else               { ax = 0; ay = 1; az = 0; bx = 1; by = 1; bz = 0; w1 = fy - fx; w2 = fx - fz; w3 = fz; }
        }

        w0 = 1f - w1 - w2 - w3;

        Fetch(x0 + ax, y0 + ay, z0 + az, out float car, out float cag, out float cab);
        Fetch(x0 + bx, y0 + by, z0 + bz, out float cbr, out float cbg, out float cbb);

        r = w0 * c000r + w1 * car + w2 * cbr + w3 * c111r;
        g = w0 * c000g + w1 * cag + w2 * cbg + w3 * c111g;
        b = w0 * c000b + w1 * cab + w2 * cbb + w3 * c111b;
    }

    /// <summary>Rot laeuft am schnellsten - das ist die Ordnung, die das Format vorschreibt.</summary>
    private void Fetch(int x, int y, int z, out float r, out float g, out float b)
    {
        int index = ((z * Size + y) * Size + x) * 3;
        r = _values[index];
        g = _values[index + 1];
        b = _values[index + 2];
    }

    private static void ParseTriple(ReadOnlySpan<char> span, float[] target, int at)
    {
        for (int i = 0; i < 3; i++)
        {
            span = span.TrimStart();
            int end = span.IndexOf(' ');
            var token = end < 0 ? span : span[..end];

            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new InvalidDataException($"'{token}' ist keine Zahl.");

            target[at + i] = value;
            span = end < 0 ? ReadOnlySpan<char>.Empty : span[end..];
        }
    }

    private static int ParseInt(ReadOnlySpan<char> span)
        => int.TryParse(span.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static float ParseFirstFloat(ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        int end = span.IndexOf(' ');
        var token = end < 0 ? span : span[..end];

        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }
}
