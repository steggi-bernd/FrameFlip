using System.IO;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;

namespace FrameFlip.Tests;

/// <summary>
/// Der EXR-Reader, geprueft gegen Dateien aus Blender 4.5. Was hier zaehlt, ist
/// nicht, dass ueberhaupt Zahlen herauskommen, sondern dass es dieselben sind,
/// die hineingeschrieben wurden - samt der Werte oberhalb von Weiss.
/// </summary>
public static class ExrInvariants
{
    public static void Run()
    {
        Header();
        Values();
        MultiBlock();
        Selective();
        Rejections();
        HalfConversion();
        ChannelPicking();
        Decoder();
        Registration();
    }

    // ------------------------------------------------------------------- Kopf

    private static void Header()
    {
        Check.Group("EXR-Kopf");

        using var stream = new MemoryStream(ExrSamples.ZipHalf());
        var header = ExrHeaderReader.Read(stream);

        Check.That(header.DataWindow.Width == ExrSamples.ProbeWidth, "Breite aus dem Datenfenster");
        Check.That(header.DataWindow.Height == ExrSamples.ProbeHeight, "Hoehe aus dem Datenfenster");
        Check.That(header.Compression == ExrCompression.Zip, "Kompression ist ZIP");
        Check.That(header.LineOrder == ExrLineOrder.IncreasingY, "Zeilen laufen aufsteigend");
        Check.That(header.Channels.Count == 4, "vier Kanaele");

        // Das Format schreibt die alphabetische Reihenfolge vor. Wer sie nicht
        // beachtet, liest Rot als Alpha - und zwar ohne dass etwas abstuerzt.
        Check.That(string.Join(",", header.Channels.Select(c => c.Name)) == "A,B,G,R",
                   "Kanaele stehen alphabetisch", string.Join(",", header.Channels.Select(c => c.Name)));

        Check.That(header.Channels.All(c => c.PixelType == ExrPixelType.Half), "alle Kanaele sind Half");
        Check.That(header.ScanlinesPerBlock == 16, "ZIP fasst 16 Zeilen zusammen");
        Check.That(header.BlockCount == 1, "vier Zeilen ergeben einen Block");

        // Attribute, die der Reader nicht auswertet, gehen nicht verloren - dort
        // liegt spaeter das Cryptomatte-Manifest.
        Check.That(header.RawAttributes.ContainsKey("screenWindowWidth"),
                   "unausgewertete Attribute bleiben erhalten");

        using var floats = new MemoryStream(ExrSamples.ZipFloat());
        var floatHeader = ExrHeaderReader.Read(floats);
        Check.That(floatHeader.Channels.All(c => c.PixelType == ExrPixelType.Float),
                   "die 32-Bit-Datei meldet Float");
    }

    // ------------------------------------------------------------------ Werte

    /// <summary>
    /// Dieselben Pixel in vier Verfahren. Gruen steht ueberall auf 0,5: waere
    /// unterwegs ein sRGB-Transform passiert, stuende dort 0,735. Blau ist in der
    /// ersten Spalte 4,0 - der Wert, den ein 8-Bit-Format nicht halten kann.
    /// </summary>
    private static void Values()
    {
        Check.Group("EXR-Werte in allen vier Verfahren");

        Read("ZIP, Half", ExrSamples.ZipHalf(), 0.0005f);
        Read("ZIP, Float", ExrSamples.ZipFloat(), 1e-6f);
        Read("RLE", ExrSamples.Rle(), 0.0005f);
        Read("unkomprimiert", ExrSamples.Uncompressed(), 0.0005f);

        static void Read(string label, byte[] data, float tolerance)
        {
            using var stream = new MemoryStream(data);
            var header = ExrHeaderReader.Read(stream);
            var image = ExrReader.Read(stream, header);

            var r = image.Channel("R");
            var g = image.Channel("G");
            var b = image.Channel("B");
            var a = image.Channel("A");

            if (r is null || g is null || b is null || a is null)
            {
                Check.That(false, $"{label}: alle vier Kanaele gelesen");
                return;
            }

            int wrong = 0;
            for (int row = 0; row < ExrSamples.ProbeHeight; row++)
            {
                for (int x = 0; x < ExrSamples.ProbeWidth; x++)
                {
                    int i = row * ExrSamples.ProbeWidth + x;
                    if (Math.Abs(r[i] - ExrSamples.ProbeRed(row, x)) > tolerance) wrong++;
                    if (Math.Abs(g[i] - ExrSamples.ProbeGreen) > tolerance) wrong++;
                    if (Math.Abs(b[i] - ExrSamples.ProbeBlue(x)) > tolerance) wrong++;
                    if (Math.Abs(a[i] - 1.0f) > tolerance) wrong++;
                }
            }

            Check.That(wrong == 0, $"{label}: alle Pixel stimmen", $"{wrong} Abweichungen");
        }
    }

    private static void MultiBlock()
    {
        Check.Group("EXR ueber mehrere Bloecke");

        using var stream = new MemoryStream(ExrSamples.MultiBlock());
        var header = ExrHeaderReader.Read(stream);

        Check.That(header.BlockCount == 4, "64 Zeilen ergeben vier ZIP-Bloecke", $"{header.BlockCount}");

        var image = ExrReader.Read(stream, header);
        var r = image.Channel("R")!;
        var g = image.Channel("G")!;
        var b = image.Channel("B")!;

        int wrong = 0;
        for (int row = 0; row < ExrSamples.BigHeight; row++)
        {
            for (int x = 0; x < ExrSamples.BigWidth; x++)
            {
                int i = row * ExrSamples.BigWidth + x;
                if (Math.Abs(r[i] - ExrSamples.BigRed(x)) > 0.001f) wrong++;
                if (Math.Abs(g[i] - ExrSamples.BigGreen(row)) > 0.001f) wrong++;
                if (Math.Abs(b[i] - ExrSamples.BigBlue(row, x)) > 0.002f) wrong++;
            }
        }

        Check.That(wrong == 0, "alle Pixel ueber vier Bloecke stimmen", $"{wrong} Abweichungen");

        // Ein um einen Block verschobenes Bild faellt in der Summenpruefung oben
        // schon auf; diese beiden Ecken sagen im Fehlerfall, in welche Richtung.
        Check.Near(g[0], (ExrSamples.BigHeight - 1) / 100.0, 0.001, "oberste Zeile ist Blenders letzte");
        Check.Near(g[(ExrSamples.BigHeight - 1) * ExrSamples.BigWidth], 0.0, 0.001, "unterste Zeile ist Blenders erste");
    }

    /// <summary>
    /// Nur die angeforderten Kanaele landen im Speicher. Bei einer Multilayer-Datei
    /// mit zwanzig Kanaelen ist das der Unterschied zwischen dreissig Megabyte und
    /// dreihundert - und damit kein Feinschliff, sondern die Voraussetzung dafuer,
    /// dass sich eine 4K-Sequenz ueberhaupt oeffnen laesst.
    /// </summary>
    private static void Selective()
    {
        Check.Group("EXR liest gezielt");

        using var stream = new MemoryStream(ExrSamples.ZipHalf());
        var header = ExrHeaderReader.Read(stream);
        var image = ExrReader.Read(stream, header, new[] { "R", "B" });

        Check.That(image.Channels.Count == 2, "nur die zwei angeforderten Kanaele", $"{image.Channels.Count}");
        Check.That(image.Channel("R") is not null, "Rot ist da");
        Check.That(image.Channel("B") is not null, "Blau ist da");
        Check.That(image.Channel("G") is null, "Gruen wurde nicht gelesen");

        // Die uebersprungenen Kanaele duerfen die Zeilenrechnung nicht verschieben.
        Check.Near(image.Channel("B")![0], 4.0, 0.001, "Blau stimmt trotz uebersprungener Kanaele");
        Check.Near(image.Channel("R")![7], ExrSamples.ProbeRed(0, 7), 0.001, "Rot stimmt am Zeilenende");

        // Ein Name, den es nicht gibt, ist kein Fehler: ob eine Datei Alpha fuehrt,
        // entscheidet die Datei.
        using var again = new MemoryStream(ExrSamples.ZipHalf());
        var header2 = ExrHeaderReader.Read(again);
        var partial = ExrReader.Read(again, header2, new[] { "R", "Nichtvorhanden" });
        Check.That(partial.Channels.Count == 1, "unbekannter Kanalname wird ausgelassen");
    }

    private static void Rejections()
    {
        Check.Group("EXR lehnt ab, was sie nicht lesen kann");

        Check.Throws<ExrFormatException>(
            () => ExrHeaderReader.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })),
            "falsche Kennung");

        Check.Throws<ExrFormatException>(
            () => ExrHeaderReader.Read(new MemoryStream(Array.Empty<byte>())),
            "leere Datei");

        // Das Kachel-Bit im Versionsfeld. Eine gekachelte Datei hat einen anderen
        // Aufbau; sie anzufangen und mitten in der Offset-Tabelle zu scheitern waere
        // die schlechtere Antwort.
        byte[] tiled = ExrSamples.ZipHalf();
        tiled[5] |= 0x02;
        Check.Throws<ExrFormatException>(
            () => ExrHeaderReader.Read(new MemoryStream(tiled)),
            "gekachelte Datei");

        // PIZ wird erkannt, aber nicht ausgepackt - und das muss am Kopf bereits
        // ablesbar sein, bevor Speicher fuer die Pixel bereitsteht.
        byte[] piz = ExrSamples.ZipHalf();
        int at = FindCompressionByte(piz);
        Check.That(at > 0, "Kompressionsbyte gefunden");
        if (at > 0)
        {
            piz[at] = (byte)ExrCompression.Piz;
            using var stream = new MemoryStream(piz);
            var header = ExrHeaderReader.Read(stream);

            Check.That(header.Compression == ExrCompression.Piz, "PIZ wird als PIZ erkannt");
            Check.That(!header.IsSupportedCompression, "PIZ meldet sich als nicht lesbar");
            Check.Throws<ExrFormatException>(() => ExrReader.Read(stream, header), "PIZ wird abgelehnt");
        }

        // Abgeschnittene Datei: der haeufigste Fall ueberhaupt, weil Blender die
        // Datei womoeglich gerade erst schreibt.
        byte[] full = ExrSamples.ZipHalf();
        byte[] cut = full[..(full.Length - 40)];
        Check.Throws<ExrFormatException>(
            () =>
            {
                using var stream = new MemoryStream(cut);
                var header = ExrHeaderReader.Read(stream);
                ExrReader.Read(stream, header);
            },
            "abgeschnittene Datei");
    }

    /// <summary>Sucht das Wertbyte des compression-Attributs im Kopf.</summary>
    private static int FindCompressionByte(byte[] data)
    {
        // "compression\0compression\0" + int32 Laenge, dann folgt das eine Wertbyte.
        byte[] pattern = System.Text.Encoding.ASCII.GetBytes("compression\0compression\0");

        for (int i = 0; i + pattern.Length + 5 < data.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < pattern.Length && hit; j++)
                if (data[i + j] != pattern[j]) hit = false;

            if (hit) return i + pattern.Length + 4;
        }

        return -1;
    }

    /// <summary>
    /// Die Umrechnungstabelle Half nach Float. Sie hat 65536 Eintraege und wird
    /// einmal gebaut; ein Fehler darin fiele in den Bildwerten nur an den Stellen
    /// auf, die gerade betroffen sind.
    /// </summary>
    private static void HalfConversion()
    {
        Check.Group("Half nach Float");

        using var stream = new MemoryStream(ExrSamples.ZipHalf());
        var header = ExrHeaderReader.Read(stream);
        var image = ExrReader.Read(stream, header);

        var b = image.Channel("B")!;

        // 4,0 ist in Half exakt darstellbar - hier darf nichts gerundet werden.
        Check.That(b[0] == 4.0f, "Ueberstrahlung kommt exakt an", $"{b[0]}");
        Check.That(b[1] == 0.25f, "0,25 kommt exakt an", $"{b[1]}");

        // Und die Kernaussage des ganzen Formats: der Wert liegt ueber Weiss und
        // wird nicht unterwegs beschnitten.
        Check.That(image.Channel("B")!.Max() > 1.0f, "Werte oberhalb von 1 ueberleben");
    }

    // ----------------------------------------------------------- Kanalauswahl

    /// <summary>
    /// Welche Kanaele das Bild ergeben. Die Namen stammen aus echten Dateien: eine
    /// schmucklose EXR, ein Blender-Multilayer, und einer mit umbenannter
    /// Ansichtsebene - denn diesen Namen vergibt der Anwender frei.
    /// </summary>
    private static void ChannelPicking()
    {
        Check.Group("EXR-Kanalauswahl");

        var plain = ExrChannelPick.Colour(new[] { "A", "B", "G", "R" });
        Check.That(plain.Red == "R" && plain.Green == "G" && plain.Blue == "B",
                   "schmucklose Datei: R, G, B");
        Check.That(plain.HasAlpha, "und Alpha, wenn vorhanden");
        Check.That(plain.Layer is null, "ohne Ebenennamen");

        var noAlpha = ExrChannelPick.Colour(new[] { "B", "G", "R" });
        Check.That(noAlpha.IsComplete && !noAlpha.HasAlpha, "ohne Alpha ist auch vollstaendig");

        // Ein Multilayer-Render, wie Blender 4.5 ihn schreibt: 24 Kanaele, und das
        // sichtbare Bild steckt in "Combined" - nicht im ersten, das alphabetisch
        // kommt.
        var multilayer = ExrChannelPick.Colour(new[]
        {
            "ViewLayer.Combined.A", "ViewLayer.Combined.B", "ViewLayer.Combined.G", "ViewLayer.Combined.R",
            "ViewLayer.Depth.Z",
            "ViewLayer.DiffCol.B", "ViewLayer.DiffCol.G", "ViewLayer.DiffCol.R",
            "ViewLayer.DiffDir.B", "ViewLayer.DiffDir.G", "ViewLayer.DiffDir.R",
            "ViewLayer.Normal.X", "ViewLayer.Normal.Y", "ViewLayer.Normal.Z",
        });

        Check.That(multilayer.Layer == "ViewLayer.Combined",
                   "Multilayer: die Combined-Ebene gewinnt", multilayer.Layer ?? "(keine)");
        Check.That(multilayer.Red == "ViewLayer.Combined.R", "und liefert ihr Rot");
        Check.That(multilayer.HasAlpha, "samt Alpha");

        // Dieselbe Datei mit umbenannter Ebene. Wer auf "ViewLayer" prueft, faellt
        // hier herein - der Name ist frei vergeben.
        var renamed = ExrChannelPick.Colour(new[]
        {
            "Vordergrund.Combined.A", "Vordergrund.Combined.B",
            "Vordergrund.Combined.G", "Vordergrund.Combined.R",
            "Vordergrund.Depth.Z",
        });

        Check.That(renamed.Layer == "Vordergrund.Combined",
                   "umbenannte Ansichtsebene wird gefunden", renamed.Layer ?? "(keine)");

        // Ohne Combined: irgendeine vollstaendige Ebene ist besser als nichts.
        var noCombined = ExrChannelPick.Colour(new[]
        {
            "ViewLayer.Depth.Z", "ViewLayer.DiffCol.B", "ViewLayer.DiffCol.G", "ViewLayer.DiffCol.R",
        });

        Check.That(noCombined.IsComplete && noCombined.Layer == "ViewLayer.DiffCol",
                   "ohne Combined wird eine vollstaendige Ebene genommen");

        // Eine reine Tiefendatei: ein Kanal, als Graustufe.
        var single = ExrChannelPick.Colour(new[] { "Z" });
        Check.That(single.Red == "Z" && single.Green == "Z" && single.Blue == "Z",
                   "ein einzelner Kanal wird zur Graustufe");

        // Nichts Brauchbares darf nicht in eine halbe Auswahl muenden.
        var nothing = ExrChannelPick.Colour(new[] { "ViewLayer.Depth.Z", "ViewLayer.Normal.X" });
        Check.That(!nothing.IsComplete, "unvollstaendige Datei ergibt keine Auswahl");
    }

    // ---------------------------------------------------------------- Decoder

    /// <summary>
    /// Der Decoder in der Rolle, die der Ringpuffer von ihm erwartet: Pfad hinein,
    /// Bgra32 heraus. Geprueft wird mit der Sichtumwandlung "Standard", damit das
    /// Ergebnis ohne Blender-Installation nachrechenbar bleibt.
    /// </summary>
    private static void Decoder()
    {
        Check.Group("EXR-Decoder");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-exr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            string probe = Path.Combine(folder, "probe.exr");
            File.WriteAllBytes(probe, ExrSamples.ZipHalf());

            var decoder = new ExrFrameDecoder(() => new StandardViewTransform());

            Check.That(decoder.SupportedExtensions.Contains(".exr"), "meldet sich fuer .exr zustaendig");

            Check.That(decoder.TryProbeSize(probe, out int width, out int height), "Groesse ohne Dekodieren");
            Check.That(width == ExrSamples.ProbeWidth && height == ExrSamples.ProbeHeight,
                       "und sie stimmt", $"{width}x{height}");

            Check.That(decoder.TryProbeInfo(probe, out var info), "Kopfdaten fuer die Anzeige");
            Check.That(info.BitsPerChannel == 16, "16 Bit je Kanal", $"{info.BitsPerChannel}");

            byte[]? allocated = null;
            byte[] Allocate(int count) => allocated = new byte[count];

            Check.That(decoder.TryDecode(probe, 4096, 4096, Allocate, out var frame), "dekodiert");
            Check.That(frame.Width == 8 && frame.Height == 4, "volle Groesse", $"{frame.Width}x{frame.Height}");
            Check.That(frame.Stride == 32, "Bgra32 ergibt vier Byte je Pixel", $"{frame.Stride}");
            Check.That(ReferenceEquals(frame.Pixels, allocated), "nimmt den gereichten Puffer");

            // Blau steht in der ersten Spalte auf 4,0. Mit "Standard" laeuft das auf
            // Weiss - und genau das ist der Grund, warum es AgX gibt.
            Check.That(frame.Pixels[0] == 255, "Ueberstrahlung wird zu Weiss", $"{frame.Pixels[0]}");

            // Gruen liegt ueberall auf 0,5 linear; in sRGB sind das 188.
            int green = frame.Pixels[1];
            Check.That(Math.Abs(green - 188) <= 1, "0,5 linear wird zu 188 in sRGB", $"{green}");

            // Alpha ist in der Probe durchgehend 1.
            Check.That(frame.Pixels[3] == 255, "Alpha kommt durch");

            // Verkleinern: Die Datei ist 8x4, mehr als 4x2 darf nicht herauskommen.
            Check.That(decoder.TryDecode(probe, 4, 2, Allocate, out var small), "dekodiert verkleinert");
            Check.That(small.Width <= 4 && small.Height <= 2,
                       "haelt die Obergrenze ein", $"{small.Width}x{small.Height}");
            Check.That(small.Width >= 1 && small.Height >= 1, "und bleibt ein Bild");

            // Eine Datei, die keine ist, darf nicht werfen - der Decoder laeuft auf
            // einem Hintergrundthread, und dort waere eine Ausnahme das Ende.
            string broken = Path.Combine(folder, "kaputt.exr");
            File.WriteAllBytes(broken, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            Check.That(!decoder.TryDecode(broken, 4096, 4096, Allocate, out _), "kaputte Datei: false statt Ausnahme");
            Check.That(!decoder.TryProbeSize(broken, out _, out _), "und auch beim Groessenlesen");

            Check.That(!decoder.TryDecode(Path.Combine(folder, "gibtsnicht.exr"), 4096, 4096, Allocate, out _),
                       "fehlende Datei: false statt Ausnahme");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Der Weg, den die Wiedergabe wirklich geht: ueber die Registrierung. Ohne
    /// diese Pruefung koennte der Decoder fehlerfrei sein und trotzdem nie
    /// aufgerufen werden.
    /// </summary>
    private static void Registration()
    {
        Check.Group("EXR in der Decoder-Registrierung");

        // Ohne Blender-Pfad, damit die Pruefung keine Installation voraussetzt und
        // nicht 2,2 MB Tabelle laedt.
        var registry = FrameDecoderRegistry.CreateDefault(() => null);

        Check.That(registry.IsSupported(".exr"), "die Registrierung kennt .exr");
        Check.That(registry.IsSupported(".EXR"), "und zwar unabhaengig von der Schreibweise");
        Check.That(registry.For(".exr") is ExrFrameDecoder, "und liefert den EXR-Decoder");

        // Die vorhandenen Formate duerfen dabei nicht verlorengehen.
        Check.That(registry.For(".png") is WicFrameDecoder, "PNG geht weiter an WIC");
        Check.That(registry.IsSupported(".jpg"), "JPEG ist weiterhin dabei");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-reg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            string path = Path.Combine(folder, "bild.exr");
            File.WriteAllBytes(path, ExrSamples.ZipHalf());

            Check.That(registry.TryProbeSize(path, out int w, out int h) && w == 8 && h == 4,
                       "die Registrierung liest die Groesse einer EXR", $"{w}x{h}");

            Check.That(registry.TryProbeInfo(path, out var info) && info.BitsPerChannel == 16,
                       "und die Farbtiefe", $"{info.BitsPerChannel} Bit");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }
}
