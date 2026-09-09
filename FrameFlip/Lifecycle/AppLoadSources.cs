using System.Diagnostics;
using FrameFlip.Diagnostics;
using FrameFlip.Views;

namespace FrameFlip.Lifecycle;

internal sealed record AppLoadSources(
    Func<int, TimeSpan, IAppLoadMonitor> Create,
    Func<IAppLoadTarget?> Viewer,
    Action<ProcessPriorityClass> ApplyPriority)
{
    internal static AppLoadSources Default(Func<IAppLoadTarget?> viewer) => new(
        (threads, interval) => new AppLoadMonitor(new SystemLoadMonitor(threads, interval)), viewer, SetPriority);

    private static void SetPriority(ProcessPriorityClass priority)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.PriorityClass != priority) process.PriorityClass = priority;
        }
        catch (Exception)
        {
            // Ohne ausreichende Rechte bleibt es bei der aktuellen Stufe.
        }
    }
}

internal interface IAppLoadMonitor : IDisposable
{
    event Action<LoadSnapshot, ResourceProfile>? Updated;
    LoadSnapshot? LastSnapshot { get; }
    int MaxDecoderThreads { get; }
    void Start();
    void SetRenderMode(bool rendering);
}

internal sealed class AppLoadMonitor(SystemLoadMonitor monitor) : IAppLoadMonitor
{
    public event Action<LoadSnapshot, ResourceProfile>? Updated
    {
        add => monitor.Updated += value;
        remove => monitor.Updated -= value;
    }
    public LoadSnapshot? LastSnapshot => monitor.LastSnapshot;
    public int MaxDecoderThreads => monitor.MaxDecoderThreads;
    public void Start() => monitor.Start();
    public void SetRenderMode(bool rendering) => monitor.SetRenderMode(rendering);
    public void Dispose() => monitor.Dispose();
}

internal interface IAppLoadTarget
{
    void Dispatch(Action callback);
    void ApplyLoad(LoadSnapshot snapshot, ResourceProfile profile);
}

internal sealed class AppViewerLoadTarget(ViewerWindow window) : IAppLoadTarget
{
    public void Dispatch(Action callback) => window.Dispatcher.BeginInvoke(callback);
    public void ApplyLoad(LoadSnapshot snapshot, ResourceProfile profile) => window.ApplyLoad(snapshot, profile);
}
