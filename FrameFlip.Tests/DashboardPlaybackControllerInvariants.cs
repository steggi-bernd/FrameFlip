using FrameFlip.Dashboard;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

/// <summary>Wiedergaberegeln des Dashboards ohne Fenster, Zeitgeber oder Dateien.</summary>
public static class DashboardPlaybackControllerInvariants
{
    public static void Run()
    {
        Check.Group("Dashboard-Wiedergabecontroller - Kopf, Takt und Bereich");
        ImageSequence? shown = null;
        var playback = new DashboardPlaybackController(() => shown);
        Check.That(playback.Seek(1) == -1 && !playback.Play() && playback.StepTarget(1) is null
                   && playback.Advance(true, out _) == DashboardTick.None && playback.SetFollow(true) is null,
            "ohne Folge gibt es weder Kopf noch Takt noch Follow-Ziel");
        shown = Sequence(7);
        Check.That(playback.Seek(7) == 0 && !playback.Play() && !playback.IsPlaying,
            "ein einzelnes Bild kann gezeigt, aber nicht abgespielt werden");

        shown = Sequence(1, 2, 3, 5, 6);
        playback.ResetRange(shown);
        Check.That(playback.InPoint == 1 && playback.OutPoint == 6, "eine neue Folge gilt zunaechst ganz");
        Check.That(playback.Seek(4) == 2 && playback.Head == 3 && playback.Seek(100) == 4 && playback.Head == 6
                   && playback.Seek(-5) == 0 && playback.Head == 1,
            "der Kopf landet auf dem naechstgelegenen vorhandenen Frame, bei Gleichstand auf dem frueheren");
        Check.That(playback.Play() && playback.Play() && playback.IsPlaying && playback.Pause() && !playback.Pause(),
            "Abspielen darf erneut starten; Pause meldet nur eine tatsaechlich laufende Wiedergabe");

        playback.Seek(3);
        Check.That(playback.Advance(false, out int next) == DashboardTick.Show && next == 5 && playback.Head == 3,
            "der Takt nennt den naechsten vorhandenen Frame und bewegt den Kopf nicht selbst");
        playback.Seek(6);
        Check.That(playback.Advance(true, out next) == DashboardTick.Show && next == 1
                   && playback.Advance(false, out next) == DashboardTick.Stop && next == 6,
            "am Ende der Folge beginnt die Schleife beim Start oder die Wiedergabe endet");

        playback.Seek(2);
        playback.MarkIn();
        playback.Seek(3);
        playback.MarkOut();
        playback.Seek(5);
        Check.That(playback.Advance(true, out next) == DashboardTick.Show && next == 2
                   && playback.Advance(false, out _) == DashboardTick.Stop,
            "ein Kopf hinter dem gesetzten Ende springt zum Start oder haelt an");
        playback.Seek(1);
        Check.That(playback.Advance(false, out next) == DashboardTick.Show && next == 2,
            "ein Kopf vor dem gesetzten Start laeuft von seiner Stelle aus");
        Check.That(playback.StepTarget(-1) == 1 && playback.StepTarget(+3) == 5 && playback.StepTarget(+99) == 6,
            "Schrittziele zaehlen vorhandene Frames und enden an der Folge, nicht am Bereich");
        playback.Seek(6);
        playback.MarkIn();
        playback.Seek(1);
        playback.MarkOut();
        Check.That(playback.InPoint == 3 && playback.OutPoint == 3,
            "Start und Ende koennen sich begrenzen, aber nicht ueberholen");

        Check.Group("Dashboard-Wiedergabecontroller - wachsende Folge und Follow");
        var before = Sequence(1, 2, 3);
        shown = before;
        playback.ResetRange(before);
        playback.Seek(1);
        shown = Sequence(1, 2, 3, 4);
        Check.That(playback.Rescan(before, shown) == 4 && playback.OutPoint == 4 && playback.InPoint == 1,
            "ein offenes Ende waechst mit, und Follow nennt das neue Bild");
        playback.Seek(2);
        playback.MarkOut();
        before = shown;
        shown = Sequence(1, 2, 3, 4, 5);
        Check.That(playback.Rescan(before, shown) == 5 && playback.OutPoint == 2,
            "ein von Hand gesetztes Ende bleibt stehen, Follow springt trotzdem ans neue Bild");
        playback.SetFollow(false);
        before = shown;
        shown = Sequence(1, 2, 3, 4, 5, 6);
        Check.That(playback.Rescan(before, shown) is null, "ohne Follow gibt es kein Sprungziel");
        playback.SetFollow(true);
        playback.Play();
        before = shown;
        shown = Sequence(1, 2, 3, 4, 5, 6, 7);
        Check.That(playback.Rescan(before, shown) is null && playback.SetFollow(true) is null,
            "waehrend der Wiedergabe springen weder Nachwachsen noch Einschalten von Follow");
        playback.Pause();
        Check.That(playback.SetFollow(true) == 7 && playback.SetFollow(false) is null && !playback.Follow,
            "im Stillstand springt eingeschaltetes Follow ans Ende, ausgeschaltetes nicht");
        before = shown;
        shown = Sequence(3, 4, 5, 6, 7);
        Check.That(playback.Rescan(before, shown) is null && playback.InPoint == 3,
            "ein entfernter Anfang schiebt den Start auf den ersten verbliebenen Frame");
        playback.SetFollow(true);
        playback.Seek(7);
        playback.MarkOut();
        before = shown;
        shown = Sequence(3, 4, 5);
        Check.That(playback.Rescan(before, shown) is null && playback.OutPoint == 5,
            "ein Ende am alten Folgenende schrumpft mit, ohne Follow-Sprung");

        Check.Group("Dashboard-Wiedergabecontroller - Bildrate");
        Check.That(playback.Fps == 24 && playback.Interval == TimeSpan.FromSeconds(1.0 / 24), "ohne Einstellung gelten 24 Bilder je Sekunde");
        playback.UseRate(double.NaN);
        Check.That(playback.Fps == 24, "eine ungueltige gespeicherte Rate faellt auf 24 zurueck");
        playback.UseRate(1000);
        Check.That(playback.Fps == 240, "eine zu hohe gespeicherte Rate wird begrenzt");
        Check.That(playback.TakeRate("12,5") == 12.5 && playback.TakeRate(" 30 ") == 30 && playback.TakeRate(null) == 30
                   && playback.TakeRate("") == 30 && playback.TakeRate("-3") == 24 && playback.TakeRate("0.25") == 1,
            "getippte Raten: Komma, Leerraum, nichts, negativ und zu klein");
        Check.That(playback.Interval == TimeSpan.FromSeconds(1), "der Takt folgt der geltenden Rate");
    }

    private static ImageSequence Sequence(params int[] numbers)
        => new(new SequencePattern("synthetic", "frame_", 4, "", ".png"),
               numbers.Select(n => new SequenceFrame(n, $"synthetic/frame_{n:0000}.png", $"frame_{n:0000}.png")).ToArray());
}
