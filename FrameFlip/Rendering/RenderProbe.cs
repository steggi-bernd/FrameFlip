using System.Text.Json;

namespace FrameFlip.Rendering;

/// <summary>Was in der .blend-Datei eingestellt ist - abgefragt, nicht geraten.</summary>
/// <param name="Options">Die Werte, in derselben Form, in der man sie auch aendert.</param>
/// <param name="Scenes">Alle Szenen der Datei, damit sich am Handy eine auswaehlen laesst.</param>
/// <param name="Output">Der Ausgabepfad, wie er in der Datei steht - nur zur Anzeige.</param>
public sealed record SceneReport(RenderOptions Options, IReadOnlyList<string> Scenes, string Output);

/// <summary>
/// Die Einstellungen einer .blend-Datei auslesen.
///
/// Eine .blend ist ein gepacktes Binaerformat mit Zeigern auf Speicheradressen; sie
/// von aussen zu lesen hiesse, Blenders Datenmodell nachzubauen. Also fragt FrameFlip
/// Blender selbst: Es laedt die Datei ohne Fenster, druckt die Werte als eine Zeile
/// JSON und beendet sich. Das kostet ein, zwei Sekunden und ist dafuer immer richtig -
/// auch bei der naechsten Blender-Fassung.
///
/// Der Zweck ist nicht Vollstaendigkeit, sondern die Frage am Handy: Was ist hier
/// eigentlich eingestellt? Wer am Rechner in 1920x1080 gerendert hat und unterwegs
/// 4K will, muss erst einmal sehen, wovon er ausgeht.
/// </summary>
public static class RenderProbe
{
    /// <summary>
    /// Woran die Zeile zu erkennen ist.
    ///
    /// Blender schreibt beim Laden allerhand auf die Ausgabe - Addons, Warnungen,
    /// Treibermeldungen. Ohne eine eindeutige Marke muesste man die JSON-Zeile daran
    /// erkennen, dass sie mit einer geschweiften Klammer anfaengt, und das tut
    /// frueher oder spaeter auch etwas anderes.
    /// </summary>
    public const string Marker = "FRAMEFLIP_SETTINGS ";

    /// <summary>
    /// Der Python-Ausdruck, der die Werte druckt.
    ///
    /// Alles mit getattr, weil nicht jede Blender-Fassung jedes Feld hat: Ohne
    /// Cycles gibt es keine Samples, ohne EEVEE keine taa_render_samples, und ein
    /// AttributeError mitten darin liesse gar nichts uebrig.
    /// </summary>
    public static string Script =>
        "import bpy,json;"
        + "s=bpy.context.scene;r=s.render;i=r.image_settings;"
        + "c=getattr(s,'cycles',None);e=getattr(s,'eevee',None);"
        + "print('" + Marker + "'+json.dumps({"
        + "'engine':r.engine,"
        + "'scene':s.name,"
        + "'scenes':[x.name for x in bpy.data.scenes],"
        + "'first':s.frame_start,'last':s.frame_end,'step':s.frame_step,'frame':s.frame_current,"
        + "'width':r.resolution_x,'height':r.resolution_y,'percent':r.resolution_percentage,"
        + "'format':i.file_format,'color':i.color_mode,'depth':i.color_depth,"
        + "'quality':getattr(i,'quality',None),'compression':getattr(i,'compression',None),"
        + "'transparent':r.film_transparent,'threads':r.threads,"
        + "'samples':getattr(c,'samples',None) if c else getattr(e,'taa_render_samples',None),"
        + "'denoise':getattr(c,'use_denoising',None) if c else None,"
        + "'device':getattr(c,'device',None) if c else None,"
        + "'output':r.filepath"
        + "}))";

    /// <summary>Die Argumente, mit denen Blender nur nachschaut und nichts rendert.</summary>
    public static List<string> Arguments(string blendFile)
        => new() { "--disable-autoexec", "-b", blendFile, "--factory-startup", "--python-expr", Script };

    /// <summary>
    /// Die Antwort aus Blenders Ausgabe herauslesen.
    ///
    /// null heisst: Es stand nichts Brauchbares darin. Das ist kein Absturz und kein
    /// Grund fuer einen - eine fehlende Antwort heisst nur, dass das Handy die
    /// Einstellungen von Hand angeben muss.
    /// </summary>
    public static SceneReport? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        // Die letzte Marke gewinnt: Ein Addon, das beim Laden selbst etwas druckt,
        // steht davor.
        string? line = output.Split('\n')
                             .Select(l => l.Trim())
                             .LastOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal));

        if (line is null) return null;

        try
        {
            using var document = JsonDocument.Parse(line[Marker.Length..]);
            var root = document.RootElement;

            var options = new RenderOptions
            {
                Scene = Text(root, "scene"),
                First = Number(root, "first"),
                Last = Number(root, "last"),
                Step = Number(root, "step"),
                Frame = Number(root, "frame"),
                Width = Number(root, "width"),
                Height = Number(root, "height"),
                Percentage = Number(root, "percent"),
                Format = Text(root, "format"),
                Depth = Number(root, "depth"),

                // Beide Felder tragen dieselbe Zahl in verschiedenen Formaten. Was
                // gesetzt ist, gilt - PNG fuehrt die Kompression, JPEG die Qualitaet.
                Quality = Number(root, "quality") ?? Number(root, "compression"),
                Transparent = Flag(root, "transparent"),
                Threads = Number(root, "threads"),
                Samples = Number(root, "samples"),
                Denoise = Flag(root, "denoise"),
                Engine = EngineOf(Text(root, "engine")),
                Device = Text(root, "device").ToUpperInvariant() switch
                {
                    "GPU" => RenderDevice.Gpu,
                    "CPU" => RenderDevice.Cpu,
                    _ => RenderDevice.Unchanged,
                },
                Color = Text(root, "color").ToUpperInvariant() switch
                {
                    "BW" => ColorMode.Bw,
                    "RGB" => ColorMode.Rgb,
                    "RGBA" => ColorMode.Rgba,
                    _ => ColorMode.Unchanged,
                },
            };

            var scenes = new List<string>();

            if (root.TryGetProperty("scenes", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                scenes.AddRange(list.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));

            return new SceneReport(options.Normalized(), scenes, Text(root, "output"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Blenders Bezeichner auf das, was die Oberflaeche kennt.</summary>
    private static RenderEngine? EngineOf(string engine) => engine.ToUpperInvariant() switch
    {
        "CYCLES" => RenderEngine.Cycles,
        "BLENDER_WORKBENCH" => RenderEngine.Workbench,
        var name when name.StartsWith("BLENDER_EEVEE") => RenderEngine.Eevee,
        _ => null,
    };

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return null;

        // Blender liefert die Farbtiefe als Zeichenkette ('8', '16'), die anderen
        // Felder als Zahl. Beides kommt hier an.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null,
        };
    }

    private static bool? Flag(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True
                                                                               or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
