namespace FrameFlip.Export;

/// <summary>
/// Ausgabeformat mit den zugehoerigen Encoder-Argumenten.
///
/// Der Pixelformat-Wert gehoert bewusst zum Preset und ist nicht global: yuv420p ist
/// fuer H.264 und H.265 Pflicht, fuer ProRes 422 aber falsch - dort waere es ein
/// Qualitaetsverlust gegenueber yuv422p10le.
/// </summary>
public sealed record ExportPreset(
    string Name,
    string Extension,
    string[] VideoArguments,
    bool TwoPassPalette = false,
    string? Description = null,
    string[]? AlsoAllowed = null)
{
    public override string ToString() => Name;

    /// <summary>
    /// Behaelter, die diesen Codec aufnehmen koennen.
    ///
    /// Der Zielname ist im Dialog frei editierbar, und nicht jede Kombination geht:
    /// ProRes in MP4 laesst ffmpeg mit "Could not find tag for codec prores"
    /// scheitern - eine Meldung, die niemand ohne Vorwissen deutet. Deshalb wird die
    /// Endung vor dem Start geprueft.
    /// </summary>
    public IReadOnlyList<string> AllowedExtensions
        => AlsoAllowed is null ? new[] { Extension } : AlsoAllowed.Prepend(Extension).ToArray();

    public bool Accepts(string extension)
        => AllowedExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));

    public static readonly ExportPreset H264 = new(
        "H.264 / MP4", ".mp4",
        new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "18",
                "-pix_fmt", "yuv420p", "-movflags", "+faststart" },
        Description: "Universeller Standard. Läuft überall.",
        AlsoAllowed: new[] { ".mkv", ".mov" });

    public static readonly ExportPreset H265 = new(
        "H.265 / MP4", ".mp4",
        // hvc1 statt hev1: ohne dieses Tag spielen QuickTime und Apple-Geraete die
        // Datei nicht ab, obwohl sie technisch in Ordnung ist.
        new[] { "-c:v", "libx265", "-preset", "medium", "-crf", "22",
                "-pix_fmt", "yuv420p", "-tag:v", "hvc1" },
        Description: "Kleinere Dateien, langsamer im Encoding.",
        AlsoAllowed: new[] { ".mkv", ".mov" });

    public static readonly ExportPreset ProRes = new(
        "ProRes 422 HQ / MOV", ".mov",
        new[] { "-c:v", "prores_ks", "-profile:v", "3", "-pix_fmt", "yuv422p10le" },
        Description: "Für die Weiterverarbeitung im Schnitt. Große Dateien.",
        AlsoAllowed: new[] { ".mkv" });

    public static readonly ExportPreset Vp9 = new(
        "WebM / VP9", ".webm",
        // -b:v 0 ist noetig, damit -crf wirklich als Qualitaetsziel wirkt; ohne das
        // behandelt libvpx den CRF-Wert nur als Obergrenze.
        new[] { "-c:v", "libvpx-vp9", "-crf", "30", "-b:v", "0", "-row-mt", "1" },
        Description: "Für das Web. Kein Apple-Support ohne Umwege.",
        AlsoAllowed: new[] { ".mkv" });

    public static readonly ExportPreset Gif = new(
        "GIF", ".gif",
        Array.Empty<string>(),
        TwoPassPalette: true,
        Description: "Zum Teilen. Zwei Durchläufe mit eigener Farbpalette.");

    public static readonly IReadOnlyList<ExportPreset> All =
        new[] { H264, H265, ProRes, Vp9, Gif };
}
