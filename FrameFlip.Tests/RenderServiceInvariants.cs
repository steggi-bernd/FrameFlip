using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Projects;
using FrameFlip.Remote;
using FrameFlip.Rendering;

namespace FrameFlip.Tests;

/// <summary>
/// Die Renderbefehle vom Handy.
///
/// Drei Bedingungen haengen an dem einen Befehl, der etwas startet: die Erlaubnis am
/// Rechner, eine Datei aus der freigegebenen Bibliothek, und ein eingetragenes
/// Blender. Hier steht, dass jede einzelne davon abgelehnt wird - und zwar mit
/// Grund.
///
/// Der Grund ist nicht Hoeflichkeit. Ein Knopf, der nichts tut und nichts sagt,
/// laesst jemanden am anderen Ende raten, ob es an der Leitung liegt, an der Datei
/// oder an ihm.
/// </summary>
public static class RenderServiceInvariants
{
    public static void Run()
    {
        Check.Group("Render - was das Handy darf");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-render-" + Guid.NewGuid().ToString("N")[..8]);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TRACER"));

            string blend = Path.Combine(root, "TRACER", "tracer.blend");
            string image = Path.Combine(root, "TRACER", "frame.png");

            File.WriteAllText(blend, "x");
            File.WriteAllText(image, "x");

            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));
            ProjectLibrary.AddFolder(root);

            var settings = new AppSettings();
            var answers = new List<JsonElement>();

            var runner = new RenderRunner(() => settings, _ => { });

            var service = new RenderService(() => settings,
                                            answer => answers.Add(Parse(answer)),
                                            runner);

            // Ohne Bibliothek geht gar nichts - auch nicht das Nachsehen.
            Ask(service, $$"""{"c":"scene","p":{{JsonSerializer.Serialize(blend)}}}""");

            Check.That(answers.Count == 1 && !answers[^1].GetProperty("ok").GetBoolean(),
                       "ohne freigegebene Bibliothek wird abgelehnt");
            Check.That(answers[^1].GetProperty("why").GetString()!.Length > 10, "mit Grund",
                       answers[^1].GetProperty("why").GetString());

            settings.LibraryAccessEnabled = true;
            settings.RemoteEnabled = true;

            // Mit Bibliothek, aber ohne Render-Erlaubnis: Auch das blosse Nachsehen
            // startet Blender und faellt deshalb unter denselben Schalter. Der Grund
            // muss das sagen - sonst steht am Handy "die Einstellungen liessen sich
            // nicht lesen", und jemand sucht den Fehler in seiner Datei.
            answers.Clear();
            Ask(service, $$"""{"c":"scene","p":{{JsonSerializer.Serialize(blend)}}}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ohne Erlaubnis kein Nachsehen");
            Check.That(answers[^1].GetProperty("why").GetString()!.Contains("switched off"),
                       "und der Grund nennt den Schalter, nicht die Datei",
                       answers[^1].GetProperty("why").GetString());

            // Ein Bild ist keine Blender-Datei.
            answers.Clear();
            Ask(service, $$"""{"c":"render","p":{{JsonSerializer.Serialize(image)}}}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ein Bild laesst sich nicht rendern");

            // Eine Datei ausserhalb der Bibliothek auch nicht.
            answers.Clear();
            Ask(service, """{"c":"render","p":"C:\\Windows\\System32\\drivers\\etc\\hosts"}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "und nichts von ausserhalb");

            // Die richtige Datei, aber das Rendern ist aus.
            answers.Clear();
            Ask(service, $$"""{"c":"render","p":{{JsonSerializer.Serialize(blend)}}}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ohne Erlaubnis wird nicht gerendert");
            Check.That(answers[^1].GetProperty("why").GetString()!.Contains("switched off"),
                       "und der Grund sagt, woran es liegt", answers[^1].GetProperty("why").GetString());

            // Eingeschaltet, aber kein Blender eingetragen.
            settings.HeadlessRenderEnabled = true;
            settings.BlenderPath = string.Empty;

            answers.Clear();
            Ask(service, $$"""{"c":"render","p":{{JsonSerializer.Serialize(blend)}}}""");

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(), "ohne Blender auch nicht");

            // Und ein Blender, das dieser Rechner gar nicht kennt, wird nicht genommen.
            settings.BlenderPath = @"C:\gibtsnicht\blender.exe";

            answers.Clear();
            Ask(service, $$"""
                {"c":"render","p":{{JsonSerializer.Serialize(blend)}},"blender":"D:\\fremd\\blender.exe"}
                """);

            Check.That(!answers[^1].GetProperty("ok").GetBoolean(),
                       "ein fremdes Blender wird abgelehnt - womit gerechnet wird, bestimmt der Rechner");

            // Auf den GRUND geprueft und nicht nur auf die Ablehnung: Ohne diese
            // Pruefung waere die Datei trotzdem abgelehnt worden, nur eben aus einem
            // anderen Grund - und dann steht hier eine Zusicherung, die nichts
            // zusichert. Genau das ist beim Falsifizieren herausgekommen.
            Check.That(answers[^1].GetProperty("why").GetString()!.Contains("not one of the machine"),
                       "und zwar mit genau diesem Grund", answers[^1].GetProperty("why").GetString());

            Check.Group("Render - die Auswahl der Fassungen");

            // Die Liste kommt immer, auch bei abgeschaltetem Render: Eine leere
            // Auswahl waere keine Auskunft darueber, warum es nicht geht.
            settings.HeadlessRenderEnabled = false;

            answers.Clear();
            Ask(service, """{"c":"blenders"}""");

            var list = answers[^1];

            Check.That(list.GetProperty("ok").GetBoolean(), "die Liste kommt auch, wenn Rendern aus ist");
            Check.That(!list.GetProperty("allowed").GetBoolean(), "sie sagt dabei, dass es nicht erlaubt ist");
            Check.That(list.GetProperty("why").ValueKind != JsonValueKind.Null, "und warum",
                       list.GetProperty("why").ToString());

            Check.That(list.GetProperty("items").ValueKind == JsonValueKind.Array,
                       "und traegt die gefundenen Fassungen");

            Check.Group("Render - die Optionen hin und zurueck");

            var options = new RenderOptions
            {
                Animation = true,
                First = 1,
                Last = 250,
                Width = 3840,
                Height = 2160,
                Samples = 512,
                Denoise = true,
                Device = RenderDevice.Gpu,
                Engine = RenderEngine.Cycles,
                Color = ColorMode.Rgba,
                Depth = 16,
                Format = "OPEN_EXR",
                Scene = "Scene",
            };

            var back = RenderOptionsJson.Read(Parse(RenderOptionsJson.Write(options)));

            Check.That(back.Width == 3840 && back.Height == 2160, "die Aufloesung kommt zurueck",
                       $"{back.Width}x{back.Height}");
            Check.That(back.Samples == 512, "die Samples");
            Check.That(back.Denoise == true, "das Entrauschen");
            Check.That(back.Device == RenderDevice.Gpu, "das Rechenwerk");
            Check.That(back.Engine == RenderEngine.Cycles, "die Maschine");
            Check.That(back.Color == ColorMode.Rgba, "der Farbmodus");
            Check.That(back.Depth == 16, "die Farbtiefe");
            Check.That(back.Format == "OPEN_EXR", "das Format");
            Check.That(back.Animation && back.First == 1 && back.Last == 250, "und der Bildbereich");

            // Der wichtigere Fall: Was das Handy NICHT schickt, bleibt offen - und
            // damit so, wie es in der Datei steht.
            var sparse = RenderOptionsJson.Read(Parse(new { width = 3840 }));

            Check.That(sparse.Width == 3840, "was geschickt wird, kommt an");
            Check.That(sparse.Samples is null && sparse.Denoise is null && sparse.Format.Length == 0,
                       "und alles andere bleibt offen");

            Check.That(sparse.Engine is null && sparse.Device == RenderDevice.Unchanged,
                       "auch Maschine und Rechenwerk");

            // Unsinn von aussen wird begrenzt, nicht durchgereicht.
            var wild = RenderOptionsJson.Read(Parse(new { samples = 99_999_999, width = -3 }));

            Check.That(wild.Samples <= 100_000 && wild.Width >= 4, "unsinnige Zahlen werden begrenzt",
                       $"{wild.Samples} / {wild.Width}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);

            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    private static void Ask(RenderService service, string json)
    {
        using var document = JsonDocument.Parse(json);

        service.Handle(document.RootElement.GetProperty("c").GetString()!, document.RootElement);
    }

    private static JsonElement Parse(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
