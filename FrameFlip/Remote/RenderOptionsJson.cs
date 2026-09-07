using System.Text.Json;
using FrameFlip.Rendering;

namespace FrameFlip.Remote;

/// <summary>
/// Renderoptionen zwischen PC und Handy uebersetzen.
///
/// Beide Richtungen an einer Stelle, und das ist Absicht: Der PC schickt, was in der
/// Datei steht, das Handy schickt zurueck, was jemand geaendert hat, und beides ist
/// dieselbe Form. Zwei getrennte Uebersetzungen waeren zwei Gelegenheiten, sich zu
/// widersprechen - und ein Feld, das nur in einer Richtung ankommt, faellt niemandem
/// auf, bis der Render mit der falschen Aufloesung durchgelaufen ist.
///
/// Was fehlt, bleibt null und damit "wie in der Datei". Das ist der Kern: Das Handy
/// schickt nicht den ganzen Zustand, sondern nur, was es aendern will.
/// </summary>
public static class RenderOptionsJson
{
    public static object Write(RenderOptions options) => new
    {
        anim = options.Animation,
        first = options.First,
        last = options.Last,
        step = options.Step,
        frame = options.Frame,
        scene = options.Scene,
        engine = options.Engine?.ToString().ToLowerInvariant(),
        device = options.Device == RenderDevice.Unchanged ? null : options.Device.ToString().ToLowerInvariant(),
        samples = options.Samples,
        denoise = options.Denoise,
        width = options.Width,
        height = options.Height,
        percent = options.Percentage,
        format = options.Format.Length > 0 ? options.Format : null,
        color = options.Color == ColorMode.Unchanged ? null : options.Color.ToString().ToLowerInvariant(),
        depth = options.Depth,
        quality = options.Quality,
        transparent = options.Transparent,
        threads = options.Threads,
    };

    public static RenderOptions Read(JsonElement root) => new RenderOptions
    {
        Animation = Flag(root, "anim") ?? false,
        First = Number(root, "first"),
        Last = Number(root, "last"),
        Step = Number(root, "step"),
        Frame = Number(root, "frame"),
        Scene = Text(root, "scene"),
        Samples = Number(root, "samples"),
        Denoise = Flag(root, "denoise"),
        Width = Number(root, "width"),
        Height = Number(root, "height"),
        Percentage = Number(root, "percent"),
        Format = Text(root, "format").ToUpperInvariant(),
        Depth = Number(root, "depth"),
        Quality = Number(root, "quality"),
        Transparent = Flag(root, "transparent"),
        Threads = Number(root, "threads"),

        Engine = Text(root, "engine").ToLowerInvariant() switch
        {
            "cycles" => RenderEngine.Cycles,
            "eevee" => RenderEngine.Eevee,
            "workbench" => RenderEngine.Workbench,
            _ => null,
        },

        Device = Text(root, "device").ToLowerInvariant() switch
        {
            "gpu" => RenderDevice.Gpu,
            "cpu" => RenderDevice.Cpu,
            _ => RenderDevice.Unchanged,
        },

        Color = Text(root, "color").ToLowerInvariant() switch
        {
            "bw" => ColorMode.Bw,
            "rgb" => ColorMode.Rgb,
            "rgba" => ColorMode.Rgba,
            _ => ColorMode.Unchanged,
        },
    }.Normalized();

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return null;

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
