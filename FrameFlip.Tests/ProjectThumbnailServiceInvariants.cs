using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Projects;

namespace FrameFlip.Tests;

public static class ProjectThumbnailServiceInvariants
{
    public static void Run()
    {
        Check.Group("Projekt-Thumbnails - Cache, Worker und Abbruch");
        Cache();
        Cancellation();
        Failure();
        ConcurrentReaders();
    }

    private static void Cache()
    {
        int caller = Environment.CurrentManagedThreadId;
        var reads = new List<(string Path, int Width, int Thread)>();
        var image = Bitmap();
        var service = new ProjectThumbnailService((path, width) =>
        {
            reads.Add((path, width, Environment.CurrentManagedThreadId));
            return image;
        });
        Finish(service.LoadAsync("frame.png", 320, default));
        var cached = service.LoadAsync("frame.png", 320, default);
        Check.That(cached.IsCompletedSuccessfully && ReferenceEquals(Finish(cached), image), "Cache-Treffer liefert sofort dasselbe Bitmap");
        Check.That(reads.Count == 1 && reads[0].Thread != caller, "nur der erste Zugriff dekodiert und laeuft auf einem Worker");
        Finish(service.LoadAsync("frame.png", 480, default));
        Finish(service.LoadAsync("other.png", 320, default));
        Check.That(reads.Select(r => (r.Path, r.Width)).SequenceEqual(new[] { ("frame.png", 320), ("frame.png", 480), ("other.png", 320) }),
                   "Dateipfad und Dekodierbreite unterscheiden die Cache-Eintraege");

        int count = 0;
        var bounded = new ProjectThumbnailService((_, _) => { count++; return image; });
        for (int i = 0; i < 401; i++) Finish(bounded.LoadAsync(i.ToString(), 320, default));
        Finish(bounded.LoadAsync("0", 320, default));
        Check.That(count == 401, "bereits vorhandenes Bild bleibt am bisherigen Cache-Limit erhalten");
        Finish(bounded.LoadAsync("next", 320, default));
        Finish(bounded.LoadAsync("0", 320, default));
        Check.That(count == 403, "neues Bild ueber dem Limit leert den alten Bestand");
        Finish(bounded.LoadAsync("next", 320, default));
        Check.That(count == 403, "das ausloesende neue Bild bleibt nach dem Leeren im Cache");
    }

    private static void Cancellation()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var canceled = new CancellationTokenSource();
        var image = Bitmap();
        int reads = 0;
        var service = new ProjectThumbnailService((_, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1) { started.Set(); Wait(release); }
            return image;
        });
        var old = service.LoadAsync("same", 320, canceled.Token);
        try
        {
            Wait(started);
            canceled.Cancel();
            release.Set();
            Check.Throws<OperationCanceledException>(() => Finish(old), "abgeloeste Anfrage liefert ihr spaetes Bitmap nicht aus");
            Check.That(ReferenceEquals(Finish(service.LoadAsync("same", 320, default)), image) && reads == 2,
                       "abgebrochenes Ergebnis wurde nicht zwischengespeichert");
            Check.Throws<OperationCanceledException>(() => Finish(service.LoadAsync("same", 320, canceled.Token)),
                       "abgebrochene Anfrage liefert auch keinen vorhandenen Cache-Treffer");
            Check.Throws<OperationCanceledException>(() => Finish(service.LoadAsync("never", 320, canceled.Token)),
                       "vorab abgebrochene Anfrage startet keinen Worker");
            Check.That(reads == 2, "abgebrochene Folgeanfragen verursachen keine Dateizugriffe");
        }
        finally
        {
            release.Set();
            try { Finish(old); } catch (OperationCanceledException) { }
        }
    }

    private static void Failure()
    {
        int reads = 0;
        var image = Bitmap();
        var service = new ProjectThumbnailService((_, _) => ++reads switch
        {
            1 => throw new IOException("isolated decode failure"),
            2 => null,
            _ => image,
        });
        Check.That(Finish(service.LoadAsync("frame", 320, default)) is null, "Dekodierfehler liefert einen leeren Platzhalter");
        Check.That(Finish(service.LoadAsync("frame", 320, default)) is null && reads == 2, "auch erfolgloser Decoder kann erneut gelesen werden");
        Check.That(ReferenceEquals(Finish(service.LoadAsync("frame", 320, default)), image) && reads == 3,
                   "spaeter lesbares Bild gelangt in den Cache");
        Finish(service.LoadAsync("frame", 320, default));
        Check.That(reads == 3, "erfolgreiche Wiederholung wird wiederverwendet");
    }

    private static void ConcurrentReaders()
    {
        var images = Enumerable.Range(0, 64).Select(_ => Bitmap()).ToArray();
        int reads = 0;
        var service = new ProjectThumbnailService((path, _) =>
        {
            Interlocked.Increment(ref reads);
            return images[int.Parse(path)];
        });
        var jobs = Enumerable.Range(0, images.Length).Select(i => service.LoadAsync(i.ToString(), 320, default)).ToArray();
        var loaded = Finish(Task.WhenAll(jobs));
        Check.That(loaded.Zip(images).All(pair => ReferenceEquals(pair.First, pair.Second)), "gleichzeitige Ladevorgaenge behalten ihre Bildzuordnung");
        for (int i = 0; i < images.Length; i++) Finish(service.LoadAsync(i.ToString(), 320, default));
        Check.That(reads == images.Length, "gleichzeitiges Befuellen verliert keine Cache-Eintraege");
    }

    internal static BitmapSource Bitmap()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        image.Freeze();
        return image;
    }
    private static T Finish<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    internal static void Wait(ManualResetEventSlim signal)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Thumbnail-Testsignal fehlt.");
    }
}
