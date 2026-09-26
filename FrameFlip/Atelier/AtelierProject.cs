using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Sequencing;

namespace FrameFlip.Atelier;

/// <summary>
/// Welche Sequenz gemeint ist: Ordner, Namensteile und Endung - ohne die Nummer und ohne
/// ihre Stellenzahl. Bild 47 und Bild 48 derselben Folge sind dasselbe Projekt, und
/// "f_99" und "f_100" auch. Ein Einzelbild ohne Nummer ist eine Folge aus einem.
/// </summary>
internal sealed record SequenceKey(string Folder, string Prefix, string Suffix, string Extension)
{
    /// <summary>Die Kennung zu einem Bildpfad - oder null, wenn der Pfad keinen Ordner hat.</summary>
    public static SequenceKey? Of(string imagePath)
    {
        if (SequenceScanner.DerivePattern(imagePath) is not { } pattern) return null;

        return new SequenceKey(Path.GetFullPath(pattern.Directory), pattern.Prefix, pattern.Suffix,
                               pattern.Extension.ToLowerInvariant());
    }

    /// <summary>
    /// Der lesbare Name: die Namensteile ohne Trennzeichen am Rand, die Nummer dazwischen
    /// weggelassen. "render_0047.exr" heisst "render", "shot-010_v2.0001.png" "shot-010_v2".
    /// </summary>
    public string Name
    {
        get
        {
            string name = (Prefix.TrimEnd('_', '-', '.', ' ') + Suffix.TrimStart('_', '-', '.', ' ')).Trim();
            return name.Length > 0 ? name : "sequenz";
        }
    }

    /// <summary>Die Folge als Muster, wie sie im Projekt steht - "render_#.exr".</summary>
    public string Pattern => Prefix + "#" + Suffix + Extension;

    public bool Equals(SequenceKey? other)
        => other is not null &&
           string.Equals(Folder, other.Folder, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Prefix, other.Prefix, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Suffix, other.Suffix, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Extension, other.Extension, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => HashCode.Combine(Folder.ToLowerInvariant(), Prefix.ToLowerInvariant(), Suffix.ToLowerInvariant(), Extension);
}

/// <summary>
/// Der Stand des Ateliers fuer eine Sequenz, wie er in der Projektdatei steht. Siehe
/// docs/Projekte-und-Masken.md, Abschnitt 3.2.
/// </summary>
internal sealed class AtelierProject
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Die Folge als Muster - zum Wiedererkennen, nicht zum Suchen.</summary>
    public string Sequence { get; set; } = "";

    /// <summary>Das Bild der Folge, das zuletzt im Atelier offen war - nur der Dateiname.</summary>
    public string? Frame { get; set; }

    public ImageAdjustments? Adjustments { get; set; }

    public GradingStack? Grading { get; set; }

    public LayerStack? Layers { get; set; }

    /// <summary>Der Graph des Knotenmodus - als eingebetteter Text-Baum, lesbar in der Datei. Null: Stapel.</summary>
    public JsonNode? Nodes { get; set; }

    public DateTime SavedUtc { get; set; }
}

/// <summary>
/// Die Ablage der Projektdateien: <c>&lt;Quellordner&gt;\FrameFlip\&lt;name&gt;&lt;endung&gt;.ffproj</c>.
///
/// Laesst sich der Quellordner nicht beschreiben (Netzlaufwerk, schreibgeschuetzt), dann
/// unter den Einstellungen in <c>projects\&lt;kennung&gt;\</c>. Gelesen wird zuerst am Quellordner,
/// dann dort. Geschrieben wird ueber eine temporaere Datei, die danach die alte ersetzt -
/// ein Absturz mitten im Schreiben zerreisst nichts.
/// </summary>
internal sealed class AtelierProjectStore
{
    /// <summary>Der Unterordner im Quellordner - auch fuer die Schnell-Exporte.</summary>
    public const string FolderName = "FrameFlip";

    public const string FileExtension = ".ffproj";

    /// <summary>Der Ordner neben der Projektdatei fuer das, was nicht in sie gehoert - der Verlauf der Masken.</summary>
    public const string DataExtension = ".ffdata";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Namen statt Zahlen: Eine neue Maskenart mitten in der Liste deutet eine
        // Projektdatei nicht um. Zahlen werden weiterhin gelesen.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Func<string> _fallbackRoot;

    /// <param name="fallbackRoot">Wo Projekte liegen, deren Quellordner nicht beschreibbar ist.</param>
    internal AtelierProjectStore(Func<string>? fallbackRoot = null)
        => _fallbackRoot = fallbackRoot ?? (() => Path.Combine(SettingsStore.DirectoryPath, "projects"));

    /// <summary>Der Ordner im Quellordner, in dem Projekt und Schnell-Exporte liegen.</summary>
    public static string FolderOf(SequenceKey key) => Path.Combine(key.Folder, FolderName);

    /// <summary>Die Projektdatei im Quellordner.</summary>
    public static string PrimaryPath(SequenceKey key) => Path.Combine(FolderOf(key), FileNameOf(key));

    /// <summary>Die Projektdatei unter den Einstellungen - falls der Quellordner nicht beschreibbar ist.</summary>
    public string FallbackPath(SequenceKey key)
    {
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            (key.Folder + "|" + key.Pattern).ToLowerInvariant())))[..16];

        return Path.Combine(_fallbackRoot(), id, FileNameOf(key));
    }

    private static string FileNameOf(SequenceKey key) => key.Name + key.Extension + FileExtension;

    /// <summary>Wo das Projekt zuletzt geschrieben wurde - fuer die Anzeige.</summary>
    public string? LastWritten { get; private set; }

    private static readonly object Gate = new();

    /// <summary>
    /// Die Schreibvorgaenge aller Seiten, der Reihe nach. Prozessweit und nicht je Seite:
    /// Eine Seite, die geht, schreibt vielleicht noch, waehrend die naechste dieselbe
    /// Folge oeffnet - und die soll den neuen Stand lesen, nicht den davor.
    /// </summary>
    private static Task s_pending = Task.CompletedTask;

    /// <summary>
    /// Reiht einen Schreibvorgang ein. <paramref name="done"/> erfaehrt im Hintergrund, ob
    /// geschrieben wurde. Der Abdruck muss fertig sein - geschrieben wird er spaeter.
    /// </summary>
    internal Task Enqueue(SequenceKey key, AtelierProject project, Action<bool> done)
    {
        lock (Gate)
        {
            s_pending = s_pending.ContinueWith(_ => done(Save(key, project)), TaskScheduler.Default);
            return s_pending;
        }
    }

    /// <summary>Wartet, bis alles Eingereihte geschrieben ist - beim Ende und vor dem Lesen.</summary>
    internal static void WaitForWrites(TimeSpan timeout)
    {
        Task pending;
        lock (Gate) pending = s_pending;

        try { pending.Wait(timeout); }
        catch (AggregateException) { /* ein fehlgeschlagener Schreibvorgang hat sich schon gemeldet */ }
    }

    /// <summary>Liest das Projekt einer Folge - oder null, wenn es keines gibt oder es sich nicht lesen laesst.</summary>
    public AtelierProject? Load(SequenceKey key)
    {
        // Erst schreiben lassen, was noch unterwegs ist.
        WaitForWrites(TimeSpan.FromSeconds(10));

        foreach (string path in new[] { PrimaryPath(key), FallbackPath(key) })
        {
            try
            {
                if (!File.Exists(path)) continue;

                var project = JsonSerializer.Deserialize<AtelierProject>(File.ReadAllText(path), Options);
                if (project is not null) return project;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // Eine defekte Projektdatei ist kein Grund, das Bild nicht zu oeffnen.
                // Sie bleibt liegen und wird erst beim naechsten Speichern ersetzt.
                SettingsStore.Trace("Projekt nicht lesbar: " + path + " - " + e.Message);
            }
        }

        return null;
    }

    /// <summary>
    /// Wo eine Beidatei des Projekts liegt: im Ordner neben der Projektdatei - am
    /// Quellordner, sonst unter den Einstellungen. <paramref name="relative"/> ist der Weg
    /// darin, etwa "verlauf/maske.json".
    /// </summary>
    public IEnumerable<string> SidePaths(SequenceKey key, string relative)
        => new[] { PrimaryPath(key), FallbackPath(key) }
            .Select(project => Path.Combine(Path.GetDirectoryName(project)!,
                                            Path.GetFileNameWithoutExtension(project) + DataExtension, relative));

    /// <summary>Liest eine Beidatei - oder null, wenn es keine gibt oder sie sich nicht lesen laesst.</summary>
    public byte[]? LoadSide(SequenceKey key, string relative)
    {
        WaitForWrites(TimeSpan.FromSeconds(10));

        foreach (string path in SidePaths(key, relative))
        {
            try
            {
                if (File.Exists(path)) return File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SettingsStore.Trace("Beidatei nicht lesbar: " + path + " - " + e.Message);
            }
        }

        return null;
    }

    /// <summary>Reiht das Schreiben einer Beidatei ein - der Reihe nach mit den Projekten.</summary>
    internal Task EnqueueSide(SequenceKey key, string relative, byte[] data, Action<bool>? done = null)
        => EnqueueSide(key, relative, () => data, done);

    /// <summary>
    /// Wie oben, aber der Inhalt entsteht erst im Hintergrund - fuer einen Abdruck, dessen
    /// Umwandlung in Text den Oberflaechenfaden nicht aufhalten soll.
    /// </summary>
    internal Task EnqueueSide(SequenceKey key, string relative, Func<byte[]> data, Action<bool>? done = null)
    {
        lock (Gate)
        {
            s_pending = s_pending.ContinueWith(_ =>
            {
                bool written = SaveSide(key, relative, data());
                done?.Invoke(written);
            }, TaskScheduler.Default);

            return s_pending;
        }
    }

    /// <summary>Schreibt eine Beidatei - am Quellordner, sonst unter den Einstellungen. False, wenn beides nicht ging.</summary>
    public bool SaveSide(SequenceKey key, string relative, byte[] data)
    {
        foreach (string path in SidePaths(key, relative))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                string temp = path + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, path, overwrite: true);

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SettingsStore.Trace("Beidatei nicht schreibbar: " + path + " - " + e.Message);
            }
        }

        return false;
    }

    /// <summary>
    /// Schreibt das Projekt - am Quellordner, sonst unter den Einstellungen. False, wenn
    /// beides nicht ging.
    /// </summary>
    public bool Save(SequenceKey key, AtelierProject project)
    {
        project.Sequence = key.Pattern;
        project.Version = AtelierProject.CurrentVersion;
        project.SavedUtc = DateTime.UtcNow;

        string json = JsonSerializer.Serialize(project, Options);

        foreach (string path in new[] { PrimaryPath(key), FallbackPath(key) })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);

                LastWritten = path;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SettingsStore.Trace("Projekt nicht schreibbar: " + path + " - " + e.Message);
            }
        }

        return false;
    }
}

/// <summary>
/// Das Rezept einer Sequenz im Speicher - die Ablage hinter der Bearbeitungssitzung,
/// solange ein Projekt offen ist. Gelesen und geschrieben wird die Datei vom
/// <see cref="AtelierProjectKeeper"/>, nicht bei jeder Aenderung.
/// </summary>
internal sealed class ProjectRecipeStore : IAtelierRecipeStore
{
    public ImageAdjustments? Adjustments { get; set; }

    public GradingStack? Grading { get; set; }

    public LayerStack? Layers { get; set; }

    public string? Nodes { get; set; }

    /// <summary>Aus einer gelesenen Projektdatei.</summary>
    public static ProjectRecipeStore From(AtelierProject project) => new()
    {
        Adjustments = project.Adjustments,
        Grading = project.Grading,
        Layers = project.Layers,
        Nodes = project.Nodes?.ToJsonString(),
    };

    /// <summary>Ein Abdruck zum Schreiben - eigene Kopien, damit weitergearbeitet werden kann, waehrend geschrieben wird.</summary>
    public AtelierProject ToProject(string? frame) => new()
    {
        Frame = frame,
        Adjustments = Adjustments,
        Grading = Grading?.Clone(),
        Layers = Layers?.Clone(),
        Nodes = Nodes is { Length: > 0 } json ? JsonNode.Parse(json) : null,
    };
}
