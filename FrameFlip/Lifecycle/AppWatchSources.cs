using FrameFlip.Remote;
using FrameFlip.Web;

namespace FrameFlip.Lifecycle;

/// <summary>Der Lebenszyklus braucht eine Dienstfabrik, keine eigene Netzwerkverbindung.</summary>
internal sealed record AppWatchSources(Func<WatchKey, string, IAppWatchService> Create);

internal interface IAppWatchService : IAsyncDisposable
{
    WatchService Service { get; }
    void Start();
}

/// <summary>Die oeffentliche WatchService-Fassade bleibt fuer die Oberflaeche erhalten.</summary>
internal sealed class AppWatchService(WatchService service) : IAppWatchService
{
    public WatchService Service => service;
    public void Start() => service.Start();
    public ValueTask DisposeAsync() => service.DisposeAsync();
}
