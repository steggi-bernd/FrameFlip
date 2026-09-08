using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using FrameFlip.Bridge;
using FrameFlip.Configuration;
using FrameFlip.Remote;
using FrameFlip.Rendering;

namespace FrameFlip.Tests;

/// <summary>
/// Die Grenze zwischen der entschluesselten Leitung und den Fernbedienungsdiensten.
///
/// Die Dienste pruefen ihre Arbeit selbst. Hier geht es darum, dass der Router
/// genau die richtige Arbeit startet - in der richtigen Reihenfolge und ohne ein
/// JsonElement an sein schon entsorgtes JsonDocument zu ketten.
/// </summary>
public static class RemoteCommandRouterInvariants
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static void Run()
    {
        VocabularyAndGate();
        ClonesBeforeQueueing();
        ChunksStaySynchronous();
        PreviewAndFollowAnswer();
    }

    private static void VocabularyAndGate()
    {
        Check.Group("Fernbefehle - Vokabular und Eingangstor");

        string[] browse = { "projects", "folder", "seq", "view", "fetch", "ack", "stop" };
        string[] render = { "blenders", "scene", "render", "renderstop" };
        string[] upload = { "files", "put", "putstop" };

        Check.That(browse.All(BrowseService.Handles), "alle sieben Bibliotheksbefehle gehoeren zum Browser");
        Check.That(render.All(RenderService.Handles), "alle vier Renderbefehle gehoeren zum Renderer");
        Check.That(upload.All(UploadService.Handles), "alle drei Dateibefehle gehoeren zum Upload");

        string[] services = browse.Concat(render).Concat(upload).ToArray();

        Check.That(services.Distinct(StringComparer.Ordinal).Count() == services.Length,
                   "kein Dienstbefehl gehoert zu zwei Diensten");
        Check.That(!services.Contains("preview") && !services.Contains("follow"),
                   "Vorschau und Follow bleiben beim Router");

        using var harness = new RouterHarness(new AppSettings(), () => null);

        harness.Router.OnPayload(Array.Empty<byte>());
        harness.Router.OnPayload(new byte[] { 0x7F, 1, 2 });
        harness.Router.OnPayload(Envelope.Preview(1, new byte[] { 0xFF, 0xD8 }));
        harness.Router.OnPayload(Envelope.Json("kein JSON"));
        harness.Router.OnPayload(Envelope.Json("""{"c":42}"""));
        harness.Router.OnPayload(Envelope.Json("""{"c":"etwas-neues"}"""));

        Check.That(harness.Pending == 0, "unbekannte oder kaputte Nutzlast bleibt folgenlos");
    }

    private static void ClonesBeforeQueueing()
    {
        Check.Group("Fernbefehle - JSON lebt ueber den Netzwerk-Handler hinaus");

        using var harness = new RouterHarness(new AppSettings(), () => null);

        // folder liest p erst im Hintergrundtask. Diese Antwort beweist damit,
        // dass der Router den Root vor dem Dispose des JsonDocument geklont hat.
        harness.Router.OnPayload(Envelope.Json("""{"c":"folder","p":"C:\\nicht-freigegeben"}"""));

        byte[]? response = harness.Take();
        Check.That(response is not null, "der asynchrone Bibliotheksbefehl antwortet");

        if (response is null) return;

        JsonElement json = ReadJson(response);

        Check.That(json.GetProperty("t").GetString() == "folder", "die Antwort bleibt beim richtigen Befehl");
        Check.That(!json.GetProperty("ok").GetBoolean(), "ohne Freigabe wird weiter abgelehnt");
        Check.That(json.GetProperty("p").GetString() == @"C:\nicht-freigegeben",
                   "der Hintergrundtask liest den geklonten Pfad");
    }

    private static void ChunksStaySynchronous()
    {
        Check.Group("Fernbefehle - Dateistuecke bleiben geordnet");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-router-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(folder);

            var settings = new AppSettings
            {
                RemoteEnabled = true,
                PairingSecret = "x",
                FileFolder = folder,
                FileAccessEnabled = true,
                FilePushEnabled = true,
            };

            settings.Normalize();

            using var harness = new RouterHarness(settings, () => null);
            byte[] bytes = { 7, 8, 9 };

            harness.Router.OnPayload(Envelope.Json("""{"c":"put","n":"router.blend","s":3}"""));

            byte[]? announced = harness.Take();
            Check.That(announced is not null, "die Upload-Ankuendigung geht durch den Hintergrundtask");

            if (announced is null) return;

            JsonElement offer = ReadJson(announced);
            int id = offer.GetProperty("id").GetInt32();

            harness.Router.OnPayload(Envelope.Chunk(id, 0, last: true, bytes));

            // Das letzte Stueck wird im selben Aufruf geschrieben und beantwortet;
            // ein Task hier wuerde Reihenfolge und Quittierung aufbrechen.
            Check.That(harness.Pending == 1, "das Dateistueck wird synchron beantwortet");

            byte[]? stored = harness.Take();
            Check.That(stored is not null, "das letzte Stueck meldet den Abschluss");

            if (stored is null) return;

            JsonElement complete = ReadJson(stored);

            Check.That(complete.GetProperty("t").GetString() == "stored" && complete.GetProperty("ok").GetBoolean(),
                       "das Abschlussprotokoll bleibt erhalten");

            string target = Path.Combine(folder, "router_exchanged.blend");

            Check.That(File.Exists(target), "das Stueck wird als fertige Datei abgelegt");

            if (File.Exists(target))
                Check.That(File.ReadAllBytes(target).SequenceEqual(bytes), "die Stueckbytes kommen unveraendert an");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static void PreviewAndFollowAnswer()
    {
        Check.Group("Fernbefehle - Vorschau und Follow schweigen nicht");

        using var harness = new RouterHarness(new AppSettings(), () => null);

        harness.Router.OnPayload(Envelope.Json("""{"c":"preview"}"""));

        byte[]? preview = harness.Take();
        Check.That(preview is not null, "eine Vorschauanfrage ohne Job bekommt eine Antwort");

        if (preview is not null)
        {
            JsonElement answer = ReadJson(preview);

            Check.That(answer.GetProperty("t").GetString() == "preview" && !answer.GetProperty("ok").GetBoolean(),
                       "die Antwort bleibt eine abgelehnte Vorschau");
            Check.That(answer.GetProperty("why").GetString() == "No render is running on the machine.",
                       "die Leerstelle wird ehrlich erklaert");
        }

        harness.Router.OnPayload(Envelope.Json("""{"c":"follow","w":1}"""));

        byte[]? follow = harness.Take();
        Check.That(follow is not null, "Follow schickt sofort eine statt erst beim naechsten Frame");

        if (follow is not null)
        {
            JsonElement answer = ReadJson(follow);

            Check.That(answer.GetProperty("t").GetString() == "preview" && !answer.GetProperty("ok").GetBoolean(),
                       "auch Follow benutzt dieselbe ehrliche Vorschauantwort");
        }

        harness.Router.OnPayload(Envelope.Json("""{"c":"follow","on":false}"""));
        harness.Router.OnFrameWritten(@"C:\out\f_0001.png");

        Check.That(harness.Pending == 0, "Follow aus unterdrueckt spaetere Frame-Vorschauen");
    }

    private static JsonElement ReadJson(byte[] frame)
    {
        if (!Envelope.TryRead(frame, out PayloadKind kind, out byte[] body) || kind != PayloadKind.Json)
            throw new InvalidOperationException("Der Test erwartete JSON im Frame.");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private sealed class RouterHarness : IDisposable
    {
        private readonly ConcurrentQueue<byte[]> _sent = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly BrowseService _browse;
        private readonly UploadService _upload;
        private readonly RenderRunner _runner;

        public RouterHarness(AppSettings settings, Func<RenderJob?> job)
        {
            void Send(byte[] payload)
            {
                _sent.Enqueue(payload.ToArray());
                _signal.Release();
            }

            _browse = new BrowseService(() => settings, Send);

            void Answer(object payload) => Send(Envelope.Json(JsonSerializer.Serialize(payload)));

            _runner = new RenderRunner(() => settings, _ => { });
            var render = new RenderService(() => settings, Answer, _runner);
            _upload = new UploadService(() => settings, Answer);

            Router = new RemoteCommandRouter(job, _browse, render, _upload, Send);
        }

        public RemoteCommandRouter Router { get; }

        public int Pending => _sent.Count;

        public byte[]? Take()
        {
            if (!_signal.Wait(Timeout)) return null;

            return _sent.TryDequeue(out byte[]? payload) ? payload : null;
        }

        public void Dispose()
        {
            _browse.Dispose();
            _upload.Dispose();
            _runner.Dispose();
            _signal.Dispose();
        }
    }
}
