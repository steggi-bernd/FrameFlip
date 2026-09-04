using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Decoding;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

/// <summary>
/// Invariante 6 und 7 der Referenzliste: die Namensmuster aus Blenders
/// Ausgabepfaden und die Aufbereitung der Luecken fuer die Befehlszeile.
/// </summary>
public static class SequenceInvariants
{
    private static readonly FrameDecoderRegistry Decoders = FrameDecoderRegistry.CreateDefault();

    public static void Run()
    {
        BlenderNamingPatterns();
        GapsAndBlenderCommand();
        InOutRange();
        NearestFrame();
    }

    // ---------------------------------------------------------------- In/Out-Bereich

    private static void InOutRange()
    {
        Check.Group("In- und Out-Punkt begrenzen die Wiedergabe");

        // Ohne gesetzte Punkte verhaelt sich alles wie ueber die ganze Sequenz.
        Check.That(SequenceMath.OffsetInRange(9, 1, 0, 9, loop: true) == 0,
            "ohne Bereich wrappt der Loop am Sequenzende");
        Check.That(SequenceMath.OffsetInRange(9, 1, 0, 9, loop: false) == -1,
            "ohne Loop bleibt die Wiedergabe am Ende stehen");

        // Mit Bereich [3..7]
        Check.That(SequenceMath.OffsetInRange(7, 1, 3, 7, loop: true) == 3,
            "Loop springt vom Out-Punkt auf den In-Punkt");
        Check.That(SequenceMath.OffsetInRange(3, -1, 3, 7, loop: true) == 7,
            "rueckwaerts springt der Loop vom In- auf den Out-Punkt");
        Check.That(SequenceMath.OffsetInRange(7, 1, 3, 7, loop: false) == -1,
            "ohne Loop endet die Wiedergabe am Out-Punkt");
        Check.That(SequenceMath.OffsetInRange(5, 1, 3, 7, loop: false) == 6,
            "innerhalb des Bereichs zaehlt der Schritt normal");

        // Die Zeitachse laeuft weiter und wird erst beim Aufloesen gefaltet.
        Check.That(SequenceMath.ResolveInRange(3, 3, 7, loop: true, out _) == 3,
            "Zeitachse am In-Punkt");
        Check.That(SequenceMath.ResolveInRange(8, 3, 7, loop: true, out _) == 3,
            "Zeitachse faltet hinter dem Out-Punkt auf den In-Punkt");
        Check.That(SequenceMath.ResolveInRange(12, 3, 7, loop: true, out _) == 7,
            "Zeitachse faltet auch ueber mehrere Durchlaeufe korrekt");

        SequenceMath.ResolveInRange(20, 3, 7, loop: false, out bool pastEnd);
        Check.That(pastEnd, "ohne Loop wird das Ende des Bereichs gemeldet");

        // Ein Bereich von genau einem Frame darf nicht in eine Division durch null laufen.
        Check.That(SequenceMath.OffsetInRange(4, 1, 4, 4, loop: true) == 4,
            "ein Bereich aus einem einzigen Frame bleibt stehen");
        Check.That(SequenceMath.ResolveInRange(99, 4, 4, loop: true, out _) == 4,
            "auch die Zeitachse bleibt bei einem Einzelframe stehen");

        // Die alte Schnittstelle muss sich unveraendert verhalten.
        Check.That(SequenceMath.Offset(9, 1, 10, loop: true) == 0,
            "Offset bleibt zur bisherigen Fassung kompatibel");
        Check.That(SequenceMath.Resolve(11, 10, loop: true, out _) == 1,
            "Resolve bleibt zur bisherigen Fassung kompatibel");
    }

    // ---------------------------------------------------------------- Sprung in Luecken

    private static void NearestFrame()
    {
        Check.Group("Sprung in eine Luecke landet auf einem vorhandenen Frame");

        // Vorhanden: 1..6, 10..12, 14..20 - wie bei einem abgebrochenen Render.
        var names = Enumerable.Range(1, 6)
            .Concat(Enumerable.Range(10, 3))
            .Concat(Enumerable.Range(14, 7))
            .Select(n => $"render_{n:0000}.png").ToArray();

        InFolder(names, "render_0001.png", seq =>
        {
            Check.That(seq.Frames[seq.IndexNearestNumber(6)].Number == 6,
                "vorhandene Nummer wird direkt getroffen");
            Check.That(seq.Frames[seq.IndexNearestNumber(7)].Number == 6,
                "kurz hinter dem Rand faellt es auf den letzten vorhandenen zurueck");
            Check.That(seq.Frames[seq.IndexNearestNumber(9)].Number == 10,
                "kurz vor dem Rand geht es auf den naechsten vorhandenen vor");
            Check.That(seq.Frames[seq.IndexNearestNumber(0)].Number == 1,
                "vor dem Anfang wird der erste Frame gezeigt");
            Check.That(seq.Frames[seq.IndexNearestNumber(999)].Number == 20,
                "hinter dem Ende wird der letzte Frame gezeigt");
        });
    }

    // ---------------------------------------------------------------- Invariante 6

    private static void BlenderNamingPatterns()
    {
        Check.Group("Invariante 6 - Namensmuster aus Blenders Ausgabepfaden");

        // Ausgabepfad "//render/" - der Praefix ist leer.
        InFolder(new[] { "0001.png", "0002.png", "0003.png" }, "0002.png", seq =>
        {
            Check.That(seq.Count == 3, "leeres Praefix: alle drei Frames gefunden", $"{seq.Count} gefunden");
            Check.That(seq.Pattern.Prefix.Length == 0, "leeres Praefix erkannt", $"Praefix '{seq.Pattern.Prefix}'");
            Check.That(seq.Padding == 4, "Padding 4 erkannt", $"Padding {seq.Padding}");
        });

        // Ziffer im Praefix: die LETZTE Zifferngruppe ist die Framenummer.
        InFolder(new[] { "shot2_0001.png", "shot2_0002.png" }, "shot2_0001.png", seq =>
        {
            Check.That(seq.Count == 2, "Ziffer im Praefix: beide Frames gefunden", $"{seq.Count} gefunden");
            Check.That(seq.Pattern.Prefix == "shot2_", "Praefix 'shot2_' erkannt", $"'{seq.Pattern.Prefix}'");
            Check.That(seq.Frames[0].Number == 1, "Framenummer ist 1, nicht 20001", $"{seq.Frames[0].Number}");
        });

        // Padding-Ueberlauf. Blender schreibt nach f_99 die Datei f_100, nicht f_00100.
        // Beide Richtungen pruefen: es haengt davon ab, welche Datei selektiert war.
        var overflow = new[] { "f_97.png", "f_98.png", "f_99.png", "f_100.png", "f_101.png" };

        InFolder(overflow, "f_98.png", seq =>
            Check.That(seq.Count == 5, "Padding-Ueberlauf, von der kurzen Nummer aus", $"{seq.Count} von 5"));

        InFolder(overflow, "f_100.png", seq =>
            Check.That(seq.Count == 5, "Padding-Ueberlauf, von der langen Nummer aus", $"{seq.Count} von 5"));

        // Fuehrende Nullen legen das Padding eindeutig fest, auch wenn die
        // Beispieldatei selbst darueber hinausgewachsen ist.
        InFolder(new[] { "f_0099.png", "f_0100.png", "f_10000.png" }, "f_10000.png", seq =>
        {
            Check.That(seq.Count == 3, "vierstelliges Padding mit Ueberlauf auf fuenf", $"{seq.Count} von 3");
            Check.That(seq.Padding == 4, "Padding aus der fuehrenden Null abgeleitet", $"Padding {seq.Padding}");
        });

        // Zwei Muster im selben Ordner: die Beispieldatei entscheidet.
        InFolder(new[] { "f_001.png", "f_002.png", "f_0001.png", "f_0002.png" }, "f_0001.png", seq =>
            Check.That(seq.Count == 2 && seq.Padding == 4,
                "vierstelliges Muster gewinnt, wenn vierstellig selektiert wurde",
                $"{seq.Count} Frames, Padding {seq.Padding}"));

        InFolder(new[] { "f_001.png", "f_002.png", "f_0001.png", "f_0002.png" }, "f_001.png", seq =>
            Check.That(seq.Count == 2 && seq.Padding == 3,
                "dreistelliges Muster gewinnt, wenn dreistellig selektiert wurde",
                $"{seq.Count} Frames, Padding {seq.Padding}"));

        // Multi-View: das Suffix nach der Nummer trennt die beiden Augen.
        var stereo = new[] { "f_0001_L.png", "f_0002_L.png", "f_0001_R.png", "f_0002_R.png" };

        InFolder(stereo, "f_0001_L.png", seq =>
        {
            Check.That(seq.Count == 2, "View-Suffix _L bildet eine eigene Sequenz", $"{seq.Count} von 2");
            Check.That(seq.Pattern.Suffix == "_L", "Suffix '_L' erkannt", $"'{seq.Pattern.Suffix}'");
            Check.That(seq.Frames.All(f => f.FileName.Contains("_L")), "nur die linke Ansicht enthalten");
        });

        InFolder(stereo, "f_0002_R.png", seq =>
            Check.That(seq.Count == 2 && seq.Frames.All(f => f.FileName.Contains("_R")),
                "View-Suffix _R bildet eine eigene Sequenz", $"{seq.Count} von 2"));

        // Andere Endungen im selben Ordner gehoeren nicht dazu.
        InFolder(new[] { "f_0001.png", "f_0002.png", "f_0001.jpg" }, "f_0001.png", seq =>
            Check.That(seq.Count == 2, "andere Dateiendung bleibt aussen vor", $"{seq.Count} von 2"));

        // Einzelbild ohne Zaehler.
        InFolder(new[] { "beauty.png" }, "beauty.png", seq =>
            Check.That(seq.Count == 1, "Einzelbild ohne Nummer wird angezeigt", $"{seq.Count}"));
    }

    // ---------------------------------------------------------------- Invariante 7

    private static void GapsAndBlenderCommand()
    {
        Check.Group("Invariante 7 - Luecken und der Befehl zum Nachrendern");

        Check.That(SequenceMath.FormatForBlender(new[] { 42, 88, 89, 90, 91, 130 }) == "42,88..91,130",
            "FormatForBlender fasst Bereiche zusammen",
            SequenceMath.FormatForBlender(new[] { 42, 88, 89, 90, 91, 130 }));

        Check.That(SequenceMath.FormatForDisplay(new[] { 42, 88, 89, 90, 91, 130 }) == "42, 88–91, 130",
            "die Anzeige benutzt den Halbgeviertstrich",
            SequenceMath.FormatForDisplay(new[] { 42, 88, 89, 90, 91, 130 }));

        Check.That(SequenceMath.FormatForBlender(Array.Empty<int>()) == "",
            "leere Liste ergibt leeren Befehl");

        Check.That(SequenceMath.FormatForBlender(new[] { 7 }) == "7",
            "eine einzelne Nummer bleibt einzeln");

        Check.That(SequenceMath.FormatForBlender(new[] { 1, 2, 3 }) == "1..3",
            "ein durchgehender Bereich wird zusammengefasst");

        Check.That(SequenceMath.CountRanges(new[] { 42, 88, 89, 90, 91, 130 }) == 3,
            "drei Luecken, nicht sechs fehlende Frames",
            SequenceMath.CountRanges(new[] { 42, 88, 89, 90, 91, 130 }).ToString());

        // Am echten Bestand: ein abgebrochener Render mit zwei Luecken.
        InFolder(new[] { "f_0001.png", "f_0002.png", "f_0005.png", "f_0006.png", "f_0009.png" },
                 "f_0001.png", seq =>
        {
            Check.That(seq.HasGaps, "Luecken werden erkannt");
            Check.That(seq.SpanLength == 9, "Nummernbereich umfasst 9 Frames", $"{seq.SpanLength}");

            var missing = seq.MissingNumbers();
            Check.That(missing.SequenceEqual(new[] { 3, 4, 7, 8 }),
                "fehlende Nummern korrekt ermittelt",
                string.Join(",", missing));

            Check.That(SequenceMath.FormatForBlender(missing) == "3..4,7..8",
                "Befehl fuer Blender aufbereitet",
                SequenceMath.FormatForBlender(missing));
        });

        InFolder(new[] { "f_0001.png", "f_0002.png", "f_0003.png" }, "f_0001.png", seq =>
        {
            Check.That(!seq.HasGaps, "lueckenlose Sequenz meldet keine Luecken");
            Check.That(seq.MissingNumbers().Count == 0, "lueckenlos heisst keine fehlenden Nummern");
        });
    }

    // ---------------------------------------------------------------- Hilfsmittel

    /// <summary>Legt einen Ordner mit den genannten Dateien an und ruft die Pruefung auf.</summary>
    private static void InFolder(string[] names, string seed, Action<ImageSequence> assert)
    {
        string folder = Path.Combine(Path.GetTempPath(), "frameflip_seq_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            foreach (var name in names) WriteTinyImage(Path.Combine(folder, name));

            var sequence = SequenceScanner.Scan(Path.Combine(folder, seed), Decoders);
            if (sequence is null)
            {
                Check.That(false, $"Sequenz fuer {seed} konnte gelesen werden");
                return;
            }

            assert(sequence);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static void WriteTinyImage(string path)
    {
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                                         new byte[2 * 4 * 2], 2 * 4);

        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };

        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
