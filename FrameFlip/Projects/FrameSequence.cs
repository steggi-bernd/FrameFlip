using System.IO;

namespace FrameFlip.Projects;

/// <summary>
/// Die Bilder eines Ordners in der Reihenfolge, in der sie ein Film sind.
///
/// Der Projektbrowser sortiert alphabetisch, und das ist dort richtig: In einer
/// Dateiliste sucht man einen Namen. Zum Abspielen taugt es nicht. Blender schreibt
/// mit "####" zwar gepolsterte Namen, bei denen beides zusammenfaellt - aber sobald
/// jemand "#" oder gar nichts angibt, entstehen frame_1 bis frame_360, und
/// alphabetisch kommt frame_10 direkt nach frame_1. Ein Film, der von 1 auf 10 auf
/// 100 springt und die 2 erst am Ende zeigt, ist kein Fehler, den man sucht - man
/// haelt ihn fuer einen kaputten Render.
///
/// Verglichen wird deshalb ziffernweise: Wo beide Namen eine Zahl haben, zaehlt ihr
/// Wert, sonst der Buchstabe. Fuehrende Nullen fallen dabei weg, damit 0001 und 1
/// dieselbe Zahl sind - und weil eine Zahl laenger sein kann als jeder Zahlentyp,
/// wird nach dem Kuerzen erst die Laenge und dann Ziffer fuer Ziffer verglichen,
/// statt zu parsen.
/// </summary>
public static class FrameSequence
{
    /// <summary>
    /// Soviele Frames beschreibt eine Antwort hoechstens.
    ///
    /// Die Namen gehen in einer einzigen Nachricht hinaus, und die des Relays ist
    /// bei einem Mebibyte zu Ende. Zwanzigtausend kurze Namen bleiben weit darunter
    /// und sind bei 24 Bildern je Sekunde ueber vierzehn Minuten Film - laenger, als
    /// irgendjemand auf einem Handy durch einen Renderordner blaettert.
    /// </summary>
    public const int MaxFrames = 20000;

    /// <summary>Die Bilder eines Ordners, abspielfertig geordnet.</summary>
    public static List<string> Of(string directory)
        => ProjectScanner.Images(directory)
                         .OrderBy(path => Path.GetFileName(path) ?? path, Order)
                         .ToList();

    /// <summary>Die Ordnung, nach der ein Frame vor dem anderen kommt.</summary>
    public static readonly IComparer<string> Order = Comparer<string>.Create(Compare);

    public static int Compare(string? left, string? right)
    {
        if (left is null || right is null) return left is null ? (right is null ? 0 : -1) : 1;

        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                int startLeft = i, startRight = j;

                while (i < left.Length && char.IsDigit(left[i])) i++;
                while (j < right.Length && char.IsDigit(right[j])) j++;

                ReadOnlySpan<char> a = Trimmed(left.AsSpan(startLeft, i - startLeft));
                ReadOnlySpan<char> b = Trimmed(right.AsSpan(startRight, j - startRight));

                // Die laengere Zahl ist die groessere - nach dem Kuerzen stimmt das
                // immer, und es kommt ohne einen Zahlentyp aus.
                if (a.Length != b.Length) return a.Length - b.Length;

                int digits = a.SequenceCompareTo(b);

                if (digits != 0) return digits;

                continue;
            }

            int letters = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));

            if (letters != 0) return letters;

            i++;
            j++;
        }

        int rest = (left.Length - i) - (right.Length - j);

        // Gleich heisst hier nur "gleich nach dieser Ordnung": 0001.png und 1.png
        // sind es. Zwei Dateien duerfen aber nicht je nach Laune die Plaetze
        // tauschen, also entscheidet zuletzt der blosse Name.
        return rest != 0 ? rest : string.CompareOrdinal(left, right);
    }

    /// <summary>Eine Ziffernfolge ohne ihre fuehrenden Nullen. "0000" bleibt eine "0".</summary>
    private static ReadOnlySpan<char> Trimmed(ReadOnlySpan<char> digits)
    {
        int start = 0;

        while (start < digits.Length - 1 && digits[start] == '0') start++;

        return digits[start..];
    }
}
