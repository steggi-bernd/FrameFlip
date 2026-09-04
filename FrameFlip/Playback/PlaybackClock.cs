using System.Diagnostics;

namespace FrameFlip.Playback;

/// <summary>
/// Zeitbasierte Wiedergabeposition. Der Zielframe wird aus der Wanduhr berechnet,
/// nicht durch Hochzaehlen bei jedem Tick - dadurch stimmt die FPS auch dann noch,
/// wenn der Decoder zurueckfaellt oder ein Tick ausfaellt. Faellt der Decoder zurueck,
/// laeuft die Zeitachse einfach weiter und Frames entfallen.
///
/// Der Wert kann ueber die Sequenzlaenge hinauslaufen; das Aufloesen in einen Index
/// (Loop oder Anschlag) macht SequenceMath.Resolve.
///
/// Ausnahme: liegt die Sollrate praktisch auf dem Bildschirmtakt, wird stattdessen je
/// Zeichenschritt genau ein Frame weitergeschaltet. Der Grund ist gemessen: 60 fps auf
/// 60 Hz hat keinerlei Reserve, jeder ausgelassene Kompositionsschritt wird sofort zu
/// einem uebersprungenen Bild. Zeitbasiert kamen so 40 von 60 Bildern an, gekoppelt
/// sind es 59 - ohne ein einziges Standbild und ohne einen einzigen Sprung. Bezahlt
/// wird das mit dem Unterschied zwischen 60,000 und dem echten Schirmtakt, also
/// rund einem Promille. Bei allen anderen Sollraten bleibt es bei der Uhr: dort ist
/// Reserve vorhanden, und eine Kopplung wuerde die Wiedergabe bei jedem ausgelassenen
/// Schritt spuerbar verlangsamen.
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>Wie nah die Sollrate am Schirmtakt liegen muss, damit gekoppelt wird.</summary>
    private const double LockTolerance = 0.05;

    /// <summary>
    /// Wie viel der Schirm von seinem eigenen Takt liefern muss. Darunter laesst die
    /// Komposition so viele Schritte aus, dass eine Kopplung Zeitlupe ergaebe.
    /// </summary>
    private const double MinDelivery = 0.8;

    private readonly Stopwatch _stopwatch = new();
    private double _fps = 24.0;
    private long _anchor;

    private double _displayHz;
    private double _deliveredHz;
    private bool _locked;
    private long _lockedFrames;
    private double _accumulator;

    public bool IsRunning { get; private set; }

    /// <summary>Vom Nutzer erlaubt. Aus heisst: immer die Uhr, wie urspruenglich.</summary>
    public bool LockToDisplay { get; set; }

    /// <summary>Gemessener Bildschirmtakt, 0 solange unbekannt.</summary>
    public double DisplayHz => _displayHz;

    /// <summary>Ob gerade tatsaechlich am Bildschirm haengt statt an der Uhr.</summary>
    public bool IsLockedToDisplay => _locked;

    public double Fps
    {
        get => _fps;
        set
        {
            double next = value > 0 ? value : 24.0;
            if (Math.Abs(next - _fps) < 0.0001) return;

            // Auf die aktuelle Position umsetzen, sonst springt die Wiedergabe
            // beim Umschalten der Bildrate.
            Rebase(RawTarget);
            _fps = next;
        }
    }

    /// <summary>Fortlaufende Framenummer seit dem letzten Anker, ohne Loop-Aufloesung.</summary>
    public long RawTarget
    {
        get
        {
            if (!IsRunning) return _anchor;
            if (_locked) return _anchor + _lockedFrames;
            return _anchor + (long)(_stopwatch.Elapsed.TotalSeconds * _fps);
        }
    }

    /// <summary>Der zuletzt gemessene Bildschirmtakt. Entscheidet ueber die Kopplung.</summary>
    public void ObserveDisplay(double nominalHz, double deliveredHz)
    {
        _displayHz = nominalHz;
        _deliveredHz = deliveredHz;
    }

    /// <summary>
    /// Einmal je Zeichenschritt aufzurufen, bevor <see cref="RawTarget"/> gelesen wird.
    /// Ohne Kopplung tut das nichts ausser der Zustandspruefung.
    /// </summary>
    public void Tick()
    {
        if (!IsRunning) return;

        bool wanted = ShouldLock();

        if (wanted != _locked)
        {
            // Beim Umschalten die erreichte Position festhalten, sonst springt die
            // Wiedergabe genau in dem Moment, in dem sie ruhiger werden soll.
            Rebase(RawTarget);
            _locked = wanted;
        }

        if (!_locked) return;

        // Refreshes je Frame. Bei 60 fps auf 60 Hz genau eins, bei 59,94 Hz knapp
        // darunter - dann faellt hin und wieder ein zweiter Frame auf einen Schritt,
        // was der Sollrate entspricht.
        double step = _displayHz / _fps;
        if (step <= 0) return;

        _accumulator += 1.0;

        while (_accumulator + 1e-9 >= step)
        {
            _accumulator -= step;
            _lockedFrames++;
        }
    }

    private bool ShouldLock()
    {
        if (!LockToDisplay || _fps <= 0 || _displayHz <= 0) return false;

        if (Math.Abs(_fps - _displayHz) > _fps * LockTolerance) return false;

        return _deliveredHz >= _displayHz * MinDelivery;
    }

    public void Start(long fromFrame)
    {
        _anchor = fromFrame;
        _locked = false;
        _lockedFrames = 0;
        _accumulator = 0;
        _stopwatch.Restart();
        IsRunning = true;
    }

    public void Stop()
    {
        if (IsRunning)
        {
            _anchor = RawTarget;
            IsRunning = false;
        }

        _locked = false;
        _lockedFrames = 0;
        _accumulator = 0;
        _stopwatch.Reset();
    }

    /// <summary>Setzt die Zeitachse auf eine neue Position, ohne den Laufzustand zu aendern.</summary>
    public void Seek(long frame) => Rebase(frame);

    private void Rebase(long frame)
    {
        _anchor = frame;
        _lockedFrames = 0;
        _accumulator = 0;
        if (IsRunning) _stopwatch.Restart();
    }
}
