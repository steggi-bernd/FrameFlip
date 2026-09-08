using System.IO;
using FrameFlip.Configuration;
using FrameFlip.Projects;

namespace FrameFlip.Tests;

public static class ProjectScanServiceInvariants
{
    public static void Run()
    {
        Check.Group("Projekt-Scans - Daten, Worker und Abbruch");
        Data();
        Scheduling();
        Failure();
    }

    private static void Data()
    {
        int caller = Environment.CurrentManagedThreadId;
        var threads = new List<int>();
        var paths = new List<string>();
        var checkedFolders = new List<string>();
        string root = Path.Combine(Path.GetTempPath(), "scan-root");
        var version = new BlendVersion(Path.Combine(root, "scene.blend"), "scene.blend", null, null,
                                       false, DateTime.UtcNow, 1);
        var projects = new List<BlendProject>
        {
            new("explicit", "Explicit", "explicit-root", Array.Empty<BlendVersion>()),
            new("fallback", "Fallback", "", new[] { version }),
        };
        var recent = Enumerable.Range(0, 14).Select(i => new RecentSequence { Folder = i == 0 ? "" : $"recent-{i}" }).ToList();
        var folders = new List<FolderTile> { new("child", "Child", 0, 0, null) };
        var frames = new List<string> { "frame_02.png", "frame_10.png" };
        var service = new ProjectScanService(
            () => { threads.Add(Environment.CurrentManagedThreadId); return projects; },
            () => { threads.Add(Environment.CurrentManagedThreadId); return recent; },
            children: path => { threads.Add(Environment.CurrentManagedThreadId); paths.Add(path); return folders; },
            images: path => { threads.Add(Environment.CurrentManagedThreadId); paths.Add(path); return frames; },
            thumbnail: path => { threads.Add(Environment.CurrentManagedThreadId); paths.Add(path); return path + ".png"; },
            exists: path => { threads.Add(Environment.CurrentManagedThreadId); checkedFolders.Add(path); return path == "recent-1"; });

        Check.That(Finish(service.LibraryAsync(default)).SequenceEqual(projects), "Bibliothek behaelt Projekte und Reihenfolge");
        var overview = Finish(service.OverviewAsync(projects, default));
        Check.That(overview.Projects.Select(p => p.Project).SequenceEqual(projects), "Uebersicht behaelt die Projektzuordnung");
        Check.That(paths.SequenceEqual(new[] { "explicit-root", root }), "Thumbnail-Suche nutzt Projektwurzel oder Versionsordner");
        Check.That(overview.Projects[1].Thumbnail == root + ".png", "Thumbnail-Pfad bleibt seinem Projekt zugeordnet");
        Check.That(overview.Recent.Select(r => r.Sequence).SequenceEqual(recent.Take(12)), "Verlauf behaelt die ersten zwoelf Eintraege");
        Check.That(!overview.Recent[0].Exists && overview.Recent[1].Exists && !overview.Recent[2].Exists,
                   "Erreichbarkeit wird je Verlaufseintrag erfasst");
        Check.That(checkedFolders.Count == 11 && !checkedFolders.Contains(""), "leere und abgeschnittene Verlaufspfade werden nicht geprueft");
        var content = Finish(service.FolderAsync(root, default));
        Check.That(content.Folders.SequenceEqual(folders) && content.Frames.SequenceEqual(frames), "Ordner und Frames behalten die Scanner-Reihenfolge");
        Check.That(paths.TakeLast(2).All(path => path == root), "beide Inhaltsabfragen nutzen denselben Ordner");
        Check.That(threads.Count > 0 && threads.All(id => id != caller), "alle Dateiquellen laufen ausserhalb des aufrufenden Threads");
    }

    private static void Scheduling()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var old = new CancellationTokenSource();
        using var skipped = new CancellationTokenSource();
        var reads = new List<string>();
        int imageReads = 0;
        var service = new ProjectScanService(() => new(), () => new(),
            children: path =>
            {
                reads.Add(path);
                if (path == "old") { started.Set(); Wait(release); }
                return new();
            },
            images: _ => { imageReads++; return new(); },
            thumbnail: _ => null);
        var first = service.FolderAsync("old", old.Token);
        try
        {
            Wait(started);
            var middle = service.FolderAsync("skipped", skipped.Token);
            skipped.Cancel();
            Check.Throws<OperationCanceledException>(() => Finish(middle), "wartende Anfrage bricht ohne Dateizugriff ab");
            var projects = new List<BlendProject> { new("kept", "Kept", "root", Array.Empty<BlendVersion>()) };
            var overview = service.OverviewAsync(projects, default);
            projects.Clear();
            Check.That(!overview.IsCompleted && reads.SequenceEqual(new[] { "old" }), "Inhaltsabfragen laufen nacheinander");
            Check.That(Finish(service.LibraryAsync(default)).Count == 0, "Bibliothek bleibt neben blockiertem Inhalt lesbar");
            old.Cancel();
            release.Set();
            Check.Throws<OperationCanceledException>(() => Finish(first), "laufender Scan verwirft sein Ergebnis nach Abbruch");
            Check.That(Finish(overview).Projects.Single().Project.Key == "kept", "wartende Uebersicht besitzt einen Schnappschuss der Auswahl");
            Finish(service.FolderAsync("fresh", default));
            Check.That(reads.SequenceEqual(new[] { "old", "fresh" }), "ueberholte Zwischenanfrage wird nicht nachtraeglich gelesen");
            Check.That(imageReads == 1, "Abbruch nach Ordnerlesen ueberspringt die alte Frame-Abfrage");
        }
        finally { release.Set(); FinishIgnoringCancellation(first); }
    }

    private static void Failure()
    {
        int attempts = 0;
        var service = new ProjectScanService(() => new(), () => new(),
            children: _ => ++attempts == 1 ? throw new IOException("isolated failure") : new(), images: _ => new());
        Check.Throws<IOException>(() => Finish(service.FolderAsync("bad", default)), "Lesefehler bleiben fuer die Seite erkennbar");
        Check.That(Finish(service.FolderAsync("good", default)).Frames.Count == 0 && attempts == 2,
                   "nach einem Fehler wird der naechste Scan wieder freigegeben");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Check.Throws<OperationCanceledException>(() => Finish(service.FolderAsync("never", canceled.Token)), "vorab abgebrochene Anfrage startet nicht");
        Check.That(attempts == 2, "vorab abgebrochene Anfrage liest keine Dateien");
    }

    private static T Finish<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static void Wait(ManualResetEventSlim signal)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scan-Worker hat sein Testsignal nicht erreicht.");
    }
    private static void FinishIgnoringCancellation(Task task)
    {
        try { task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
    }
}
