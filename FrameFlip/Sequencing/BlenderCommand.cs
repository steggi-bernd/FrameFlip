using System.IO;

namespace FrameFlip.Sequencing;

/// <summary>
/// Baut den Aufruf, mit dem sich fehlende Frames nachrendern lassen.
///
/// Die Argumentreihenfolge ist bindend: -f startet den Render sofort, alles danach
/// wirkt nicht mehr. Ein -o hinter -f wird stillschweigend ignoriert, und der Render
/// landet im temporaeren Ordner - der haeufigste Fehler beim Bau solcher Aufrufe.
/// </summary>
public static class BlenderCommand
{
    /// <summary>Blenders Formatname zur Dateiendung. Unbekanntes bleibt PNG.</summary>
    public static string FormatFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jpe" => "JPEG",
        ".tif" or ".tiff" => "TIFF",
        ".bmp" => "BMP",
        ".webp" => "WEBP",
        ".exr" => "OPEN_EXR",
        ".dpx" => "DPX",
        ".tga" => "TARGA",
        _ => "PNG",
    };

    /// <summary>
    /// Ausgabemuster in Blender-Schreibweise: der Zaehler wird durch so viele
    /// Rautezeichen ersetzt, wie das Padding Stellen hat.
    /// </summary>
    public static string OutputPattern(ImageSequence sequence)
    {
        var pattern = sequence.Pattern;
        var stem = pattern.Prefix + new string('#', Math.Max(1, pattern.Padding)) + pattern.Suffix;

        // Vorwaertsschraegstriche: Blender nimmt beide, aber in Anfuehrungszeichen
        // sind Rueckwaertsschraegstriche je nach Shell ein Fluchtzeichen.
        return Path.Combine(pattern.Directory, stem).Replace('\\', '/');
    }

    /// <summary>
    /// Vollstaendiger Befehl fuer die Zwischenablage. Die .blend-Datei kennt
    /// FrameFlip nicht - von aussen ist nicht feststellbar, aus welchem Projekt ein
    /// Ordner gerendert wurde. Sie bleibt deshalb ein sichtbarer Platzhalter, statt
    /// geraten zu werden.
    /// </summary>
    public static string BuildRepairCommand(ImageSequence sequence, IReadOnlyList<int> missing,
                                            string? blendFile = null)
    {
        if (missing.Count == 0) return string.Empty;

        var blend = string.IsNullOrWhiteSpace(blendFile) ? "PFAD/ZUM/PROJEKT.blend" : blendFile!.Replace('\\', '/');

        return $"blender -b \"{blend}\"" +
               $" -o \"{OutputPattern(sequence)}\"" +
               $" -F {FormatFor(sequence.Pattern.Extension)}" +
               " -x 1" +
               $" -f {SequenceMath.FormatForBlender(missing)}";
    }
}
