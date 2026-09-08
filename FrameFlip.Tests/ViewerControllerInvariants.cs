using FrameFlip.Playback;

namespace FrameFlip.Tests;

public static class ViewerControllerInvariants
{
    public static void Run()
    {
        Buffering();
        Navigation();
        SequenceChange();
    }

    private static void Buffering()
    {
        Check.Group("Viewer-Controller - Puffergrenzen ohne Warten");
        long now = 100;
        var session = new ViewerPlaybackController(100, 20, true, () => now);
        session.Clock.Fps = 24;
        Check.That(session.WarmupTarget(0, 60, 100) == 36, "automatischer Vorlauf entspricht anderthalb Sekunden");
        Check.That(session.WarmupTarget(99, 60, 8) == 7, "der aktuelle Frame belegt einen Platz im Ring");
        Check.That(session.WarmupTarget(99, 4, 100) == 4, "mehr als der Prefetch wird nicht verlangt");
        Check.That(session.WarmupTarget(1, 1, 1) == 2, "die bisherige Untergrenze von zwei Frames bleibt erhalten");

        session.Play();
        session.MarkPresented(23);
        session.ResolveTarget(30, out _);
        session.EnterBuffering(true);
        Check.That(session.Index == 23 && !session.IsPlaying && !session.Clock.IsRunning,
                   "Nachpuffern haelt Uhr und Position am letzten sichtbaren Bild an");
        Check.That(session.CompleteBuffering(35, 36, 36) == BufferCompletion.Waiting
                   && session.IsBuffering && session.ResumeAfterBuffering, "ein Frame zu wenig wartet weiter");
        Check.That(session.CompleteBuffering(36, 36, 37) == BufferCompletion.Resume
                   && !session.IsBuffering && !session.ResumeAfterBuffering, "am Ziel wird der Neustart genau einmal freigegeben");

        session.EnterBuffering(false);
        Check.That(session.CompleteBuffering(0, 36, 100) == BufferCompletion.Paused,
                   "ein vollstaendiger Ring beendet auch bei null Vorlauf das Puffern ohne Play-Wunsch");
        session.EnterBuffering(true);
        now += 8000;
        Check.That(session.CompleteBuffering(0, 36, 0) == BufferCompletion.Waiting,
                   "nach genau acht Sekunden gilt noch die bisherige Wartegrenze");
        now++;
        Check.That(session.CompleteBuffering(0, 36, 0) == BufferCompletion.Resume,
                   "nach mehr als acht Sekunden greift der Notausstieg");
        session.EnterBuffering(true);
        now += 5000;
        session.EnterBuffering(false);
        now += 3001;
        Check.That(session.CompleteBuffering(0, 36, 0) == BufferCompletion.Waiting,
                   "erneutes Puffern beginnt eine neue Frist und uebernimmt den neuen Play-Wunsch");
        session.Pause();
        Check.That(!session.IsBuffering && !session.ResumeAfterBuffering && !session.Clock.IsRunning,
                   "Pause verwirft den Neustart auch waehrend des zweiten Pufferns");

        session.Seek(20);
        session.SetInPoint();
        session.Seek(24);
        session.SetOutPoint();
        Check.That(session.WarmupTarget(99, 60, 100) == 4, "ein kurzer Ausschnitt begrenzt den Vorlauf");
        session.EnterBuffering(true);
        Check.That(session.CompleteBuffering(0, 4, 5) == BufferCompletion.Resume,
                   "der vollstaendige Ausschnitt reicht, auch wenn der Rest nicht im Ring liegt");
    }

    private static void Navigation()
    {
        Check.Group("Viewer-Controller - sichtbares Bild, Uhr und aktiver Bereich");
        var session = new ViewerPlaybackController(20, -5, false);
        Check.That(session.Index == 0, "Start vor dem Anfang wird auf null begrenzt");
        session.Seek(5);
        session.SetInPoint();
        session.Seek(8);
        session.SetOutPoint();
        Check.That(session.ResolveTarget(9, out bool pastEnd) == 8 && pastEnd && session.Index == 8,
                   "ohne Loop endet die Uhr am Out-Punkt");
        session.Loop = true;
        Check.That(session.ResolveTarget(9, out pastEnd) == 5 && !pastEnd,
                   "mit Loop wird das Uhrziel innerhalb des Ausschnitts aufgeloest");
        session.MarkPresented(5);
        session.ResolveTarget(7, out _);
        Check.That(session.ResolveTarget(5, out _) == 5 && session.Index == 7,
                   "ein schon gezeigtes Uhrziel verschiebt das Vorausladeziel nicht");
        session.Seek(6);
        session.Clock.Fps = 60;
        session.Clock.LockToDisplay = true;
        session.Clock.ObserveDisplay(60, 60);
        session.Play();
        session.Clock.Tick();
        session.Seek(8);
        Check.That(session.Clock.RawTarget == 8 && session.IsPlaying,
                   "ein Sprung beim Abspielen verankert die laufende Uhr neu");
        session.MarkPresented(7);
        session.Pause();
        Check.That(session.Index == 7, "Pause folgt dem sichtbaren Fallback, nicht der Uhr");
        session.Step(-1);
        Check.That(session.Index == 6 && session.Direction == -1, "Rueckwaertsnavigation setzt die Richtung");
        session.Play();
        Check.That(session.Direction == 1, "Play laeuft nach Rueckwaertsnavigation wieder vorwaerts");
        session.Pause();
        session.Seek(10);
        session.SetInPoint();
        Check.That(session.InPoint == 10 && session.OutPoint == -1, "neuer In-Punkt hinter Out verwirft Out");
        session.Seek(4);
        session.SetOutPoint();
        Check.That(session.InPoint == -1 && session.OutPoint == 4, "neuer Out-Punkt vor In verwirft In");
        session.ClearRange();
        Check.That(!session.HasRange && session.ActiveRange() == (0, 19), "Bereich loeschen stellt die ganze Sequenz her");
    }

    private static void SequenceChange()
    {
        Check.Group("Viewer-Controller - Sequenzwechsel und Beenden");
        var session = new ViewerPlaybackController(20, 15, true);
        session.Clock.Fps = 30;
        session.SetInPoint();
        session.MarkPresented(15);
        session.Play();
        session.EnterBuffering(true);
        session.ResetSequence(3, 99);
        Check.That(session.FrameCount == 3 && session.Index == 2 && session.ShownIndex == -1,
                   "neue Sequenz begrenzt den Start und verwirft das alte sichtbare Bild");
        Check.That(!session.IsPlaying && !session.IsBuffering && !session.ResumeAfterBuffering
                   && !session.HasRange && session.Direction == 1, "alter Wiedergabe- und Bereichszustand wird geloescht");
        Check.That(session.Loop && session.Clock.Fps == 30, "Loop und Bildrate bleiben beim Sequenzwechsel erhalten");
        session.Play();
        session.Stop();
        Check.That(!session.IsPlaying && !session.Clock.IsRunning, "Beenden stoppt auch die Uhr");
        session.ResetSequence(1, 0);
        Check.That(!session.Play() && !session.EnterBuffering(true), "Einzelbild startet weder Wiedergabe noch Puffern");
    }
}
