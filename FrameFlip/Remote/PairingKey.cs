using System.Security.Cryptography;
using System.Text;

namespace FrameFlip.Remote;

/// <summary>Welche Seite eines Raums. Der Name sagt, wer sendet, nicht wer empfaengt.</summary>
public enum RelayRole
{
    /// <summary>FrameFlip auf dem PC.</summary>
    Host,

    /// <summary>Die App auf dem Handy.</summary>
    Client
}

/// <summary>
/// Das Geheimnis, das PC und Handy teilen: 256 Bit aus dem Systemzufall.
///
/// Es verlaesst den Rechner ausschliesslich ueber den Bildschirm - als QR-Code, den
/// das Handy abliest. Ueber das Netz geht es nie, auch nicht verschluesselt. Das ist
/// der ganze Grund, warum der Relay nichts wissen kann und niemandem vertraut werden
/// muss: Er sieht nur die Raumkennung, und die ist eine Einbahnstrasse aus diesem
/// Schluessel heraus.
///
/// Alles Weitere kommt aus einer HKDF-SHA256, mit festen info-Zeichenketten. Die
/// stehen so in PROTOCOL.md, damit die App aus dem Dokument allein geschrieben
/// werden kann - jede Abweichung hier ist ein Bruch der Schnittstelle, kein Detail.
/// </summary>
public sealed class PairingKey
{
    public const int KeyBytes = 32;

    /// <summary>Laenge der Raumkennung in Bytes. 16 reichen: sie ist ein Name, kein Geheimnis.</summary>
    private const int RoomBytes = 16;

    private const string RoomInfo = "frameflip/v1/room";
    private const string HostInfo = "frameflip/v1/host";
    private const string ClientInfo = "frameflip/v1/client";

    private readonly byte[] _key;

    private PairingKey(byte[] key) => _key = key;

    /// <summary>Ein frischer Schluessel. Nur hierueber entsteht einer auf dem PC.</summary>
    public static PairingKey Create()
    {
        byte[] key = new byte[KeyBytes];
        RandomNumberGenerator.Fill(key);
        return new PairingKey(key);
    }

    public static PairingKey FromBytes(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyBytes)
            throw new ArgumentException($"Schluessel muss {KeyBytes} Bytes haben, hat {key.Length}.", nameof(key));

        return new PairingKey(key.ToArray());
    }

    /// <summary>Textform fuer den QR-Code: 43 Zeichen base64url, ohne Fuellzeichen.</summary>
    public string Text => Base64Url.Encode(_key);

    public static bool TryParse(string? text, out PairingKey? key)
    {
        key = null;

        if (!Base64Url.TryDecode(text, out byte[]? raw) || raw!.Length != KeyBytes) return false;

        key = new PairingKey(raw);
        return true;
    }

    /// <summary>
    /// Die Raumkennung: 32 Kleinbuchstaben-Hexziffern, wie der Relay sie erwartet.
    ///
    /// Das Einzige aus dieser Ableitung, das je das Netz sieht. Wer sie kennt, findet
    /// den Raum - lesen kann er darin nichts.
    /// </summary>
    public string RoomId => Convert.ToHexString(Derive(RoomInfo, RoomBytes, default)).ToLowerInvariant();

    /// <summary>
    /// Der Sitzungsschluessel fuer eine Richtung.
    ///
    /// Beide Seiten wuerfeln beim Verbinden je einen Zufallswert und tauschen ihn
    /// offen aus; er geht hier als Salz ein. Damit ist der Schluessel je Verbindung
    /// ein anderer, und genau das erlaubt es, den Zaehler bei null anfangen zu lassen:
    /// Eine mitgeschnittene Nachricht laesst sich in einer spaeteren Sitzung nicht
    /// noch einmal einspielen, weil sie dort mit einem anderen Schluessel geprueft
    /// wird.
    ///
    /// Die Reihenfolge der Salze ist festgelegt - erst Host, dann Client -, sonst
    /// kaemen beide Seiten auf verschiedene Schluessel.
    /// </summary>
    public byte[] SessionKey(RelayRole sender, ReadOnlySpan<byte> hostSalt, ReadOnlySpan<byte> clientSalt)
    {
        if (hostSalt.Length != SecureChannel.SaltBytes || clientSalt.Length != SecureChannel.SaltBytes)
            throw new ArgumentException($"Salze muessen je {SecureChannel.SaltBytes} Bytes haben.");

        Span<byte> salt = stackalloc byte[SecureChannel.SaltBytes * 2];
        hostSalt.CopyTo(salt);
        clientSalt.CopyTo(salt[SecureChannel.SaltBytes..]);

        return Derive(sender == RelayRole.Host ? HostInfo : ClientInfo, KeyBytes, salt);
    }

    private byte[] Derive(string info, int length, ReadOnlySpan<byte> salt)
    {
        byte[] output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, output, salt, Encoding.UTF8.GetBytes(info));
        return output;
    }
}
