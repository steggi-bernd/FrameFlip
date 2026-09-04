using System.Globalization;

namespace FrameFlip.Export;

/// <summary>
/// Baut die Argumentlisten fuer ffmpeg. Bewusst ohne Prozessbezug, damit sich jede
/// Zeile ohne installiertes ffmpeg pruefen laesst - und damit die Fallstricke aus der
/// Referenz an einer Stelle stehen, statt sich im Aufrufcode zu verteilen.
/// </summary>
public static class FfmpegArguments
{
    /// <summary>
    /// Ein Durchlauf: Argumente plus die Rolle, damit der Fortschritt weiss, welcher
    /// Anteil des Gesamtvorgangs damit erledigt ist.
    /// </summary>
    public sealed record Pass(IReadOnlyList<string> Arguments, string Label, bool ReportsProgress);

    /// <summary>
    /// Alle Durchlaeufe fuer einen Auftrag. GIF braucht zwei: erst die Farbpalette
    /// aus dem Material, dann das Bild damit. Ohne den ersten Durchlauf bleibt GIF
    /// bei der Standardpalette und sieht sichtbar schlechter aus.
    /// </summary>
    public static IReadOnlyList<Pass> Build(ExportRequest request, string listPath, string palettePath)
    {
        return request.Preset.TwoPassPalette
            ? new[] { PalettePass(request, listPath, palettePath),
                      GifPass(request, listPath, palettePath) }
            : new[] { StandardPass(request, listPath) };
    }

    // ---------------------------------------------------------------- Durchlaeufe

    private static Pass StandardPass(ExportRequest request, string listPath)
    {
        var args = new List<string>();

        AddInput(args, request, listPath);

        var filter = BuildVideoFilter(request);
        if (filter is not null) { args.Add("-vf"); args.Add(filter); }

        args.AddRange(request.Preset.VideoArguments);

        AddCommonOutput(args, request);
        args.Add(request.OutputPath);

        return new Pass(args, request.Preset.Name, ReportsProgress: true);
    }

    private static Pass PalettePass(ExportRequest request, string listPath, string palettePath)
    {
        var args = new List<string>();

        AddInput(args, request, listPath);

        // stats_mode=diff gewichtet die Farben nach dem, was sich bewegt - bei einer
        // Rendersequenz mit ruhigem Hintergrund deutlich besser als der Standard.
        args.Add("-vf");
        args.Add(Combine(BuildVideoFilter(request), "palettegen=stats_mode=diff"));

        args.Add("-y");
        args.Add(palettePath);

        return new Pass(args, "Farbpalette", ReportsProgress: false);
    }

    private static Pass GifPass(ExportRequest request, string listPath, string palettePath)
    {
        var args = new List<string>();

        AddInput(args, request, listPath);

        args.Add("-i");
        args.Add(palettePath);

        // Zwei Eingaenge: der Filtergraph muss benannt werden, -vf reicht nicht.
        var scale = BuildVideoFilter(request);
        var chain = scale is null
            ? "[0:v][1:v]paletteuse=dither=bayer:bayer_scale=3"
            : $"[0:v]{scale}[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3";

        args.Add("-lavfi");
        args.Add(chain);

        AddCommonOutput(args, request);
        args.Add(request.OutputPath);

        return new Pass(args, "GIF", ReportsProgress: true);
    }

    // ---------------------------------------------------------------- Bausteine

    private static void AddInput(List<string> args, ExportRequest request, string listPath)
    {
        // Maschinenlesbarer Fortschritt auf stdout. Die Statuszeile auf stderr laesst
        // sich zwar auch parsen, aendert aber ihr Format zwischen Versionen.
        args.Add("-hide_banner");
        args.Add("-nostdin");
        args.Add("-progress"); args.Add("pipe:1");
        args.Add("-nostats");
        args.Add("-loglevel"); args.Add("error");
        args.Add("-y");

        // KEIN -framerate: das ist eine Option des Bilddatei-Demuxers (image2) und
        // existiert beim concat-Demuxer nicht - ffmpeg bricht mit "Option framerate
        // not found" ab, noch bevor eine Datei gelesen wird. Die Bildrate der Eingabe
        // steht stattdessen in den duration-Zeilen der Liste; -r am Ausgang macht
        // daraus eine konstante Bildrate.
        //
        // -safe 0 ist noetig, weil die Liste absolute Pfade enthaelt.
        args.Add("-f"); args.Add("concat");
        args.Add("-safe"); args.Add("0");
        args.Add("-i"); args.Add(listPath);
    }

    private static void AddCommonOutput(List<string> args, ExportRequest request)
    {
        // Konstante Bildrate erzwingen, auch wenn die Liste ungleiche Abstaende haette.
        args.Add("-fps_mode"); args.Add("cfr");
        args.Add("-r"); args.Add(Number(request.Fps));

        // Der Encoder darf den laufenden Render nicht verdraengen. libx264 nimmt sich
        // sonst alle Kerne - mitten in einem CPU-Render genau das falsche Verhalten.
        if (request.Threads > 0)
        {
            args.Add("-threads");
            args.Add(request.Threads.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Skalierung, Massekorrektur und - falls gewuenscht - die Anzeigekorrektur als
    /// ein Filterausdruck. Die Reihenfolge ist bewusst: erst skalieren, dann
    /// korrigieren, damit die Korrektur auf weniger Pixeln arbeitet.
    /// </summary>
    public static string? BuildVideoFilter(ExportRequest request)
    {
        var scale = BuildScaleFilter(request);
        var adjust = request.Adjustments?.ToFfmpegFilter();

        if (adjust is null) return scale;
        return scale is null ? adjust : scale + "," + adjust;
    }

    /// <summary>
    /// Skalierung und die Absicherung gegen ungerade Bildmasse in einem Ausdruck.
    ///
    /// Ungerade Masse brechen H.264 und H.265: die Chroma-Ebene von yuv420p ist halb
    /// so gross, und eine ungerade Kantenlaenge laesst sich nicht halbieren. Der
    /// Abbruch kommt erst beim Encodieren, also nach dem Einlesen aller Frames.
    /// </summary>
    public static string? BuildScaleFilter(ExportRequest request)
    {
        if (request.TargetWidth > 0)
        {
            // Breite vorgeben, Hoehe folgt dem Seitenverhaeltnis und wird auf ein
            // gerades Mass gerundet. lanczos, weil Verkleinern hier der Normalfall ist.
            int width = request.TargetWidth - (request.TargetWidth % 2);
            return $"scale={width.ToString(CultureInfo.InvariantCulture)}:-2:flags=lanczos";
        }

        return "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    }

    private static string Combine(string? first, string second)
        => string.IsNullOrEmpty(first) ? second : first + "," + second;

    private static string Number(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Argumentliste als Befehlszeile, zum Anzeigen und Kopieren. Nur fuer Menschen -
    /// der Prozessaufruf bekommt die Liste unveraendert, ohne diesen Umweg.
    /// </summary>
    public static string ToCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var parts = new List<string>(arguments.Count + 1) { Quote(executable) };
        foreach (var argument in arguments) parts.Add(Quote(argument));
        return string.Join(' ', parts);
    }

    private static string Quote(string value)
        => value.Length == 0 || value.Contains(' ') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
}
