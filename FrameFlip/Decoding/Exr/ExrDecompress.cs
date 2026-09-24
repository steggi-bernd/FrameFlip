using System.IO;
using System.IO.Compression;

namespace FrameFlip.Decoding.Exr;

/// <summary>
/// Packt einen Scanline-Block aus.
///
/// Unterstuetzt werden die vier Verfahren, die ohne Fremdpaket auskommen: gar keine
/// Kompression, RLE, und ZIP/ZIPS - letztere sind Deflate, das im Framework liegt.
/// Blender schreibt ZIP als Vorgabe, damit ist der Normalfall abgedeckt. PIZ (Wavelet
/// plus Huffman) und DWA (DCT) waeren je ein eigenes Stueck Arbeit und kommen erst,
/// wenn tatsaechlich jemand mit einer solchen Datei auftaucht.
/// </summary>
internal static class ExrDecompress
{
    /// <summary>
    /// Packt <paramref name="source"/> nach <paramref name="destination"/> aus.
    ///
    /// Der Rueckgabewert ist die Zahl gueltiger Bytes im Ziel. Sie kann kleiner sein
    /// als dessen Laenge: der letzte Block einer Datei enthaelt nur die Zeilen, die
    /// noch uebrig sind.
    /// </summary>
    public static int Block(ExrCompression compression, ReadOnlySpan<byte> source,
                            Span<byte> destination, int expectedBytes)
    {
        if (expectedBytes <= 0 || expectedBytes > destination.Length)
            throw new ExrFormatException("Blockgroesse passt nicht zum Puffer.");

        // Wenn das Packen nichts gebracht haette, legt das Format die Daten roh ab.
        // Ohne diese Abfrage liefe ein solcher Block in den Entpacker und scheiterte
        // dort an einem Kopf, den er nicht hat - bei glatten Farbflaechen, wo die
        // Kompression am besten arbeitet, ist das kein seltener Fall, sondern einer,
        // den etwa ein einfarbiger Hintergrund schon ausloest.
        if (compression == ExrCompression.None || source.Length >= expectedBytes)
        {
            if (source.Length < expectedBytes)
                throw new ExrFormatException("Unkomprimierter Block ist zu kurz.");

            source[..expectedBytes].CopyTo(destination);
            return expectedBytes;
        }

        switch (compression)
        {
            case ExrCompression.Zip:
            case ExrCompression.ZipS:
                Inflate(source, destination[..expectedBytes]);
                break;

            case ExrCompression.Rle:
                RunLength(source, destination[..expectedBytes]);
                break;

            default:
                throw new ExrFormatException($"Kompression {compression} wird nicht gelesen.");
        }

        // Beide Verfahren liegen dieselben zwei Umformungen vor dem eigentlichen
        // Packen: erst werden die Bytes eines Blocks in zwei Haelften sortiert
        // (hohe und niedrige Haelfte jedes Half-Werts landen beieinander und sind
        // dann einander aehnlich), dann wird die Differenz zum Vorgaenger gespeichert.
        // Rueckwaerts also in umgekehrter Reihenfolge.
        UndoPredictor(destination[..expectedBytes]);
        UndoInterleave(destination[..expectedBytes]);
        return expectedBytes;
    }

    private static void Inflate(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        // ZlibStream statt DeflateStream: die Bloecke tragen den Zlib-Kopf mit
        // Pruefsumme, den DeflateStream nicht erwartet.
        using var input = new MemoryStream(source.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);

        int filled = 0;
        while (filled < destination.Length)
        {
            int read = zlib.Read(destination[filled..]);
            if (read <= 0) throw new ExrFormatException("Block endet vor der erwarteten Laenge.");
            filled += read;
        }
    }

    /// <summary>
    /// Das Lauflaengenverfahren des Formats: ein Zaehlbyte mit Vorzeichen, danach
    /// entweder ein Wert zum Wiederholen oder eine Folge unveraenderter Bytes.
    /// </summary>
    private static void RunLength(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int read = 0;
        int written = 0;

        while (read < source.Length)
        {
            sbyte count = (sbyte)source[read++];

            if (count < 0)
            {
                // Negativ: so viele Bytes folgen einzeln.
                int literals = -count;
                if (read + literals > source.Length || written + literals > destination.Length)
                    throw new ExrFormatException("RLE-Block laeuft ueber.");

                source.Slice(read, literals).CopyTo(destination[written..]);
                read += literals;
                written += literals;
            }
            else
            {
                // Nicht negativ: das naechste Byte count+1 mal.
                if (read >= source.Length) throw new ExrFormatException("RLE-Block endet im Lauf.");

                int run = count + 1;
                if (written + run > destination.Length)
                    throw new ExrFormatException("RLE-Block laeuft ueber.");

                destination.Slice(written, run).Fill(source[read++]);
                written += run;
            }
        }

        if (written != destination.Length)
            throw new ExrFormatException("RLE-Block ergibt die falsche Laenge.");
    }

    /// <summary>
    /// Jedes Byte war als Differenz zu seinem Vorgaenger abgelegt. Die Rechnung mit
    /// dem Versatz von 128 ist die des Formats und muss genau so bleiben - sie haelt
    /// das Ergebnis im Bytebereich, ohne dass ein Ueberlauf verlorenginge.
    /// </summary>
    private static void UndoPredictor(Span<byte> data)
    {
        for (int i = 1; i < data.Length; i++)
        {
            int d = data[i - 1] + data[i] - 128;
            data[i] = (byte)d;
        }
    }

    /// <summary>
    /// Die Bytes lagen in zwei Haelften: erst jedes zweite, dann der Rest. Diese
    /// Trennung ist der Grund, warum Deflate auf Half-Werten ueberhaupt etwas
    /// ausrichtet - die hohen Bytes benachbarter Pixel aehneln einander, die
    /// niedrigen sind nahezu Rauschen.
    /// </summary>
    private static void UndoInterleave(Span<byte> data)
    {
        // Ein Zwischenpuffer ist hier nicht zu vermeiden: die Umsortierung schreibt
        // an Stellen, die sie spaeter noch lesen muss.
        byte[] source = data.ToArray();

        int half = (data.Length + 1) / 2;
        int first = 0;
        int second = half;

        for (int at = 0; at < data.Length; at++)
        {
            data[at] = (at & 1) == 0 ? source[first++] : source[second++];
        }
    }
}
