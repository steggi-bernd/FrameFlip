using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameFlip.Imaging.Grading;

/// <summary>Wohin geschrieben wird.</summary>
public enum GradeOutputFormat
{
    Png8,
    Png16,
    Tiff16,
    Jpeg,
}

/// <summary>
/// Was ein Durchlauf tun soll.
///
/// Als record, damit sich eine Abwandlung mit <c>with</c> bilden laesst - dasselbe
/// Rezept in ein anderes Format auszugeben ist der haeufigste Fall.
/// </summary>
public sealed record GradeBatchRequest
{
    /// <summary>Die Bilder, in der Reihenfolge der Sequenz.</summary>
    public required IReadOnlyList<string> Frames { get; init; }

    public required string OutputDirectory { get; init; }

    public GradeOutputFormat Format { get; init; } = GradeOutputFormat.Png16;

    /// <summary>1 bis 100 fuer JPEG; bei den uebrigen Formaten ohne Bedeutung.</summary>
    public int JpegQuality { get; init; } = 92;

    public required ImageAdjustments Adjustments { get; init; }

    /// <summary>
    /// Der Stapel. Wird vom Aufrufer KOPIERT uebergeben - waehrend der Durchlauf
    /// laeuft, darf niemand mehr daran drehen.
    /// </summary>
    public required GradingStack Grading { get; init; }

    /// <summary>
    /// Der Ebenenstapel, ebenfalls KOPIERT uebergeben. Null heisst: das Bild so, wie
    /// die Datei es hergibt.
    ///
    /// Er nennt nur Passe, keine Bilddaten - und genau deshalb laesst sich derselbe
    /// Stapel auf jedes Bild der Sequenz anwenden. Bei einem Bild eingerichtet,
    /// dreihundert gerechnet.
    /// </summary>
    public LayerStack? Layers { get; init; }

    /// <summary>
    /// Die Sichtumwandlung fuer Szenenlicht. Material, das aus acht Bit
    /// zurueckgerechnet wurde, bekommt unabhaengig davon die einfache.
    /// </summary>
    public required IViewTransform View { get; init; }

    /// <summary>
    /// Wie viele Bilder gleichzeitig. Kommt aus der Lastregelung: waehrend Blender
    /// rendert, soll der Durchlauf langsamer werden statt zu kaempfen.
    /// </summary>
    public int MaxWorkers { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    /// <summary>
    /// Der Graph, wenn das Atelier im Knotenmodus ist. Dann gilt er allein - Stapel,
    /// Grundkorrektur und Werkzeuge oben sind in ihm aufgegangen und werden nicht
    /// ein zweites Mal gerechnet.
    /// </summary>
    public Nodes.NodeGraph? Graph { get; init; }

    /// <summary>
    /// Der Name einer Ausgabe ohne Endung - null: der Name der Quelle. Der Schnell-Export
    /// gibt einem Einzelbild so seinen eindeutigen Namen, "render_FrameFlip_001".
    /// </summary>
    public Func<string, string>? NameFor { get; init; }
}

/// <summary>Wie weit der Durchlauf ist.</summary>
public readonly record struct GradeProgress(int Done, int Total, string CurrentFile);

/// <summary>Was dabei herausgekommen ist.</summary>
public sealed record GradeBatchResult(
    int Written,
    IReadOnlyList<string> Failures,
    bool Cancelled,
    TimeSpan Elapsed)
{
    public bool Complete => Failures.Count == 0 && !Cancelled;
}

/// <summary>
/// Wendet ein Rezept auf eine ganze Sequenz an.
///
/// Das ist der Schritt, der aus der Farbkorrektur eines Bildes eine Sequenz macht,
/// und der einzige, den die Wiedergabe nicht schon kann. Gerechnet wird ueber die
/// Bilder parallel - sie wissen nichts voneinander, was hier besser skaliert als
/// die Aufteilung nach Zeilen innerhalb eines Bildes.
///
/// **Ein Fehler beendet den Durchlauf nicht.** Ein fehlendes Bild, eine Datei, die
/// gerade geschrieben wird, eine volle Platte - all das kommt vor, und ein
/// dreiminuetiger Lauf, der seine Arbeit bei Bild 280 wegwirft, ist schlimmer als
/// gar keiner. Was schiefging, steht am Ende in der Liste.
/// </summary>
public static class GradeBatch
{
    public static GradeBatchResult Run(GradeBatchRequest request,
                                       IProgress<GradeProgress>? progress = null,
                                       CancellationToken token = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failures = new List<string>();
        int written = 0;
        int done = 0;

        Directory.CreateDirectory(request.OutputDirectory);

        // Einmal vorbereiten, dann von allen Threads nur noch gelesen - das ist der
        // Vertrag, den die Werkzeuge zusichern.
        var prepared = request.Grading.Prepare();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(request.MaxWorkers, 1, 16),
            CancellationToken = token,
        };

        try
        {
            // Je Faden eine eigene Kopie des Graphen: Seine Knoten halten beim Rechnen
            // vorbereitete Tabellen, und zwei Bilder zugleich im selben Knoten
            // schrieben sich gegenseitig hinein. Und je Faden ein eigener Vorrat - er
            // dient einer Rechnung zur Zeit, und von Bild zu Bild derselben.
            Parallel.ForEach(request.Frames, options,
                             () => (Graph: request.Graph?.Clone(), Pool: new Nodes.GridPool()),
                             (path, _, worker) =>
            {
                One(path, worker.Graph, worker.Pool);
                return worker;
            },
            _ => { });

            void One(string path, Nodes.NodeGraph? graph, Nodes.GridPool pool)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    string target = TargetFor(path, request);

                    // Die Quelle ueberschreiben waere ein Verlust, der sich nicht
                    // rueckgaengig machen laesst - und bei gleichem Ordner und
                    // gleicher Endung passiert es sonst stillschweigend.
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(path),
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        lock (failures)
                            failures.Add($"{Path.GetFileName(path)}: Ziel ist die Quelle");

                        return;
                    }

                    if (graph is not null)
                    {
                        var inputs = Nodes.GraphFrames.Read(graph, path, request.View, pool: pool);

                        if (inputs is null || !WriteGraph(graph, inputs, request, target))
                        {
                            lock (failures) failures.Add($"{Path.GetFileName(path)}: nicht lesbar");
                            return;
                        }

                        Interlocked.Increment(ref written);
                        return;
                    }

                    var (frame, overlays) = LayeredFrameLoader.LoadAll(path, request.Layers);
                    if (frame is null)
                    {
                        lock (failures) failures.Add($"{Path.GetFileName(path)}: nicht lesbar");
                        return;
                    }

                    var view = frame.IsSceneReferred ? request.View : new StandardViewTransform();

                    // Die Bildnummer aus dem Dateinamen, nicht die Stelle im Lauf:
                    // Sonst bekaeme dasselbe Bild ein anderes Korn, sobald jemand
                    // einen Ausschnitt nachexportiert.
                    Write(frame, request, view, prepared, overlays, target,
                          SequenceLink.NumberOf(path) ?? 0, Renderdata(prepared, path));

                    Interlocked.Increment(ref written);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (failures) failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
                finally
                {
                    int at = Interlocked.Increment(ref done);
                    progress?.Report(new GradeProgress(at, request.Frames.Count, Path.GetFileName(path)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new GradeBatchResult(written, failures, Cancelled: true, watch.Elapsed);
        }

        return new GradeBatchResult(written, failures, Cancelled: false, watch.Elapsed);
    }

    /// <summary>
    /// Der Zielpfad. Der Name bleibt, die Endung richtet sich nach dem Format -
    /// so bleibt die Nummerierung erhalten, an der die Sequenzerkennung haengt.
    /// </summary>
    public static string TargetFor(string source, GradeBatchRequest request)
        => Path.Combine(request.OutputDirectory,
                        (request.NameFor?.Invoke(source) ?? Path.GetFileNameWithoutExtension(source)) + Extension(request.Format));

    public static string Extension(GradeOutputFormat format) => format switch
    {
        GradeOutputFormat.Tiff16 => ".tif",
        GradeOutputFormat.Jpeg => ".jpg",
        _ => ".png",
    };

    /// <summary>
    /// Die Passe, die die Werkzeuge dieses Bildes brauchen.
    ///
    /// Je Bild gelesen, weil die Entfernung sich je Bild aendert - anders als das
    /// Rezept, das fuer den ganzen Lauf gilt. Fehlt der Pass, ruht das Werkzeug.
    /// </summary>
    private static FloatFrame?[] Renderdata(PreparedGrading grading, string path)
        => grading.Data.Length == 0
            ? Array.Empty<FloatFrame?>()
            : FramePasses.Resolve(grading.Data, Decoding.Exr.ExrPasses.Of(path),
                                  name => FloatFrame.FromExrPass(path, name));

    private static void Write(FloatFrame frame, GradeBatchRequest request, IViewTransform view,
                              PreparedGrading grading, OverlayPlan[] overlays, string target,
                              int number, FloatFrame?[] data)
    {
        BitmapSource image = request.Format == GradeOutputFormat.Png8 || request.Format == GradeOutputFormat.Jpeg
            ? Render8(frame, request, view, grading, overlays, number, data)
            : Render16(frame, request, view, grading, overlays, number, data);

        Save(image, request, target);
    }

    /// <summary>
    /// Ein Bild im Knotenmodus: der Graph rechnet, in acht oder sechzehn Bit, und
    /// geschrieben wird auf demselben Weg wie sonst.
    /// </summary>
    private static unsafe bool WriteGraph(Nodes.NodeGraph graph, Nodes.GraphInputs inputs,
                                          GradeBatchRequest request, string target)
    {
        if (Nodes.GraphFrames.Size(inputs) is not var (width, height)) return false;

        bool eight = request.Format == GradeOutputFormat.Png8 || request.Format == GradeOutputFormat.Jpeg;
        int stride = width * (eight ? 4 : 8);
        var pixels = new byte[stride * height];
        bool done;

        fixed (byte* start = pixels)
        {
            done = eight
                ? Nodes.GraphEvaluator.Render(graph, inputs, (IntPtr)start, stride)
                : Nodes.GraphEvaluator.Render16(graph, inputs, (IntPtr)start, stride);
        }

        if (!done) return false;

        var format = eight
            ? request.Format == GradeOutputFormat.Jpeg ? PixelFormats.Bgr32 : PixelFormats.Bgra32
            : PixelFormats.Rgba64;

        var image = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        image.Freeze();

        Save(image, request, target);
        return true;
    }

    private static void Save(BitmapSource image, GradeBatchRequest request, string target)
    {
        BitmapEncoder encoder = request.Format switch
        {
            GradeOutputFormat.Tiff16 => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            GradeOutputFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(request.JpegQuality, 1, 100) },
            _ => new PngBitmapEncoder(),
        };

        encoder.Frames.Add(BitmapFrame.Create(image));

        // Erst vollstaendig schreiben, dann an den Platz schieben: bricht der Lauf
        // mitten im Schreiben ab, liegt sonst eine halbe Datei da, und die sieht
        // fuer jeden spaeteren Durchgang aus wie ein fertiges Bild.
        string temporary = target + ".part";

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(stream);

        File.Move(temporary, target, overwrite: true);
    }

    private static unsafe BitmapSource Render8(FloatFrame frame, GradeBatchRequest request,
                                               IViewTransform view, PreparedGrading grading,
                                               OverlayPlan[] overlays, int number,
                                               FloatFrame?[] data)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        fixed (byte* target = pixels)
            FloatFrameProcessor.Apply(frame, request.Adjustments, view, grading, (IntPtr)target, stride,
                                      step: 1, overlays, number, data);

        // JPEG kennt kein Alpha; Bgr32 statt Bgra32 zu schreiben spart dem Encoder
        // das Verwerfen und dem Ergebnis eine Ueberraschung bei durchsichtigen Stellen.
        var format = request.Format == GradeOutputFormat.Jpeg ? PixelFormats.Bgr32 : PixelFormats.Bgra32;

        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, format, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static unsafe BitmapSource Render16(FloatFrame frame, GradeBatchRequest request,
                                                IViewTransform view, PreparedGrading grading,
                                                OverlayPlan[] overlays, int number,
                                                FloatFrame?[] data)
    {
        int stride = frame.Width * 8;      // vier Kanaele zu je zwei Byte
        var pixels = new byte[stride * frame.Height];

        fixed (byte* target = pixels)
            FloatFrameProcessor.ApplyRgba64(frame, request.Adjustments, view, grading,
                                            (IntPtr)target, stride, overlays, number, data);

        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96,
                                         PixelFormats.Rgba64, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
