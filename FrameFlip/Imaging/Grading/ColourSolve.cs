namespace FrameFlip.Imaging.Grading;

/// <summary>Eine der drei Zonen von Lift, Gamma und Gain.</summary>
public enum ZoneKind { Lift, Gamma, Gain }

/// <summary>
/// Farbraeder aus einer Farbe im Bild (docs/Atelier-Arbeitsablauf.md, C4b).
///
/// Neutralisieren wird nicht nachgebaut, sondern gesucht: mit genau der Rechnung, die das Bild
/// erzeugt (<see cref="LiftGammaGainTool"/>), ueber die Flaeche des Rades. So stimmt das
/// Ergebnis auch dort, wo sich die Formel nicht einfach umkehren laesst - bei Gamma, bei
/// Werten an der Grenze, und mit den beiden anderen Zonen, wie sie gerade stehen.
/// </summary>
public static class ColourSolve
{
    /// <summary>
    /// Der Radpunkt einer Zone, bei dem der Ton <paramref name="r"/>, <paramref name="g"/>,
    /// <paramref name="b"/> - so, wie er bei Lift, Gamma und Gain ankommt - grau herauskommt.
    /// Die Helligkeit der Zone bleibt, die beiden anderen Zonen auch. Laesst sich der Ton
    /// nicht ganz neutral machen, weil der Rand des Rades nicht weiter reicht, ist es der
    /// Punkt, der am naechsten kommt.
    /// </summary>
    public static WheelPoint Neutralise(LiftGammaGainTool tool, ZoneKind zone, float r, float g, float b, float scale)
    {
        var triplet = zone switch
        {
            ZoneKind.Lift => tool.Lift,
            ZoneKind.Gamma => tool.Gamma,
            _ => tool.Gain,
        };

        float neutral = zone == ZoneKind.Lift ? 0f : 1f;
        float brightness = ColourWheelMath.ToBrightness(triplet.R, triplet.G, triplet.B, neutral);

        float Out(float value, float lift, float gain, float gamma, float zoneValue) => zone switch
        {
            ZoneKind.Lift => LiftGammaGainTool.Channel(value, zoneValue, gain, 1f / MathF.Max(0.01f, gamma)),
            ZoneKind.Gamma => LiftGammaGainTool.Channel(value, lift, gain, 1f / MathF.Max(0.01f, zoneValue)),
            _ => LiftGammaGainTool.Channel(value, lift, zoneValue, 1f / MathF.Max(0.01f, gamma)),
        };

        // Wie weit die drei Kanaele auseinanderliegen - und ein Hauch Radius dazu: Sind mehrere
        // Punkte gleich gut, gewinnt der, der am wenigsten verstellt.
        float Cost(WheelPoint point)
        {
            var (cr, cg, cb) = ColourWheelMath.ToChannels(point, brightness, neutral, scale);

            float or = Out(r, tool.Lift.R, tool.Gain.R, tool.Gamma.R, cr);
            float og = Out(g, tool.Lift.G, tool.Gain.G, tool.Gamma.G, cg);
            float ob = Out(b, tool.Lift.B, tool.Gain.B, tool.Gamma.B, cb);

            return MathF.Max(or, MathF.Max(og, ob)) - MathF.Min(or, MathF.Min(og, ob)) + 1e-4f * point.Radius;
        }

        return Search(Cost);
    }

    /// <summary>
    /// Die Toenung, die eine Farbe uebernimmt: das Rad in ihre Richtung, so weit, wie ihre
    /// Kanaele auseinanderliegen. Die Helligkeit bleibt - eine Toenung faerbt, sie hellt nicht.
    /// Grau hat keine Richtung und gibt die Mitte.
    /// </summary>
    public static WheelPoint TintOf(float r, float g, float b, float scale)
    {
        float mean = (r + g + b) / 3f;
        if (mean <= 1e-6f) return new WheelPoint(0f, 0f);

        return ColourWheelMath.ToPoint(r / mean / scale, g / mean / scale, b / mean / scale);
    }

    /// <summary>
    /// Sucht ueber die Flaeche des Rades: erst grob, dann um den besten Punkt immer feiner.
    /// Ein paar tausend Rechnungen eines einzelnen Tons - weniger, als ein Bildpunkt-Durchgang kostet.
    /// </summary>
    private static WheelPoint Search(Func<WheelPoint, float> cost)
    {
        var best = new WheelPoint(0f, 0f);
        float bestCost = cost(best);
        float step = 0.05f;

        void Around(float cx, float cy, float reach, float by)
        {
            int n = (int)MathF.Round(reach / by);

            for (int i = -n; i <= n; i++)
            {
                for (int j = -n; j <= n; j++)
                {
                    float x = cx + i * by, y = cy + j * by;
                    if (x * x + y * y > 1f) continue;

                    var point = new WheelPoint(x, y);
                    float c = cost(point);

                    if (c < bestCost)
                    {
                        best = point;
                        bestCost = c;
                    }
                }
            }
        }

        Around(0f, 0f, 1f, step);

        for (int round = 0; round < 4; round++)
        {
            float fine = step / 5f;
            Around(best.X, best.Y, step, fine);
            step = fine;
        }

        return best;
    }
}
