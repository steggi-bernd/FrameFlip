using FrameFlip.Remote;

namespace FrameFlip.Lifecycle;

/// <summary>Der Lebenszyklus braucht nur Verfuegbarkeit und Erzeugung, keinen eigenen Bridge-Server.</summary>
internal sealed record AppRemoteSources(Func<bool> BridgeAvailable, Func<PairingInvite, IAppRemoteLink> Create);

internal interface IAppRemoteLink : IAsyncDisposable
{
    RelayState State { get; }
    void Start();
}

/// <summary>Die oeffentliche RemoteLink-Fassade bleibt vom Anwendungs-Lebenszyklus unabhaengig.</summary>
internal sealed class AppRemoteLink(RemoteLink link) : IAppRemoteLink
{
    public RelayState State => link.State;
    public void Start() => link.Start();
    public ValueTask DisposeAsync() => link.DisposeAsync();
}
