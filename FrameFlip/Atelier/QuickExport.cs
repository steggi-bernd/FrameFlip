using System.IO;

namespace FrameFlip.Atelier;

/// <summary>
/// Wohin ein Schnell-Export schreibt und unter welchem Namen (docs/Projekte-und-Masken.md,
/// Punkt 6): in den gewaehlten Zielordner, sonst in den Ordner FrameFlip neben den Bildern -
/// derselbe, in dem das Projekt liegt. Ein Einzelbild wird "render_FrameFlip_001.png", eine
/// Sequenz ein Ordner "render_FrameFlip_001\", ein Video "render_FrameFlip_001.mp4".
///
/// Nie ueberschreiben: Die Nummer ist die naechste, unter der weder eine Datei (mit
/// welcher Endung auch immer) noch ein Ordner liegt, und der Name wird belegt, bevor
/// geschrieben wird - eine leere Datei oder der Ordner selbst.
/// </summary>
internal static class QuickExport
{
    public const string Marker = "_FrameFlip_";

    /// <summary>Der Zielordner: gewaehlt, sonst FrameFlip neben den Bildern.</summary>
    public static string FolderFor(SequenceKey key, string? chosen)
        => chosen is { Length: > 0 } ? chosen : AtelierProjectStore.FolderOf(key);

    /// <summary>
    /// Belegt den naechsten freien Namen im Ordner und liefert ihn ohne Endung -
    /// "render_FrameFlip_004". <paramref name="asFolder"/>: als Ordner (eine Sequenz),
    /// sonst als leere Datei mit <paramref name="extension"/>.
    /// </summary>
    public static string Claim(string folder, string name, string extension, bool asFolder)
    {
        Directory.CreateDirectory(folder);

        for (int n = 1; n < 100000; n++)
        {
            string stem = name + Marker + n.ToString(n < 1000 ? "000" : "0");

            if (Taken(folder, stem)) continue;

            try
            {
                if (asFolder)
                {
                    // CreateDirectory meldet keinen Fehler, wenn es ihn schon gibt - geprueft
                    // ist oben; zwischen Pruefen und Anlegen kommt hier niemand dazwischen,
                    // ausser einem zweiten Programm, und das faende dann einen vollen Ordner.
                    Directory.CreateDirectory(Path.Combine(folder, stem));
                }
                else
                {
                    using var _ = new FileStream(Path.Combine(folder, stem + extension), FileMode.CreateNew, FileAccess.Write);
                }

                return stem;
            }
            catch (IOException)
            {
                // Genau jetzt belegt - die naechste Nummer.
            }
        }

        throw new IOException("Kein freier Name fuer den Schnell-Export in " + folder);
    }

    /// <summary>Ob unter diesem Namen schon etwas liegt - Ordner oder Datei mit beliebiger Endung.</summary>
    private static bool Taken(string folder, string stem)
        => Directory.Exists(Path.Combine(folder, stem)) ||
           Directory.EnumerateFiles(folder, stem + ".*").Any();
}
