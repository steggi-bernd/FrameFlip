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

// Flutmodus: Schiebt so viel durch eine Verbindung, wie die Bremse zulässt, und
// sagt, ob der Leuchtturm sie dabei getrennt hat. Das ist die eigentliche Frage -
// eine Rechnung auf dem Papier beantwortet sie nicht.
if (args.Length > 0 && args[0] == "--flut")
{
    await Fluten(args.Length > 1 ? args[1] : "127.0.0.1:8080");
    return;
}

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

// ---------------------------------------------------------------------- Flutmodus

static async Task Fluten(string relay)
{
    var key = WatchKey.Create(null);

    Console.WriteLine("SECRET=" + key.Text);
    Console.Out.Flush();

    var client = new RelayClient(RelayRoom.ForWatching(key, relay, 0));

    var verbunden = new TaskCompletionSource();
    bool abgerissen = false;

    client.StateChanged += state =>
    {
        Console.WriteLine("STATE=" + state);

        if (state == RelayState.Paired) verbunden.TrySetResult();
        else if (verbunden.Task.IsCompleted) abgerissen = true;
    };

    client.Start();

    await verbunden.Task.WaitAsync(TimeSpan.FromSeconds(20));

    /* In Runden, nicht alles auf einmal.
     *
     * Der Ausgangspuffer fasst 64 Nachrichten und wirft im Ueberlauf die aeltesten
     * weg. Vierhundert Stuecke auf einen Schlag einzureihen wuerde also nur zeigen,
     * dass ein Puffer ueberlaeuft - nicht, ob der Leuchtturm trennt. Vierzig je
     * Runde passen hinein, drei Sekunden Pause reichen der Bremse, sie
     * abzuarbeiten. Zusammen vierzig Megabyte, das Fuenffache des Vorrats den der
     * Relay einraeumt. */
    var stueck = new byte[Envelope.ChunkBytes];
    var begonnen = DateTime.UtcNow;
    int gesendet = 0;

    for (int runde = 0; runde < 8 && !abgerissen; runde++)
    {
        for (int i = 0; i < 40 && !abgerissen; i++)
        {
            client.Send(Envelope.Chunk(1, gesendet, false, stueck));
            gesendet++;
        }

        await Task.Delay(3000);
    }

    double dauer = (DateTime.UtcNow - begonnen).TotalSeconds;

    Console.WriteLine($"GESENDET={gesendet * (long)Envelope.ChunkBytes / 1048576.0:0.0} MiB angereiht");
    Console.WriteLine($"DAUER={dauer:0.0} s");
    Console.WriteLine("ABGERISSEN=" + (abgerissen ? "JA" : "nein"));

    await client.DisposeAsync();
}
