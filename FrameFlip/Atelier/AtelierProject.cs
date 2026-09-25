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

    /// <summary>Liest das Projekt einer Folge - oder null, wenn es keines gibt oder es sich nicht lesen laesst.</summary>
    public AtelierProject? Load(SequenceKey key)
    {
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
