namespace FrameFlip.Imaging;

/// <summary>
/// Was die Pipette ueber eine Farbe sagt (docs/Atelier-Arbeitsablauf.md, C4): HEX und HSV der
/// angezeigten Bytes, der Farbton linearer Werte, und wie weit ein Farbbereich werden muss, um
/// einen weiteren Farbton aufzunehmen.
/// </summary>
public static class ColourReadout
{
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Farbton in Grad (0 bis 360), Saettigung und Helligkeit in Prozent.</summary>
    public static (int Hue, int Saturation, int Value) Hsv(byte r, byte g, byte b)
    {
        float hue = Hue(r / 255f, g / 255f, b / 255f) ?? 0f;
        float max = Math.Max(r, Math.Max(g, b)) / 255f;
        float min = Math.Min(r, Math.Min(g, b)) / 255f;
        float saturation = max <= 0f ? 0f : (max - min) / max;

        return ((int)MathF.Round(hue) % 360, (int)MathF.Round(saturation * 100f), (int)MathF.Round(max * 100f));
    }

    /// <summary>Der Farbton in Grad - oder null bei Grau, das keinen hat.</summary>
    public static float? Hue(float r, float g, float b)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float range = max - min;

        if (range <= 1e-6f) return null;

        float hue = max == r ? (g - b) / range
                  : max == g ? 2f + (b - r) / range
                  : 4f + (r - g) / range;

        hue *= 60f;
        return hue < 0f ? hue + 360f : hue;
    }

    /// <summary>Der Abstand zweier Farbtoene auf dem Kreis, 0 bis 180.</summary>
    public static float Distance(float a, float b)
    {
        float d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    /// <summary>
    /// Wie breit ein Farbbereich um <paramref name="hue"/> werden muss, damit <paramref name="more"/>
    /// dazugehoert - mit etwas Luft, nie schmaler als bisher und nie ueber den halben Kreis.
    /// </summary>
    public static float Widen(float hue, float spread, float more)
        => Math.Clamp(Math.Max(spread, Distance(hue, more) + 5f), 0f, 180f);
}
