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

    public static ExrHeader Read(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        ReadExactly(stream, head);

        int magic = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (magic != Magic) throw new ExrFormatException("Keine EXR-Datei - Kennung stimmt nicht.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(head[4..]);
        int number = version & 0xFF;
        if (number != 2) throw new ExrFormatException($"EXR-Version {number} wird nicht gelesen.");

        // Diese drei Varianten haben einen anderen Aufbau als eine einteilige
        // Scanline-Datei. Sie hier abzulehnen ist ehrlicher, als sie anzufangen und
        // mitten in der Offset-Tabelle aus dem Tritt zu geraten.
        if ((version & FlagTiled) != 0) throw new ExrFormatException("Gekachelte EXR wird nicht gelesen.");
        if ((version & FlagNonImage) != 0) throw new ExrFormatException("Deep-EXR wird nicht gelesen.");
        if ((version & FlagMultiPart) != 0) throw new ExrFormatException("Mehrteilige EXR wird nicht gelesen.");

        bool longNames = (version & FlagLongNames) != 0;

        ExrBox? dataWindow = null;
        ExrBox? displayWindow = null;
        ExrCompression? compression = null;
        ExrLineOrder lineOrder = ExrLineOrder.IncreasingY;
        IReadOnlyList<ExrChannel>? channels = null;
        double aspect = 1.0;
        var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);

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
                    dataWindow = ReadBox(value);
                    break;

                case "displayWindow" when type == "box2i":
                    displayWindow = ReadBox(value);
                    break;

                case "compression" when type == "compression" && size >= 1:
                    compression = (ExrCompression)value[0];
                    break;

                case "lineOrder" when type == "lineOrder" && size >= 1:
                    lineOrder = (ExrLineOrder)value[0];
                    break;

                case "channels" when type == "chlist":
                    channels = ReadChannels(value);
                    break;

                case "pixelAspectRatio" when type == "float" && size >= 4:
                    aspect = BinaryPrimitives.ReadSingleLittleEndian(value);
                    break;

                default:
                    raw[name] = value;
                    break;
            }
        }

        if (dataWindow is null) throw new ExrFormatException("Kein dataWindow im Kopf.");
        if (channels is null || channels.Count == 0) throw new ExrFormatException("Keine Kanaele im Kopf.");
        if (compression is null) throw new ExrFormatException("Keine Kompressionsangabe im Kopf.");
        if (!dataWindow.Value.IsValid) throw new ExrFormatException("dataWindow ist leer.");

        foreach (var channel in channels)
        {
            if (!channel.IsFullResolution)
                throw new ExrFormatException($"Kanal '{channel.Name}' ist unterabgetastet.");
        }

        return new ExrHeader
        {
            DataWindow = dataWindow.Value,
            DisplayWindow = displayWindow ?? dataWindow.Value,
            Compression = compression.Value,
            LineOrder = lineOrder,
            Channels = channels,
            PixelAspectRatio = aspect > 0 ? aspect : 1.0,
            TableOffset = stream.Position,
            RawAttributes = raw,
        };
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
