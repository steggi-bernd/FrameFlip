using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Export;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Der Videoweg. Die Argumente lassen sich ohne ffmpeg pruefen; der Durchlauf
/// selbst nur, wenn eines auf der Maschine liegt - dann aber richtig, bis zur
/// abspielbaren Datei.
/// </summary>
public static class GradeVideoInvariants
{
    public static void Run()
    {
        Arguments();
        Overrides();
        RealRun();
    }

    private static GradeVideoRequest Sample(IReadOnlyList<string> frames, string output) => new()
    {
        Frames = frames,
        OutputPath = output,
        Preset = ExportPreset.H264,
        Fps = 24,
        Adjustments = ImageAdjustments.Neutral,
        Grading = new GradingStack(),
        View = new StandardViewTransform(),
    };

    private static void Arguments()
    {
        Check.Group("Videoweg: der Aufruf");

        var request = Sample(new[] { "a.exr" }, @"C:\ziel\film.mp4");
        var info = GradeVideo.Arguments("ffmpeg.exe", request, 1920, 1080);
        var args = info.ArgumentList.ToList();
        string line = string.Join(" ", args);

        // Rohdaten haben keinen Kopf: Format, Pixelaufbau, Groesse und Bildrate
        // muessen VOR dem Eingang stehen, sonst raet ffmpeg.
        int input = args.IndexOf("-i");
        Check.That(input > 0 && args[input + 1] == "-", "die Eingabe kommt von der Standardeingabe");

        foreach (string key in new[] { "-f", "-pixel_format", "-video_size", "-framerate" })
        {
            int at = args.IndexOf(key);
            Check.That(at >= 0 && at < input, $"{key} steht vor dem Eingang", $"{at} gegen {input}");
        }

        Check.That(args[args.IndexOf("-pixel_format") + 1] == "bgra", "das Pixelformat ist bgra");
        Check.That(args[args.IndexOf("-video_size") + 1] == "1920x1080", "die Groesse steht drin");

        // Die Ausgabeseite kommt aus dem Preset - Codec und Pixelformat sollen nicht
        // zweimal aufgeschrieben sein.
        Check.That(line.Contains("libx264"), "der Codec kommt aus dem Preset");
        Check.That(line.Contains("yuv420p"), "und das Ausgabe-Pixelformat auch");

        Check.That(args[^1] == @"C:\ziel\film.mp4", "der Zielpfad steht am Ende");
        Check.That(info.RedirectStandardInput, "die Standardeingabe wird umgeleitet");
        Check.That(!info.UseShellExecute && info.CreateNoWindow, "ohne Fenster und ohne Shell");

        // Die Bildrate muss mit Punkt geschrieben sein, egal wie das System zaehlt -
        // "23,976" verstuende ffmpeg als zwei Argumente.
        var odd = GradeVideo.Arguments("ffmpeg.exe", request with { Fps = 23.976 }, 640, 480);
        Check.That(odd.ArgumentList.Contains("23.976"), "krumme Bildraten mit Punkt",
                   string.Join(" ", odd.ArgumentList));
    }

    private static void Overrides()
    {
        Check.Group("Videoweg: Guete und Tempo");

        var request = Sample(Array.Empty<string>(), "x.mp4");

        var plain = GradeVideo.WithOverrides(request);
        Check.That(plain[plain.ToList().IndexOf("-crf") + 1] == "18", "ohne Angabe bleibt die Vorgabe");

        var sharper = GradeVideo.WithOverrides(request with { Crf = 14, Speed = "slow" });
        var list = sharper.ToList();

        Check.That(list[list.IndexOf("-crf") + 1] == "14", "die Guete wird ersetzt");
        Check.That(list[list.IndexOf("-preset") + 1] == "slow", "und das Tempo");

        // Ersetzt, nicht angehaengt: zweimal -crf im Aufruf waere eine Wette darauf,
        // welches gewinnt.
        Check.That(list.Count(a => a == "-crf") == 1, "jeder Schalter steht genau einmal");
        Check.That(list.Count(a => a == "-preset") == 1, "auch das Tempo");

        // Ein Preset ohne CRF - ProRes - darf davon nicht durcheinandergeraten.
        var prores = GradeVideo.WithOverrides(request with { Preset = ExportPreset.ProRes, Crf = 10 });
        Check.That(!prores.Contains("-crf"), "ein Format ohne Guetewert bekommt keinen");
    }

    /// <summary>
    /// Wenn ffmpeg da ist, wird wirklich eines geschrieben - und danach gelesen, um
    /// zu sehen, dass es Bilder in der richtigen Zahl und Groesse enthaelt.
    /// </summary>
    private static void RealRun()
    {
        Check.Group("Videoweg: ein echter Durchlauf");

        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            Console.WriteLine("  [--]   uebersprungen: kein ffmpeg gefunden");
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-video-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var frames = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                string path = Path.Combine(folder, $"f_{i + 1:0000}.png");
                WritePng(path, 64, 48, (x, y) => (byte)((x * 4 + i * 20) % 256));
                frames.Add(path);
            }

            string output = Path.Combine(folder, "film.mp4");

            var stack = new GradingStack();
            stack.Tools.Add(new CurvesTool
            {
                Master = new ToneCurve(new[]
                {
                    new CurvePoint(0, 0), new CurvePoint(0.5f, 0.75f), new CurvePoint(1, 1),
                }),
            });

            var request = Sample(frames, output) with { Grading = stack, Fps = 12 };
            var result = GradeVideo.RunAsync(ffmpeg, request).GetAwaiter().GetResult();

            Check.That(result.Written == 12, "alle zwoelf Bilder gingen hinein", $"{result.Written}");
            Check.That(result.Failures.Count == 0, "ohne Fehler", string.Join(" | ", result.Failures));
            Check.That(File.Exists(output), "die Datei ist da");

            if (File.Exists(output))
            {
                long size = new FileInfo(output).Length;
                Check.That(size > 1000, "und nicht leer", $"{size} Bytes");
            }

            // Ein fehlendes Bild mittendrin: gemeldet, aber der Strom laeuft weiter
            // und behaelt seine Laenge.
            var withGap = frames.ToList();
            withGap[5] = Path.Combine(folder, "gibtsnicht.png");

            string second = Path.Combine(folder, "luecke.mp4");
            var gapped = GradeVideo.RunAsync(ffmpeg, request with { Frames = withGap, OutputPath = second })
                                   .GetAwaiter().GetResult();

            Check.That(gapped.Written == 12, "eine Luecke kuerzt das Video nicht", $"{gapped.Written}");
            Check.That(gapped.Failures.Count == 1, "und wird gemeldet", string.Join(" | ", gapped.Failures));
            Check.That(gapped.Failures[0].Contains("wiederholt"), "mit dem Hinweis, was stattdessen geschah",
                       gapped.Failures[0]);

            // Abbruch: die angefangene Datei darf nicht liegenbleiben.
            using var stop = new CancellationTokenSource();
            stop.Cancel();

            string third = Path.Combine(folder, "abbruch.mp4");
            var stopped = GradeVideo.RunAsync(ffmpeg, request with { OutputPath = third }, null, stop.Token)
                                    .GetAwaiter().GetResult();

            Check.That(stopped.Cancelled, "der Abbruch wird gemeldet");
            Check.That(!File.Exists(third), "und die unbrauchbare Datei ist fort");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static void WritePng(string path, int width, int height, Func<int, int, byte> value)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = value(x, y);
                int at = y * stride + x * 4;
                pixels[at] = v;
                pixels[at + 1] = v;
                pixels[at + 2] = v;
                pixels[at + 3] = 255;
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
