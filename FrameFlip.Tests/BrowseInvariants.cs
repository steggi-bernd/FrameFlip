using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Projects;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Die Antworten an das Handy.
///
/// Der Dienst bekommt eine Absendefunktion statt einer Verbindung - damit laesst
/// sich hier pruefen, was sonst nur mit einem Handy in der Hand zu sehen waere: dass
/// auf JEDE Anfrage etwas zurueckkommt, dass eine Ablehnung einen Grund traegt, und
/// dass die Ablehnung auch dann kommt, wenn die Freigabe aus ist.
///
/// Das Letzte ist der eigentliche Punkt. Ein Tor, das bei geschlossenem Zustand
/// einfach schweigt, sieht in der App aus wie eine haengende Verbindung - und genau
/// dieser Fehler stand hier schon einmal als "Bild wird geholt" auf dem Bildschirm.
/// </summary>
public static class BrowseInvariants
{
    public static void Run()
    {
        Check.Group("Bibliothek - die Antworten an das Handy");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-antwort-" + Guid.NewGuid().ToString("N")[..8]);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TRACER", "render"));
            Directory.CreateDirectory(Path.Combine(root, "geheim"));

            string blend = Path.Combine(root, "TRACER", "tracer.blend");
            string frame = Path.Combine(root, "TRACER", "render", "cam1_0001.png");
            string later = Path.Combine(root, "TRACER", "render", "cam1_9.png");
            string tenth = Path.Combine(root, "TRACER", "render", "cam1_10.png");
            string movie = Path.Combine(root, "TRACER", "render", "schnitt.mp4");
            string note = Path.Combine(root, "TRACER", "render", "notiz.txt");

            File.WriteAllText(blend, "x");
            File.WriteAllBytes(frame, Png());
            File.WriteAllBytes(later, Png());
            File.WriteAllBytes(tenth, Png());
            File.WriteAllText(movie, "x");
            File.WriteAllText(note, "x");

            // Die Merkliste zeigt auf diesen Ordner - der Dienst liest sie selbst.
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));
            ProjectLibrary.AddFolder(root);

            var settings = new AppSettings { LibraryAccessEnabled = false };
            var sent = new List<byte[]>();

            using var service = new BrowseService(() => settings, sent.Add);

            // ---------------------------------------------------------- zu

            Ask(service, """{"c":"projects"}""");

            Check.That(sent.Count == 1, "auch die Ablehnung ist eine Antwort", sent.Count.ToString());

            var refused = Json(sent[^1]);

            Check.That(refused.GetProperty("t").GetString() == "projects", "sie sagt, worauf sie antwortet");
            Check.That(!refused.GetProperty("ok").GetBoolean(), "und dass es nicht geht");
            Check.That(refused.GetProperty("why").GetString()!.Length > 10, "mit einem Grund im Klartext",
                       refused.GetProperty("why").GetString());

            sent.Clear();

            // Auch die anderen Befehle schweigen nicht.
            foreach (string command in new[]
                     {
                         """{"c":"folder","p":"C:\\"}""",
                         $$"""{"c":"view","p":{{JsonSerializer.Serialize(frame)}}}""",
                         $$"""{"c":"fetch","p":{{JsonSerializer.Serialize(blend)}}}""",
                         $$"""{"c":"seq","p":{{JsonSerializer.Serialize(Path.Combine(root, "TRACER", "render"))}}}""",
                     })
            {
                sent.Clear();
                Ask(service, command);

                Check.That(sent.Count == 1 && !Json(sent[0]).GetProperty("ok").GetBoolean(),
                           $"abgelehnt statt verschwiegen: {command[..Math.Min(24, command.Length)]}…");
            }

            // ---------------------------------------------------------- auf

            settings.LibraryAccessEnabled = true;
            sent.Clear();

            Ask(service, """{"c":"projects"}""");

            var projects = Json(sent[^1]);

            Check.That(projects.GetProperty("ok").GetBoolean(), "mit Freigabe kommt die Liste");

            var items = projects.GetProperty("items").EnumerateArray().ToList();

            Check.That(items.Count == 1, "ein Projektordner, ein Projekt", items.Count.ToString());
            Check.That(items[0].GetProperty("n").GetString() == "TRACER", "und es heisst wie der Ordner",
                       items[0].GetProperty("n").GetString());

            // Blaettern.
            sent.Clear();
            Ask(service, $$"""{"c":"folder","p":{{JsonSerializer.Serialize(Path.Combine(root, "TRACER"))}}}""");

            var folder = Json(sent[^1]);

            Check.That(folder.GetProperty("ok").GetBoolean(), "in den Projektordner kommt man");
            Check.That(folder.GetProperty("dirs").EnumerateArray().Any(d => d.GetProperty("n").GetString() == "render"),
                       "der Unterordner steht dabei");
            Check.That(folder.GetProperty("files").EnumerateArray().Count() == 1,
                       "und die .blend als Datei",
                       folder.GetProperty("files").EnumerateArray().Count().ToString());

            sent.Clear();
            Ask(service, $$"""{"c":"folder","p":{{JsonSerializer.Serialize(Path.Combine(root, "TRACER", "render"))}}}""");

            var renders = Json(sent[^1]);
            var files = renders.GetProperty("files").EnumerateArray().ToList();

            Check.That(files.Count == 4, "drei Bilder und ein Video, aber nicht die Textdatei",
                       files.Count.ToString());
            Check.That(files.Any(f => f.GetProperty("u").GetString() == "image"), "das Bild ist als Bild ausgewiesen");
            Check.That(files.Any(f => f.GetProperty("u").GetString() == "video"), "das Video als Video");
            Check.That(files.All(f => !f.GetProperty("n").GetString()!.EndsWith(".txt")),
                       "und die Notiz taucht nicht auf");

            Check.That(renders.GetProperty("up").GetString() is not null, "von hier fuehrt ein Weg nach oben");

            // Aus der Bibliothek heraus fuehrt keiner.
            sent.Clear();
            Check.Group("Bibliothek - eine Folge zum Abspielen");

            string render = Path.Combine(root, "TRACER", "render");

            sent.Clear();
            Ask(service, $$"""{"c":"seq","p":{{JsonSerializer.Serialize(render)}}}""");

            var sequence = Json(sent[^1]);

            Check.That(sequence.GetProperty("t").GetString() == "seq"
                       && sequence.GetProperty("ok").GetBoolean(),
                       "ein Ordner mit Bildern ergibt eine Folge");

            var names = sequence.GetProperty("items").EnumerateArray().Select(x => x.GetString()).ToList();

            Check.That(names.Count == 3, "alle drei Bilder stehen darin", names.Count.ToString());

            Check.That(string.Join(" ", names) == "cam1_0001.png cam1_9.png cam1_10.png",
                       "und zwar in der Reihenfolge, in der sie ein Film sind",
                       string.Join(" ", names));

            Check.That(!names.Any(n => n!.Contains(Path.DirectorySeparatorChar)),
                       "es sind Namen, keine Pfade - der Ordner steht einmal daneben");

            Check.That(sequence.GetProperty("p").GetString() == render
                       && sequence.GetProperty("sep").GetString() == Path.DirectorySeparatorChar.ToString(),
                       "mit Ordner und Trennzeichen, damit die App nicht raet");

            Check.That(sequence.GetProperty("total").GetInt32() == 3, "und der vollen Zahl");

            // Ein Bild statt des Ordners: Wer auf einen Frame tippt, meint die Folge,
            // in der er steht.
            sent.Clear();
            Ask(service, $$"""{"c":"seq","p":{{JsonSerializer.Serialize(later)}}}""");

            Check.That(Json(sent[^1]).GetProperty("ok").GetBoolean()
                       && Json(sent[^1]).GetProperty("p").GetString() == render,
                       "ein Frame fuehrt zu seinem eigenen Ordner");

            // Ein Ordner ohne Bilder ist eine Antwort, kein Schweigen.
            sent.Clear();
            Ask(service, $$"""{"c":"seq","p":{{JsonSerializer.Serialize(Path.Combine(root, "geheim"))}}}""");

            Check.That(sent.Count == 1 && !Json(sent[0]).GetProperty("ok").GetBoolean()
                       && Json(sent[0]).GetProperty("why").GetString()!.Contains("no images"),
                       "ein leerer Ordner sagt, dass er leer ist");

            sent.Clear();
            Ask(service, """{"c":"seq","p":"C:\\Windows"}""");

            Check.That(sent.Count == 1 && !Json(sent[0]).GetProperty("ok").GetBoolean(),
                       "und ausserhalb der Bibliothek gibt es keine Folge");

            Check.Group("Bibliothek - die Antworten an das Handy");

            sent.Clear();
            Ask(service, """{"c":"folder","p":"C:\\Windows"}""");

            Check.That(!Json(sent[^1]).GetProperty("ok").GetBoolean(), "ein fremder Ordner wird abgelehnt");

            // Ansehen: ein echtes PNG kommt als Bild zurueck, nicht als JSON.
            sent.Clear();
            Ask(service, $$"""{"c":"view","p":{{JsonSerializer.Serialize(frame)}},"w":320}""");

            Check.That(sent.Count == 1, "auf Ansehen kommt genau eine Antwort", sent.Count.ToString());
            Check.That(Envelope.TryRead(sent[0], out PayloadKind kind, out byte[] body) && kind == PayloadKind.Image,
                       "und zwar ein Bild");

            Check.That(Envelope.TryReadImage(body, out string shown, out byte[] jpeg)
                       && shown == frame && jpeg.Length > 0,
                       "mit dem Pfad, nach dem gefragt wurde", shown);

            Check.That(jpeg.Length < new FileInfo(frame).Length + 4096,
                       "verkleinert, nicht das Original", $"{jpeg.Length} statt {new FileInfo(frame).Length}");

            // Ein Video laesst sich nicht als Bild ansehen - und das steht auch da.
            sent.Clear();
            Ask(service, $$"""{"c":"view","p":{{JsonSerializer.Serialize(movie)}}}""");

            var noPicture = Json(sent[^1]);

            Check.That(!noPicture.GetProperty("ok").GetBoolean(), "ein Video ist kein Bild");
            Check.That(noPicture.GetProperty("why").GetString()!.Contains("stream"),
                       "und der Grund sagt, was stattdessen geht", noPicture.GetProperty("why").GetString());

            // Holen: Ankuendigung, dann Stuecke.
            sent.Clear();
            Ask(service, $$"""{"c":"fetch","p":{{JsonSerializer.Serialize(blend)}}}""");

            var fetch = Json(sent[0]);

            Check.That(fetch.GetProperty("ok").GetBoolean(), "das Holen wird angekuendigt");
            Check.That(fetch.GetProperty("s").GetInt64() == 1, "mit der Groesse");

            int id = fetch.GetProperty("id").GetInt32();

            Check.That(sent.Count == 2, "und das erste Stueck kommt gleich mit", sent.Count.ToString());
            Check.That(Envelope.TryRead(sent[1], out PayloadKind chunkKind, out _) && chunkKind == PayloadKind.Chunk,
                       "als Stueck");

            sent.Clear();
            Ask(service, $$"""{"c":"ack","id":{{id}},"i":0}""");

            Check.That(sent.Count == 1 && Json(sent[^1]).GetProperty("t").GetString() == "sent",
                       "nach der Quittung ist der Vorgang fertig");

            // Eine Datei ausserhalb der Bibliothek wird nicht geholt.
            sent.Clear();
            Ask(service, """{"c":"fetch","p":"C:\\Windows\\System32\\drivers\\etc\\hosts"}""");

            Check.That(!Json(sent[^1]).GetProperty("ok").GetBoolean(), "und nichts von ausserhalb");

            // Ein Befehl, den es nicht gibt, gehoert nicht hierher.
            Check.That(!BrowseService.Handles("preview"), "die Vorschau bleibt beim Renderteil");
            Check.That(BrowseService.Handles("folder"), "das Blaettern nicht");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);

            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Der Blick in den Kopf einer Filmdatei.
    ///
    /// Die Aussage ist klein, aber sie entscheidet, was am Handy auf dem Bildschirm
    /// steht: "laeuft" oder "erst wenn alles da ist". Falsch waere sie schlimmer als
    /// gar keine - dann wartet jemand auf einen Film, der nie anfaengt, und haelt die
    /// Verbindung fuer kaputt.
    /// </summary>
    public static void Movies()
    {
        Check.Group("Film - faengt er vor dem Ende an");

        // Der Normalfall aus Blender: erst die Bilder, das Verzeichnis hinterher.
        Check.That(MovieProbe.Read(Movie(Box("ftyp", 8), Box("mdat", 400), Box("moov", 60))) == MovieStart.Back,
                   "Verzeichnis hinten heisst: erst wenn alles da ist");

        // Und der Fall, fuer den es faststart gibt.
        Check.That(MovieProbe.Read(Movie(Box("ftyp", 8), Box("moov", 60), Box("mdat", 400))) == MovieStart.Front,
                   "Verzeichnis vorne heisst: laeuft, waehrend es kommt");

        // Ein Kasten ueber vier Gigabyte traegt seine Groesse in acht Bytes
        // dahinter - bei Renderausgaben ist das der Normalfall. Wer die
        // Verlaengerung nicht liest, springt an die falsche Stelle und verliert sich.
        Check.That(MovieProbe.Read(Movie(Box("ftyp", 8), Wide("free", 32), Box("moov", 60)))
                   == MovieStart.Front,
                   "auch ueber die grosse Laengenform hinweg wird richtig gezaehlt");

        // Was kein MP4 ist, bekommt keine geratene Aussage.
        Check.That(MovieProbe.Read(new MemoryStream(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8 }))
                   == MovieStart.Unknown,
                   "ein Matroska sagt hier nichts - und das ist die ehrliche Antwort");

        // Eine Groesse, die nicht vorwaerts fuehrt, waere eine Endlosschleife.
        Check.That(MovieProbe.Read(Movie(Box("ftyp", 8), Box("free", -4))) == MovieStart.Unknown,
                   "eine Groesse, die nicht weiterfuehrt, haelt die Suche an");

        // Ein halber Kopf ist kein Kopf.
        Check.That(MovieProbe.Read(new MemoryStream(new byte[] { 0, 0, 0, 16, (byte)'f' })) == MovieStart.Unknown,
                   "und ein abgeschnittener Anfang auch nicht");

        Check.That(MovieProbe.Word(MovieStart.Front) == "fast" && MovieProbe.Word(MovieStart.Back) == "slow"
                   && MovieProbe.Word(MovieStart.Unknown) == "unknown",
                   "die Auskunft heisst auf der Leitung, was sie heisst");
    }

    /// <summary>Ein Kasten: vier Bytes Groesse, vier Bytes Name, dann Fuellung.</summary>
    private static byte[] Box(string name, int payload)
    {
        int size = 8 + payload;
        var box = new byte[Math.Max(8, size)];

        box[0] = (byte)(size >> 24);
        box[1] = (byte)(size >> 16);
        box[2] = (byte)(size >> 8);
        box[3] = (byte)size;

        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(box, 4);

        return box;
    }

    /// <summary>Ein Kasten in der grossen Form: Groesse 1, die echte steht dahinter.</summary>
    private static byte[] Wide(string name, long size)
    {
        var box = new byte[size];

        box[3] = 1;

        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(box, 4);

        for (int i = 0; i < 8; i++) box[8 + i] = (byte)(size >> (56 - 8 * i));

        return box;
    }

    /// <summary>Die Kaesten hintereinander, mit Fuellung bis zur angegebenen Groesse.</summary>
    private static MemoryStream Movie(params byte[][] boxes)
    {
        var stream = new MemoryStream();

        foreach (byte[] box in boxes) stream.Write(box, 0, box.Length);

        stream.Position = 0;

        return stream;
    }

    private static void Ask(BrowseService service, string json)
    {
        using var document = JsonDocument.Parse(json);

        string command = document.RootElement.GetProperty("c").GetString()!;

        service.Handle(command, document.RootElement);
    }

    private static JsonElement Json(byte[] frame)
    {
        Envelope.TryRead(frame, out _, out byte[] body);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Ein winziges, echtes PNG - damit der Kodierer etwas zu tun hat.</summary>
    private static byte[] Png()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            8, 8, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
