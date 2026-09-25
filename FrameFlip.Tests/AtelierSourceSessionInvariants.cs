using System.Collections.Concurrent;
using FrameFlip.Atelier;
using FrameFlip.Imaging;

namespace FrameFlip.Tests;

/// <summary>
/// Die Quellsitzung des Ateliers ohne Fenster: Zugestellt wird nur, was noch gilt -
/// nach einem neueren Oeffnen, nach A, B, A und nach dem Ende nichts Altes mehr.
/// </summary>
public static class AtelierSourceSessionInvariants
{
    public static void Run()
    {
        Check.Group("Quellsitzung: nur was noch gilt");

        var posted = new ConcurrentQueue<Action>();
        var gates = new ConcurrentDictionary<string, ManualResetEventSlim>();
        int reads = 0;

        AtelierSource Read(string path)
        {
            Interlocked.Increment(ref reads);
            if (gates.TryGetValue(path, out var gate)) gate.Wait(TimeSpan.FromSeconds(5));
            if (path.EndsWith("wirft", StringComparison.Ordinal)) throw new InvalidOperationException("Probe");

            return new AtelierSource(path, Frame(path.Length), Array.Empty<Decoding.Exr.ExrPass>(), Array.Empty<Decoding.Exr.CryptomatteSet>());
        }

        // Zustellen wie der Oberflaechenfaden: erst, wenn die Probe es abarbeitet.
        void Drain()
        {
            while (posted.TryDequeue(out var action)) action();
        }

        var shown = new List<AtelierSource>();
        var session = new AtelierSourceSession(Read, posted.Enqueue);

        // A langsam, dann B.
        var slowA = new ManualResetEventSlim(false);
        gates["a"] = slowA;

        var first = session.Open("a", shown.Add);
        var second = session.Open("bb", shown.Add);
        second.Wait(TimeSpan.FromSeconds(5));
        Drain();

        slowA.Set();
        first.Wait(TimeSpan.FromSeconds(5));
        Drain();

        Check.That(shown.Select(s => s.Path).SequenceEqual(new[] { "bb" }) && session.Path == "bb",
                   "A langsam, dann B: nur B wird zugestellt", string.Join(", ", shown.Select(s => s.Path)));

        // A langsam, B, A schnell: Das erste A kommt zuletzt und verfaellt.
        shown.Clear();
        var slowAgain = new ManualResetEventSlim(false);
        gates["a"] = slowAgain;

        var old = session.Open("a", shown.Add);
        gates.TryRemove("a", out _);
        session.Open("bb", shown.Add).Wait(TimeSpan.FromSeconds(5));
        var fresh = session.Open("a", shown.Add);
        fresh.Wait(TimeSpan.FromSeconds(5));
        Drain();

        slowAgain.Set();
        old.Wait(TimeSpan.FromSeconds(5));
        Drain();

        Check.That(shown.Count == 1 && shown[0].Path == "a",
                   "A, B, A: nur das zweite A - das erste traegt eine alte Nummer", string.Join(", ", shown.Select(s => s.Path)));

        // Ein Lesen, das wirft, ist ein unlesbares Bild - kein Absturz, keine Stille.
        shown.Clear();
        session.Open("wirft", shown.Add).Wait(TimeSpan.FromSeconds(5));
        Drain();

        Check.That(shown.Count == 1 && shown[0].Frame is null && shown[0].Path == "wirft",
                   "ein Lesefehler wird als unlesbar zugestellt");

        // Nummern: jede Anfrage eine neue, und nur die letzte gilt.
        long before = session.Opened;
        session.Open("c", _ => { }).Wait(TimeSpan.FromSeconds(5));
        Drain();

        Check.That(session.Opened == before + 1 && session.IsCurrent(session.Opened) && !session.IsCurrent(before),
                   "jede Anfrage bekommt eine neue Nummer, nur die letzte gilt");

        // Das Ende: Was noch gelesen wird, kommt nicht mehr an; neue Anfragen lesen nicht.
        shown.Clear();
        var slowEnd = new ManualResetEventSlim(false);
        gates["d"] = slowEnd;

        var pending = session.Open("d", shown.Add);
        long last = session.Opened;
        session.Dispose();

        slowEnd.Set();
        pending.Wait(TimeSpan.FromSeconds(5));
        Drain();

        int readsBefore = reads;
        session.Open("e", shown.Add).Wait(TimeSpan.FromSeconds(1));
        Drain();

        Check.That(shown.Count == 0 && !session.IsCurrent(last) && reads == readsBefore,
                   "nach dem Ende wird nichts mehr zugestellt und nichts mehr gelesen");
    }

    private static FloatFrame Frame(int width) => new()
    {
        Width = width,
        Height = 1,
        R = new float[width],
        G = new float[width],
        B = new float[width],
    };
}
