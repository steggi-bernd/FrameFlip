using System.Diagnostics;
using FrameFlip.Export;
using FrameFlip.Playback;

namespace FrameFlip.Dashboard;

/// <summary>
/// Besitzt die optionale Videovorbereitung. Anfrage und Bereich werden vor dem
/// ersten Hintergrundzugriff festgehalten. Aufrufe und Fortsetzungen laufen auf
/// dem UI-Thread; abgebrochene Auftraege koennen ihre Nachfolger nicht veraendern.
/// </summary>
internal sealed class DashboardVideoController(DashboardVideoSources sources,
    Func<ProcessPriorityClass> priority) : IDisposable
{
    // Ein abgebrochener Export loescht seine Teildatei erst nach dem Prozessende.
    // Auch ein Nachfolger mit demselben Fingerabdruck muss darauf warten.
    // Kein WaitHandle und keine fruehe Freigabe: alte Auftraege koennen Dispose ueberleben.
    private readonly SemaphoreSlim _files = new(1, 1);
    private CancellationTokenSource? _active;
    private bool _disposed;
    private long _version;
    internal bool IsPreparing => _active is not null;
    internal string? PreparedPath { get; private set; }

    internal async Task<DashboardVideoResult?> PrepareAsync(DashboardVideoRequest request)
    {
        if (_disposed || IsPreparing || request.Frames.Count < 2) return null;
        request = request with { Frames = request.Frames.ToArray() };
        long version = ++_version;
        var stop = new CancellationTokenSource();
        _active = stop;
        bool ownsFiles = false;
        bool Current() => !_disposed && ReferenceEquals(_active, stop) && !stop.IsCancellationRequested;
        try
        {
            var print = await Task.Run(() => new VideoFingerprint(
                request.Pattern, request.First, request.Last, request.Frames.Count, request.Fps,
                request.Width, request.Height, 0, request.Preset.Name, nameof(GapHandling.HoldLast),
                sources.NewestTicks(request.Frames.Select(f => f.Path))), stop.Token);
            if (!Current()) return null;
            await _files.WaitAsync(stop.Token);
            ownsFiles = true;
            if (!Current()) return null;
            if (sources.Find(print, request.Preset.Extension) is { Length: > 0 } existing)
            {
                PreparedPath = existing;
                return new DashboardVideoResult(existing, true, null, version);
            }
            sources.PrepareFolder();
            string target = sources.PathFor(print, request.Preset.Extension);
            var export = new ExportRequest
            {
                Frames = request.Frames, Preset = request.Preset, OutputPath = target,
                Fps = request.Fps, Gaps = GapHandling.HoldLast, TargetWidth = 0,
                SourceWidth = Math.Max(1, request.Width), SourceHeight = Math.Max(1, request.Height),
                Threads = request.Threads,
            };
            var result = await sources.Export(request.Executable, export, priority(), stop.Token);
            if (!Current()) return null;
            if (result.Success && result.OutputPath is { Length: > 0 } made)
            {
                sources.Note(print, made);
                PreparedPath = made;
                return new DashboardVideoResult(made, false, null, version);
            }
            return result.Cancelled ? null : new DashboardVideoResult(null, false, result.Error ?? "?", version);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { return Current() ? new DashboardVideoResult(null, false, error.Message, version) : null; }
        finally
        {
            if (ReferenceEquals(_active, stop)) _active = null;
            stop.Dispose();
            if (ownsFiles) _files.Release();
        }
    }

    internal void Cancel()
    {
        _version++;
        var previous = _active;
        _active = null;
        previous?.Cancel();
    }

    internal bool IsCurrent(DashboardVideoResult result) => !_disposed && result.Version == _version;

    internal void Reset()
    {
        Cancel();
        PreparedPath = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
