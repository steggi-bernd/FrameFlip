// Prüfstand: FrameFlip als Host in den Zuschauerräumen eines lokalen Leuchtturms.
//
// Bewusst ohne NewestFrame und ohne ProjectLibrary - der Prüfstand soll keine
// Bilder des Benutzers anfassen. Was hier hinausgeht, entsteht in dieser Datei.

using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Remote;

string relay = args.Length > 0 ? args[0] : "127.0.0.1:8080";
string code = args.Length > 1 ? args[1] : "geheim123";

var key = WatchKey.Create(code);

Console.WriteLine("SECRET=" + key.Text);
Console.WriteLine("CODE=" + code);
Console.WriteLine("SEATS=" + key.OpenSeats);

for (int seat = 0; seat < key.OpenSeats; seat++)
{
    Console.WriteLine($"ROOM{seat}=" + key.RoomId(seat));

    // Nicht der Schluessel selbst, nur ein Fingerabdruck - er genuegt zum Vergleich.
    byte[] raw = System.Text.Encoding.UTF8.GetBytes(key.Channel(seat).Text);
    Console.WriteLine($"FP{seat}=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw))[..16].ToLowerInvariant());
}

var clients = new List<RelayClient>();

// Wer zusieht - und nur dorthin wird gesendet.
//
// Das ist keine Sparsamkeit, sondern Notwendigkeit: Der Ausgangspuffer haelt 64
// Nachrichten. Wer an einen leeren Platz sendet, staut sie dort auf, und sobald
// jemand hereinkommt, ergiesst sich der ganze Stau auf ihn. Die Warteschlange des
// Leuchtturms fasst 32 - er trennt den Neuankoemmling als "zu langsam", bevor der
// ein einziges Bild gesehen hat. WatchService haelt sich aus demselben Grund daran.
var paired = new bool[key.OpenSeats];

for (int seat = 0; seat < key.OpenSeats; seat++)
{
    var client = new RelayClient(RelayRoom.ForWatching(key, relay, seat));
    int mine = seat;

    client.StateChanged += state =>
    {
        paired[mine] = state == RelayState.Paired;
        Console.WriteLine($"SEAT{mine}={state}");
    };

    // Darf nie feuern: Der Raum hört nicht zu. Feuert es doch, ist die Einbahnstraße
    // keine - und der Prüfstand soll das laut sagen.
    client.PayloadReceived += _ => Console.WriteLine($"LEAK{mine}=eingehende Nutzlast geöffnet!");

    clients.Add(client);
    client.Start();
}

byte[] jpeg = TestBild();
Console.WriteLine("JPEG=" + jpeg.Length);
Console.Out.Flush();

int tick = 0;

while (true)
{
    tick++;

    // Invariant, sonst schreibt eine deutsche Umgebung "5,0" - und das ist kein JSON.
    string percent = (tick * 5.0).ToString("0.0", CultureInfo.InvariantCulture);

    string json = $"{{\"rendering\":true,\"percent\":{percent},\"frame\":{100 + tick}," +
                  "\"first\":100,\"last\":120,\"written\":" + tick + ",\"elapsed\":" + (tick * 3) +
                  ",\"remaining\":42,\"secondsPerFrame\":3.25,\"width\":1920,\"height\":1080," +
                  "\"scene\":\"pruefstand\",\"sample\":16,\"sampleTotal\":128,\"memoryMb\":2048," +
                  "\"cpu\":73.5,\"gpu\":91.0,\"freeMb\":8192,\"totalMb\":32768," +
                  "\"frameId\":\"t" + tick.ToString("x4") + "\"}";

    for (int seat = 0; seat < clients.Count; seat++)
    {
        if (!paired[seat]) continue;

        clients[seat].Send(Envelope.Json(json));
        clients[seat].Send(Envelope.Preview(100 + tick, jpeg));
    }

    await Task.Delay(1000);
}

// Ein eigenes Bild, damit nichts vom Rechner des Benutzers das Haus verlässt.
static byte[] TestBild()
{
    try
    {
        const int w = 160, h = 90;
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
    catch (Exception ex)
    {
        Console.WriteLine("JPEGFEHLER=" + ex.GetType().Name);
        return new byte[512];
    }
}
