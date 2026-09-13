using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Dass Einstellungen die Reise auf die Platte und zurueck ueberstehen.
///
/// Anlass war ein handfester Fall: In der config.json stand nach dem Eintragen der
/// Relais-Adresse weiterhin ein leeres Feld, und in der App blieb es bei "verbinde".
/// Eine Einstellung, die sich speichern laesst und beim naechsten Start weg ist, ist
/// schlimmer als eine, die es gar nicht gibt - man sucht den Fehler ueberall, nur
/// nicht dort.
/// </summary>
public static class SettingsInvariants
{
    public static void Run()
    {
        Check.Group("Einstellungen - Standard");

        var fresh = new AppSettings();

        Check.That(fresh.RelayHost == AppSettings.DefaultRelayHost,
                   "eine frische Konfiguration kennt den Standard-Relais", fresh.RelayHost);
        Check.That(!fresh.RemoteEnabled, "eingeschaltet wird trotzdem nichts von allein");

        // Ein leeres Feld heisst "Standard", nicht "kein Relay".
        var emptied = new AppSettings { RelayHost = "  " };
        emptied.Normalize();

        Check.That(emptied.RelayHost == AppSettings.DefaultRelayHost,
                   "ein leeres Feld faellt auf den Standard zurueck", emptied.RelayHost);

        // Ein eigener Relay bleibt stehen.
        var own = new AppSettings { RelayHost = "relay.example.org:8443" };
        own.Normalize();

        Check.That(own.RelayHost == "relay.example.org:8443", "ein eigener Relay bleibt", own.RelayHost);

        // Ohne Schluessel bleibt die Fernsteuerung aus, egal was angehakt ist.
        var keyless = new AppSettings
        {
            RemoteEnabled = true,
            TermsAccepted = AppSettings.TermsVersion, PairingSecret = ""
        };
        keyless.Normalize();

        Check.That(!keyless.RemoteEnabled, "ohne Schluessel bleibt es aus");

        /* Der Riegel vor jeder Verbindung nach draussen.
         *
         * Er sitzt in Normalize und nicht in der Oberflaeche - deshalb wird er auch
         * hier geprueft und nicht dort. Eine von Hand geaenderte config.json, ein
         * alter Stand, ein kuenftiger Weg, den es noch nicht gibt: Sie alle laufen
         * durch Normalize, und alle finden dieselbe verschlossene Tuer. */
        Check.Group("Einstellungen - ohne Zustimmung geht nichts nach draussen");

        var ohneZustimmung = new AppSettings
        {
            RemoteEnabled = true,
            WatchEnabled = true,
            PairingSecret = "nur-ein-platzhalter",
            TermsAccepted = 0
        };
        ohneZustimmung.Normalize();

        Check.That(!ohneZustimmung.RemoteEnabled, "die Kopplung bleibt aus");
        Check.That(!ohneZustimmung.WatchEnabled, "die Zuschauerseite bleibt aus");

        var veraltet = new AppSettings
        {
            RemoteEnabled = true,
            WatchEnabled = true,
            PairingSecret = "nur-ein-platzhalter",
            TermsAccepted = AppSettings.TermsVersion - 1
        };
        veraltet.Normalize();

        Check.That(!veraltet.RemoteEnabled && !veraltet.WatchEnabled,
            "und eine Zustimmung zu einer aelteren Fassung traegt nicht");

        var zugestimmt = new AppSettings
        {
            RemoteEnabled = true,
            WatchEnabled = true,
            PairingSecret = "nur-ein-platzhalter",
            TermsAccepted = AppSettings.TermsVersion
        };
        zugestimmt.Normalize();

        Check.That(zugestimmt.RemoteEnabled && zugestimmt.WatchEnabled,
            "mit Zustimmung geht beides");

        Check.That(!zugestimmt.FileAccessEnabled && !zugestimmt.LibraryAccessEnabled,
            "der Dateizugriff bleibt davon unberuehrt aus - er will eigens eingeschaltet werden");

        Check.Group("Einstellungen - Weg auf die Platte und zurueck");

        var written = new AppSettings
        {
            RemoteEnabled = true,
            TermsAccepted = AppSettings.TermsVersion,
            RelayHost = "relay.example.org",
            PairingSecret = "nur-ein-platzhalter",
            Hotkey = "Ctrl+Alt+F",
            MaxDecoderThreads = 6
        };

        written.Normalize();

        // Ueber JSON, wie SettingsStore es tut - ohne die echte Datei anzufassen.
        string json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<AppSettings>(json)!;
        read.Normalize();

        Check.That(read.RemoteEnabled, "eingeschaltet bleibt eingeschaltet");
        Check.That(read.RelayHost == "relay.example.org", "die Adresse ueberlebt", read.RelayHost);
        Check.That(read.PairingSecret == "nur-ein-platzhalter", "der Schluessel ueberlebt");
        Check.That(read.Hotkey == "Ctrl+Alt+F", "und der Rest auch");
        Check.That(read.MaxDecoderThreads == 6, "und die Zahlen");

        Check.Group("Einstellungen - der Kopplungsschluessel");

        // DPAPI ist an das Windows-Konto gebunden; auf diesem Rechner muss der Weg
        // hin und zurueck funktionieren.
        var key = PairingKey.Create();
        string sealed_ = PairingStore.Protect(key);

        Check.That(sealed_.Length > 0, "der Schluessel laesst sich verpacken");
        Check.That(sealed_ != key.Text, "und liegt nicht im Klartext da");

        Check.That(PairingStore.TryUnprotect(sealed_, out PairingKey? back) && back!.RoomId == key.RoomId,
                   "und wieder auspacken");

        Check.That(!PairingStore.TryUnprotect("", out _), "nichts ergibt nichts");
        Check.That(!PairingStore.TryUnprotect("kein base64!", out _), "Unsinn ergibt nichts");
        // Base64-Endzeichen durch AAAA zu ersetzen kann die Originalbytes
        // unveraendert lassen und nur Nullbytes anhaengen. Ein Bit im Paket
        // selbst kippen, damit dieser Test immer eine echte Aenderung prueft.
        byte[] tampered = Convert.FromBase64String(sealed_);
        tampered[^1] ^= 1;
        Check.That(!PairingStore.TryUnprotect(Convert.ToBase64String(tampered), out _),
                   "ein veraendertes Paket wird abgelehnt");

        Check.Group("Einstellungen - die Einladung");

        var invite = new PairingInvite(key, AppSettings.DefaultRelayHost);

        Check.That(PairingInvite.TryParse(invite.Text, out PairingInvite? parsed)
                   && parsed!.Key.RoomId == key.RoomId,
                   "die Einladung mit dem Standard-Relais ist lesbar");

        // Zwei Installationen landen nie im selben Raum - der Schluessel ist
        // Zufall, nichts Abgeleitetes. Sonst waere ein gemeinsamer Relay ein
        // gemeinsamer Raum.
        var rooms = new HashSet<string>();
        for (int i = 0; i < 200; i++) rooms.Add(PairingKey.Create().RoomId);

        Check.That(rooms.Count == 200, "200 Schluessel ergeben 200 verschiedene Raeume", rooms.Count.ToString());
    }
}
