using System.IO;

namespace FrameFlip.Projects;

/// <summary>Ein Ordner, wie er als Kachel im Projekt steht - cam1, cam2, preview.</summary>
public sealed record FolderTile(
    string Path,
    string Name,

    /// <summary>Bilder, die direkt darin liegen.</summary>
    int Images,

    /// <summary>Unterordner - daran haengt, ob man tiefer kommt.</summary>
    int Folders,

    /// <summary>Ein Bild aus dem Ordner, oder null. Das Vorschaubild der Kachel.</summary>
    string? Thumbnail);

/// <summary>
/// Blender-Dateien und ihre Ausgabeordner auf der Platte finden.
///
/// Der Scanner sucht nicht den ganzen Rechner ab. Er kennt zwei Quellen: Ordner, die
/// jemand ausdruecklich benannt hat, und einzelne Dateien, die FrameFlip beim Rendern
/// gesehen hat. Alles andere waere Raten auf fremden Platten - und ein Programm, das
/// beim Start eine halbe Stunde lang jede Festplatte durchgeht, will niemand.
///
/// Fehler enden hier still und ordnerweise. Ein Verzeichnis ohne Leserecht darf
/// nicht die ganze Suche abbrechen; man bekommt dann eben eines weniger zu sehen.
/// </summary>
public static class ProjectScanner
{
    /// <summary>Bildendungen, die als Frame zaehlen - dieselben, die die Vorschau oeffnet.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".tif", ".tiff", ".bmp", ".webp", ".exr", ".hdr", ".tga",
    };

    /// <summary>
    /// So tief geht die Suche nach .blend-Dateien.
    ///
    /// Sechs Ebenen reichen fuer jede Ordnung, die ein Mensch von Hand anlegt, und
    /// begrenzen den Schaden, wenn jemand versehentlich C:\ eintraegt.
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>Obergrenze je Suchlauf. Danach ist Schluss, mit dem, was da ist.</summary>
    public const int MaxFiles = 4000;

    public static bool IsImage(string path) => ImageExtensions.Contains(System.IO.Path.GetExtension(path));

    /// <summary>
    /// Alle Blender-Dateien aus den genannten Ordnern, dazu die einzeln bekannten.
    ///
    /// Doppelte fallen heraus: Eine Datei, die FrameFlip beim Rendern gesehen hat und
    /// die ausserdem in einem durchsuchten Ordner liegt, ist eine Datei.
    /// </summary>
    public static List<BlendVersion> Collect(IEnumerable<string>? folders, IEnumerable<string>? singles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<BlendVersion>();

        foreach (string folder in folders ?? Enumerable.Empty<string>())
        {
            string root = folder.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                         System.IO.Path.AltDirectorySeparatorChar);

            foreach (string path in Walk(folder))
            {
                if (found.Count >= MaxFiles) return found;
                if (!seen.Add(path)) continue;

                if (Describe(path, ProjectFolderFor(root, path)) is BlendVersion version) found.Add(version);
            }
        }

        foreach (string path in singles ?? Enumerable.Empty<string>())
        {
            if (found.Count >= MaxFiles) return found;
            if (!BlendProjects.IsBlendFile(path) || !seen.Add(path)) continue;

            if (Describe(path) is BlendVersion version) found.Add(version);
        }

        return found;
    }

    /// <summary>Die .blend-Dateien unterhalb eines Ordners. Leer, wenn er nicht da ist.</summary>
    public static IEnumerable<string> Walk(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<string>();

        try
        {
            // IgnoreInaccessible sorgt dafuer, dass ein gesperrter Unterordner
            // uebersprungen wird, statt die Aufzaehlung mit einer Ausnahme zu beenden.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxDepth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            };

            // "*.blend*" faengt auch .blend1 und .blend2 ein; die Endung wird danach
            // geprueft, damit nicht auch .blender-Notizen mitkommen.
            return Directory.EnumerateFiles(folder, "*.blend*", options)
                            .Where(BlendProjects.IsBlendFile)
                            .Where(path => !IsAsset(folder, path))
                            .Take(MaxFiles);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Ob eine Datei in einer Materialbibliothek liegt statt in einem Projekt.
    ///
    /// Heruntergeladene Materialien und Modelle sind .blend-Dateien wie jede andere -
    /// black-fabric_2K_57b8c197.blend, dreissig Stueck in einem Ordner. Als Projekt
    /// gezaehlt schwemmen sie die Liste zu, und niemand hat je eines davon "geoeffnet".
    /// Verloren geht dabei nichts: Sie liegen IM Projekt, und ueber dessen Ordner
    /// kommt man wie im Explorer an sie heran.
    ///
    /// Geprueft wird nur unterhalb des durchsuchten Ordners. Wer selbst einen Ordner
    /// "Assets" eintraegt, meint genau den - und bekommt, was darin liegt.
    /// </summary>
    private static bool IsAsset(string root, string path)
    {
        if (path.Length <= root.Length) return false;

        return path[root.Length..]
               .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
               .SkipLast(1)
               .Any(part => part.Equals("assets", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Welcher Ordner das Projekt ausmacht - der erste unterhalb des durchsuchten.
    ///
    /// Wer "Projekte" eintraegt, meint mit "Projekte\TRACER" ein Projekt, auch wenn
    /// darin noch beatch und tracer_pb liegen: Unterordner gehoeren dazu, so wie im
    /// Explorer. Was direkt im durchsuchten Ordner liegt, hat keinen eigenen Ordner -
    /// dort entscheidet wieder der Name.
    /// </summary>
    public static string ProjectFolderFor(string root, string path)
    {
        if (path.Length <= root.Length + 1) return string.Empty;

        string[] parts = path[(root.Length + 1)..]
            .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        // Ein Teil ist der Dateiname selbst - dann liegt die Datei unmittelbar im
        // durchsuchten Ordner.
        return parts.Length < 2 ? string.Empty : System.IO.Path.Combine(root, parts[0]);
    }

    /// <summary>Eine einzelne Datei beschreiben, oder null, wenn sie nicht mehr da ist.</summary>
    public static BlendVersion? Describe(string path, string projectFolder = "")
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;

            string name = info.Name;
            var (_, number, backup, autosave) = BlendProjects.Split(name);

            return new BlendVersion(info.FullName, name, number, backup, autosave,
                                    info.LastWriteTimeUtc, info.Length, projectFolder);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Was in einem Ordner steht, als Kacheln - die Ebene unter dem Projekt.
    ///
    /// Ein Blender-Projekt legt seine Ausgaben ueblicherweise nebeneinander ab:
    /// cam1, cam2, preview, final. Genau diese Ordner sind hier die Kacheln, mit der
    /// Zahl der Bilder darin und einem davon als Vorschau.
    /// </summary>
    public static List<FolderTile> Children(string directory)
    {
        var tiles = new List<FolderTile>();

        foreach (string child in Directories(directory))
        {
            var (images, thumbnail) = Peek(child);

            tiles.Add(new FolderTile(child, System.IO.Path.GetFileName(child), images,
                                     Directories(child).Count, thumbnail));
        }

        return tiles.OrderByDescending(tile => tile.Images > 0)
                    .ThenBy(tile => tile.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    /// <summary>Die Bilddateien in einem Ordner, nach Namen sortiert - das ist die Frame-Reihenfolge.</summary>
    public static List<string> Images(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();

            return Directory.EnumerateFiles(directory)
                            .Where(IsImage)
                            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Ein Vorschaubild fuer ein Projekt: das letzte Bild, das sich in seinem Ordner
    /// finden laesst.
    ///
    /// Gesucht wird im Ordner selbst, dann in seinen Unterordnern. Genommen wird
    /// das im Namen letzte - bei durchnummerierten Frames ist das der zuletzt
    /// gerenderte, und dafuer muss keine einzige Datei angefasst werden. Ueber die
    /// Aenderungszeit zu gehen hiesse, in einem Ordner mit zehntausend Frames
    /// zehntausendmal die Platte zu fragen.
    /// </summary>
    public static string? Thumbnail(string directory, int depth = 3)
    {
        var (_, direct) = Peek(directory);
        if (direct is not null) return direct;

        if (depth <= 1) return null;

        // Drei Ebenen, weil ein Projektordner die Bilder selten selbst haelt:
        // Bei TRACER liegen sie in beatch/render, also zwei Stockwerke tiefer.
        foreach (string child in Directories(directory))
            if (Thumbnail(child, depth - 1) is string deeper) return deeper;

        return null;
    }

    /// <summary>Zahl der Bilder und das letzte davon - in einem Durchgang.</summary>
    private static (int Count, string? Last) Peek(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return (0, null);

            int count = 0;
            string? last = null;

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                if (!IsImage(path)) continue;

                count++;

                if (last is null || string.Compare(path, last, StringComparison.OrdinalIgnoreCase) > 0)
                    last = path;
            }

            return (count, last);
        }
        catch (Exception)
        {
            return (0, null);
        }
    }

    private static List<string> Directories(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };

            return Directory.EnumerateDirectories(directory, "*", options).ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
}
