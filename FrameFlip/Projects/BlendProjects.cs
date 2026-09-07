using System.IO;
using System.Text.RegularExpressions;

namespace FrameFlip.Projects;

/// <summary>Eine einzelne .blend-Datei - eine Fassung eines Projekts.</summary>
public sealed record BlendVersion(
    string Path,
    string FileName,

    /// <summary>Die Nummer aus dem Namen, oder null bei einer Datei ohne Bezifferung.</summary>
    int? Number,

    /// <summary>
    /// Blenders eigene Sicherung: kitchen.blend1, kitchen.blend2 - null bei allem
    /// anderen.
    ///
    /// Sie gehoert zum Projekt, ist aber keine Fassung, die jemand angelegt hat:
    /// Blender schreibt sie beim Speichern automatisch, und zwar so, dass .blend1
    /// immer die zuletzt verdraengte ist und .blend2 die davor. In der Liste steht
    /// sie deshalb hinten.
    /// </summary>
    int? BackupIndex,

    /// <summary>
    /// Blenders Autosicherung: kiriko_12_14976_autosave.blend.
    ///
    /// Blender legt sie alle paar Minuten in seinem Temp-Ordner ab, mit der
    /// Prozessnummer im Namen. Nach ein paar Wochen Arbeit liegen dort Hunderte -
    /// in einem echten Projektordner waren es 90 Stueck gegen 28 richtige Fassungen.
    /// Sie gehoeren zum Projekt, aber niemand hat sie angelegt, und niemand sucht
    /// sie, ausser nach einem Absturz.
    /// </summary>
    bool IsAutosave,

    DateTime ModifiedUtc,
    long Bytes,

    /// <summary>
    /// Der Ordner, der das Projekt ausmacht - leer, wenn keiner bekannt ist.
    ///
    /// Steht hier einer, entscheidet er ueber die Zugehoerigkeit, und der Name aus
    /// der Datei zaehlt nicht mehr. Wer seine Arbeit in Ordner sortiert, hat die
    /// Frage damit bereits beantwortet.
    /// </summary>
    string ProjectFolder = "")
{
    public bool IsBackup => BackupIndex is not null;

    /// <summary>Weder Sicherung noch Autosicherung - eine Fassung, die jemand gespeichert hat.</summary>
    public bool IsReal => BackupIndex is null && !IsAutosave;

    /// <summary>Was in der Liste steht: "v3", "Sicherung 1 zu v3", "Autosicherung zu v12".</summary>
    public string Label => (IsAutosave, BackupIndex, Number) switch
    {
        (true, _, int number) => Localization.Strings.T("S_LabelAutosaveOf", number),
        (true, _, null) => Localization.Strings.T("S_LabelAutosave"),
        (false, int index, int number) => Localization.Strings.T("S_LabelBackupOf", index, number),
        (false, int index, null) => Localization.Strings.T("S_LabelBackup", index),
        (false, null, int number) => Localization.Strings.T("S_LabelVersion", number),
        _ => Localization.Strings.T("S_LabelNoNumber"),
    };
}

/// <summary>Alle Fassungen, die zusammengehoeren.</summary>
public sealed record BlendProject(string Key, string Name, string Folder, IReadOnlyList<BlendVersion> Versions)
{
    /// <summary>Die juengste echte Fassung - das, was man oeffnen will.</summary>
    public BlendVersion? Newest => Versions.FirstOrDefault(v => v.IsReal) ?? Versions.FirstOrDefault();

    public int VersionCount => Versions.Count(v => v.IsReal);

    /// <summary>Wieviele Autosicherungen dabei sind - die zaehlen nicht als Fassung.</summary>
    public int AutosaveCount => Versions.Count(v => v.IsAutosave);

    public DateTime TouchedUtc => Versions.Count == 0 ? DateTime.MinValue : Versions.Max(v => v.ModifiedUtc);
}

/// <summary>
/// Blender-Dateien zu Projekten zusammenfassen.
///
/// Wer an einer Szene arbeitet, hinterlaesst selten eine Datei. Er hinterlaesst
/// kitchen.blend, kitchen_v2.blend, kitchen_v3.blend und drei Sicherungen, die
/// Blender selbst geschrieben hat. Als flache Liste ist das unbrauchbar; als
/// Projekt mit Fassungen ist es genau das, was man sucht.
///
/// ZUERST ZAEHLT DER ORDNER. Wer seine Arbeit in Ordner sortiert hat, hat die Frage
/// "was gehoert zusammen?" bereits beantwortet, und keine Namensregel darf sich
/// darueber hinwegsetzen: In einem Projektordner lagen acht Dateien, die alle
/// verschieden hiessen - tracer_al_8_3e_cage_18_2RED neben tracer_al_8_3e - und aus
/// dem einen Projekt wurden acht. Liegt ein Projektordner vor, ist er der Massstab,
/// samt allem, was in seinen Unterordnern steckt.
///
/// Nur wo kein Ordner etwas hergibt - alle Dateien nebeneinander im durchsuchten
/// Verzeichnis - wird nach Namen zusammengefasst. Diese Zusammenfassung ist eine
/// VERMUTUNG, und das ist wichtig. Aus einem Dateinamen laesst sich nicht sicher
/// ablesen, ob zwei Dateien dieselbe Arbeit sind - cam1 und
/// cam2 koennen zwei Fassungen sein oder zwei Kameras. Deshalb ist die Regel
/// einfach genug, um sie im Kopf nachzuvollziehen, und deshalb laesst sie sich von
/// Hand ueberschreiben. Eine kluge Heuristik, die man nicht korrigieren kann, ist
/// schlechter als eine schlichte, die man versteht.
/// </summary>
public static class BlendProjects
{
    /// <summary>Blenders Sicherungen: .blend1, .blend2, ...</summary>
    private static readonly Regex BackupExtension = new(@"^\.blend(\d+)$", RegexOptions.IgnoreCase);

    /// <summary>"kitchen_v3", "kitchen-V03", "kitchen v3"</summary>
    private static readonly Regex WithV = new(@"^(.+?)[ _\-.]v(\d+)$", RegexOptions.IgnoreCase);

    /// <summary>"kitchen_003", "kitchen-2", "kitchen 12"</summary>
    private static readonly Regex WithSeparator = new(@"^(.+?)[ _\-.](\d+)$");

    /// <summary>
    /// "cam1", "shot02" - ohne Trennzeichen.
    ///
    /// Vor der Nummer muss etwas stehen, das keine Ziffer ist. Sonst waere
    /// "001.blend" die Fassung 1 eines Projekts namens "0", und das ist Unsinn: Ein
    /// Name, der nur aus Ziffern besteht, IST der Name.
    /// </summary>
    private static readonly Regex Trailing = new(@"^(.*?\D)(\d+)$");

    /// <summary>
    /// Blenders Autosicherung: "kiriko_12_14976_autosave" - Name, Prozessnummer,
    /// Endung.
    ///
    /// Der Kopf ist faul, damit auch die Prozessnummer mit abgeschnitten wird: Sonst
    /// hiesse das Projekt "kiriko_12_14976" und jede Sicherung stuende fuer sich.
    /// Blender sichert auch Sicherungen, deshalb wird wiederholt abgeschnitten -
    /// "kiriko_12_14976_autosave_22073_autosave" ist ein echter Dateiname.
    /// </summary>
    private static readonly Regex AutosaveTail = new(@"^(?<head>.+?)(?:_\d+)?_autosave$",
                                                     RegexOptions.IgnoreCase);

    private static readonly Regex[] Patterns = { WithV, WithSeparator, Trailing };

    /// <summary>Ob diese Datei ueberhaupt eine Blender-Datei ist.</summary>
    public static bool IsBlendFile(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        return extension.Equals(".blend", StringComparison.OrdinalIgnoreCase)
               || BackupExtension.IsMatch(extension);
    }

    /// <summary>
    /// Der Name, unter dem eine Datei einsortiert wird - dazu die Nummer, die dabei
    /// abgetrennt wurde, und die Sicherungsnummer aus der Endung.
    ///
    /// Kleingeschrieben, weil Windows Dateinamen nicht nach Gross- und
    /// Kleinschreibung unterscheidet: Kitchen.blend und kitchen_v2.blend gehoeren
    /// zusammen, und alles andere waere eine Ueberraschung.
    /// </summary>
    public static (string Key, int? Number, int? BackupIndex, bool IsAutosave) Split(string fileName)
    {
        // Eine Sicherung traegt ihre Nummer in der Endung. Der Name davor ist
        // unveraendert der des Originals und wird deshalb genauso zerlegt - sonst
        // landete kitchen_v2.blend1 in einem eigenen Projekt "kitchen_v2".
        var backup = BackupExtension.Match(System.IO.Path.GetExtension(fileName));
        var (head, number, autosave) = SplitStem(System.IO.Path.GetFileNameWithoutExtension(fileName));

        int? index = backup.Success
            ? int.TryParse(backup.Groups[1].Value, out int parsed) ? parsed : 1
            : null;

        return (Normalize(head), number, index, autosave);
    }

    /// <summary>
    /// Dateien zu Projekten zusammenfassen.
    ///
    /// <paramref name="assignments"/> ordnet einzelne Dateien von Hand einem Projekt
    /// zu und schlaegt die Vermutung. Damit laesst sich beides beheben: eine Datei,
    /// die anders heisst und deshalb allein steht, und zwei, die faelschlich
    /// zusammengefasst wurden.
    /// </summary>
    public static List<BlendProject> Group(
        IEnumerable<BlendVersion> files,
        IReadOnlyDictionary<string, string>? assignments = null)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);

        // Erster Durchgang: wohin jede Datei von sich aus gehoert. Das muss vor den
        // Zuordnungen von Hand feststehen - sonst haengt es von der Reihenfolge ab,
        // ob eine Zuordnung ein bestehendes Projekt trifft oder ein neues aufmacht.
        var entries = new List<(BlendVersion File, string Key)>();

        foreach (var file in files)
        {
            bool byFolder = file.ProjectFolder.Length > 0;

            string key = byFolder ? Normalize(file.ProjectFolder) : Split(file.FileName).Key;

            // Ein Name, von dem nach dem Kuerzen nichts uebrig bleibt, bleibt fuer
            // sich stehen - lieber ein Projekt zuviel als ein Sammelbecken.
            if (key.Length == 0) key = file.FileName.ToLowerInvariant();

            entries.Add((file, key));

            if (names.ContainsKey(key)) continue;

            // Der angezeigte Name: der Ordner, oder - ohne Ordner - der Dateiname in
            // seiner eigenen Schreibweise, "Kitchen" statt "kitchen".
            names[key] = byFolder
                ? System.IO.Path.GetFileName(file.ProjectFolder)
                : DisplayName(file.FileName);

            folders[key] = byFolder ? file.ProjectFolder : System.IO.Path.GetDirectoryName(file.Path) ?? "";
        }

        // Zweiter Durchgang: die Korrekturen von Hand. Sie schlagen beides, Ordner
        // wie Vermutung, und tragen den Schluessel des Ziels - deshalb landen sie im
        // bestehenden Projekt statt in einem zweiten mit gleichem Namen.
        var buckets = new Dictionary<string, List<BlendVersion>>(StringComparer.Ordinal);

        foreach (var (file, natural) in entries)
        {
            string key = natural;

            if (assignments is not null
                && assignments.TryGetValue(file.Path, out string? wanted)
                && wanted.Trim() is { Length: > 0 } forced)
            {
                key = Normalize(forced);

                if (!names.ContainsKey(key))
                {
                    // Ein Ziel, das es nicht mehr gibt. Dann steht da, was eingetippt
                    // wurde - und war es ein Pfad, nur sein letztes Stueck.
                    names[key] = forced.Contains(System.IO.Path.DirectorySeparatorChar)
                        ? System.IO.Path.GetFileName(forced.TrimEnd(System.IO.Path.DirectorySeparatorChar))
                        : forced;

                    folders[key] = string.Empty;
                }
            }

            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<BlendVersion>();

            list.Add(file);
        }

        return buckets
            .Select(pair => new BlendProject(pair.Key, names[pair.Key], folders[pair.Key], Order(pair.Value)))
            .OrderByDescending(project => project.TouchedUtc)
            .ToList();
    }

    /// <summary>Der Name, wie er auf der Kachel steht - in der Schreibweise der Datei.</summary>
    public static string DisplayName(string fileName)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string head = SplitStem(stem).Head;

        return head.Length > 0 ? head : stem;
    }

    /// <summary>
    /// Fassungen sortieren: echte zuerst, hoechste Nummer oben, Sicherungen hinten.
    ///
    /// Eine Datei ohne Nummer gilt als die aelteste - "kitchen.blend" ist ueblicher-
    /// weise der Anfang, und kitchen_v2 kam danach.
    /// </summary>
    private static List<BlendVersion> Order(List<BlendVersion> versions)
    {
        var real = versions
            .Where(v => v.IsReal)
            .OrderByDescending(v => v.Number ?? 0)
            .ThenByDescending(v => v.ModifiedUtc);

        // Bei Blenders Sicherungen zaehlt die Nummer andersherum: .blend1 ist die
        // zuletzt verdraengte Fassung, .blend2 die davor. Aufsteigend heisst hier
        // also ebenfalls "das Juengste zuerst".
        var backups = versions
            .Where(v => v.IsBackup && !v.IsAutosave)
            .OrderBy(v => v.BackupIndex ?? 1)
            .ThenByDescending(v => v.ModifiedUtc);

        // Autosicherungen ganz hinten, die juengste oben. Bei ihnen sagt der Name
        // nichts ueber das Alter - die Zahl darin ist die Prozessnummer.
        var autosaves = versions
            .Where(v => v.IsAutosave)
            .OrderByDescending(v => v.ModifiedUtc);

        return real.Concat(backups).Concat(autosaves).ToList();
    }

    /// <summary>Den Namen von seiner Bezifferung trennen - ohne die Endung.</summary>
    private static (string Head, int? Number, bool Autosave) SplitStem(string stem)
    {
        bool autosave = false;

        // Erst die Autosicherungen abschneiden, und zwar so oft, wie sie sich
        // stapeln. Was uebrig bleibt, ist der Name der Datei, aus der Blender
        // gesichert hat - und damit dasselbe Projekt.
        while (AutosaveTail.Match(stem) is { Success: true } tail)
        {
            string head = Trim(tail.Groups["head"].Value);
            if (head.Length == 0) break;

            stem = head;
            autosave = true;
        }

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(stem);
            if (!match.Success) continue;

            string head = Trim(match.Groups[1].Value);

            // Ein Rest, der nur noch aus Trennzeichen besteht, war keine Bezifferung,
            // sondern der ganze Name.
            if (head.Length == 0) break;

            return (head, int.TryParse(match.Groups[2].Value, out int number) ? number : null, autosave);
        }

        return (Trim(stem), null, autosave);
    }

    private static string Trim(string text) => text.Trim().TrimEnd(' ', '_', '-', '.');

    /// <summary>Kleinschreibung und keine Trennzeichen am Rand - sonst nichts.</summary>
    private static string Normalize(string text) => Trim(text).ToLowerInvariant();
}
