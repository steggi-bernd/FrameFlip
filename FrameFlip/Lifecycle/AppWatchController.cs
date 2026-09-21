using FrameFlip.Configuration;
using FrameFlip.Remote;
using FrameFlip.Web;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Besitzt den Zuschauerdienst. Aufrufe kommen vom UI-Thread; ausgetauschte
/// Verbindungen schliessen im Hintergrund und besitzen danach keine UI-Referenz.
/// Schluessel, Speicherung und die oeffentliche Dienstfassade bleiben unveraendert.
/// </summary>
internal sealed class AppWatchController : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly AppWatchSources _sources;
    private readonly Func<WatchKey> _key;
    private readonly Action _invalidRelay;
    private readonly Action _changed;
    private readonly TimeSpan _shutdownTimeout;
    private readonly List<Task> _closing = new();
    private IAppWatchService? _current;
    private bool _disposed;

    internal bool HasService => _current is not null;
    internal WatchService? Service => _current?.Service;

    internal AppWatchController(Func<AppSettings> settings, AppWatchSources sources,
                                Func<WatchKey> key, Action invalidRelay, Action changed,
                                TimeSpan? shutdownTimeout = null)
    {
        _settings = settings;
        _sources = sources;
        _key = key;
        _invalidRelay = invalidRelay;
        _changed = changed;
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2);
    }

    internal void SettingsChanged(AppSettings previous)
    {
        if (_disposed) return;
        var current = _settings();
        if (previous.WatchEnabled != current.WatchEnabled
            || previous.WatchSecret != current.WatchSecret
            || previous.RelayHost != current.RelayHost) Restart();
    }

    internal void Restart()
    {
        if (_disposed) return;
        ReleaseCurrent();

        try
        {
            var settings = _settings();
            // SettingsStore und ApplySettings normalisieren die Zustimmung zuvor.
            if (!settings.WatchEnabled) return;
            if (!PairingInvite.IsUsableHost(settings.RelayHost))
            {
                _invalidRelay();
                return;
            }

            _current = _sources.Create(_key(), settings.RelayHost);
            _current.Start();
        }
        catch
        {
            // Ein fehlgeschlagener Start darf keinen halben Dienst festhalten.
            // Der Fehler bleibt beim Aufrufer; nur der Besitz wird aufgeraeumt.
            ReleaseCurrent();
            throw;
        }
        finally
        {
            // Auch Abschalten oder ein unbrauchbarer Relay aendern den Lastbedarf.
            _changed();
        }
    }

    private void ReleaseCurrent()
    {
        var previous = _current;
        _current = null;
        _closing.RemoveAll(task => task.IsCompleted);
        if (previous is not null) _closing.Add(CloseAsync(previous));
    }

    private static async Task CloseAsync(IAppWatchService service)
    {
        try { await service.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* das Ende einer alten Leitung darf die neue nicht beenden */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseCurrent();

        // Ein gemeinsames Zeitfenster auch fuer noch schliessende Vorgaenger,
        // nicht zwei Sekunden je Verbindung. Kein Last-Callback beim Hostende.
        Task.WhenAll(_closing).Wait(_shutdownTimeout);
        _closing.Clear();
    }
}
