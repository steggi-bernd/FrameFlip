using System.Diagnostics;
using System.IO;
using FrameFlip.Caching;

namespace FrameFlip.Tests;

/// <summary>
/// Die zweite Cachestufe auf der Platte. Kernpunkte: sie darf nie ein falsches Bild
/// liefern, und sie darf nie der Grund sein, dass etwas gar nicht geht.
/// </summary>
public static class RawCacheInvariants
{
    public static void Run()
    {
        RoundTrip();
        RejectsStaleEntries();
        SurvivesTrouble();
        RespectsLimit();
        Speed();
    }

    private static string NewSource(int bytes = 4096)
    {
        var path = Path.Combine(Path.GetTempPath(), "ffrawtest_" + Guid.NewGuid().ToString("N")[..8] + ".png");
        var data = new byte[bytes];
        new Random(3).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Pattern(int width, int height)
    {
        var pixels = new byte[width * 4 * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 % 251);
        return pixels;
    }

    // ---------------------------------------------------------------- Hin und zurueck

    private static void RoundTrip()
    {
        Check.Group("Rohcache - schreiben und lesen");

        const int w = 64, h = 48;
        var source = NewSource();
        var cache = new RawFrameCache("test-roundtrip-" + Guid.NewGuid(), 64L * 1024 * 1024);

        try
        {
            var pixels = Pattern(w, h);

            Check.That(!cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                "ein leerer Cache liefert nichts");

            cache.Write(0, source, pixels, w, h, w * 4);

            bool ok = cache.TryRead(0, source, n => new byte[n],
                                    out var read, out int rw, out int rh, out int rs);

            Check.That(ok, "der abgelegte Frame wird gefunden");
            Check.That(rw == w && rh == h && rs == w * 4,
                "Abmessungen und Zeilenabstand kommen unveraendert zurueck", $"{rw}x{rh}, Stride {rs}");
            Check.That(read.AsSpan(0, pixels.Length).SequenceEqual(pixels),
                "und die Pixel sind Byte fuer Byte dieselben");

            Check.That(cache.Hits == 1 && cache.Writes == 1,
                "Treffer und Schreibvorgaenge werden gezaehlt", $"{cache.Hits}/{cache.Writes}");

            // Andere Nummer, nichts abgelegt.
            Check.That(!cache.TryRead(5, source, n => new byte[n], out _, out _, out _, out _),
                "ein nicht abgelegter Frame wird nicht erfunden");
        }
        finally
        {
            cache.Dispose();
            try { File.Delete(source); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- Veraltete Eintraege

    /// <summary>
    /// Der wichtigste Fall: waehrend eines laufenden Renders werden Frames
    /// ueberschrieben. Ein Treffer auf den alten Stand zeigte hartnaeckig das Bild
    /// von vorhin - schlimmer als gar kein Cache.
    /// </summary>
    private static void RejectsStaleEntries()
    {
        Check.Group("Rohcache - veraltete Eintraege werden verworfen");

        const int w = 32, h = 32;
        var source = NewSource();
        var cache = new RawFrameCache("test-stale-" + Guid.NewGuid(), 64L * 1024 * 1024);

        try
        {
            cache.Write(0, source, Pattern(w, h), w, h, w * 4);
            Check.That(cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                "frisch abgelegt wird gefunden");

            // Quelldatei neu schreiben - andere Laenge und andere Zeit.
            System.Threading.Thread.Sleep(20);
            File.WriteAllBytes(source, new byte[8192]);

            Check.That(!cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                "nach dem Ueberschreiben der Quelle gilt der Eintrag nicht mehr");
            Check.That(cache.Misses >= 1, "und wird als Fehltreffer gezaehlt", $"{cache.Misses}");

            // Geloeschte Quelle: kein Treffer, keine Ausnahme.
            File.Delete(source);
            Check.That(!cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                "eine geloeschte Quelldatei ergibt keinen Treffer");
        }
        finally
        {
            cache.Dispose();
            try { if (File.Exists(source)) File.Delete(source); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- Robustheit

    private static void SurvivesTrouble()
    {
        Check.Group("Rohcache - Stoerungen bleiben folgenlos");

        const int w = 16, h = 16;
        var source = NewSource();
        var cache = new RawFrameCache("test-trouble-" + Guid.NewGuid(), 64L * 1024 * 1024);

        try
        {
            cache.Write(0, source, Pattern(w, h), w, h, w * 4);

            // Eine abgeschnittene Datei darf keinen halben Frame liefern.
            var path = Directory.GetFiles(cache.Directory, "*.ffr").FirstOrDefault();
            Check.That(path is not null, "die Ablage liegt im eigenen Ordner");

            if (path is not null)
            {
                var all = File.ReadAllBytes(path);
                File.WriteAllBytes(path, all.Take(all.Length / 3).ToArray());

                Check.That(!cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                    "eine abgeschnittene Datei wird zurueckgewiesen");
            }

            // Fremddatei mit passendem Namen: die Kennung am Dateianfang faengt das ab.
            var alien = Path.Combine(cache.Directory, "00000009.ffr");
            File.WriteAllText(alien, "das ist kein Frame");
            Check.That(!cache.TryRead(9, source, n => new byte[n], out _, out _, out _, out _),
                "eine Fremddatei wird nicht als Frame gelesen");

            // Widerspruechliche Angaben werden gar nicht erst abgelegt.
            cache.Write(20, source, Pattern(w, h), w, h, stride: w * 4 + 8);
            Check.That(!cache.TryRead(20, source, n => new byte[n], out _, out _, out _, out _),
                "ein Stride, der nicht zur Breite passt, wird abgelehnt");
        }
        finally
        {
            cache.Dispose();
            try { File.Delete(source); } catch (IOException) { }
        }

        Check.That(true, "Dispose raeumt den Ordner weg, ohne zu werfen");
    }

    // ---------------------------------------------------------------- Platzgrenze

    private static void RespectsLimit()
    {
        Check.Group("Rohcache - die Platzgrenze haelt");

        const int w = 256, h = 256;                       // 256 KB je Frame
        var source = NewSource();

        // Grenze bewusst klein: die Klasse hebt sie auf mindestens 64 MB an.
        var cache = new RawFrameCache("test-limit-" + Guid.NewGuid(), 1024);

        try
        {
            var pixels = Pattern(w, h);
            for (int i = 0; i < 400; i++) cache.Write(i, source, pixels, w, h, w * 4);

            long onDisk = Directory.GetFiles(cache.Directory, "*.ffr").Sum(f => new FileInfo(f).Length);

            Check.That(cache.IsFull, "der Cache meldet sich als voll");
            Check.That(onDisk <= 80L * 1024 * 1024,
                "und bleibt in der Naehe seiner Grenze", $"{onDisk / 1024.0 / 1024:0.0} MB");

            // Was abgelegt wurde, ist weiter lesbar - voll heisst nicht kaputt.
            Check.That(cache.TryRead(0, source, n => new byte[n], out _, out _, out _, out _),
                "frueh abgelegte Frames bleiben nutzbar");
        }
        finally
        {
            cache.Dispose();
            try { File.Delete(source); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- Tempo

    private static void Speed()
    {
        Check.Group("Rohcache - Tempo");

        const int w = 1920, h = 1080;
        var source = NewSource();
        var cache = new RawFrameCache("test-speed-" + Guid.NewGuid(), 512L * 1024 * 1024);

        try
        {
            var pixels = Pattern(w, h);
            var buffer = new byte[w * 4 * h];

            cache.Write(0, source, pixels, w, h, w * 4);
            cache.TryRead(0, source, _ => buffer, out _, out _, out _, out _);   // warmlaufen

            var writes = new List<double>();
            var reads = new List<double>();

            for (int i = 1; i <= 10; i++)
            {
                var watch = Stopwatch.StartNew();
                cache.Write(i, source, pixels, w, h, w * 4);
                watch.Stop();
                writes.Add(watch.Elapsed.TotalMilliseconds);

                watch.Restart();
                cache.TryRead(i, source, _ => buffer, out _, out _, out _, out _);
                watch.Stop();
                reads.Add(watch.Elapsed.TotalMilliseconds);
            }

            writes.Sort();
            reads.Sort();

            double read = reads[reads.Count / 2];
            double write = writes[writes.Count / 2];

            Console.WriteLine($"         1080p: lesen {read:0.0} ms, schreiben {write:0.0} ms " +
                              $"(PNG entpacken kostet rund 31 ms)");

            // Der ganze Zweck ist, schneller als das Entpacken zu sein.
            Check.That(read < 20, "lesen bleibt deutlich unter dem Entpacken", $"{read:0.0} ms");
            Check.That(write < 40, "schreiben bleibt vertretbar", $"{write:0.0} ms");
        }
        finally
        {
            cache.Dispose();
            try { File.Delete(source); } catch (IOException) { }
        }
    }
}
