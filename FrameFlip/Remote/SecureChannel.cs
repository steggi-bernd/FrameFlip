using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FrameFlip.Remote;

/// <summary>
/// Eine verschluesselte Richtungspaarung fuer die Dauer einer Verbindung.
///
/// Der Relay reicht Bytes weiter, ohne hineinzusehen - was er weiterreicht, entsteht
/// hier. Jede Nachricht traegt ihren Zaehler offen voran, der Rest ist AES-256-GCM:
///
///     Zaehler (8, big endian) ‖ Pruefsumme (16) ‖ Geheimtext
///
/// Der Zaehler ist zugleich die Nonce (vier Nullbytes davor). Das ist nur deshalb
/// sicher, weil der Schluessel je Verbindung ein anderer ist - beide Seiten wuerfeln
/// beim Verbinden ein Salz und tauschen es offen aus. Ohne diesen Schritt waere ein
/// Neustart von FrameFlip verhaengnisvoll: gleicher Schluessel, Zaehler wieder bei
/// null, dieselbe Nonce fuer anderen Klartext - der Fall, in dem GCM auseinanderfaellt.
///
/// Was das nicht leistet: Der Handschlag ist unbeglaubigt. Wer die Raumkennung kennt,
/// kann sich in einen freien Platz setzen und ein Salz schicken. Lesen kann er
/// dennoch nichts, und die Gegenseite verwirft seine Nachrichten bei der ersten
/// Pruefsumme. Er kann stoeren, nicht mithoeren.
/// </summary>
public sealed class SecureChannel : IDisposable
{
    /// <summary>Laenge des Sitzungssalzes je Seite.</summary>
    public const int SaltBytes = 16;

    /// <summary>Erste Fassung des Handschlags. Steht vorn, damit spaetere erkennbar sind.</summary>
    public const byte Version = 1;

    private const int CounterBytes = 8;
    private const int TagBytes = 16;
    private const int NonceBytes = 12;

    /// <summary>Was eine Nachricht ueber ihre Nutzlast hinaus kostet: 24 Bytes.</summary>
    public const int Overhead = CounterBytes + TagBytes;

    private readonly AesGcm _send;
    private readonly AesGcm _receive;

    private ulong _next;
    private ulong _lastSeen;
    private bool _sawAny;

    private SecureChannel(byte[] sendKey, byte[] receiveKey)
    {
        _send = new AesGcm(sendKey, TagBytes);
        _receive = new AesGcm(receiveKey, TagBytes);

        CryptographicOperations.ZeroMemory(sendKey);
        CryptographicOperations.ZeroMemory(receiveKey);
    }

    /// <summary>Das offene Begruessungspaket: Fassung und das eigene Salz.</summary>
    public static byte[] Hello(out byte[] salt)
    {
        salt = new byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);

        byte[] frame = new byte[1 + SaltBytes];
        frame[0] = Version;
        salt.CopyTo(frame, 1);

        return frame;
    }

    public static bool TryReadHello(ReadOnlySpan<byte> frame, out byte[]? salt)
    {
        salt = null;
        if (frame.Length != 1 + SaltBytes || frame[0] != Version) return false;

        salt = frame[1..].ToArray();
        return true;
    }

    /// <summary>
    /// Beide Salze sind da - ab hier steht der Kanal. Die Reihenfolge ist fest
    /// (erst Host, dann Client), damit beide Seiten auf dieselben Schluessel kommen.
    /// </summary>
    public static SecureChannel Establish(PairingKey key, RelayRole us, byte[] hostSalt, byte[] clientSalt)
    {
        var them = us == RelayRole.Host ? RelayRole.Client : RelayRole.Host;

        return new SecureChannel(
            key.SessionKey(us, hostSalt, clientSalt),
            key.SessionKey(them, hostSalt, clientSalt));
    }

    /// <summary>Verpackt eine Nutzlast. Das Ergebnis geht als Binaerframe an den Relay.</summary>
    public byte[] Seal(ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[Overhead + payload.Length];
        BinaryPrimitives.WriteUInt64BigEndian(frame, _next++);

        Span<byte> nonce = stackalloc byte[NonceBytes];
        frame.AsSpan(0, CounterBytes).CopyTo(nonce[(NonceBytes - CounterBytes)..]);

        _send.Encrypt(
            nonce,
            payload,
            frame.AsSpan(Overhead),
            frame.AsSpan(CounterBytes, TagBytes),
            frame.AsSpan(0, CounterBytes));

        return frame;
    }

    /// <summary>
    /// Packt aus, wenn alles stimmt. Gibt false zurueck, statt zu werfen: Auf einer
    /// offenen Leitung ist eine unbrauchbare Nachricht der Normalfall, kein Ausnahmefall.
    /// </summary>
    public bool TryOpen(ReadOnlySpan<byte> frame, out byte[]? payload)
    {
        payload = null;
        if (frame.Length < Overhead) return false;

        ulong counter = BinaryPrimitives.ReadUInt64BigEndian(frame);
        if (_sawAny && counter <= _lastSeen) return false;

        Span<byte> nonce = stackalloc byte[NonceBytes];
        frame[..CounterBytes].CopyTo(nonce[(NonceBytes - CounterBytes)..]);

        byte[] plain = new byte[frame.Length - Overhead];

        try
        {
            _receive.Decrypt(
                nonce,
                frame[Overhead..],
                frame.Slice(CounterBytes, TagBytes),
                plain,
                frame[..CounterBytes]);
        }
        catch (CryptographicException)
        {
            return false;
        }

        // Erst jetzt weiterzaehlen. Wuerde der Zaehler schon vor der Pruefung
        // nachgezogen, koennte ein Fremder mit einer erfundenen hohen Zahl die echte
        // Gegenseite aussperren.
        _lastSeen = counter;
        _sawAny = true;

        payload = plain;
        return true;
    }

    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
    }
}
