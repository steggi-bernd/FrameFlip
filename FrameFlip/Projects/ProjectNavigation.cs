using System.IO;

namespace FrameFlip.Projects;

/// <summary>
/// Auswahl und Ordnerweg des Projektbrowsers. Die Seite entscheidet weiter, wann
/// sie zeichnet oder eine Vorschau schliesst; diese Klasse kennt weder WPF noch
/// Dateizugriffe. Alle Aufrufe kommen vom UI-Thread.
/// </summary>
internal sealed class ProjectNavigation
{
    internal IReadOnlyList<BlendProject> Projects { get; private set; } = Array.Empty<BlendProject>();
    internal BlendProject? Project { get; private set; }
    internal string? Folder { get; private set; }
    internal bool VersionsOpen { get; private set; }

    internal string Root => Project is null ? string.Empty : RootOf(Project);
    internal string CurrentFolder => Folder ?? Root;

    internal void Apply(IReadOnlyList<BlendProject> projects)
    {
        Projects = projects;
        // Ein neuer Scan liefert neue Objekte. Die Auswahl haengt am Schluessel;
        // ein verschwundenes Projekt gibt auch seinen Ordner frei.
        if (Project is not null)
            Project = Projects.FirstOrDefault(p => p.Key == Project.Key);
        if (Project is null) Folder = null;
    }

    internal void OpenProject(BlendProject project)
    {
        Project = project;
        Folder = RootOf(project);
    }

    internal void OpenFolder(string folder) => Folder = folder;

    internal void ShowOverview()
    {
        Project = null;
        Folder = null;
    }

    internal void ToggleVersions() => VersionsOpen = !VersionsOpen;

    /// <summary>Ordnerweise zurueck, von der Projektwurzel in die Uebersicht.</summary>
    internal bool Back()
    {
        if (Project is null) return false;

        string root = Root;
        string folder = CurrentFolder;
        if (!string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
        {
            string? up = Path.GetDirectoryName(folder);
            Folder = up is not null && up.Length >= root.Length ? up : root;
            return true;
        }

        ShowOverview();
        return true;
    }

    /// <summary>Die Unterordner zwischen Projektwurzel und aktuellem Ordner, in Wegreihenfolge.</summary>
    internal IEnumerable<(string Name, string Path)> Breadcrumbs()
    {
        string root = Root;
        string folder = CurrentFolder;
        if (folder.Length <= root.Length || !folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            yield break;

        string walked = root;
        foreach (string part in folder[root.Length..].Split(Path.DirectorySeparatorChar,
                                                           StringSplitOptions.RemoveEmptyEntries))
        {
            walked = Path.Combine(walked, part);
            yield return (part, walked);
        }
    }

    /// <summary>Expliziter Projektordner, sonst der Ordner der juengsten Fassung.</summary>
    internal static string RootOf(BlendProject project)
    {
        if (project.Folder.Length > 0) return project.Folder;
        string? path = project.Newest?.Path ?? project.Versions.FirstOrDefault()?.Path;
        return path is null ? string.Empty : Path.GetDirectoryName(path) ?? string.Empty;
    }
}
