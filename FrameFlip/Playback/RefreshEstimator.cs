namespace FrameFlip.Playback;

/// <summary>
/// Schaetzt den Bildschirmtakt aus den tatsaechlichen Zeichenschritten.
///
/// Warum gemessen und nicht erfragt: die Anzeigeeinstellung nennt 60 Hz, tatsaechlich
/// laufen viele Anschluesse mit 59,94. Bei einer Sollrate von 60 fps entscheidet
/// genau dieser Unterschied darueber, ob pro Sekunde ein Bild verschluckt wird.
///
/// Zwei Groessen, die auseinanderfallen koennen und beide gebraucht werden:
/// der Median der Abstaende ergibt den Takt, mit dem der Schirm arbeitet, die Zahl
/// der Schritte je Sekunde dagegen den Takt, den er tatsaechlich liefert. Bricht der
/// zweite ein, waehrend der erste steht, hat die Komposition Schritte ausgelassen -
/// dann darf die Wiedergabe sich nicht mehr an den Schirm haengen, sonst laeuft sie
/// in Zeitlupe.
/// </summary>
public sealed class RefreshEstimator
{
    private const int Window = 64;

    /// <summary>Laenger als das ist kein Bildschirmtakt mehr, sondern eine Pause.</summary>
    private const double PauseMs = 250.0;

    private readonly double[] _gaps = new double[Window];
    private readonly double[] _scratch = new double[Window];

    private int _count;
    private int _next;
    private double _last = -1;
    private double _sum;

    /// <summary>Takt aus dem Median der Abstaende. 0, solange zu wenig gemessen wurde.</summary>
    public double NominalHz { get; private set; }

    /// <summary>Tatsaechlich gelieferte Schritte je Sekunde ueber dasselbe Fenster.</summary>
    public double EffectiveHz { get; private set; }

    /// <summary>
    /// Ab wann eine Aussage gewagt wird. Bewusst weit unter der Fenstergroesse: bei
    /// 60 Hz sind 16 Werte gut eine Viertelsekunde, und genau so lange laeuft die
    /// Wiedergabe sonst nach jedem Start ungekoppelt - also ruckelnd. Der Median aus
    /// 16 Abstaenden ist robust genug, um den Takt zu treffen.
    /// </summary>
    private const int MinSamples = 16;

    public bool HasEstimate => _count >= MinSamples;

    public void Reset()
    {
        _count = 0;
        _next = 0;
        _last = -1;
        _sum = 0;
        NominalHz = 0;
        EffectiveHz = 0;
    }

    /// <param name="nowMs">Fortlaufende Zeit, ueblicherweise aus einer Stopwatch.</param>
    public void Sample(double nowMs)
    {
        if (_last < 0) { _last = nowMs; return; }

        double gap = nowMs - _last;
        _last = nowMs;

        // Ein Doppelschlag im selben Zeitraster sagt nichts ueber den Takt aus.
        if (gap < 0.5) return;

        // Die Vorschau war verdeckt oder das Fenster stand still: das Fenster
        // beschreibt dann nicht mehr den laufenden Betrieb.
        if (gap > PauseMs) { Reset(); return; }

        if (_count == Window) _sum -= _gaps[_next];
        else _count++;

        _gaps[_next] = gap;
        _sum += gap;
        _next = (_next + 1) % Window;

        if (!HasEstimate) return;

        Array.Copy(_gaps, _scratch, _count);
        Array.Sort(_scratch, 0, _count);

        double median = _scratch[_count / 2];

        NominalHz = median > 0 ? 1000.0 / median : 0;
        EffectiveHz = _sum > 0 ? _count * 1000.0 / _sum : 0;
    }
}
