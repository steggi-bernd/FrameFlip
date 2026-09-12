using System.Security.Cryptography;
using System.Text;

namespace FrameFlip.Remote;

/// <summary>
/// Bewahrt Zuschauer-Geheimnis und Kennwort zwischen zwei Starts auf.
///
/// Dass der Link ueber einen Neustart hinweg gilt, ist hier kein Nebeneffekt, sondern
/// der Zweck: Ein Lesezeichen auf dem Handy soll morgen noch funktionieren. Ungueltig
/// wird ein Link ausschliesslich dann, wenn jemand am PC einen neuen erzeugt - das
/// ist der einzige Widerruf, und er ist sofort und vollstaendig.
///
/// Beides liegt in derselben config.json wie alles andere, aber nicht im Klartext:
/// DPAPI verschluesselt es gegen das Windows-Benutzerkonto. Wer die Datei kopiert -
/// aus einem Backup, von einem geteilten Laufwerk, aus einem Support-Postfach -,
/// bekommt damit nichts.
///
/// Geheimnis und Kennwort stehen absichtlich in <b>einem</b> Feld. Zwei Felder waeren
/// zwei Dinge, die auseinanderlaufen koennen - ein erneuertes Geheimnis neben einem
/// alten Kennwort etwa -, und das faellt erst auf, wenn jemand nicht hereinkommt.
///
/// Alle Fehler enden als "kein Schluessel". Das ist kein Wegsehen: Ein Geheimnis, das
/// sich nicht entschluesseln laesst - anderes Konto, anderer Rechner, beschaedigte
/// Datei -, ist unbrauchbar, und die einzig sinnvolle Antwort darauf ist ein neues.
/// </summary>
public static class WatchStore
{
    /// <summary>Bindet den Schutz an diesen Zweck, nicht nur an das Konto.</summary>
    private static readonly byte[] Entropy = "FrameFlip/watch/v1"u8.ToArray();

    /// <summary>Verpackt Geheimnis und Kennwort fuer die Konfigurationsdatei.</summary>
    public static string Protect(WatchKey key)
    {
        byte[]? raw = null;

        try
        {
            // Zeilenumbruch als Trenner: Weder base64url noch ein Kennwort enthaelt
            // einen, und ein Kennwort mit Leerzeichen oder Doppelpunkt bleibt heil.
            raw = Encoding.UTF8.GetBytes(key.Text + "\n" + (key.Code ?? string.Empty));

            return Convert.ToBase64String(ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        finally
        {
            if (raw is not null) CryptographicOperations.ZeroMemory(raw);
        }
    }

    /// <summary>Holt beides zurueck. false heisst: es gibt nichts Brauchbares.</summary>
    public static bool TryUnprotect(string? stored, out WatchKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(stored)) return false;

        byte[]? raw = null;

        try
        {
            raw = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);

            string text = Encoding.UTF8.GetString(raw);
            int split = text.IndexOf('\n');

            if (split < 0) return false;

            return WatchKey.TryParse(text[..split], text[(split + 1)..], out key);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
        finally
        {
            if (raw is not null) CryptographicOperations.ZeroMemory(raw);
        }
    }
}
