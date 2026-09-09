using System.Diagnostics;
using FrameFlip.Configuration;
using FrameFlip.Diagnostics;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Besitzt die gemeinsame Lastmessung fuer Viewer, Hauptfenster und Remote.
/// Ensure/Dispose kommen vom UI-Thread; Render-Ereignisse und Messwerte duerfen
/// aus dem Hintergrund kommen. Alte Sitzungen liefern keine UI-Aenderungen mehr.
/// </summary>
internal sealed class AppLoadController : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly AppLoadSources _sources;
    private readonly object _gate = new();
    private volatile Session? _session;
    private volatile bool _disposed;
    private bool _rendering;

    internal bool IsRunning => _session is not null;
    internal LoadSnapshot? LastSnapshot => _session?.Monitor.LastSnapshot;
    internal int ViewerDecoderThreads => _settings().AdaptiveResources ? _session?.Monitor.MaxDecoderThreads ?? 1 : 1;

    internal AppLoadController(Func<AppSettings> settings, AppLoadSources sources)
    {
        _settings = settings;
        _sources = sources;
    }

    internal void Ensure(bool hasViewer, bool hasMain, bool hasRemote)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!hasViewer && !hasMain && !hasRemote) { Stop(); return; }
            // Laufende Messreihen bleiben auch bei geaenderten Einstellungen bestehen.
            if (_session is not null) return;
            Stop();

            var settings = _settings();
            if (!settings.AdaptiveResources && !hasRemote) return;
            var monitor = _sources.Create(settings.MaxDecoderThreads, TimeSpan.FromSeconds(settings.LoadIntervalSeconds));
            var session = new Session(this, monitor);
            monitor.Updated += session.Updated;
            _session = session;
            try
            {
                monitor.Start();
                // Ohne Render bleibt die schnelle erste Messung des Monitors erhalten.
                if (_rendering) monitor.SetRenderMode(true);
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }

    internal void SetRenderMode(bool rendering)
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Changed meldet auch jeden Render-Fortschritt. Wiederholtes Setzen
            // desselben Modus wuerde die naechste Timer-Messung stets verschieben.
            if (_rendering == rendering) return;
            _rendering = rendering;
            _session?.Monitor.SetRenderMode(rendering);
        }
    }

    private void OnUpdated(Session session, LoadSnapshot snapshot, ResourceProfile profile)
    {
        if (_disposed || !ReferenceEquals(_session, session)) return;
        var viewer = _sources.Viewer();
        if (viewer is null) return;

        viewer.Dispatch(() =>
        {
            if (_disposed || !ReferenceEquals(_session, session)) return;
            var current = _sources.Viewer();
            if (current is null) return;
            _sources.ApplyPriority(profile.ProcessPriority);
            current.ApplyLoad(snapshot, profile);
        });
    }

    // Aufrufer halten _gate. Die Referenz verschwindet vor der Abmeldung,
    // damit schon erfasste Updated-Handler ihre Sitzung nicht mehr antreffen.
    private void Stop()
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.Monitor.Updated -= session.Updated;
            session.Monitor.Dispose();
        }
        _sources.ApplyPriority(ProcessPriorityClass.BelowNormal);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }

    private sealed class Session
    {
        internal readonly IAppLoadMonitor Monitor;
        internal readonly Action<LoadSnapshot, ResourceProfile> Updated;
        internal Session(AppLoadController owner, IAppLoadMonitor monitor)
        {
            Monitor = monitor;
            Updated = (snapshot, profile) => owner.OnUpdated(this, snapshot, profile);
        }
    }
}
