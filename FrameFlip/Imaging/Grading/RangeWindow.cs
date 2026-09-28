namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Fenster auf einer Skala mit weichen Kanten - das, was der Bereichsregler zeigt und zieht
/// (docs/Atelier-Arbeitsablauf.md, C7).
///
/// Von <see cref="Low"/> bis <see cref="High"/> laesst es voll durch. Darunter laeuft die Kante
/// ueber <see cref="SoftLow"/> auf null, darueber ueber <see cref="SoftHigh"/>. Vier Griffe also,
/// je zwei als Paar: der innere an der Grenze, der aeussere am Ende der weichen Kante.
/// </summary>
public readonly record struct RangeWindow(float Low, float High, float SoftLow, float SoftHigh)
{
    public float OuterLow => Low - SoftLow;
    public float OuterHigh => High + SoftHigh;

    public float At(RangeHandle handle) => handle switch
    {
        RangeHandle.OuterLow => OuterLow,
        RangeHandle.Low => Low,
        RangeHandle.High => High,
        _ => OuterHigh,
    };
}

/// <summary>Die vier Griffe des Bereichsreglers, von links nach rechts.</summary>
public enum RangeHandle { OuterLow, Low, High, OuterHigh }

/// <summary>
/// Die Skala unter dem Fenster: eine Strecke (Helligkeit, 0 bis 1) oder ein Kreis (Farbton,
/// 0 bis 360 Grad), dazu wie weich eine Kante hoechstens werden darf.
/// </summary>
public readonly record struct RangeScale(float Min, float Max, bool Circular, float MaxSoft)
{
    public float Span => Max - Min;

    /// <summary>Helligkeit: 0 bis 1, eine Kante hoechstens 0,5 weit - so weit wie der Regler "Weich".</summary>
    public static readonly RangeScale Unit = new(0f, 1f, false, 0.5f);

    /// <summary>Farbton: ein Kreis in Grad, eine Kante hoechstens 90 Grad - derselbe Regler "Weich", in halben Kreisen.</summary>
    public static readonly RangeScale Hue = new(0f, 360f, true, 90f);

    /// <summary>
    /// Ein hartes Fenster von 0 bis 1 - ohne Kanten, zwei Griffe (C7b). Fuer das, was nur "dazu"
    /// oder "nicht dazu" kennt, wie die Schwelle von Pixel Sort.
    /// </summary>
    public static readonly RangeScale Window = new(0f, 1f, false, 0f);

    /// <summary>Ob das Fenster weiche Kanten haben kann - sonst hat es nur die beiden inneren Griffe.</summary>
    public bool HasEdges => MaxSoft > 0f;
}

/// <summary>Die Rechnung des Bereichsreglers - ohne Fenster pruefbar, wie die des Farbrads.</summary>
public static class RangeWindows
{
    /// <summary>
    /// Ein Zug an einem Griff um <paramref name="delta"/> Einheiten der Skala.
    ///
    /// Der innere Griff zieht sein Paar mit: Die Grenze wandert, die Kante bleibt so weich, wie
    /// sie war. Der aeussere Griff zieht allein - und mit <paramref name="alone"/> (Alt) auch der
    /// innere: Dann trennt sich das Paar, und die Kante wird weicher oder haerter, wie bei
    /// "Farbbereich" in Photoshop.
    /// </summary>
    public static RangeWindow Drag(RangeWindow window, RangeHandle handle, float delta, bool alone, RangeScale scale)
    {
        float low = window.Low, high = window.High, softLow = window.SoftLow, softHigh = window.SoftHigh;

        // Ohne Kanten gibt es nichts zu trennen: Alt zieht dann wie ohne Alt, sonst stuende der
        // Griff fest, weil sich keine Kante auftun darf.
        if (!scale.HasEdges) alone = false;

        // Auf dem Kreis bleibt ein Fenster mindestens ein Grad weit und hoechstens ein ganzer Kreis.
        float minWidth = scale.Circular ? 1f : 0f;
        float maxWidth = scale.Span;

        switch (handle)
        {
            case RangeHandle.Low:
            {
                float lowest = scale.Circular ? high - maxWidth : scale.Min;
                float wanted = Math.Clamp(low + delta, lowest, high - minWidth);

                // Allein: Der aeussere Griff bleibt stehen, die Kante nimmt auf, was die Grenze wandert.
                if (alone)
                {
                    wanted = Math.Clamp(wanted, window.OuterLow, window.OuterLow + Cap(window.SoftLow, scale));
                    softLow = wanted - window.OuterLow;
                }

                low = wanted;
                break;
            }

            case RangeHandle.High:
            {
                float highest = scale.Circular ? low + maxWidth : scale.Max;
                float wanted = Math.Clamp(high + delta, low + minWidth, highest);

                if (alone)
                {
                    wanted = Math.Clamp(wanted, window.OuterHigh - Cap(window.SoftHigh, scale), window.OuterHigh);
                    softHigh = window.OuterHigh - wanted;
                }

                high = wanted;
                break;
            }

            case RangeHandle.OuterLow:
                softLow = Math.Clamp(window.SoftLow - delta, 0f, Cap(window.SoftLow, scale));
                break;

            case RangeHandle.OuterHigh:
                softHigh = Math.Clamp(window.SoftHigh + delta, 0f, Cap(window.SoftHigh, scale));
                break;
        }

        // Auf dem Kreis liegt die Mitte immer in einer Runde - so wandert das Fenster beliebig oft
        // herum, ohne dass die Zahlen dabei wachsen.
        if (scale.Circular)
        {
            float turn = MathF.Floor(((low + high) / 2f - scale.Min) / scale.Span) * scale.Span;
            low -= turn;
            high -= turn;
        }

        return new RangeWindow(low, high, softLow, softHigh);
    }

    /// <summary>
    /// Wie weich eine Kante durch einen Zug hoechstens wird: die Grenze der Skala - oder mehr, wenn
    /// sie schon weicher war. Ein Zug an der anderen Seite darf sie nicht nebenbei stutzen.
    /// </summary>
    private static float Cap(float current, RangeScale scale) => MathF.Max(scale.MaxSoft, current);

    /// <summary>Wo ein Griff auf der Skala zu sehen ist - auf der Strecke nie ueber ihre Enden hinaus.</summary>
    public static float Shown(RangeWindow window, RangeHandle handle, RangeScale scale)
    {
        float value = window.At(handle);

        if (!scale.Circular) return Math.Clamp(value, scale.Min, scale.Max);

        float wrapped = (value - scale.Min) % scale.Span;
        return scale.Min + (wrapped < 0f ? wrapped + scale.Span : wrapped);
    }

    /// <summary>
    /// Welcher Griff an einer Stelle liegt - der naechste in Reichweite, oder keiner.
    ///
    /// Liegen zwei aufeinander (eine harte Kante, oder die weiche Kante reicht ueber das Ende
    /// der Skala), greift man den inneren und zieht das Paar. Mit Alt greift man den aeusseren
    /// und zieht es auseinander.
    /// </summary>
    public static RangeHandle? HandleAt(RangeWindow window, float value, float reach, bool alt, RangeScale scale)
    {
        // Ohne Kanten gibt es keinen aeusseren Griff - auch nicht mit Alt.
        if (!scale.HasEdges) alt = false;

        RangeHandle[] order = alt
            ? new[] { RangeHandle.OuterLow, RangeHandle.OuterHigh, RangeHandle.Low, RangeHandle.High }
            : new[] { RangeHandle.Low, RangeHandle.High, RangeHandle.OuterLow, RangeHandle.OuterHigh };

        RangeHandle? best = null;
        float nearest = float.MaxValue;

        foreach (var handle in order)
        {
            if (!scale.HasEdges && handle is RangeHandle.OuterLow or RangeHandle.OuterHigh) continue;

            float distance = Distance(Shown(window, handle, scale), value, scale);

            // Erst deutlich naeher zaehlt: So gewinnt bei Gleichstand der Griff, der vorn in der Reihe steht.
            if (distance > reach || distance >= nearest - 1e-4f * scale.Span) continue;

            best = handle;
            nearest = distance;
        }

        return best;
    }

    /// <summary>Wie weit eine Stelle im Fenster liegt, 0 bis 1 - dieselbe Kante, die die Maske rechnet.</summary>
    public static float Cover(RangeWindow window, float value, RangeScale scale)
    {
        if (!scale.Circular) return Masking.Band(value, window.Low, window.High, window.SoftLow, window.SoftHigh);

        float centre = (window.Low + window.High) / 2f;
        float half = (window.High - window.Low) / 2f;

        float signed = value - centre;
        signed -= MathF.Round(signed / scale.Span) * scale.Span;

        float away = MathF.Abs(signed);
        float edge = signed < 0f ? window.SoftLow : window.SoftHigh;

        if (away <= half) return 1f;

        return edge <= 1e-4f ? 0f : 1f - Math.Clamp((away - half) / edge, 0f, 1f);
    }

    /// <summary>
    /// Eine Verteilung fuer das Band unter der Skala: die Werte in <paramref name="bins"/> Faecher
    /// gezaehlt, auf das hoechste bezogen. Null, wenn nichts gezaehlt wurde.
    /// </summary>
    public static float[]? Distribution(IEnumerable<(float Value, float Weight)> values, RangeScale scale, int bins = 64)
    {
        var counts = new float[bins];
        float total = 0f;

        foreach (var (value, weight) in values)
        {
            if (weight <= 0f || float.IsNaN(value)) continue;

            float at = (value - scale.Min) / scale.Span;
            if (scale.Circular) at -= MathF.Floor(at);

            int bin = Math.Clamp((int)(at * bins), 0, bins - 1);
            counts[bin] += weight;
            total += weight;
        }

        if (total <= 0f) return null;

        float highest = counts.Max();
        for (int i = 0; i < bins; i++) counts[i] /= highest;

        return counts;
    }

    private static float Distance(float a, float b, RangeScale scale)
    {
        float d = MathF.Abs(a - b);
        return scale.Circular ? MathF.Min(d, scale.Span - d) : d;
    }

    // ---------------------------------------------------------------- Masken

    /// <summary>
    /// Das Fenster einer Maske: bei der Helligkeit Von, Bis und die Kanten; beim Farbbereich der
    /// Farbton plus und minus die Weite, und die Kanten in Grad.
    /// </summary>
    public static RangeWindow Of(LayerMask mask) => mask.Kind == MaskKind.Colour
        ? new RangeWindow(mask.Hue - mask.Spread, mask.Hue + mask.Spread, mask.LowSoftness * 180f, mask.HighSoftness * 180f)
        : new RangeWindow(mask.Low, mask.High, mask.LowSoftness, mask.HighSoftness);

    /// <summary>Die Skala, auf der das Fenster einer Maske liegt.</summary>
    public static RangeScale ScaleOf(LayerMask mask) => mask.Kind == MaskKind.Colour ? RangeScale.Hue : RangeScale.Unit;

    /// <summary>
    /// Schreibt ein Fenster in die Maske. Sind beide Kanten gleich, ist das Paar wieder vereint:
    /// Dann gilt die gemeinsame Weichheit, und das Rezept sieht aus wie vor dem Trennen.
    /// </summary>
    public static void Apply(LayerMask mask, RangeWindow window)
    {
        bool colour = mask.Kind == MaskKind.Colour;
        float unit = colour ? 180f : 1f;

        if (colour)
        {
            float centre = (window.Low + window.High) / 2f % 360f;
            mask.Hue = centre < 0f ? centre + 360f : centre;
            mask.Spread = Math.Clamp((window.High - window.Low) / 2f, 0.5f, 180f);
        }
        else
        {
            mask.Low = window.Low;
            mask.High = window.High;
        }

        float softLow = window.SoftLow / unit;
        float softHigh = window.SoftHigh / unit;

        if (MathF.Abs(softLow - softHigh) < 1e-4f)
        {
            mask.Softness = softLow;
            mask.SoftLow = null;
            mask.SoftHigh = null;
        }
        else
        {
            mask.SoftLow = softLow;
            mask.SoftHigh = softHigh;
        }
    }

    /// <summary>
    /// Doppelklick: zurueck in die Grundstellung - bei der Helligkeit das ganze Fenster, beim
    /// Farbbereich die Weite und die Kanten um den Farbton, der schon gewaehlt ist.
    /// </summary>
    public static void Reset(LayerMask mask)
    {
        if (mask.Kind == MaskKind.Colour)
        {
            mask.Spread = 30f;
        }
        else
        {
            mask.Low = 0f;
            mask.High = 1f;
        }

        mask.Softness = 0.1f;
        mask.SoftLow = null;
        mask.SoftHigh = null;
    }
}
