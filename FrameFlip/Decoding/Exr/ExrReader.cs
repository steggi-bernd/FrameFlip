using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace FrameFlip.Decoding.Exr;

/// <summary>
/// Ein gelesenes Bild: die angeforderten Kanaele, jeder als Gleitkommafeld in
/// Zeilenordnung. Werte oberhalb von 1 bleiben erhalten - sie sind der Grund,
/// warum ueberhaupt EXR gelesen wird.
/// </summary>
public sealed class ExrImage
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Kanalname auf Pixelwerte, Laenge jeweils Width * Height.</summary>
    public required IReadOnlyDictionary<string, float[]> Channels { get; init; }

    public float[]? Channel(string name)
        => Channels.TryGetValue(name, out var values) ? values : null;
}

/// <summary>
/// Liest die Pixel einer Scanline-EXR.
///
/// Gelesen wird gezielt: wer nur R, G und B braucht, bekommt auch nur die drei im
/// Speicher. Bei einer Multilayer-Datei mit zwanzig Kanaelen ist das der Unterschied
/// zwischen dreissig Megabyte und dreihundert. Ausgepackt werden muss jeder Block
/// trotzdem vollstaendig - Deflate laesst sich nicht in der Mitte anspringen.
/// </summary>
public static class ExrReader
{
    /// <summary>
    /// Umrechnungstabelle Half nach Float, ueber alle 65536 moeglichen Bitmuster.
    ///
    /// 256 KB einmalig gegen eine Umrechnung je Pixel: ein 4K-Bild mit vier Kanaelen
    /// sind 33 Millionen Werte, und die Tabelle macht daraus einen Feldzugriff. Das
    /// ist dieselbe Ueberlegung wie bei der vorberechneten Tonwertkurve.
    /// </summary>
    private static readonly float[] HalfToFloat = BuildHalfTable();

    private static float[] BuildHalfTable()
    {
        var table = new float[65536];
        for (int i = 0; i < table.Length; i++)
            table[i] = (float)BitConverter.UInt16BitsToHalf((ushort)i);

        return table;
    }

    /// <summary>
    /// Liest die genannten Kanaele. <paramref name="wanted"/> leer bedeutet: alle.
    ///
    /// Kanaele, die im Kopf nicht vorkommen, werden stillschweigend ausgelassen -
    /// ob ein Bild einen Alphakanal hat, entscheidet der Aufrufer nicht.
    /// </summary>
    public static ExrImage Read(Stream stream, ExrHeader header, IReadOnlyCollection<string>? wanted = null)
    {
        if (!header.IsSupportedCompression)
            throw new ExrFormatException($"Kompression {header.Compression} wird nicht gelesen.");

        int width = header.DataWindow.Width;
        int height = header.DataWindow.Height;

        if ((long)width * height > int.MaxValue)
            throw new ExrFormatException("Bild ist zu gross fuer ein einzelnes Feld.");

        var targets = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var channel in header.Channels)
        {
            bool take = wanted is null || wanted.Count == 0 || wanted.Contains(channel.Name);
            if (take) targets[channel.Name] = new float[width * height];
        }

        int perBlock = header.ScanlinesPerBlock;
        int blocks = header.BlockCount;
        int blockBytes = header.BytesPerScanline * perBlock;

        // Die Offset-Tabelle steht unmittelbar hinter dem Kopf: je Block ein
        // 64-Bit-Versatz in die Datei.
        stream.Position = header.TableOffset;
        var offsets = new long[blocks];
        for (int i = 0; i < blocks; i++) offsets[i] = ExrHeaderReader.ReadInt64(stream);

        var pool = ArrayPool<byte>.Shared;
        byte[] packed = pool.Rent(blockBytes + 1024);
        byte[] plain = pool.Rent(blockBytes);

        try
        {
            foreach (long offset in offsets)
            {
                if (offset <= 0 || offset >= stream.Length)
                    throw new ExrFormatException("Offset-Tabelle zeigt aus der Datei heraus.");

                stream.Position = offset;

                // Jeder Block nennt seine eigene erste Zeile. Deshalb spielt es keine
                // Rolle, ob die Bloecke auf- oder absteigend in der Datei liegen -
                // lineOrder muss gar nicht ausgewertet werden.
                int firstLine = ExrHeaderReader.ReadInt32(stream);
                int packedBytes = ExrHeaderReader.ReadInt32(stream);

                if (packedBytes < 0 || packedBytes > packed.Length)
                    throw new ExrFormatException($"Block bei {offset} meldet {packedBytes} Bytes.");

                int row = firstLine - header.DataWindow.YMin;
                if (row < 0 || row >= height)
                    throw new ExrFormatException($"Block beginnt bei Zeile {firstLine} ausserhalb des Bildes.");

                // Der letzte Block ist kuerzer, wenn die Hoehe kein Vielfaches ist.
                int lines = Math.Min(perBlock, height - row);
                int expected = header.BytesPerScanline * lines;

                ExrHeaderReader.ReadExactly(stream, packed.AsSpan(0, packedBytes));
                ExrDecompress.Block(header.Compression, packed.AsSpan(0, packedBytes),
                                    plain.AsSpan(0, blockBytes), expected);

                Scatter(header, plain.AsSpan(0, expected), targets, row, lines, width);
            }
        }
        finally
        {
            pool.Return(packed);
            pool.Return(plain);
        }

        return new ExrImage { Width = width, Height = height, Channels = targets };
    }

    /// <summary>
    /// Verteilt einen ausgepackten Block auf die Zielfelder.
    ///
    /// Aufbau innerhalb des Blocks: Zeile fuer Zeile, und in jeder Zeile die Kanaele
    /// nacheinander, jeder vollstaendig. Nicht angeforderte Kanaele werden dabei nur
    /// uebersprungen.
    /// </summary>
    private static void Scatter(ExrHeader header, ReadOnlySpan<byte> block,
                                Dictionary<string, float[]> targets,
                                int firstRow, int lines, int width)
    {
        int at = 0;

        for (int line = 0; line < lines; line++)
        {
            int row = firstRow + line;

            foreach (var channel in header.Channels)
            {
                int bytes = width * channel.BytesPerSample;

                if (at + bytes > block.Length)
                    throw new ExrFormatException("Block ist kuerzer als seine Kanaele.");

                if (targets.TryGetValue(channel.Name, out var target))
                    Convert(block.Slice(at, bytes), channel.PixelType, target, row * width, width);

                at += bytes;
            }
        }
    }

    private static void Convert(ReadOnlySpan<byte> source, ExrPixelType type,
                                float[] target, int start, int count)
    {
        switch (type)
        {
            case ExrPixelType.Half:
                for (int i = 0; i < count; i++)
                    target[start + i] = HalfToFloat[BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..])];
                break;

            case ExrPixelType.Float:
                for (int i = 0; i < count; i++)
                    target[start + i] = BinaryPrimitives.ReadSingleLittleEndian(source[(i * 4)..]);
                break;

            case ExrPixelType.UInt:
                // Ganzzahlkanaele tragen Kennungen, keine Helligkeiten - Object-ID,
                // Material-Index. Sie werden unveraendert uebernommen; wer sie als
                // Bild anzeigt, bekommt Unsinn zu sehen, aber das ist die richtige
                // Antwort auf die Frage.
                for (int i = 0; i < count; i++)
                    target[start + i] = BinaryPrimitives.ReadUInt32LittleEndian(source[(i * 4)..]);
                break;
        }
    }

    /// <summary>Kopf und Bild in einem Zug, fuer den einfachen Fall.</summary>
    public static ExrImage Read(string path, IReadOnlyCollection<string>? wanted = null)
    {
        using var stream = Open(path);
        var header = ExrHeaderReader.Read(stream);
        return Read(stream, header, wanted);
    }

    /// <summary>
    /// FileShare.ReadWrite wie beim WIC-Decoder: Blender schreibt womoeglich gerade
    /// in denselben Ordner.
    /// </summary>
    internal static FileStream Open(string path)
        => new(path, FileMode.Open, FileAccess.Read,
               FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
}
