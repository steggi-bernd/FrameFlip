using FrameFlip.Playback;

namespace FrameFlip.Tests;

/// <summary>
/// Die Kopplung an den Bildschirmtakt. Sie ist eine Abweichung von der zeitbasierten
/// Wiedergabe und darf deshalb nur dort greifen, wo sie gemessen etwas bringt:
/// wenn die Sollrate praktisch auf dem Schirmtakt liegt. Bei 30 fps auf 60 Hz war
/// dieselbe Technik in der Messung unbrauchbar - 18,3 statt 30 Bilder je Sekunde,
/// weil jeder ausgelassene Kompositionsschritt dort gleich einen halben Frame kostet.
/// </summary>
public static class CadenceInvariants
{
    public static void Run()
    {
        LockOnlyWhereItHelps();
        LockedAdvancesOncePerRefresh();
        SwitchingModesDoesNotJump();
        DisabledBehavesAsBefore();
        Estimator();
    }

    private static PlaybackClock Running(double fps, double hz, double delivered, bool allow = true)
    {
        var clock = new PlaybackClock { Fps = fps, LockToDisplay = allow };
        clock.ObserveDisplay(hz, delivered);
        clock.Start(0);
        clock.Tick();
        return clock;
    }

    // ---------------------------------------------------------------- Wann gekoppelt wird

    private static void LockOnlyWhereItHelps()
    {
        Check.Group("Bildschirmkopplung - nur bei passender Bildrate");

        Check.That(Running(60, 60, 60).IsLockedToDisplay,
            "60 fps auf 60 Hz wird gekoppelt");
        Check.That(Running(60, 59.94, 59.9).IsLockedToDisplay,
            "59,94 Hz zaehlt als dieselbe Rate");
        Check.That(Running(24, 23.976, 23.9).IsLockedToDisplay,
            "24 fps auf einem 24-Hz-Schirm ebenso");

        Check.That(!Running(30, 60, 60).IsLockedToDisplay,
            "30 fps auf 60 Hz bleibt zeitbasiert - dort ist Reserve vorhanden");
        Check.That(!Running(24, 60, 60).IsLockedToDisplay,
            "24 fps auf 60 Hz ebenso");
        Check.That(!Running(60, 144, 143).IsLockedToDisplay,
            "60 fps auf 144 Hz ebenso");
        Check.That(!Running(60, 0, 0).IsLockedToDisplay,
            "ohne Messung wird nicht gekoppelt");

        // Der Schirm nennt 60 Hz, liefert aber nur 40: koppeln hiesse Zeitlupe.
        Check.That(!Running(60, 60, 40).IsLockedToDisplay,
            "bricht die Lieferung ein, wird die Kopplung geloest", "40 von 60 Hz");
        Check.That(Running(60, 60, 50).IsLockedToDisplay,
            "kleinere Aussetzer loesen sie dagegen nicht", "50 von 60 Hz");
    }

    // ---------------------------------------------------------------- Was sie bewirkt

    private static void LockedAdvancesOncePerRefresh()
    {
        Check.Group("Bildschirmkopplung - ein Bild je Zeichenschritt");

        var clock = Running(60, 60, 60);
        long start = clock.RawTarget;

        for (int i = 0; i < 60; i++) clock.Tick();

        Check.That(clock.RawTarget - start == 60,
            "60 Schritte ergeben genau 60 Bilder", $"{clock.RawTarget - start}");

        // Kein Standbild und kein Sprung: jeder Schritt genau eins weiter.
        var steady = Running(60, 60, 60);
        long previous = steady.RawTarget;
        int holds = 0, jumps = 0;

        for (int i = 0; i < 300; i++)
        {
            steady.Tick();
            long now = steady.RawTarget;
            if (now == previous) holds++;
            else if (now - previous > 1) jumps++;
            previous = now;
        }

        Check.That(holds == 0 && jumps == 0,
            "ueber 300 Schritte kein Standbild und kein Sprung", $"{holds} / {jumps}");

        // 59,94 Hz bei 60 fps Soll: die Sollrate bleibt massgeblich, deshalb faellt
        // etwa alle tausend Schritte ein zweites Bild auf denselben Schritt.
        var pulldown = Running(60, 59.94, 59.9);
        long before = pulldown.RawTarget;
        for (int i = 0; i < 6000; i++) pulldown.Tick();
        long advanced = pulldown.RawTarget - before;

        Check.That(advanced is >= 6000 and <= 6010,
            "bei 59,94 Hz bleibt die Zahl der Bilder nahe der Sollrate",
            $"{advanced} auf 6000 Schritte");
    }

    // ---------------------------------------------------------------- Umschalten

    private static void SwitchingModesDoesNotJump()
    {
        Check.Group("Bildschirmkopplung - Umschalten ohne Sprung");

        var clock = new PlaybackClock { Fps = 60, LockToDisplay = true };
        clock.Start(100);

        // Noch keine Messung: zeitbasiert.
        clock.Tick();
        Check.That(!clock.IsLockedToDisplay, "ohne Messung laeuft die Uhr");
        long beforeLock = clock.RawTarget;

        // Messung liegt vor, es wird gekoppelt.
        clock.ObserveDisplay(60, 60);
        clock.Tick();
        Check.That(clock.IsLockedToDisplay, "mit Messung wird gekoppelt");
        Check.That(clock.RawTarget >= beforeLock,
            "die Position laeuft dabei nicht zurueck", $"{beforeLock} -> {clock.RawTarget}");

        for (int i = 0; i < 30; i++) clock.Tick();
        long locked = clock.RawTarget;

        // Der Schirm bricht ein: zurueck zur Uhr, wieder ohne Sprung.
        clock.ObserveDisplay(60, 30);
        clock.Tick();
        Check.That(!clock.IsLockedToDisplay, "bricht der Schirm ein, uebernimmt die Uhr wieder");
        Check.That(clock.RawTarget >= locked,
            "auch dabei laeuft die Position nicht zurueck", $"{locked} -> {clock.RawTarget}");

        // Ein Sprung im Bild setzt beides zurueck.
        clock.Seek(500);
        Check.That(clock.RawTarget == 500, "ein Sprung setzt die Position genau", $"{clock.RawTarget}");
    }

    // ---------------------------------------------------------------- Abschaltbar

    private static void DisabledBehavesAsBefore()
    {
        Check.Group("Bildschirmkopplung - abgeschaltet bleibt alles beim Alten");

        var clock = Running(60, 60, 60, allow: false);

        Check.That(!clock.IsLockedToDisplay, "ausgeschaltet wird nie gekoppelt");

        for (int i = 0; i < 500; i++) clock.Tick();

        // Ohne Kopplung darf Tick die Position nicht beeinflussen - sie kommt dann
        // ausschliesslich aus der Uhr, so wie die Vorgabe es verlangt.
        long viaTicks = clock.RawTarget;
        System.Threading.Thread.Sleep(120);
        Check.That(clock.RawTarget > viaTicks,
            "die Position folgt der verstrichenen Zeit, nicht der Zahl der Aufrufe",
            $"{viaTicks} -> {clock.RawTarget}");

        var idle = new PlaybackClock { Fps = 60 };
        idle.Tick();
        Check.That(idle.RawTarget == 0, "im Stillstand bewegt Tick nichts");
    }

    // ---------------------------------------------------------------- Die Messung selbst

    private static void Estimator()
    {
        Check.Group("Bildschirmtakt - Messung");

        var estimator = new RefreshEstimator();
        Check.That(!estimator.HasEstimate, "ohne Werte gibt es keine Schaetzung");

        double now = 0;
        for (int i = 0; i < 100; i++) { now += 1000.0 / 60; estimator.Sample(now); }

        Check.Near(estimator.NominalHz, 60, 0.6, "gleichmaessige Schritte ergeben 60 Hz");
        Check.Near(estimator.EffectiveHz, 60, 0.6, "und dieselbe Lieferrate");

        // Jeder vierte Schritt faellt aus: der Takt bleibt 60 Hz, die Lieferung faellt.
        estimator.Reset();
        now = 0;
        for (int i = 0; i < 200; i++)
        {
            now += (i % 4 == 0 ? 2 : 1) * 1000.0 / 60;
            estimator.Sample(now);
        }

        Check.Near(estimator.NominalHz, 60, 1.0,
            $"ausgelassene Schritte verschieben den Median nicht ({estimator.NominalHz:0.0} Hz)");
        Check.That(estimator.EffectiveHz < 50,
            "die Lieferrate zeigt den Ausfall dagegen an", $"{estimator.EffectiveHz:0.0} Hz");

        // Eine Pause ist kein Takt.
        estimator.Sample(now + 4000);
        Check.That(!estimator.HasEstimate, "nach einer Pause wird neu gemessen");
        Check.That(estimator.NominalHz == 0, "und bis dahin nichts behauptet");
    }
}
