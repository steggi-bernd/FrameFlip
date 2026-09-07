using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
using FrameFlip.Projects;
using FrameFlip.Rendering;

namespace FrameFlip.Remote;

/// <summary>
/// Rendern auf Zuruf vom Handy.
///
/// Vier Befehle, und drei davon sind harmlos: nachsehen, welche Blender-Fassungen es
/// gibt, nachsehen, was in einer Datei eingestellt ist, und abbrechen. Nur einer
/// startet etwas, und der steht unter drei Bedingungen:
///
/// Die Erlaubnis muss am Rechner gesetzt sein. Die Datei muss ohnehin schon
/// freigegeben sein - gerendert wird nur, was das Handy auch ansehen darf. Und ein
/// Blender muss eingetragen sein, mit dem gerechnet wird.
///
/// Faellt eine davon weg, kommt eine Absage mit Grund. Das ist wichtiger als es
/// klingt: Ein Knopf, der nichts tut und nichts sagt, laesst jemanden am anderen
/// Ende raten, ob es an der Leitung liegt, an der Datei oder an ihm.
/// </summary>
public sealed class RenderService
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<object> _send;
    private readonly RenderRunner _runner;

    public RenderService(Func<AppSettings> settings, Action<object> send, RenderRunner runner)
    {
        _settings = settings;
        _send = send;
        _runner = runner;
    }

    public static bool Handles(string command)
        => command is "blenders" or "scene" or "render" or "renderstop";

    public bool Handle(string command, JsonElement root)
    {
        switch (command)
        {
            case "blenders": Blenders(); return true;
            case "scene": Scene(Text(root, "p"), Blender(root)); return true;
            case "render": Render(root); return true;
            case "renderstop": Stop(); return true;
            default: return false;
        }
    }

    // ---------------------------------------------------------------- Auswahl

    /// <summary>
    /// Welche Blender-Fassungen es hier gibt.
    ///
    /// Auch dann, wenn das Rendern abgeschaltet ist: Die Liste verraet nichts, was
    /// nicht ohnehin auf jedem Rechner steht, und in der App soll sichtbar sein,
    /// WARUM es nicht geht - eine leere Auswahl ist keine Auskunft.
    /// </summary>
    private void Blenders()
    {
        var settings = _settings();
        var found = BlenderFinder.Find(settings.ExtraBlenders);

        string current = settings.BlenderPath.Length > 0
            ? settings.BlenderPath
            : BlenderFinder.Preferred(found)?.Path ?? string.Empty;

        _send(new
        {
            t = "blenders",
            ok = true,
            allowed = settings.HeadlessRenderEnabled,
            why = _runner.Unavailable(),
            current,
            items = found.Select(install => new
            {
                p = install.Path,
                v = install.Version?.ToString(),
                s = install.Source,
                steam = install.IsSteam,
            }).ToList(),
        });
    }

    // ---------------------------------------------------------------- Nachsehen

    private void Scene(string? path, string? blender = null)
    {
        string? file = Allowed(path, out string? refusal);

        if (file is null)
        {
            Refuse("scene", path, refusal!);
            return;
        }

        // Nachsehen heisst Blender starten, und das faellt unter dieselbe Erlaubnis
        // wie das Rendern. Der Grund muss hierher: Sonst steht am Handy "die
        // Einstellungen liessen sich nicht lesen", waehrend in Wahrheit der Schalter
        // am Rechner aus ist - und dann sucht jemand den Fehler in seiner Datei.
        if (_runner.Unavailable() is string closed)
        {
            Refuse("scene", file, closed);
            return;
        }

        // Blender zu fragen dauert ein, zwei Sekunden. Das darf die Leitung nicht
        // anhalten - die Antwort kommt, wenn sie da ist.
        _ = Task.Run(async () =>
        {
            SceneReport? report = await _runner.ProbeAsync(file, blender);

            if (report is null)
            {
                Refuse("scene", file, "The settings could not be read from the file.");
                return;
            }

            _send(new
            {
                t = "scene",
                ok = true,
                p = file,
                n = Path.GetFileName(file),
                o = RenderOptionsJson.Write(report.Options),
                scenes = report.Scenes,
                output = report.Output,
            });
        });
    }

    // ---------------------------------------------------------------- Starten

    private void Render(JsonElement root)
    {
        string? path = Text(root, "p");
        string? file = Allowed(path, out string? refusal);

        if (file is null)
        {
            Refuse("render", path, refusal!);
            return;
        }

        var settings = _settings();

        if (!settings.HeadlessRenderEnabled)
        {
            Refuse("render", file, "Rendering from the phone is switched off on the machine.");
            return;
        }

        var options = root.TryGetProperty("o", out JsonElement wanted) && wanted.ValueKind == JsonValueKind.Object
            ? RenderOptionsJson.Read(wanted)
            : new RenderOptions();

        // Ein Blender, das die App ausgesucht hat, muss eines von denen sein, die
        // dieser Rechner kennt. Sonst waere "womit gerechnet wird" eine Angabe von
        // aussen, und das ist genau die Sorte Angabe, die man nicht nehmen sollte.
        string? chosen = Text(root, "blender");

        if (chosen is { Length: > 0 } && !Known(chosen))
        {
            Refuse("render", file, "That Blender is not one of the machine's.");
            return;
        }

        string? trouble = _runner.Start(new RenderRequest(file, options, null, chosen));

        if (trouble is not null)
        {
            Refuse("render", file, trouble);
            return;
        }

        _send(new
        {
            t = "render",
            ok = true,
            p = file,
            n = Path.GetFileName(file),
            output = _runner.OutputFolder,
        });
    }

    private void Stop()
    {
        bool ran = _runner.IsRunning;

        _runner.Cancel();

        _send(new { t = "renderstop", ok = true, ran });
    }

    // ---------------------------------------------------------------- Kleinteile

    /// <summary>
    /// Ob diese Datei gerendert werden darf - und warum nicht.
    ///
    /// Gerendert wird nur, was ohnehin freigegeben ist. Damit ist die Frage, welche
    /// Dateien erreichbar sind, an genau einer Stelle beantwortet und nicht an zwei.
    /// </summary>
    private string? Allowed(string? path, out string? refusal)
    {
        refusal = null;

        var known = ProjectLibrary.Load();
        var vault = new LibraryVault(_settings().LibraryAccessEnabled, known.Folders, known.RenderOutputs);

        if (!vault.LibraryEnabled)
        {
            refusal = "The project folders are not shared on the machine.";
            return null;
        }

        string? file = vault.ResolveFile(path, out FileUse use);

        if (file is null || use != FileUse.Blend)
        {
            refusal = "This is not a Blender file from the shared library.";
            return null;
        }

        return file;
    }

    /// <summary>
    /// Die gewaehlte Fassung, sofern dieser Rechner sie kennt.
    ///
    /// Auch beim blossen Nachsehen: Was in der Datei steht, haengt an der Fassung -
    /// eine 3.6 kennt kein EEVEE Next, und die Antwort waere eine andere.
    /// </summary>
    private string? Blender(JsonElement root)
    {
        string chosen = Text(root, "blender");

        return chosen.Length > 0 && Known(chosen) ? chosen : null;
    }

    private bool Known(string path)
        => BlenderFinder.Find(_settings().ExtraBlenders)
                        .Any(install => string.Equals(install.Path, path, StringComparison.OrdinalIgnoreCase));

    private void Refuse(string topic, string? path, string why)
        => _send(new { t = topic, ok = false, p = path ?? "", why });

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
