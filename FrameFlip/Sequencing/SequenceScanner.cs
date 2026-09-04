using System.IO;
using System.Text.RegularExpressions;
using FrameFlip.Decoding;

namespace FrameFlip.Sequencing;

/// <summary>
/// Findet zu einer Beispieldatei alle Geschwister derselben Sequenz.
/// Luecken sind ausdruecklich erlaubt (abgebrochener Render).
/// </summary>
public static class SequenceScanner
{
    // Die letzte Zifferngruppe im Stamm ist die Framenummer: der Praefix ist faul,
    // der Suffix darf keine Ziffern mehr enthalten.
    private static readonly Regex StemPattern = new(@"^(?<prefix>.*?)(?<number>\d+)(?<suffix>\D*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SequencePattern? DerivePattern(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return null;

        var extension = Path.GetExtension(filePath);
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(stem)) return null;

        var match = StemPattern.Match(stem);
        if (!match.Success)
        {
            // Kein Zaehler im Namen - Einzelbild.
            return new SequencePattern(directory, stem, 0, string.Empty, extension);
        }

        return new SequencePattern(
            directory,
            match.Groups["prefix"].Value,
            match.Groups["number"].Value.Length,
            match.Groups["suffix"].Value,
            extension);
    }

    public static ImageSequence? Scan(string seedFile, FrameDecoderRegistry decoders)
    {
        var pattern = DerivePattern(seedFile);
        if (pattern is null) return null;

        if (pattern.Padding == 0)
        {
            // Einzelbild ohne Nummer.
            var name = Path.GetFileName(seedFile);
            return new ImageSequence(pattern, new[] { new SequenceFrame(0, seedFile, name) });
        }

        var matcher = new Regex(
            "^" + Regex.Escape(pattern.Prefix) + @"(?<number>\d+)" + Regex.Escape(pattern.Suffix) + "$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        IEnumerable<string> files;
        try
        {
            // Ohne Suchmuster enumerieren: Praefixe duerfen * und ? enthalten.
            files = Directory.EnumerateFiles(pattern.Directory);
        }
        catch (Exception)
        {
            return null;
        }

        // Erst alle Namenskandidaten sammeln, dann das Padding bestimmen. Andersherum
        // ginge es nicht: bei einem Ueberlauf hat die Beispieldatei mehr Stellen als
        // das Padding, und die kuerzeren Geschwister fielen aus dem Raster.
        var candidates = new List<(string Path, string Digits)>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (!string.Equals(extension, pattern.Extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (!decoders.IsSupported(extension)) continue;

            var stem = Path.GetFileNameWithoutExtension(file);
            var m = matcher.Match(stem);
            if (!m.Success) continue;

            candidates.Add((file, m.Groups["number"].Value));
        }

        int padding = DeterminePadding(candidates, pattern.Padding);

        var frames = new List<SequenceFrame>();
        var seen = new HashSet<int>();

        foreach (var (file, digits) in candidates)
        {
            if (!BelongsTo(digits, padding)) continue;
            if (!int.TryParse(digits, out int number)) continue;
            if (!seen.Add(number)) continue;

            frames.Add(new SequenceFrame(number, file, Path.GetFileName(file)));
        }

        if (frames.Count == 0)
        {
            var name = Path.GetFileName(seedFile);
            return new ImageSequence(pattern, new[] { new SequenceFrame(0, seedFile, name) });
        }

        frames.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return new ImageSequence(pattern with { Padding = padding }, frames);
    }

    /// <summary>
    /// Eine Datei gehoert zur Sequenz, wenn sie genau die Padding-Stellen hat oder
    /// mehr, dann aber ohne fuehrende Null. Blender fuellt auf genau N Stellen auf
    /// und laesst die Zahl darueber hinauswachsen: nach f_99 kommt f_100, nicht f_00100.
    /// </summary>
    private static bool BelongsTo(string digits, int padding)
        => digits.Length == padding || (digits.Length > padding && digits[0] != '0');

    /// <summary>
    /// Leitet die Stellenzahl aus dem Bestand ab, nicht allein aus der Beispieldatei.
    ///
    /// Eine fuehrende Null beweist das Padding: "0042" kann nur aus einem
    /// vierstelligen Muster stammen. Ohne fuehrende Nullen im ganzen Ordner ist die
    /// kuerzeste vorkommende Zahl massgeblich - bei f_99 und f_100 also zwei.
    ///
    /// Ohne diesen Schritt haengt das Ergebnis davon ab, welche Datei im Explorer
    /// selektiert war: von f_100 aus waere f_99 nicht gefunden worden.
    /// </summary>
    private static int DeterminePadding(List<(string Path, string Digits)> candidates, int seedPadding)
    {
        if (candidates.Count == 0) return seedPadding;

        // Die Beispieldatei hat Vorrang, wenn sie selbst eine fuehrende Null traegt.
        // Liegen mehrere Muster im selben Ordner, entscheidet sie damit eindeutig.
        int padded = -1;
        int shortest = int.MaxValue;

        foreach (var (_, digits) in candidates)
        {
            shortest = Math.Min(shortest, digits.Length);

            // Nur Kandidaten betrachten, die nicht laenger als die Beispieldatei sind:
            // ein laengeres Muster im selben Ordner ist eine andere Sequenz.
            if (digits.Length > 1 && digits[0] == '0' && digits.Length <= seedPadding)
                padded = Math.Max(padded, digits.Length);
        }

        return padded > 0 ? padded : Math.Min(shortest, seedPadding);
    }

    /// <summary>Fallback, wenn im Explorer nichts selektiert war: erstes darstellbares Bild im Ordner.</summary>
    public static string? FindFirstImage(string directory, FrameDecoderRegistry decoders)
    {
        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(f => decoders.IsSupported(Path.GetExtension(f)))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
