// Prüfstand: FrameFlips echter WatchService gegen einen Leuchtturm.
//
// Bewusst der Dienst selbst und kein Nachbau. Eine frühere Fassung schickte die
// Nachrichten von Hand über RelayClient - und prüfte damit alles außer der Logik,
// auf die es ankommt: wann Plätze aufgehen, wann sie wieder zugehen, und dass nie
// an einen leeren Platz gesendet wird.
//
// Das Bild entsteht hier in der Datei. Der Prüfstand fasst nie die Medien des
// Benutzers an - deshalb bekommt WatchService den Pfad als Funktion und nicht die
// Merkliste des Projekts.

using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Remote;
using FrameFlip.Web;

string relay = args.Length > 0 ? args[0] : "127.0.0.1:8080";
string code = args.Length > 1 ? args[1] : "geheim123";

var key = WatchKey.Create(code);

// Ein eigenes Bild, damit nichts vom Rechner des Benutzers das Haus verlässt.
string bild = Path.Combine(Path.GetTempPath(), "frameflip-pruefbild.jpg");
File.WriteAllBytes(bild, TestBild());

Console.WriteLine("SECRET=" + key.Text);
Console.WriteLine("CODE=" + code);
Console.WriteLine("MAXSEATS=" + key.OpenSeats);
Console.WriteLine("JPEG=" + new FileInfo(bild).Length);

// Drei Sekunden statt dreißig: Eine Prüfung, die eine halbe Minute darauf wartet,
// dass ein Platz zugeht, wäre keine Prüfung, sondern eine Geduldsprobe.
var dienst = new WatchService(key, relay, null, () => null, () => bild, TimeSpan.FromSeconds(3));

dienst.Changed += () =>
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"STAND offen={dienst.OpenSeats} zusehend={dienst.Watchers} hoechstens={dienst.MaxSeats}"));

dienst.Start();

Console.WriteLine("BEREIT");
Console.Out.Flush();

await Task.Delay(Timeout.Infinite);

// Ein Bild in Renderformat - an einer Briefmarke ließe sich nichts nachmessen.
static byte[] TestBild()
{
    const int w = 1280, h = 720;
    var pixels = new byte[w * h * 3];

    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 3;
            pixels[i] = (byte)(x * 255 / w);
            pixels[i + 1] = (byte)(y * 255 / h);
            pixels[i + 2] = 120;
        }
    }

    var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, pixels, w * 3);

    var encoder = new JpegBitmapEncoder { QualityLevel = 70 };
    encoder.Frames.Add(BitmapFrame.Create(source));

    using var buffer = new MemoryStream();
    encoder.Save(buffer);

    return buffer.ToArray();
}
