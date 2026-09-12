using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Diagnostics;

namespace FrameFlip.Playback;

/// <summary>Wie schnell vorausgeladen werden darf.</summary>
public enum PreloadPace
{
    /// <summary>Die Maschine hat nichts zu tun - FrameFlip darf zulangen.</summary>
    Fast,

    Medium,

    /// <summary>Etwas anderes rechnet. Ein Kern, und zwischen den Bloecken Luft.</summary>
    Slow,
}

/// <summary>Was der Lader nach aussen meldet.</summary>
public sealed record PreloadProgress(int Loaded, int Total, PreloadPace Pace, long Bytes)
{
    /// <summary>Mit welcher Breite gelesen wird.</summary>
    public int Width { get; init; }

    /// <summary>Welche Breite die Buehne haette zeigen koennen.</summary>
    public int WantedWidth { get; init; }

    /// <summary>True, wenn fuers Budget verkleinert werden musste.</summary>
    public bool Reduced => WantedWidth > 0 && Width < WantedWidth - 8;

    public double Share => Total <= 0 ? 0 : Loaded / (double)Total;
}

/// <summary>
/// Eine Bildfolge vollstaendig in den Speicher lesen, bevor sie abgespielt wird.
///
/// WARUM UEBERHAUPT. Von der Platte gelesen kostet jedes Bild zweistellige
/// Millisekunden. Bei 24 Bildern je Sekunde bleibt davon nichts uebrig, und die
/// Wiedergabe ruckelt genau dann am staerksten, wenn nebenan ein Render laeuft - also
/// immer. Liegt die Folge im Speicher, ist das Abspielen nur noch Anzeigen.
///
/// DAS TEMPO RICHTET SICH NACH DER MASCHINE. Wer vorausladen will, waehrend Blender
/// rendert, darf ihm nicht die Kerne wegnehmen. Vor jedem Block wird neu gefragt, wie
/// es um die Maschine steht; zwischen den Bloecken wird bei Last eine Pause eingelegt.
/// Der Block ist klein genug, dass sich das Tempo binnen Sekundenbruchteilen anpasst,
/// und gross genug, dass die Frage nicht teurer wird als die Arbeit.
///
/// DER SPEICHER IST BEGRENZT. Passt die Folge in voller Anzeigebreite nicht in das
/// eingestellte Budget, wird die Breite verringert, bis sie passt - ein etwas
/// weicheres Bild ist besser als eine Wiedergabe, die nach der Haelfte stockt. Passt
/// sie auch dann nicht, wird geladen, was hineingeht; der Rest kommt beim Abspielen
/// von der Platte, und das Fenster sagt es.
/// </summary>
public sealed class SequencePreloader : IDisposable
{
    /// <summary>Unter diese Breite wird nicht verkleinert - darunter sieht man es.</summary>
    private const int NarrowestWidth = 480;

    private readonly IReadOnlyList<string> _paths;
    private readonly Func<PreloadPace> _pace;
    private readonly Action<PreloadProgress> _report;
    private readonly CancellationTokenSource _stop = new();

    private int _loaded;
    private long _bytes;

    public SequencePreloader(IReadOnlyList<string> paths, Func<PreloadPace> pace,
                             Action<PreloadProgress> report)
    {
        _paths = paths;
        _pace = pace;
        _report = report;
    }

    /// <summary>Die gelesenen Bilder, an derselben Stelle wie ihr Pfad. Null heisst: nicht geladen.</summary>
    public BitmapSource?[] Frames { get; private set; } = Array.Empty<BitmapSource?>();

    /// <summary>Wieviele wirklich im Speicher liegen.</summary>
    public int Loaded => _loaded;

    /// <summary>True, wenn das Budget nicht fuer alle gereicht hat.</summary>
    public bool Partial { get; private set; }

    /// <summary>Die Breite, mit der gelesen wurde - kann unter der gewuenschten liegen.</summary>
    public int Width { get; private set; }

    public void Cancel() => _stop.Cancel();

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    /// <summary>
    /// Laden. Gibt zurueck, ob die Folge vollstaendig im Speicher liegt.
    /// Laeuft abseits des Oberflaechenfadens; die Meldungen kommen von dort, wo der
    /// Aufrufer sie hinlegt.
    /// </summary>
    public async Task<bool> RunAsync(int wantedWidth, long budgetBytes, double aspect = 16.0 / 9.0)
    {
        int count = _paths.Count;

        Frames = new BitmapSource?[count];
        Width = ChooseWidth(wantedWidth, count, budgetBytes, aspect);

        var token = _stop.Token;
        int start = 0;

        while (start < count)
        {
            if (token.IsCancellationRequested) return false;

            var pace = _pace();
            int end = Math.Min(count, start + BlockFor(pace));

            int from = start;
            int to = end;

            try
            {
                // Parallel.For und nicht ForEachAsync: Der Rumpf ist durch und durch
                // synchron - er liest eine Datei und dekodiert sie. In der
                // asynchronen Fassung lief er auf wenigen Poolfaeden hintereinander
                // ab; gemessen waren von sechzehn erlaubten Kernen im Mittel gut drei
                // beschaeftigt. Parallel.For verteilt synchrone Arbeit auf so viele
                // Faeden, wie erlaubt sind.
                await Task.Run(() => Parallel.For(from, to,
                    new ParallelOptions { MaxDegreeOfParallelism = Workers(pace), CancellationToken = token },
                    index =>
                    {
                        // Ueber dem Budget wird nicht weitergelesen. Das Abbruchzeichen
                        // faellt hier, nicht vorher: Die tatsaechliche Groesse steht
                        // erst fest, wenn das erste Bild gelesen ist.
                        if (Interlocked.Read(ref _bytes) >= budgetBytes) return;

                        var image = Read(_paths[index], Width);
                        if (image is null) return;

                        Frames[index] = image;

                        Interlocked.Add(ref _bytes, (long)image.PixelWidth * image.PixelHeight * 4);
                        Interlocked.Increment(ref _loaded);
                    }), token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            start = end;

            _report(new PreloadProgress(_loaded, count, pace, Interlocked.Read(ref _bytes))
            {
                Width = Width,
                WantedWidth = wantedWidth,
            });

            if (Interlocked.Read(ref _bytes) >= budgetBytes)
            {
                Partial = true;
                return false;
            }

            // Bei Last zwischen den Bloecken Luft lassen. Ohne das haelt FrameFlip
            // zwar nur einen Kern, laesst ihn aber nie los.
            if (pace == PreloadPace.Slow) await Task.Delay(45, CancellationToken.None);
            else if (pace == PreloadPace.Medium) await Task.Delay(8, CancellationToken.None);
        }

        Partial = _loaded < count;
        return !Partial;
    }

    /// <summary>
    /// Wieviele Kerne das Lesen nehmen darf.
    ///
    /// Bei Ruhe alle: Wenn nichts anderes rechnet, gibt es niemanden, dem man sie
    /// wegnaehme, und die halbe Maschine brachliegen zu lassen macht das Warten nur
    /// laenger. Vorher war hier die Haelfte gedeckelt bei acht, und auf einer ruhigen
    /// Maschine war davon nichts zu sehen ausser einem kurzen Zucken.
    /// </summary>
    private static int Workers(PreloadPace pace) => pace switch
    {
        PreloadPace.Fast => Math.Clamp(Environment.ProcessorCount, 2, 16),
        PreloadPace.Medium => Math.Clamp(Environment.ProcessorCount / 3, 1, 4),
        _ => 1,
    };

    /// <summary>
    /// Bilder je Block. Nach jedem Block wird das Tempo neu gefragt.
    ///
    /// Bei vollem Tempo grosse Bloecke, weil jede Frage eine Fadenumschaltung kostet
    /// und es nichts zu schonen gibt. Bei Last kleine, damit die Drosselung greift,
    /// bevor der Renderer nebenan es merkt.
    /// </summary>
    private static int BlockFor(PreloadPace pace) => pace switch
    {
        PreloadPace.Fast => 48,
        PreloadPace.Medium => 16,
        _ => 8,
    };

    /// <summary>
    /// Die Lesebreite so waehlen, dass die ganze Folge ins Budget passt.
    ///
    /// Gerechnet wird mit vier Byte je Bildpunkt und dem TATSAECHLICHEN Seitenverhaeltnis
    /// des Renders. Vorher standen hier pauschal 16:9, und bei einem Breitbildformat
    /// wie 2,35:1 war die Schaetzung um ein Drittel zu hoch - verkleinert wurde also
    /// auch dort, wo es gar nicht noetig war.
    ///
    /// Verkleinert wird ueberhaupt nur, wenn es sein muss. Ein weicheres Bild ist
    /// besser als eine Wiedergabe, die nach der Haelfte stockt - aber es ist eben
    /// auch schlechter als ein scharfes, und deshalb sagt der Lader hinterher, ob er
    /// dazu gezwungen war.
    /// </summary>
    private static int ChooseWidth(int wanted, int count, long budget, double aspect)
    {
        double ratio = aspect > 0.05 && aspect < 20 ? aspect : 16.0 / 9.0;
        int width = Math.Max(NarrowestWidth, wanted);

        while (width > NarrowestWidth)
        {
            long perFrame = (long)(width * (width / ratio) * 4);

            if (perFrame * (long)count <= budget) break;

            width -= 40;
        }

        return Math.Max(NarrowestWidth, width);
    }

    private static BitmapSource? Read(string path, int width)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = width;
            bitmap.EndInit();

            // Eingefroren wandert es ueber Fadengrenzen, ohne kopiert zu werden.
            //
            // Der Speicherbedarf ist dabei genau das, was die Bildpunkte ausmachen.
            // Das sieht man dem Prozess nicht sofort an: Nach hundertzwanzig Bildern
            // von 1280x544 meldete er neunhundert Megabyte, waehrend sein Arbeitssatz
            // bei knapp vierhundert lag - die Differenz sind reservierte Adressen,
            // die Laufzeit und Bilddekoder vorhalten, nicht belegter Speicher. Wer
            // hier nach Einsparungen sucht, misst das Falsche; das Budget rechnet
            // deshalb mit den Punkten.
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception)
        {
            // Halb geschrieben, gesperrt, kaputt: Diese Stelle bleibt leer, und beim
            // Abspielen kommt sie eben von der Platte.
            return null;
        }
    }

    /// <summary>
    /// Aus der Systemlast ein Tempo machen.
    ///
    /// Laeuft nebenan ein Render, wird nie mit vollem Tempo geladen - selbst wenn die
    /// Messung gerade Ruhe meldet. Ein Render hat Phasen, in denen er wenig rechnet,
    /// und genau dann die Kerne zu nehmen, hiesse ihn auszubremsen, sobald er wieder
    /// anzieht.
    /// </summary>
    public static PreloadPace PaceFor(LoadSnapshot? load, bool rendering)
    {
        if (load is null) return rendering ? PreloadPace.Slow : PreloadPace.Medium;

        var pace = load.Level switch
        {
            LoadLevel.Idle => PreloadPace.Fast,
            LoadLevel.Moderate => PreloadPace.Medium,
            _ => PreloadPace.Slow,
        };

        if (rendering && pace == PreloadPace.Fast) pace = PreloadPace.Medium;

        return pace;
    }
}
