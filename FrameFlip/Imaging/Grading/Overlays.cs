namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ebenen, die ueber allem liegen - Wasserzeichen, Logos, ein Raster zum Ausrichten.
///
/// Der Unterschied zu einer gewoehnlichen Bildebene ist nicht die Reihenfolge,
/// sondern die STELLE in der Kette. Der Stapel wird in linearem Licht
/// zusammengesetzt und geht danach durch AgX und die Korrektur; ein Wasserzeichen,
/// das dort mitliefe, wuerde tonwertabgebildet und mitkorrigiert. Ein reines Weiss
/// kaeme als Grau heraus, und wer danach die Kurve anhebt, hebt das Wasserzeichen
/// mit an.
///
/// Diese Ebenen werden deshalb ganz am Ende aufgetragen, auf den fertigen
/// Anzeigewerten. Sie sehen in jedem Bild gleich aus, egal was am Bild eingestellt
/// ist - und genau das ist von einem Wasserzeichen verlangt.
/// </summary>
public readonly struct OverlayPlan
{
    public OverlayPlan(FloatFrame frame, LayerPlacement placement, BlendMode mode, float opacity,
                       float sr, float sg, float sb)
    {
        Frame = frame;
        Placement = placement;
        Mode = mode;
        Opacity = opacity;
        ScaleR = sr;
        ScaleG = sg;
        ScaleB = sb;
    }

    public readonly FloatFrame Frame;
    public readonly LayerPlacement Placement;
    public readonly BlendMode Mode;
    public readonly float Opacity, ScaleR, ScaleG, ScaleB;
}

/// <summary>Bereitet die obenauf liegenden Ebenen vor und traegt sie auf.</summary>
public static class Overlays
{
    /// <summary>Nichts obenauf - der Normalfall, und er darf nichts kosten.</summary>
    public static readonly OverlayPlan[] None = Array.Empty<OverlayPlan>();

    /// <summary>
    /// Sammelt die Ebenen, die obenauf liegen, und rechnet ihre Werte in
    /// Anzeigewerte um.
    ///
    /// Umgerechnet wird EINMAL je Bild und nicht je Bildpunkt: Die Umrechnung ist
    /// eine Potenz, und bei 4K waeren es fuenfundsiebzig Millionen davon. Bei einem
    /// Logo von zweihundert Punkten Kantenlaenge kostet sie nichts.
    /// </summary>
    public static OverlayPlan[] Prepare(LayerStack? stack, IReadOnlyDictionary<string, FloatFrame> sources,
                                        int width, int height)
    {
        if (stack is null) return None;

        var plans = new List<OverlayPlan>();

        foreach (var layer in stack.All())
        {
            if (!layer.OnTop || !layer.Visible || layer.Opacity <= 0.0005f) continue;
            if (layer.Content != LayerContent.Image) continue;
            if (!sources.TryGetValue(layer.Source, out var frame)) continue;

            float gain = MathF.Pow(2f, layer.Exposure);

            plans.Add(new OverlayPlan(
                ToDisplay(frame),
                LayerPlacement.Prepare(layer.Place, frame.Width, frame.Height, width, height),
                layer.Mode,
                Math.Clamp(layer.Opacity, 0f, 1f),
                gain * layer.Tint.R, gain * layer.Tint.G, gain * layer.Tint.B));
        }

        return plans.Count == 0 ? None : plans.ToArray();
    }

    /// <summary>
    /// Traegt die Ebenen auf einen fertigen Bildpunkt auf. Die Werte liegen hier
    /// zwischen 0 und 1.
    /// </summary>
    public static void Apply(OverlayPlan[] plans, int x, int y,
                             ref float r, ref float g, ref float b)
    {
        for (int p = 0; p < plans.Length; p++)
        {
            ref readonly var plan = ref plans[p];

            if (!plan.Placement.Locate(x, y, out float u, out float v)) continue;

            plan.Placement.Sample(plan.Frame, u, v,
                                  out float or_, out float og, out float ob, out float oa);

            float opacity = plan.Opacity * Math.Clamp(oa, 0f, 1f);
            if (opacity <= 0f) continue;

            Blending.Mix(plan.Mode, opacity, r, g, b,
                         or_ * plan.ScaleR, og * plan.ScaleG, ob * plan.ScaleB,
                         out r, out g, out b);

            r = Math.Clamp(r, 0f, 1f);
            g = Math.Clamp(g, 0f, 1f);
            b = Math.Clamp(b, 0f, 1f);
        }
    }

    /// <summary>
    /// Rechnet einen gelesenen Frame in Anzeigewerte zurueck.
    ///
    /// Ein PNG wurde beim Lesen linearisiert; diese Umrechnung macht genau das
    /// rueckgaengig, und ein Wasserzeichen kommt damit mit den Werten heraus, die in
    /// der Datei stehen. Eine EXR bekommt dieselbe Kurve - sie ist dann eine
    /// Auslegung und keine Umkehrung, aber ein Wasserzeichen liegt ohnehin als
    /// fertiges Bild vor.
    /// </summary>
    private static FloatFrame ToDisplay(FloatFrame frame)
    {
        if (!frame.IsSceneReferred && frame.Display is not null) return frame.Display;

        int count = frame.PixelCount;

        var r = new float[count];
        var g = new float[count];
        var b = new float[count];

        for (int i = 0; i < count; i++)
        {
            r[i] = Srgb.Encode(frame.R[i]);
            g[i] = Srgb.Encode(frame.G[i]);
            b[i] = Srgb.Encode(frame.B[i]);
        }

        var display = new FloatFrame
        {
            Width = frame.Width,
            Height = frame.Height,
            R = r,
            G = g,
            B = b,
            A = frame.A,
            IsSceneReferred = false,
        };

        // Am Frame gemerkt: Beim Stapellauf wird je Bild vorbereitet, und ein
        // Wasserzeichen ist in jedem Bild dasselbe.
        frame.Display = display;

        return display;
    }
}
