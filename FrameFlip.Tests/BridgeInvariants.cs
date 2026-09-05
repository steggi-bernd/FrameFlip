using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using FrameFlip.Bridge;

namespace FrameFlip.Tests;

/// <summary>
/// Die Bruecke zum Blender-Addon.
///
/// Zwei Dinge stehen hier im Mittelpunkt. Erstens: Der Statustext ist keine
/// Schnittstelle - er darf sich aendern, ohne dass etwas kaputtgeht. Zweitens: Die
/// verlaesslichen Zahlen kommen aus den Ereignissen, nicht aus dem Text.
/// </summary>
public static class BridgeInvariants
{
    public static void Run()
    {
        ParsesWhatBlenderSends();
        SurvivesGarbage();
        ProgressComesFromEvents();
        EstimatesFromMeasuredFrames();
        SpeaksOverLoopback();
        RejectsWrongToken();
    }

    // ---------------------------------------------------------------- Statustext

    private static void ParsesWhatBlenderSends()
    {
        Check.Group("Bruecke - Statustext lesen");

        // So sieht er im Hintergrundmodus aus, zusammengesetzt in session.cpp.
        var full = StatsParser.Parse(
            "Remaining: 01:23.45 | Mem: 1234M | Scene, ViewLayer | Rendering | Sample 42/128");

        Check.That(full.Sample == 42 && full.SampleTotal == 128,
            "Cycles: Sample 42/128", $"{full.Sample}/{full.SampleTotal}");
        Check.That(full.MemoryMb == 1234, "Speicher in Megabyte", $"{full.MemoryMb}");
        Check.Near(full.FrameRemaining?.TotalSeconds ?? -1, 83.45, 0.01,
            "Restzeit 01:23.45 sind 83,45 Sekunden");
        Check.That(full.Activity == "Rendering", "Taetigkeit", $"{full.Activity}");
        Check.Near(full.SampleProgress ?? -1, 42 / 128.0, 1e-9, "Sample-Fortschritt");

        // In der Oberflaeche fehlen Restzeit und Speicher - das ist kein Fehler.
        var lean = StatsParser.Parse("Scene, ViewLayer | Sample 7/64");
        Check.That(lean.Sample == 7 && lean.SampleTotal == 64, "auch ohne die Zusaetze");
        Check.That(lean.MemoryMb is null && lean.FrameRemaining is null,
            "was nicht dasteht, wird nicht erfunden");

        // Aufgezeichnet aus einem echten Lauf mit Blender 5.1 und EEVEE: Die Zahlen
        // stehen VOR dem Wort, es gibt weder Trennstriche noch Speicher noch Restzeit.
        var eevee = StatsParser.Parse("Rendering 1 / 64 samples");
        Check.That(eevee.Sample == 1 && eevee.SampleTotal == 64,
            "EEVEE (gemessen): Rendering 1 / 64 samples", $"{eevee.Sample}/{eevee.SampleTotal}");
        Check.That(eevee.Activity == "Rendering 1 / 64 samples" || eevee.Activity is null,
            "der Text bleibt als Taetigkeit lesbar", $"{eevee.Activity}");

        // Ebenfalls gemessen: Nach jedem Frame kommt ueber denselben Handler eine
        // Abschlussmeldung. Sie beschreibt keine laufende Taetigkeit.
        var finished = StatsParser.Parse("Time: 00:00.23 (Saving: 00:00.00)");
        Check.That(finished.Activity is null,
            "die Abschlussmeldung wird nicht als Taetigkeit angezeigt", $"{finished.Activity}");
        Check.That(finished.Sample is null, "und enthaelt keinen Sample-Zaehler");

        // Gigabyte kommen vor, sobald die Szene groesser wird.
        Check.That(StatsParser.Parse("Mem: 2.5G | Rendering").MemoryMb == 2560,
            "2,5G sind 2560 MB");

        // Stunden in der Restzeit.
        Check.Near(StatsParser.Parse("Remaining: 2:03:04.00").FrameRemaining?.TotalSeconds ?? -1,
            7384, 0.01, "Restzeit mit Stunden");

        // Ein Zustand ohne Zahlen ist trotzdem eine Information.
        var loading = StatsParser.Parse("Loading render kernels (may take a few minutes the first time)");
        Check.That(loading.Activity is not null && loading.Activity.StartsWith("Loading"),
            "eine Taetigkeit ohne Zahlen bleibt lesbar", $"{loading.Activity}");
        Check.That(loading.Sample is null, "und erfindet keinen Sample-Zaehler");
    }

    private static void SurvivesGarbage()
    {
        Check.Group("Bruecke - unbrauchbarer Text bleibt folgenlos");

        foreach (var text in new[]
        {
            null, "", "   ", "|||", "Sample", "Sample /", "Sample x/y", "Mem:", "Mem: abcM",
            "Remaining:", "Remaining: ::", "Sample 999999999999999999999/1",
            "samples", "/ samples", new string('x', 5000),
        })
        {
            var stats = StatsParser.Parse(text);
            Check.That(stats.SampleProgress is null or (>= 0 and <= 1),
                $"\"{Shorten(text)}\" ergibt keinen unsinnigen Fortschritt");
        }

        Check.That(StatsParser.Parse(null).IsEmpty, "null ergibt nichts");
        Check.That(StatsParser.Parse("Sample 300/128").SampleProgress == 1.0,
            "mehr Samples als angekuendigt wird auf eins begrenzt");
    }

    private static string Shorten(string? text)
        => text is null ? "null" : text.Length <= 18 ? text : text[..15] + "…";

    // ---------------------------------------------------------------- Fortschritt

    private static RenderJob NewJob(int first = 1, int last = 10)
        => new() { Id = "test", FirstFrame = first, LastFrame = last };

    private static void ProgressComesFromEvents()
    {
        Check.Group("Bruecke - Fortschritt zaehlt Ereignisse, nicht Text");

        var job = NewJob(1, 10);

        Check.That(job.TotalFrames == 10, "zehn Frames im Auftrag", $"{job.TotalFrames}");
        Check.That(job.Progress == 0, "am Anfang steht der Balken auf null");

        job.BeginFrame(1);
        job.FrameWritten(1, @"C:\out\f_0001.png");
        job.FrameWritten(2, @"C:\out\f_0002.png");

        Check.Near(job.Progress, 0.2, 1e-9, "zwei von zehn sind ein Fuenftel");
        Check.That(job.LatestFrameFile!.EndsWith("f_0002.png"),
            "der zuletzt geschriebene Frame ist die Vorschau");

        // Der laufende Frame zaehlt anteilig mit - sonst stuende der Balken bei einer
        // kurzen Sequenz minutenlang still.
        job.BeginFrame(3);
        job.UpdateStats(StatsParser.Parse("Sample 64/128"));

        Check.Near(job.Progress, 0.25, 1e-9,
            "der halb fertige dritte Frame zaehlt zur Haelfte");

        // Ein fertiger Frame setzt den Sample-Zaehler zurueck: Er gehoerte zum alten.
        job.FrameWritten(3, null);
        Check.That(job.Stats.Sample is null, "nach dem Schreiben gilt der Zaehler nicht mehr");
        Check.Near(job.Progress, 0.3, 1e-9, "und der Balken steht auf drei Zehnteln");

        // Ein Statustext ohne Zahlen darf vorhandene nicht loeschen.
        job.UpdateStats(StatsParser.Parse("Sample 10/128"));
        job.UpdateStats(StatsParser.Parse("Updating Scene"));
        Check.That(job.Stats.Sample == 10, "eine leere Meldung verdraengt nichts", $"{job.Stats.Sample}");
        Check.That(job.Stats.Activity == "Updating Scene", "die Taetigkeit wird trotzdem aktuell");

        // Ein Einzelbild ist ein Auftrag aus einem Frame, kein Sonderfall.
        var single = NewJob(7, 7);
        Check.That(single.TotalFrames == 1, "ein Einzelbild zaehlt als ein Frame");
        single.FrameWritten(7, null);
        Check.Near(single.Progress, 1.0, 1e-9, "und ist danach fertig");

        // Mehr Frames als angekuendigt darf den Balken nicht ueber eins treiben.
        var overrun = NewJob(1, 2);
        for (int i = 0; i < 5; i++) overrun.FrameWritten(i, null);
        Check.That(overrun.Progress <= 1.0, "der Balken bleibt bei eins stehen", $"{overrun.Progress}");
    }

    private static void EstimatesFromMeasuredFrames()
    {
        Check.Group("Bruecke - Restzeit aus gemessenen Frames");

        var job = NewJob(1, 100);

        Check.That(job.Remaining is null, "ohne einen fertigen Frame gibt es keine Schaetzung");
        Check.That(job.SecondsPerFrame is null, "und keine Dauer je Frame");

        job.BeginFrame(1);
        Thread.Sleep(120);
        job.FrameWritten(1, null);

        Check.That(job.SecondsPerFrame is > 0.05 and < 2.0,
            "die gemessene Dauer ist plausibel", $"{job.SecondsPerFrame:0.000} s");

        var remaining = job.Remaining;
        Check.That(remaining is not null, "mit einer Messung gibt es eine Schaetzung");
        Check.That(remaining!.Value.TotalSeconds > 5,
            "99 Frames zu je gut 0,1 s sind ueber zehn Sekunden",
            $"{remaining.Value.TotalSeconds:0.0} s");

        // Ein fertiger Auftrag hat keine Restzeit mehr.
        job.Finish(JobState.Finished);
        Check.That(job.Remaining is null, "nach dem Ende gibt es nichts mehr zu warten");
        Check.That(!job.IsRunning, "und der Auftrag laeuft nicht mehr");
        Check.That(job.EndedUtc is not null, "das Ende wird festgehalten");
    }

    // ---------------------------------------------------------------- Verbindung

    /// <summary>Spielt den Addon: verbindet sich und schickt Zeilen.</summary>
    private static void Send(int port, string token, params string[] lines)
    {
        using var client = new TcpClient();
        client.Connect("127.0.0.1", port);

        using var stream = client.GetStream();

        var all = new StringBuilder();
        all.Append($"{{\"type\":\"hello\",\"token\":\"{token}\"}}\n");
        foreach (var line in lines) all.Append(line).Append('\n');

        var bytes = Encoding.UTF8.GetBytes(all.ToString());
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        Thread.Sleep(150);            // dem Empfaenger Zeit lassen
    }

    private static int FreePort()
    {
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void SpeaksOverLoopback()
    {
        Check.Group("Bruecke - Verbindung ueber Loopback");

        int port = FreePort();
        string token = "";

        var received = new List<BridgeMessage>();
        using var server = new BridgeServer(port, token = "0123456789abcdef");
        server.MessageReceived += m => { lock (received) received.Add(m); };
        server.Start();

        Check.That(server.IsListening, "der Empfaenger lauscht", $"Port {port}");

        Send(port, token,
            """{"type":"init","job":"j1","file":"C:\\x.blend","first":1,"last":4,"engine":"CYCLES"}""",
            """{"type":"pre","job":"j1","frame":1}""",
            """{"type":"write","job":"j1","frame":1,"path":"C:\\out\\f_0001.png"}""",
            """{"type":"stats","job":"j1","text":"Sample 5/16"}""",
            "das ist kein JSON",
            """{"type":"done","job":"j1"}""");

        lock (received)
        {
            Check.That(received.Count == 5,
                "fuenf gueltige Zeilen kommen an, die unbrauchbare wird verworfen",
                $"{received.Count}");

            Check.That(received[0].Type == "init" && received[0].Last == 4,
                "der Auftrag wird vollstaendig gelesen");
            Check.That(received[2].Path!.EndsWith("f_0001.png"),
                "Pfade ueberstehen die Uebertragung", received[2].Path!);
            Check.That(received[^1].Type == "done", "und das Ende kommt an");
        }
    }

    private static void RejectsWrongToken()
    {
        Check.Group("Bruecke - falsches Token wird abgewiesen");

        int port = FreePort();

        int count = 0;
        using var server = new BridgeServer(port, "richtiges-token-0123");
        server.MessageReceived += _ => Interlocked.Increment(ref count);
        server.Start();

        Send(port, "falsches-token-9999", """{"type":"init","job":"x","first":1,"last":9}""");

        Check.That(Volatile.Read(ref count) == 0,
            "mit falschem Token kommt nichts durch", $"{count} Meldungen");

        // Ohne Begruessung ebenso: die erste Zeile MUSS der Handschlag sein.
        using (var client = new TcpClient())
        {
            client.Connect("127.0.0.1", port);
            var bytes = Encoding.UTF8.GetBytes("""{"type":"init","job":"y","first":1,"last":9}""" + "\n");
            client.GetStream().Write(bytes, 0, bytes.Length);
            Thread.Sleep(120);
        }

        Check.That(Volatile.Read(ref count) == 0, "und ohne Begruessung auch nicht");

        // Mit dem richtigen Token dagegen schon - sonst prueft der Test nichts.
        Send(port, "richtiges-token-0123", """{"type":"init","job":"z","first":1,"last":9}""");
        Check.That(Volatile.Read(ref count) == 1,
            "mit richtigem Token geht es durch", $"{count}");
    }
}
