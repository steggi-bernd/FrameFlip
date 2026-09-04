using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Caching;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

/// <summary>
/// Der In/Out-Bereich und der Ringpuffer.
///
/// Der Ring rechnete lange ueber die ganze Sequenz, auch wenn nur ein Ausschnitt
/// gespielt wurde. Beim Loop-Sprung vom Out- zurueck zum In-Punkt wrappte das
/// Vorausladen dadurch ueber das SEQUENZende statt ueber das Bereichsende: Der Frame
/// am In-Punkt war weder vorgeladen noch gehalten, waehrend der Ring die Frames
/// hinter dem Out-Punkt lud, die nie gezeigt werden. Bei einer Sequenz aus 2000
/// Bildern waren das hunderte nutzlose Dekodierungen je Runde, jede davon zusaetzlich
/// in den Rohcache geschrieben.
/// </summary>
public static class RangeInvariants
{
    private const int Frames = 40;

    public static void Run()
    {
        var folder = CreateSequence();

        try
        {
            var decoders = FrameDecoderRegistry.CreateDefault();
            var sequence = SequenceScanner.Scan(Path.Combine(folder, "f_0001.png"), decoders);

            if (sequence is null || sequence.Count != Frames)
            {
                Check.Group("In/Out-Bereich");
                Check.That(false, "Testsequenz liess sich anlegen", $"{sequence?.Count ?? -1} Frames");
                return;
            }

            PrefetchStaysInRange(sequence, decoders);
            RangeFitsEntirelyInRing(sequence, decoders);
            RangeMathHoldsAtTheEdges();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- Vorausladen

    private static void PrefetchStaysInRange(ImageSequence sequence, FrameDecoderRegistry decoders)
    {
        Check.Group("In/Out-Bereich - der Ring laedt nur darin");

        using var cache = Build(sequence, decoders);

        cache.SetRange(10, 19);
        cache.SetPosition(19, 1, loop: true, urgent: true);

        Settle(cache, expected: 10);

        // Der springende Punkt: Am Out-Punkt stehend muss der In-Punkt bereitliegen,
        // denn genau dorthin geht der naechste Schritt.
        Check.That(cache.Contains(10),
            "am Out-Punkt liegt der In-Punkt bereit");

        int inside = Enumerable.Range(10, 10).Count(cache.Contains);
        Check.That(inside == 10, "der ganze Bereich liegt im Ring", $"{inside} von 10");

        int beyond = Enumerable.Range(20, 15).Count(cache.Contains);
        Check.That(beyond == 0,
            "hinter dem Out-Punkt wird nichts geladen", $"{beyond} Frames daneben");

        int before = Enumerable.Range(0, 10).Count(cache.Contains);
        Check.That(before == 0, "vor dem In-Punkt ebenso wenig", $"{before} Frames daneben");

        Check.That(cache.ReadyAhead() > 0,
            "und es gilt als Vorrat, nicht als Leerlauf", $"{cache.ReadyAhead()}");

        // Gegenprobe: Ohne Bereich laedt derselbe Ring sehr wohl ueber Index 19 hinaus.
        // Ohne sie koennte der Test auch dann bestehen, wenn gar nichts geladen wuerde.
        cache.SetRange(0, sequence.Count - 1);
        cache.SetPosition(19, 1, loop: true, urgent: true);
        Settle(cache, expected: 25);

        Check.That(Enumerable.Range(20, 10).Count(cache.Contains) > 0,
            "ohne Bereich wird dagegen darueber hinaus geladen");
    }

    // ---------------------------------------------------------------- Kapazitaet

    private static void RangeFitsEntirelyInRing(ImageSequence sequence, FrameDecoderRegistry decoders)
    {
        Check.Group("In/Out-Bereich - ein kurzer Ausschnitt passt ganz hinein");

        using var cache = Build(sequence, decoders);

        cache.SetRange(4, 11);                       // acht Frames
        cache.SetPosition(4, 1, loop: true, urgent: true);

        Settle(cache, expected: 8);

        var stats = cache.GetStats();

        Check.That(stats.CachedFrames == 8,
            "alle acht Frames werden gehalten", $"{stats.CachedFrames}");
        Check.That(stats.Capacity <= 8,
            "und der Ring reserviert nicht mehr, als der Bereich braucht", $"{stats.Capacity}");

        // Liegt alles im Ring, ist der Vorrat vollstaendig - sonst puffert die
        // Wiedergabe nach, obwohl jeder Frame bereits daliegt.
        Check.That(stats.AheadReady >= 7,
            "der Vorrat reicht ueber den ganzen Bereich", $"{stats.AheadReady}");
    }

    // ---------------------------------------------------------------- Randfaelle

    private static void RangeMathHoldsAtTheEdges()
    {
        Check.Group("In/Out-Bereich - Raender");

        // Ein einzelner Frame als Bereich: der Schritt bleibt darauf stehen.
        Check.That(SequenceMath.OffsetInRange(7, 1, 7, 7, loop: true) == 7,
            "ein Bereich aus einem Frame bleibt auf sich selbst");
        Check.That(SequenceMath.OffsetInRange(7, -1, 7, 7, loop: true) == 7,
            "auch rueckwaerts");

        // Der Loop-Sprung geht auf den In-Punkt, nicht auf Null.
        Check.That(SequenceMath.OffsetInRange(19, 1, 10, 19, loop: true) == 10,
            "hinter dem Out-Punkt folgt der In-Punkt", "nicht Index 0");
        Check.That(SequenceMath.OffsetInRange(10, -1, 10, 19, loop: true) == 19,
            "und vor dem In-Punkt der Out-Punkt");

        // Ohne Loop endet der Bereich hart.
        Check.That(SequenceMath.OffsetInRange(19, 1, 10, 19, loop: false) < 0,
            "ohne Loop gibt es hinter dem Out-Punkt nichts");

        // Ein umgedrehter Bereich ist kein Bereich.
        Check.That(SequenceMath.OffsetInRange(5, 1, 10, 4, loop: true) < 0,
            "ein umgedrehter Bereich liefert nichts");

        // Ein Sprung ueber mehrere Runden landet trotzdem im Bereich.
        int far = SequenceMath.OffsetInRange(10, 253, 10, 19, loop: true);
        Check.That(far is >= 10 and <= 19,
            "auch ein weiter Sprung bleibt im Bereich", $"{far}");
    }

    // ---------------------------------------------------------------- Hilfen

    private static FrameCache Build(ImageSequence sequence, FrameDecoderRegistry decoders)
    {
        var profile = new ResourceProfile(LoadLevel.Idle, 4, ThreadPriority.Normal,
                                          ProcessPriorityClass.Normal, 1.0, 1.0);

        return new FrameCache(sequence, decoders, 32, 32,
                              budgetBytes: 64L * 1024 * 1024,
                              ahead: 20, behind: 5, loop: true,
                              maxWorkers: 4, profile: profile);
    }

    /// <summary>Wartet, bis der Ring zur Ruhe gekommen ist - hoechstens drei Sekunden.</summary>
    private static void Settle(FrameCache cache, int expected)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < 3000)
        {
            if (cache.GetStats().CachedFrames >= expected) break;
            Thread.Sleep(15);
        }

        // Kurz nachlaufen lassen: gerade laufende Dekodierungen wuerden sonst als
        // "nicht geladen" gelten, obwohl sie eine Millisekunde spaeter dastehen.
        Thread.Sleep(120);
    }

    /// <summary>
    /// Erzeugt die Sequenz selbst, statt Dateien aus dem Repo zu lesen: Der Test
    /// bleibt damit unabhaengig davon, was im Arbeitsverzeichnis liegt. 32x32 ist
    /// klein genug, dass vierzig Bilder in Sekundenbruchteilen entstehen.
    /// </summary>
    private static string CreateSequence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ffrange_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        const int size = 32;
        var pixels = new byte[size * size * 4];

        for (int frame = 1; frame <= Frames; frame++)
        {
            // Jeder Frame anders eingefaerbt, damit ein vertauschter Index auffiele.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(frame * 6 % 256);
                pixels[i + 1] = (byte)(i % 256);
                pixels[i + 2] = (byte)(255 - frame * 6 % 256);
                pixels[i + 3] = 255;
            }

            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null,
                                             pixels, size * 4);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(Path.Combine(folder, $"f_{frame:0000}.png"));
            encoder.Save(stream);
        }

        return folder;
    }
}
