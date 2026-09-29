using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Wie ein Bild im Ganzen aussieht: Mittel und Streuung der Helligkeit und der beiden
/// Farbdifferenzen (Rec. 709) - genug, um ein anderes Bild in dieselbe Stimmung zu bringen.
/// </summary>
public sealed record ColourStats(float MeanY, float MeanCb, float MeanCr, float SpreadY, float SpreadCb, float SpreadCr)
{
    /// <summary>Misst eine Stichprobe - RGB nacheinander, Anzeigewerte von 0 bis 1.</summary>
    public static ColourStats Measure(float[] rgb)
    {
        int count = rgb.Length / 3;
        if (count == 0) return new ColourStats(0.5f, 0f, 0f, 0f, 0f, 0f);

        double sy = 0, sb = 0, sr = 0, qy = 0, qb = 0, qr = 0;

        for (int i = 0; i < count; i++)
        {
            var (y, cb, cr) = MatchTool.ToYcc(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

            sy += y; sb += cb; sr += cr;
            qy += y * y; qb += cb * cb; qr += cr * cr;
        }

        double my = sy / count, mb = sb / count, mr = sr / count;

        return new ColourStats((float)my, (float)mb, (float)mr,
                               (float)Math.Sqrt(Math.Max(0, qy / count - my * my)),
                               (float)Math.Sqrt(Math.Max(0, qb / count - mb * mb)),
                               (float)Math.Sqrt(Math.Max(0, qr / count - mr * mr)));
    }
}

/// <summary>
/// Farbe angleichen (docs/Atelier-Werkzeugplan.md, W2f): bringt das Bild in die Stimmung eines
/// Vorbilds - eines anderen Bildes der Folge oder einer Datei.
///
/// Uebertragen werden Mittel und Streuung der beiden Farbdifferenzen, auf Wunsch auch der
/// Helligkeit: Wer die Temperatur eines anderen Bildes uebernehmen will, will meist nicht auch
/// seine Belichtung. Beides wird EINMAL gemessen, das Vorbild beim Merken und dieses Bild beim
/// Angleichen; danach gilt die Abbildung fuer die ganze Folge und flackert nicht.
///
/// Das Werkzeug steht zuletzt im Stapel. Gemessen wird dort, wo es steht - so gleicht das fertige
/// Bild dem Vorbild, auch wenn davor noch Kurven und LUT wirken. An einer Ebene mit Maske wirkt
/// es nur stellenweise.
/// </summary>
public sealed class MatchTool : IGradingTool
{
    public const string KindName = "match";

    public string Kind => KindName;

    public GradingStage Stage => GradingStage.Display;

    /// <summary>Wie das Vorbild aussieht. Null, solange keines gemerkt ist.</summary>
    public ColourStats? Reference { get; set; }

    /// <summary>Woher das Vorbild stammt - der Name, den die Karte zeigt.</summary>
    public string? ReferenceName { get; set; }

    /// <summary>Wie dieses Bild beim Angleichen aussah. Null, solange nicht angeglichen wurde.</summary>
    public ColourStats? Source { get; set; }

    /// <summary>Wie weit angeglichen wird, 0 bis 1.</summary>
    public float Amount { get; set; }

    /// <summary>Auch Helligkeit und Kontrast uebernehmen - sonst nur die Farbe.</summary>
    public bool Tone { get; set; }

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.001f || Reference is null || Source is null;

    private bool _neutral = true;
    private float _amount;
    private float _kY = 1, _kCb = 1, _kCr = 1;

    public void Prepare()
    {
        _neutral = IsNeutral;
        if (_neutral) return;

        _amount = Math.Clamp(Amount, 0f, 1f);
        _kY = Tone ? Scale(Source!.SpreadY, Reference!.SpreadY) : 1f;
        _kCb = Scale(Source!.SpreadCb, Reference!.SpreadCb);
        _kCr = Scale(Source!.SpreadCr, Reference!.SpreadCr);
    }

    /// <summary>Wie stark eine Streuung gestreckt wird - begrenzt, damit ein graues Bild nicht explodiert.</summary>
    private static float Scale(float from, float to)
        => from < 1e-4f || to < 1e-4f ? 1f : Math.Clamp(to / from, 0.25f, 4f);

    public void Apply(ref float r, ref float g, ref float b)
    {
        if (_neutral) return;

        var s = Source!;
        var t = Reference!;
        var (y, cb, cr) = ToYcc(r, g, b);

        float y2 = Tone ? (y - s.MeanY) * _kY + t.MeanY : y;
        float cb2 = (cb - s.MeanCb) * _kCb + t.MeanCb;
        float cr2 = (cr - s.MeanCr) * _kCr + t.MeanCr;

        var (r2, g2, b2) = FromYcc(y2, cb2, cr2);

        r += (r2 - r) * _amount;
        g += (g2 - g) * _amount;
        b += (b2 - b) * _amount;
    }

    public static (float Y, float Cb, float Cr) ToYcc(float r, float g, float b)
    {
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        return (y, (b - y) / 1.8556f, (r - y) / 1.5748f);
    }

    public static (float R, float G, float B) FromYcc(float y, float cb, float cr)
    {
        float r = y + 1.5748f * cr;
        float b = y + 1.8556f * cb;
        return (r, (y - 0.2126f * r - 0.0722f * b) / 0.7152f, b);
    }

    public MatchTool Clone() => new()
    {
        Reference = Reference,
        ReferenceName = ReferenceName,
        Source = Source,
        Amount = Amount,
        Tone = Tone,
    };
}
