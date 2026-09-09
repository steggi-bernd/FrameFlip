using FrameFlip.Configuration;
using FrameFlip.Remote;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Besitzt die aktuelle Remote-Verbindung. Aufbau und Wechsel kommen vom UI-Thread;
/// das asynchrone Ende einer alten Verbindung darf den Aufrufer nicht aufhalten.
/// Der Host entscheidet nach jeder Aufbauentscheidung neu ueber die Lastmessung.
/// </summary>
internal sealed class AppRemoteController : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly AppRemoteSources _sources;
    private readonly Action _changed;
    private IAppRemoteLink? _current;
    private bool _disposed;

    internal bool HasConnection => _current is not null;
    internal RelayState? State => _current?.State;

    internal AppRemoteController(Func<AppSettings> settings, AppRemoteSources sources, Action changed)
    {
        _settings = settings;
        _sources = sources;
        _changed = changed;
    }

    internal void SettingsChanged(AppSettings previous)
    {
        if (_disposed) return;
        var current = _settings();
        if (current.RemoteEnabled != previous.RemoteEnabled
            || current.RelayHost != previous.RelayHost
            || current.PairingSecret != previous.PairingSecret) Restart();
    }

    internal void Restart()
    {
        if (_disposed) return;
        ReleaseCurrent();

        var settings = _settings();
        if (!settings.RemoteEnabled || !_sources.BridgeAvailable()) { _changed(); return; }
        if (!PairingStore.TryUnprotect(settings.PairingSecret, out var key)) { _changed(); return; }

        try
        {
            var invite = new PairingInvite(key!, settings.RelayHost);
            _current = _sources.Create(invite);
            _current.Start();
        }
        catch (ArgumentException)
        {
            // Eine von Hand bearbeitete Konfiguration kann eine unbrauchbare
            // Adresse enthalten. Auch ein teilweise aufgebautes Ziel freigeben.
            ReleaseCurrent();
        }

        // Auch nach einer unbrauchbaren Adresse gilt die alte Verbindung nicht
        // mehr als Verbraucher. Sonst laeuft ihre Lastmessung unnoetig weiter.
        _changed();
    }

    private void ReleaseCurrent()
    {
        var previous = _current;
        _current = null;
        if (previous is not null) _ = previous.DisposeAsync().AsTask();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Beim Beenden hat der Host seine Lastmessung bereits gestoppt.
        // Hier keinen weiteren Bedarfs-Callback ausloesen.
        ReleaseCurrent();
    }
}
