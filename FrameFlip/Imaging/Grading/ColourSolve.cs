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

    /// <summary>Die Grenzen des Temperaturreglers in Mired (15000 bis 2000 Kelvin) und seine Grundstellung.</summary>
    private const float MinMired = 66.7f, MaxMired = 500f, NeutralMired = 1e6f / WhiteBalanceTool.NeutralKelvin;

    /// <summary>
    /// Der Weissabgleich - Temperatur in Kelvin und Tendenz -, bei dem ein Ton dieselbe Farbart
    /// bekommt wie <paramref name="target"/>; ohne Ziel wird er grau (C4c).
    ///
    /// <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/> ist das Licht, wie es beim
    /// Weissabgleich ankommt. <paramref name="shown"/> rechnet danach in die Anzeige, damit
    /// verglichen wird, was man sieht - eine gemerkte Farbe ist eine gesehene. Gesucht wird wie
    /// bei den Raedern mit der Rechnung des Werkzeugs selbst, innerhalb der Grenzen seiner Regler.
    /// </summary>
    public static (float Kelvin, float Tint) WhiteBalance(float r, float g, float b,
                                                          Func<(float R, float G, float B), (float R, float G, float B)> shown,
                                                          (float R, float G, float B)? target)
    {
        var goal = target ?? (1f, 1f, 1f);

        float Cost(float mired, float tint)
        {
            var tool = new WhiteBalanceTool { Kelvin = 1e6f / mired, Tint = tint };
            tool.Prepare();

            float wr = r, wg = g, wb = b;
            tool.Apply(ref wr, ref wg, ref wb);

            var (sr, sg, sb) = shown((wr, wg, wb));

            // Ein Hauch Abstand zur Grundstellung: Sind mehrere gleich gut, gewinnt die kleinste Aenderung.
            return Chroma(sr, sg, sb, goal) + 1e-5f * (MathF.Abs(mired - NeutralMired) / 100f + MathF.Abs(tint) / 100f);
        }

        float bestMired = NeutralMired, bestTint = 0f;
        float bestCost = Cost(bestMired, bestTint);

        void Around(float cm, float ct, float reachM, float reachT, int steps)
        {
            for (int i = -steps; i <= steps; i++)
            {
                for (int j = -steps; j <= steps; j++)
                {
                    float m = Math.Clamp(cm + i * reachM / steps, MinMired, MaxMired);
                    float t = Math.Clamp(ct + j * reachT / steps, -100f, 100f);
                    float c = Cost(m, t);

                    if (c < bestCost)
                    {
                        bestMired = m;
                        bestTint = t;
                        bestCost = c;
                    }
                }
            }
        }

        float stepM = (MaxMired - MinMired) / 2f, stepT = 100f;
        Around((MinMired + MaxMired) / 2f, 0f, stepM, stepT, 30);

        for (int round = 0; round < 4; round++)
        {
            stepM /= 12f;
            stepT /= 12f;
            Around(bestMired, bestTint, stepM, stepT, 6);
        }

        return (1e6f / bestMired, bestTint);
    }

    /// <summary>Wie weit zwei Farbarten auseinanderliegen - die Helligkeit zaehlt nicht.</summary>
    private static float Chroma(float r, float g, float b, (float R, float G, float B) goal)
    {
        float sum = r + g + b, target = goal.R + goal.G + goal.B;
        if (sum <= 1e-6f || target <= 1e-6f) return 10f;

        return MathF.Abs(r / sum - goal.R / target) + MathF.Abs(g / sum - goal.G / target) + MathF.Abs(b / sum - goal.B / target);
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
