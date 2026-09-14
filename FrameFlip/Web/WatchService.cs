using System.Threading;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;
using FrameFlip.Remote;

namespace FrameFlip.Web;

/// <summary>
/// Die Zuschauerseite, von FrameFlip aus gesehen.
///
/// FrameFlip sitzt als Host in mehreren Raeumen des Leuchtturms - einem je Platz -
/// und schiebt dorthin Zahlen und das jeweils neueste Bild. Mehr passiert nicht, und
/// zwar in beide Richtungen: Die Raeume sind als Einbahnstrasse angelegt, eingehende
/// Nutzlast wird nicht einmal geoeffnet (siehe <see cref="RelayRoom.Listens"/>).
///
/// Der Weg ueber den Leuchtturm hat einen Grund, der nichts mit Bequemlichkeit zu
/// tun hat: Ein eigener Server im Heimnetz braucht eine Oeffnung in der Firewall,
/// also einen Weg von aussen nach innen. Hier baut FrameFlip die Verbindung selbst
/// auf, nach draussen, wie ein Browser auch. Es gibt keinen Eingang, der offen
/// stehen koennte.
///
/// <para><b>Plaetze werden bei Bedarf geoeffnet, nicht auf Vorrat.</b> Ein Raum des
/// Leuchtturms gilt als belegt, sobald jemand darin sitzt - auch wenn das nur
/// FrameFlip selbst ist und nie ein Zuschauer kommt. Sechs Plaetze dauerhaft offen
/// zu halten hiesse, sechs Raeume fuer Leute freizuhalten, die nicht da sind. Der
/// Leuchtturm fasst 128 Raeume insgesamt; bei sieben je Nutzer waeren das achtzehn
/// gleichzeitige Installationen, bei dreien mehr als vierzig.</para>
///
/// <para><b>Nichts wird gesendet, solange niemand zusieht.</b> Das spart nicht nur
/// Daten; es ist auch die Grundlage der Anzeige im Dashboard: Datenverkehr und
/// Zuschauer sind hier dasselbe, und was dort steht, ist deshalb keine Vermutung.</para>
/// </summary>
public sealed class WatchService : IAsyncDisposable
{
    /// <summary>Takt, in dem nachgesehen wird, ob es etwas zu schicken gibt.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Takt im Leerlauf.
    ///
    /// Auch ohne Render soll etwas ankommen - gerade dann ist die Frage, ob der
    /// Rechner ueberhaupt noch wach ist. Aber nicht im Sekundentakt: Das waere ueber
    /// ein Mobilnetz ein spuerbarer Strom fuer die Nachricht "hier passiert nichts".
    /// </summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wie lange ein ueberzaehliger Platz offen bleibt, bevor er zugeht.
    ///
    /// Nicht sofort: Wer die Seite neu laedt oder kurz das Netz wechselt, ist
    /// innerhalb von Sekunden zurueck. Wuerde der Platz dabei jedesmal geschlossen
    /// und neu geoeffnet, entstuende genau das Verbindungsflattern, gegen das die
    /// Grenzen des Leuchtturms gedacht sind.
    /// </summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Fehlversuche am Kennwort, nach denen die hinteren Plaetze zumachen.
    ///
    /// Ohne diese Grenze waere ein vierstelliges Kennwort in ein paar Stunden
    /// durchprobiert. Mit ihr braucht es Jahre, und im Normalfall merkt niemand,
    /// dass es sie gibt - wer sein eigenes Kennwort eingibt, vertippt sich nicht
    /// achtmal.
    /// </summary>
    private const int MaxFailures = 8;

    /// <summary>Wie lange danach zu ist.</summary>
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private readonly WatchKey _key;
    private readonly string _relay;
    private readonly RenderMonitor? _monitor;
    private readonly Func<LoadSnapshot?> _load;
    private readonly Func<string?> _newestFrame;
    private readonly TimeSpan _linger;
    private readonly List<Seat> _seats = new();
    private readonly object _gate = new();

    private Timer? _ticker;
    private DateTime _lastSentUtc = DateTime.MinValue;
    private DateTime? _surplusSince;
    private int _failures;
    private DateTime? _lockedUntilUtc;
    private bool _stopped;

    public WatchService(
        WatchKey key,
        string relay,
        RenderMonitor? monitor,
        Func<LoadSnapshot?> load,
        Func<string?> newestFrame)
        : this(key, relay, monitor, load, newestFrame, Linger)
    {
    }

    /// <summary>
    /// Interner Testeingang. Die Produktfassung verwendet <see cref="Linger"/>;
    /// eine Pruefung, die dreissig Sekunden auf das Schliessen eines Platzes wartet,
    /// waere keine Pruefung, sondern eine Geduldsprobe.
    /// </summary>
    internal WatchService(
        WatchKey key,
        string relay,
        RenderMonitor? monitor,
        Func<LoadSnapshot?> load,
        Func<string?> newestFrame,
        TimeSpan linger)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _relay = relay ?? throw new ArgumentNullException(nameof(relay));
        _monitor = monitor;
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _newestFrame = newestFrame ?? throw new ArgumentNullException(nameof(newestFrame));
        _linger = linger;
    }

    /// <summary>Die Adresse fuer den Browser. Das Geheimnis steht hinter der Raute.</summary>
    public string Link => _key.Link(_relay);

    /// <summary>Wieviele gerade zusehen.</summary>
    public int Watchers
    {
        get { lock (_gate) { return _seats.Count(s => s.Watching); } }
    }

    /// <summary>Wieviele Plaetze gerade offen stehen - waechst und schrumpft mit dem Bedarf.</summary>
    public int OpenSeats
    {
        get { lock (_gate) { return _seats.Count; } }
    }

    /// <summary>Wieviele hoechstens moeglich waeren - abhaengig davon, ob ein Kennwort gesetzt ist.</summary>
    public int MaxSeats => _key.OpenSeats;

    /// <summary>Bis wann die kennwortgeschuetzten Plaetze wegen Fehlversuchen zu sind - oder null.</summary>
    public DateTime? LockedUntilUtc
    {
        get { lock (_gate) { return _lockedUntilUtc; } }
    }

    /// <summary>Zuschauer gekommen, gegangen, oder gesperrt. Kommt vom Netzwerk-Thread.</summary>
    public event Action? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_seats.Count > 0 || _stopped) return;

            _ticker = new Timer(_ => Tick(), null, Interval, Interval);
        }

        Adjust();
    }

    /* --------------------------------------------------------------- Die Plaetze

       Offen bleibt immer EINER MEHR, als gerade besetzt sind - mindestens aber die
       freien. Der eine mehr ist keine Grosszuegigkeit, sondern Notwendigkeit: Der
       Leuchtturm bringt nur zusammen, was schon da ist. Wer einen Platz erst
       oeffnete, wenn jemand danach fragt, kaeme immer zu spaet - der Zuschauer faende
       einen leeren Raum vor und zoege weiter.

       Mehr als einen im Voraus braucht es dagegen nicht. Zwischen zwei Ankoemmlingen
       liegt immer die Zeit, die einer braucht, um den Handschlag zu fuehren; das
       genuegt, um den naechsten Platz zu oeffnen. */

    /// <summary>Wieviele Plaetze offen stehen sollten.</summary>
    private int Needed()
    {
        int watching = _seats.Count(s => s.Watching);

        return Math.Clamp(watching + 1, Math.Min(WatchKey.FreeSeats, _key.OpenSeats), _key.OpenSeats);
    }

    /// <summary>
    /// Oeffnet fehlende Plaetze und merkt sich, seit wann welche ueberzaehlig sind.
    /// Geschlossen wird nicht hier, sondern im Takt - nach <see cref="_linger"/>.
    /// </summary>
    private void Adjust()
    {
        bool opened = false;

        lock (_gate)
        {
            if (_stopped || _ticker is null) return;

            int needed = Needed();

            // Waehrend einer Sperre bleiben die hinteren Plaetze zu, auch wenn der
            // Bedarf da waere. Sonst haette das Zumachen keine Wirkung.
            int ceiling = _lockedUntilUtc is null ? _key.OpenSeats : WatchKey.FreeSeats;

            for (int index = 0; index < ceiling && _seats.Count < needed; index++)
            {
                if (_seats.Any(s => s.Index == index)) continue;

                _seats.Add(Open(index));
                opened = true;
            }

            _surplusSince = _seats.Count > needed
                ? _surplusSince ?? DateTime.UtcNow
                : null;
        }

        if (opened) Announce();
    }

    /// <summary>
    /// Schliesst hoechstens einen ueberzaehligen Platz je Takt, und nur einen leeren.
    ///
    /// Von hinten, weil die Seite immer den kleinsten freien Platz nimmt: Was oben
    /// liegt, wird am seltensten gebraucht.
    /// </summary>
    private async Task ShrinkAsync()
    {
        Seat? closing = null;

        lock (_gate)
        {
            if (_stopped || _surplusSince is not { } since) return;
            if (DateTime.UtcNow - since < _linger) return;
            if (_seats.Count <= Needed()) { _surplusSince = null; return; }

            closing = _seats.Where(s => !s.Watching).OrderByDescending(s => s.Index).FirstOrDefault();

            if (closing is null) { _surplusSince = null; return; }

            _seats.Remove(closing);
            _surplusSince = _seats.Count > Needed() ? DateTime.UtcNow : null;
        }

        if (closing.Client is not null) await closing.Client.DisposeAsync();

        Announce();
    }

    private Seat Open(int index)
    {
        var seat = new Seat(index);
        var client = new RelayClient(RelayRoom.ForWatching(_key, _relay, index));

        client.StateChanged += state => OnStateChanged(seat, state);

        // Nur die hinteren Plaetze sind ueberhaupt durch ein Kennwort gesichert. Ein
        // gescheiterter Nachweis auf einem freien Platz ist kein Rateversuch, sondern
        // jemand mit einem alten Link - das darf niemanden aussperren.
        if (index >= WatchKey.FreeSeats) client.PeerRejected += OnRejected;

        seat.Client = client;
        client.Start();

        return seat;
    }

    private void OnStateChanged(Seat seat, RelayState state)
    {
        bool watching = state == RelayState.Paired;

        lock (_gate)
        {
            if (seat.Watching == watching) return;

            seat.Watching = watching;

            // Ein neuer Zuschauer hat nichts. Der naechste Takt schickt ihm Zahlen und
            // Bild, statt beides fuer bereits zugestellt zu halten.
            seat.SentFrameId = string.Empty;

            // Wer den Schluessel nachweisen konnte, kannte das Kennwort - dann waren
            // die bisherigen Fehlversuche offenbar Vertipper.
            if (watching && seat.Index >= WatchKey.FreeSeats) _failures = 0;
        }

        // Ein Platz mehr, sobald dieser hier besetzt ist - und einer weniger, wenn
        // er lange genug leer stand.
        Adjust();

        Announce();
    }

    /// <summary>Ein gescheiterter Schluesselnachweis auf einem kennwortgeschuetzten Platz.</summary>
    private void OnRejected()
    {
        lock (_gate)
        {
            if (_lockedUntilUtc is not null) return;
            if (++_failures < MaxFailures) return;

            _lockedUntilUtc = DateTime.UtcNow + Lockout;
        }

        _ = CloseProtectedSeatsAsync();
    }

    /// <summary>
    /// Waehrend der Sperre gibt es die hinteren Plaetze nicht - nicht nur keine
    /// Antwort auf ihnen.
    ///
    /// Wer durchprobieren will, findet dann einen leeren Raum vor und kann nicht
    /// einmal erkennen, ob er beim richtigen Geheimnis gelandet ist. Die freien
    /// Plaetze bleiben offen: Wer schon zusieht, soll nicht darunter leiden, dass
    /// jemand anderes am Kennwort scheitert.
    /// </summary>
    private async Task CloseProtectedSeatsAsync()
    {
        Seat[] closing;

        lock (_gate)
        {
            closing = _seats.Where(s => s.Index >= WatchKey.FreeSeats).ToArray();
            _seats.RemoveAll(s => s.Index >= WatchKey.FreeSeats);
            _surplusSince = null;
        }

        foreach (var seat in closing)
        {
            seat.Watching = false;
            if (seat.Client is not null) await seat.Client.DisposeAsync();
        }

        Announce();
    }

    private void ReopenIfDue()
    {
        lock (_gate)
        {
            if (_lockedUntilUtc is not { } until || DateTime.UtcNow < until) return;

            _lockedUntilUtc = null;
            _failures = 0;
        }

        Adjust();
    }

    private void Tick()
    {
        ReopenIfDue();

        _ = ShrinkAsync();

        Seat[] watching;

        lock (_gate)
        {
            watching = _seats.Where(s => s.Watching).ToArray();
        }

        // Kein Zuschauer, kein Verkehr. Das ist die Regel, an der die Anzeige haengt.
        if (watching.Length == 0) return;

        try
        {
            var job = _monitor?.Job;
            bool rendering = job?.IsRunning == true;

            // Im Leerlauf seltener. Der Takt laeuft trotzdem im Sekundenabstand, damit
            // ein beginnender Render nicht bis zu fuenf Sekunden braucht, um anzukommen.
            if (!rendering && DateTime.UtcNow - _lastSentUtc < IdleInterval) return;

            _lastSentUtc = DateTime.UtcNow;

            string? newest = _newestFrame();
            byte[] state = Envelope.Json(WatchState.Describe(job, _load(), newest));

            foreach (var seat in watching) seat.Client?.Send(state);

            SendFrameIfNew(watching, job, newest);
        }
        catch (Exception)
        {
            // Ein Aussetzer beim Lesen oder Kodieren darf weder den Takt beenden noch
            // den Render stoeren. Der naechste Durchlauf versucht es wieder.
        }
    }

    /// <summary>
    /// Das Bild geht nur an Plaetze, die es noch nicht haben.
    ///
    /// Sonst waere ein stehendes Bild ein Dauerstrom: zig Kilobyte je Sekunde fuer die
    /// Auskunft, dass sich nichts geaendert hat. Kodiert wird es hoechstens einmal je
    /// Takt, auch bei sechs Zuschauern - verschluesselt dann je Platz einzeln, weil
    /// jeder seinen eigenen Kanal hat.
    /// </summary>
    private void SendFrameIfNew(Seat[] watching, RenderJob? job, string? newest)
    {
        if (newest is null) return;

        string id = WatchState.FrameIdOf(newest);
        if (id.Length == 0) return;

        var waiting = watching.Where(s => s.SentFrameId != id).ToArray();
        if (waiting.Length == 0) return;

        byte[]? jpeg = PreviewEncoder.Encode(newest);
        if (jpeg is null) return;

        byte[] frame = Envelope.Preview(job?.CurrentFrame ?? 0, jpeg);

        foreach (var seat in waiting)
        {
            seat.Client?.Send(frame);
            seat.SentFrameId = id;
        }
    }

    private void Announce()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* die Oberflaeche darf die Leitung nicht reissen */ }
    }

    public async ValueTask DisposeAsync()
    {
        Seat[] seats;
        Timer? ticker;

        lock (_gate)
        {
            _stopped = true;
            seats = _seats.ToArray();
            _seats.Clear();
            ticker = _ticker;
            _ticker = null;
        }

        if (ticker is not null) await ticker.DisposeAsync();

        foreach (var seat in seats)
        {
            seat.Watching = false;
            if (seat.Client is not null) await seat.Client.DisposeAsync();
        }
    }

    /// <summary>Ein Platz: eine stehende Verbindung in einen eigenen Raum.</summary>
    private sealed class Seat
    {
        public Seat(int index) => Index = index;

        public int Index { get; }

        public RelayClient? Client { get; set; }

        public bool Watching { get; set; }

        /// <summary>Welches Bild dieser Platz zuletzt bekommen hat.</summary>
        public string SentFrameId { get; set; } = string.Empty;
    }
}
