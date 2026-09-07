using System.IO;

namespace FrameFlip.Remote;

/// <summary>Wozu eine Datei taugt, wenn sie am Handy ankommt.</summary>
public enum FileUse
{
    /// <summary>Nichts davon - kommt nicht durch.</summary>
    None,

    /// <summary>Ein Bild. Laesst sich verkleinert ansehen, ohne es zu holen.</summary>
    Image,

    /// <summary>Ein Video. Laesst sich holen; zum Ansehen muss es fliessen.</summary>
    Video,

    /// <summary>Ein Blender-Dokument. Nur zum Holen.</summary>
    Blend,
}

/// <summary>
/// Das zweite Tor: die Projektordner mitlesen.
///
/// Der Austauschordner (<see cref="FileVault"/>) ist ein Briefkasten - ein Ordner,
/// nur .blend, auch zum Ablegen. Hier geht es um etwas anderes: die Bibliothek, die
/// im Projektbrowser steht, mit allen Unterordnern und allen gerenderten Bildern.
/// Das ist mehr zu sehen und deshalb eine eigene Erlaubnis, und es ist ausdruecklich
/// NUR Lesen. Wer von unterwegs einen Frame ansehen will, muss dafuer nichts
/// ablegen duerfen.
///
/// Die Grenze ist der Satz eingetragener Ordner - dieselben, die der Projektbrowser
/// durchsucht. Was darin liegt, ist erreichbar; alles andere nicht, und zwar auch
/// dann nicht, wenn ein Pfad sich mit Punkten oder ueber eine Verknuepfung
/// hinauszuschreiben versucht.
///
/// Auf einen Fallstrick sei hingewiesen, weil er so unscheinbar ist: Ein Pfad, der
/// mit dem Wurzelordner ANFAENGT, liegt nicht darin. "C:\Projekte-geheim" beginnt
/// mit "C:\Projekte". Verglichen wird deshalb bis zum Trennzeichen.
/// </summary>
public sealed class LibraryVault
{
    private static readonly HashSet<string> ImageKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".tif", ".tiff", ".bmp", ".webp", ".exr", ".hdr", ".tga",
    };

    private static readonly HashSet<string> VideoKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".mpg", ".mpeg",
    };

    /// <summary>Soviele Eintraege je Ordner. Ein Frameordner hat leicht zehntausend.</summary>
    public const int PageSize = 300;

    private readonly List<string> _roots;
    private readonly List<string> _outputs;

    /// <param name="enabled">Ob die Bibliothek ueberhaupt freigegeben ist.</param>
    /// <param name="roots">Die durchsuchten Projektordner.</param>
    /// <param name="outputs">
    /// Ordner, die FrameFlip fuer einen Render selbst angelegt hat.
    ///
    /// Die sind IMMER erreichbar, auch ohne Freigabe der Bibliothek. Wer einen
    /// Render von unterwegs startet, muss sein Ergebnis ansehen koennen - sonst
    /// waere der Auftrag ein Schuss ins Dunkle. Es geht dabei ausdruecklich nur um
    /// das, was dieser Render selbst erzeugt hat: nicht der Ordner der .blend-Datei,
    /// nicht seine Nachbarn, sondern der eine angelegte Ausgabeordner.
    /// </param>
    public LibraryVault(bool enabled, IEnumerable<string>? roots, IEnumerable<string>? outputs = null)
    {
        _roots = Clean(enabled ? roots : null);
        _outputs = Clean(outputs);

        LibraryEnabled = enabled && _roots.Count > 0;
        Enabled = LibraryEnabled || _outputs.Count > 0;
    }

    /// <summary>Ob ueberhaupt etwas erreichbar ist - Bibliothek oder Renderausgabe.</summary>
    public bool Enabled { get; }

    /// <summary>Ob in den Projektordnern geblaettert werden darf. Ohne das gibt es keine Projektliste.</summary>
    public bool LibraryEnabled { get; }

    public IReadOnlyList<string> Roots => _roots;

    /// <summary>Die Ausgabeordner der Renderauftraege - erreichbar auch ohne Bibliothek.</summary>
    public IReadOnlyList<string> Outputs => _outputs;

    private static List<string> Clean(IEnumerable<string>? paths)
        => (paths ?? Enumerable.Empty<string>())
           .Where(path => !string.IsNullOrWhiteSpace(path))
           .Select(Full)
           .Where(path => path.Length > 0)
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .ToList();

    /// <summary>Was diese Datei ist - und ob sie ueberhaupt etwas ist.</summary>
    public static FileUse UseOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return FileUse.None;

        string extension = Path.GetExtension(path);

        if (ImageKinds.Contains(extension)) return FileUse.Image;
        if (VideoKinds.Contains(extension)) return FileUse.Video;

        // Blenders Sicherungen bleiben aussen vor: .blend1 ist nichts, was man sich
        // aufs Handy holt, und jede Endung mehr ist eine Regel mehr.
        return extension.Equals(".blend", StringComparison.OrdinalIgnoreCase) ? FileUse.Blend : FileUse.None;
    }

    /// <summary>
    /// Ob dieser Pfad innerhalb der Bibliothek liegt.
    ///
    /// Gerechnet wird auf dem aufgeloesten Pfad, nicht auf dem uebergebenen: "..",
    /// doppelte Trennzeichen und kurze Namen sind danach weg. Verknuepfungen werden
    /// zusaetzlich abgelehnt - eine Abzweigung mitten in der Bibliothek koennte sonst
    /// auf jeden beliebigen Ordner der Platte zeigen.
    /// </summary>
    public bool Contains(string? path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path)) return false;

        string full = Full(path);

        if (full.Length == 0) return false;

        // Ein Ausgabeordner zaehlt auch als er selbst: Der Render meldet ihn, und das
        // Handy oeffnet genau ihn.
        return _roots.Any(root => Inside(root, full))
               || _outputs.Any(output => Inside(output, full)
                                         || string.Equals(output, full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Der Pfad einer Datei, die gelesen werden darf - oder null.</summary>
    public string? ResolveFile(string? path, out FileUse use)
    {
        use = FileUse.None;

        if (!Contains(path)) return null;

        string full = Full(path!);

        use = UseOf(full);

        if (use == FileUse.None) return null;

        try
        {
            var info = new FileInfo(full);

            if (!info.Exists) return null;
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;

            return full;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Der Pfad eines Ordners, in den geschaut werden darf - oder null.</summary>
    public string? ResolveFolder(string? path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path)) return null;

        string full = Full(path);

        if (full.Length == 0) return null;

        // Ein Wurzelordner selbst ist erlaubt - sonst kaeme man nie hinein.
        bool allowed = _roots.Concat(_outputs)
                             .Any(root => string.Equals(root, full, StringComparison.OrdinalIgnoreCase)
                                          || Inside(root, full));

        if (!allowed) return null;

        try
        {
            var info = new DirectoryInfo(full);

            if (!info.Exists) return null;
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;

            return full;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Ob ein Pfad unterhalb eines Wurzelordners liegt.
    ///
    /// Bis zum Trennzeichen verglichen, nicht als blosser Anfang - "C:\Projekte-alt"
    /// faengt mit "C:\Projekte" an und gehoert trotzdem nicht dazu.
    /// </summary>
    private static bool Inside(string root, string full)
    {
        if (full.Length <= root.Length) return false;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;

        char next = full[root.Length];

        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    private static string Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception)
        {
            // Ein Pfad mit unerlaubten Zeichen wirft hier. Das ist eine Antwort.
            return string.Empty;
        }
    }
}
