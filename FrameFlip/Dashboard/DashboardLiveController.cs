using System.IO;

namespace FrameFlip.Dashboard;

/// <summary>
/// Besitzt die Ordnerbeobachtung des Dashboards. Dateimeldungen und Bruecke
/// teilen sich eine Ruhefrist; Sequenz, Follow und Darstellung bleiben beim Fenster.
/// Auswahl, Brueckenmeldungen und Dispose kommen vom UI-Thread.
/// </summary>
internal sealed class DashboardLiveController : IDisposable
{
    private readonly DashboardLiveSources _sources;
    private readonly Func<string, bool> _supported;
    private readonly Action _rescan;
    private IDisposable? _watcher;
    private IDashboardLiveTimer? _settle;
    private long _generation;
    private bool _disposed;

    internal DashboardLiveController(DashboardLiveSources sources, Func<string, bool> supported, Action rescan)
    {
        _sources = sources;
        _supported = supported;
        _rescan = rescan;
    }

    internal void WatchFolder(string? folder)
    {
        if (_disposed) return;
        ReleaseSelection();
        if (string.IsNullOrEmpty(folder) || !_sources.FolderExists(folder)) return;
        long generation = _generation;
        try { _watcher = _sources.Observe(folder, name => OnFolderChanged(name, generation)); }
        catch (Exception)
        {
            // Ohne Dateimeldungen (etwa auf Netzlaufwerken) bleibt die Bruecke.
            StopWatching();
        }
    }

    private void ReleaseSelection()
    {
        // Stoppen allein kann bereits eingereihte Datei- oder Timer-Rueckrufe
        // nicht zurueckholen. Sie gehoeren weiterhin ihrer alten Auswahl.
        _generation++;
        _settle?.Dispose();
        _settle = null;
        StopWatching();
    }

    private void StopWatching()
    {
        var watcher = _watcher;
        _watcher = null;
        try { watcher?.Dispose(); }
        catch (Exception) { /* Abbauen darf den Auswahlwechsel nicht verhindern. */ }
    }

    private void OnFolderChanged(string name, long generation)
    {
        if (!_supported(Path.GetExtension(name))) return;
        _sources.Post(() =>
        {
            if (!_disposed && generation == _generation) NoteNewFrames();
        });
    }

    internal void NoteNewFrames()
    {
        if (_disposed) return;
        long generation = _generation;
        _settle ??= _sources.CreateTimer(() =>
        {
            if (!_disposed && generation == _generation) _rescan();
        });
        _settle.Restart();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseSelection();
    }
}
