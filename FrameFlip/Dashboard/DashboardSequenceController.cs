using System.IO;
using FrameFlip.Projects;
using FrameFlip.Sequencing;

namespace FrameFlip.Dashboard;

/// <summary>
/// Besitzt Liste, Auswahl und gelesene Folge des Dashboards. Der UI-Thread trifft
/// Auswahlentscheidungen; nur die beilaufigen Zeilenzaehlungen laufen im Hintergrund.
/// WPF, Abspielposition, Follow und Bildspeicher liegen weiterhin beim Fenster.
/// </summary>
internal sealed class DashboardSequenceController : IDisposable
{
    private readonly DashboardSequenceSources _sources;
    private readonly Action<Action> _post;
    private readonly Action<DashboardSequenceEntry> _updated;
    private readonly List<DashboardSequenceEntry> _adhoc = new();
    private readonly SemaphoreSlim _countGate = new(1, 1);
    private long _generation;
    private bool _disposed;

    internal IReadOnlyList<DashboardSequenceEntry> Entries { get; private set; } = Array.Empty<DashboardSequenceEntry>();
    internal DashboardSequenceEntry? Current { get; private set; }
    internal ImageSequence? Sequence { get; private set; }
    internal Task CountCompletion { get; private set; } = Task.CompletedTask;

    internal DashboardSequenceController(DashboardSequenceSources sources, Action<Action> post,
        Action<DashboardSequenceEntry> updated)
    {
        _sources = sources;
        _post = post;
        _updated = updated;
    }

    internal void Reload(bool keepSelection = false)
    {
        if (_disposed) return;
        // Auch eine leere oder bereits gezaehlte Liste loest den alten Lauf ab.
        long generation = Interlocked.Increment(ref _generation);
        string? keep = keepSelection
            ? Current?.BlendPath.Length > 0 ? Current.BlendPath : Current?.Folder
            : null;
        var entries = new List<DashboardSequenceEntry>(_adhoc);
        foreach (var known in _sources.Library())
        {
            if (!BlendProjects.IsBlendFile(known.Path)) continue;
            entries.Add(new DashboardSequenceEntry(_sources.FolderExists)
            {
                Name = Path.GetFileName(known.Path), BlendPath = known.Path,
                Folder = known.Output, Seed = known.Seed,
                Width = known.Width, Height = known.Height, SeenUtc = known.SeenUtc,
            });
        }
        Entries = entries.AsReadOnly();
        Current = (keep is { Length: > 0 } ? Find(keep) : null)
                  ?? entries.FirstOrDefault(e => e.HasOutput) ?? entries.FirstOrDefault();
        Sequence = Current is null ? null : Read(Current);
        if (Current is { } current) ApplyScan(current, Sequence);

        var pending = entries.Where(e => e.Frames is null && e.HasOutput)
            .Select(e => (Entry: e, Revision: e.Revision)).ToArray();
        CountCompletion = pending.Length == 0 ? Task.CompletedTask : CountAsync(pending, generation);
    }

    internal DashboardSequenceEntry? Find(string path) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.Folder, path, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Seed, path, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.BlendPath, path, StringComparison.OrdinalIgnoreCase));

    internal bool Select(DashboardSequenceEntry entry)
    {
        if (_disposed || ReferenceEquals(Current, entry) || !Entries.Contains(entry)) return false;
        Current = entry;
        Sequence = Read(entry);
        ApplyScan(entry, Sequence);
        return true;
    }

    /// <summary>Erst einen gueltigen Bildpfad aufnehmen; die UI baut danach die Liste neu.</summary>
    internal bool AddPath(string path)
    {
        if (_disposed || path.Length == 0 || !_sources.FileExists(path)
            || !_sources.Supported(Path.GetExtension(path))) return false;
        string? folder = Path.GetDirectoryName(path);
        if (folder is null) return false;
        var entry = new DashboardSequenceEntry(_sources.FolderExists)
        {
            Name = Path.GetFileName(folder.TrimEnd('\\', '/')), Folder = folder, Seed = path,
            SeenUtc = DateTime.UtcNow, Adhoc = true,
        };
        _adhoc.RemoveAll(a => string.Equals(a.Folder, folder, StringComparison.OrdinalIgnoreCase));
        _adhoc.Insert(0, entry);
        return true;
    }

    internal DashboardSequenceChange? Rescan()
    {
        if (_disposed || Current is null) return null;
        var previous = Sequence;
        var fresh = Read(Current, previous?.Frames[0].Path);
        if (fresh is null) return null;
        // Die bisherige Live-Regel bleibt bestehen: erst neue Anzahl oder neues Ende.
        if (previous is not null && fresh.Count == previous.Count && fresh.EndNumber == previous.EndNumber) return null;
        Sequence = fresh;
        ApplyScan(Current, fresh);
        return new DashboardSequenceChange(previous, fresh);
    }

    private ImageSequence? Read(DashboardSequenceEntry entry, string? preferred = null)
    {
        try
        {
            string seed = preferred ?? entry.Seed;
            if (seed.Length == 0 || !_sources.FileExists(seed))
                seed = entry.HasOutput ? _sources.FirstImage(entry.Folder) ?? string.Empty : string.Empty;
            var scanned = seed.Length > 0 ? _sources.Scan(seed) : null;
            return scanned is { Count: > 0 } ? scanned : null;
        }
        catch (Exception) { return null; /* Eine unlesbare Ausgabe bleibt ohne Folge. */ }
    }

    private static void ApplyScan(DashboardSequenceEntry entry, ImageSequence? sequence)
    {
        entry.Revision++;
        if (sequence is null) return;
        entry.Frames = sequence.Count;
        entry.Missing = sequence.MissingNumbers().Count;
    }

    private async Task CountAsync((DashboardSequenceEntry Entry, long Revision)[] pending, long generation)
    {
        // Ein laufender Dateizugriff ist nicht abbrechbar. Neue Listen warten auf
        // dessen Ende; ueberholte Listen beginnen danach keinen weiteren Scan.
        await _countGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _generation)) return;
            await Task.Run(() =>
            {
                foreach (var (entry, revision) in pending)
                {
                    if (generation != Volatile.Read(ref _generation)) return;
                    var scanned = Read(entry);
                    if (scanned is null) continue;
                    int count = scanned.Count, gaps = scanned.MissingNumbers().Count;
                    try
                    {
                        _post(() =>
                        {
                            if (_disposed || generation != _generation || entry.Revision != revision) return;
                            entry.Frames = count;
                            entry.Missing = gaps;
                            _updated(entry);
                        });
                    }
                    catch (Exception) { /* Ein beendeter Dispatcher braucht keine Zeilenwerte mehr. */ }
                }
            }).ConfigureAwait(false);
        }
        finally { _countGate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generation);
        Current = null;
        Sequence = null;
        Entries = Array.Empty<DashboardSequenceEntry>();
        _adhoc.Clear();
    }
}
