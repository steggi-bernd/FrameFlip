using System.Text.Json.Serialization;

namespace FrameFlip.Imaging;

/// <summary>Einzelne Kanaele isolieren, um Alpha oder eine Farbe zu beurteilen.</summary>
public enum ChannelView
{
    All,
    Red,
    Green,
    Blue,
    Alpha,
    /// <summary>Graustufen aus der Luminanz - zeigt Helligkeitsverlaeufe ohne Farbablenkung.</summary>
    Luminance,
}

/// <summary>
/// Nicht-destruktive Anzeigekorrektur. Betrifft ausschliesslich die Darstellung -
/// die Dateien auf der Platte bleiben unberuehrt.
///
/// Die Reihenfolge der Schritte ist die in der Farbkorrektur uebliche und nicht
/// beliebig: Belichtung wirkt auf die Lichtmenge, also zuerst; danach werden
/// Schwarz- und Weisspunkt gesetzt, dann die Tonwertkurve ueber Gamma gebogen und
/// zuletzt der Kontrast um die Bildmitte gespreizt. Saettigung kommt am Ende, weil
/// sie alle drei Kanaele gegeneinander verrechnet.
/// </summary>
public sealed record ImageAdjustments
{
    public static readonly ImageAdjustments Neutral = new();

    /// <summary>Belichtung in Blendenstufen. 0 = unveraendert, +1 = doppelte Lichtmenge.</summary>
    public double Exposure { get; init; }

    /// <summary>Schwarzpunkt: Werte darunter werden auf 0 gezogen. 0 bis knapp unter Weisspunkt.</summary>
    public double BlackPoint { get; init; }

    /// <summary>Weisspunkt: Werte darueber laufen auf 1. Knapp ueber Schwarzpunkt bis 1.</summary>
    public double WhitePoint { get; init; } = 1.0;

    /// <summary>Gamma. 1 = unveraendert, groesser hellt die Mitten auf.</summary>
    public double Gamma { get; init; } = 1.0;

    /// <summary>Kontrast um die Bildmitte. 1 = unveraendert.</summary>
    public double Contrast { get; init; } = 1.0;

    /// <summary>Saettigung. 1 = unveraendert, 0 = Graustufen, groesser 1 verstaerkt.</summary>
    public double Saturation { get; init; } = 1.0;

    public ChannelView Channel { get; init; } = ChannelView.All;

    /// <summary>
    /// Kleinste Abweichung, die noch als Aenderung zaehlt.
    ///
    /// Ohne diese Toleranz entscheidet Gleitkomma-Rauschen ueber den Rechenweg: ein
    /// Regler, der auf seinen Ausgangswert zurueckgezogen wurde, landet nicht exakt
    /// auf 1,0, sondern auf 0,9999999999995. Ein Vergleich mit == haelt das fuer
    /// eine Korrektur und laesst bei jedem Bild den vollen Pfad laufen - 9,6 ms
    /// statt 0,3 ms, bei 60 fps ueber die Haelfte des Zeitbudgets fuer nichts.
    ///
    /// Ein Tausendstel liegt weit unter dem, was auf 8 Bit ueberhaupt sichtbar wird:
    /// eine Aenderung um 1/255 entspraeche 0,004.
    /// </summary>
    private const double Epsilon = 0.001;

    private static bool Same(double value, double reference) => Math.Abs(value - reference) < Epsilon;

    /// <summary>
    /// True, wenn nichts zu rechnen ist. Der Blit nimmt dann den direkten Weg -
    /// eine unbenutzte Korrektur darf die Wiedergabe nicht einen Takt kosten.
    /// </summary>
    [JsonIgnore]
    public bool IsNeutral => !NeedsToneCurve && !NeedsChannelMix;

    /// <summary>True, wenn die Tonwertkurve von der Geraden abweicht.</summary>
    [JsonIgnore]
    public bool NeedsToneCurve =>
        !Same(Exposure, 0) || !Same(BlackPoint, 0) || !Same(WhitePoint, 1.0) ||
        !Same(Gamma, 1.0) || !Same(Contrast, 1.0);

    [JsonIgnore]
    public bool NeedsChannelMix => !Same(Saturation, 1.0) || Channel != ChannelView.All;

    /// <summary>
    /// Begrenzt auf sinnvolle Bereiche und raeumt dabei das Rauschen weg, das aus
    /// den Reglern kommt: auf drei Nachkommastellen gerundet, und was dem
    /// Ausgangswert nahekommt, wird darauf eingerastet. Sonst steht spaeter
    /// "Gamma 0,9999999999995" in der Konfiguration und die Korrektur gilt als aktiv,
    /// obwohl nichts zu sehen ist.
    /// </summary>
    public ImageAdjustments Clamped() => this with
    {
        Exposure = Snap(Math.Clamp(Exposure, -6, 6), 0),
        BlackPoint = Snap(Math.Clamp(BlackPoint, 0, 0.95), 0),
        WhitePoint = Snap(Math.Clamp(WhitePoint, 0.05, 2.0), 1.0),
        Gamma = Snap(Math.Clamp(Gamma, 0.1, 5.0), 1.0),
        Contrast = Snap(Math.Clamp(Contrast, 0, 4.0), 1.0),
        Saturation = Snap(Math.Clamp(Saturation, 0, 4.0), 1.0),
    };

    private static double Snap(double value, double neutral)
        => Same(value, neutral) ? neutral : Math.Round(value, 3);

    /// <summary>
    /// Nachschlagetabelle fuer die Tonwertkurve, 256 Eintraege.
    ///
    /// Alles ausser der Saettigung laesst sich je Kanal unabhaengig rechnen und
    /// deshalb einmal vorberechnen. Fuer ein 1080p-Bild sind das statt sechs
    /// Gleitkommaschritten je Farbkanal - rund 18 Millionen Rechnungen - nur noch
    /// 256 Vorberechnungen und ein Feldzugriff je Byte.
    /// </summary>
    public byte[] BuildToneCurve()
    {
        var table = new byte[256];

        double gain = Math.Pow(2.0, Exposure);
        double span = WhitePoint - BlackPoint;
        if (Math.Abs(span) < 1e-6) span = 1e-6;

        double inverseGamma = 1.0 / Math.Max(0.0001, Gamma);

        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;

            v *= gain;                                  // Belichtung
            v = (v - BlackPoint) / span;                // Schwarz- und Weisspunkt
            v = Math.Clamp(v, 0, 1);
            v = Math.Pow(v, inverseGamma);              // Gamma
            v = (v - 0.5) * Contrast + 0.5;             // Kontrast um die Mitte

            table[i] = (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
        }

        return table;
    }

    /// <summary>
    /// Entsprechung als ffmpeg-Filter, damit ein Export so aussieht wie die Vorschau.
    ///
    /// Der eq-Filter kennt dieselben Groessen, benennt sie aber anders: brightness
    /// ist ein additiver Versatz von -1 bis 1, waehrend Belichtung multiplikativ
    /// wirkt. Umgerechnet wird ueber die Bildmitte, was fuer uebliche Werte gut
    /// hinkommt; Schwarz- und Weisspunkt haben in eq keine Entsprechung und werden
    /// deshalb ueber curves abgebildet.
    /// </summary>
    public string? ToFfmpegFilter()
    {
        if (IsNeutral) return null;

        var parts = new List<string>();

        if (BlackPoint != 0 || WhitePoint != 1.0)
        {
            var black = BlackPoint.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            var white = WhitePoint.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add($"curves=all='{black}/0 {white}/1'");
        }

        var eq = new List<string>();

        if (Exposure != 0)
        {
            // 2^EV als additive Helligkeit um die Bildmitte genaehert.
            double brightness = Math.Clamp((Math.Pow(2.0, Exposure) - 1.0) * 0.5, -1, 1);
            eq.Add("brightness=" + brightness.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (Contrast != 1.0)
            eq.Add("contrast=" + Contrast.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

        if (Saturation != 1.0)
            eq.Add("saturation=" + Saturation.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

        if (Gamma != 1.0)
            eq.Add("gamma=" + Gamma.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

        if (eq.Count > 0) parts.Add("eq=" + string.Join(':', eq));

        // Kanalansichten sind Beurteilungswerkzeuge, keine Bildkorrektur - ein Export
        // in Falschfarben oder nur mit dem Rotkanal ist praktisch nie gewollt.
        return parts.Count > 0 ? string.Join(',', parts) : null;
    }

    /// <summary>Kurzfassung fuer die Oberflaeche.</summary>
    public string Describe()
    {
        if (IsNeutral) return "neutral";

        var parts = new List<string>();
        if (Exposure != 0) parts.Add($"EV {Exposure:+0.##;-0.##}");
        if (Gamma != 1.0) parts.Add($"γ {Gamma:0.##}");
        if (Contrast != 1.0) parts.Add($"K {Contrast:0.##}");
        if (Saturation != 1.0) parts.Add($"S {Saturation:0.##}");
        if (BlackPoint != 0 || WhitePoint != 1.0) parts.Add($"{BlackPoint:0.##}–{WhitePoint:0.##}");
        if (Channel != ChannelView.All) parts.Add(Channel.ToString());

        return string.Join("  ", parts);
    }
}
