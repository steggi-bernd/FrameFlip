using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FrameFlip.Export;

public readonly record struct ExportProgress(
    int Frame, int TotalFrames, double Fps, string Stage, int PassIndex, int PassCount)
{
    public double Fraction
    {
        get
        {
            if (TotalFrames <= 0 || PassCount <= 0) return 0;

            // Der Anteil innerhalb des Durchlaufs, verrechnet mit den bereits
            // abgeschlossenen. Sonst springt die Anzeige beim GIF-Export auf null
            // zurueck, sobald der zweite Durchlauf beginnt.
            double within = Math.Clamp(Frame / (double)TotalFrames, 0, 1);
            return Math.Clamp((PassIndex + within) / PassCount, 0, 1);
        }
    }
}

public sealed record ExportResult(bool Success, bool Cancelled, string? Error, string? OutputPath);

/// <summary>
/// Fuehrt ffmpeg aus, liest den Fortschritt und laesst sich abbrechen.
///
/// Der Aufrufer bleibt waehrenddessen bedienbar: es wird nirgends gewartet, alles
/// laeuft ueber Ereignisse und await.
/// </summary>
public sealed class VideoExporter
{
    private readonly string _executable;

    public VideoExporter(string executable) => _executable = executable;

    /// <summary>Wird aus einem Hintergrundthread ausgeloest - der Aufrufer muss marshallen.</summary>
    public event Action<ExportProgress>? Progress;

    public async Task<ExportResult> RunAsync(ExportRequest request, ProcessPriorityClass priority,
                                             CancellationToken cancellation)
    {
        // Arbeitsdateien in einem eigenen Ordner: die Liste kann bei langen Sequenzen
        // einige Megabyte gross werden und hat neben der Ausgabe nichts zu suchen.
        var work = Path.Combine(Path.GetTempPath(), "FrameFlip", "export_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(work);

            var listPath = Path.Combine(work, "frames.txt");
            var palettePath = Path.Combine(work, "palette.png");

            ConcatListWriter.Write(request.Frames, request.Fps, request.Gaps, listPath);

            var passes = FfmpegArguments.Build(request, listPath, palettePath);

            for (int i = 0; i < passes.Count; i++)
            {
                var result = await RunPassAsync(passes[i], i, passes.Count, request, priority, cancellation);
                if (result.Success) continue;

                // Auch nach einem Encoderfehler ist die angefangene Datei wertlos:
                // ohne Abschlussindex laesst sie sich nicht abspielen und sieht doch
                // aus wie ein Ergebnis.
                DeletePartialOutput(request.OutputPath);
                return result;
            }

            if (!File.Exists(request.OutputPath))
                return new ExportResult(false, false,
                    "ffmpeg meldete Erfolg, aber es wurde keine Datei geschrieben.", null);

            return new ExportResult(true, false, null, request.OutputPath);
        }
        catch (OperationCanceledException)
        {
            DeletePartialOutput(request.OutputPath);
            return new ExportResult(false, true, null, null);
        }
        catch (Exception ex)
        {
            DeletePartialOutput(request.OutputPath);
            return new ExportResult(false, false, ex.Message, null);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception) { }
        }
    }

    private async Task<ExportResult> RunPassAsync(FfmpegArguments.Pass pass, int index, int count,
                                                  ExportRequest request, ProcessPriorityClass priority,
                                                  CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in pass.Arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        if (!process.Start())
            return new ExportResult(false, false, "ffmpeg liess sich nicht starten.", null);

        TrySetPriority(process, priority);

        // stderr mitlesen: bei -loglevel error steht dort die eigentliche Ursache,
        // falls etwas schiefgeht. Ohne das bleibt nur ein nackter Exitcode.
        var errors = Task.Run(() => process.StandardError.ReadToEnd(), CancellationToken.None);
        var progress = Task.Run(() => ReadProgress(process, pass, index, count, request), CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        await progress;
        var errorText = await errors;

        // Der Token kann ausgeloest worden sein, ohne dass WaitForExitAsync noch
        // geworfen hat - etwa wenn der Prozess im selben Moment ohnehin endete. Ohne
        // diese Pruefung meldet der Abbruch einen Fehler statt eines Abbruchs, und die
        // halbe Datei bliebe liegen.
        if (cancellation.IsCancellationRequested)
        {
            KillTree(process);
            throw new OperationCanceledException(cancellation);
        }

        if (process.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(errorText)
                ? $"ffmpeg endete mit Code {process.ExitCode}."
                : errorText.Trim();

            // Nur die letzten Zeilen: ffmpeg wiederholt bei fehlenden Codecs sonst
            // seitenweise dasselbe.
            var lines = reason.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 6) reason = string.Join('\n', lines[^6..]);

            return new ExportResult(false, false, reason, null);
        }

        return new ExportResult(true, false, null, null);
    }

    /// <summary>
    /// Liest das Ausgabeformat von -progress. Es ist ein Schluessel-Wert-Strom, je
    /// Zeile ein Paar, und deutlich stabiler als die Statuszeile auf stderr.
    /// </summary>
    private void ReadProgress(Process process, FfmpegArguments.Pass pass, int index, int count,
                              ExportRequest request)
    {
        if (!pass.ReportsProgress)
        {
            Progress?.Invoke(new ExportProgress(0, request.OutputFrameCount, 0, pass.Label, index, count));
            return;
        }

        int frame = 0;
        double fps = 0;

        try
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line.AsSpan(0, separator);
                var value = line.AsSpan(separator + 1);

                if (key.SequenceEqual("frame"))
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        frame = parsed;
                }
                else if (key.SequenceEqual("fps"))
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                        fps = parsed;
                }
                else if (key.SequenceEqual("progress"))
                {
                    // "continue" nach jedem Block, "end" zum Schluss. Erst hier melden:
                    // vorher sind frame und fps womoeglich aus verschiedenen Blocks.
                    Progress?.Invoke(new ExportProgress(
                        frame, request.OutputFrameCount, fps, pass.Label, index, count));
                }
            }
        }
        catch (Exception)
        {
            // Abgebrochener Prozess schliesst die Leitung mitten im Lesen. Das ist
            // der Normalfall beim Abbrechen und kein Fehler.
        }
    }

    private static void TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        try { process.PriorityClass = priority; }
        catch (Exception) { /* Prozess schon beendet oder Rechte fehlen */ }
    }

    /// <summary>
    /// Beendet ffmpeg samt Kindprozessen und wartet, bis er wirklich weg ist.
    ///
    /// Das Warten ist nicht optional: Kill kehrt sofort zurueck, der Prozess laeuft
    /// aber noch einen Moment weiter. Ohne diese Sperre kehrt der Export zurueck,
    /// waehrend ffmpeg noch in die Ausgabedatei schreibt - das anschliessende Loeschen
    /// der halben Datei schlaegt dann fehl, und ein verwaister Encoder arbeitet
    /// weiter an einem Ergebnis, das niemand mehr erwartet.
    /// </summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (process.HasExited) return;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Prozess in derselben Sekunde von selbst beendet, oder keine Rechte.
        }
    }

    /// <summary>
    /// Halbe Ausgabedatei entfernen. Eine abgebrochene MP4-Datei hat keinen
    /// Abschlussindex und laesst sich nicht abspielen - sie stehen zu lassen wuerde
    /// nur Verwirrung stiften.
    /// </summary>
    private static void DeletePartialOutput(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
    }
}
