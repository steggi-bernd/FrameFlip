using System.Globalization;

namespace FrameFlip.Bridge;

/// <summary>Was sich aus dem Statustext herauslesen liess. Alles einzeln optional.</summary>
public readonly record struct RenderStats(
    int? Sample,
    int? SampleTotal,
    long? MemoryMb,
    long? PeakMemoryMb,
    TimeSpan? FrameRemaining,
    string? Activity)
{
    public bool IsEmpty => Sample is null && MemoryMb is null && FrameRemaining is null && Activity is null;

    public double? SampleProgress
        => Sample is int s && SampleTotal is int t && t > 0 ? Math.Clamp(s / (double)t, 0, 1) : null;
}

/// <summary>
/// Liest den Fortschrittstext aus, den Blender an den render_stats-Handler gibt.
///
/// Der Text ist KEINE Schnittstelle. Cycles baut ihn in session.cpp zusammen, EEVEE
/// anders, und zwischen Versionen aendert er sich. Deshalb gilt hier durchgehend:
/// Was sich nicht lesen laesst, fehlt eben. Nichts wirft, nichts blockiert, und keine
/// Funktion haengt davon ab - die verlaesslichen Zahlen kommen aus den Ereignissen
/// selbst (welcher Frame geschrieben wurde), nicht aus diesem String.
///
/// Zu erwarten ist unter anderem:
///
///     Remaining: 00:12.34 | Mem: 1234M | Scene, ViewLayer | Sample 42/128
///     Rendering 12 / 64 samples
///     Loading render kernels (may take a few minutes the first time)
///
/// Restzeit und Speicher liefert Cycles nur im Hintergrundmodus - in der Oberflaeche
/// bleibt der Text arm. Das ist kein Fehler hier, sondern steht so im Quelltext.
/// </summary>
public static class StatsParser
{
    public static RenderStats Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;

        return new RenderStats(
            ReadSample(text, out int? total), total,
            ReadMegabytes(text, "Mem:"),
            ReadMegabytes(text, "Peak:"),
            ReadRemaining(text),
            ReadActivity(text));
    }

    // ---------------------------------------------------------------- Samples

    /// <summary>
    /// Erkennt beide Schreibweisen: "Sample 42/128" von Cycles und
    /// "Rendering 12 / 64 samples" von EEVEE.
    /// </summary>
    private static int? ReadSample(string text, out int? total)
    {
        total = null;

        int at = text.IndexOf("Sample", StringComparison.OrdinalIgnoreCase);
        if (at >= 0 && TryReadPair(text, at + "Sample".Length, out int done, out int all))
        {
            total = all;
            return done;
        }

        // EEVEE stellt die Zahlen VOR das Wort: "Rendering 12 / 64 samples".
        at = text.IndexOf("samples", StringComparison.OrdinalIgnoreCase);
        if (at > 0 && TryReadPairBefore(text, at, out done, out all))
        {
            total = all;
            return done;
        }

        return null;
    }

    /// <summary>Liest "42/128" oder "42 / 128" ab einer Position.</summary>
    private static bool TryReadPair(string text, int from, out int first, out int second)
    {
        first = second = 0;

        int i = from;
        while (i < text.Length && !char.IsDigit(text[i]))
        {
            // Nur Trennzeichen ueberspringen - hinter einem Buchstaben steht keine
            // zu diesem Wort gehoerende Zahl mehr.
            if (char.IsLetter(text[i])) return false;
            i++;
        }

        if (!TryReadInt(text, ref i, out first)) return false;

        while (i < text.Length && (text[i] == ' ' || text[i] == '/')) i++;

        return TryReadInt(text, ref i, out second);
    }

    /// <summary>Liest "12 / 64" rueckwaerts vor einer Position.</summary>
    private static bool TryReadPairBefore(string text, int before, out int first, out int second)
    {
        first = second = 0;

        int end = before - 1;
        while (end >= 0 && text[end] == ' ') end--;
        if (end < 0 || !char.IsDigit(text[end])) return false;

        int start = end;
        while (start > 0 && char.IsDigit(text[start - 1])) start--;
        if (!int.TryParse(text.AsSpan(start, end - start + 1), out second)) return false;

        int i = start - 1;
        while (i >= 0 && (text[i] == ' ' || text[i] == '/')) i--;
        if (i < 0 || !char.IsDigit(text[i])) return false;

        end = i;
        while (i > 0 && char.IsDigit(text[i - 1])) i--;

        return int.TryParse(text.AsSpan(i, end - i + 1), out first);
    }

    private static bool TryReadInt(string text, ref int i, out int value)
    {
        int start = i;
        while (i < text.Length && char.IsDigit(text[i])) i++;

        value = 0;
        return i > start && int.TryParse(text.AsSpan(start, i - start), out value);
    }

    // ---------------------------------------------------------------- Speicher

    /// <summary>Liest "Mem: 1234M" oder "Peak: 2.5G".</summary>
    private static long? ReadMegabytes(string text, string label)
    {
        int at = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        int i = at + label.Length;
        while (i < text.Length && text[i] == ' ') i++;

        int start = i;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',')) i++;
        if (i == start) return null;

        var number = text.AsSpan(start, i - start);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return null;

        char unit = i < text.Length ? char.ToUpperInvariant(text[i]) : 'M';

        return unit switch
        {
            'G' => (long)Math.Round(value * 1024),
            'K' => (long)Math.Round(value / 1024),
            _ => (long)Math.Round(value),
        };
    }

    // ---------------------------------------------------------------- Restzeit

    /// <summary>
    /// Liest "Remaining: 01:23.45". Blender schreibt Zeiten als [hh:]mm:ss[.ff],
    /// was TimeSpan.Parse in dieser Form nicht annimmt - deshalb von Hand.
    /// </summary>
    private static TimeSpan? ReadRemaining(string text)
    {
        int at = text.IndexOf("Remaining:", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        int i = at + "Remaining:".Length;
        while (i < text.Length && text[i] == ' ') i++;

        int start = i;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == ':' || text[i] == '.')) i++;
        if (i == start) return null;

        var parts = text[start..i].Split(':');
        if (parts.Length is < 2 or > 3) return null;

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return null;

            total = total * 60 + value;
        }

        return total >= 0 && total < 60 * 60 * 24 * 30 ? TimeSpan.FromSeconds(total) : null;
    }

    // ---------------------------------------------------------------- Taetigkeit

    /// <summary>
    /// Der beschreibende Teil, ohne die Zahlenfelder. Das ist der Text, der dem
    /// Nutzer sagt, WORAN gerade gearbeitet wird - "Loading render kernels" zu sehen
    /// erklaert eine Minute Stillstand, die sonst wie ein Absturz aussieht.
    /// </summary>
    private static string? ReadActivity(string text)
    {
        var parts = text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(2);

        foreach (var part in parts)
        {
            if (part.StartsWith("Remaining:", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.StartsWith("Peak:", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.StartsWith("Sample", StringComparison.OrdinalIgnoreCase)) continue;

            // Nach jedem geschriebenen Frame kommt ueber denselben Handler eine
            // Abschlussmeldung: "Time: 02:01.99 (Saving: 00:00.18)". Sie beschreibt
            // keine laufende Taetigkeit, sondern eine bereits vergangene.
            if (part.StartsWith("Time:", StringComparison.OrdinalIgnoreCase)) continue;

            if (part.Length > 0) kept.Add(part);
        }

        // Zusammenfuegen statt nur den letzten Teil zu nehmen: Cycles schreibt die
        // Vorbereitungsphasen zweiteilig - "Synchronizing object | Fingernails.001",
        // "Updating Images | Loading Normal Texture.007". Nur der zweite Teil waere
        // ein Objektname ohne Zusammenhang, nur der erste liesse offen, woran es
        // gerade haengt. Aufgezeichnet aus einem echten Cycles-Lauf.
        return kept.Count == 0 ? null : string.Join(" · ", kept);
    }
}
