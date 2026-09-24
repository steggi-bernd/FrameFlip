namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Felder fuer die Bilder eines Graphen - wiederverwendet von einer Rechnung zur naechsten.
///
/// Ein Bild auf dem Gitter ist bei 1080p ein Feld von 25 Megabyte. Ein neu angelegtes
/// muss erst genullt werden, und die Speicherbereinigung sammelt es spaeter wieder ein.
/// Gemessen kostet das mehr als das Rechnen hinein: achtzehn frische Felder zu schreiben
/// dauert zweieinhalbmal so lange wie achtzehn gebrauchte. Der Vorrat haelt bereit, was
/// eine Rechnung losgelassen hat.
///
/// Wann ein Feld zurueckkommt, entscheidet der Auswerter: erst, wenn KEIN Ergebnis es
/// mehr haelt. Ergebnisse teilen sich Felder - die Deckung geht durch viele Knoten
/// unveraendert -, und ein Feld, das zu frueh zurueckkaeme, wuerde beschrieben, waehrend
/// ein anderer Knoten es noch liest.
///
/// Ein Vorrat dient einer Rechnung zur Zeit; der Auswerter sperrt ihn so lange.
/// </summary>
public sealed class GridPool
{
    /// <summary>
    /// Wie viele Feldgroessen er haelt: Farbe und Deckung, im vollen und im groben
    /// Durchgang. Mehr kommt nur vor, wenn sich die Leinwand aendert - dann ist das
    /// Alte nichts mehr wert.
    /// </summary>
    private const int Sizes = 4;

    private readonly Dictionary<int, Stack<float[]>> _free = new();
    private readonly HashSet<float[]> _resting = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _recent = new();
    private readonly Dictionary<int, LocalPass.Scratch> _scratches = new();

    /// <summary>Ein Feld dieser Laenge - gebraucht, wenn eines da ist. Was darin steht, ist unbestimmt.</summary>
    internal float[] Take(int length)
    {
        Touch(length);

        if (_free.TryGetValue(length, out var stack) && stack.Count > 0)
        {
            var array = stack.Pop();
            _resting.Remove(array);
            return array;
        }

        return new float[length];
    }

    /// <summary>
    /// Ein Feld zurueck. Eines, das schon da liegt, zaehlt nicht doppelt - sonst kaeme
    /// es zweimal heraus und zwei Knoten schrieben hinein.
    /// </summary>
    internal void Give(float[] array)
    {
        if (!_free.TryGetValue(array.Length, out var stack) || !_resting.Add(array)) return;

        stack.Push(array);
    }

    /// <summary>
    /// Der Puffer der oertlichen Wege fuer diese Gitterpunktzahl. Seine Hilfsfelder
    /// bleiben hier liegen; Werte- und Arbeitsfeld setzt der Kontext bei jedem Knoten
    /// frisch aus dem Vorrat ein, weil die Wege der Geometrie diese beiden tauschen und
    /// eines davon als Ergebnis hinausgeht.
    /// </summary>
    internal LocalPass.Scratch Scratch(int count)
    {
        if (_scratches.TryGetValue(count, out var scratch)) return scratch;

        // Zwei reichen: voller und grober Durchgang.
        if (_scratches.Count >= 2) _scratches.Clear();

        scratch = new LocalPass.Scratch();
        scratch.Hold(count);
        _scratches[count] = scratch;

        return scratch;
    }

    /// <summary>Laesst alles los - wenn eine andere Datei kommt oder die Seite geht.</summary>
    public void Clear()
    {
        _free.Clear();
        _resting.Clear();
        _recent.Clear();
        _scratches.Clear();
    }

    /// <summary>Merkt, dass diese Groesse gebraucht wird - und vergisst die, die am laengsten nicht gebraucht wurde.</summary>
    private void Touch(int length)
    {
        if (_recent.Count > 0 && _recent[^1] == length) return;

        _recent.Remove(length);
        _recent.Add(length);

        if (!_free.ContainsKey(length)) _free[length] = new Stack<float[]>();

        while (_recent.Count > Sizes)
        {
            if (_free.Remove(_recent[0], out var gone))
                foreach (var array in gone) _resting.Remove(array);

            _recent.RemoveAt(0);
        }
    }
}
