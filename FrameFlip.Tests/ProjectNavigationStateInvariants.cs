using System.IO;
using FrameFlip.Projects;

namespace FrameFlip.Tests;

public static class ProjectNavigationStateInvariants
{
    public static void Run()
    {
        Roots();
        Navigation();
        Refresh();
    }

    private static BlendVersion Version(string path, int? backup = null)
        => new(path, Path.GetFileName(path), null, backup, false, DateTime.UnixEpoch, 0);

    private static void Roots()
    {
        Check.Group("Projektnavigation - Ausgangsordner");
        var real = Version(@"C:\projects\kitchen\scene.blend");
        var backup = Version(@"C:\backup\scene.blend1", 1);
        var project = new BlendProject("kitchen", "Kitchen", @"D:\renders", new[] { backup, real });
        Check.That(ProjectNavigation.RootOf(project) == @"D:\renders", "ein expliziter Projektordner hat Vorrang");
        Check.That(ProjectNavigation.RootOf(project with { Folder = "" }) == @"C:\projects\kitchen",
                   "ohne Projektordner zaehlt die echte Fassung vor einer Sicherung");
        Check.That(ProjectNavigation.RootOf(project with { Folder = "", Versions = new[] { backup } }) == @"C:\backup",
                   "ein Projekt mit nur Sicherungen bleibt erreichbar");
        Check.That(ProjectNavigation.RootOf(project with { Folder = "", Versions = Array.Empty<BlendVersion>() }) == "",
                   "ohne Ordner und Fassungen bleibt die Wurzel leer");
    }

    private static void Navigation()
    {
        Check.Group("Projektnavigation - Ebenen, Brotkrumen und Versionsansicht");
        var state = new ProjectNavigation();
        var project = new BlendProject("kitchen", "Kitchen", @"C:\projects\kitchen", Array.Empty<BlendVersion>());
        Check.That(state.Projects.Count == 0 && state.Project is null && state.Folder is null && !state.Back(),
                   "der Browser beginnt in einer leeren Uebersicht");
        Check.That(!state.VersionsOpen, "die Versionsansicht beginnt zugeklappt");
        state.OpenProject(project);
        Check.That(state.Root == project.Folder && state.CurrentFolder == project.Folder,
                   "Oeffnen setzt Wurzel und aktuellen Ordner gemeinsam");
        Check.That(!state.Breadcrumbs().Any(), "die Projektwurzel hat keine Unterordner-Brotkrumen");
        state.OpenFolder(@"C:\projects\kitchen\renders\camera");
        var crumbs = state.Breadcrumbs().ToArray();
        Check.That(crumbs.SequenceEqual(new[] { ("renders", @"C:\projects\kitchen\renders"),
                                               ("camera", @"C:\projects\kitchen\renders\camera") }),
                   "Brotkrumen tragen Namen und kumulierte Zielpfade in Reihenfolge");
        state.OpenFolder(crumbs[0].Path);
        Check.That(state.CurrentFolder == @"C:\projects\kitchen\renders", "ein Brotkrumenziel kann direkt geoeffnet werden");
        Check.That(state.Back() && state.CurrentFolder == project.Folder, "Zurueck erreicht die Wurzel");
        Check.That(state.Back() && state.Project is null && state.Folder is null, "erst danach wird die Uebersicht erreicht");
        Check.That(!state.Back(), "weiteres Zurueck ist in der Uebersicht wirkungslos");

        state.OpenProject(project);
        state.OpenFolder(project.Folder.ToUpperInvariant());
        Check.That(state.Back() && state.Project is null, "Grossschreibung allein erzeugt keine zusaetzliche Ordnerebene");
        state.OpenProject(project);
        state.OpenFolder(@"D:\elsewhere");
        Check.That(!state.Breadcrumbs().Any(), "fuer einen Pfad ausserhalb der Wurzel werden keine Brotkrumen erfunden");
        Check.That(state.Back() && state.CurrentFolder == project.Folder, "ein zu kurzer Elternpfad faellt auf die Wurzel zurueck");

        state.ToggleVersions();
        state.ShowOverview();
        state.OpenProject(project with { Key = "other", Name = "Other" });
        Check.That(state.VersionsOpen, "die aufgeklappte Versionsansicht bleibt wie bisher beim Projektwechsel erhalten");
        state.ToggleVersions();
        Check.That(!state.VersionsOpen, "ein zweiter Klick klappt die Fassungen wieder zu");
    }

    private static void Refresh()
    {
        Check.Group("Projektnavigation - Auswahl nach einem Scan");
        var state = new ProjectNavigation();
        var old = new BlendProject("kitchen", "Kitchen", @"C:\projects\kitchen", Array.Empty<BlendVersion>());
        var updated = old with { Name = "Updated" };
        state.OpenProject(old);
        state.OpenFolder(@"C:\projects\kitchen\renders");
        state.ToggleVersions();
        state.Apply(new[] { updated });
        Check.That(ReferenceEquals(state.Project, updated) && state.Folder == @"C:\projects\kitchen\renders",
                   "gleicher Projektschluessel ersetzt die Metadaten und behaelt den Ordner");
        Check.That(state.Projects.Count == 1 && state.VersionsOpen, "Liste und aufgeklappte Fassungen bleiben verfuegbar");
        state.Apply(new[] { updated with { Key = "KITCHEN" } });
        Check.That(state.Project is null && state.Folder is null,
                   "Projektschluessel bleiben wie bisher exakt und unterscheiden Grossschreibung");
        state.Apply(new[] { old });
        Check.That(state.Project is null, "ein Scan oeffnet in der Uebersicht kein Projekt von selbst");
        state.OpenProject(old);
        state.Apply(Array.Empty<BlendProject>());
        Check.That(state.Projects.Count == 0 && state.Project is null && state.Folder is null,
                   "ein entferntes Projekt hinterlaesst weder Auswahl noch Ordner");
    }
}
