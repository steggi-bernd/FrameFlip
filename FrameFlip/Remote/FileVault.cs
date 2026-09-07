using System.IO;
using FrameFlip.Configuration;

namespace FrameFlip.Remote;

/// <summary>Eine Datei im Austauschordner, so wie das Handy sie zu sehen bekommt.</summary>
public sealed record VaultFile(string Name, long Bytes, DateTime ModifiedUtc);

/// <summary>Warum etwas nicht geht. Der Grund geht wortwoertlich ans Handy.</summary>
public enum VaultRefusal
{
    None,
    Disabled,
    NoFolder,
    BadName,
    Missing,
    TooBig,
    WriteDisabled,
    Exists,
}

/// <summary>
/// Das Tor zwischen Handy und Festplatte.
///
/// Bis hierher konnte das Handy nur ZUSEHEN: Zahlen und ein Vorschaubild, alles vom
/// Rechner aus geschickt. Dateien zu holen und abzulegen ist etwas anderes - damit
/// wird aus einem Lesekanal ein Schreibkanal, und ein verlorenes Handy ist dann
/// nicht mehr nur ein Mithoerer. Deshalb steht hier ein Tor und keine Tuer:
///
/// - Es ist zu, bis es jemand am Rechner aufmacht. Zweimal: einmal fuers Lesen,
///   einmal fuers Schreiben.
/// - Es gibt genau EINEN Ordner. Keine Unterordner, keine Pfade - ein Name ohne
///   Trennzeichen, sonst nichts.
/// - Nur .blend. Keine .exe, keine .bat, keine .dll, die sich irgendwo
///   dazwischenschiebt.
/// - Ueberschrieben wird nichts. Wer schon da ist, bleibt.
///
/// Die Pruefung laeuft doppelt: erst der Name, dann der aufgeloeste Pfad. Das ist
/// Absicht. Namensregeln haben Loecher, die man erst kennt, wenn sie ausgenutzt
/// wurden - "..", Doppelpunkte fuer alternative Datenstroeme, Geraetenamen wie CON.
/// Die zweite Pruefung fragt das Dateisystem, wo die Datei am Ende laege, und
/// verlangt, dass das unmittelbar der Austauschordner ist.
/// </summary>
public sealed class FileVault
{
    /// <summary>Groesste Datei, die angenommen wird. Eine .blend ueber 2 GB ist eine Ausnahme.</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Soviele Eintraege werden aufgelistet. Ein Ordner mit mehr ist kein Austauschordner.</summary>
    public const int ListLimit = 500;

    /// <summary>Namen, die Windows fuer Geraete reserviert - auch mit Endung.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public FileVault(bool readEnabled, bool writeEnabled, string folder)
    {
        Folder = folder?.Trim() ?? string.Empty;

        // Schreiben ohne Lesen gibt es nicht: Wer nichts sehen darf, soll auch nichts
        // ablegen koennen - sonst waere der Ordner ein blinder Briefkasten.
        ReadEnabled = readEnabled && Folder.Length > 0;
        WriteEnabled = ReadEnabled && writeEnabled;
    }

    public static FileVault From(AppSettings settings)
        => new(settings.FileAccessEnabled, settings.FilePushEnabled, settings.FileFolder);

    public bool ReadEnabled { get; }
    public bool WriteEnabled { get; }
    public string Folder { get; }

    /// <summary>Ob der Ordner ueberhaupt existiert. Getrennt geprueft, damit die Antwort stimmt.</summary>
    public bool FolderExists => Folder.Length > 0 && Directory.Exists(Folder);

    /// <summary>
    /// Ob dieser Name ueberhaupt einer ist - ohne die Platte zu fragen.
    ///
    /// Erlaubt ist genau ein schlichter Dateiname mit der Endung .blend. Nicht
    /// .blend1: Blenders Sicherungen sind nichts, was man verschickt, und jede
    /// Endung mehr ist eine Regel mehr, die man im Kopf behalten muss.
    /// </summary>
    public static bool IsAllowedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 180) return false;
        if (name != name.Trim()) return false;

        // Faengt Trennzeichen, Doppelpunkte (alternative Datenstroeme) und
        // Steuerzeichen in einem Rutsch ab.
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

        if (!name.EndsWith(".blend", StringComparison.OrdinalIgnoreCase)) return false;

        string stem = name[..^".blend".Length];

        if (stem.Length == 0) return false;
        if (stem.EndsWith('.') || stem.EndsWith(' ')) return false;
        if (Reserved.Contains(stem)) return false;

        return true;
    }

    /// <summary>
    /// Der vollstaendige Pfad zu einem Namen - oder null, wenn er nicht infrage kommt.
    ///
    /// Die zweite Pruefung: Wo die Datei nach dem Aufloesen wirklich laege. Nur wenn
    /// das unmittelbar der Austauschordner ist, geht es weiter.
    /// </summary>
    public string? Resolve(string? name)
    {
        if (!ReadEnabled || !IsAllowedName(name)) return null;

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder));
            string full = Path.GetFullPath(Path.Combine(root, name!));

            string? parent = Path.GetDirectoryName(full);

            if (parent is null) return null;

            return string.Equals(Path.TrimEndingDirectorySeparator(parent), root,
                                 StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Was im Austauschordner liegt. Nie null, im Zweifel leer.</summary>
    public List<VaultFile> List()
    {
        var found = new List<VaultFile>();

        if (!ReadEnabled) return found;

        try
        {
            if (!Directory.Exists(Folder)) return found;

            foreach (string path in Directory.EnumerateFiles(Folder, "*.blend"))
            {
                string name = Path.GetFileName(path);

                // Die Aufzaehlung mit Muster nimmt unter Windows auch kurze Namen und
                // Endungen mit Anhang mit - deshalb nochmal gegen dieselbe Regel.
                if (!IsAllowedName(name)) continue;

                var info = new FileInfo(path);

                found.Add(new VaultFile(name, info.Length, info.LastWriteTimeUtc));

                if (found.Count >= ListLimit) break;
            }
        }
        catch (Exception)
        {
            // Ein Ordner, der gerade verschwindet, ist kein Grund fuer eine Ausnahme
            // im Netzwerkpfad.
        }

        return found.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Ob sich diese Datei holen laesst - und warum nicht.</summary>
    public VaultRefusal CanRead(string? name, out string? path)
    {
        path = null;

        if (!ReadEnabled) return Folder.Length == 0 ? VaultRefusal.NoFolder : VaultRefusal.Disabled;
        if (!FolderExists) return VaultRefusal.NoFolder;

        path = Resolve(name);

        if (path is null) return VaultRefusal.BadName;
        if (!File.Exists(path)) return VaultRefusal.Missing;

        try
        {
            if (new FileInfo(path).Length > MaxBytes) return VaultRefusal.TooBig;
        }
        catch (Exception)
        {
            return VaultRefusal.Missing;
        }

        return VaultRefusal.None;
    }

    /// <summary>Ob sich diese Datei ablegen laesst - und warum nicht.</summary>
    public VaultRefusal CanWrite(string? name, long size, out string? path)
    {
        path = null;

        if (!ReadEnabled) return Folder.Length == 0 ? VaultRefusal.NoFolder : VaultRefusal.Disabled;
        if (!WriteEnabled) return VaultRefusal.WriteDisabled;
        if (!FolderExists) return VaultRefusal.NoFolder;
        if (size < 0 || size > MaxBytes) return VaultRefusal.TooBig;

        if (Resolve(name) is null) return VaultRefusal.BadName;

        // Mit Markierung und freiem Namen. Ueberschrieben wird dadurch nie - eine
        // Datei, an der jemand seit Wochen arbeitet, ist der denkbar schlechteste
        // Ort fuer ein Missverstaendnis -, und am Namen sieht man, was von aussen kam.
        // Auch eine liegengebliebene .teil-Datei belegt ihren Namen. Mit CreateNew
        // beim eigentlichen Oeffnen ist das die zweite Haelfte gegen versehentliches
        // Ueberschreiben zwischen Pruefung und Schreiben.
        path = System.IO.Path.Combine(Folder, FreeName(Folder, name!, candidate =>
            File.Exists(candidate) || File.Exists(PartialPath(candidate))));

        return VaultRefusal.None;
    }

    /// <summary>Der Grund im Klartext - genau so steht er dann in der App.</summary>
    public static string Explain(VaultRefusal refusal) => refusal switch
    {
        VaultRefusal.Disabled => "File access is switched off on the machine.",
        VaultRefusal.NoFolder => "No exchange folder is set on the machine.",
        VaultRefusal.BadName => "That name will not do. A .blend file without a path is allowed.",
        VaultRefusal.Missing => "The file is not in the exchange folder.",
        VaultRefusal.TooBig => "The file is too large.",
        VaultRefusal.WriteDisabled => "Storing is not allowed on the machine.",
        VaultRefusal.Exists => "A file with that name is already there.",
        _ => "",
    };

    /// <summary>Was an den Namen einer angenommenen Datei gehaengt wird.</summary>
    public const string Mark = "_exchanged";

    /// <summary>
    /// Der Name, unter dem eine Datei vom Handy abgelegt wird.
    ///
    /// Jede angenommene Datei traegt "_exchanged" im Namen, und zwar aus zwei
    /// Gruenden. Erstens sieht man an einem Ordner voller .blend-Dateien sofort,
    /// welche von aussen kam - und das will man wissen, bevor man sie oeffnet.
    /// Zweitens kann eine Datei so nie eine bestehende verdraengen: Selbst wenn sie
    /// genauso heisst wie die, an der jemand seit Wochen arbeitet, ist es ein
    /// anderer Name.
    ///
    /// Zweimal angehaengt wird nicht - wer eine Datei zurueckschickt, bekommt keine
    /// "szene_exchanged_exchanged.blend".
    /// </summary>
    public static string Exchanged(string name)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(name);
        string extension = System.IO.Path.GetExtension(name);

        if (stem.EndsWith(Mark, StringComparison.OrdinalIgnoreCase)) return stem + extension;

        return stem + Mark + extension;
    }

    /// <summary>
    /// Ein freier Name in diesem Ordner - "_2", "_3", falls noetig.
    ///
    /// Auch mit der Markierung kann derselbe Name zweimal kommen; wer dieselbe Datei
    /// zweimal schickt, will die erste nicht verlieren.
    /// </summary>
    public static string FreeName(string folder, string name, Func<string, bool> exists)
    {
        string wanted = Exchanged(name);
        string stem = System.IO.Path.GetFileNameWithoutExtension(wanted);
        string extension = System.IO.Path.GetExtension(wanted);

        for (int run = 1; run <= 999; run++)
        {
            string candidate = run == 1 ? wanted : $"{stem}_{run}{extension}";

            if (!exists(System.IO.Path.Combine(folder, candidate))) return candidate;
        }

        return $"{stem}_{DateTime.Now:HHmmss}{extension}";
    }

    /// <summary>
    /// Der Pfad, unter dem eine ankommende Datei entsteht, bevor sie fertig ist.
    ///
    /// Erst vollstaendig schreiben, dann umbenennen: Eine abgebrochene Uebertragung
    /// hinterlaesst sonst eine halbe .blend, die aussieht wie eine ganze. Die Endung
    /// ist mit Absicht keine, die Blender oeffnet.
    /// </summary>
    public static string PartialPath(string target) => target + ".teil";
}
