namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Haelt Zwischenbilder eines Graphen von einer Rechnung zur naechsten - die, die in den
/// gewaehlten Knoten hineinfliessen.
///
/// Wer an einem Knoten dreht, aendert nichts, was vor ihm liegt. Was dort herauskommt,
/// muss also nicht neu gerechnet werden: Beim Ziehen am Tonwert laeuft nur der Tonwert
/// und was dahinter kommt, und die Ebenen, die Masken und die Werkzeuge davor bleiben,
/// wie sie waren. Das ist der Zwischenspeicher, den der Stapel auf seine Art auch hat -
/// er setzt die Ebenen nicht neu zusammen, wenn nur am Bild gedreht wird.
///
/// Ob ein gemerktes Bild noch gilt, entscheidet allein sein Schluessel: Er fasst alles,
/// was in die Rechnung eingeht - die Einstellungen jedes Knotens davor, wie er
/// gespeichert wuerde, die gelesenen Quellen, die Bildnummer, die Sichtumwandlung. Stimmt
/// irgendwo etwas nicht mehr ueberein, passt der Schluessel nicht, und es wird gerechnet.
/// Was behalten wird, entscheidet nur, wie oft das Merken sich lohnt, nie, ob das Bild
/// stimmt.
///
/// Behalten wird, was von ausserhalb in den gewaehlten Knoten und alles dahinter fliesst -
/// fuer den vollen und den groben Durchgang, damit das Loslassen eines Reglers nach dem
/// Ziehen nicht wieder alles rechnet.
/// </summary>
public sealed class GraphCache
{
    /// <summary>Ein gemerktes Ergebnis - und wofuer es steht, unabhaengig vom Raster.</summary>
    internal sealed record Entry(object? Value, string Shape);

    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Wie viele Ergebnisse gerade gemerkt sind.</summary>
    public int Count => _entries.Count;

    internal IReadOnlyDictionary<string, Entry> Entries => _entries;

    internal bool TryGet(string key, out object? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Was nach dieser Rechnung gemerkt bleibt - alles andere geht.</summary>
    internal void Replace(Dictionary<string, Entry> next) => _entries = next;

    /// <summary>Vergisst alles - wenn eine andere Datei kommt oder der Knotenmodus endet.</summary>
    public void Clear() => _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
}
