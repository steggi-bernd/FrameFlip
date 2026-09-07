using System.Text.RegularExpressions;

namespace FrameFlip.Rendering;

/// <summary>Was eine Zeile von Blender hergibt. Alles darin darf fehlen.</summary>
public sealed record TraceUpdate(int? Frame, int? Sample, int? SampleTotal, string? Saved, string? Status);

/// <summary>
/// Blenders Ausgabe mitlesen.
///
/// Ein Render ohne Fenster meldet sich nicht bei FrameFlips Bruecke - dort sitzt das
/// Addon, und das laeuft nur in einer laufenden Blender-Oberflaeche. Was bleibt, ist
/// die Standardausgabe, und die ist gespraechig: Fuer jeden Abschnitt eine Zeile,
/// mehrere je Sekunde.
///
/// Gelesen wird daraus genau das, was verlaesslich ist - die Framenummer, der
/// Samplestand und die gespeicherte Datei. Die Zeitangaben laesst diese Stelle
/// liegen: Blenders "Remaining" gilt fuer den laufenden Frame, und wer daraus die
/// Restzeit eines ganzen Auftrags baut, rechnet falsch. Die rechnet FrameFlip
/// ohnehin selbst aus den gemessenen Frame-Dauern.
///
/// Die Ausgabe ist keine Schnittstelle und aendert sich zwischen Blender-Fassungen.
/// Deshalb ist hier alles freiwillig: Was nicht passt, ergibt null, und der Render
/// laeuft weiter - nur ohne diese eine Zahl.
/// </summary>
public static class RenderTrace
{
    /// <summary>"Fra:12" - die Nummer des Bildes, an dem gerade gerechnet wird.</summary>
    private static readonly Regex FramePattern = new(@"\bFra:(\d+)", RegexOptions.Compiled);

    /// <summary>"Sample 45/128" - der Fortschritt innerhalb des Bildes.</summary>
    private static readonly Regex SamplePattern = new(@"\bSample (\d+)/(\d+)", RegexOptions.Compiled);

    /// <summary>"Saved: 'C:\...\frame_0012.png'" - das eine, worauf es am Ende ankommt.</summary>
    private static readonly Regex SavedPattern = new(@"Saved:\s*'(?<path>[^']+)'", RegexOptions.Compiled);

    /// <summary>
    /// Eine Zeile lesen. null heisst: nichts Brauchbares darin.
    /// </summary>
    public static TraceUpdate? Read(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var saved = SavedPattern.Match(line);

        if (saved.Success)
            return new TraceUpdate(null, null, null, saved.Groups["path"].Value.Trim(), null);

        var frame = FramePattern.Match(line);
        var sample = SamplePattern.Match(line);

        if (!frame.Success && !sample.Success) return null;

        return new TraceUpdate(
            frame.Success && int.TryParse(frame.Groups[1].Value, out int number) ? number : null,
            sample.Success && int.TryParse(sample.Groups[1].Value, out int at) ? at : null,
            sample.Success && int.TryParse(sample.Groups[2].Value, out int total) ? total : null,
            null,
            Status(line));
    }

    /// <summary>
    /// Der Text, der in der App als Taetigkeit steht.
    ///
    /// Blender haengt seine Fortschrittszeilen mit senkrechten Strichen aneinander;
    /// hinten steht, was gerade passiert - "Sample 45/128", "Synchronizing object",
    /// "Denoising". Vorne stehen Speicher und Zeiten, und die interessieren hier
    /// nicht: FrameFlip misst beides selbst und genauer.
    /// </summary>
    private static string? Status(string line)
    {
        int cut = line.LastIndexOf('|');

        if (cut < 0 || cut + 1 >= line.Length) return null;

        string tail = line[(cut + 1)..].Trim();

        return tail.Length == 0 ? null : tail;
    }
}
