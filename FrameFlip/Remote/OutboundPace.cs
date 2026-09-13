namespace FrameFlip.Remote;

/// <summary>
/// Haelt zurueck, was sonst zu schnell hinausginge.
///
/// Der Relay begrenzt, was eine Verbindung ihm zumutet - eine Nachrichtenzahl und
/// eine Byterate je Sekunde. Wer darueber liegt, wird nicht gebremst, sondern
/// <b>getrennt</b>, ohne Vorwarnung und mitten im Satz. Eine .blend geht in Stuecken
/// von 128 KiB mit vier gleichzeitig unterwegs; auf einer schnellen Leitung sind das
/// zweistellige MiB je Sekunde, und die Verbindung fiel nach gut einer Sekunde.
///
/// Die Bremse sitzt deshalb hier und nicht im Dateiweg. Der Relay zaehlt die
/// <b>Verbindung</b>, nicht das Feature: Vorschaubilder, Zustandsmeldungen,
/// Dateistuecke und alles Kuenftige teilen sich dieselbe Leitung und muessen sich
/// dasselbe Budget teilen. Eine Bremse je Sender waere eine Rechnung, die niemand
/// fuehrt.
///
/// <para>Die Werte liegen bewusst unter denen des Relays. Sie sind kein Nachbau
/// seiner Grenzen, sondern ein Abstand dazu: Der Relay darf streng bleiben, ohne dass
/// ein Zittern in der Laufzeit gleich eine Trennung bedeutet.</para>
///
/// <para><b>Warum der Ausgangspuffer das aushaelt:</b> Er fasst 64 Nachrichten und
/// wirft im Überlauf die aeltesten weg - bei einer Dateiuebertragung waere das
/// verhaengnisvoll. Sie kann ihn aber nicht fuellen: Der Dateiweg haelt hoechstens
/// vier Stuecke gleichzeitig unterwegs und wartet auf Quittungen. Bremst der Ausgang,
/// verzoegern sich die Quittungen, und der Sender legt von selbst langsamer nach.
/// Zustandsmeldungen sind ein paar hundert Byte und gehen ohnehin durch.</para>
/// </summary>
internal sealed class OutboundPace
{
    /// <summary>Nachrichten je Sekunde im Mittel. Der Relay erlaubt 64.</summary>
    private const double DefaultMessagesPerSecond = 48;

    /// <summary>Kurzfristig erlaubte Zahl. Der Relay erlaubt 128.</summary>
    private const double DefaultMessageBurst = 64;

    /// <summary>Bytes je Sekunde im Mittel. Der Relay erlaubt 4 MiB.</summary>
    private const double DefaultBytesPerSecond = 3 * 1024 * 1024;

    /// <summary>Kurzfristig erlaubte Menge. Der Relay erlaubt 8 MiB.</summary>
    private const double DefaultByteBurst = 2 * 1024 * 1024;

    /// <summary>
    /// Laengste Pause fuer eine einzelne Nachricht.
    ///
    /// Eine Nachricht groesser als der Vorrat wuerde sonst beliebig lange warten. Der
    /// Deckel macht daraus eine Verzoegerung statt eines Stillstands - und der Relay
    /// hat seinerseits einen Vorrat, der eine solche Einzelnachricht traegt.
    /// </summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    private readonly double _messageRate;
    private readonly double _messageBurst;
    private readonly double _byteRate;
    private readonly double _byteBurst;

    private double _messageTokens;
    private double _byteTokens;
    private DateTime _last;

    public OutboundPace(DateTime now)
        : this(now, DefaultMessagesPerSecond, DefaultMessageBurst, DefaultBytesPerSecond, DefaultByteBurst)
    {
    }

    internal OutboundPace(DateTime now, double messagesPerSecond, double messageBurst,
                          double bytesPerSecond, double byteBurst)
    {
        _messageRate = messagesPerSecond;
        _messageBurst = messageBurst;
        _byteRate = bytesPerSecond;
        _byteBurst = byteBurst;

        _messageTokens = messageBurst;
        _byteTokens = byteBurst;
        _last = now;
    }

    /// <summary>
    /// Bucht eine Nachricht und sagt, wie lange vorher zu warten ist.
    ///
    /// Gebucht wird IMMER - auch wenn der Vorrat dafuer ins Minus geht. Die Pause ist
    /// dann genau die Zeit, die es braucht, um wieder auf null zu kommen. Anders
    /// herum - erst warten, dann pruefen - koennten zwei Aufrufer denselben Vorrat
    /// zweimal ausgeben.
    ///
    /// Reine Rechnung, keine Uhr und kein Schlaf: Wer wartet, entscheidet der
    /// Aufrufer. Dadurch laesst sich das hier ohne Zeitablauf pruefen.
    /// </summary>
    public TimeSpan Reserve(int bytes, DateTime now)
    {
        double elapsed = (now - _last).TotalSeconds;

        // Eine rueckwaerts laufende Uhr - Zeitumstellung, Winterschlaf - darf keinen
        // negativen Nachschub ergeben.
        if (elapsed > 0)
        {
            _messageTokens = Math.Min(_messageBurst, _messageTokens + elapsed * _messageRate);
            _byteTokens = Math.Min(_byteBurst, _byteTokens + elapsed * _byteRate);
        }

        _last = now;

        _messageTokens -= 1;
        _byteTokens -= bytes;

        double wait = Math.Max(
            _messageTokens < 0 ? -_messageTokens / _messageRate : 0,
            _byteTokens < 0 ? -_byteTokens / _byteRate : 0);

        if (wait <= 0) return TimeSpan.Zero;

        var pause = TimeSpan.FromSeconds(wait);

        return pause > MaxWait ? MaxWait : pause;
    }
}
