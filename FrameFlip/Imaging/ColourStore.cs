using System.Text.Json.Serialization;

namespace FrameFlip.Imaging;

/// <summary>
/// Eine gemerkte Farbe (docs/Atelier-Arbeitsablauf.md, C4b): was die Pipette an einer Stelle
/// gelesen hat - wie angezeigt und als Licht der Quelle -, und woher sie kommt.
///
/// Gemerkt ueber das Bild hinaus, denn dafuer ist sie da: eine Farbe aus einem Bild in einem
/// anderen verwenden, als Toenung oder als Vorlage fuer eine Angleichung.
/// </summary>
public sealed class SavedColour
{
    /// <summary>Das Licht der Quelle an der Stelle, linear.</summary>
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }

    /// <summary>Die Farbe, wie sie angezeigt wurde - fuer das Feld und die Zahl daneben.</summary>
    public byte ShownR { get; set; }
    public byte ShownG { get; set; }
    public byte ShownB { get; set; }

    /// <summary>Aus welcher Datei, und wo darin. Nur zum Wiedererkennen.</summary>
    public string? From { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    [JsonIgnore]
    public string Hex => ColourReadout.Hex(ShownR, ShownG, ShownB);
}

/// <summary>Der Farbspeicher: die zuletzt gelesenen Farben, die neueste vorn.</summary>
public static class ColourStore
{
    /// <summary>So viele passen in die Leiste - aeltere fallen hinten heraus.</summary>
    public const int Capacity = 12;

    /// <summary>
    /// Merkt eine Farbe vorn. Dieselbe Farbe aus derselben Datei ein zweites Mal hintereinander
    /// kommt nicht doppelt hinein - wer zweimal auf dieselbe Stelle klickt, will keine zwei Felder.
    /// </summary>
    public static bool Add(List<SavedColour> store, SavedColour colour)
    {
        if (store.Count > 0 && store[0] is var newest &&
            newest.ShownR == colour.ShownR && newest.ShownG == colour.ShownG && newest.ShownB == colour.ShownB &&
            string.Equals(newest.From, colour.From, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        store.Insert(0, colour);

        while (store.Count > Capacity) store.RemoveAt(store.Count - 1);

        return true;
    }
}
