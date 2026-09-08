using FrameFlip.Configuration;

namespace FrameFlip.Projects;

internal sealed record ScannedProject(BlendProject Project, string? Thumbnail);
internal sealed record ScannedRecent(RecentSequence Sequence, bool Exists);
internal sealed record ProjectOverviewScan(IReadOnlyList<ScannedProject> Projects, IReadOnlyList<ScannedRecent> Recent);
internal sealed record ProjectFolderScan(IReadOnlyList<FolderTile> Folders, List<string> Frames);

/// <summary>
/// Dateizugriffe fuer die Projektseite. Ergebnisse enthalten nur Daten; WPF und
/// die Auswahl bleiben bei der Seite. Je ein Bibliotheks- und Inhaltsscan kann
/// gleichzeitig laufen. Schnelle Klickfolgen starten keine unbegrenzten Leser.
/// </summary>
internal sealed class ProjectScanService
{
    private readonly Func<List<BlendProject>> _library;
    private readonly Func<List<RecentSequence>> _recent;
    private readonly Func<string, List<FolderTile>> _children;
    private readonly Func<string, List<string>> _images;
    private readonly Func<string, string?> _thumbnail;
    private readonly Func<string, bool> _exists;
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly SemaphoreSlim _contentGate = new(1, 1);

    internal ProjectScanService()
        : this(ProjectLibrary.Scan, RecentSequences.Load)
    {
    }

    internal ProjectScanService(Func<List<BlendProject>> library, Func<List<RecentSequence>> recent,
        Func<string, List<FolderTile>>? children = null, Func<string, List<string>>? images = null,
        Func<string, string?>? thumbnail = null, Func<string, bool>? exists = null)
    {
        _library = library;
        _recent = recent;
        _children = children ?? ProjectScanner.Children;
        _images = images ?? ProjectScanner.Images;
        _thumbnail = thumbnail ?? (path => ProjectScanner.Thumbnail(path));
        _exists = exists ?? System.IO.Directory.Exists;
    }

    internal Task<List<BlendProject>> LibraryAsync(CancellationToken token)
        => ReadAsync(_libraryGate, _library, token);

    internal Task<ProjectFolderScan> FolderAsync(string folder, CancellationToken token)
        => ReadAsync(_contentGate, () =>
        {
            var children = _children(folder);
            token.ThrowIfCancellationRequested();
            return new ProjectFolderScan(children, _images(folder));
        }, token);

    internal Task<ProjectOverviewScan> OverviewAsync(IReadOnlyList<BlendProject> projects, CancellationToken token)
    {
        // Die Auswahl kann sich aendern, waehrend der Worker auf seinen Slot wartet.
        var snapshot = projects.ToArray();
        return ReadAsync(_contentGate, () =>
        {
            var tiles = new List<ScannedProject>();
            foreach (var project in snapshot)
            {
                token.ThrowIfCancellationRequested();
                tiles.Add(new ScannedProject(project, _thumbnail(ProjectNavigation.RootOf(project))));
            }

            token.ThrowIfCancellationRequested();
            var recent = new List<ScannedRecent>();
            foreach (var entry in _recent().Take(12))
            {
                token.ThrowIfCancellationRequested();
                recent.Add(new ScannedRecent(entry, entry.Folder.Length > 0 && _exists(entry.Folder)));
            }
            return new ProjectOverviewScan(tiles, recent);
        }, token);
    }

    private static async Task<T> ReadAsync<T>(SemaphoreSlim gate, Func<T> read, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(read, token).ConfigureAwait(false);
            // Ein bereits laufender synchroner Dateizugriff laesst sich nicht
            // abbrechen. Sein Ergebnis darf danach trotzdem nicht mehr hinaus.
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }
}
