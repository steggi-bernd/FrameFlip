using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Die Verteilungen unter den Bereichsreglern (docs/Atelier-Arbeitsablauf.md, C7b): Wo liegen
/// die Werte des Bildes, zwischen denen man ein Fenster setzt?
///
/// Gemessen nach jedem vollen Durchgang, wie das Histogramm - aber nur, wenn ein Regler sie
/// zeigt: die Karte von Pixel Sort, oder eine Helligkeits- oder Farbbereichsmaske im Streifen.
/// </summary>
public partial class AtelierPage
{
    private void SetUpDistributions()
    {
        Tools.SortDistributionWanted += ShowSortDistribution;
        Layers.MaskDistributionWanted += ShowMaskDistribution;
    }

    /// <summary>
    /// Der Sortierwert, wie er beim Sortieren ankommt: das fertige Anzeigebild vor den
    /// Durchgaengen - dieselbe Stichprobe wie Messung und Pipetten, jedes vierte Pixel.
    /// </summary>
    private void ShowSortDistribution()
    {
        if (!Tools.SortShown || InNodes || _frame is not { } frame) return;

        var key = Tools.SortKeyShown;
        var (rgb, _, _) = FloatFrameProcessor.Sample(frame, _finalAdjustments, ViewFor(frame), _finalGrading, step: 4, _number);

        IEnumerable<(float, float)> Keys()
        {
            for (int i = 0; i + 2 < rgb.Length; i += 3)
                yield return (SortTool.KeyOf(key, Byte(rgb[i]), Byte(rgb[i + 1]), Byte(rgb[i + 2])), 1f);
        }

        Tools.ShowSortDistribution(RangeWindows.Distribution(Keys(), RangeScale.Window));
    }

    /// <summary>
    /// Unter der Maske: bei Helligkeit und Untergrund die wahrgenommene Helligkeit des
    /// zusammengesetzten Bildes, beim Farbbereich die Farbtoene - je blasser, desto weniger,
    /// wie die Maske selbst zaehlt.
    /// </summary>
    private void ShowMaskDistribution()
    {
        if (InNodes || Layers.MaskDistributionKind is not { } kind || _frame is not { } frame) return;

        const int Step = 4;

        IEnumerable<(float, float)> Values()
        {
            for (int y = 0; y < frame.Height; y += Step)
            {
                for (int x = 0; x < frame.Width; x += Step)
                {
                    int i = y * frame.Width + x;
                    float r = frame.R[i], g = frame.G[i], b = frame.B[i];

                    if (kind != MaskKind.Colour)
                    {
                        yield return (Masking.Perceptual(0.2126f * r + 0.7152f * g + 0.0722f * b), 1f);
                        continue;
                    }

                    float high = MathF.Max(r, MathF.Max(g, b)), low = MathF.Min(r, MathF.Min(g, b));
                    if (ColourReadout.Hue(r, g, b) is { } hue && high > 1e-6f) yield return (hue, (high - low) / high);
                }
            }
        }

        Layers.ShowMaskDistribution(RangeWindows.Distribution(Values(), kind == MaskKind.Colour ? RangeScale.Hue : RangeScale.Unit,
                                                              kind == MaskKind.Colour ? 72 : 64));
    }

    private static byte Byte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
