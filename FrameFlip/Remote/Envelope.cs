using System.Buffers.Binary;
using System.Text;

namespace FrameFlip.Remote;

/// <summary>
/// Was fuer eine Nutzlast das ist.
///
/// Ein Byte vorweg, statt es am Inhalt zu erraten. Beim Relay war genau das der
/// Fehler, den ein Test gefunden hat: Wer den Typ am ersten Zeichen ablesen will,
/// haelt ein Bild, das zufaellig mit '{' beginnt, fuer Text. Hier waere es
/// umgekehrt genauso - ein JPEG faengt mit 0xFF 0xD8 an, aber darauf zu bauen
/// heisst, den Rahmen aus dem Inhalt zu raten.
/// </summary>
public enum PayloadKind : byte
{
    /// <summary>UTF-8-JSON. In beide Richtungen: Zustand vom PC, Befehle vom Handy.</summary>
    Json = 0x01,

    /// <summary>Vorschaubild: Framenummer (4 Bytes, big endian), dann JPEG.</summary>
    Preview = 0x02,

    /// <summary>
    /// Ein Stueck einer Datei: Vorgang (4), laufende Nummer (4), Merkmale (1), Daten.
    ///
    /// Der Relay laesst eine Nachricht von einem Mebibyte durch und puffert 32 davon.
    /// Eine .blend ist schnell zweihundert Megabyte - sie geht deshalb in Stuecken
    /// und mit Quittungen, sonst laeuft der Puffer des Relays voll, waehrend das
    /// Handy noch schreibt.
    /// </summary>
    Chunk = 0x03,

    /// <summary>
    /// Ein angefordertes Bild: Pfadlaenge (2), Pfad als UTF-8, dann JPEG.
    ///
    /// Getrennt von <see cref="Preview"/>, obwohl beides Bilder sind. Die Vorschau
    /// gehoert zum laufenden Render und traegt eine Framenummer; hier geht es um eine
    /// bestimmte Datei, die jemand angetippt hat. Beides ueber denselben Typ zu
    /// schicken hiesse, dass die App raten muss, was da gerade ankam - und die kommen
    /// nun einmal durcheinander, wenn waehrend des Blaetterns ein Render laeuft.
    /// </summary>
    Image = 0x04
}

/// <summary>
/// Der Umschlag um jede verschluesselte Nachricht.
///
/// Er sitzt INNERHALB der Verschluesselung, nicht davor: Der Relay soll auch nicht
/// sehen, ob gerade Zahlen oder ein Bild unterwegs sind. Die Groesse verraet das
/// ohnehin grob, aber das ist ein Unterschied zwischen "kann man schaetzen" und
/// "steht da".
/// </summary>
public static class Envelope
{
    public static byte[] Json(string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] frame = new byte[1 + body.Length];

        frame[0] = (byte)PayloadKind.Json;
        body.CopyTo(frame, 1);

        return frame;
    }

    public static byte[] Preview(int frameNumber, ReadOnlySpan<byte> jpeg)
    {
        byte[] frame = new byte[5 + jpeg.Length];

        frame[0] = (byte)PayloadKind.Preview;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), frameNumber);
        jpeg.CopyTo(frame.AsSpan(5));

        return frame;
    }

    /// <summary>Groesse eines Dateistuecks. Bleibt mit Abstand unter der Grenze des Relays.</summary>
    public const int ChunkBytes = 128 * 1024;

    /// <summary>Kopf eines Dateistuecks: Vorgang (4), Nummer (4), Merkmale (1).</summary>
    private const int ChunkHeader = 9;

    /// <summary>Merkmal: Dies war das letzte Stueck.</summary>
    private const byte LastFlag = 0x01;

    public static byte[] Chunk(int transfer, int index, bool last, ReadOnlySpan<byte> data)
    {
        byte[] frame = new byte[1 + ChunkHeader + data.Length];

        frame[0] = (byte)PayloadKind.Chunk;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), transfer);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(5), index);
        frame[9] = last ? LastFlag : (byte)0;
        data.CopyTo(frame.AsSpan(10));

        return frame;
    }

    /// <summary>
    /// Ein Dateistueck auseinandernehmen.
    ///
    /// Ein zu kurzer Kopf ist kein Stueck - dann lieber nichts als ein Stueck mit
    /// geratener Nummer. Ein leeres letztes Stueck ist dagegen erlaubt und bedeutet
    /// genau das: Ende, nichts mehr dahinter.
    /// </summary>
    public static bool TryReadChunk(ReadOnlySpan<byte> body, out int transfer, out int index,
                                    out bool last, out byte[] data)
    {
        transfer = 0;
        index = 0;
        last = false;
        data = Array.Empty<byte>();

        if (body.Length < ChunkHeader) return false;

        transfer = BinaryPrimitives.ReadInt32BigEndian(body);
        index = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
        last = (body[8] & LastFlag) != 0;
        data = body[ChunkHeader..].ToArray();

        return index >= 0;
    }

    /// <summary>Ein angefordertes Bild mit dem Pfad, zu dem es gehoert.</summary>
    public static byte[] Image(string path, ReadOnlySpan<byte> jpeg)
    {
        byte[] name = Encoding.UTF8.GetBytes(path);

        if (name.Length > ushort.MaxValue) name = name[..ushort.MaxValue];

        byte[] frame = new byte[1 + 2 + name.Length + jpeg.Length];

        frame[0] = (byte)PayloadKind.Image;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)name.Length);
        name.CopyTo(frame, 3);
        jpeg.CopyTo(frame.AsSpan(3 + name.Length));

        return frame;
    }

    /// <summary>
    /// Ein Bild auseinandernehmen.
    ///
    /// Die Laengenangabe wird geprueft, statt ihr zu glauben: Sie kommt zwar
    /// entschluesselt und damit von der Gegenstelle - aber eine Fassung mit einem
    /// Fehler ist auch eine Gegenstelle, und ein Griff hinter das Ende der Nachricht
    /// waere hier der Absturz.
    /// </summary>
    public static bool TryReadImage(ReadOnlySpan<byte> body, out string path, out byte[] jpeg)
    {
        path = string.Empty;
        jpeg = Array.Empty<byte>();

        if (body.Length < 2) return false;

        int length = BinaryPrimitives.ReadUInt16BigEndian(body);

        if (body.Length < 2 + length) return false;

        path = Encoding.UTF8.GetString(body.Slice(2, length));
        jpeg = body[(2 + length)..].ToArray();

        return true;
    }

    /// <summary>
    /// Zerlegt eine Nachricht. false heisst: unbrauchbar, wird verworfen.
    ///
    /// Ein unbekannter Typ ist ausdruecklich kein Fehler - eine spaetere Fassung
    /// darf welche hinzufuegen, ohne diese Seite zu brechen.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> payload, out PayloadKind kind, out byte[] body)
    {
        kind = default;
        body = Array.Empty<byte>();

        if (payload.Length < 1) return false;

        kind = (PayloadKind)payload[0];
        body = payload[1..].ToArray();

        return kind is PayloadKind.Json or PayloadKind.Preview
                    or PayloadKind.Chunk or PayloadKind.Image;
    }
}
