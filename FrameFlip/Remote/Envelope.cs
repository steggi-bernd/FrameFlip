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
    Preview = 0x02
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

        return kind is PayloadKind.Json or PayloadKind.Preview;
    }
}
