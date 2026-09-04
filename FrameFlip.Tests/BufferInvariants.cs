using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Caching;
using FrameFlip.Decoding;

namespace FrameFlip.Tests;

/// <summary>Invarianten 1 und 2 der Referenzliste.</summary>
public static class BufferInvariants
{
    public static void Run()
    {
        StrideIsTight();
        PoolLendsByCapacity();
        BitmapMatchesFrame();
    }

    // ---------------------------------------------------------------- Invariante 1

    /// <summary>
    /// Jeder Frame aus dem Pool ist dicht gepackt. Geprueft am echten Dekodierweg,
    /// weil genau dort der Stride entsteht.
    /// </summary>
    private static void StrideIsTight()
    {
        Check.Group("Invariante 1 - Stride ist dicht gepackt");

        string path = Path.Combine(Path.GetTempPath(), "frameflip_stride_probe.png");
        WriteProbePng(path, 1920, 1080);

        try
        {
            var pool = new PixelBufferPool();
            var decoder = new WicFrameDecoder();

            // Ungerade Zielbreiten sind der interessante Fall: dort weicht eine
            // ausgerichtete Zeilenlaenge am ehesten von Breite * 4 ab.
            foreach (int target in new[] { 1920, 1233, 801, 640, 317, 97 })
            {
                bool ok = decoder.TryDecode(path, target, target, pool.Rent, out var frame);
                Check.That(ok, $"Dekodieren auf {target} px gelingt");
                if (!ok) continue;

                Check.That(frame.Stride == frame.Width * 4,
                    $"Stride == Breite * 4 bei Zielbreite {target}",
                    $"Stride {frame.Stride}, Breite {frame.Width}");

                Check.That(frame.Pixels.Length >= frame.Stride * frame.Height,
                    $"Puffer fasst die Nutzlast bei Zielbreite {target}",
                    $"{frame.Pixels.Length} Bytes fuer {frame.Stride * frame.Height} noetig");

                // Der FrameBuffer setzt dieselbe Bedingung im Konstruktor durch.
                var buffer = new FrameBuffer(frame.Pixels, frame.Width, frame.Height, frame.Stride, 0);
                Check.That(buffer.ByteCount == frame.Width * 4 * frame.Height,
                    $"Nutzlast folgt der Bildgroesse, nicht der Arraylaenge, bei {target}");
            }

            // Ein widerspruechlicher Stride darf gar nicht erst entstehen koennen -
            // das ist die Absicherung gegen den Rueckfall.
            Check.Throws<ArgumentException>(
                () => new FrameBuffer(new byte[100 * 4 * 10], 100, 10, 128 * 4, 0),
                "FrameBuffer weist einen Stride aus der Poolgroesse zurueck");

            Check.Throws<ArgumentException>(
                () => new FrameBuffer(new byte[10], 100, 10, 400, 0),
                "FrameBuffer weist einen zu kleinen Puffer zurueck");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>Der Pool vergibt nach Kapazitaet, nie nach Geometrie.</summary>
    private static void PoolLendsByCapacity()
    {
        Check.Group("Der Pool vergibt nach Kapazitaet");

        var pool = new PixelBufferPool();
        pool.Configure(1000, 4);

        var a = pool.Rent(1000);
        Check.That(a.Length >= 1000, "Rent liefert mindestens die geforderte Groesse");
        pool.Return(a);

        // Etwas kleinerer Bedarf: der vorhandene Puffer wird wiederverwendet, statt
        // den Pool zu verwerfen. Genau das passiert beim Zoomen staendig.
        var b = pool.Rent(900);
        Check.That(ReferenceEquals(a, b), "leicht kleinerer Bedarf verwendet den Puffer weiter");
        Check.That(b.Length >= 900, "der wiederverwendete Puffer ist gross genug");
        pool.Return(b);

        // Deutlich groesserer Bedarf: neuer Puffer, der alte passt nicht.
        var c = pool.Rent(5000);
        Check.That(!ReferenceEquals(a, c), "deutlich groesserer Bedarf bekommt einen neuen Puffer");
        Check.That(c.Length >= 5000, "der neue Puffer ist gross genug");

        // Ein stark ueberdimensionierter Puffer wird nicht wiederverwendet, sonst
        // bliebe der Speicher nach dem Herauszoomen dauerhaft belegt.
        pool.Configure(1000, 4);
        pool.Return(c);
        var d = pool.Rent(1000);
        Check.That(!ReferenceEquals(c, d), "ein viel zu grosser Puffer wird nicht behalten");
    }

    // ---------------------------------------------------------------- Invariante 2

    /// <summary>
    /// Vor jedem WritePixels hat die Bitmap exakt die Dimension des Frames. Der Test
    /// bildet die Bedingung aus Blit() nach und schreibt tatsaechlich hinein - waere
    /// die Bitmap groesser, blieben Bereiche schwarz.
    /// </summary>
    private static void BitmapMatchesFrame()
    {
        Check.Group("Invariante 2 - Bitmapdimension entspricht dem Frame");

        WriteableBitmap? surface = null;

        // Wechselnde Groessen wie beim Nachschaerfen, inklusive Rueckweg.
        foreach (var (w, h) in new[] { (1232, 693), (1479, 831), (1920, 1080), (1232, 693), (97, 55) })
        {
            int stride = w * 4;

            // Der Pool darf ein groesseres Array liefern - das ist der Fall, den die
            // urspruengliche Fassung nicht sauber getrennt hat.
            var pixels = new byte[stride * h + 4096];
            Array.Fill(pixels, (byte)0xFF);

            var frame = new FrameBuffer(pixels, w, h, stride, 0);

            if (surface is null || surface.PixelWidth != frame.Width || surface.PixelHeight != frame.Height)
                surface = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);

            Check.That(surface.PixelWidth == frame.Width && surface.PixelHeight == frame.Height,
                $"Bitmap {surface.PixelWidth}x{surface.PixelHeight} passt zum Frame {w}x{h}");

            surface.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height),
                                frame.Pixels, frame.Stride, 0);

            // Gegenprobe: die Ecke unten rechts muss beschrieben sein. Genau dort
            // stand im Fehlerfall das Schwarz.
            var corner = new byte[4];
            surface.CopyPixels(new Int32Rect(frame.Width - 1, frame.Height - 1, 1, 1), corner, 4, 0);
            Check.That(corner[0] == 0xFF && corner[1] == 0xFF && corner[2] == 0xFF,
                $"Ecke unten rechts ist beschrieben bei {w}x{h}",
                $"BGRA {corner[0]},{corner[1]},{corner[2]},{corner[3]}");
        }
    }

    // ---------------------------------------------------------------- Hilfsmittel

    private static void WriteProbePng(string path, int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                pixels[i + 0] = (byte)(x % 251);
                pixels[i + 1] = (byte)(y % 241);
                pixels[i + 2] = (byte)((x + y) % 239);
                pixels[i + 3] = 0xFF;
            }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
