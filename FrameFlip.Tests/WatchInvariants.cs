using System.Text;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Zusicherungen fuer den Zuschauer-Zugang.
///
/// Der Kern ist eine Trennung, die halten muss: Wer zusieht, darf mit seinem Link
/// nichts anfangen, was ueber Zusehen hinausgeht - und wer ein Kennwort nicht kennt,
/// kommt auf die hinteren Plaetze nicht. Beides haengt allein daran, dass die
/// Ableitungen verschiedene Bytes liefern. Genau das wird hier geprueft, nicht die
/// gute Absicht der Oberflaeche.
/// </summary>
public static class WatchInvariants
{
    public static void Run()
    {
        Check.Group("Zusehen - der Link ist nicht der Kopplungsschluessel");

        var watch = WatchKey.Create("geheim123");

        Check.That(WatchKey.TryParse(watch.Text, watch.Code, out var wieder) && wieder is not null,
            "ein Geheimnis ueberlebt den Weg durch Text");

        Check.That(wieder!.RoomId(0) == watch.RoomId(0),
            "und kommt auf dieselbe Raumkennung");

        var pairing = PairingKey.Create();

        Check.That(watch.RoomId(0) != pairing.RoomId,
            "der Zuschauerraum ist nicht der Kopplungsraum");

        // Der wichtigste Satz dieser Datei: Ein Zuschauer-Link fuehrt niemals in den
        // Raum, in dem das Handy sitzt - er entsteht aus einem anderen Geheimnis.
        var ausWatch = PairingKey.TryParse(watch.Text, out var alsPairing) ? alsPairing : null;

        Check.That(ausWatch is not null && ausWatch.RoomId != pairing.RoomId,
            "und auch als Kopplungsschluessel gelesen fuehrt er nicht dorthin");

        Check.Group("Zusehen - jeder Platz ist ein eigener Raum");

        var kennungen = new HashSet<string>();

        for (int seat = 0; seat < WatchKey.Seats; seat++) kennungen.Add(watch.RoomId(seat));

        Check.That(kennungen.Count == WatchKey.Seats,
            $"alle {WatchKey.Seats} Plaetze liegen in verschiedenen Raeumen");

        foreach (string kennung in kennungen)
        {
            if (kennung.Length == 32 && kennung.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) continue;

            Check.That(false, "eine Raumkennung hat die falsche Form", kennung);
            break;
        }

        Check.That(true, "und jede hat die Form, die der Relay verlangt");

        Check.Throws<ArgumentOutOfRangeException>(() => watch.RoomId(WatchKey.Seats),
            "ein Platz jenseits der letzten Nummer wird abgewiesen");

        Check.Group("Zusehen - das Kennwort schuetzt nur die hinteren Plaetze");

        var ohne = watch.WithCode(null);

        Check.That(ohne.RoomId(0) == watch.RoomId(0),
            "ein anderes Kennwort aendert die Raeume nicht");

        Check.That(Gleich(watch.Channel(0), ohne.Channel(0)),
            "die freien Plaetze oeffnet der Link allein");

        Check.That(!Gleich(watch.Channel(WatchKey.FreeSeats), ohne.Channel(WatchKey.FreeSeats)),
            "die hinteren nicht");

        var anderes = watch.WithCode("anderes456");

        Check.That(!Gleich(watch.Channel(WatchKey.FreeSeats), anderes.Channel(WatchKey.FreeSeats)),
            "und ein falsches Kennwort ergibt einen anderen Schluessel");

        Check.That(ohne.OpenSeats == WatchKey.FreeSeats && watch.OpenSeats == WatchKey.Seats,
            "ohne Kennwort stehen nur die freien Plaetze offen");

        Check.Group("Zusehen - was abgelegt wird, ist geschuetzt");

        string abgelegt = WatchStore.Protect(watch);

        Check.That(abgelegt.Length > 0, "das Ablegen gelingt");

        Check.That(!abgelegt.Contains(watch.Text, StringComparison.Ordinal),
            "das Geheimnis steht nicht im Klartext darin");

        Check.That(!Encoding.UTF8.GetString(Convert.FromBase64String(abgelegt))
                       .Contains("geheim123", StringComparison.Ordinal),
            "und das Kennwort auch nicht");

        Check.That(WatchStore.TryUnprotect(abgelegt, out var geholt) && geholt is not null
                   && geholt.Text == watch.Text && geholt.Code == watch.Code,
            "zurueck kommen beide unveraendert");

        Check.That(!WatchStore.TryUnprotect("kein gueltiger Wert", out _),
            "unbrauchbares ergibt kein Geheimnis, sondern keines");

        var frisch = WatchKey.Create(watch.Code);

        Check.That(frisch.RoomId(0) != watch.RoomId(0),
            "ein neuer Link fuehrt in andere Raeume - der alte laeuft damit ins Leere");

        Check.That(frisch.Code == watch.Code,
            "das Kennwort bleibt dabei, was es war");

        Check.Group("Zusehen - der Raum hoert nicht zu");

        var raum = RelayRoom.ForWatching(watch, "relay.example.org", 0);

        Check.That(!raum.Listens,
            "eingehende Nutzlast wird nicht einmal geoeffnet");

        Check.That(raum.SocketUrl.StartsWith("wss://", StringComparison.Ordinal),
            "nach draussen geht es nur verschluesselt");

        Check.That(raum.SocketUrl.EndsWith("?role=host", StringComparison.Ordinal),
            "FrameFlip sitzt als Host darin, damit niemand seinen Platz einnehmen kann");

        Check.That(RelayRoom.ForWatching(watch, "127.0.0.1:8080", 0).SocketUrl
                       .StartsWith("ws://", StringComparison.Ordinal),
            "gegen den eigenen Rechner darf es unverschluesselt sein - er verlaesst ihn nie");

        Check.That(RelayRoom.ForPairing(new PairingInvite(pairing, "relay.example.org")).Listens,
            "im Kopplungsraum wird dagegen zugehoert - dort kommen Befehle an");

        Check.That(watch.Link("relay.example.org").StartsWith("https://", StringComparison.Ordinal),
            "der Link ist verschluesselt");

        Check.That(watch.Link("127.0.0.1:8080").StartsWith("http://", StringComparison.Ordinal),
            "und folgt derselben Regel wie die Leitung - gegen den eigenen Rechner ohne");

        Check.That(watch.Link("relay.example.org").Contains("/w/#", StringComparison.Ordinal),
            "das Geheimnis steht hinter der Raute, wo kein Browser es mitschickt");

        Check.Group("Zusehen - was als Kennwort taugt");

        Check.That(WatchKey.IsUsableCode(null) && WatchKey.IsUsableCode(""),
            "leer ist erlaubt und heisst: nur die freien Plaetze");

        Check.That(!WatchKey.IsUsableCode("abc"),
            "drei Zeichen sind Zierde, keine Sicherung");

        Check.That(WatchKey.IsUsableCode("abcd"),
            "vier reichen");
    }

    private static bool Gleich(PairingKey a, PairingKey b) => a.RoomId == b.RoomId;
}
