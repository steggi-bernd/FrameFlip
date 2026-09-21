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

    internal DashboardLiveController(DashboardLiveSources sources, Func<string, bool> supported, Action rescan)
    {
        _sources = sources;
        _supported = supported;
        _rescan = rescan;
    }

    internal void WatchFolder(string? folder)
    {
        StopWatching();
        if (string.IsNullOrEmpty(folder) || !_sources.FolderExists(folder)) return;
        try { _watcher = _sources.Observe(folder, OnFolderChanged); }
        catch (Exception)
        {
            // Ohne Dateimeldungen (etwa auf Netzlaufwerken) bleibt die Bruecke.
            StopWatching();
        }
    }

    private void StopWatching()
    {
        var watcher = _watcher;
        _watcher = null;
        try { watcher?.Dispose(); }
        catch (Exception) { /* Abbauen darf den Auswahlwechsel nicht verhindern. */ }
    }

    private void OnFolderChanged(string name)
    {
        if (_supported(Path.GetExtension(name))) _sources.Post(NoteNewFrames);
    }

    internal void NoteNewFrames()
    {
        _settle ??= _sources.CreateTimer(_rescan);
        _settle.Restart();
    }

    public void Dispose()
    {
        _settle?.Dispose();
        StopWatching();
    }
}
