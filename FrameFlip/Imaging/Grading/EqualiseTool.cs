using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ausgleichen und Tonwerttrennung nach Quantilen (docs/Atelier-Werkzeugplan.md, W2e).
///
/// Beides folgt aus der Verteilung der Helligkeit, die beim Werkzeug ankommt. Gemessen wird sie
/// EINMAL, auf Knopfdruck, und steht dann im Werkzeug - wie beim Auto-Tonwert. Die Abbildung gilt
/// damit fuer die ganze Folge und auch im Export mit 16 Bit: Je Bild neu gemessen, flackerte jede
/// Folge, in der sich etwas bewegt.
///
/// Ausgleichen stellt jede Helligkeit dorthin, wo ihr Anteil am Bild sie hinstellt: Was die Haelfte
/// der Bildpunkte unter sich hat, wird Mittelgrau. Mit Stufen wird daraus eine Tonwerttrennung
/// nach Quantilen - jede Stufe deckt gleich viel Bild, und ihr Ton ist der mittlere ihrer Punkte,
/// bleibt also, wo er war.
///
/// Gerechnet wird an der Helligkeit (Rec. 709). Die Farbe wandert als Abstand zur Helligkeit mit;
/// die Kanaele einzeln zu strecken verschoebe die Farben.
/// </summary>
public sealed class EqualiseTool : IGradingTool
{
    public const string KindName = "equalise";

    /// <summary>Wie fein die Verteilung gemessen wird.</summary>
    public const int Bins = 256;

    /// <summary>Die meisten Stufen der Tonwerttrennung.</summary>
    public const int MaxSteps = 16;

    public string Kind => KindName;

    public GradingStage Stage => GradingStage.Display;

    /// <summary>
    /// Die gemessene Verteilung: je Stufe der Anteil der Bildpunkte darunter, bis zur Mitte der
    /// Stufe gezaehlt, von 0 bis 1. Null, solange nicht gemessen wurde.
    /// </summary>
    public float[]? Measured { get; set; }

    /// <summary>Wie weit die Abbildung wirkt, 0 bis 1.</summary>
    public float Amount { get; set; }

    /// <summary>Stufenlos (0) ist Ausgleichen; ab 2 eine Tonwerttrennung mit so vielen Stufen.</summary>
    public int Steps { get; set; }

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.001f || Measured is not { Length: Bins };

    private readonly float[] _map = new float[Bins];
    private float _amount;
    private bool _neutral = true;

    public void Prepare()
    {
        _neutral = IsNeutral;
        _amount = Math.Clamp(Amount, 0f, 1f);

        if (!_neutral) Map(Measured!, Steps, _map);
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        if (_neutral) return;

        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float at = Math.Clamp(y, 0f, 1f) * (Bins - 1);
        int i = Math.Min((int)at, Bins - 2);
        float mapped = _map[i] + (_map[i + 1] - _map[i]) * (at - i);
        float shift = (mapped - y) * _amount;

        r += shift;
        g += shift;
        b += shift;
    }

    /// <summary>
    /// Die Abbildung der Helligkeit je Stufe der Messung. Stufenlos ist sie die Verteilung selbst;
    /// mit Stufen bekommt jede Stufe der Messung den mittleren Ton ihres Quantils.
    /// </summary>
    public static void Map(float[] measured, int steps, float[] map)
    {
        if (steps < 2)
        {
            Array.Copy(measured, map, Bins);
            return;
        }

        steps = Math.Min(steps, MaxSteps);
        Span<float> tones = stackalloc float[MaxSteps];

        for (int k = 0; k < steps; k++) tones[k] = Quantile(measured, (k + 0.5f) / steps);

        for (int i = 0; i < Bins; i++) map[i] = tones[Math.Clamp((int)(measured[i] * steps), 0, steps - 1)];
    }

    /// <summary>Die Helligkeit, unter der der Anteil <paramref name="share"/> der Bildpunkte liegt.</summary>
    public static float Quantile(float[] measured, float share)
    {
        if (share <= measured[0]) return 0f;

        for (int i = 1; i < Bins; i++)
        {
            if (measured[i] < share) continue;

            float low = measured[i - 1], high = measured[i];
            float t = high > low ? (share - low) / (high - low) : 0f;

            return (i - 1 + t) / (Bins - 1);
        }

        return 1f;
    }

    /// <summary>Misst die Verteilung aus einer Stichprobe - RGB nacheinander, 0 bis 1.</summary>
    public static float[] Measure(float[] rgb)
    {
        var counts = new double[Bins];
        int count = rgb.Length / 3;

        for (int i = 0; i < count; i++)
        {
            float y = 0.2126f * rgb[i * 3] + 0.7152f * rgb[i * 3 + 1] + 0.0722f * rgb[i * 3 + 2];
            counts[Math.Clamp((int)(y * (Bins - 1) + 0.5f), 0, Bins - 1)]++;
        }

        var measured = new float[Bins];
        double below = 0;

        for (int i = 0; i < Bins; i++)
        {
            measured[i] = count > 0 ? (float)((below + counts[i] / 2) / count) : i / (float)(Bins - 1);
            below += counts[i];
        }

        return measured;
    }

    public EqualiseTool Clone() => new()
    {
        Measured = Measured?.ToArray(),
        Amount = Amount,
        Steps = Steps,
    };
}
