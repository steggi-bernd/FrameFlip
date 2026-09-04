using System.Text;

namespace FrameFlip.Sequencing;

public static class SequenceMath
{
    /// <summary>
    /// Fasst zusammenhaengende Nummern zu Bereichen zusammen. Trennzeichen und
    /// Bereichszeichen sind waehlbar, weil dieselbe Liste zweimal gebraucht wird:
    /// fuer Blenders Befehlszeile (42,88..91,130) und fuer die Anzeige (42, 88–91, 130).
    /// </summary>
    public static string FormatRanges(IReadOnlyList<int> numbers, string separator, string rangeMark)
    {
        if (numbers.Count == 0) return string.Empty;

        var text = new StringBuilder();

        for (int i = 0; i < numbers.Count;)
        {
            int start = numbers[i];
            int j = i;
            while (j + 1 < numbers.Count && numbers[j + 1] == numbers[j] + 1) j++;

            if (text.Length > 0) text.Append(separator);
            text.Append(start);
            if (numbers[j] != start) text.Append(rangeMark).Append(numbers[j]);

            i = j + 1;
        }

        return text.ToString();
    }

    /// <summary>
    /// Framebereich fuer Blenders Schalter -f. Beispiel: 42,88..91,130
    /// Der Aufruf lautet dann: blender -b datei.blend -o ... -f 42,88..91,130
    /// </summary>
    public static string FormatForBlender(IReadOnlyList<int> missing)
        => FormatRanges(missing, ",", "..");

    /// <summary>Dieselbe Liste fuer die Oberflaeche, mit Halbgeviertstrich.</summary>
    public static string FormatForDisplay(IReadOnlyList<int> missing)
        => FormatRanges(missing, ", ", "–");

    /// <summary>Zahl der zusammenhaengenden Luecken - nicht der fehlenden Frames.</summary>
    public static int CountRanges(IReadOnlyList<int> numbers)
    {
        int ranges = 0;
        for (int i = 0; i < numbers.Count; ranges++)
        {
            int j = i;
            while (j + 1 < numbers.Count && numbers[j + 1] == numbers[j] + 1) j++;
            i = j + 1;
        }
        return ranges;
    }

    /// <summary>
    /// Index-Arithmetik ueber die Sequenz. Mit Loop wird gewrappt - dadurch ist der
    /// Sprung vom letzten auf den ersten Frame kein Sonderfall, weder in der Wiedergabe
    /// noch beim Vorausladen. Ohne Loop liefert ein Ueberlauf -1.
    /// </summary>
    public static int Offset(int position, int delta, int count, bool loop)
        => count <= 0 ? -1 : OffsetInRange(position, delta, 0, count - 1, loop);

    /// <summary>
    /// Wie <see cref="Offset"/>, aber auf einen In/Out-Bereich beschraenkt. Ohne
    /// gesetzte Punkte ist der Bereich die ganze Sequenz, dadurch braucht die
    /// Wiedergabe keine Fallunterscheidung.
    /// </summary>
    public static int OffsetInRange(int position, int delta, int first, int last, bool loop)
    {
        if (last < first) return -1;

        int span = last - first + 1;
        long target = (long)position + delta;

        if (loop)
        {
            long m = (target - first) % span;
            if (m < 0) m += span;
            return (int)(first + m);
        }

        if (target < first || target > last) return -1;
        return (int)target;
    }

    /// <summary>Wandelt die fortlaufende Zeitachse der Wiedergabe in einen Sequenzindex.</summary>
    public static int Resolve(long rawFrame, int count, bool loop, out bool pastEnd)
    {
        if (count <= 0) { pastEnd = false; return -1; }
        return ResolveInRange(rawFrame, 0, count - 1, loop, out pastEnd);
    }

    /// <summary>
    /// Wie <see cref="Resolve"/>, auf einen In/Out-Bereich beschraenkt. Die Zeitachse
    /// laeuft weiter und wird erst hier auf den Bereich abgebildet - dadurch bleibt
    /// die Wiedergabe zeitbasiert, auch wenn zwischendurch In- oder Out-Punkt
    /// verschoben werden.
    /// </summary>
    public static int ResolveInRange(long rawFrame, int first, int last, bool loop, out bool pastEnd)
    {
        pastEnd = false;
        if (last < first) return -1;

        int span = last - first + 1;

        if (loop)
        {
            long m = (rawFrame - first) % span;
            if (m < 0) m += span;
            return (int)(first + m);
        }

        if (rawFrame > last) { pastEnd = true; return last; }
        if (rawFrame < first) return first;
        return (int)rawFrame;
    }
}
