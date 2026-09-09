using System.IO;
using FrameFlip.Localization;
using FrameFlip.Sequencing;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Entscheidet, was ein Hotkey oder ein expliziter Dateiaufruf mit der Vorschau
/// macht. Der Host erzeugt das Fenster und besitzt dessen Ressourcen weiterhin.
/// Explorer-Zugriffe und Viewer-Aufrufe bleiben auf dem UI-Thread.
/// </summary>
internal sealed class ViewerOpenController
{
    private readonly ViewerOpenSources _sources;
    private readonly Func<IViewerOpenTarget?> _current;
    private readonly Action<ViewerOpenRequest> _create;
    private readonly Action<string> _notify;

    internal ViewerOpenController(ViewerOpenSources sources, Func<IViewerOpenTarget?> current,
                                  Action<ViewerOpenRequest> create, Action<string> notify)
    {
        _sources = sources;
        _current = current;
        _create = create;
        _notify = notify;
    }

    internal void Toggle()
    {
        var target = _sources.Explorer();
        if (target is null || !target.HasAnything)
        {
            if (_current() is { } open) { open.BeginClose(); return; }
            _notify(Strings.T("S_NoExplorer"));
            return;
        }

        string? seed = target.FilePath;
        if (seed is null || !_sources.Decoders.IsSupported(Path.GetExtension(seed)))
        {
            seed = target.FolderPath is not null ? _sources.FirstImage(target.FolderPath) : null;
        }

        if (seed is null)
        {
            if (_current() is { } open) { open.BeginClose(); return; }
            _notify(Strings.T("S_NoImageInFolder"));
            return;
        }

        var sequence = _sources.Scan(seed);
        if (sequence is null || sequence.Count == 0)
        {
            if (_current() is { } open) { open.BeginClose(); return; }
            _notify(Strings.T("S_SequenceUnreadable"));
            return;
        }

        ShowSequence(sequence, seed, target.WindowHandle, allowToggleClose: true);
    }

    internal void OpenFile(string path)
    {
        if (!_sources.Exists(path) || !_sources.Decoders.IsSupported(Path.GetExtension(path)))
        {
            _notify(Strings.T("S_FileUnsupported"));
            return;
        }

        var sequence = _sources.Scan(path);
        if (sequence is null || sequence.Count == 0)
        {
            _notify(Strings.T("S_SequenceUnreadable"));
            return;
        }

        // Eine ausdruecklich uebergebene Datei soll sichtbar werden, auch wenn
        // derselbe Aufruf ueber den Hotkey das Fenster schliessen wuerde.
        ShowSequence(sequence, path, IntPtr.Zero, allowToggleClose: false);
    }

    private void ShowSequence(ImageSequence sequence, string seed, IntPtr explorerWindow, bool allowToggleClose)
    {
        int start = Math.Max(0, sequence.IndexOfPath(seed));
        if (!_sources.Decoders.TryProbeSize(sequence.Frames[start].Path, out int width, out int height))
        {
            if (_current() is null) _notify(Strings.T("S_ImageUnreadable"));
            return;
        }

        // Erst nach erfolgreicher Headerpruefung umschalten: Ein halbfertiger
        // Renderframe darf die bisher brauchbare Vorschau nicht ersetzen.
        if (_current() is { } open)
        {
            if (allowToggleClose && open.ShowsSameSequence(sequence)) open.BeginClose();
            else if (open.ShowsSameSequence(sequence)) open.Activate();
            else open.TryLoadSequence(sequence, start, width, height);
            return;
        }

        _create(new ViewerOpenRequest(sequence, seed, start, explorerWindow, width, height));
    }
}
