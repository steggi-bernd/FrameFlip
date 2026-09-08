using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using FrameFlip.Bridge;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Die Eigenheiten der Live-Vorschau, die ein Strukturumbau nicht verlieren darf.
///
/// Der Dienst hat bewusst einen kleinen Kodierer- und Uhr-Rand. Dadurch pruefen
/// diese Tests die Breiten, Framenummern und die Zwei-Sekunden-Grenze direkt,
/// statt fuer jeden Fall ein echtes Bild zu kodieren oder auf die Uhr zu warten.
/// </summary>
public static class RemotePreviewFollowInvariants
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly byte[] FakeJpeg = { 0xFF, 0xD8, 0x00, 0xD9 };

    public static void Run()
    {
        PreviewKeepsWireShape();
        FollowClampsAndThrottles();
        FailureAnswersStayStable();
    }

    private static void PreviewKeepsWireShape()
    {
        Check.Group("Live-Vorschau - Breite und Umschlag");

        RenderJob job = ReadyJob();
        using var harness = new PreviewHarness(() => job);

        Ask(harness.Service, """{"c":"preview"}""");

        if (ExpectPreview(harness, 412, "die Standardvorschau"))
            Check.That(harness.TakeWidth() == PreviewEncoder.Width,
                       "ohne Breite wird die volle Vorschau angefordert");

        Ask(harness.Service, """{"c":"preview","w":1}""");

        if (ExpectPreview(harness, 412, "die kleine Vorschau"))
            Check.That(harness.TakeWidth() == 1,
                       "preview reicht die Breite an den Kodierer weiter, der selbst klemmt");

        Ask(harness.Service, """{"c":"preview","w":99999}""");

        if (ExpectPreview(harness, 412, "die grosse Vorschau"))
            Check.That(harness.TakeWidth() == 99999,
                       "auch grosse Vorschauwerte bleiben beim Kodierer verantwortlich");

        int unknownEncodeCalls = harness.EncodeCalls;
        int unknownSendCalls = harness.SendCalls;
        Ask(harness.Service, """{"c":"etwas-neues"}""");
        Check.That(harness.EncodeCalls == unknownEncodeCalls && harness.SendCalls == unknownSendCalls,
                   "ein fremder Befehl erzeugt keine Vorschau");
    }

    private static void FollowClampsAndThrottles()
    {
        Check.Group("Live-Vorschau - Follow sofort, aber gedrosselt");

        RenderJob job = ReadyJob();
        using var harness = new PreviewHarness(() => job);

        DateTime start = harness.Now;

        Ask(harness.Service, """{"c":"follow"}""");

        if (ExpectPreview(harness, 412, "Follow schickt sofort ein Bild"))
            Check.That(harness.TakeWidth() == 480, "Follow startet mit der Kachelbreite 480");

        // Der Sofortversand verbraucht bewusst noch keinen Takt: Der erste neue
        // Frame darf ohne Wartezeit folgen.
        harness.Service.OnFrameWritten(@"C:\ein-anderer-pfad.png");

        if (ExpectPreview(harness, 412, "der erste neue Frame folgt sofort"))
            Check.That(harness.TakeWidth() == 480,
                       "der Eventpfad aendert weder Bildquelle noch Followbreite");

        harness.Now = start.AddSeconds(1);
        int throttledEncodeCalls = harness.EncodeCalls;
        int throttledSendCalls = harness.SendCalls;
        harness.Service.OnFrameWritten(@"C:\noch-ein-frame.png");

        Check.That(harness.EncodeCalls == throttledEncodeCalls && harness.SendCalls == throttledSendCalls,
                   "innerhalb von zwei Sekunden wird kein drittes Bild angefordert");

        harness.Now = start.AddSeconds(2);
        harness.Service.OnFrameWritten(@"C:\wieder-ein-frame.png");

        if (ExpectPreview(harness, 412, "nach genau zwei Sekunden folgt wieder ein Bild"))
            Check.That(harness.TakeWidth() == 480, "die Drossel laesst die Grenze selbst durch");

        Ask(harness.Service, """{"c":"follow","on":false}""");
        harness.Now = start.AddSeconds(4);
        int disabledEncodeCalls = harness.EncodeCalls;
        int disabledSendCalls = harness.SendCalls;
        harness.Service.OnFrameWritten(@"C:\kein-versand.png");

        Check.That(harness.EncodeCalls == disabledEncodeCalls && harness.SendCalls == disabledSendCalls,
                   "Follow aus unterdrueckt weitere Frames");

        using var low = new PreviewHarness(() => ReadyJob());

        Ask(low.Service, """{"c":"follow","w":1}""");

        if (ExpectPreview(low, 412, "geklemmt kleine Followbreite"))
            Check.That(low.TakeWidth() == 240, "Follow klemmt nach unten auf 240");

        using var high = new PreviewHarness(() => ReadyJob());

        Ask(high.Service, """{"c":"follow","w":99999}""");

        if (ExpectPreview(high, 412, "geklemmt grosse Followbreite"))
            Check.That(high.TakeWidth() == 1920, "Follow klemmt nach oben auf 1920");
    }

    private static void FailureAnswersStayStable()
    {
        Check.Group("Live-Vorschau - jede Leerstelle bekommt eine Antwort");

        using (var noJob = new PreviewHarness(() => null))
        {
            Ask(noJob.Service, """{"c":"preview"}""");
            ExpectFailure(noJob, "No render is running on the machine.", "ohne Render");
        }

        var waiting = new RenderJob { Id = "waiting" };

        using (var noFrame = new PreviewHarness(() => waiting))
        {
            Ask(noFrame.Service, """{"c":"preview"}""");
            ExpectFailure(noFrame, "No frame written yet.", "vor dem ersten Frame");
        }

        RenderJob unreadable = ReadyJob();

        using (var badFrame = new PreviewHarness(() => unreadable, static (_, _) => null))
        {
            Ask(badFrame.Service, """{"c":"preview"}""");
            ExpectFailure(badFrame, "The image could not be read.", "bei unlesbarem Bild");
        }
    }

    private static RenderJob ReadyJob()
    {
        var job = new RenderJob { Id = "live", FirstFrame = 1, LastFrame = 24 };
        job.FrameWritten(412, @"C:\render\f_0412.png");
        return job;
    }

    private static void Ask(RemotePreviewFollowService service, string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        service.Handle(root.GetProperty("c").GetString()!, root);
    }

    private static bool ExpectPreview(PreviewHarness harness, int expectedFrame, string name)
    {
        byte[]? packet = harness.Take();

        Check.That(packet is not null, name + " kommt an");

        if (packet is null) return false;

        bool readable = TryReadPreview(packet, out int frame, out byte[] jpeg);

        Check.That(readable, name + " bleibt eine Preview-Nutzlast");

        if (!readable) return false;

        Check.That(frame == expectedFrame, name + " traegt die Framenummer des Auftrags");
        Check.That(jpeg.SequenceEqual(FakeJpeg), name + " traegt die JPEG-Nutzlast unveraendert");
        return true;
    }

    private static void ExpectFailure(PreviewHarness harness, string why, string name)
    {
        byte[]? packet = harness.Take();

        Check.That(packet is not null, name + " antwortet statt zu schweigen");

        if (packet is null) return;

        if (!TryReadJson(packet, out JsonElement json))
        {
            Check.That(false, name + " antwortet mit JSON");
            return;
        }

        Check.That(json.GetProperty("t").GetString() == "preview" && !json.GetProperty("ok").GetBoolean(),
                   name + " bleibt eine abgelehnte Vorschau");
        Check.That(json.GetProperty("why").GetString() == why, name + " nennt den bestehenden Grund");
    }

    private static bool TryReadPreview(byte[] packet, out int frame, out byte[] jpeg)
    {
        frame = 0;
        jpeg = Array.Empty<byte>();

        if (!Envelope.TryRead(packet, out PayloadKind kind, out byte[] body)
            || kind != PayloadKind.Preview || body.Length < 4)
        {
            return false;
        }

        frame = BinaryPrimitives.ReadInt32BigEndian(body);
        jpeg = body[4..];
        return true;
    }

    private static bool TryReadJson(byte[] packet, out JsonElement json)
    {
        json = default;

        if (!Envelope.TryRead(packet, out PayloadKind kind, out byte[] body) || kind != PayloadKind.Json)
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            json = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class PreviewHarness : IDisposable
    {
        private readonly ConcurrentQueue<byte[]> _sent = new();
        private readonly ConcurrentQueue<int> _widths = new();
        private readonly SemaphoreSlim _signal = new(0);
        private int _encodeCalls;
        private int _sendCalls;

        public PreviewHarness(Func<RenderJob?> job, Func<string?, int, byte[]?>? encode = null)
        {
            Func<string?, int, byte[]?> activeEncode = encode ?? ((_, _) => FakeJpeg);

            Service = new RemotePreviewFollowService(
                job,
                payload =>
                {
                    Interlocked.Increment(ref _sendCalls);
                    _sent.Enqueue(payload.ToArray());
                    _signal.Release();
                },
                (path, width) =>
                {
                    Interlocked.Increment(ref _encodeCalls);
                    _widths.Enqueue(width);
                    return activeEncode(path, width);
                },
                () => Now);
        }

        public DateTime Now { get; set; } = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        public RemotePreviewFollowService Service { get; }

        public int EncodeCalls => Volatile.Read(ref _encodeCalls);

        public int SendCalls => Volatile.Read(ref _sendCalls);

        public byte[]? Take()
        {
            if (!_signal.Wait(Timeout)) return null;

            return _sent.TryDequeue(out byte[]? payload) ? payload : null;
        }

        public int? TakeWidth() => _widths.TryDequeue(out int width) ? width : null;

        public void Dispose() => _signal.Dispose();
    }
}
