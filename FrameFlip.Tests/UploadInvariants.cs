using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Dateien vom Handy entgegennehmen.
///
/// Die einzige Stelle, an der etwas von aussen auf diese Platte gelangt. Zwei
/// Zusicherungen tragen hier alles:
///
/// Eine halbe Uebertragung hinterlaesst keine halbe Datei. Sie entsteht unter einem
/// Namen, den Blender nicht oeffnet, und bekommt ihren richtigen erst, wenn sie
/// vollstaendig ist. Sonst faellt der Abbruch erst auf, wenn Blender sie nicht
/// laden kann - und dann weiss niemand mehr, woher sie kam.
///
/// Ein Stueck an der falschen Stelle wird verworfen und nicht quittiert. Eine
/// stillschweigend falsch zusammengesetzte Datei waere schlimmer als eine, die nie
/// ankommt.
/// </summary>
public static class UploadInvariants
{
    public static void Run()
    {
        Check.Group("Ablegen - was hereinkommen darf");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-put-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "schon_da.blend"), "alt");

            var settings = new AppSettings { RemoteEnabled = true, PairingSecret = "x", FileFolder = root };
            var answers = new List<JsonElement>();

            using var service = new UploadService(() => settings, answer => answers.Add(Parse(answer)));

            // Ausgeschaltet: keine Liste, kein Ablegen.
            Ask(service, """{"c":"files"}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ohne Erlaubnis keine Liste");

            settings.FileAccessEnabled = true;
            settings.Normalize();

            answers.Clear();
            Ask(service, """{"c":"files"}""");

            Check.That(answers[^1].GetProperty("ok").GetBoolean(), "mit Leseerlaubnis kommt sie");
            Check.That(!answers[^1].GetProperty("write").GetBoolean(), "aber ablegen darf man noch nicht");
            Check.That(answers[^1].GetProperty("items").EnumerateArray().Count() == 1,
                       "die vorhandene Datei steht drin");

            answers.Clear();
            Ask(service, """{"c":"put","n":"neu.blend","s":10}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ohne Schreiberlaubnis wird abgelehnt");

            settings.FilePushEnabled = true;
            settings.Normalize();

            // Eine gleichnamige Datei darf kommen - sie legt sich daneben.
            answers.Clear();
            Ask(service, """{"c":"put","n":"schon_da.blend","s":10}""");

            Check.That(answers[^1].GetProperty("ok").GetBoolean(), "eine gleichnamige Datei wird angenommen");
            Check.That(answers[^1].GetProperty("n").GetString() == "schon_da_exchanged.blend",
                       "und bekommt die Markierung - der Name steht in der Antwort, bevor etwas fliesst",
                       answers[^1].GetProperty("n").GetString());

            Check.That(File.ReadAllText(Path.Combine(root, "schon_da.blend")) == "alt",
                       "die vorhandene bleibt unveraendert");

            Ask(service, """{"c":"putstop"}""");

            // Ein Name, der aus dem Ordner fuehrt.
            answers.Clear();
            Ask(service, """{"c":"put","n":"..\\draussen.blend","s":10}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ein Name mit Pfad kommt nicht durch");

            Check.Group("Ablegen - Stueck fuer Stueck");

            var payload = new byte[2500];
            new Random(3).NextBytes(payload);

            answers.Clear();
            Ask(service, $$"""{"c":"put","n":"neu.blend","s":{{payload.Length}}}""");

            Check.That(answers[^1].GetProperty("ok").GetBoolean(), "die Ankuendigung wird angenommen");

            int id = answers[^1].GetProperty("id").GetInt32();

            Check.That(File.Exists(Path.Combine(root, "neu_exchanged.blend.teil")),
                       "sie entsteht unter einem Namen, den Blender nicht oeffnet");

            Check.That(!File.Exists(Path.Combine(root, "neu_exchanged.blend")),
                       "und noch nicht unter ihrem eigenen");

            // Ein Stueck aus der Reihe wird verworfen - und nicht quittiert.
            answers.Clear();
            service.OnChunk(id, 5, false, new byte[] { 1, 2, 3 });

            Check.That(answers.Count == 0, "ein Stueck an der falschen Stelle wird nicht quittiert");

            // Und eines aus einem anderen Vorgang ebenso wenig.
            service.OnChunk(id + 99, 0, false, new byte[] { 1 });

            Check.That(answers.Count == 0, "und eines aus einem fremden Vorgang auch nicht");

            // Der richtige Weg.
            answers.Clear();
            service.OnChunk(id, 0, false, payload[..1000]);

            Check.That(answers.Count == 1 && answers[^1].GetProperty("t").GetString() == "ack",
                       "ein richtiges Stueck wird quittiert");

            service.OnChunk(id, 1, false, payload[1000..2000]);
            service.OnChunk(id, 2, true, payload[2000..]);

            Check.That(answers[^1].GetProperty("t").GetString() == "stored"
                       && answers[^1].GetProperty("ok").GetBoolean(),
                       "das letzte Stueck schliesst die Datei ab");

            Check.That(!File.Exists(Path.Combine(root, "neu_exchanged.blend.teil")), "die Teildatei ist weg");
            Check.That(File.Exists(Path.Combine(root, "neu_exchanged.blend")), "und die fertige da");

            Check.That(File.ReadAllBytes(Path.Combine(root, "neu_exchanged.blend")).SequenceEqual(payload),
                       "Byte fuer Byte dasselbe");

            Check.Group("Ablegen - die Ankuendigung gilt exakt");

            // Ein vorzeitiges Schlussstueck darf nie als fertige Datei landen. Die
            // alte Toleranz von einem ganzen Chunk haette genau das zugelassen.
            answers.Clear();
            Ask(service, """{"c":"put","n":"kurz.blend","s":10}""");
            int shortId = answers[^1].GetProperty("id").GetInt32();

            answers.Clear();
            service.OnChunk(shortId, 0, true, new byte[9]);

            Check.That(answers.Count == 1 && !answers[^1].GetProperty("ok").GetBoolean(),
                       "ein zu kurzes Schlussstueck wird abgelehnt");
            Check.That(!File.Exists(Path.Combine(root, "kurz_exchanged.blend.teil")),
                       "die kurze Teildatei wird entfernt");
            Check.That(!File.Exists(Path.Combine(root, "kurz_exchanged.blend")),
                       "und nie als fertige .blend sichtbar");

            answers.Clear();
            Ask(service, """{"c":"put","n":"gross.blend","s":10}""");
            int largeId = answers[^1].GetProperty("id").GetInt32();

            answers.Clear();
            service.OnChunk(largeId, 0, true, new byte[11]);

            Check.That(answers.Count == 1 && !answers[^1].GetProperty("ok").GetBoolean(),
                       "mehr Daten als angekuendigt werden vor dem Schreiben abgelehnt");
            Check.That(!File.Exists(Path.Combine(root, "gross_exchanged.blend")),
                       "auch die zu grosse Datei landet nicht im Zielordner");

            Check.Group("Ablegen - ein Abbruch hinterlaesst nichts");

            answers.Clear();
            Ask(service, """{"c":"put","n":"halb.blend","s":5000}""");

            int second = answers[^1].GetProperty("id").GetInt32();

            service.OnChunk(second, 0, false, new byte[1000]);

            Check.That(File.Exists(Path.Combine(root, "halb_exchanged.blend.teil")),
                       "sie faengt an zu entstehen");

            answers.Clear();
            Ask(service, """{"c":"putstop"}""");

            Check.That(!File.Exists(Path.Combine(root, "halb_exchanged.blend.teil")),
                       "nach dem Abbruch ist sie weg");
            Check.That(!File.Exists(Path.Combine(root, "halb_exchanged.blend")),
                       "und keine halbe blieb liegen");

            Check.That(answers.Any(a => a.GetProperty("t").GetString() == "stored"
                                        && !a.GetProperty("ok").GetBoolean()),
                       "der Abbruch wird gemeldet - Schweigen waere hier das Schlimmste");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    private static void Ask(UploadService service, string json)
    {
        using var document = JsonDocument.Parse(json);

        service.Handle(document.RootElement.GetProperty("c").GetString()!, document.RootElement);
    }

    private static JsonElement Parse(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
