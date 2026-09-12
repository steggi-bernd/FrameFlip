using System.Globalization;
using System.IO;
using System.Text.Json;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;

namespace FrameFlip.Web;

/// <summary>
/// Der Zustand, den die Seite anzeigt - als JSON.
///
/// Was hier NICHT hineingehoert, ist ebenso wichtig wie das, was drinsteht: keine
/// Pfade, keine Dateinamen, keine Ordner. Die Seite soll zeigen, wie weit ein Render
/// ist - nicht, wie der Rechner aufgeraeumt ist. Vom aktuellen Bild geht deshalb nur
/// eine Kennung nach draussen, damit der Browser merkt, wann sich etwas geaendert
/// hat; der Dateiname bleibt hier.
///
/// Ein nicht gemessener Wert wird ausgelassen, nicht auf null oder 0 gesetzt. Die
/// Seite macht daraus einen Gedankenstrich - dieselbe Regel wie im Dashboard: Eine
/// Null ist eine Behauptung, ein Gedankenstrich ist eine ehrliche Auskunft.
/// </summary>
public static class WatchState
{
    public static string Describe(RenderJob? job, LoadSnapshot? load, string? newestFrame)
    {
        var state = new Dictionary<string, object?>
        {
            ["rendering"] = job?.IsRunning == true,
        };

        if (job is not null)
        {
            state["percent"] = Math.Round(job.Progress * 100, 1);
            state["frame"] = job.CurrentFrame;
            state["first"] = job.FirstFrame;
            state["last"] = job.LastFrame;
            state["written"] = job.FramesWritten;
            state["elapsed"] = (int)job.Elapsed.TotalSeconds;

            if (job.Remaining is { } remaining) state["remaining"] = (int)remaining.TotalSeconds;
            if (job.SecondsPerFrame is { } spf) state["secondsPerFrame"] = Math.Round(spf, 2);
            if (job.Width > 0) state["width"] = job.Width;
            if (job.Height > 0) state["height"] = job.Height;

            // Der Dateiname der .blend bliebe ein Hinweis auf die Ordnerstruktur -
            // ohne Endung und ohne Pfad ist es nur noch der Name der Arbeit.
            if (job.BlendFile is { Length: > 0 } blend)
                state["scene"] = Path.GetFileNameWithoutExtension(blend);

            var stats = job.Stats;

            if (stats.Sample is { } sample) state["sample"] = sample;
            if (stats.SampleTotal is { } total) state["sampleTotal"] = total;
            if (stats.MemoryMb is { } memory) state["memoryMb"] = memory;
            if (stats.Activity is { Length: > 0 } activity) state["activity"] = activity;
        }

        if (load is not null)
        {
            state["cpu"] = Math.Round(load.CpuPercent, 1);

            if (load.GpuPercent is { } gpu) state["gpu"] = Math.Round(gpu, 1);
            if (load.AvailableMb > 0) state["freeMb"] = load.AvailableMb;
            if (load.TotalMb > 0) state["totalMb"] = load.TotalMb;
        }

        // Nur eine Kennung, kein Pfad: Aendert sie sich, holt die Seite das Bild neu.
        state["frameId"] = FrameIdOf(newestFrame);

        return JsonSerializer.Serialize(state);
    }

    /// <summary>
    /// Eine Kennung fuer den aktuellen Frame, aus Name und Schreibzeit.
    ///
    /// Sie muss sich aendern, wenn sich das Bild aendert, und sonst nicht - mehr
    /// braucht der Browser nicht zu wissen. Der Name wird dabei nicht mitgeschickt,
    /// nur seine Quersumme.
    /// </summary>
    public static string FrameIdOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists) return string.Empty;

            long mark = info.LastWriteTimeUtc.Ticks ^ info.Length;

            return (Path.GetFileName(path).GetHashCode() ^ mark.GetHashCode())
                   .ToString("x8", CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
