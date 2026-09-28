namespace FrameFlip.Imaging;

/// <summary>Was ueber dem Bild liegt, um es zu beurteilen - nur in der Anzeige, nie im Export (W2c).</summary>
public enum ViewAid
{
    /// <summary>Das Bild, wie es ist.</summary>
    None,

    /// <summary>Wo es abschneidet: Lichter rot, Tiefen blau.</summary>
    Clipping,

    /// <summary>Falschfarben: die Helligkeit als Zonen, wie am Monitor einer Kamera.</summary>
    FalseColour,
}

/// <summary>Eine Zone der Falschfarben - ihre Farbe, oder null: das Bild bleibt dort grau.</summary>
public readonly record struct FalseColourZone(float From, float To, string Key, byte R, byte G, byte B, bool Grey);

/// <summary>
/// Die Sichthilfen des Ateliers (docs/Atelier-Werkzeugplan.md, W2c). Sie rechnen auf dem fertigen
/// Anzeigebild, also auf dem, was man sieht - Abschneiden und Helligkeit sind Aussagen darueber.
/// </summary>
public static class ViewAids
{
    /// <summary>
    /// Die Zonen der Falschfarben, nach dem Anteil des Anzeigewerts (0 bis 1). Die Grenzen folgen
    /// den ueblichen Tafeln an Kameramonitoren: abgesoffen, Tiefen, Mittelgrau, Haut eine Blende
    /// darueber, Lichter kurz vor dem Ende, ausgefressen. Dazwischen bleibt das Bild grau, damit
    /// die Form lesbar bleibt.
    /// </summary>
    public static readonly FalseColourZone[] Zones =
    {
        new(0.000f, 0.025f, "S_FalseColourCrushed", 128, 0, 160, false),
        new(0.025f, 0.100f, "S_FalseColourShadows", 20, 70, 255, false),
        new(0.100f, 0.410f, "", 0, 0, 0, true),
        new(0.410f, 0.480f, "S_FalseColourMiddle", 20, 190, 40, false),
        new(0.480f, 0.530f, "", 0, 0, 0, true),
        new(0.530f, 0.580f, "S_FalseColourSkin", 255, 130, 190, false),
        new(0.580f, 0.930f, "", 0, 0, 0, true),
        new(0.930f, 0.975f, "S_FalseColourHighlights", 255, 230, 0, false),
        new(0.975f, 1.001f, "S_FalseColourBlown", 235, 20, 20, false),
    };

    /// <summary>Die Zone eines Anzeigewerts.</summary>
    public static FalseColourZone ZoneOf(float value)
    {
        value = Math.Clamp(value, 0f, 1f);

        foreach (var zone in Zones)
            if (value >= zone.From && value < zone.To) return zone;

        return Zones[^1];
    }

    /// <summary>Die Helligkeit eines Anzeigewerts - Rec. 709 auf den kodierten Werten, wie ein Kameramonitor.</summary>
    public static float Luma(byte r, byte g, byte b) => (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f;

    /// <summary>
    /// Was die Anzeige des Abschneidens an einem Punkt zeigt: rot, wenn ein Kanal die obere Grenze
    /// erreicht, blau, wenn alle unter der unteren liegen - sonst nichts, das Bild bleibt.
    /// </summary>
    public static (byte R, byte G, byte B)? ClippingAt(byte r, byte g, byte b, float low, float high)
    {
        float top = high * 255f, bottom = low * 255f;

        if (r >= top || g >= top || b >= top) return (235, 20, 20);
        if (r <= bottom && g <= bottom && b <= bottom) return (20, 70, 255);

        return null;
    }

    /// <summary>Wendet eine Sichthilfe auf ein Anzeigebild an, Bgra32, an Ort und Stelle.</summary>
    public static unsafe void Apply(IntPtr pixels, int width, int height, int stride, ViewAid aid, float low, float high)
    {
        if (aid == ViewAid.None) return;

        byte* start = (byte*)pixels;

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8) }, y =>
        {
            byte* row = start + (long)y * stride;

            for (int x = 0; x < width; x++)
            {
                byte* p = row + x * 4;
                byte b = p[0], g = p[1], r = p[2];

                if (aid == ViewAid.Clipping)
                {
                    if (ClippingAt(r, g, b, low, high) is var (cr, cg, cb))
                    {
                        p[0] = cb;
                        p[1] = cg;
                        p[2] = cr;
                    }

                    continue;
                }

                float luma = Luma(r, g, b);
                var zone = ZoneOf(luma);

                if (zone.Grey)
                {
                    byte grey = (byte)MathF.Round(luma * 255f);
                    p[0] = p[1] = p[2] = grey;
                }
                else
                {
                    p[0] = zone.B;
                    p[1] = zone.G;
                    p[2] = zone.R;
                }
            }
        });
    }
}
