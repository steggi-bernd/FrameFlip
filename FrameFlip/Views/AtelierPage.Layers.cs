using System.Windows;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Die Ebenen auf der Atelierseite: Passe lesen, zusammensetzen, anzeigen.
///
/// Der Weg ist bewusst zweigeteilt. Das Lesen eines Passes dauert bei 4K spuerbar
/// lange und geschieht nur, wenn eine Ebene ihn wirklich braucht; das Zusammensetzen
/// laeuft danach bei jedem Reglerzug und muss billig bleiben. Beides in einem Schritt
/// zu machen hiesse, beim Ziehen an der Deckkraft die Datei erneut aufzumachen.
/// </summary>
public partial class AtelierPage
{
    private void ShowLayers(bool visible)
    {
        Layers.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LayerSeparator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Behaelt aus einem gespeicherten Stapel, was diese Datei hergibt.
    ///
    /// Ein Rezept ueberdauert die Datei, fuer die es gemacht wurde - das ist der
    /// Zweck. Aber ein Pass, den es hier nicht gibt, ist keine Ebene, sondern eine
    /// Zeile, die nichts tut und nach einer Erklaerung verlangt.
    /// </summary>
    private static LayerStack? Prune(LayerStack? stack, IReadOnlyList<ExrPass> passes)
    {
        if (stack is null || stack.Layers.Count == 0) return null;

        var kept = stack.Layers
            .Where(l => l.Source.Length == 0 || ExrPasses.Find(passes, l.Source) is not null)
            .ToList();

        return kept.Count == 0 ? null : new LayerStack { Layers = kept };
    }

    /// <summary>
    /// Der Stapel hat sich geaendert.
    ///
    /// Bei einem laufenden Reglerzug wird nur neu zusammengesetzt; die Passe stehen
    /// dann schon. Erst beim Loslassen - und bei jeder Aenderung am Aufbau - wird
    /// nachgelesen, was noch fehlt.
    ///
    /// Bewusst ohne async: Nach einem await landet die Fortsetzung dort, wo der
    /// Synchronisationskontext sie hinschickt, und der ist nicht ueberall derselbe.
    /// Wer von dort aus die Oberflaeche anfasst, bekommt einen Zugriff aus dem
    /// falschen Faden - eine Ausnahme, die beim Ausprobieren nie auftritt und dann
    /// irgendwann doch. Der Rueckweg ueber den Dispatcher ist eine Zeile mehr und
    /// laesst die Frage gar nicht erst aufkommen; es ist derselbe Weg, den auch
    /// <see cref="Open"/> nimmt.
    /// </summary>
    private void OnLayersChanged(bool interim)
    {
        _settings.Layers = Layers.Stack;

        string? path = _path;

        // Beim Ziehen steht alles schon bereit - hier darf nichts gelesen werden,
        // sonst haette jeder Reglerzug eine Datei im Weg.
        if (interim || path is null || _base is null)
        {
            Refresh(interim, recompose: true);
            return;
        }

        var needed = Layers.Stack.NeededSources();
        var missing = needed.Where(s => s.Length > 0 && !_sources.ContainsKey(s)).ToList();

        if (missing.Count == 0)
        {
            DropStale(needed);
            Refresh(interim: false, recompose: true);
            return;
        }

        BusyBadge.Visibility = Visibility.Visible;

        Task.Run(() =>
        {
            var found = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

            foreach (string source in missing)
            {
                var frame = FloatFrame.FromExrPass(path, source);
                if (frame is not null) found[source] = frame;
            }

            return found;
        })
        .ContinueWith(task =>
        {
            var read = task.IsCompletedSuccessfully
                ? task.Result
                : new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

            Dispatcher.Invoke(() =>
            {
                BusyBadge.Visibility = Visibility.Collapsed;

                // Waehrend gelesen wurde, kann eine andere Datei geoeffnet worden
                // sein. Die Passe gehoeren dann zu einem Bild, das nicht mehr auf
                // dem Schirm steht.
                if (!string.Equals(path, _path, StringComparison.Ordinal)) return;

                foreach (var (name, frame) in read) _sources[name] = frame;

                DropStale(Layers.Stack.NeededSources());
                Refresh(interim: false, recompose: true);
            });
        });
    }

    /// <summary>
    /// Vergisst die Passe, die niemand mehr will.
    ///
    /// Nicht Ordnungsliebe: Ein 4K-Pass sind rund hundert Megabyte, und eine Datei
    /// aus Blender fuehrt zwanzig davon. Wer sie alle liegenliesse, haette nach ein
    /// paar Versuchen zwei Gigabyte im Speicher.
    /// </summary>
    private void DropStale(IReadOnlyList<string> needed)
    {
        foreach (string stale in _sources.Keys.Where(k => k.Length > 0 && !needed.Contains(k)).ToList())
            _sources.Remove(stale);
    }

    /// <summary>
    /// Setzt das Bild aus den Ebenen zusammen - beim Reglerzug nur auf dem Gitter,
    /// das die Anzeige danach liest.
    /// </summary>
    private void Recompose()
    {
        if (_base is null)
        {
            _frame = null;
            return;
        }

        // Faellt die Zusammensetzung aus - etwa, weil keine Ebene einen lesbaren
        // Pass hat -, steht wieder das Bild der Datei. Ein schwarzes Feld waere die
        // formal richtige Antwort und die unbrauchbare.
        //
        // Hineingeschrieben wird in _composed und niemals in einen gelesenen Pass:
        // Der Composer liest aus den Passen, waehrend er schreibt. Deshalb fuehrt
        // die Seite einen eigenen Frame mit, den sonst niemand anfasst.
        _composed = LayerComposer.Compose(Layers.Stack, _sources, _composed,
                                          _coarse ? CoarseStep : 1);
        _frame = _composed ?? _base;

        // Hat der Composer eine Quelle durchgereicht, statt zu rechnen, gehoert sie
        // ihm nicht - beim naechsten Mal darf nicht hineingeschrieben werden.
        if (_composed is not null && Owned(_composed) == false) _composed = null;
    }

    /// <summary>Ob dieser Frame der Seite gehoert oder einer der gelesenen Passe ist.</summary>
    private bool Owned(FloatFrame frame)
    {
        if (ReferenceEquals(frame, _base)) return false;

        foreach (var source in _sources.Values)
            if (ReferenceEquals(frame, source)) return false;

        return true;
    }
}
