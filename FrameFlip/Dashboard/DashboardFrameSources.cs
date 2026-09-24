using System.Windows.Media.Imaging;
using FrameFlip.Playback;

namespace FrameFlip.Dashboard;

internal sealed record DashboardFrameSources(
    Func<string, int, BitmapSource?> Read,
    Func<IReadOnlyList<string>, Func<PreloadPace>, Action<PreloadProgress>, IDashboardPreloader> CreatePreloader)
{
    internal static DashboardFrameSources Default { get; } = new(ReadBitmap,
        (paths, pace, report) => new DashboardPreloader(new SequencePreloader(paths, pace, report)));

    private static BitmapSource ReadBitmap(string path, int width)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = width;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

internal interface IDashboardPreloader : IDisposable
{
    BitmapSource?[] Frames { get; }
    int Loaded { get; }
    Task<bool> RunAsync(int width, long budget, double aspect);
    void Cancel();
}

internal sealed class DashboardPreloader(SequencePreloader loader) : IDashboardPreloader
{
    public BitmapSource?[] Frames => loader.Frames;
    public int Loaded => loader.Loaded;
    public Task<bool> RunAsync(int width, long budget, double aspect) => loader.RunAsync(width, budget, aspect);
    public void Cancel() => loader.Cancel();
    public void Dispose() => loader.Dispose();
}
