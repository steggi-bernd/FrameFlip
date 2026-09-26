using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FrameFlip.Decoding.Exr;

/// <summary>Die Datei ist keine EXR oder eine, die dieser Reader nicht bedienen kann.</summary>
public sealed class ExrFormatException : Exception
{
    public ExrFormatException(string message) : base(message) { }
}

/// <summary>
/// Liest den Dateikopf. Alles hier ist Little-Endian, unabhaengig von der Maschine -
/// das Format schreibt es so vor, und auf einer Big-Endian-Maschine waere ein blosses
/// BitConverter falsch herum. Deshalb durchgehend BinaryPrimitives.
/// </summary>
public static class ExrHeaderReader
{
    /// <summary>"v/1" mit gesetztem hoechsten Bit - 20000630, das Entstehungsdatum des Formats.</summary>
    private const int Magic = 20000630;

    private const int FlagTiled = 0x0200;
    private const int FlagLongNames = 0x0400;
    private const int FlagNonImage = 0x0800;
    private const int FlagMultiPart = 0x1000;

    /// <summary>
    /// Obergrenze fuer einen Attributnamen. Ohne Long-Name-Bit erlaubt das Format 31
    /// Zeichen; die Grenze steht hier trotzdem grosszuegiger, weil sie nur verhindern
    /// soll, dass eine kaputte Datei den Leser in eine endlose Zeichenkette schickt.
    /// </summary>
    private const int MaxNameLength = 1024;

    /// <summary>
    /// Groesstes Attribut, das der Reader ungesehen in den Speicher nimmt. Ein
    /// Cryptomatte-Manifest liegt bei komplexen Szenen im Megabytebereich, ein
    /// fehlerhaftes Groessenfeld dagegen schnell bei zwei Gigabyte.
    /// </summary>
    private const int MaxAttributeBytes = 64 * 1024 * 1024;

    /// <summary>Obergrenze fuer die Zahl der Teile - gegen eine kaputte Datei, die nicht aufhoert.</summary>
    private const int MaxParts = 4096;

    public static ExrHeader Read(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        ReadExactly(stream, head);

        int magic = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (magic != Magic) throw new ExrFormatException("Keine EXR-Datei - Kennung stimmt nicht.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        int number = version & 0xFF;
        if (number != 2) throw new ExrFormatException($"EXR-Version {number} wird nicht gelesen.");

        bool longNames = (version & FlagLongNames) != 0;

        // Mehrteilig: Blender 5.2 schreibt so, sobald es mehr als das Bild ausgibt - jeder
        // Pass ein eigener Teil. Das Deep-Bit heisst dort nur, dass IRGENDEIN Teil deep ist;
        // die anderen lassen sich trotzdem lesen.
        if ((version & FlagMultiPart) != 0) return ReadParts(stream, longNames);

        // Diese beiden Varianten haben einen anderen Aufbau als eine einteilige
        // Scanline-Datei. Sie hier abzulehnen ist ehrlicher, als sie anzufangen und
        // mitten in der Offset-Tabelle aus dem Tritt zu geraten.
        if ((version & FlagTiled) != 0) throw new ExrFormatException("Gekachelte EXR wird nicht gelesen.");
        if ((version & FlagNonImage) != 0) throw new ExrFormatException("Deep-EXR wird nicht gelesen.");

        var single = ReadAttributes(stream, longNames);

        if (single.DataWindow is null) throw new ExrFormatException("Kein dataWindow im Kopf.");
        if (single.Channels is null || single.Channels.Count == 0) throw new ExrFormatException("Keine Kanaele im Kopf.");
        if (single.Compression is null) throw new ExrFormatException("Keine Kompressionsangabe im Kopf.");
        if (!single.DataWindow.Value.IsValid) throw new ExrFormatException("dataWindow ist leer.");

        foreach (var channel in single.Channels)
        {
            if (!channel.IsFullResolution)
                throw new ExrFormatException($"Kanal '{channel.Name}' ist unterabgetastet.");
        }

        return single.Build(stream.Position, single.Channels);
    }

    /// <summary>
    /// Die Koepfe einer mehrteiligen Datei, bis zum leeren Kopf. Danach stehen die
    /// Offset-Tabellen aller Teile hintereinander, jede so lang, wie ihr Kopf in
    /// "chunkCount" sagt - auch die eines Teils, der hier nicht gelesen wird.
    ///
    /// Gelesen werden die Scanline-Teile mit derselben Bildgroesse wie der erste. Ihre Kanaele
    /// kommen in eine gemeinsame Liste. Traegt ein spaeterer Teil einen Namen, den es schon
    /// gibt - bei Stereo heissen "left" und "right" beide R, G, B -, bekommt er seinen
    /// Teilnamen davor. Die Namen im Kopf eines Teils sind dieselben wie in der Liste; die
    /// Daten eines Blocks haengen nur an Reihenfolge und Art der Kanaele, nicht an ihren Namen.
    /// </summary>
    private static ExrHeader ReadParts(Stream stream, bool longNames)
    {
        var heads = new List<PartHead>();

        while (true)
        {
            int first = stream.ReadByte();
            if (first < 0) throw new ExrFormatException("Datei endet in den Koepfen.");
            if (first == 0) break;

            stream.Position -= 1;
            heads.Add(ReadAttributes(stream, longNames));

            if (heads.Count > MaxParts) throw new ExrFormatException("Mehr Teile, als eine Datei haben kann.");
        }

        long table = stream.Position;
        long at = table;

        var parts = new List<ExrHeader>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        ExrBox? window = null;

        for (int index = 0; index < heads.Count; index++)
        {
            var head = heads[index];

            int chunks = head.ChunkCount
                         ?? throw new ExrFormatException($"Teil {index} nennt keine Zahl von Bloecken.");

            long offset = at;
            at += 8L * chunks;

            // Gekachelt oder deep: anders aufgebaut. Uebersprungen - aber seine Tabelle zaehlt mit.
            if (head.Type is not (null or "scanlineimage")) continue;

            if (head.DataWindow is not { IsValid: true } box || head.Channels is not { Count: > 0 } channels ||
                head.Compression is null || channels.Any(c => !c.IsFullResolution))
                continue;

            window ??= box;
            if (box != window) continue;

            var named = new List<ExrChannel>(channels.Count);
            foreach (var channel in channels)
            {
                string name = channel.Name;

                if (!taken.Add(name))
                {
                    name = $"{head.Name ?? $"Teil{index}"}.{channel.Name}";
                    for (int n = 2; !taken.Add(name); n++) name = $"{head.Name ?? $"Teil{index}"}#{n}.{channel.Name}";
                }

                named.Add(channel with { Name = name });
            }

            parts.Add(head.Build(offset, named, index, chunks));
        }

        if (parts.Count == 0) throw new ExrFormatException("Kein Teil der Datei ist ein lesbares Scanline-Bild.");

        // Die Attribute aller Teile - das Manifest der Kryptomatten steht bei Blender im
        // Kopf des ersten Teils, die Kanaele dazu in eigenen. Bei gleichem Namen gilt der erste.
        var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var head in heads)
            foreach (var (name, value) in head.Raw)
                raw.TryAdd(name, value);

        var lead = parts[0];

        return new ExrHeader
        {
            DataWindow = lead.DataWindow,
            DisplayWindow = lead.DisplayWindow,
            Compression = lead.Compression,
            LineOrder = lead.LineOrder,
            Channels = parts.SelectMany(p => p.Channels).ToList(),
            PixelAspectRatio = lead.PixelAspectRatio,
            TableOffset = table,
            RawAttributes = raw,
            Parts = parts,
        };
    }

    /// <summary>Was ein Kopf sagt - bei einer mehrteiligen Datei einer je Teil.</summary>
    private sealed class PartHead
    {
        public ExrBox? DataWindow;
        public ExrBox? DisplayWindow;
        public ExrCompression? Compression;
        public ExrLineOrder LineOrder = ExrLineOrder.IncreasingY;
        public IReadOnlyList<ExrChannel>? Channels;
        public double Aspect = 1.0;
        public string? Name;
        public string? Type;
        public int? ChunkCount;
        public readonly Dictionary<string, byte[]> Raw = new(StringComparer.Ordinal);

        public ExrHeader Build(long table, IReadOnlyList<ExrChannel> channels, int? index = null, int? chunks = null) => new()
        {
            DataWindow = DataWindow!.Value,
            DisplayWindow = DisplayWindow ?? DataWindow.Value,
            Compression = Compression!.Value,
            LineOrder = LineOrder,
            Channels = channels,
            PixelAspectRatio = Aspect > 0 ? Aspect : 1.0,
            TableOffset = table,
            RawAttributes = Raw,
            PartIndex = index,
            PartName = Name,
            ChunkCount = chunks,
        };
    }

    /// <summary>Die Attribute eines Kopfes, bis zum leeren Namen.</summary>
    private static PartHead ReadAttributes(Stream stream, bool longNames)
    {
        var head = new PartHead();

        while (true)
        {
            string name = ReadName(stream, longNames);
            if (name.Length == 0) break;                 // leerer Name beendet den Kopf

            string type = ReadName(stream, longNames);
            int size = ReadInt32(stream);

            if (size < 0 || size > MaxAttributeBytes)
                throw new ExrFormatException($"Attribut '{name}' meldet {size} Bytes.");

            byte[] value = new byte[size];
            ReadExactly(stream, value);

            switch (name)
            {
                case "dataWindow" when type == "box2i":
                    head.DataWindow = ReadBox(value);
                    break;

                case "displayWindow" when type == "box2i":
                    head.DisplayWindow = ReadBox(value);
                    break;

                case "compression" when type == "compression" && size >= 1:
                    head.Compression = (ExrCompression)value[0];
                    break;

                case "lineOrder" when type == "lineOrder" && size >= 1:
                    head.LineOrder = (ExrLineOrder)value[0];
                    break;

                case "channels" when type == "chlist":
                    head.Channels = ReadChannels(value);
                    break;

                case "pixelAspectRatio" when type == "float" && size >= 4:
                    head.Aspect = BinaryPrimitives.ReadSingleLittleEndian(value);
                    break;

                // Nur in mehrteiligen Dateien - wie der Teil heisst, was er ist, wie viele Bloecke er hat.
                case "name" when type == "string":
                    head.Name = Encoding.UTF8.GetString(value);
                    break;

                case "type" when type == "string":
                    head.Type = Encoding.UTF8.GetString(value);
                    break;

                case "chunkCount" when type == "int" && size >= 4:
                    head.ChunkCount = BinaryPrimitives.ReadInt32LittleEndian(value);
                    break;

                default:
                    head.Raw[name] = value;
                    break;
            }
        }

        return head;
    }

    /// <summary>
    /// Die Kanalliste. Endet mit einem Nullbyte anstelle des naechsten Namens - das ist
    /// dieselbe Klammer wie beim Kopf selbst, eine Ebene tiefer.
    /// </summary>
    private static List<ExrChannel> ReadChannels(byte[] data)
    {
        var list = new List<ExrChannel>();
        int at = 0;

        while (at < data.Length && data[at] != 0)
        {
            int end = Array.IndexOf(data, (byte)0, at);
            if (end < 0) throw new ExrFormatException("Kanalname ohne Abschluss.");

            string name = Encoding.UTF8.GetString(data, at, end - at);
            at = end + 1;

            // pixelType (4) + pLinear (1) + 3 Fuellbytes + xSampling (4) + ySampling (4)
            if (at + 16 > data.Length) throw new ExrFormatException($"Kanal '{name}' ist unvollstaendig.");

            var span = data.AsSpan(at);
            int pixelType = BinaryPrimitives.ReadInt32LittleEndian(span);
            bool linear = span[4] != 0;
            int xSampling = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
            int ySampling = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
            at += 16;

            if (pixelType is < 0 or > 2)
                throw new ExrFormatException($"Kanal '{name}' hat Pixeltyp {pixelType}.");

            list.Add(new ExrChannel(name, (ExrPixelType)pixelType, linear,
                                    Math.Max(1, xSampling), Math.Max(1, ySampling)));
        }

        return list;
    }

    private static ExrBox ReadBox(byte[] data)
    {
        if (data.Length < 16) throw new ExrFormatException("box2i ist zu kurz.");

        var span = data.AsSpan();
        return new ExrBox(
            BinaryPrimitives.ReadInt32LittleEndian(span),
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
    }

    /// <summary>Nullterminierte Zeichenkette. Ein leerer Name ist das Ende des Kopfes.</summary>
    private static string ReadName(Stream stream, bool longNames)
    {
        int limit = longNames ? MaxNameLength : 32;
        var bytes = new List<byte>(32);

        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0) throw new ExrFormatException("Datei endet im Kopf.");
            if (b == 0) break;

            bytes.Add((byte)b);
            if (bytes.Count > limit) throw new ExrFormatException("Attributname ohne Abschluss.");
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    internal static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    internal static long ReadInt64(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(stream, buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    /// <summary>
    /// Stream.Read darf weniger liefern als angefordert, ohne dass das ein Fehler
    /// waere - bei einer Datei, die gerade geschrieben wird, ist genau das der Normalfall.
    /// </summary>
    internal static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = stream.Read(buffer[filled..]);
            if (read <= 0) throw new ExrFormatException("Datei ist kuerzer als ihr Kopf verspricht.");
            filled += read;
        }
    }
}
