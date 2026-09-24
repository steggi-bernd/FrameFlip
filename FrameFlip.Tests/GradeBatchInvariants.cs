using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Der Stapellauf: ein Rezept auf eine ganze Sequenz.
///
/// Was hier zaehlt, ist nicht nur, dass Dateien entstehen, sondern dass der Lauf
/// die Faelle uebersteht, die im Betrieb wirklich vorkommen - ein fehlendes Bild,
/// eine Datei, die keine ist, ein Abbruch mittendrin.
/// </summary>
public static class GradeBatchInvariants
{
    public static void Run()
    {
        WritesEveryFrame();
        SurvivesFailures();
        NeverOverwritesSource();
        Cancels();
        SixteenBitCarriesMore();
    }

    private static void WritesEveryFrame()
    {
        Check.Group("Stapellauf schreibt die Sequenz");

        using var work = new Workspace();
        var frames = work.MakeSequence(6, 32, 24);

        var progress = new List<GradeProgress>();
        var result = GradeBatch.Run(work.Request(frames, GradeOutputFormat.Png16),
                                    new Progress<GradeProgress>(progress.Add));

        Check.That(result.Written == 6, "alle sechs Bilder geschrieben", $"{result.Written}");
        Check.That(result.Complete, "ohne Fehler", string.Join(", ", result.Failures));

        var produced = Directory.GetFiles(work.Output, "*.png").OrderBy(p => p).ToList();
        Check.That(produced.Count == 6, "und liegen im Zielordner", $"{produced.Count}");

        // Die Namen muessen die Nummerierung behalten - daran haengt die
        // Sequenzerkennung, wenn das Ergebnis spaeter wieder geoeffnet wird.
        Check.That(Path.GetFileNameWithoutExtension(produced[0]) == "f_0001",
                   "der Name bleibt erhalten", Path.GetFileName(produced[0]));

        // Und keine halben Dateien.
        Check.That(Directory.GetFiles(work.Output, "*.part").Length == 0,
                   "keine Reste vom Schreiben");

        // Das Ergebnis muss sich als Bild lesen lassen und die richtige Groesse haben.
        var check = Read(produced[0]);
        Check.That(check.PixelWidth == 32 && check.PixelHeight == 24, "mit der richtigen Groesse",
                   $"{check.PixelWidth}x{check.PixelHeight}");

        Check.That(check.Format == PixelFormats.Rgba64, "und sechzehn Bit je Kanal", check.Format.ToString());
    }

    /// <summary>
    /// Ein Fehler darf den Lauf nicht beenden. Ein dreiminuetiger Durchgang, der
    /// seine Arbeit bei Bild 280 wegwirft, ist schlimmer als gar keiner.
    /// </summary>
    private static void SurvivesFailures()
    {
        Check.Group("Stapellauf uebersteht Fehler");

        using var work = new Workspace();
        var frames = work.MakeSequence(4, 16, 16).ToList();

        // Eine Datei, die keine ist, und eine, die es nicht gibt.
        string broken = Path.Combine(work.Input, "kaputt.png");
        File.WriteAllBytes(broken, new byte[] { 9, 9, 9, 9 });
        frames.Insert(2, broken);
        frames.Add(Path.Combine(work.Input, "gibtsnicht.png"));

        var result = GradeBatch.Run(work.Request(frames, GradeOutputFormat.Png8));

        Check.That(result.Written == 4, "die brauchbaren Bilder sind geschrieben", $"{result.Written}");
        Check.That(result.Failures.Count == 2, "die beiden anderen stehen in der Liste",
                   string.Join(" | ", result.Failures));
        Check.That(!result.Cancelled, "und der Lauf gilt nicht als abgebrochen");
        Check.That(!result.Complete, "aber auch nicht als vollstaendig");

        // Der Grund muss dranstehen, nicht nur die Zahl - sonst sucht man ihn
        // hinterher in den Dateien.
        Check.That(result.Failures.Any(f => f.Contains("kaputt")), "die kaputte Datei ist benannt");
    }

    private static void NeverOverwritesSource()
    {
        Check.Group("Stapellauf schreibt nie auf die Quelle");

        using var work = new Workspace();
        var frames = work.MakeSequence(3, 16, 16);

        // Zielordner gleich Quellordner, Format gleich Endung: genau die Lage, in
        // der die Quelle stillschweigend verlorenginge.
        var request = new GradeBatchRequest
        {
            Frames = frames,
            OutputDirectory = work.Input,
            Format = GradeOutputFormat.Png8,
            Adjustments = new ImageAdjustments { Exposure = -2 },
            Grading = new GradingStack(),
            View = new StandardViewTransform(),
        };

        var before = File.ReadAllBytes(frames[0]);
        var result = GradeBatch.Run(request);

        Check.That(result.Written == 0, "es wird nichts geschrieben", $"{result.Written}");
        Check.That(result.Failures.Count == 3, "und jedes Bild wird gemeldet", $"{result.Failures.Count}");
        Check.That(File.ReadAllBytes(frames[0]).SequenceEqual(before), "die Quelle ist unveraendert");

        // Mit anderer Endung im selben Ordner ist es dagegen in Ordnung.
        var ok = GradeBatch.Run(request with { Format = GradeOutputFormat.Tiff16 });
        Check.That(ok.Written == 3, "eine andere Endung im selben Ordner geht", $"{ok.Written}");
    }

    private static void Cancels()
    {
        Check.Group("Stapellauf laesst sich abbrechen");

        using var work = new Workspace();
        var frames = work.MakeSequence(40, 64, 48);

        using var source = new CancellationTokenSource();

        var result = GradeBatch.Run(
            work.Request(frames, GradeOutputFormat.Png8) with { MaxWorkers = 1 },
            new Progress<GradeProgress>(p =>
            {
                if (p.Done >= 3) source.Cancel();
            }),
            source.Token);

        Check.That(result.Cancelled, "der Abbruch wird gemeldet");
        Check.That(result.Written < 40, "und es wurde nicht alles geschrieben", $"{result.Written}");

        // Auch beim Abbruch darf nichts Halbes liegenbleiben.
        Check.That(Directory.GetFiles(work.Output, "*.part").Length == 0,
                   "keine halbe Datei bleibt zurueck");
    }

    /// <summary>
    /// Der Grund fuer sechzehn Bit: Eine Kurve, die einen schmalen Bereich streckt,
    /// erzeugt auf acht Bit Stufen, wo vorher keine waren.
    /// </summary>
    private static void SixteenBitCarriesMore()
    {
        Check.Group("Sechzehn Bit halten mehr");

        using var work = new Workspace();

        // Ein Verlauf ueber einen sehr schmalen Wertebereich.
        string path = work.MakeGradient(256, 8, 0.20f, 0.24f);
        var frames = new[] { path };

        // Eine Kurve, die genau diesen Bereich auf die volle Breite zieht.
        var stack = new GradingStack();
        stack.Tools.Add(new CurvesTool
        {
            Master = new ToneCurve(new[]
            {
                new CurvePoint(0, 0), new CurvePoint(0.45f, 0.05f),
                new CurvePoint(0.55f, 0.95f), new CurvePoint(1, 1),
            }),
        });

        var request = work.Request(frames, GradeOutputFormat.Png16) with { Grading = stack };

        GradeBatch.Run(request);
        var wide = Distinct(Read(GradeBatch.TargetFor(path, request)));

        GradeBatch.Run(request with { Format = GradeOutputFormat.Png8 });
        var narrow = Distinct(Read(GradeBatch.TargetFor(path, request with { Format = GradeOutputFormat.Png8 })));

        Check.That(wide > narrow * 4, "sechzehn Bit halten deutlich mehr Abstufungen",
                   $"{wide} gegen {narrow}");
    }

    /// <summary>Wie viele verschiedene Helligkeitswerte im Bild vorkommen.</summary>
    private static int Distinct(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Rgba64, null, 0);
        converted.Freeze();

        int stride = converted.PixelWidth * 8;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var seen = new HashSet<ushort>();
        for (int i = 0; i + 1 < pixels.Length; i += 8)
            seen.Add((ushort)(pixels[i] | (pixels[i + 1] << 8)));

        return seen.Count;
    }

    private static BitmapSource Read(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                                           BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    // ------------------------------------------------------------- Hilfsmittel

    private sealed class Workspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
                                                     "frameflip-batch-" + Guid.NewGuid().ToString("N")[..8]);

        public Workspace()
        {
            Input = Path.Combine(_root, "quelle");
            Output = Path.Combine(_root, "ziel");
            Directory.CreateDirectory(Input);
            Directory.CreateDirectory(Output);
        }

        public string Input { get; }

        public string Output { get; }

        public GradeBatchRequest Request(IReadOnlyList<string> frames, GradeOutputFormat format) => new()
        {
            Frames = frames,
            OutputDirectory = Output,
            Format = format,
            Adjustments = ImageAdjustments.Neutral,
            Grading = new GradingStack(),
            View = new StandardViewTransform(),
        };

        public string[] MakeSequence(int count, int width, int height)
        {
            var paths = new string[count];

            for (int i = 0; i < count; i++)
            {
                paths[i] = Path.Combine(Input, $"f_{i + 1:0000}.png");
                WritePng(paths[i], width, height, (x, y) => (byte)((x * 7 + y * 3 + i * 11) % 256));
            }

            return paths;
        }

        public string MakeGradient(int width, int height, float from, float to)
        {
            string path = Path.Combine(Input, "verlauf.png");

            WritePng(path, width, height, (x, _) =>
            {
                float t = width > 1 ? x / (width - 1f) : 0f;
                return (byte)Math.Clamp(MathF.Round((from + (to - from) * t) * 255f), 0, 255);
            });

            return path;
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

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }
}
