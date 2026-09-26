using System.Buffers.Binary;
using System.IO;
using System.Text;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;

namespace FrameFlip.Tests;

/// <summary>
/// Mehrteilige EXR, wie Blender 5.2 sie mit allen Paessen schreibt: je Pass ein eigener
/// Teil, Kanaele wie "Depth.V" und "Normal.X", das Manifest der Kryptomatten im Kopf des
/// ersten Teils. Vorher lehnte der Leser jede solche Datei ab - "kann nicht gelesen werden".
///
/// Die Probedatei wird hier gebaut, nach der Beschreibung des Formats: ein Kopf je Teil,
/// eine Offset-Tabelle je Teil, und jeder Block beginnt mit der Nummer seines Teils.
/// Dazu ein gekachelter Teil in der Mitte, den der Leser ueberspringen, dessen Tabelle er
/// aber mitzaehlen muss, und zwei Teile mit denselben Kanalnamen wie bei einer
/// Stereo-Datei. Die Bloecke liegen verschraenkt in der Datei.
/// </summary>
public static class ExrMultipartInvariants
{
    private const int W = 8, H = 5;

    /// <summary>Ein Teil der Probedatei: Name, Art, Kanaele und der Wert je Kanal und Bildpunkt.</summary>
    internal sealed record Part(string Name, string Type, string[] Channels, Func<int, int, int, float> Value,
                               IReadOnlyDictionary<string, string>? Extra = null);

    public static void Run()
    {
        Check.Group("EXR: mehrteilige Dateien");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-multipart-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "render_0001.exr");

        // Werte, an denen sich jeder Kanal jedes Teils erkennen laesst.
        static float Code(int part, int channel, int x, int y) => part * 1000 + channel * 100 + y * 10 + x;

        var manifest = """{"Kugel":"3f800000","Boden":"40000000"}""";

        var parts = new[]
        {
            new Part("Alpha", "scanlineimage", new[] { "Alpha.V" }, (c, x, y) => Code(0, c, x, y),
                     new Dictionary<string, string>
                     {
                         ["cryptomatte/abc1234/name"] = "CryptoObject",
                         ["cryptomatte/abc1234/hash"] = "MurmurHash3_32",
                         ["cryptomatte/abc1234/conversion"] = "uint32_to_float32",
                         ["cryptomatte/abc1234/manifest"] = manifest,
                     }),
            new Part("Depth", "scanlineimage", new[] { "Depth.V" }, (c, x, y) => Code(1, c, x, y)),
            new Part("Kacheln", "tiledimage", new[] { "Kacheln.V" }, (c, x, y) => 0),
            new Part("Diffuse Direct", "scanlineimage",
                     new[] { "Diffuse Direct.A", "Diffuse Direct.B", "Diffuse Direct.G", "Diffuse Direct.R" },
                     (c, x, y) => Code(3, c, x, y)),
            new Part("Normal", "scanlineimage", new[] { "Normal.X", "Normal.Y", "Normal.Z" }, (c, x, y) => Code(4, c, x, y)),
            new Part("CryptoObject00", "scanlineimage",
                     new[] { "CryptoObject00.a", "CryptoObject00.b", "CryptoObject00.g", "CryptoObject00.r" },
                     (c, x, y) => c == 3 ? (x < W / 2 ? 1f : 2f) : c == 2 ? 1f : 0f),
            new Part("links", "scanlineimage", new[] { "B", "G", "R" }, (c, x, y) => Code(6, c, x, y)),
            new Part("rechts", "scanlineimage", new[] { "B", "G", "R" }, (c, x, y) => Code(7, c, x, y)),
        };

        File.WriteAllBytes(path, Build(parts));

        try
        {
            ExrHeader header;
            using (var stream = ExrReader.Open(path))
            {
                header = ExrHeaderReader.Read(stream);
            }

            var names = header.Channels.Select(c => c.Name).ToList();

            Check.That(header.DataWindow.Width == W && header.DataWindow.Height == H, "der Kopf nennt die Groesse des Bildes");
            Check.That(names.Contains("Depth.V") && names.Contains("Normal.Z") && names.Contains("CryptoObject00.r") && names.Contains("Alpha.V"),
                       "alle Teile stehen in einer Kanalliste", string.Join(", ", names));
            Check.That(!names.Any(n => n.StartsWith("Kacheln", StringComparison.Ordinal)),
                       "der gekachelte Teil wird uebersprungen - nicht geraten");
            Check.That(names.Contains("R") && names.Contains("rechts.R") && !names.Contains("links.R"),
                       "gleiche Kanalnamen in zwei Teilen: der erste behaelt seine, der zweite bekommt den Teilnamen davor");

            // Gelesen wird gezielt - aus drei Teilen, hinter dem gekachelten.
            var wanted = new[] { "Depth.V", "Normal.Y", "rechts.G", "R" };
            ExrImage image;
            using (var stream = ExrReader.Open(path))
            {
                image = ExrReader.Read(stream, ExrHeaderReader.Read(stream), wanted);
            }

            bool Matches(string channel, int part, int index)
                => image.Channel(channel) is { } values &&
                   Enumerable.Range(0, W * H).All(i => values[i] == Code(part, index, i % W, i / W));

            Check.That(image.Channels.Count == 4, "nur die gewollten Kanaele im Speicher", string.Join(", ", image.Channels.Keys));
            Check.That(Matches("Depth.V", 1, 0) && Matches("Normal.Y", 4, 1), "jeder Kanal aus seinem Teil, Punkt fuer Punkt");
            Check.That(Matches("R", 6, 2) && Matches("rechts.G", 7, 1), "und die beiden gleichnamigen Teile auseinandergehalten");

            // Was die Anwendung daraus macht: Farbe, Paesse, Kryptomatte, Bild.
            var colour = ExrChannelPick.Colour(header);
            Check.That(colour.IsComplete && colour.Red == "R", "die Farbe kommt aus dem Teil mit R, G und B", $"{colour.Layer} {colour.Red}");

            var passes = ExrPasses.Of(path);
            Check.That(passes.Any(p => p.Name == "Depth" && p.Grey) && passes.Any(p => p.Name == "Normal") &&
                       passes.Any(p => p.Name == "Diffuse Direct" && p.Alpha is not null),
                       "die Paesse erscheinen unter ihren Namen", string.Join(", ", passes.Select(p => p.Name)));

            var sets = Cryptomatte.Of(path);
            Check.That(sets.Count == 1 && sets[0].Prefix == "CryptoObject" && sets[0].Names.Count == 2,
                       "die Kryptomatte findet ihr Manifest im Kopf des ersten Teils",
                       string.Join(", ", sets.Select(s => $"{s.Prefix}:{s.Names.Count}")));

            var frame = FloatFrame.FromExr(path);
            var depth = FloatFrame.FromExrPass(path, "Depth");
            Check.That(frame is not null && frame.Width == W && frame.R[W + 3] == Code(6, 2, 3, 1),
                       "das Atelier liest das Bild");
            Check.That(depth is not null && depth.R[2 * W + 5] == Code(1, 0, 5, 2), "und einen Pass daraus");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Schreibt eine mehrteilige, unkomprimierte Scanline-EXR mit float-Kanaelen. Der
    /// gekachelte Teil bekommt einen Kopf und eine Tabelle, aber keinen lesbaren Inhalt -
    /// der Leser darf ihn nicht anfassen.
    /// </summary>
    internal static byte[] Build(Part[] parts)
    {
        using var file = new MemoryStream();
        var writer = new BinaryWriter(file);

        writer.Write(20000630);
        writer.Write(2 | 0x1000);

        void Name(string text)
        {
            writer.Write(Encoding.UTF8.GetBytes(text));
            writer.Write((byte)0);
        }

        void Attribute(string name, string type, byte[] value)
        {
            Name(name);
            Name(type);
            writer.Write(value.Length);
            writer.Write(value);
        }

        byte[] Box(int x0, int y0, int x1, int y1)
        {
            var box = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(box, x0);
            BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(4), y0);
            BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(8), x1);
            BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(12), y1);
            return box;
        }

        byte[] Int(int value) => BitConverter.GetBytes(value);

        foreach (var part in parts)
        {
            using var list = new MemoryStream();
            foreach (string channel in part.Channels)
            {
                list.Write(Encoding.UTF8.GetBytes(channel));
                list.WriteByte(0);
                list.Write(Int(2));                    // float
                list.Write(new byte[4]);               // pLinear und Fuellbytes
                list.Write(Int(1));
                list.Write(Int(1));
            }
            list.WriteByte(0);

            Attribute("channels", "chlist", list.ToArray());
            Attribute("chunkCount", "int", Int(H));
            Attribute("compression", "compression", new byte[] { 0 });
            Attribute("dataWindow", "box2i", Box(0, 0, W - 1, H - 1));
            Attribute("displayWindow", "box2i", Box(0, 0, W - 1, H - 1));
            Attribute("lineOrder", "lineOrder", new byte[] { 0 });
            Attribute("name", "string", Encoding.UTF8.GetBytes(part.Name));
            Attribute("pixelAspectRatio", "float", BitConverter.GetBytes(1f));
            if (part.Type == "tiledimage")
            {
                var tiles = new byte[9];
                BinaryPrimitives.WriteInt32LittleEndian(tiles, W);
                BinaryPrimitives.WriteInt32LittleEndian(tiles.AsSpan(4), 1);
                Attribute("tiles", "tiledesc", tiles);
            }
            Attribute("type", "string", Encoding.UTF8.GetBytes(part.Type));

            foreach (var (key, value) in part.Extra ?? new Dictionary<string, string>())
                Attribute(key, "string", Encoding.UTF8.GetBytes(value));

            writer.Write((byte)0);                      // Ende dieses Kopfes
        }

        writer.Write((byte)0);                          // Ende aller Koepfe

        // Die Tabellen: je Teil H Eintraege. Erst Platz, die Werte kommen nach den Bloecken.
        long table = file.Position;
        for (int i = 0; i < parts.Length * H; i++) writer.Write(0L);

        var offsets = new long[parts.Length, H];

        // Verschraenkt: Zeile fuer Zeile, darin Teil fuer Teil - rueckwaerts, damit die
        // Reihenfolge in der Datei nichts mit der Nummer des Teils zu tun hat.
        for (int y = 0; y < H; y++)
        {
            for (int p = parts.Length - 1; p >= 0; p--)
            {
                offsets[p, y] = file.Position;

                writer.Write(p);
                if (parts[p].Type == "tiledimage")
                {
                    // Eine Kachel haette hier Koordinaten; der Leser soll sie gar nicht lesen.
                    writer.Write(new byte[16]);
                    continue;
                }

                writer.Write(y);
                writer.Write(parts[p].Channels.Length * W * 4);

                for (int c = 0; c < parts[p].Channels.Length; c++)
                    for (int x = 0; x < W; x++)
                        writer.Write(parts[p].Value(c, x, y));
            }
        }

        file.Position = table;
        for (int p = 0; p < parts.Length; p++)
            for (int y = 0; y < H; y++)
                writer.Write(offsets[p, y]);

        writer.Flush();
        return file.ToArray();
    }
}
