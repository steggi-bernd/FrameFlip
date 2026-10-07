using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;

namespace FrameFlip.Atelier;

/// <summary>Was das Lesen eines Bildes fuer das Atelier ergibt. Ohne Bild: unlesbar.</summary>
internal sealed record AtelierSource(string Path, FloatFrame? Frame, IReadOnlyList<ExrPass> Passes,
                                     IReadOnlyList<CryptomatteSet> Cryptomattes)
{
    internal static AtelierSource Unreadable(string path)
        => new(path, null, Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());
}

/// <summary>
/// Besitzt das Oeffnen eines Bildes im Atelier: die laufende Anfrage, ihre Nummer, das
/// Lesen im Hintergrund und die Zustellung auf den Oberflaechenfaden.
///
/// Zugestellt wird nur, was noch gilt. Kam waehrend des Lesens ein neueres Oeffnen, oder
/// ist die Sitzung beendet, verfaellt das Ergebnis - auch wenn sein Pfad wieder stimmt,
/// weil jemand von A ueber B zurueck zu A gegangen ist. Andere Wege, die an einem Oeffnen
/// haengen (Passe nachladen, Miniaturen), fragen mit <see cref="IsCurrent"/> nach.
///
/// Anzeigen, Werkzeuge, Rezept und Bildspeicher bleiben bei der Seite
/// (Refactoring-Studio, S1).
/// </summary>
internal sealed class AtelierSourceSession : IDisposable
{
    private readonly Func<string, AtelierSource> _read;
    private readonly Action<Action> _dispatch;
    private long _opened;
    private bool _disposed;

    /// <param name="read">Liest eine Datei - im Hintergrund gerufen. Eine Ausnahme heisst: unlesbar.</param>
    /// <param name="dispatch">Bringt eine Rueckgabe auf den Oberflaechenfaden.</param>
    internal AtelierSourceSession(Func<string, AtelierSource> read, Action<Action> dispatch)
    {
        _read = read;
        _dispatch = dispatch;
    }

    /// <summary>Die Datei der letzten Anfrage - auch waehrend sie noch gelesen wird.</summary>
    internal string? Path { get; private set; }

    /// <summary>Die Nummer der letzten Anfrage.</summary>
    internal long Opened => _opened;

    /// <summary>Ob eine Rueckgabe zu dieser Nummer noch gilt.</summary>
    internal bool IsCurrent(long opened) => !_disposed && opened == _opened;

    /// <summary>
    /// Liest eine Datei im Hintergrund und liefert das Ergebnis auf dem Oberflaechenfaden -
    /// nur, wenn es dann noch gilt. Die Aufgabe endet, wenn zugestellt oder verworfen ist.
    /// </summary>
    internal Task Open(string path, Action<AtelierSource> shown)
    {
        if (_disposed) return Task.CompletedTask;

        long opened = ++_opened;
        Path = path;

        // Lesen und Auspacken dauert bei 4K spuerbar lange; auf dem Oberflaechenfaden
        // staende dabei das ganze Fenster.
        return Task.Run(() => _read(path)).ContinueWith(task =>
        {
            var loaded = task.IsCompletedSuccessfully ? task.Result : AtelierSource.Unreadable(path);

            _dispatch(() =>
            {
                if (!IsCurrent(opened)) return;

                shown(loaded);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Beendet die Sitzung: Was noch gelesen wird, wird nicht mehr zugestellt.</summary>
    public void Dispose()
    {
        _disposed = true;
        _opened++;
    }
}
