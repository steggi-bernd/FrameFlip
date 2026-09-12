namespace FrameFlip.Export;

/// <summary>
/// Schaetzt, wie gross die Datei wird und wie lange es dauert.
///
/// BEIDES SIND SCHAETZUNGEN, und das soll man ihnen ansehen. Eine CRF-Kodierung
/// richtet sich nach dem Bildinhalt: Dieselbe Einstellung ergibt bei einer ruhigen
/// Studioszene ein Drittel der Datei einer Szene voller Rauch und Partikel. Eine Zahl
/// auf das Megabyte genau vorzugeben waere deshalb nicht genauer, sondern nur
/// selbstsicherer.
///
/// Die Groesse kommt aus Bit je Bildpunkt - der einzigen Groesse, die sich zwischen
/// Aufloesungen und Bildraten uebertragen laesst. Die Ausgangswerte sind Messwerte an
/// gerendertem Material, kein Lehrbuch.
///
/// Die Dauer kommt aus dem, was die Maschine beim letzten Mal geschafft hat. Beim
/// ersten Export gibt es das nicht, dann steht ein vorsichtiger Anfangswert da - und
/// sobald der Encoder laeuft, zaehlt ohnehin nur noch die Messung.
/// </summary>
public static class ExportEstimate
{
    /// <summary>
    /// Bit je Bildpunkt beim jeweiligen Bezugs-CRF des Codecs.
    ///
    /// Ueber den Daumen halbiert sich die Datei je sechs CRF-Stufen; das ist die
    /// Faustregel, mit der auch die Encoder selbst arbeiten.
    /// </summary>
    private static (double Bpp, int BaseCrf, double PerStep) Curve(ExportPreset preset)
    {
        if (preset == ExportPreset.H264) return (0.15, 18, 6);
        if (preset == ExportPreset.H265) return (0.10, 22, 6);
        if (preset == ExportPreset.Vp9) return (0.09, 30, 6);

        // ProRes 422 HQ rechnet nicht nach Qualitaetsstufe, sondern nach Format:
        // rund 4,2 Bit je Bildpunkt, unabhaengig vom Inhalt.
        if (preset == ExportPreset.ProRes) return (4.2, 0, 0);

        // GIF laesst sich so nicht fassen - acht Bit je Punkt vor der Kompression,
        // und wieviel davon uebrig bleibt, entscheidet das Bild.
        return (1.6, 0, 0);
    }

    /// <summary>Der CRF-Wert, der am Ende in der Befehlszeile steht.</summary>
    public static int? Crf(ExportPreset preset, ExportQuality quality)
    {
        var (_, baseCrf, perStep) = Curve(preset);

        if (baseCrf <= 0 || perStep <= 0) return null;

        return Math.Clamp(baseCrf + quality.Offset, 0, 51);
    }

    /// <summary>
    /// Geschaetzte Dateigroesse in Byte. Null, wenn sich nichts Sinnvolles sagen laesst.
    ///
    /// <paramref name="calibration"/> ist das Verhaeltnis, das sich beim letzten
    /// Export ergeben hat: tatsaechliche zu geschaetzter Groesse. Damit passt sich
    /// die Schaetzung an das an, was hier wirklich gerendert wird - eine ruhige
    /// Studioszene und eine Rauchsimulation liegen leicht um den Faktor drei
    /// auseinander, und keine Zahl im Code kann beide treffen.
    /// </summary>
    public static long? Bytes(ExportRequest request, ExportQuality quality, ExportSpeed speed,
                              double calibration = 1.0)
    {
        long pixels = (long)OutputWidth(request) * OutputHeight(request);

        if (pixels <= 0 || request.OutputFrameCount <= 0) return null;

        var (bpp, baseCrf, perStep) = Curve(request.Preset);

        if (baseCrf > 0 && perStep > 0)
        {
            int crf = Math.Clamp(baseCrf + quality.Offset, 0, 51);

            bpp *= Math.Pow(2, (baseCrf - crf) / perStep);
            bpp *= speed.SizeFactor;
        }

        if (calibration > MinFactor && calibration < MaxFactor) bpp *= calibration;

        double bits = bpp * pixels * request.OutputFrameCount;

        return (long)(bits / 8);
    }

    /// <summary>
    /// Geschaetzte Dauer. <paramref name="throughput"/> ist, was die Maschine beim
    /// letzten Export geschafft hat, in Megabildpunkten je Sekunde.
    /// </summary>
    public static TimeSpan? Duration(ExportRequest request, ExportSpeed speed, double throughput)
    {
        long pixels = (long)OutputWidth(request) * OutputHeight(request);

        if (pixels <= 0 || request.OutputFrameCount <= 0) return null;
        if (!(throughput > 0)) return null;

        double megapixels = pixels / 1_000_000.0 * request.OutputFrameCount;

        // Zwei Durchlaeufe kosten ungefaehr das Anderthalbfache: Der Palettenlauf
        // liest dasselbe Material, schreibt aber fast nichts.
        double passes = request.Preset.TwoPassPalette ? 1.5 : 1.0;

        return TimeSpan.FromSeconds(megapixels / (throughput * speed.Factor) * passes);
    }

    /// <summary>Was eine unbekannte Maschine vermutlich schafft - bewusst vorsichtig.</summary>
    public const double DefaultThroughput = 18.0;

    /// <summary>
    /// Die Grenzen, zwischen denen ein gemessener Korrekturfaktor als glaubhaft gilt.
    ///
    /// Sie stehen hier und nicht an zwei Stellen: Beim ersten Anlauf nahm die eine
    /// Seite ab 0,02 an, was die andere erst ab 0,05 anwandte - ein gemessener Faktor
    /// von 0,037 wurde also gespeichert und danach ignoriert. Sehr gleichmaessiges
    /// Material komprimiert wirklich so gut; der Wert war richtig, die Grenze falsch.
    /// </summary>
    public const double MinFactor = 0.02;

    public const double MaxFactor = 25.0;

    public static int OutputWidth(ExportRequest request)
    {
        int width = request.TargetWidth > 0 ? request.TargetWidth : request.SourceWidth;
        return Math.Max(2, width - width % 2);
    }

    public static int OutputHeight(ExportRequest request)
    {
        int height = request.TargetWidth > 0 && request.SourceWidth > 0
            ? (int)Math.Round(request.SourceHeight * (request.TargetWidth / (double)request.SourceWidth))
            : request.SourceHeight;

        return Math.Max(2, height - height % 2);
    }

    /// <summary>"1,2 GB", "340 MB", "8,5 MB" - immer mit einer Stelle, wo es zaehlt.</summary>
    public static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    /// <summary>"2:14 min", "45 s", "1:05 h".</summary>
    public static string Clock(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}:{span.Minutes:00} h"
        : span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}:{span.Seconds:00} min"
            : $"{Math.Max(1, (int)span.TotalSeconds)} s";
}
