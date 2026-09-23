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
        _layersShown = visible;

        LayersTab.IsEnabled = visible;
        ShowLayerCount();

        ApplyPanelTab();
    }

    /// <summary>
    /// Wieviele Ebenen es gibt - klein neben dem Reiter.
    ///
    /// Damit sieht man, dass im anderen Reiter etwas liegt, ohne hinzuschauen. Eine
    /// einzige Ebene ist der Normalfall und wird nicht eigens gezaehlt.
    /// </summary>
    private void ShowLayerCount()
    {
        int count = _layersShown ? Layers.Stack.Layers.Count : 0;

        LayerCount.Text = count > 1 ? count.ToString() : "";
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

        var kept = Keep(stack.Layers);

        return kept.Count == 0 ? null : new LayerStack { Layers = kept };

        // Bis in die Gruppen hinein: Ein Rezept mit einer Gruppe voller Passe, die
        // es hier nicht gibt, soll die Gruppe leeren und nicht ungeprueft
        // stehenlassen.
        List<ImageLayer> Keep(List<ImageLayer> layers)
        {
            var list = new List<ImageLayer>();

            foreach (var layer in layers)
            {
                // Eine Bildebene nennt eine andere Datei und keinen Pass dieser
                // hier - sie darf nicht daran scheitern, dass es den "Pass" nicht
                // gibt. Eine Einstellungsebene nennt gar nichts.
                if (layer.Content == LayerContent.Pass &&
                    layer.Source.Length > 0 &&
                    ExrPasses.Find(passes, layer.Source) is null)
                {
                    continue;
                }

                if (layer.Content == LayerContent.Group) layer.Children = Keep(layer.Children);

                list.Add(layer);
            }

            return list;
        }
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

        ShowLayerCount();

        string? path = _path;

        // Beim Ziehen steht alles schon bereit - hier darf nichts gelesen werden,
        // sonst haette jeder Reglerzug eine Datei im Weg.
        if (interim || path is null || _base is null)
        {
            Refresh(interim, recompose: true);
            return;
        }

        var reads = Layers.Stack.Reads();
        var missing = reads.Where(r => r.Key.Length > 0 && !_sources.ContainsKey(r.Key)).ToList();

        // Die Werkzeuge koennen Passe verlangen, die keine Ebene liest - die
        // Tiefenschaerfe die Entfernung, die Bewegungsunschaerfe den Vektorpass. Sie
        // werden auf demselben Weg geholt und in demselben Vorrat gehalten; sonst
        // gaebe es zwei Wege zu derselben Datei.
        var data = DataPasses();
        var dataMissing = data.Where(name => !_sources.ContainsKey(name)).ToList();

        if (missing.Count == 0 && dataMissing.Count == 0)
        {
            DropStale(NeededPasses());
            Refresh(interim: false, recompose: true);
            return;
        }

        BusyBadge.Visibility = Visibility.Visible;

        Task.Run(() =>
        {
            var found = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

            foreach (var read in missing)
            {
                var frame = LayeredFrameLoader.Read(read, path);
                if (frame is not null) found[read.Key] = frame;
            }

            foreach (string name in dataMissing)
            {
                var frame = FloatFrame.FromExrPass(path, name);
                if (frame is not null) found[name] = frame;
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

                // Was angefordert war und nicht kam, wird vermerkt - und was diesmal
                // kam, wird vergessen. Ein Lesefehler ist nicht endgueltig.
                bool changed = false;

                foreach (var want in missing)
                {
                    if (want.Key.Length == 0) continue;

                    changed |= read.ContainsKey(want.Key)
                        ? _unreadable.Remove(want.Key)
                        : _unreadable.Add(want.Key);
                }

                Layers.Unreadable = _unreadable;

                DropStale(NeededPasses());

                // Jetzt erst gibt es Miniaturen: Die Zeilen standen schon, als die
                // Dateien noch gelesen wurden, und haben damals nichts bekommen.
                //
                // Und auch dann, wenn NICHTS ankam: Dann hat sich der Vermerk
                // geaendert, und die Zeile muss ihn zeigen. Ein fehlgeschlagener
                // Leseversuch, nach dem die Oberflaeche unveraendert dasteht, ist
                // genau der Fall, den niemand als Fehler erkennt.
                if (read.Count > 0 || changed) Layers.ShowThumbnails();

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
    /// <summary>
    /// Welche Passe die Werkzeuge verlangen.
    ///
    /// Gefragt wird der FERTIGE Stapel und nicht der, den der Streifen gerade zeigt:
    /// Die Werkzeuge mit Renderdaten gelten dem ganzen Bild, wie die oertlichen auch.
    /// </summary>
    private List<string> DataPasses()
    {
        var names = new List<string>();

        foreach (var tool in _finalGrading.Data)
            if (FramePasses.NameFor(tool.Needs, _passes) is { } name && !names.Contains(name))
                names.Add(name);

        return names;
    }

    /// <summary>
    /// Was behalten werden darf: alles, was der Stapel nennt, und die Passe der
    /// Werkzeuge.
    ///
    /// Genannt und nicht gelesen - der Unterschied entscheidet darueber, ob das
    /// Ausblenden einer Ebene ihre Quelle wegwirft. Sie beim naechsten Einblenden
    /// wieder von der Platte zu holen kostet bei 4K eine spuerbare Pause, in der die
    /// Ebene unsichtbar bleibt, obwohl das Auge schon offen ist.
    /// </summary>
    private List<string> NeededPasses()
    {
        var needed = Layers.Stack.NamedSources().ToList();
        needed.AddRange(DataPasses());

        return needed;
    }

    private void DropStale(IReadOnlyList<string> needed)
    {
        foreach (string stale in _sources.Keys.Where(k => k.Length > 0 && !needed.Contains(k)).ToList())
        {
            _sources.Remove(stale);
            _thumbnails.Remove(stale);
        }
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
                                          _coarse ? CoarseStep : 1, _number);
        _frame = _composed ?? _base;

        // Was obenauf liegt, wird nach der Bildwerdung aufgetragen - es steht
        // deshalb nicht im zusammengesetzten Bild, sondern daneben.
        _overlays = _frame is null
            ? Overlays.None
            : Overlays.Prepare(Layers.Stack, _sources, _frame.Width, _frame.Height);

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
