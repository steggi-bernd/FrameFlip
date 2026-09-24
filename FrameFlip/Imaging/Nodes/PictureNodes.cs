using System.Runtime.InteropServices;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ein Knoten mit einem Bild herein und einem heraus, der jeden Gitterpunkt fuer sich
/// rechnet. Die Deckung geht unveraendert durch.
/// </summary>
public abstract class PointNode : Node
{
    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    /// <summary>Ob es gerade nichts zu rechnen gibt - dann geht das Bild unberuehrt durch.</summary>
    internal abstract bool Idle { get; }

    /// <summary>Einmal je Bild, vor den Punkten.</summary>
    internal virtual void Prepare(NodeContext context)
    {
    }

    /// <summary>Ein Punkt. Muss nach <see cref="Prepare"/> von mehreren Faeden gerufen werden koennen.</summary>
    internal abstract void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b);

    internal override void Run(NodeRun run) => RunChain(new[] { this }, run);

    /// <summary>
    /// Rechnet Punktknoten, die hintereinander haengen, in EINEM Durchgang: je Gitterpunkt
    /// alle nacheinander, wie der Prozessor im Stapel. Das Ergebnis ist dasselbe wie
    /// Knoten fuer Knoten - dieselben Rechnungen in derselben Reihenfolge -, nur ohne
    /// ein Zwischenbild je Knoten. Bei sieben Werkzeugen am Bild sind das sechs Bilder
    /// weniger, die angelegt, geschrieben und wieder gelesen werden.
    /// </summary>
    internal static void RunChain(IReadOnlyList<PointNode> chain, NodeRun run)
    {
        var image = run.Image("Bild");
        var active = chain.Where(n => !n.Muted && !n.Idle).ToArray();

        if (image is null || active.Length == 0)
        {
            run.Set("Bild", image);
            return;
        }

        var context = run.Context;
        foreach (var node in active) node.Prepare(context);

        var input = image.Rgb;
        var rgb = context.Take(input.Length);
        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;

        Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            int y = rows[gy];
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = (row + gx) * 3;
                int x = columns[gx];
                float r = input[at], g = input[at + 1], b = input[at + 2];

                foreach (var node in active) node.Shade(context, x, y, ref r, ref g, ref b);

                rgb[at] = r;
                rgb[at + 1] = g;
                rgb[at + 2] = b;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = image.A, Matte = image.Matte, Contributed = image.Contributed });
    }
}

/// <summary>Belichtung und Saettigung des ganzen Bildes - der Anfang der linearen Seite.</summary>
public sealed class LightNode : PointNode
{
    public const string KindName = "light";

    public double Exposure { get; set; }

    public double Saturation { get; set; } = 1.0;

    private float _gain = 1f, _saturation = 1f;

    internal override bool Idle => Exposure == 0 && Saturation == 1.0;

    /// <summary>Die Potenz einmal je Bild, wie im Prozessor - in doppelter Genauigkeit gerechnet.</summary>
    internal override void Prepare(NodeContext context)
    {
        _gain = (float)Math.Pow(2.0, Exposure);
        _saturation = (float)Saturation;
    }

    internal override void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b)
        => FloatFrameProcessor.Light(_gain, _saturation, ref r, ref g, ref b);
}

/// <summary>
/// Ein Werkzeug, das jeden Bildpunkt fuer sich behandelt - Weissabgleich, Kurven,
/// Lift/Gamma/Gain, HSL, eine Look-Tabelle. Auf welcher Seite der Anzeige es steht,
/// entscheidet der Graph; das Werkzeug sagt nur, wo es hingehoert.
/// </summary>
public sealed class PointToolNode : PointNode
{
    public const string KindName = "point";

    public IGradingTool? Tool { get; set; }

    internal override bool Idle => Tool is null || Tool.IsNeutral;

    internal override void Prepare(NodeContext context) => Tool!.Prepare();

    internal override void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b)
        => Tool!.Apply(ref r, ref g, ref b);
}

/// <summary>Ein Werkzeug, das den Ort kennt - Vignette, Korn, Raster.</summary>
public sealed class OpticsNode : PointNode
{
    public const string KindName = "optics";

    public IOpticsTool? Tool { get; set; }

    private OpticsPlace _place;

    internal override bool Idle => Tool is null || Tool.IsNeutral;

    internal override void Prepare(NodeContext context)
    {
        Tool!.Prepare();
        _place = context.Place;
    }

    internal override void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b)
        => Tool!.Apply(in _place, x, y, ref r, ref g, ref b);
}

/// <summary>
/// Die Sichtumwandlung: aus Szenenlicht werden Anzeigewerte. Links davon rechnet alles
/// in Licht, rechts davon auf einer Skala mit Weiss.
/// </summary>
public sealed class ViewNode : PointNode
{
    public const string KindName = "view";

    internal override bool Idle => false;

    internal override void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b)
        => context.View.Apply(ref r, ref g, ref b);
}

/// <summary>Schwarz- und Weisspunkt, Gamma und Kontrast - auf Anzeigewerten.</summary>
public sealed class ToneNode : PointNode
{
    public const string KindName = "tone";

    public double BlackPoint { get; set; }

    public double WhitePoint { get; set; } = 1.0;

    public double Gamma { get; set; } = 1.0;

    public double Contrast { get; set; } = 1.0;

    private float _black, _white, _span, _inverseGamma, _contrast;

    /// <summary>Dieselbe Frage wie im Prozessor: Ist an den Tonwerten etwas eingestellt?</summary>
    internal override bool Idle
    {
        get
        {
            Measure();

            return FloatFrameProcessor.Same(_black, 0f) && FloatFrameProcessor.Same(_white, 1f) &&
                   FloatFrameProcessor.Same(_inverseGamma, 1f) && FloatFrameProcessor.Same(_contrast, 1f);
        }
    }

    /// <summary>Dieselbe Vorbereitung wie im Prozessor.</summary>
    private void Measure()
    {
        _black = (float)BlackPoint;
        _white = (float)WhitePoint;
        _span = _white - _black;
        if (MathF.Abs(_span) < 1e-6f) _span = 1e-6f;

        _inverseGamma = 1f / MathF.Max(0.0001f, (float)Gamma);
        _contrast = (float)Contrast;
    }

    internal override void Prepare(NodeContext context) => Measure();

    internal override void Shade(NodeContext context, int x, int y, ref float r, ref float g, ref float b)
    {
        r = FloatFrameProcessor.Tone(r, _black, _span, _inverseGamma, _contrast);
        g = FloatFrameProcessor.Tone(g, _black, _span, _inverseGamma, _contrast);
        b = FloatFrameProcessor.Tone(b, _black, _span, _inverseGamma, _contrast);
    }
}

/// <summary>
/// Ein Werkzeug mit oertlicher Wirkung - Klarheit, Schaerfe, Rauschminderung, Glanz,
/// Halation, Dunst, Struktur.
///
/// Mehrere hintereinander rechnet der Graph in EINEM Durchgang, wie der Stapel: Dort
/// teilen sich Werkzeuge mit gleichem Radius ihre Weichzeichnung, und zwei getrennte
/// Durchgaenge gaeben ein anderes Bild. Siehe <see cref="RunChain"/>.
/// </summary>
public sealed class LocalNode : Node
{
    public const string KindName = "local";

    public ILocalTool? Tool { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run) => RunChain(new[] { this }, run);

    internal static void RunChain(IReadOnlyList<LocalNode> chain, NodeRun run)
    {
        var image = run.Image("Bild");
        var context = run.Context;

        var tools = chain.Where(n => !n.Muted && n.Tool is { IsNeutral: false }).Select(n => n.Tool!).ToArray();

        if (image is null || tools.Length == 0)
        {
            run.Set("Bild", image);
            return;
        }

        foreach (var tool in tools) tool.Prepare();

        var scratch = context.Scratch();
        Array.Copy(image.Rgb, scratch.Values, image.Rgb.Length);

        LocalPass.Run(scratch, tools, context.GridWidth, context.GridHeight, context.Width, context.Step);

        // Die Deckung beruehren diese Werkzeuge nicht - sie geht in voller Genauigkeit
        // durch, wie im Sechzehn-Bit-Weg des Stapels.
        run.Set("Bild", new GridImage { Rgb = scratch.Values, A = image.A, Matte = image.Matte, Contributed = image.Contributed });
    }
}

/// <summary>
/// Ein Werkzeug, das Bildpunkte verschiebt - Verzeichnung, Farbsaum.
///
/// Mehrere hintereinander werden in EINEM Durchgang abgetastet, wie im Stapel: Die
/// Kanalfaktoren multiplizieren sich, und zweimal abgetastet waere zweimal weichgezeichnet.
/// </summary>
public sealed class GeometryNode : Node
{
    public const string KindName = "geometry";

    public IGeometryTool? Tool { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run) => RunChain(new[] { this }, run);

    internal static void RunChain(IReadOnlyList<GeometryNode> chain, NodeRun run)
    {
        var image = run.Image("Bild");
        var context = run.Context;

        var tools = chain.Where(n => !n.Muted && n.Tool is { IsNeutral: false }).Select(n => n.Tool!).ToArray();

        if (image is null || tools.Length == 0)
        {
            run.Set("Bild", image);
            return;
        }

        foreach (var tool in tools) tool.Prepare();

        var scratch = Fill(image, context);

        GeometryPass.Run(scratch, tools, context.Place, context.GridWidth, context.GridHeight, context.Step);

        run.Set("Bild", Read(scratch, image, context));
    }

    /// <summary>Das Bild in den Puffer der oertlichen Wege - die Deckung als Byte, wie dort.</summary>
    internal static LocalPass.Scratch Fill(GridImage image, NodeContext context)
    {
        var scratch = context.Scratch();

        Array.Copy(image.Rgb, scratch.Values, image.Rgb.Length);

        var alpha = scratch.Alpha;
        var a = image.A;

        for (int i = 0; i < a.Length; i++) alpha[i] = FloatFrameProcessor.ToByte(Math.Clamp(a[i], 0f, 1f));

        return scratch;
    }

    /// <summary>Zurueck aus dem Puffer - mit der Deckung, die dort mitgewandert ist.</summary>
    internal static GridImage Read(LocalPass.Scratch scratch, GridImage image, NodeContext context)
    {
        var alpha = scratch.Alpha;
        var a = context.Take(context.Count);

        for (int i = 0; i < a.Length; i++) a[i] = alpha[i] / 255f;

        return new GridImage { Rgb = scratch.Values, A = a, Matte = image.Matte, Contributed = image.Contributed };
    }
}

/// <summary>
/// Ein Werkzeug mit Renderdaten - Tiefenschaerfe, Bewegungsunschaerfe, Verschiebung.
/// Der Pass kommt als Kabel: vom Ausgang "Tiefe", "Vektor" oder "Normale" der Datei.
///
/// Fehlt er, ruht das Werkzeug - es sei denn, es kommt ohne aus (die Verschiebung mit
/// einer Welle). Dieselbe Regel wie im Stapel.
/// </summary>
public sealed class DataNode : Node
{
    public const string KindName = "data";

    public IDataTool? Tool { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Bild", SocketType.Image),
        new Socket("Daten", SocketType.Data, Source: true),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var image = run.Image("Bild");
        var context = run.Context;

        if (image is null || Tool is null || Tool.IsNeutral)
        {
            run.Set("Bild", image);
            return;
        }

        var data = run.Source("Daten")?.Frame;
        var scratch = GeometryNode.Fill(image, context);

        // Ohne Pass ruht es - aber die Deckung geht trotzdem durch den Puffer, wie im
        // Stapel: Dort steht sie ab dem ersten Werkzeug dieser Art als Byte.
        if (data is not null || Tool.Optional)
        {
            Tool.Prepare();
            Tool.Run(scratch, data, context.Columns, context.Rows, context.Width, context.Step, context.Number);
        }

        run.Set("Bild", GeometryNode.Read(scratch, image, context));
    }
}

/// <summary>
/// Ein Bild obenauf - ein Wasserzeichen, ein Logo -, aufgetragen auf die fertigen
/// Anzeigewerte. Es sieht in jedem Bild gleich aus, egal was am Bild eingestellt ist.
/// </summary>
public sealed class OverlayNode : Node
{
    public const string KindName = "overlay";

    public LayerTransform Place { get; set; } = new();

    public BlendMode Mode { get; set; } = BlendMode.Normal;

    public float Opacity { get; set; } = 1f;

    public float Exposure { get; set; }

    public ColourTriplet Tint { get; set; } = new(1, 1, 1);

    public float MatteFloor { get; set; }

    public float Reveal { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Bild", SocketType.Image),
        new Socket("Ebene", SocketType.Image, Source: true),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var image = run.Image("Bild");
        var layer = run.Source("Ebene");
        var context = run.Context;

        if (image is null || layer is null)
        {
            run.Set("Bild", image);
            return;
        }

        var frame = layer.Frame;
        float gain = MathF.Pow(2f, Exposure);

        var plans = new[]
        {
            new OverlayPlan(Overlays.ToDisplay(frame),
                            LayerPlacement.Prepare(Place, frame.Width, frame.Height, context.Width, context.Height),
                            Mode, Math.Clamp(Opacity, 0f, 1f),
                            gain * Tint.R, gain * Tint.G, gain * Tint.B, MatteFloor, Reveal),
        };

        var input = image.Rgb;
        var rgb = context.Take(input.Length);
        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;

        Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            int y = rows[gy];
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = (row + gx) * 3;
                float r = input[at], g = input[at + 1], b = input[at + 2];

                Overlays.Apply(plans, columns[gx], y, ref r, ref g, ref b);

                rgb[at] = r;
                rgb[at + 1] = g;
                rgb[at + 2] = b;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = image.A, Matte = image.Matte, Contributed = image.Contributed });
    }
}

/// <summary>
/// Ein Durchgang ueber das fertige Bild in acht Bit - Rasterdiffusion, Pixelsortierung.
///
/// Er laeuft nur im vollen Durchgang und nicht im Sechzehn-Bit-Ausgang, wie im Stapel.
/// Steht er mitten im Graphen, wird das Bild vorher auf acht Bit gebracht und danach
/// zurueck - am Ende des Graphen ist das verlustfrei, weil die Ausgabe ohnehin acht
/// Bit schreibt.
/// </summary>
public sealed class FramePassNode : Node
{
    public const string KindName = "frame";

    public IFramePass? Pass { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override unsafe void Run(NodeRun run)
    {
        var image = run.Image("Bild");
        var context = run.Context;

        if (image is null || Pass is null || Pass.IsNeutral || context.SkipFramePasses || context.Step != 1)
        {
            run.Set("Bild", image);
            return;
        }

        Pass.Prepare();

        int width = context.Width, height = context.Height;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        var rgb = image.Rgb;
        var a = image.A;

        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = FloatFrameProcessor.ToByte(rgb[i * 3 + 2]);
            pixels[i * 4 + 1] = FloatFrameProcessor.ToByte(rgb[i * 3 + 1]);
            pixels[i * 4 + 2] = FloatFrameProcessor.ToByte(rgb[i * 3]);
            pixels[i * 4 + 3] = FloatFrameProcessor.ToByte(Math.Clamp(a[i], 0f, 1f));
        }

        fixed (byte* start = pixels)
            Pass.Apply((IntPtr)start, width, height, stride, context.Number);

        var back = context.Take(rgb.Length);
        var alpha = context.Take(a.Length);

        for (int i = 0; i < width * height; i++)
        {
            back[i * 3] = pixels[i * 4 + 2] / 255f;
            back[i * 3 + 1] = pixels[i * 4 + 1] / 255f;
            back[i * 3 + 2] = pixels[i * 4] / 255f;
            alpha[i] = pixels[i * 4 + 3] / 255f;
        }

        run.Set("Bild", new GridImage { Rgb = back, A = alpha, Matte = image.Matte, Contributed = image.Contributed });
    }
}

/// <summary>Wo das Bild herauskommt - die Vorschau, der Export.</summary>
public sealed class OutputNode : Node
{
    public const string KindName = "output";

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs => Array.Empty<Socket>();

    internal override void Run(NodeRun run) => run.Set("Bild", run.Image("Bild"));
}
