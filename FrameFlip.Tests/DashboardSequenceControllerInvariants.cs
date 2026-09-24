using System.Collections.Concurrent;
using System.IO;
using FrameFlip.Dashboard;
using FrameFlip.Projects;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

public static class DashboardSequenceControllerInvariants
{
    public static void Run()
    {
        ReadsAndSelection();
        Delivery();
        NewerSelection();
        RetiredLists();
        BoundedReaders();
        ErrorsAndEnd();
        ClosingReader();
    }

    private static void ReadsAndSelection()
    {
        Check.Group("Dashboard-Auswahl - Seeds, Fehler und Live-Ergebnisse");
        using var h = new Harness();
        var a = h.Known("a", 1, 3);
        var b = h.Known("b", 10, 11);
        h.Library.AddRange(new[] { a, b });
        h.Controller.Reload();
        Check.That(h.Controller.Current?.BlendPath == a.Path && h.Controller.Sequence?.Count == 2,
            "die erste Ausgabe bestimmt die Anfangsauswahl");
        Check.That(h.Controller.Current?.Missing == 1 && h.Controller.Current.Frames == 2,
            "der synchrone Scan liefert Zeilenmetadaten derselben Folge");
        Check.That(ReferenceEquals(h.Controller.Find(a.Path.ToUpperInvariant()), h.Controller.Current)
                   && ReferenceEquals(h.Controller.Find(a.Seed), h.Controller.Current), "Projekt und Seed treffen dieselbe Auswahl");
        var sequence = h.Controller.Sequence;
        Check.That(!h.Controller.Select(h.Controller.Current!) && ReferenceEquals(sequence, h.Controller.Sequence),
            "erneute Auswahl liest die laufende Folge nicht neu ein");
        Check.That(h.Controller.Rescan() is null && ReferenceEquals(sequence, h.Controller.Sequence),
            "ein unveraenderter Live-Scan behaelt die Folge");
        h.Sequences[a.Seed] = h.Sequence(a.Output, 1, 2, 3, 4);
        var changed = h.Controller.Rescan();
        Check.That(ReferenceEquals(changed?.Previous, sequence) && changed?.Current.EndNumber == 4
                   && h.Controller.Current?.Missing == 0, "ein Live-Ergebnis verbindet alten und neuen Stand samt Metadaten");
        h.Files.TryRemove(a.Seed, out _);
        string fallback = h.AddSequence(a.Output, 8, 9);
        h.First[a.Output] = fallback;
        changed = h.Controller.Rescan();
        Check.That(changed?.Current.StartNumber == 8, "ein verschwundener Seed faellt auf den ersten lesbaren Frame zurueck");
        h.Scan = _ => throw new IOException("synthetic scan failure");
        sequence = h.Controller.Sequence;
        Check.That(h.Controller.Rescan() is null && ReferenceEquals(sequence, h.Controller.Sequence),
            "ein fehlerhafter Live-Scan loescht die angezeigte Folge nicht");
        var next = h.Controller.Find(b.Path)!;
        Check.That(h.Controller.Select(next) && h.Controller.Sequence is null,
            "eine unlesbare neue Auswahl behaelt ihre Zeile ohne fremde Folge");
        h.Scan = path => h.Sequences.GetValueOrDefault(path);
        changed = h.Controller.Rescan();
        Check.That(changed is { Previous: null, Current.Count: 2 }, "der erste erfolgreiche Scan einer leeren Auswahl ist ein eigenes Ergebnis");
        h.Finish();
    }

    private static void Delivery()
    {
        Check.Group("Dashboard-Auswahl - Zaehler auf dem UI-Thread");
        using var h = new Harness();
        h.Library.Add(h.Known("a", 1));
        h.Library.Add(h.Known("b", 4, 6));
        h.Controller.Reload();
        var b = h.Controller.Entries[1];
        h.Wait(h.Controller.CountCompletion);
        Check.That(b.Frames is null && h.Updated.Count == 0 && h.Posted.Count == 1,
            "der Hintergrundscan aendert keine UI-Metadaten vor der Zustellung");
        h.Drain();
        Check.That(b.Frames == 2 && b.Missing == 1 && ReferenceEquals(h.Updated.Single(), b),
            "die UI erhaelt genau die gezaehlte Zeile samt Luecken");
        Check.That(h.UpdateThreads.All(id => id == h.UiThread), "Zeilenrueckrufe laufen auf dem aufrufenden UI-Thread");
        Check.That(h.BackgroundScans == 1, "die bereits gelesene Auswahl wird nicht nochmals im Hintergrund gezaehlt");
    }

    private static void NewerSelection()
    {
        Check.Group("Dashboard-Auswahl - ein neuerer Scan gewinnt gegen alte Zaehler");
        using var h = new Harness();
        var a = h.Known("a", 1);
        var b = h.Known("b", 2, 4);
        h.Library.AddRange(new[] { a, b });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = h.Sequences[b.Seed];
        h.Scan = path =>
        {
            if (path == b.Seed && Environment.CurrentManagedThreadId != h.UiThread)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic reader was not released");
                return old;
            }
            return h.Sequences.GetValueOrDefault(path);
        };
        try
        {
            h.Controller.Reload();
            Check.That(entered.Wait(TimeSpan.FromSeconds(5)), "eine alte Zeilenzaehlung kann gezielt aufgehalten werden");
            var row = h.Controller.Entries[1];
            h.Sequences[b.Seed] = h.Sequence(b.Output, 2, 3, 4, 5);
            h.Controller.Select(row);
            Check.That(row.Frames == 4 && row.Missing == 0, "eine neue Auswahl kann den alten Hintergrundleser ueberholen");
            release.Set();
            h.Finish();
            Check.That(row.Frames == 4 && row.Missing == 0 && h.Updated.Count == 0,
                "der verspaetete Zaehler ueberschreibt den neueren Auswahlscan nicht");
        }
        finally { release.Set(); h.Wait(h.Controller.CountCompletion); }

        h.Scan = path => h.Sequences.GetValueOrDefault(path);
        h.Controller.Select(h.Controller.Entries[0]);
        h.Controller.Reload();
        h.Wait(h.Controller.CountCompletion);
        var queued = h.Controller.Entries[1];
        h.Controller.Select(queued);
        h.Sequences[b.Seed] = h.Sequence(b.Output, 2, 3, 4, 5, 6);
        h.Controller.Rescan();
        h.Drain();
        Check.That(queued.Frames == 5 && h.Updated.Count == 0,
            "auch ein schon eingereihter Zaehler verliert gegen Auswahl und Live-Scan");
    }

    private static void RetiredLists()
    {
        Check.Group("Dashboard-Auswahl - abgelegte Listen");
        using var h = new Harness();
        var a = h.Known("a", 1);
        var b = h.Known("b", 2, 3);
        h.Library.AddRange(new[] { a, b });
        h.Controller.Reload();
        var old = h.Controller.Entries[1];
        h.Wait(h.Controller.CountCompletion);
        h.Library.Clear();
        h.Controller.Reload(true);
        h.Drain();
        Check.That(old.Frames is null && h.Updated.Count == 0,
            "auch eine leere neue Liste verwirft schon eingereihte Zaehler");
        Check.That(h.Controller.Entries.Count == 0 && h.Controller.Current is null && h.Controller.Sequence is null,
            "eine leere Bibliothek hat keine unsichtbare alte Auswahl");
        Check.That(!h.Controller.Select(old), "eine entfernte Zeile kann nicht erneut ausgewaehlt werden");

        h.Library.AddRange(new[] { a, b });
        h.Controller.Reload();
        old = h.Controller.Entries[1];
        h.Wait(h.Controller.CountCompletion);
        h.Library.Remove(b);
        h.Controller.Reload(true);
        h.Drain();
        Check.That(old.Frames is null && h.Updated.Count == 0 && h.Controller.Current?.BlendPath == a.Path,
            "eine neue Liste ohne offene Zaehler verwirft ebenfalls alte Rueckgaben");
    }

    private static void BoundedReaders()
    {
        Check.Group("Dashboard-Auswahl - begrenzte Hintergrundleser");
        using var h = new Harness();
        var a = h.Known("a", 1);
        var b = h.Known("b", 2);
        var c = h.Known("c", 3, 4);
        h.Library.AddRange(new[] { a, b });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int bReads = 0;
        h.Scan = path =>
        {
            if (path == b.Seed)
            {
                Interlocked.Increment(ref bReads);
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic reader was not released");
            }
            return h.Sequences.GetValueOrDefault(path);
        };
        var jobs = new List<Task>();
        try
        {
            h.Controller.Reload();
            jobs.Add(h.Controller.CountCompletion);
            Check.That(entered.Wait(TimeSpan.FromSeconds(5)), "ein bereits laufender Dateizugriff bleibt nachvollziehbar");
            for (int i = 0; i < 8; i++) { h.Controller.Reload(true); jobs.Add(h.Controller.CountCompletion); }
            h.Library[1] = c;
            h.Controller.Reload(true);
            jobs.Add(h.Controller.CountCompletion);
            Check.That(bReads == 1 && h.PeakReaders == 1, "schnelle Neuaufbauten starten keine weiteren parallelen Hintergrundleser");
            release.Set();
            h.Wait(Task.WhenAll(jobs));
            h.Drain();
            Check.That(bReads == 1 && h.PeakReaders == 1, "ueberholte wartende Listen beginnen auch spaeter keinen Scan");
            Check.That(h.Controller.Entries[1].Frames == 2 && h.Updated.Count == 1
                       && h.Updated[0].BlendPath == c.Path, "nur die letzte Liste liefert ihre Zeilenwerte");
        }
        finally { release.Set(); h.Wait(Task.WhenAll(jobs)); }
    }

    private static void ErrorsAndEnd()
    {
        Check.Group("Dashboard-Auswahl - Lesefehler und Beenden");
        using var h = new Harness();
        var a = h.Known("a", 1);
        var b = h.Known("b", 2);
        var c = h.Known("c", 3);
        h.Library.AddRange(new[] { a, b, c });
        h.Scan = path => path == b.Seed ? throw new IOException("synthetic failure") : h.Sequences.GetValueOrDefault(path);
        h.Controller.Reload();
        h.Finish();
        Check.That(h.Controller.Entries[1].Frames is null && h.Controller.Entries[2].Frames == 1,
            "ein unlesbarer Ordner verhindert die Zaehler der folgenden Zeilen nicht");
        h.RejectPosts = true;
        h.Controller.Reload();
        h.Wait(h.Controller.CountCompletion);
        Check.That(h.Controller.CountCompletion.IsCompletedSuccessfully,
            "ein abgewiesener Dispatcher-Aufruf hinterlaesst keinen unbeobachteten Taskfehler");
        h.RejectPosts = false;
        h.Updated.Clear();
        h.Controller.Reload();
        h.Wait(h.Controller.CountCompletion);
        var abandoned = h.Controller.Entries[2];
        h.Controller.Dispose();
        h.Controller.Dispose();
        h.Drain();
        Check.That(abandoned.Frames is null && h.Updated.Count == 0,
            "Beenden verwirft alle noch eingereihten Zeilenwerte");
        int reads = h.LibraryReads;
        h.Controller.Reload();
        Check.That(!h.Controller.AddPath(a.Seed) && !h.Controller.Select(abandoned)
                   && h.Controller.Rescan() is null && h.LibraryReads == reads,
            "spaete Aufrufe nach Dispose starten weder Bibliotheks- noch Sequenzzugriffe");
    }

    private static void ClosingReader()
    {
        Check.Group("Dashboard-Auswahl - Beenden waehrend eines blockierten Dateizugriffs");
        using var h = new Harness();
        var a = h.Known("a", 1);
        var b = h.Known("b", 2);
        var c = h.Known("c", 3);
        h.Library.AddRange(new[] { a, b, c });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int laterReads = 0;
        h.Scan = path =>
        {
            if (path == b.Seed)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic reader was not released");
            }
            if (path == c.Seed) Interlocked.Increment(ref laterReads);
            return h.Sequences.GetValueOrDefault(path);
        };
        try
        {
            h.Controller.Reload();
            Check.That(entered.Wait(TimeSpan.FromSeconds(5)), "der Dateizugriff laeuft beim Schliessen noch");
            var counting = h.Controller.CountCompletion;
            h.Controller.Dispose();
            Check.That(!counting.IsCompleted && h.Controller.Current is null && h.Controller.Entries.Count == 0,
                "Beenden wartet nicht auf einen haengenden Ordner und gibt die Auswahl frei");
            release.Set();
            h.Finish();
            Check.That(laterReads == 0 && h.Updated.Count == 0,
                "nach dem laufenden Zugriff werden weder weitere Ordner gelesen noch alte Werte geliefert");
        }
        finally { release.Set(); h.Wait(h.Controller.CountCompletion); }
    }

    private sealed class Harness : IDisposable
    {
        internal readonly int UiThread = Environment.CurrentManagedThreadId;
        internal List<KnownBlend> Library { get; } = new();
        internal HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal ConcurrentDictionary<string, byte> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, string> First { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal ConcurrentDictionary<string, ImageSequence> Sequences { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal ConcurrentQueue<Action> Posted { get; } = new();
        internal List<DashboardSequenceEntry> Updated { get; } = new();
        internal List<int> UpdateThreads { get; } = new();
        internal Func<string, ImageSequence?> Scan;
        internal DashboardSequenceController Controller { get; }
        internal int LibraryReads, BackgroundScans, PeakReaders;
        internal bool RejectPosts;
        private int _readers;

        internal Harness()
        {
            Scan = path => Sequences.GetValueOrDefault(path);
            var sources = new DashboardSequenceSources(() => { LibraryReads++; return Library.ToArray(); },
                Folders.Contains, Files.ContainsKey, ext => ext.Equals(".png", StringComparison.OrdinalIgnoreCase),
                folder => First.GetValueOrDefault(folder), path =>
                {
                    bool background = Environment.CurrentManagedThreadId != UiThread;
                    if (background)
                    {
                        Interlocked.Increment(ref BackgroundScans);
                        int active = Interlocked.Increment(ref _readers);
                        PeakReaders = Math.Max(PeakReaders, active);
                    }
                    try { return Scan(path); }
                    finally { if (background) Interlocked.Decrement(ref _readers); }
                });
            Controller = new DashboardSequenceController(sources, action =>
                {
                    if (RejectPosts) throw new InvalidOperationException("synthetic stopped dispatcher");
                    Posted.Enqueue(action);
                },
                entry => { Updated.Add(entry); UpdateThreads.Add(Environment.CurrentManagedThreadId); });
        }
        internal KnownBlend Known(string name, params int[] numbers)
        {
            string folder = @"C:\synthetic-dashboard\" + name;
            Folders.Add(folder);
            string seed = AddSequence(folder, numbers);
            First[folder] = seed;
            return new KnownBlend { Path = folder + ".blend", Output = folder, Seed = seed };
        }
        internal string AddSequence(string folder, params int[] numbers)
        {
            var sequence = Sequence(folder, numbers);
            string seed = sequence.Frames[0].Path;
            Files[seed] = 0;
            Sequences[seed] = sequence;
            return seed;
        }
        internal ImageSequence Sequence(string folder, params int[] numbers) => new(
            new SequencePattern(folder, "frame_", 4, "", ".png"),
            numbers.Select(n => new SequenceFrame(n, Path.Combine(folder, $"frame_{n:0000}.png"), $"frame_{n:0000}.png")).ToArray());
        internal void Wait(Task task)
        {
            if (!task.Wait(TimeSpan.FromSeconds(6))) throw new TimeoutException("Dashboard count did not finish");
            task.GetAwaiter().GetResult();
        }
        internal void Drain() { while (Posted.TryDequeue(out var action)) action(); }
        internal void Finish() { Wait(Controller.CountCompletion); Drain(); }
        public void Dispose() { Controller.Dispose(); Wait(Controller.CountCompletion); }
    }
}
