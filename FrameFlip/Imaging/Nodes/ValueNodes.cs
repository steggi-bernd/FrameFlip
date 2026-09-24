using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>Wie zwei Masken verrechnet werden.</summary>
public enum MaskOperation
{
    Add,
    Multiply,
    Subtract,
    Minimum,
    Maximum,
    Difference,
}

/// <summary>
/// Verrechnet zwei Masken - addieren, schneiden, abziehen, das Kleinere oder das
/// Groessere nehmen, den Unterschied. Der Mathe-Knoten aus Blender, beschraenkt auf
/// das, was man mit Masken tut: "die Kryptomatte, aber nicht, was im Nebel liegt".
///
/// Ein Eingang ohne Kabel zaehlt als fester Wert - so laesst sich eine Maske auch mit
/// einer Zahl verrechnen, etwa auf die Haelfte bringen.
/// </summary>
public sealed class MaskMathNode : Node
{
    public const string KindName = "mask-math";

    public MaskOperation Operation { get; set; } = MaskOperation.Multiply;

    /// <summary>Was fuer A gilt, solange dort kein Kabel steckt.</summary>
    public float ValueA { get; set; } = 1f;

    /// <summary>Was fuer B gilt, solange dort kein Kabel steckt.</summary>
    public float ValueB { get; set; } = 1f;

    /// <summary>Auf 0 bis 1 begrenzen - eine Maske ueber eins deckte mehr als ganz.</summary>
    public bool Clamp { get; set; } = true;

    public bool Invert { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("A", SocketType.Value),
        new Socket("B", SocketType.Value),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Maske", SocketType.Value) };

    /// <summary>Stumm reicht er A durch.</summary>
    internal override string? Through => "A";

    internal override void Run(NodeRun run)
    {
        var context = run.Context;
        var a = run.Value("A")?.V;
        var b = run.Value("B")?.V;

        float fixedA = ValueA, fixedB = ValueB;
        var operation = Operation;
        bool clamp = Clamp, invert = Invert;

        var values = context.Take(context.Count);
        int gridWidth = context.GridWidth;

        Parallel.For(0, context.GridHeight, NodeContext.Parallel, gy =>
        {
            int end = (gy + 1) * gridWidth;

            for (int i = gy * gridWidth; i < end; i++)
            {
                float v = Apply(operation, a is null ? fixedA : a[i], b is null ? fixedB : b[i]);

                if (clamp) v = Math.Clamp(v, 0f, 1f);
                if (invert) v = 1f - v;

                values[i] = v;
            }
        });

        run.Set("Maske", new GridValue { V = values });
    }

    internal static float Apply(MaskOperation operation, float a, float b) => operation switch
    {
        MaskOperation.Add => a + b,
        MaskOperation.Multiply => a * b,
        MaskOperation.Subtract => a - b,
        MaskOperation.Minimum => MathF.Min(a, b),
        MaskOperation.Maximum => MathF.Max(a, b),
        MaskOperation.Difference => MathF.Abs(a - b),
        _ => a,
    };
}

/// <summary>
/// Bildet einen Wert von einem Bereich auf einen anderen ab - "Map Range" in Blender.
///
/// Der Weg vom Tiefenpass zum Nebel: Die Tiefe steht in Metern, eine Maske zwischen 0
/// und 1. In der Grundstellung wird ein Wert, der ueber 0 bis 1 hinausgeht, erst auf
/// seine eigene Spanne bezogen - genau wie bei der Passmaske, mit derselben Rechnung -,
/// und "Von" und "Bis" sind Anteile dieser Spanne. Ohne das stehen "Von" und "Bis" in
/// den Einheiten des Eingangs, etwa 5 und 20 Meter; das flackert nicht, wenn die Tiefe
/// von Bild zu Bild eine andere Spanne hat.
/// </summary>
public sealed class MapRangeNode : Node
{
    public const string KindName = "map-range";

    /// <summary>Einen Wert ausserhalb von 0 bis 1 zuerst auf seine eigene Spanne beziehen.</summary>
    public bool Auto { get; set; } = true;

    public float FromLow { get; set; }

    public float FromHigh { get; set; } = 1f;

    public float ToLow { get; set; }

    public float ToHigh { get; set; } = 1f;

    /// <summary>Was vor "Von" und hinter "Bis" liegt, bleibt am Rand stehen.</summary>
    public bool Clamp { get; set; } = true;

    /// <summary>Weich ein- und auslaufen statt geradlinig.</summary>
    public bool Smooth { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Wert", SocketType.Value) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Wert", SocketType.Value) };

    internal override string? Through => "Wert";

    internal override void Run(NodeRun run)
    {
        var raw = run.Raw("Wert");
        var input = run.Value("Wert");

        if (input is null)
        {
            run.Set("Wert", null);
            return;
        }

        var context = run.Context;
        float floor = 0f, span = 1f;

        if (Auto)
        {
            // Dieselbe Spanne wie bei der Passmaske: bei einem gelesenen Pass die der
            // Datei, ueber alle Bildpunkte und nicht nur ueber das Gitter - sonst
            // sprangen die Werte beim Loslassen eines Reglers.
            var (low, high) = raw is SourceImage source ? source.Frame.MaskRange : RangeOf(input.V);

            if (high > 1.0001f || low < -0.0001f)
            {
                floor = low;
                span = MathF.Max(1e-6f, high - low);
            }
        }

        float fromLow = FromLow, fromHigh = FromHigh, toLow = ToLow, toHigh = ToHigh;
        bool clamp = Clamp, smooth = Smooth;

        var v = input.V;
        var values = context.Take(context.Count);
        int gridWidth = context.GridWidth;

        Parallel.For(0, context.GridHeight, NodeContext.Parallel, gy =>
        {
            int end = (gy + 1) * gridWidth;

            for (int i = gy * gridWidth; i < end; i++)
                values[i] = Map((v[i] - floor) / span, fromLow, fromHigh, toLow, toHigh, clamp, smooth);
        });

        run.Set("Wert", new GridValue { V = values });
    }

    /// <summary>
    /// Die Abbildung. Mit "begrenzt", ohne "weich" und auf 0 bis 1 ist sie Rechenschritt
    /// fuer Rechenschritt dasselbe wie Schwarz- und Weisspunkt einer Maske
    /// (<see cref="Masking.Levels"/>) - auch bei "Von" gleich "Bis".
    /// </summary>
    internal static float Map(float value, float fromLow, float fromHigh, float toLow, float toHigh, bool clamp, bool smooth)
    {
        float t = fromHigh <= fromLow
            ? (value >= fromLow ? 1f : 0f)
            : (value - fromLow) / (fromHigh - fromLow);

        if (clamp || smooth) t = Math.Clamp(t, 0f, 1f);
        if (smooth) t = t * t * (3f - 2f * t);

        return toLow + t * (toHigh - toLow);
    }

    /// <summary>Die Spanne der Werte auf dem Gitter - ohne "nicht getroffen", wie bei den Passen.</summary>
    private static (float Low, float High) RangeOf(float[] values)
    {
        float low = float.MaxValue, high = float.MinValue;

        foreach (float value in values)
        {
            if (!float.IsFinite(value) || value >= FloatFrame.NotHit) continue;

            if (value < low) low = value;
            if (value > high) high = value;
        }

        return low > high ? (0f, 1f) : (low, high);
    }
}

/// <summary>Ein Farbstopp eines Verlaufs - wo er steht und welche Farbe er hat.</summary>
public sealed class RampStop
{
    public float Position { get; set; }

    /// <summary>Die Farbe, wie man sie sieht - in Anzeigewerten, siehe <see cref="ColorRampNode"/>.</summary>
    public ColourTriplet Colour { get; set; } = new(0, 0, 0);

    public RampStop Clone() => new() { Position = Position, Colour = new ColourTriplet(Colour.R, Colour.G, Colour.B) };
}

/// <summary>Wie zwischen zwei Stopps uebergeblendet wird.</summary>
public enum RampBlend
{
    Linear,
    Constant,
    Smooth,
}

/// <summary>
/// Setzt einen Wert ueber Farbstopps in Farbe um - "Color Ramp" in Blender. Eine Maske
/// wird zur Nebelfarbe, die Helligkeit zur Gradient Map, die Tiefe zu Falschfarben.
///
/// Die Farben stehen so da, wie man sie sieht. Gibt der Knoten sein Bild hinter der
/// Sichtumwandlung ab, gehen sie genau so hinaus; davor werden sie in Licht
/// umgerechnet, damit ein Grau nach der Anzeige wieder das Grau ist, das man gewaehlt
/// hat. Auf welcher Seite er steht, entscheidet, wer sein Bild liest.
/// </summary>
public sealed class ColorRampNode : Node
{
    public const string KindName = "color-ramp";

    public List<RampStop> Stops { get; set; } = new()
    {
        new RampStop { Position = 0f, Colour = new ColourTriplet(0, 0, 0) },
        new RampStop { Position = 1f, Colour = new ColourTriplet(1, 1, 1) },
    };

    public RampBlend Blend { get; set; } = RampBlend.Linear;

    /// <summary>Was fuer den Faktor gilt, solange dort kein Kabel steckt.</summary>
    public float Factor { get; set; } = 0.5f;

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Faktor", SocketType.Value) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override string? Through => null;

    internal override void Run(NodeRun run)
    {
        var context = run.Context;
        var factor = run.Value("Faktor")?.V;
        float fixedFactor = Factor;

        var stops = Stops.OrderBy(s => s.Position).Select(s => s.Clone()).ToArray();

        if (stops.Length == 0)
        {
            run.Set("Bild", null);
            return;
        }

        bool light = !run.Display;
        var blend = Blend;

        var rgb = context.Take(context.Count * 3);
        var a = context.Take(context.Count);
        int gridWidth = context.GridWidth;

        Parallel.For(0, context.GridHeight, NodeContext.Parallel, gy =>
        {
            int end = (gy + 1) * gridWidth;

            for (int i = gy * gridWidth; i < end; i++)
            {
                Sample(stops, blend, factor is null ? fixedFactor : factor[i], out float r, out float g, out float b);

                if (light)
                {
                    r = ToLight(r);
                    g = ToLight(g);
                    b = ToLight(b);
                }

                rgb[i * 3] = r;
                rgb[i * 3 + 1] = g;
                rgb[i * 3 + 2] = b;
                a[i] = 1f;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = a, Matte = false, Contributed = true });
    }

    /// <summary>Die Farbe an einer Stelle des Verlaufs - in Anzeigewerten, wie die Stopps.</summary>
    public ColourTriplet ColourAt(float position)
    {
        var stops = Stops.OrderBy(s => s.Position).ToArray();
        if (stops.Length == 0) return new ColourTriplet(0, 0, 0);

        Sample(stops, Blend, position, out float r, out float g, out float b);
        return new ColourTriplet(r, g, b);
    }

    /// <summary>Setzt einen Stopp dorthin - mit der Farbe, die der Verlauf dort schon hat.</summary>
    public RampStop Insert(float position)
    {
        var colour = ColourAt(position);
        var stop = new RampStop { Position = Math.Clamp(position, 0f, 1f), Colour = colour };

        Stops.Add(stop);
        return stop;
    }

    private static void Sample(RampStop[] stops, RampBlend blend, float t, out float r, out float g, out float b)
    {
        var first = stops[0];
        var last = stops[^1];

        if (t <= first.Position || stops.Length == 1)
        {
            (r, g, b) = (first.Colour.R, first.Colour.G, first.Colour.B);
            return;
        }

        if (t >= last.Position)
        {
            (r, g, b) = (last.Colour.R, last.Colour.G, last.Colour.B);
            return;
        }

        int k = 1;
        while (k < stops.Length - 1 && stops[k].Position < t) k++;

        var lo = stops[k - 1];
        var hi = stops[k];

        float u = hi.Position > lo.Position ? (t - lo.Position) / (hi.Position - lo.Position) : 1f;

        switch (blend)
        {
            case RampBlend.Constant:
                u = 0f;
                break;

            case RampBlend.Smooth:
                u = u * u * (3f - 2f * u);
                break;
        }

        r = lo.Colour.R + (hi.Colour.R - lo.Colour.R) * u;
        g = lo.Colour.G + (hi.Colour.G - lo.Colour.G) * u;
        b = lo.Colour.B + (hi.Colour.B - lo.Colour.B) * u;
    }

    /// <summary>Anzeigewert (sRGB) zu Licht.</summary>
    internal static float ToLight(float v)
    {
        if (v <= 0f) return 0f;

        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}

/// <summary>
/// Formt eine Maske: ausdehnen oder schrumpfen, dann weichzeichnen - gegen harte Raender
/// an einer Kryptomatte oder einem Farbbereich.
///
/// Beides in Bildpunkten des Bildes. Im groben Durchgang liegen die Gitterpunkte
/// weiter auseinander, und der Radius schrumpft mit - sonst waere die Maske beim Ziehen
/// eines Reglers viel weicher als danach.
/// </summary>
public sealed class MaskShapeNode : Node
{
    public const string KindName = "mask-shape";

    /// <summary>Um wie viele Bildpunkte die Maske waechst; negativ schrumpft sie.</summary>
    public float Grow { get; set; }

    /// <summary>Der Radius der Weichzeichnung in Bildpunkten.</summary>
    public float Soften { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Maske", SocketType.Value) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Maske", SocketType.Value) };

    internal override string? Through => "Maske";

    internal override void Run(NodeRun run)
    {
        var input = run.Value("Maske");
        var context = run.Context;

        int grow = (int)MathF.Round(MathF.Abs(Grow) / context.Step);
        int soften = (int)MathF.Round(MathF.Max(0f, Soften) / context.Step);

        if (input is null || (grow == 0 && soften == 0))
        {
            run.Set("Maske", input);
            return;
        }

        int width = context.GridWidth, height = context.GridHeight;
        var values = context.Take(context.Count);
        var work = context.Take(context.Count);

        Array.Copy(input.V, values, values.Length);

        if (grow > 0)
        {
            bool max = Grow > 0;

            Lines(values, work, width, height, horizontal: true, 2 * grow, (from, to, offset, stride, length, a, b) =>
                Extreme(from, to, offset, stride, length, grow, max, a, b));
            Lines(work, values, width, height, horizontal: false, 2 * grow, (from, to, offset, stride, length, a, b) =>
                Extreme(from, to, offset, stride, length, grow, max, a, b));
        }

        // Dreimal ein Kasten ist fast eine Glocke - und bleibt bei jedem Radius gleich schnell.
        for (int pass = 0; pass < (soften > 0 ? 3 : 0); pass++)
        {
            Lines(values, work, width, height, horizontal: true, 0, (from, to, offset, stride, length, _, _) =>
                Box(from, to, offset, stride, length, soften));
            Lines(work, values, width, height, horizontal: false, 0, (from, to, offset, stride, length, _, _) =>
                Box(from, to, offset, stride, length, soften));
        }

        run.Set("Maske", new GridValue { V = values });
    }

    private delegate void Line(float[] from, float[] to, int offset, int stride, int length, float[] a, float[] b);

    /// <summary>
    /// Jede Zeile oder jede Spalte fuer sich, mit eigenen Hilfsfeldern je Faden - so lang
    /// wie die Zeile und die Polsterung zu beiden Seiten.
    /// </summary>
    private static void Lines(float[] from, float[] to, int width, int height, bool horizontal, int pad, Line line)
    {
        int count = horizontal ? height : width;
        int length = horizontal ? width : height;

        Parallel.For(0, count, NodeContext.Parallel,
                     () => (A: new float[length + pad], B: new float[length + pad]),
                     (k, _, buffers) =>
                     {
                         if (horizontal) line(from, to, k * width, 1, width, buffers.A, buffers.B);
                         else line(from, to, k, width, height, buffers.A, buffers.B);

                         return buffers;
                     },
                     _ => { });
    }

    /// <summary>
    /// Das Groesste (oder Kleinste) in einem Fenster um jeden Punkt - nach van Herk und
    /// Gil-Werman, in fester Zeit je Punkt, egal wie gross der Radius ist. Ausserhalb der
    /// Zeile zaehlt nichts: Das Fenster ist dort einfach kuerzer.
    /// </summary>
    private static void Extreme(float[] from, float[] to, int offset, int stride, int length, int radius, bool max,
                                float[] forward, float[] backward)
    {
        int window = 2 * radius + 1;
        int padded = length + 2 * radius;
        float none = max ? float.NegativeInfinity : float.PositiveInfinity;

        float At(int p) => p < radius || p >= radius + length ? none : from[offset + (p - radius) * stride];

        for (int p = 0; p < padded; p++)
        {
            float x = At(p);
            forward[p] = p % window == 0 ? x : max ? MathF.Max(forward[p - 1], x) : MathF.Min(forward[p - 1], x);
        }

        for (int p = padded - 1; p >= 0; p--)
        {
            float x = At(p);
            backward[p] = p % window == window - 1 || p == padded - 1
                ? x
                : max ? MathF.Max(backward[p + 1], x) : MathF.Min(backward[p + 1], x);
        }

        // Punkt i steht im gepolsterten Feld bei i + radius; sein Fenster reicht von i bis i + 2r.
        for (int i = 0; i < length; i++)
        {
            float left = backward[i];
            float right = forward[i + 2 * radius];

            to[offset + i * stride] = max ? MathF.Max(left, right) : MathF.Min(left, right);
        }
    }

    /// <summary>Der Mittelwert in einem Fenster um jeden Punkt; am Rand zaehlt der Randwert weiter.</summary>
    private static void Box(float[] from, float[] to, int offset, int stride, int length, int radius)
    {
        float Clamped(int i) => from[offset + Math.Clamp(i, 0, length - 1) * stride];

        double sum = 0;
        for (int i = -radius; i <= radius; i++) sum += Clamped(i);

        float scale = 1f / (2 * radius + 1);

        for (int i = 0; i < length; i++)
        {
            to[offset + i * stride] = (float)(sum * scale);
            sum += Clamped(i + radius + 1) - Clamped(i - radius);
        }
    }
}
