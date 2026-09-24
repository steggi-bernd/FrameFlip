using System.Windows.Media.Imaging;
using FrameFlip.Playback;
using FrameFlip.Sequencing;

namespace FrameFlip.Dashboard;

internal sealed record DashboardPreloadResult(bool Whole, int Loaded, int Total, long Version);

/// <summary>
/// Besitzt Bildwunsch, einzelnen Decoder, Vorlader und Cache einer Dashboard-Folge.
/// Aufrufe und asynchrone Fortsetzungen laufen auf dem UI-Thread; der synchrone
/// Decoder und Fortschrittsmeldungen kehren ueber Dispatch dorthin zurueck.
/// Abspielposition, Bereich, Speicherbudget und sichtbare Rueckmeldungen bleiben im Fenster.
/// </summary>
internal sealed class DashboardFrameController : IDisposable
{
    private readonly DashboardFrameSources _sources;
    private readonly Action<Action> _dispatch;
    private readonly Action<BitmapSource> _show;
    private readonly Action<PreloadProgress> _progress;
    private readonly Func<int> _decodeWidth;
    private readonly Func<PreloadPace> _pace;
    private ImageSequence? _sequence;
    private BitmapSource?[] _cache = Array.Empty<BitmapSource?>();
    private string? _wanted;
    private bool _decoding;
    private bool _disposed;
    private long _generation, _displayGeneration, _preloadVersion;
    private PreloadSession? _preloader;

    internal bool IsPreloading => _preloader is not null;
    internal int CachedFrameCount => _cache.Count(image => image is not null);

    internal DashboardFrameController(DashboardFrameSources sources, Action<Action> dispatch,
        Action<BitmapSource> show, Action<PreloadProgress> progress, Func<int> decodeWidth, Func<PreloadPace> pace)
    {
        _sources = sources;
        _dispatch = dispatch;
        _show = show;
        _progress = progress;
        _decodeWidth = decodeWidth;
        _pace = pace;
    }

    internal void Reset(ImageSequence? sequence)
    {
        if (_disposed) return;
        Clear(sequence);
    }

    private void Clear(ImageSequence? sequence)
    {
        _generation++;
        _wanted = null;
        _sequence = sequence;
        CancelPreload();
        if (_cache.Length == 0) return;
        _cache = Array.Empty<BitmapSource?>();
        // Wie bisher nur beim Verwerfen, nicht nach dem Fuellen aufraeumen.
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
    }

    internal void Request(int index)
    {
        if (_disposed || _sequence is null || index < 0 || index >= _sequence.Count) return;
        if (index < _cache.Length && _cache[index] is { } ready)
        {
            _wanted = null;
            _displayGeneration++;
            _show(ready);
            return;
        }
        _wanted = _sequence.Frames[index].Path;
        Decode();
    }

    private void Decode()
    {
        if (_disposed || _decoding || _wanted is not { Length: > 0 } path) return;
        _wanted = null;
        _decoding = true;
        long generation = _generation, display = _displayGeneration;
        int width = _decodeWidth();
        _ = Task.Run(() =>
        {
            BitmapSource? image = null;
            try { image = _sources.Read(path, width); }
            catch (Exception) { /* Bei gesperrten oder halben Dateien bleibt das vorige Bild. */ }
            try
            {
                _dispatch(() =>
                {
                    _decoding = false;
                    if (!_disposed && generation == _generation && display == _displayGeneration && image is not null)
                        _show(image);
                    // Waehrend des Lesens ersetzt ein neuer Wunsch den vorigen.
                    Decode();
                });
            }
            catch (Exception) { /* Ein beendeter Dispatcher braucht kein Decoderbild mehr. */ }
        });
    }

    internal bool NeedsPreload(int first, int last)
    {
        if (_disposed || _sequence is null || _sequence.Count < 2) return false;
        if (_cache.Length != _sequence.Count) return true;
        for (int i = 0; i < _sequence.Count; i++)
        {
            int number = _sequence.Frames[i].Number;
            if (number >= first && number <= last && _cache[i] is null) return true;
        }
        return false;
    }

    internal async Task<DashboardPreloadResult?> PreloadAsync(int width, long budget, double aspect)
    {
        if (_disposed || _sequence is null || IsPreloading) return null;
        var paths = _sequence.Frames.Select(f => f.Path).ToArray();
        var session = new PreloadSession(++_preloadVersion);
        _preloader = session;
        bool whole = false;
        try
        {
            session.Loader = _sources.CreatePreloader(paths, _pace, report =>
                _dispatch(() =>
                {
                    if (!_disposed && ReferenceEquals(_preloader, session)) _progress(report);
                }));
            try { whole = await session.Loader.RunAsync(width, budget, aspect); }
            catch (Exception) { /* Auch eine teilweise geladene Folge bleibt abspielbar. */ }

            if (_disposed || !ReferenceEquals(_preloader, session)) return null;
            _cache = session.Loader.Frames;
            return new DashboardPreloadResult(whole, session.Loader.Loaded, paths.Length, session.Version);
        }
        catch (Exception)
        {
            return !_disposed && ReferenceEquals(_preloader, session)
                ? new DashboardPreloadResult(false, 0, paths.Length, session.Version) : null;
        }
        finally
        {
            if (ReferenceEquals(_preloader, session)) _preloader = null;
            try { session.Loader?.Dispose(); }
            catch (Exception) { /* Freigeben darf keine neue Sitzung beeintraechtigen. */ }
        }
    }

    internal bool IsCurrent(DashboardPreloadResult result) => !_disposed && result.Version == _preloadVersion;

    internal void CancelPreload()
    {
        _preloadVersion++;
        var previous = _preloader;
        _preloader = null;
        // Dispose erst nach RunAsync: Der Lader kann seinen Token noch brauchen.
        previous?.Loader?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear(null);
    }

    private sealed class PreloadSession(long version)
    {
        internal long Version => version;
        internal IDashboardPreloader? Loader;
    }
}
