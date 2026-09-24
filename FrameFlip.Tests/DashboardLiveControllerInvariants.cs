using System.IO;
using FrameFlip.Dashboard;

namespace FrameFlip.Tests;

public static class DashboardLiveControllerInvariants
{
    public static void Run()
    {
        SourcesAndFallback();
        RetiredWork();
        End();
    }

    private static void SourcesAndFallback()
    {
        Check.Group("Dashboard-Live - Quellen, Buendelung und Bruecken-Fallback");
        using var h = new Harness();
        h.Live.WatchFolder(null);
        h.Live.WatchFolder("");
        h.Live.WatchFolder("missing");
        Check.That(h.Watchers.Count == 0 && h.Timers.Count == 0, "ohne Ausgabeordner werden keine Quellen gestartet");
        h.Live.WatchFolder("first");
        var first = h.Watchers.Single();
        Check.That(first.Folder == "first", "beobachtet wird genau der ausgewaehlte Ordner");
        first.Emit("notes.txt");
        first.Emit("");
        Check.That(h.Posted.Count == 0, "unbekannte Formate werden vor dem Dispatcher gefiltert");
        Task.Run(() => first.Emit("frame_001.PNG")).GetAwaiter().GetResult();
        Check.That(h.Posted.Count == 1 && h.Timers.Count == 0 && h.Rescans == 0,
            "Dateimeldungen vom Hintergrund warten auf den UI-Thread");
        h.Drain();
        h.Live.NoteNewFrames();
        first.Emit("frame_002.png");
        h.Drain();
        var timer = h.Timers.Single();
        Check.That(timer.Restarts == 3 && h.Rescans == 0,
            "Dateimeldungen und Bruecke setzen dieselbe Ruhefrist zurueck");
        timer.Fire();
        timer.Fire();
        Check.That(h.Rescans == 1, "nach der Ruhefrist wird genau einmal neu eingelesen");

        h.FailObserve = true;
        h.Live.WatchFolder("offline");
        Check.That(first.Disposals == 1 && h.Watchers.Count == 1,
            "ein fehlgeschlagener Ordnerwechsel gibt den alten Beobachter frei");
        h.Live.NoteNewFrames();
        h.Timers.Last().Fire();
        Check.That(h.Rescans == 2, "die Bruecke funktioniert auch ohne Dateisystem-Beobachter");
        h.FailObserve = false;
        h.Live.WatchFolder("recovered");
        var recovered = h.Watchers.Last();
        recovered.ThrowOnDispose = true;
        h.Live.WatchFolder("next");
        Check.That(recovered.Disposals == 1 && h.Watchers.Last().Folder == "next",
            "ein Fehler beim Freigeben verhindert den naechsten Ordner nicht");
    }

    private static void RetiredWork()
    {
        Check.Group("Dashboard-Live - Meldungen einer abgeloesten Auswahl");
        using var h = new Harness();
        h.Live.WatchFolder("old");
        var old = h.Watchers.Single();
        old.Emit("queued.png");
        h.Live.WatchFolder("current");
        h.Drain();
        Check.That(h.Timers.All(t => !t.Pending),
            "eine schon eingereihte alte Dateimeldung startet keinen neuen Scan");
        old.Emit("late.png");
        h.Drain();
        Check.That(h.Timers.All(t => !t.Pending),
            "auch erst nach dem Wechsel eintreffende alte Meldungen verfallen");

        h.Watchers.Last().Emit("current.png");
        h.Drain();
        var retiredTimer = h.Timers.Last();
        Check.That(retiredTimer.Pending, "die aktuelle Auswahl sammelt weiterhin neue Frames");
        h.Live.WatchFolder("next");
        Check.That(!retiredTimer.Pending, "der Auswahlwechsel beendet auch eine schon laufende Ruhefrist");
        retiredTimer.FireEvenIfStopped();
        Check.That(h.Rescans == 0, "ein bereits zugestellter alter Timertick liest die neue Folge nicht ein");
        h.Live.NoteNewFrames();
        h.Timers.Last().Fire();
        Check.That(h.Rescans == 1, "die neue Auswahl erhaelt ihren eigenen Scan");

        h.Live.NoteNewFrames();
        var pending = h.Timers.Last();
        h.Live.WatchFolder("missing");
        pending.FireEvenIfStopped();
        Check.That(h.Rescans == 1 && !pending.Pending,
            "auch die Auswahl eines fehlenden Ordners verwirft alte Arbeit");
    }

    private static void End()
    {
        Check.Group("Dashboard-Live - Beenden und spaete Rueckrufe");
        using var h = new Harness();
        h.Live.WatchFolder("first");
        var watcher = h.Watchers.Single();
        watcher.Emit("queued.png");
        h.Live.NoteNewFrames();
        var timer = h.Timers.Single();
        h.Live.Dispose();
        h.Live.Dispose();
        Check.That(watcher.Disposals == 1 && timer.Disposals == 1,
            "wiederholtes Beenden gibt jede Quelle nur einmal frei");
        h.Drain();
        watcher.Emit("late.png");
        h.Drain();
        timer.FireEvenIfStopped();
        Check.That(!timer.Pending && h.Rescans == 0,
            "eingereihte Meldungen und Timerticks bleiben nach dem Beenden wirkungslos");
        int restarts = h.Timers.Sum(t => t.Restarts);
        h.Live.WatchFolder("after-close");
        h.Live.NoteNewFrames();
        Check.That(h.Watchers.Count == 1 && h.Timers.Sum(t => t.Restarts) == restarts,
            "spaete Aufrufe erzeugen nach dem Beenden weder Beobachter noch Ruhefrist");
    }

    private sealed class Harness : IDisposable
    {
        internal List<Observer> Watchers { get; } = new();
        internal List<Timer> Timers { get; } = new();
        internal Queue<Action> Posted { get; } = new();
        internal DashboardLiveController Live { get; }
        internal int Rescans;
        internal bool FailObserve;

        internal Harness()
        {
            var sources = new DashboardLiveSources(folder => folder != "missing", (folder, callback) =>
            {
                if (FailObserve) throw new IOException("synthetic unavailable folder");
                var watcher = new Observer(folder, callback);
                Watchers.Add(watcher);
                return watcher;
            }, Posted.Enqueue, callback =>
            {
                var timer = new Timer(callback);
                Timers.Add(timer);
                return timer;
            });
            Live = new DashboardLiveController(sources,
                extension => extension.Equals(".png", StringComparison.OrdinalIgnoreCase), () => Rescans++);
        }

        internal void Drain() { while (Posted.TryDequeue(out var action)) action(); }
        public void Dispose() => Live.Dispose();
    }

    private sealed class Observer(string folder, Action<string> changed) : IDisposable
    {
        internal string Folder => folder;
        internal int Disposals;
        internal bool ThrowOnDispose;
        internal void Emit(string name) => changed(name);
        public void Dispose()
        {
            Disposals++;
            if (ThrowOnDispose) throw new IOException("synthetic dispose failure");
        }
    }

    private sealed class Timer(Action settled) : IDashboardLiveTimer
    {
        internal int Restarts;
        internal int Disposals;
        internal bool Pending;
        public void Restart() { Restarts++; Pending = true; }
        internal void Fire() { if (Pending) { Pending = false; settled(); } }
        internal void FireEvenIfStopped() => settled();
        public void Dispose() { Disposals++; Pending = false; }
    }
}
