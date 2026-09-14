using System.Diagnostics;
using System.Globalization;
using System.IO;
using FrameFlip.Export;

namespace FrameFlip.Imaging.Grading;

/// <summary>Was ein Videodurchlauf tun soll.</summary>
public sealed record GradeVideoRequest
{
    public required IReadOnlyList<string> Frames { get; init; }

    public required string OutputPath { get; init; }

    public required ExportPreset Preset { get; init; }

    public double Fps { get; init; } = 24;

    /// <summary>Ersetzt den Qualitaetswert des Formats. Null laesst dessen Vorgabe stehen.</summary>
    public int? Crf { get; init; }

    /// <summary>"veryfast", "medium", "slow" - null laesst die Vorgabe stehen.</summary>
    public string? Speed { get; init; }

    public required ImageAdjustments Adjustments { get; init; }

    /// <summary>Kopiert uebergeben - waehrend des Laufs darf niemand mehr daran drehen.</summary>
    public required GradingStack Grading { get; init; }

    public required IViewTransform View { get; init; }

    /// <summary>
    /// Wie viele Bilder vorausgerechnet werden. Mehr als zwei oder drei bringt wenig
    /// und kostet je Bild die volle Bildgroesse an Speicher.
    /// </summary>
    public int LookAhead { get; init; } = 3;

    /// <summary>Threadzahl fuer den Encoder. 0 ueberlaesst ffmpeg die Wahl.</summary>
    public int EncoderThreads { get; init; }
}

/// <summary>
/// Schreibt die korrigierte Sequenz als Video.
///
/// **Die Bilder gehen roh in ffmpeg hinein, nicht als Dateien.** Der vorhandene
/// Export reicht dem Encoder eine Liste von Pfaden und legt die Korrektur als
/// ffmpeg-Filter darueber - fuer die sechs Grundregler geht das, weil es fuer jeden
/// eine Entsprechung gibt. Fuer eine Gradationskurve, acht Farbbereiche und eine
/// Nachschlagetabelle gibt es keine, und eine Naeherung waere hier genau das
/// Falsche: Man saehe im Video etwas anderes als in der Vorschau, auf die man sich
/// beim Einstellen verlassen hat.
///
/// Also wird jedes Bild hier gerechnet und als Rohdaten in die Standardeingabe
/// geschrieben. Das spart nebenbei den Zwischenspeicher: 300 Bilder in 4K als
/// PNG-Zwischenstand waeren mehrere Gigabyte, die nur entstehen, um gleich wieder
/// gelesen zu werden.
/// </summary>
public static class GradeVideo
{
    public static async Task<GradeBatchResult> RunAsync(string ffmpeg, GradeVideoRequest request,
                                                        IProgress<GradeProgress>? progress = null,
                                                        CancellationToken token = default)
    {
        var watch = Stopwatch.StartNew();
        var failures = new List<string>();

        if (request.Frames.Count == 0)
            return new GradeBatchResult(0, new[] { "Keine Bilder" }, false, watch.Elapsed);

        // Die Masse des ersten Bildes bestimmen den Datenstrom - ffmpeg muss sie
        // vorher wissen, weil Rohdaten keinen Kopf haben.
        var first = Load(request.Frames[0]);
        if (first is null)
            return new GradeBatchResult(0, new[] { $"{Path.GetFileName(request.Frames[0])}: nicht lesbar" },
                                        false, watch.Elapsed);

        int width = first.Width;
        int height = first.Height;

        // Gerade Masse: yuv420p kann mit einer ungeraden Kante nichts anfangen, und
        // ffmpeg bricht dann mit einer Meldung ab, die niemand auf die Bildgroesse
        // zurueckfuehrt.
        if (width % 2 != 0 || height % 2 != 0)
            return new GradeBatchResult(0, new[] { $"Ungerade Bildgroesse {width}x{height}" },
                                        false, watch.Elapsed);

        var prepared = request.Grading.Prepare();

        var process = new Process
        {
            StartInfo = Arguments(ffmpeg, request, width, height),
            EnableRaisingEvents = true,
        };

        var errors = new List<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) lock (errors) errors.Add(e.Data);
        };

        if (!process.Start())
            return new GradeBatchResult(0, new[] { "ffmpeg liess sich nicht starten" }, false, watch.Elapsed);

        process.BeginErrorReadLine();

        int written = 0;
        bool cancelled = false;

        try
        {
            var stream = process.StandardInput.BaseStream;
            var pending = new Queue<(string Path, Task<byte[]?> Work)>();
            var last = new Box(width * height * 4);
            int lookAhead = Math.Clamp(request.LookAhead, 1, 8);

            foreach (string path in request.Frames)
            {
                token.ThrowIfCancellationRequested();

                // Vorausrechnen, aber der Reihe nach schreiben: ein Videostrom
                // vertraegt keine vertauschten Bilder, und ein Reihenfolgepuffer
                // ueber die ganze Sequenz waere bei 4K ein Gigabyte.
                pending.Enqueue((path, Task.Run(() => Render(path, request, prepared, width, height), token)));

                if (pending.Count > lookAhead)
                    await WriteNext(pending, stream, failures, progress, request, () => written++, last);
            }

            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                await WriteNext(pending, stream, failures, progress, request, () => written++, last);
            }

            // Erst schliessen, dann warten: ffmpeg beendet den Strom, sobald die
            // Eingabe zu ist, und wartet sonst endlos auf mehr.
            process.StandardInput.Close();
            await process.WaitForExitAsync(CancellationToken.None);

            if (process.ExitCode != 0)
            {
                lock (errors)
                    failures.Add("ffmpeg: " + (errors.Count > 0 ? errors[^1] : $"Code {process.ExitCode}"));
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            }

            process.Dispose();
        }

        // Eine abgebrochene Datei ist keine halbe Datei, sondern eine unbrauchbare -
        // ein Video, dessen Kopf fehlt, laesst sich nicht abspielen und sieht im
        // Ordner trotzdem aus wie ein Ergebnis.
        if (cancelled)
        {
            try { if (File.Exists(request.OutputPath)) File.Delete(request.OutputPath); }
            catch (Exception) { }
        }

        return new GradeBatchResult(written, failures, cancelled, watch.Elapsed);
    }

    /// <summary>
    /// Nimmt das naechste fertige Bild und schreibt es in den Strom.
    ///
    /// <paramref name="last"/> haelt das zuletzt geschriebene Bild fest. Faellt eines
    /// aus, wird es wiederholt statt ausgelassen: Auslassen machte das Video kuerzer
    /// und verschoebe alles danach gegen den Ton und gegen jede Zeitangabe. Das ist
    /// dieselbe Antwort, die der gewoehnliche Export auf Luecken in einer Sequenz
    /// gibt - dort heisst sie HoldLast.
    /// </summary>
    private static async Task WriteNext(Queue<(string Path, Task<byte[]?> Work)> pending, Stream stream,
                                        List<string> failures, IProgress<GradeProgress>? progress,
                                        GradeVideoRequest request, Action count, Box last)
    {
        var (path, work) = pending.Dequeue();
        byte[]? pixels;

        try
        {
            pixels = await work;
        }
        catch (Exception ex)
        {
            failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            pixels = null;
        }

        if (pixels is null)
        {
            failures.Add($"{Path.GetFileName(path)}: nicht lesbar, voriges Bild wiederholt");
            // Ganz am Anfang gibt es noch keines zum Wiederholen; dann bleibt
            // Schwarz, damit die Zeitachse stimmt.
            pixels = last.Pixels ?? new byte[last.FrameBytes];
        }
        else
        {
            last.Pixels = pixels;
        }

        await stream.WriteAsync(pixels);
        count();

        progress?.Report(new GradeProgress(0, request.Frames.Count, Path.GetFileName(path)));
    }

    /// <summary>Haelt das zuletzt geschriebene Bild - ein Feld waere in der Schleife nicht erreichbar.</summary>
    private sealed class Box
    {
        public Box(int frameBytes) => FrameBytes = frameBytes;

        public readonly int FrameBytes;
        public byte[]? Pixels;
    }

    private static byte[]? Render(string path, GradeVideoRequest request, PreparedGrading grading,
                                  int width, int height)
    {
        var frame = Load(path);
        if (frame is null) return null;

        // Ein Bild mit anderen Massen kann nicht in einen laufenden Rohstrom -
        // dessen Bildgroesse steht seit dem Start fest.
        if (frame.Width != width || frame.Height != height) return null;

        var view = frame.IsSceneReferred ? request.View : new StandardViewTransform();

        int stride = width * 4;
        var pixels = new byte[stride * height];

        unsafe
        {
            fixed (byte* target = pixels)
                FloatFrameProcessor.Apply(frame, request.Adjustments, view, grading, (IntPtr)target, stride);
        }

        return pixels;
    }

    private static FloatFrame? Load(string path)
    {
        if (Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return FloatFrame.FromExr(path);

        var decoder = new Decoding.WicFrameDecoder();
        return decoder.TryDecode(path, 16384, 16384, n => new byte[n], out var decoded)
            ? FloatFrame.FromBgra32(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride)
            : null;
    }

    /// <summary>
    /// Der Aufruf. Die Ausgabeseite kommt aus demselben Preset wie beim gewoehnlichen
    /// Export - Codec, Pixelformat und die Eigenheiten je Format sind dort schon
    /// einmal richtig aufgeschrieben worden.
    /// </summary>
    internal static ProcessStartInfo Arguments(string ffmpeg, GradeVideoRequest request, int width, int height)
    {
        var info = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        void Add(string value) => info.ArgumentList.Add(value);

        Add("-hide_banner");
        Add("-nostdin");
        Add("-loglevel"); Add("error");
        Add("-y");

        // Die Eingabe: rohe Bildpunkte ohne Kopf, also muss alles davorstehen.
        Add("-f"); Add("rawvideo");
        Add("-pixel_format"); Add("bgra");
        Add("-video_size"); Add($"{width}x{height}");
        Add("-framerate"); Add(Number(request.Fps));
        Add("-i"); Add("-");

        foreach (string argument in WithOverrides(request)) Add(argument);

        Add("-fps_mode"); Add("cfr");
        Add("-r"); Add(Number(request.Fps));

        if (request.EncoderThreads > 0)
        {
            Add("-threads");
            Add(request.EncoderThreads.ToString(CultureInfo.InvariantCulture));
        }

        Add(request.OutputPath);
        return info;
    }

    /// <summary>
    /// Die Argumente des Presets, mit Guete und Tempo ueberschrieben, wo der Aufrufer
    /// etwas gesetzt hat. Ersetzt statt angehaengt: ein zweites -crf hinter dem
    /// ersten gewinnt zwar, aber verlassen sollte man sich darauf nicht.
    /// </summary>
    internal static IReadOnlyList<string> WithOverrides(GradeVideoRequest request)
    {
        var args = request.Preset.VideoArguments.ToList();

        Replace("-crf", request.Crf?.ToString(CultureInfo.InvariantCulture));
        Replace("-preset", request.Speed);

        return args;

        void Replace(string key, string? value)
        {
            if (value is null) return;

            int at = args.IndexOf(key);
            if (at >= 0 && at + 1 < args.Count) args[at + 1] = value;
        }
    }

    private static string Number(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);
}
