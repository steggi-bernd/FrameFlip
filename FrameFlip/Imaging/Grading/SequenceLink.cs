using System.IO;
using FrameFlip.Sequencing;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Verbindet zwei Sequenzen ueber ihre Bildnummer.
///
/// Gebraucht fuer Bildebenen: Wer eine zweite Fassung desselben Renders daruberlegt,
/// meint bei Bild 47 auch dort Bild 47. Das ist der Fall, der die Bildebene
/// rechtfertigt - zwei Fassungen auf Differenz gestellt zeigen in einem Blick, was
/// sich geaendert hat, und dafuer muessen die Nummern zusammenpassen.
///
/// Gelesen wird die Nummer mit demselben Ausdruck, mit dem auch die Sequenzsuche
/// arbeitet. Das ist kein Zufall und keine Bequemlichkeit: Waere es eine zweite
/// Regel, fiele eines Tages eine Datei unter die eine und nicht unter die andere,
/// und die Ebene laege um ein Bild versetzt - sichtbar erst in Bewegung.
/// </summary>
public static class SequenceLink
{
    /// <summary>Die Bildnummer im Dateinamen, oder null bei einem Einzelbild.</summary>
    public static int? NumberOf(string path)
    {
        var pattern = SequenceScanner.DerivePattern(path);
        if (pattern is null || pattern.Padding == 0) return null;

        string stem = Path.GetFileNameWithoutExtension(path);

        int start = pattern.Prefix.Length;
        int length = stem.Length - start - pattern.Suffix.Length;
        if (length <= 0 || start + length > stem.Length) return null;

        return int.TryParse(stem.AsSpan(start, length), out int number) ? number : null;
    }

    /// <summary>
    /// Der Pfad der Bildebene fuer das gerade bearbeitete Bild.
    ///
    /// Ein Einzelbild bleibt, was es ist - ein Logo hat keine Nummer und soll auch
    /// keine bekommen. Nur wenn BEIDE Seiten eine Nummer fuehren, wird die des
    /// laufenden Bildes eingesetzt; laesst sich daraus kein vorhandener Pfad bilden,
    /// bleibt es beim urspruenglichen. Lieber dasselbe Bild zweimal als gar keines.
    /// </summary>
    public static string Pair(string imagePath, string framePath, bool follow)
    {
        if (!follow) return imagePath;

        var image = SequenceScanner.DerivePattern(imagePath);
        if (image is null || image.Padding == 0) return imagePath;

        int? number = NumberOf(framePath);
        if (number is null) return imagePath;

        string digits = number.Value.ToString(new string('0', Math.Max(1, image.Padding)));
        string paired = Path.Combine(image.Directory,
                                     image.Prefix + digits + image.Suffix + image.Extension);

        // Fehlt das Bild in der zweiten Sequenz, bleibt das gewaehlte stehen. Ein
        // schwarzes Loch an den Stellen, wo eine Fassung kuerzer ist, waere die
        // formal richtige Antwort und die unbrauchbare.
        return File.Exists(paired) ? paired : imagePath;
    }
}
