using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Localization;
using FrameFlip.Projects;
using FrameFlip.Views;

namespace FrameFlip.Tests;

public static class ProjectScanConcurrencyInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Run()
    {
        Check.Group("Projektseite - langsame und fehlgeschlagene Scans");
        SupersededContent(failOld: false);
        SupersededContent(failOld: true);
        Library();
        ContentFailure();
    }

    private static void SupersededContent(bool failOld)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        string root = Path.Combine(Path.GetTempPath(), "isolated-scan-" + Guid.NewGuid().ToString("N"));
        string fresh = Path.Combine(root, "fresh");
        var project = new BlendProject("test", "Test", root, Array.Empty<BlendVersion>());
        var service = new ProjectScanService(() => new() { project }, () => new(),
            children: path =>
            {
                if (path == root)
                {
                    started.Set();
                    Wait(release);
                    if (failOld) throw new IOException("old failure");
                }
                return new() { new FolderTile(Path.Combine(path, "child"), "Child", 0, 0, null) };
            }, images: _ => new(), thumbnail: _ => null);
        var page = new ProjectsPage(_ => { }, service);
        Finish(page, "_libraryTask");
        Finish(page, "_contentTask");
        Click((Border)Call(page, "ProjectTile", project, null)!);
        var oldTask = Field<Task>(page, "_contentTask");
        try
        {
            Wait(started);
            Click((Border)Call(page, "FolderTileView", new FolderTile(fresh, "Fresh", 0, 0, null))!);
            bool responsive = false;
            page.Dispatcher.BeginInvoke(new Action(() => responsive = true));
            PumpUntil(() => responsive);
            Check.That(Field<ProjectNavigation>(page, "_navigation").Folder == fresh && !oldTask.IsCompleted,
                       "Ordnerwechsel und UI reagieren waehrend eines blockierten Scans");
            release.Set();
            Finish(page, "_contentTask");
            PumpUntil(() => oldTask.IsCompleted);
            oldTask.GetAwaiter().GetResult();
            var tips = Body(page).Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Border>())
                                 .Select(b => (string)b.ToolTip).ToArray();
            Check.That(tips.SequenceEqual(new[] { Path.Combine(fresh, "child") }),
                       failOld ? "verspaeteter Fehler veraendert die neue Ansicht nicht" : "verspaeteter Inhalt ersetzt die neue Ansicht nicht");
            Check.That(!Body(page).Children.OfType<TextBlock>().Any(), "nach Abschluss bleiben weder alte Lade- noch Fehlerhinweise");
        }
        finally { release.Set(); PumpUntil(() => oldTask.IsCompleted); }
    }

    private static void Library()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        bool fail = false;
        var service = new ProjectScanService(() =>
        {
            int read = Interlocked.Increment(ref reads);
            if (read == 1) { started.Set(); Wait(release); }
            if (fail) throw new IOException("library failure");
            return new() { new BlendProject(read.ToString(), "Project " + read, "", Array.Empty<BlendVersion>()) };
        }, () => new(), thumbnail: _ => null);
        // Kein SynchronizationContext erforderlich: Ergebnisse gehen explizit
        // an den Dispatcher der Seite, auch vor dessen erster Schleife.
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        ProjectsPage page;
        try { page = new ProjectsPage(_ => { }, service); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        var oldTask = Field<Task>(page, "_libraryTask");
        try
        {
            Wait(started);
            Call(page, "Reload");
            var skipped = Field<Task>(page, "_libraryTask");
            Call(page, "Reload");
            release.Set();
            Finish(page, "_libraryTask");
            Finish(page, "_contentTask");
            PumpUntil(() => oldTask.IsCompleted && skipped.IsCompleted);
            Check.That(reads == 2 && Field<ProjectNavigation>(page, "_navigation").Projects.Single().Key == "2",
                       "mehrfaches Einlesen zeigt nur die neueste Bibliothek und ueberspringt die wartende Anfrage");
            Check.That(Field<int>(page, "_generation") == 1, "veraltete Bibliothek wird auch vor der ersten Dispatcher-Schleife nicht angewendet");

            fail = true;
            Call(page, "Reload");
            Finish(page, "_libraryTask");
            Call(page, "Reload");
            Finish(page, "_libraryTask");
            Check.That(Body(page).Children.OfType<TextBlock>().Count(t => t.Text == Strings.T("S_ProjectsReadFailed")) == 1,
                       "wiederholte Bibliotheksfehler zeigen genau einen Hinweis");
            Check.That(Field<ProjectNavigation>(page, "_navigation").Projects.Single().Key == "2",
                       "Bibliotheksfehler behalten die letzte lesbare Auswahl");
            fail = false;
            Call(page, "Reload");
            Finish(page, "_libraryTask");
            Finish(page, "_contentTask");
            Check.That(!Body(page).Children.OfType<TextBlock>().Any() && Field<int>(page, "_generation") == 2,
                       "erfolgreiches erneutes Einlesen entfernt den Fehler und zeichnet die Bibliothek");
        }
        finally { release.Set(); PumpUntil(() => oldTask.IsCompleted); }
    }

    private static void ContentFailure()
    {
        bool fail = true;
        var project = new BlendProject("test", "Test", "", Array.Empty<BlendVersion>());
        var service = new ProjectScanService(() => new() { project }, () => new(),
            children: _ => fail ? throw new IOException("content failure") : new(), images: _ => new(), thumbnail: _ => null);
        var page = new ProjectsPage(_ => { }, service);
        Finish(page, "_libraryTask");
        Click((Border)Call(page, "ProjectTile", project, null)!);
        Finish(page, "_contentTask");
        Check.That(Body(page).Children.OfType<TextBlock>().Single().Text == Strings.T("S_ProjectsReadFailed"),
                   "Lesefehler ersetzt den Ladehinweis");
        fail = false;
        Call(page, "Render");
        Finish(page, "_contentTask");
        Check.That(Body(page).Children.OfType<TextBlock>().Single().Text == Strings.T("S_EmptyFolder"),
                   "Ordnerinhalt laesst sich nach einem Fehler erneut laden");
    }

    private static Panel Body(ProjectsPage page) => (Panel)page.FindName("Body");
    private static void Click(Border target)
        => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    private static T Field<T>(ProjectsPage page, string name) => (T)typeof(ProjectsPage).GetField(name, Hidden)!.GetValue(page)!;
    private static object? Call(ProjectsPage page, string name, params object?[] args)
        => typeof(ProjectsPage).GetMethod(name, Hidden)!.Invoke(page, args);
    private static void Finish(ProjectsPage page, string name)
    {
        var task = Field<Task>(page, name);
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
    private static void Wait(ManualResetEventSlim signal)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scan-Testsignal fehlt.");
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!ready()) throw new TimeoutException("Scan wurde nicht auf die Seite angewendet.");
    }
}
