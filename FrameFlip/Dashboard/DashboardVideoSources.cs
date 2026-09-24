using System.Diagnostics;
using FrameFlip.Export;
using FrameFlip.Playback;
using FrameFlip.Sequencing;

namespace FrameFlip.Dashboard;

internal sealed record DashboardVideoRequest(string Executable, string Pattern, int First, int Last,
    IReadOnlyList<SequenceFrame> Frames, double Fps, int Width, int Height, ExportPreset Preset, int Threads);

internal sealed record DashboardVideoResult(string? Path, bool Reused, string? Error, long Version);

internal sealed record DashboardVideoSources(
    Func<IEnumerable<string>, long> NewestTicks,
    Func<VideoFingerprint, string, string?> Find,
    Action PrepareFolder,
    Func<VideoFingerprint, string, string> PathFor,
    Action<VideoFingerprint, string> Note,
    Func<string, ExportRequest, ProcessPriorityClass, CancellationToken, Task<ExportResult>> Export)
{
    internal static DashboardVideoSources Default { get; } = new(
        PreparedVideo.NewestTicks,
        (print, extension) => PreparedVideo.TryFind(print, extension, out string path) ? path : null,
        PreparedVideo.Prepare, PreparedVideo.PathFor, PreparedVideo.Note,
        (exe, request, priority, token) => new VideoExporter(exe).RunAsync(request, priority, token));
}
