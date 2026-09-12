namespace FrameFlip.Remote;

/// <summary>
/// Ein Raum, so wie <see cref="RelayClient"/> ihn braucht: wohin, womit - und ob
/// ueberhaupt zugehoert wird.
///
/// Die Klasse entstand, weil es zwei Raeume gibt und sie sich in genau diesen drei
/// Punkten unterscheiden. Der Kopplungsraum fuehrt zum Handy, wird mit dem
/// Kopplungsschluessel gesichert, und dort kommen Befehle an. Der Zuschauerraum
/// fuehrt zu einem Browser, haengt an einem eigenen Geheimnis - und dort kommt
/// <b>nichts</b> an, was FrameFlip auch nur ansehen wuerde.
///
/// Alles andere - Verbindungsaufbau, Handschlag, Wiederverbinden mit wachsendem
/// Abstand, der Ausgangspuffer - ist in beiden Faellen dasselbe und bleibt deshalb an
/// einer Stelle. Zwei Fassungen davon waeren zwei Orte, an denen dieselbe
/// Netzwerkschwaeche gefunden und behoben werden muesste.
/// </summary>
public sealed class RelayRoom
{
    private RelayRoom(string socketUrl, PairingKey channel, bool listens)
    {
        SocketUrl = socketUrl;
        Channel = channel;
        Listens = listens;
    }

    /// <summary>Die vollstaendige wss-Adresse samt Raumkennung und Rolle.</summary>
    public string SocketUrl { get; }

    /// <summary>Womit Handschlag und Nutzlast gesichert werden.</summary>
    public PairingKey Channel { get; }

    /// <summary>
    /// Ob eingehende Nutzlast ueberhaupt geoeffnet wird.
    ///
    /// Bei false wird sie nicht etwa entschluesselt und danach verworfen, sondern gar
    /// nicht erst angefasst. Der Unterschied ist wichtig: Ein Empfaenger, der nichts
    /// oeffnet, hat keine Angriffsflaeche, die von einem spaeteren "wir koennten hier
    /// doch noch schnell..." wieder geoeffnet wuerde. Der Schluesselnachweis beim
    /// Aufbau ist davon ausgenommen - ohne ihn gaebe es keinen Kanal.
    /// </summary>
    public bool Listens { get; }

    /// <summary>Der Kopplungsraum: das Handy darf senden, und es wird zugehoert.</summary>
    public static RelayRoom ForPairing(PairingInvite invite)
        => new(invite.SocketUrl(RelayRole.Host), invite.Key, listens: true);

    /// <summary>
    /// Der Zuschauerraum: eine Einbahnstrasse.
    ///
    /// FrameFlip sitzt auch hier als Host - der Browser ist der Client. Das ist keine
    /// Willkuer: Der Relay weist eine zweite Verbindung derselben Rolle ab, statt die
    /// erste zu verdraengen. Waere der Browser der Host, koennte jeder mit dem Link
    /// den Platz von FrameFlip einnehmen und dem naechsten Zuschauer erzaehlen, was
    /// er wollte.
    /// </summary>
    public static RelayRoom ForWatching(WatchKey key, string relay, int seat)
        => new($"{Scheme(relay)}://{relay}/r/{key.RoomId(seat)}?role=host", key.Channel(seat), listens: false);

    /// <summary>
    /// Verschluesselt - ausser gegen den eigenen Rechner.
    ///
    /// Die Ausnahme ist eng gefasst und aendert nichts an der Sicherheit: Was die
    /// Netzwerkkarte nie erreicht, kann unterwegs niemand mitlesen. Ohne sie liesse
    /// sich der Weg ueber den Relay nur gegen den laufenden Server im Netz pruefen -
    /// also ausgerechnet die Stelle nicht, an der ein Fehler am teuersten ist.
    ///
    /// Fuer die Kopplung ans Handy gilt das bewusst NICHT: Dort steht die Adresse in
    /// einem QR-Code, und ein QR-Code kommt von aussen.
    /// </summary>
    private static string Scheme(string relay) => IsLoopback(relay) ? "ws" : "wss";

    /// <summary>
    /// Zeigt die Adresse auf diesen Rechner?
    ///
    /// Steht hier und nicht an jeder Stelle neu, die es wissen will: Dieselbe Regel
    /// zweimal aufzuschreiben heisst, dass eine der beiden Fassungen irgendwann
    /// zurueckbleibt - und ausgerechnet bei der Frage "verschluesselt oder nicht"
    /// waere das die falsche Stelle zum Auseinanderlaufen.
    /// </summary>
    internal static bool IsLoopback(string relay)
    {
        string host = relay.Split(':')[0];

        return host is "localhost" or "127.0.0.1" or "::1";
    }
}
