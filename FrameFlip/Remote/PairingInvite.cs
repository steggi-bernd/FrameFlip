using System.Text.RegularExpressions;

namespace FrameFlip.Remote;

/// <summary>
/// Was im QR-Code steht: der Schluessel und die Adresse des Relays.
///
/// Die Adresse muss irgendwie zum Handy kommen, und der Bildschirm ist der einzige
/// Weg, der ohnehin schon benutzt wird. Gespeichert wird nur der Wirtsname, nie ein
/// vollstaendiges Ziel - die App baut daraus immer eine wss-Adresse. Ein manipulierter
/// QR-Code kann die Verbindung damit nicht auf Klartext herunterstufen, sondern
/// hoechstens auf einen anderen Relay zeigen, und der sieht ohnehin nichts.
/// </summary>
public sealed partial class PairingInvite
{
    public const string Scheme = "frameflip";

    private static readonly Regex Host = HostPattern();

    public PairingInvite(PairingKey key, string relay)
    {
        if (!Host.IsMatch(relay)) throw new ArgumentException($"Unbrauchbare Relay-Adresse: {relay}", nameof(relay));

        Key = key;
        Relay = relay;
    }

    public PairingKey Key { get; }

    /// <summary>Wirtsname, wahlweise mit Port. Ohne Schema, ohne Pfad.</summary>
    public string Relay { get; }

    /// <summary>Die Zeichenkette, die als QR-Code auf dem Bildschirm steht.</summary>
    public string Text => $"{Scheme}://pair?k={Key.Text}&r={Relay}";

    /// <summary>Die Adresse, unter der diese Seite dem Raum beitritt.</summary>
    public string SocketUrl(RelayRole role)
        => $"wss://{Relay}/r/{Key.RoomId}?role={(role == RelayRole.Host ? "host" : "client")}";

    public static bool TryParse(string? text, out PairingInvite? invite)
    {
        invite = null;

        const string prefix = Scheme + "://pair?";
        if (text is null || !text.StartsWith(prefix, StringComparison.Ordinal)) return false;

        string? key = null, relay = null;

        foreach (string part in text[prefix.Length..].Split('&'))
        {
            int split = part.IndexOf('=');
            if (split <= 0) return false;

            string name = part[..split];
            string value = part[(split + 1)..];

            if (name == "k") key = value;
            else if (name == "r") relay = value;
        }

        if (!PairingKey.TryParse(key, out PairingKey? parsed) || relay is null || !Host.IsMatch(relay)) return false;

        invite = new PairingInvite(parsed!, relay);
        return true;
    }

    // Wirtsname oder IPv4, wahlweise mit Port. Bewusst eng, und Punkt fuer Punkt
    // aufgebaut statt als eine Zeichenklasse: Was hier durchkommt, landet ungeprueft
    // in einer URL, und "relay..de" oder "-relay.de" sind keine Adressen.
    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*(:[1-9][0-9]{0,4})?$")]
    private static partial Regex HostPattern();
}
