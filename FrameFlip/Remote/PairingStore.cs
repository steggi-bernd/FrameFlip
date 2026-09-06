using System.Security.Cryptography;

namespace FrameFlip.Remote;

/// <summary>
/// Bewahrt den Kopplungsschluessel zwischen zwei Starts auf.
///
/// Er liegt in derselben config.json wie alles andere, aber nicht im Klartext:
/// DPAPI verschluesselt ihn gegen das Windows-Benutzerkonto. Wer die Datei kopiert -
/// aus einem Backup, von einem geteilten Laufwerk, aus einem Support-Postfach -,
/// bekommt damit nichts. Wer ohnehin schon als dieser Benutzer angemeldet ist,
/// schon; davor schuetzt das nicht und soll es auch nicht.
///
/// Alle Fehler enden hier als "kein Schluessel". Das ist kein Wegsehen: Ein Schluessel,
/// der sich nicht entschluesseln laesst - anderes Konto, anderer Rechner, beschaedigte
/// Datei -, ist unbrauchbar, und die einzig sinnvolle Antwort darauf ist ein neuer.
/// </summary>
public static class PairingStore
{
    /// <summary>Bindet den Schutz an diese Anwendung, nicht nur an das Konto.</summary>
    private static readonly byte[] Entropy = "FrameFlip/pairing/v1"u8.ToArray();

    /// <summary>Verpackt einen Schluessel fuer die Konfigurationsdatei.</summary>
    public static string Protect(PairingKey key)
    {
        if (!Base64Url.TryDecode(key.Text, out byte[]? raw)) return string.Empty;

        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(raw!, Entropy, DataProtectionScope.CurrentUser));
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

    /// <summary>Holt ihn zurueck. false heisst: es gibt keinen brauchbaren.</summary>
    public static bool TryUnprotect(string? stored, out PairingKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(stored)) return false;

        byte[]? raw = null;

        try
        {
            raw = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
            if (raw.Length != PairingKey.KeyBytes) return false;

            key = PairingKey.FromBytes(raw);
            return true;
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
