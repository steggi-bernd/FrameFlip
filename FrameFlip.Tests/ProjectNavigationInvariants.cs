using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Projects;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Bedienungsfolgen der Projektseite, zuerst gegen die Navigation im Code-behind ausgefuehrt.</summary>
public static class ProjectNavigationInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Run()
    {
        Check.Group("Projektseite - Navigation und erneuter Scan");
        // Die Ordner sind absichtlich nicht vorhanden; weder Scan noch zuletzt
        // geoeffnete Sequenzen stammen aus dem Benutzerprofil.
        string root = Path.Combine(Path.GetTempPath(), "frameflip-navigation-" + Guid.NewGuid().ToString("N"));
        var project = new BlendProject("project", "Kitchen", root, Array.Empty<BlendVersion>());
        var page = new ProjectsPage(_ => { }, () => new List<BlendProject> { project }, () => new());
        PumpUntil(() => Read<int>(page, "_generation") > 0);

        Check.That(!page.Back(), "in der Uebersicht gibt es kein Zurueck");
        Open(page, project);
        Check.That(Navigation(page).Project == project && Navigation(page).Folder == root,
                   "die Projektkachel oeffnet den Projektordner");
        Check.That(((TextBlock)page.FindName("Heading")).Text == "KITCHEN", "die Ueberschrift folgt der Auswahl");
        Click((Border)Call(page, "VersionStack", project)!);
        Check.That(Navigation(page).VersionsOpen, "die Versionszeile klappt ihre Fassungen auf");

        string child = Path.Combine(root, "renders");
        string nested = Path.Combine(child, "camera");
        OpenFolder(page, nested);
        Check.That(Navigation(page).Folder == nested, "die Ordnerkachel oeffnet ihren Zielpfad");
        var crumbs = (Panel)page.FindName("Crumbs");
        Check.That(crumbs.Children.Count == 4, "jeder Unterordner bekommt eine Brotkrume");
        Click((Border)crumbs.Children[2]);
        Check.That(Navigation(page).Folder == child, "die Brotkrume fuehrt genau in ihren Ordner");

        OpenFolder(page, nested);
        Check.That(page.Back() && Navigation(page).Folder == child, "Zurueck geht genau eine Ordnerebene hoch");
        Check.That(page.Back() && Navigation(page).Folder == root, "die naechste Ebene ist die Projektwurzel");
        Check.That(page.Back() && Navigation(page).Project is null
                   && Navigation(page).Folder is null, "von der Wurzel fuehrt Zurueck in die Uebersicht");

        Open(page, project);
        OpenFolder(page, nested);
        var refreshed = project with { Name = "Kitchen updated" };
        Call(page, "Apply", new List<BlendProject> { refreshed });
        Check.That(ReferenceEquals(Navigation(page).Project, refreshed)
                   && Navigation(page).Folder == nested, "ein erneuter Scan aktualisiert das Projekt und behaelt den Ordner");
        Check.That(((TextBlock)page.FindName("Heading")).Text == "KITCHEN UPDATED",
                   "neue Metadaten werden nach dem Scan angezeigt");
        Call(page, "Apply", new List<BlendProject>());
        Check.That(Navigation(page).Project is null && Navigation(page).Folder is null,
                   "ein verschwundenes Projekt fuehrt in die Uebersicht");

        Open(page, project);
        OpenFolder(page, nested);
        var preview = new Window();
        Set(page, "_preview", preview);
        bool closed = false;
        preview.Closed += (_, _) => { closed = true; Set(page, "_preview", null); };
        Check.That(page.Back() && closed && Navigation(page).Folder == nested,
                   "Zurueck schliesst zuerst die Vorschau und behaelt dabei den Ordner");
        Check.That(page.Back() && Navigation(page).Folder == child,
                   "erst das naechste Zurueck navigiert im Projekt");
        Click((Border)((Panel)page.FindName("Crumbs")).Children[0]);
        Check.That(Navigation(page).Project is null, "die Projekte-Brotkrume kehrt direkt zur Uebersicht zurueck");
        Open(page, project);
        Check.That(Navigation(page).VersionsOpen, "die Versionsansicht bleibt nach dem Zurueckkehren erhalten");
    }

    private static void Open(ProjectsPage page, BlendProject project)
        => Click((Border)Call(page, "ProjectTile", project)!);

    private static void OpenFolder(ProjectsPage page, string path)
        => Click((Border)Call(page, "FolderTileView", new FolderTile(path, Path.GetFileName(path), 0, 0, null))!);

    private static void Click(Border target)
        => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!ready()) throw new TimeoutException("Die isolierte Projektliste wurde nicht angewendet.");
    }

    private static T Read<T>(ProjectsPage page, string name)
        => (T)typeof(ProjectsPage).GetField(name, Hidden)!.GetValue(page)!;
    private static ProjectNavigation Navigation(ProjectsPage page) => Read<ProjectNavigation>(page, "_navigation");
    private static void Set(ProjectsPage page, string name, object? value)
        => typeof(ProjectsPage).GetField(name, Hidden)!.SetValue(page, value);
    private static object? Call(ProjectsPage page, string name, params object?[] args)
        => typeof(ProjectsPage).GetMethod(name, Hidden)!.Invoke(page, args);
}
