using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Die Anpassung eines Farbbereichs. Alle drei Werte laufen von -100 bis 100.</summary>
public sealed class HslBand
{
    private float _hue, _saturation, _luminance;

    /// <summary>Verschiebt den Farbton. Voller Ausschlag entspricht 30 Grad.</summary>
    public float Hue
    {
        get => _hue;
        set => _hue = Math.Clamp(value, -100f, 100f);
    }

    public float Saturation
    {
        get => _saturation;
        set => _saturation = Math.Clamp(value, -100f, 100f);
    }

    public float Luminance
    {
        get => _luminance;
        set => _luminance = Math.Clamp(value, -100f, 100f);
    }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(_hue) < 0.5f && MathF.Abs(_saturation) < 0.5f &&
                             MathF.Abs(_luminance) < 0.5f;

    public HslBand Clone() => new() { Hue = _hue, Saturation = _saturation, Luminance = _luminance };
}

/// <summary>
/// Farbton, Saettigung und Helligkeit je Farbbereich - der Regler, mit dem man ein
/// einzelnes zu grelles Gruen einfaengt, ohne den Rest anzufassen.
///
/// Acht Bereiche, wie in Lightroom, weil die Aufteilung sich eingebuergert hat und
/// niemand sie neu lernen will. Die Zentren liegen ungleich weit auseinander - von
/// Rot nach Orange sind es 30 Grad, von Gelb nach Gruen 60 - was fuer die Gewichtung
/// zaehlt.
///
/// **Weiche Uebergaenge sind hier die ganze Arbeit.** Wuerde jeder Bildpunkt dem
/// naechstgelegenen Bereich zugeschlagen, entstuenden an den Grenzen sichtbare
/// Kanten quer durch Farbverlaeufe - ein Himmel bekaeme einen Riss dort, wo Blau
/// in Aqua uebergeht. Deshalb wird zwischen den beiden benachbarten Zentren linear
/// ueberblendet: Die Gewichte summieren sich in jedem Punkt auf genau eins, und ein
/// Verlauf bleibt ein Verlauf.
/// </summary>
public sealed class HslTool : IGradingTool
{
    public const string KindName = "hsl";

    public string Kind => KindName;

    public GradingStage Stage => GradingStage.Display;

    /// <summary>
    /// Die Mitten der acht Bereiche in Grad. Die Reihenfolge ist die der
    /// <see cref="Bands"/> und darf sich nicht aendern - sie steht so im
    /// gespeicherten Rezept.
    /// </summary>
    public static readonly float[] Centres = { 0f, 30f, 60f, 120f, 180f, 240f, 270f, 300f };

    public static readonly string[] Names =
    {
        "Rot", "Orange", "Gelb", "Gruen", "Aqua", "Blau", "Violett", "Magenta",
    };

    public List<HslBand> Bands { get; set; } =
        Enumerable.Range(0, 8).Select(_ => new HslBand()).ToList();

    [JsonIgnore]
    public bool IsNeutral => Bands.Count == 0 || Bands.All(b => b.IsNeutral);

    public void Prepare()
    {
        // Ein Rezept aus einer Fassung mit weniger Baendern darf nicht dazu fuehren,
        // dass hier ins Leere gegriffen wird.
        while (Bands.Count < Centres.Length) Bands.Add(new HslBand());
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        RgbToHsl(r, g, b, out float hue, out float saturation, out float lightness);

        // Ohne Saettigung gibt es keinen Farbton, den man verschieben koennte -
        // Grau bliebe sonst von jedem Bereich ein wenig eingefaerbt.
        if (saturation < 0.001f) return;

        Weights(hue, out int first, out int second, out float blend);

        var a = Bands[first];
        var c = Bands[second];

        float hueShift = a.Hue * (1f - blend) + c.Hue * blend;
        float satAdjust = a.Saturation * (1f - blend) + c.Saturation * blend;
        float lumAdjust = a.Luminance * (1f - blend) + c.Luminance * blend;

        // Der volle Ausschlag verschiebt um 30 Grad - genug, um Orange nach Rot oder
        // Gelb zu ziehen, und zu wenig, um versehentlich in den uebernaechsten
        // Bereich zu rutschen.
        hue += hueShift * 0.3f;
        if (hue < 0f) hue += 360f;
        if (hue >= 360f) hue -= 360f;

        // Saettigung und Helligkeit als Faktor, nicht als Versatz: ein Versatz
        // traefe blasse und kraeftige Stellen gleich hart.
        saturation = Math.Clamp(saturation * (1f + satAdjust * 0.01f), 0f, 1f);
        lightness = Math.Clamp(lightness * (1f + lumAdjust * 0.01f), 0f, 1f);

        HslToRgb(hue, saturation, lightness, out r, out g, out b);
    }

    /// <summary>
    /// Welche zwei Bereiche fuer diesen Farbton zustaendig sind, und wie stark.
    ///
    /// Die Zentren stehen ungleich weit auseinander, deshalb wird nicht mit festen
    /// Breiten gerechnet, sondern zwischen den beiden Nachbarn ueberblendet. Der
    /// letzte Bereich schliesst ueber 360 Grad an den ersten an - ohne diesen
    /// Umlauf bekaeme Magenta eine harte Kante gegen Rot.
    /// </summary>
    internal static void Weights(float hue, out int first, out int second, out float blend)
    {
        int n = Centres.Length;

        if (hue < Centres[0] || hue >= Centres[n - 1])
        {
            // Der Abschnitt ueber den Nullpunkt hinweg.
            float span = 360f - Centres[n - 1] + Centres[0];
            float offset = hue >= Centres[n - 1] ? hue - Centres[n - 1] : hue + 360f - Centres[n - 1];

            first = n - 1;
            second = 0;
            blend = span > 0 ? offset / span : 0f;
            return;
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (hue >= Centres[i] && hue < Centres[i + 1])
            {
                float span = Centres[i + 1] - Centres[i];
                first = i;
                second = i + 1;
                blend = span > 0 ? (hue - Centres[i]) / span : 0f;
                return;
            }
        }

        first = 0;
        second = 0;
        blend = 0f;
    }

    internal static void RgbToHsl(float r, float g, float b,
                                  out float hue, out float saturation, out float lightness)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        lightness = (max + min) * 0.5f;

        if (delta < 1e-6f)
        {
            hue = 0f;
            saturation = 0f;
            return;
        }

        saturation = lightness > 0.5f
            ? delta / (2f - max - min)
            : delta / (max + min);

        if (max == r) hue = ((g - b) / delta + (g < b ? 6f : 0f)) * 60f;
        else if (max == g) hue = ((b - r) / delta + 2f) * 60f;
        else hue = ((r - g) / delta + 4f) * 60f;
    }

    internal static void HslToRgb(float hue, float saturation, float lightness,
                                  out float r, out float g, out float b)
    {
        if (saturation < 1e-6f)
        {
            r = g = b = lightness;
            return;
        }

        float q = lightness < 0.5f
            ? lightness * (1f + saturation)
            : lightness + saturation - lightness * saturation;

        float p = 2f * lightness - q;
        float h = hue / 360f;

        r = Component(p, q, h + 1f / 3f);
        g = Component(p, q, h);
        b = Component(p, q, h - 1f / 3f);

        static float Component(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;

            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;

            return p;
        }
    }

    public HslTool Clone() => new() { Bands = Bands.Select(x => x.Clone()).ToList() };
}
