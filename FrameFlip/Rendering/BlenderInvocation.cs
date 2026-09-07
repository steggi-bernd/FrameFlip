using System.Text;

namespace FrameFlip.Rendering;

/// <summary>
/// Der Aufruf, mit dem Blender ohne Fenster rechnet.
///
/// Zwei Dinge sind hier bindend, und beide haben schon Leuten den Abend gekostet:
///
/// DIE REIHENFOLGE. Blender arbeitet die Argumente der Reihe nach ab, und "-f" oder
/// "-a" startet den Render SOFORT. Alles, was danach steht, wirkt nicht mehr - ein
/// "-o" hinter "-f" wird stillschweigend uebergangen, und die Bilder landen im
/// Temp-Ordner. Der Start steht deshalb immer als Letztes.
///
/// DIE ARGUMENTE SIND EINE LISTE, KEIN BEFEHLSTEXT. Sie werden einzeln uebergeben,
/// nicht zu einer Zeile zusammengeklebt, die dann eine Shell auseinandernimmt. Ein
/// Ordner namens "Mein Projekt &amp; Co" ist dadurch schlicht ein Argument mit
/// Leerzeichen und Kaufmannsund, und kein Anlass fuer Ueberraschungen.
///
/// Was die Kommandozeile nicht kann - Samples, Aufloesung, Farbtiefe, Entrauschen -
/// geht ueber einen Python-Ausdruck. Der wird hier gebaut, und er setzt nur, was
/// ausdruecklich gewuenscht ist: Was in den Optionen null bleibt, bleibt so, wie es
/// in der .blend-Datei steht.
/// </summary>
public static class BlenderInvocation
{
    /// <summary>Blenders Formatname zu einer Endung. Unbekanntes bleibt PNG.</summary>
    public static string FormatFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "JPEG",
        ".tif" or ".tiff" => "TIFF",
        ".bmp" => "BMP",
        ".webp" => "WEBP",
        ".exr" => "OPEN_EXR",
        ".tga" => "TARGA",
        _ => "PNG",
    };

    /// <summary>Die uebliche Endung zu einem Formatnamen - fuer die Anzeige, nicht fuer Blender.</summary>
    public static string ExtensionFor(string format) => format.ToUpperInvariant() switch
    {
        "JPEG" => ".jpg",
        "TIFF" => ".tif",
        "BMP" => ".bmp",
        "WEBP" => ".webp",
        "OPEN_EXR" or "OPEN_EXR_MULTILAYER" => ".exr",
        "TARGA" => ".tga",
        _ => ".png",
    };

    /// <summary>
    /// Die Argumentliste bauen.
    ///
    /// <paramref name="pattern"/> ist der Ausgabepfad ohne Endung, so wie ihn
    /// <see cref="OutputPlanner"/> ausgerechnet hat. Die Rautezeichen fuer die
    /// Framenummer haengt diese Stelle an - vier Stellen, wie ueberall sonst auch.
    /// </summary>
    public static List<string> Arguments(string blendFile, string pattern, RenderOptions options)
    {
        var settings = options.Normalized();
        // Eine .blend aus dem Austauschordner ist Eingabe von aussen. Sie darf
        // Einstellungen tragen, aber beim Laden kein eingebettetes Python oder
        // registrierte Auto-Run-Skripte ausfuehren.
        var args = new List<string> { "--disable-autoexec", "-b", blendFile };

        // Die Szene muss vor allem anderen stehen, sonst richtet sich der Rest nach
        // der falschen.
        if (settings.Scene.Length > 0) args.AddRange(new[] { "-S", settings.Scene });

        if (settings.Engine is RenderEngine engine)
            args.AddRange(new[] { "-E", engine switch
            {
                RenderEngine.Cycles => "CYCLES",
                RenderEngine.Workbench => "BLENDER_WORKBENCH",
                _ => "BLENDER_EEVEE_NEXT",
            } });

        if (settings.Threads is int threads and > 0) args.AddRange(new[] { "-t", threads.ToString() });

        // Vorwaertsschraegstriche: Blender nimmt beide, und in einem Python-Ausdruck
        // waeren Rueckwaertsschraegstriche Fluchtzeichen.
        args.AddRange(new[] { "-o", (pattern + "####").Replace('\\', '/') });

        if (settings.Format.Length > 0) args.AddRange(new[] { "-F", settings.Format });

        // Endung anhaengen lassen. Ohne das heissen die Dateien "frame_0001" ohne
        // alles, und kein Programm weiss damit etwas anzufangen.
        args.AddRange(new[] { "-x", "1" });

        string python = Python(settings);

        if (python.Length > 0) args.AddRange(new[] { "--python-expr", python });

        // Ab hier laeuft es los - alles Weitere waere wirkungslos.
        if (settings.Animation)
        {
            if (settings.First is int first) args.AddRange(new[] { "-s", first.ToString() });
            if (settings.Last is int last) args.AddRange(new[] { "-e", last.ToString() });
            if (settings.Step is int step and > 1) args.AddRange(new[] { "-j", step.ToString() });

            args.Add("-a");
        }
        else
        {
            args.Add("-f");
            args.Add(settings.Frame?.ToString() ?? "1");
        }

        return args;
    }

    /// <summary>
    /// Der Python-Ausdruck fuer alles, was die Kommandozeile nicht kann.
    ///
    /// Eine einzige Zeile mit Semikola, weil "--python-expr" genau ein Argument
    /// nimmt. Jede Zuweisung steht fuer sich; faellt eine aus, weil die Blender-
    /// Fassung das Feld anders nennt, faellt der ganze Ausdruck aus - deshalb sind
    /// die heiklen in ein try gepackt.
    /// </summary>
    public static string Python(RenderOptions options)
    {
        var settings = options.Normalized();
        var lines = new List<string>();

        void Set(string expression) => lines.Add(expression);

        if (settings.Width is int width) Set($"s.render.resolution_x={width}");
        if (settings.Height is int height) Set($"s.render.resolution_y={height}");
        if (settings.Percentage is int percent) Set($"s.render.resolution_percentage={percent}");
        if (settings.Transparent is bool transparent) Set($"s.render.film_transparent={Bool(transparent)}");

        if (settings.Color != ColorMode.Unchanged)
            Set($"s.render.image_settings.color_mode='{settings.Color.ToString().ToUpperInvariant()}'");

        if (settings.Depth is int depth) Set($"s.render.image_settings.color_depth='{depth}'");

        if (settings.Quality is int quality)
        {
            // Dasselbe Feld heisst bei PNG Kompression und bei JPEG Qualitaet, und
            // Blender fuehrt beide getrennt. Gesetzt werden beide; das Format
            // entscheidet, welches zaehlt.
            Set($"s.render.image_settings.quality={quality}");
            Set($"s.render.image_settings.compression={quality}");
        }

        // Cycles und EEVEE zaehlen ihre Samples in verschiedenen Feldern, und welche
        // Maschine gerade eingestellt ist, weiss erst Blender.
        if (settings.Samples is int samples)
        {
            Set($"getattr(s,'cycles',None) and setattr(s.cycles,'samples',{samples})");
            Set($"hasattr(s,'eevee') and setattr(s.eevee,'taa_render_samples',{samples})");
        }

        if (settings.Denoise is bool denoise)
            Set($"getattr(s,'cycles',None) and setattr(s.cycles,'use_denoising',{Bool(denoise)})");

        if (settings.Device != RenderDevice.Unchanged)
        {
            string device = settings.Device == RenderDevice.Gpu ? "GPU" : "CPU";

            Set($"getattr(s,'cycles',None) and setattr(s.cycles,'device','{device}')");

            // Ohne freigeschaltete Geraete in den Voreinstellungen faellt Cycles
            // stillschweigend auf die CPU zurueck - das hier schaltet sie an.
            if (settings.Device == RenderDevice.Gpu)
                Set("[setattr(d,'use',True) for d in "
                    + "bpy.context.preferences.addons['cycles'].preferences.get_devices_for_type("
                    + "bpy.context.preferences.addons['cycles'].preferences.compute_device_type)]");
        }

        if (lines.Count == 0) return string.Empty;

        var text = new StringBuilder("import bpy;s=bpy.context.scene;");

        // Alles in einem Versuch: Eine Fassung von Blender, die ein Feld anders
        // nennt, soll den Render nicht verhindern, sondern ihn mit den
        // Einstellungen der Datei laufen lassen.
        text.Append("\ntry:\n");

        foreach (string line in lines) text.Append("    ").Append(line).Append('\n');

        text.Append("except Exception as e:\n    print('FrameFlip: ' + str(e))\n");

        return text.ToString();
    }

    private static string Bool(bool value) => value ? "True" : "False";
}
