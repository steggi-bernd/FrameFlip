using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;

namespace FrameFlip.Projects;

/// <summary>Eine Blender-Datei, die FrameFlip einmal gesehen hat.</summary>
public sealed class KnownBlend
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Wann sie zuletzt aufgefallen ist - beim Rendern oder beim Oeffnen.</summary>
    public DateTime SeenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Was FrameFlip ueber die Projekte auf diesem Rechner weiss.</summary>
public sealed class ProjectKnowledge
{
    /// <summary>Ordner, die durchsucht werden. Von Hand eingetragen, nie geraten.</summary>
    public List<string> Folders { get; set; } = new();

    /// <summary>Einzelne Dateien, die beim Rendern aufgefallen sind.</summary>
    public List<KnownBlend> Files { get; set; } = new();

    /// <summary>
    /// Ordner, die FrameFlip fuer einen Render angelegt hat.
    ///
    /// Sie sind vom Handy aus lesbar, auch wenn die Bibliothek nicht freigegeben ist -
    /// wer einen Render startet, muss sein Ergebnis ansehen koennen. Mehr als diese
    /// Ordner wird dadurch nicht erreichbar.
    /// </summary>
    public List<string> RenderOutputs { get; set; } = new();

    /// <summary>
    /// Zuordnungen von Hand: Dateipfad auf Projektnamen.
    ///
    /// Das ist die Korrektur zur Vermutung in <see cref="BlendProjects"/> - und der
    /// Grund, warum die Vermutung schlicht bleiben darf.
    /// </summary>
    public Dictionary<string, string> Assignments { get; set; } = new();
}

/// <summary>
/// Die Projektliste, wie sie zwischen zwei Starts erhalten bleibt.
///
/// Eigene Datei neben der Konfiguration, aus demselben Grund wie bei den Sequenzen:
/// Sie aendert sich bei jedem Render, die Einstellungen aendern sich selten.
///
/// FrameFlip durchsucht von sich aus keine Platte. Es kennt genau zwei Quellen:
/// Ordner, die jemand eingetragen hat, und .blend-Dateien, die waehrend eines
/// Renders gemeldet wurden. Was hier nicht steht, sucht auch niemand.
/// </summary>
public static class ProjectLibrary
{
    /// <summary>Soviele einzeln gemerkte Dateien - danach faellt die aelteste heraus.</summary>
    public const int FileLimit = 200;

    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(SettingsStore.DirectoryPath, "projects.json");

    /// <summary>Nie null, im Zweifel leer.</summary>
    public static ProjectKnowledge Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return new ProjectKnowledge();

                var data = JsonSerializer.Deserialize<ProjectKnowledge>(File.ReadAllText(FilePath));
                if (data is null) return new ProjectKnowledge();

                data.Folders = data.Folders.Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                                           .ToList();
                data.Files = data.Files.Where(f => f.Path.Length > 0).ToList();
                data.RenderOutputs = data.RenderOutputs.Where(f => f.Length > 0)
                                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                return data;
            }
            catch (Exception)
            {
                return new ProjectKnowledge();
            }
        }
    }

    public static void Save(ProjectKnowledge data)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Format));
            }
            catch (Exception)
            {
                // Eine Merkliste ist Bequemlichkeit. Nichts daran darf etwas anhalten.
            }
        }
    }

    /// <summary>Soviele Ausgabeordner bleiben erreichbar. Der aelteste faellt heraus.</summary>
    public const int OutputLimit = 60;

    /// <summary>
    /// Einen Ausgabeordner merken, den ein Render angelegt hat.
    ///
    /// Die Liste ist begrenzt: Sie ist eine Erlaubnis, und eine Erlaubnis, die nie
    /// endet und immer nur waechst, ist keine gute Erlaubnis.
    /// </summary>
    public static void NoteOutput(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        var data = Load();
        string trimmed = folder.Trim().TrimEnd('\\', '/');

        data.RenderOutputs.RemoveAll(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase));
        data.RenderOutputs.Insert(0, trimmed);

        if (data.RenderOutputs.Count > OutputLimit)
            data.RenderOutputs.RemoveRange(OutputLimit, data.RenderOutputs.Count - OutputLimit);

        Save(data);
    }

    /// <summary>Einen Ordner aufnehmen. Doppelte werden still verschluckt.</summary>
    public static void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        var data = Load();
        string trimmed = folder.Trim().TrimEnd('\\', '/');

        if (data.Folders.Any(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase))) return;

        data.Folders.Add(trimmed);
        Save(data);
    }

    public static void RemoveFolder(string folder)
    {
        var data = Load();

        if (data.Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) == 0) return;

        Save(data);
    }

    /// <summary>
    /// Eine Datei merken, die beim Rendern aufgefallen ist.
    ///
    /// Das ist der Weg, auf dem die Liste ohne Zutun waechst: Wer rendert, meldet
    /// seine .blend-Datei ueber die Bruecke, und ab dann steht das Projekt da - auch
    /// wenn es in keinem eingetragenen Ordner liegt.
    /// </summary>
    public static void Note(string? blendFile)
    {
        if (string.IsNullOrWhiteSpace(blendFile) || !BlendProjects.IsBlendFile(blendFile)) return;

        var data = Load();

        data.Files.RemoveAll(f => string.Equals(f.Path, blendFile, StringComparison.OrdinalIgnoreCase));
        data.Files.Insert(0, new KnownBlend { Path = blendFile, SeenUtc = DateTime.UtcNow });

        if (data.Files.Count > FileLimit) data.Files.RemoveRange(FileLimit, data.Files.Count - FileLimit);

        Save(data);
    }

    /// <summary>Eine Datei von Hand einem Projekt zuordnen. Ein leerer Name hebt die Zuordnung auf.</summary>
    public static void Assign(string path, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var data = Load();

        if (string.IsNullOrWhiteSpace(projectName)) data.Assignments.Remove(path);
        else data.Assignments[path] = projectName.Trim();

        Save(data);
    }

    /// <summary>
    /// Der aktuelle Stand: gespeichertes Wissen plus das, was gerade auf der Platte
    /// liegt.
    ///
    /// Geht auf die Platte und gehoert deshalb nicht in den Oberflaechen-Thread.
    /// </summary>
    public static List<BlendProject> Scan()
    {
        var data = Load();

        var files = ProjectScanner.Collect(data.Folders, data.Files.Select(f => f.Path));

        return BlendProjects.Group(files, data.Assignments);
    }
}
