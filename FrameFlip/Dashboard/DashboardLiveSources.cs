using System.IO;
using System.Windows.Threading;

namespace FrameFlip.Dashboard;

internal sealed record DashboardLiveSources(
    Func<string, bool> FolderExists,
    Func<string, Action<string>, IDisposable> Observe,
    Action<Action> Post,
    Func<Action, IDashboardLiveTimer> CreateTimer)
{
    internal static DashboardLiveSources Default(Dispatcher dispatcher) => new(
        Directory.Exists, ObserveFolder, action => dispatcher.BeginInvoke(action),
        action => new DashboardLiveTimer(dispatcher, action));

    private static IDisposable ObserveFolder(string folder, Action<string> changed)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            // Renderer legen Dateien oft leer an und fuellen sie erst danach.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };
        watcher.Created += (_, e) => changed(e.Name ?? string.Empty);
        watcher.Changed += (_, e) => changed(e.Name ?? string.Empty);
        watcher.Renamed += (_, e) => changed(e.Name ?? string.Empty);
        try
        {
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }
}

internal interface IDashboardLiveTimer : IDisposable
{
    void Restart();
    void Stop();
}

internal sealed class DashboardLiveTimer : IDashboardLiveTimer
{
    private readonly DispatcherTimer _timer;
    private readonly Action _settled;

    internal DashboardLiveTimer(Dispatcher dispatcher, Action settled)
    {
        _settled = settled;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick += OnTick;
    }

    public void Restart() { _timer.Stop(); _timer.Start(); }
    public void Stop() => _timer.Stop();
    private void OnTick(object? sender, EventArgs e) { _timer.Stop(); _settled(); }
    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }
}
