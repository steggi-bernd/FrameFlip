using System.IO;

namespace FrameFlip.Rendering;

/// <summary>Wohin ein Render schreibt.</summary>
/// <param name="Directory">Der Ordner, der dafuer angelegt wird.</param>
/// <param name="Pattern">Blenders Ausgabemuster - Pfad plus Namensanfang, ohne Endung.</param>
/// <param name="Label">Was am Handy als Ziel dasteht, kurz und ohne Laufwerksbuchstaben.</param>
public sealed record OutputPlan(string Directory, string Pattern, string Label);

/// <summary>
/// Den Ablageort fuer einen Render bestimmen - so, dass nie etwas verlorengeht.
///
/// Das ist die eine Regel, an der alles haengt: EIN RENDER UEBERSCHREIBT NICHTS.
/// Wer aus der Ferne einen Auftrag lostritt, sieht nicht, was auf der Platte schon
/// liegt, und kann deshalb auch nicht abwaegen. Also darf die Frage gar nicht
/// entstehen.
///
/// Daraus folgen zwei Formen:
///
/// Eine ANIMATION bekommt jedes Mal einen eigenen, neuen Ordner. Alle Bilder eines
/// Laufs liegen damit beieinander, und ein zweiter Lauf mit anderen Einstellungen
/// legt sich nicht ueber den ersten. Das ist auch der Grund, warum durchnummeriert
/// wird und nicht nach Datum: Ein Ordner "kitchen_003" sagt, dass es 001 und 002
/// gab; ein Zeitstempel sagt nur, wann jemand Zeit hatte.
///
/// Ein EINZELBILD kommt in einen gemeinsamen Ordner "still" - dort sammeln sich
/// Probebilder, und einzeln je Bild einen Ordner anzulegen waere Unfug. Damit auch
/// dort nichts verschwindet, bekommt der Dateiname einen freien Anfang gesucht.
/// </summary>
public static class OutputPlanner
{
    /// <summary>Ordner fuer Einzelbilder.</summary>
    public const string StillFolder = "still";

    /// <summary>Ordner, unter dem die Laeufe einer Animation liegen.</summary>
    public const string RenderFolder = "render";

    /// <summary>Soweit wird gezaehlt, bevor aufgegeben wird. Wer 999 Laeufe hat, hat andere Sorgen.</summary>
    public const int MaxRuns = 999;

    /// <summary>
    /// Den Ort bestimmen. Die Platte wird ueber die zwei Pruefungen befragt, damit
    /// sich das Ganze ohne Dateisystem pruefen laesst.
    /// </summary>
    public static OutputPlan Plan(string blendPath, string? outputRoot, bool animation,
                                  Func<string, bool> folderExists, Func<string, bool> fileExists)
    {
        string stem = Clean(Path.GetFileNameWithoutExtension(blendPath));

        // Voreinstellung ist der Ordner des Projekts. Wer nichts sagt, bekommt seine
        // Bilder dort, wo die Datei liegt - und nicht irgendwo im Benutzerprofil.
        string root = string.IsNullOrWhiteSpace(outputRoot)
            ? Path.GetDirectoryName(Path.GetFullPath(blendPath)) ?? "."
            : Path.GetFullPath(outputRoot);

        return animation ? ForAnimation(root, stem, folderExists) : ForStill(root, stem, fileExists);
    }

    private static OutputPlan ForAnimation(string root, string stem, Func<string, bool> folderExists)
    {
        string parent = Path.Combine(root, RenderFolder);

        for (int run = 1; run <= MaxRuns; run++)
        {
            string folder = Path.Combine(parent, $"{stem}_{run:000}");

            if (folderExists(folder)) continue;

            // Der Name der Bilder wiederholt den Ordnernamen nicht: Im Ordner
            // "kitchen_003" hiessen sie sonst "kitchen_003_kitchen_0001.png".
            return new OutputPlan(folder, Path.Combine(folder, "frame_"),
                                  $"{RenderFolder}/{stem}_{run:000}");
        }

        // Alle Nummern belegt: Dann lieber ein Ordner mit Zeitstempel als gar keiner
        // - und ganz sicher nicht einer, der auf einen bestehenden zeigt.
        string fallback = Path.Combine(parent, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}");

        return new OutputPlan(fallback, Path.Combine(fallback, "frame_"),
                              $"{RenderFolder}/{Path.GetFileName(fallback)}");
    }

    private static OutputPlan ForStill(string root, string stem, Func<string, bool> fileExists)
    {
        string folder = Path.Combine(root, StillFolder);

        for (int run = 1; run <= MaxRuns; run++)
        {
            // Blender haengt die Framenummer und die Endung selbst an. Frei sein muss
            // deshalb der ANFANG, und geprueft wird gegen die ueblichen Endungen -
            // welche es wird, entscheidet das Format.
            string prefix = run == 1 ? stem : $"{stem}_{run}";

            if (Taken(folder, prefix, fileExists)) continue;

            return new OutputPlan(folder, Path.Combine(folder, prefix + "_"),
                                  $"{StillFolder}/{prefix}");
        }

        string fallback = $"{stem}_{DateTime.Now:HHmmss}";

        return new OutputPlan(folder, Path.Combine(folder, fallback + "_"), $"{StillFolder}/{fallback}");
    }

    /// <summary>Ob unter diesem Anfang schon ein Bild liegt - gleich welcher Endung.</summary>
    private static bool Taken(string folder, string prefix, Func<string, bool> fileExists)
    {
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".exr", ".tif", ".tiff", ".webp", ".bmp" })
        {
            // Die Framenummer kennt diese Seite nicht - geprueft wird deshalb der
            // haeufigste Fall, vier Stellen, und dazu der Name ohne Nummer.
            if (fileExists(Path.Combine(folder, prefix + "_0001" + extension))) return true;
            if (fileExists(Path.Combine(folder, prefix + extension))) return true;
        }

        return false;
    }

    /// <summary>Aus dem Dateinamen einen brauchbaren Ordnernamen machen.</summary>
    private static string Clean(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        cleaned = cleaned.Trim().TrimEnd('.', ' ');

        return cleaned.Length == 0 ? "render" : cleaned;
    }
}
