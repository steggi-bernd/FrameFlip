using System.Security.Cryptography;
using System.Text;

namespace FrameFlip.Remote;

/// <summary>
/// Das Geheimnis fuer die Zuschauerseite - und bewusst NICHT der Kopplungsschluessel.
///
/// Der Unterschied ist der ganze Sinn dieser Klasse. Das Handy, das den QR-Code
/// abliest, darf rendern, blaettern, Dateien holen. Wer nur zusieht, soll nichts
/// davon koennen - und zwar nicht, weil die Seite die Knoepfe weglaesst, sondern weil
/// ihr schlicht der Schluessel fehlt, mit dem ein Befehl ueberhaupt verstanden wuerde.
/// Zwei getrennte Geheimnisse sind dafuer die einzige belastbare Antwort. Ein
/// gemeinsames mit unterschiedlichen Rechten waere eine Vereinbarung; das hier ist
/// eine Unmoeglichkeit.
///
/// <para><b>Plaetze.</b> Ein Raum des Leuchtturms fasst genau zwei Verbindungen, also
/// FrameFlip und einen Zuschauer. Mehrere Zuschauer brauchen deshalb mehrere Raeume -
/// und die entstehen hier, indem die Platznummer als Salz in die Ableitung eingeht.
/// FrameFlip sitzt in allen gleichzeitig, ein Browser sucht sich einen freien.</para>
///
/// <para>Das ist der Entwurf, der ohne jede Aenderung am Leuchtturm auskommt: Er
/// sieht nur mehrere ganz gewoehnliche Raeume und muss von Zuschauern nichts wissen.
/// Er bringt ausserdem etwas, das ein Rundruf nicht haette - jeder Zuschauer hat
/// seinen eigenen Kanal mit eigenen Salzen. Keiner kann lesen, was ein anderer
/// bekommt, selbst wenn beide denselben Link haben.</para>
///
/// <para><b>Das Kennwort</b> schuetzt nicht alle Plaetze, sondern die hinteren. Die
/// ersten beiden oeffnet der Link allein - so war es gewuenscht, und es macht den
/// Normalfall (man selbst, vielleicht noch jemand) handgriffsfrei. Wer als Dritter
/// dazukommen will, muss das Kennwort kennen, das nur auf dem Bildschirm steht.
/// Es geht dabei nie durchs Netz: Es ist Salz in der Schluesselableitung, und eine
/// falsche Eingabe ergibt schlicht einen Schluessel, mit dem nichts aufgeht.</para>
/// </summary>
public sealed class WatchKey
{
    public const int KeyBytes = 32;

    /// <summary>
    /// Wieviele gleichzeitig zusehen koennen.
    ///
    /// Jeder Platz ist eine stehende Verbindung zum Leuchtturm, auch ein leerer.
    /// Sechs sind reichlich fuer den Zweck und kosten dort zusammen weniger als ein
    /// einziges Vorschaubild.
    /// </summary>
    public const int Seats = 6;

    /// <summary>Plaetze, die der Link allein oeffnet. Der Rest verlangt das Kennwort.</summary>
    public const int FreeSeats = 2;

    /// <summary>Kuerzestes zulaessiges Kennwort. Kuerzer waere Zierde, keine Sicherung.</summary>
    public const int MinCodeLength = 4;

    /// <summary>Laenge einer Raumkennung in Bytes - dieselbe Form, die der Relay erwartet.</summary>
    private const int RoomBytes = 16;

    private const string RoomInfo = "frameflip/v1/watchroom";
    private const string SeatInfo = "frameflip/v1/watchseat";

    private readonly byte[] _key;

    private WatchKey(byte[] key, string? code)
    {
        _key = key;
        Code = string.IsNullOrEmpty(code) ? null : code;
    }

    /// <summary>Ein frisches Geheimnis. Das Kennwort bleibt, was es war.</summary>
    public static WatchKey Create(string? code)
    {
        byte[] key = new byte[KeyBytes];
        RandomNumberGenerator.Fill(key);

        return new WatchKey(key, code);
    }

    public static bool TryParse(string? text, string? code, out WatchKey? watch)
    {
        watch = null;

        if (!Base64Url.TryDecode(text, out byte[]? raw) || raw!.Length != KeyBytes) return false;

        watch = new WatchKey(raw, code);
        return true;
    }

    /// <summary>Das Kennwort fuer die hinteren Plaetze - oder null, wenn keines gesetzt ist.</summary>
    public string? Code { get; }

    /// <summary>Dasselbe Geheimnis mit einem anderen Kennwort. Der Link bleibt gueltig.</summary>
    public WatchKey WithCode(string? code) => new((byte[])_key.Clone(), code);

    /// <summary>Ob ein Kennwort taugt. Leer ist erlaubt und heisst: nur die freien Plaetze.</summary>
    public static bool IsUsableCode(string? code)
        => string.IsNullOrEmpty(code) || code.Trim().Length >= MinCodeLength;

    /// <summary>Wieviele Plaetze mit dem aktuellen Kennwort ueberhaupt offen sind.</summary>
    public int OpenSeats => Code is null ? FreeSeats : Seats;

    /// <summary>Das Geheimnis als Text - das, was hinter der Raute des Links steht.</summary>
    public string Text => Base64Url.Encode(_key);

    /// <summary>
    /// Die Kennung eines Raums: 32 Kleinbuchstaben-Hexziffern.
    ///
    /// Das Einzige aus dieser Ableitung, das der Leuchtturm je sieht. Sie haengt am
    /// Geheimnis und an der Platznummer - nicht am Kennwort. Das ist Absicht: Beide
    /// Seiten muessen sich finden koennen, bevor irgendetwas geprueft wird. Haenge die
    /// Kennung am Kennwort, landete eine falsche Eingabe in einem leeren Raum und
    /// wartete dort ewig, ohne Auskunft und ohne dass FrameFlip je erfuehre, dass es
    /// jemand versucht hat.
    /// </summary>
    public string RoomId(int seat)
    {
        if (seat < 0 || seat >= Seats) throw new ArgumentOutOfRangeException(nameof(seat));

        Span<byte> salt = stackalloc byte[1] { (byte)seat };

        return Convert.ToHexString(Derive(RoomInfo, RoomBytes, salt)).ToLowerInvariant();
    }

    /// <summary>
    /// Der Schluessel, mit dem Handschlag und Nutzlast eines Platzes gesichert werden.
    ///
    /// Bei den hinteren Plaetzen geht das Kennwort als Salz ein. Wer ein falsches
    /// eingibt, kommt auf andere Bytes, sein Schluesselnachweis scheitert, und er
    /// sieht nichts - ohne dass FrameFlip das Kennwort je preisgeben oder vergleichen
    /// muesste.
    ///
    /// Der Rueckgabewert ist absichtlich ein <see cref="PairingKey"/>: Von hier an
    /// gilt genau dasselbe Protokoll wie fuer das Handy, mit demselben Code auf beiden
    /// Seiten. Ein zweites, aehnliches Verfahren waere ein zweiter Ort, an dem ein
    /// Fehler stecken kann.
    /// </summary>
    public PairingKey Channel(int seat)
    {
        if (seat < 0 || seat >= Seats) throw new ArgumentOutOfRangeException(nameof(seat));

        byte[] salt = seat < FreeSeats || Code is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(Code);

        byte[] derived = Derive(SeatInfo, PairingKey.KeyBytes, salt);

        try { return PairingKey.FromBytes(derived); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    /// <summary>
    /// Die Adresse der Zuschauerseite.
    ///
    /// Das Geheimnis steht hinter der Raute, und das ist keine Kosmetik: Browser
    /// schicken diesen Teil grundsaetzlich nicht zum Server. Er steht in keinem
    /// Zugriffsprotokoll, in keinem Referrer und in keinem Zwischenspeicher eines
    /// Vermittlers. Der Leuchtturm liefert die Seite aus, ohne je zu erfahren, wofuer.
    /// </summary>
    public string Link(string relay)
        => $"{(RelayRoom.IsLoopback(relay) ? "http" : "https")}://{relay}/w/#{Text}";

    private byte[] Derive(string info, int length, ReadOnlySpan<byte> salt)
    {
        byte[] output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, output, salt, Encoding.UTF8.GetBytes(info));
        return output;
    }
}
