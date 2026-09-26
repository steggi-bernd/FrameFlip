using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Die gerenderte Datei: das Bild, die Renderdaten und jeder Pass, den der Graph braucht.
///
/// Wie der Render-Layers-Knoten in Blender. Die Renderdaten heissen nach dem, was sie
/// bedeuten - Tiefe, Vektor, Normale - und werden je Datei aufgeloest, genau wie die
/// Werkzeuge es heute tun: Eine Datei nennt ihren Tiefenpass "Depth", eine andere "Z".
/// </summary>
public sealed class RenderNode : Node
{
    public const string KindName = "render";

    public const string Picture = "Bild";
    public const string Depth = "Tiefe";
    public const string Motion = "Vektor";
    public const string Normal = "Normale";

    /// <summary>Die Passe, die als eigene Ausgaenge gebraucht werden - nach ihrem Namen in der Datei.</summary>
    public List<string> Passes { get; set; } = new();

    public override IReadOnlyList<Socket> Inputs => Array.Empty<Socket>();

    public override IReadOnlyList<Socket> Outputs
    {
        get
        {
            var outputs = new List<Socket>
            {
                new(Picture, SocketType.Image, Source: true),
                new(Depth, SocketType.Data, Source: true),
                new(Motion, SocketType.Data, Source: true),
                new(Normal, SocketType.Data, Source: true),
            };

            foreach (string pass in Passes) outputs.Add(new Socket(pass, SocketType.Image, Source: true));

            return outputs;
        }
    }

    /// <summary>Welche Renderdaten hinter welchem Ausgang stehen.</summary>
    public static PassNeed? NeedFor(string output) => output switch
    {
        Depth => PassNeed.Depth,
        Motion => PassNeed.Motion,
        Normal => PassNeed.Normal,
        _ => null,
    };

    internal override void Run(NodeRun run)
    {
        var context = run.Context;

        run.Set(Picture, Read(context, ""));

        foreach (string pass in Passes) run.Set(pass, Read(context, pass));

        foreach (var name in new[] { Depth, Motion, Normal })
        {
            var need = NeedFor(name)!.Value;
            run.Set(name, context.Data.TryGetValue(need, out var frame) && frame is not null
                ? new SourceImage(frame, Matte: false)
                : null);
        }
    }

    /// <summary>
    /// Ein Pass dieser Datei. Freigestellt ist er, wenn er nicht aus einer EXR kommt -
    /// eine als Bild geoeffnete PNG traegt eine Maske, ein Pass traegt Licht.
    /// </summary>
    private static SourceImage? Read(NodeContext context, string key)
        => context.Sources.TryGetValue(key, out var frame)
            ? new SourceImage(frame, Matte: !frame.IsSceneReferred)
            : null;
}

/// <summary>Ein anderes Bild von der Platte - ein Logo, eine zweite Aufnahme, ein Raster.</summary>
public sealed class PictureNode : Node
{
    public const string KindName = "picture";

    public string Path { get; set; } = "";

    /// <summary>Ob es mit der Bildnummer mitlaeuft: bei Bild 47 auch dort Bild 47.</summary>
    public bool FollowSequence { get; set; } = true;

    public override IReadOnlyList<Socket> Inputs => Array.Empty<Socket>();

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image, Source: true) };

    internal override void Run(NodeRun run)
        => run.Set("Bild", run.Context.Sources.TryGetValue(Path, out var frame)
            ? new SourceImage(frame, Matte: true)
            : null);
}

/// <summary>
/// Die leere Leinwand: schwarz und ohne Deckung. Worauf der Stapel sich aufbaut.
/// </summary>
public sealed class BlackNode : Node
{
    public const string KindName = "black";

    public override IReadOnlyList<Socket> Inputs => Array.Empty<Socket>();

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var context = run.Context;
        var rgb = context.Take(context.Count * 3);
        var a = context.Take(context.Count);

        Array.Clear(rgb);
        Array.Clear(a);

        run.Set("Bild", new GridImage { Rgb = rgb, A = a });
    }
}

/// <summary>
/// Setzt ein gelesenes Bild auf die Leinwand - verschoben, skaliert, gedreht,
/// beschnitten.
///
/// Mit nichts zu platzieren und in Leinwandgroesse ist es ein Nachschlagen am Index,
/// genau wie im Composer. Sonst wird abgetastet, und ausserhalb der Flaeche ist die
/// Ebene nicht da - nicht schwarz, sondern abwesend.
/// </summary>
public sealed class PlaceNode : Node
{
    public const string KindName = "place";

    public LayerTransform Place { get; set; } = new();

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image, Source: true) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var source = run.Source("Bild");
        var context = run.Context;

        if (source is null)
        {
            run.Set("Bild", null);
            return;
        }

        var frame = source.Frame;

        bool placed = !Place.IsNeutral || frame.Width != context.Width || frame.Height != context.Height;

        if (!placed)
        {
            run.Set("Bild", NodeRun.Sample(source, context));
            return;
        }

        var placement = LayerPlacement.Prepare(Place, frame.Width, frame.Height, context.Width, context.Height);

        int count = context.Count;
        var rgb = context.Take(count * 3);
        var a = context.Take(count);

        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;

        Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            int y = rows[gy];
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = row + gx;
                float covered = placement.Coverage(columns[gx], y, out float u, out float v);

                if (covered <= 0f)
                {
                    // Die Farbe ist hier schwarz, wie in einem frischen Feld - ein
                    // gebrauchtes aus dem Vorrat traegt sonst ein altes Bild.
                    rgb[at * 3] = 0f;
                    rgb[at * 3 + 1] = 0f;
                    rgb[at * 3 + 2] = 0f;
                    a[at] = GridImage.Absent;
                    continue;
                }

                placement.Sample(frame, u, v, out float r, out float g, out float b, out float own);

                rgb[at * 3] = r;
                rgb[at * 3 + 1] = g;
                rgb[at * 3 + 2] = b;

                // Die eigene Deckung und die weiche Kante der Flaeche - beides zusammen
                // ist die Freistellung dieser Ebene.
                a[at] = own * covered;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = a, Matte = true, Contributed = true });
    }
}

/// <summary>Belichtung und Toenung einer Ebene - Multiplikationen am Licht.</summary>
public sealed class ExposureTintNode : Node
{
    public const string KindName = "exposure-tint";

    public float Exposure { get; set; }

    public ColourTriplet Tint { get; set; } = new(1, 1, 1);

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var image = run.Image("Bild");

        if (image is null)
        {
            run.Set("Bild", null);
            return;
        }

        // Derselbe Weg wie im Composer: einmal in Gleitkomma die Potenz, dann je Kanal
        // das Produkt mit der Toenung.
        float gain = MathF.Pow(2f, Exposure);
        float sr = gain * Tint.R, sg = gain * Tint.G, sb = gain * Tint.B;

        var input = image.Rgb;
        var alpha = image.A;
        var rgb = run.Context.Take(input.Length);

        Parallel.For(0, run.Context.GridHeight, NodeContext.Parallel, gy =>
        {
            int from = gy * run.Context.GridWidth;
            int to = from + run.Context.GridWidth;

            for (int at = from; at < to; at++)
            {
                if (alpha[at] < 0f)
                {
                    rgb[at * 3] = rgb[at * 3 + 1] = rgb[at * 3 + 2] = 0f;
                    continue;
                }

                rgb[at * 3] = input[at * 3] * sr;
                rgb[at * 3 + 1] = input[at * 3 + 1] * sg;
                rgb[at * 3 + 2] = input[at * 3 + 2] * sb;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = alpha, Matte = image.Matte, Contributed = image.Contributed });
    }
}

/// <summary>
/// Eine Maske - dieselben Arten wie im Stapel, dieselbe Rechnung (<see cref="MaskSampler"/>).
///
/// "Ebene" ist das, was eine Helligkeitsmaske ansieht; "Untergrund" das, was die
/// Farbbereichsmaske und die Maske "darunter" ansehen. Welcher Eingang gebraucht wird,
/// haengt von der Art ab - ein unverbundener zaehlt als Schwarz.
///
/// "Pass" ist die Quelle einer Passmaske als Kabel - etwa der Tiefenpass der Datei fuer
/// einen Nebel nach Entfernung. Kommt dort etwas an, gilt es vor dem Namen, den der
/// Knoten sonst liest; so wird aus einer Maske, die im Stapel einen Pass beim Namen
/// nannte, im Graphen eine sichtbare Verbindung.
/// </summary>
public sealed class MaskNode : Node
{
    public const string KindName = "mask";

    public LayerMask Mask { get; set; } = new();

    private static readonly Socket[] Looks =
    {
        new("Ebene", SocketType.Image),
        new("Untergrund", SocketType.Image),
    };

    private static readonly Socket[] LooksAndPass =
    {
        new("Ebene", SocketType.Image),
        new("Untergrund", SocketType.Image),
        new("Pass", SocketType.Data, Source: true),
    };

    /// <summary>
    /// Den Eingang "Pass" hat nur eine Passmaske - an jeder anderen waere er ein
    /// Anschluss, der nichts tut. Die Art aendert sich am Knoten nicht mehr; ein Kabel,
    /// das an "Pass" steckt, verliert seinen Eingang also nie.
    /// </summary>
    public override IReadOnlyList<Socket> Inputs => Mask.Kind == MaskKind.Pass ? LooksAndPass : Looks;

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Maske", SocketType.Value) };

    internal override string? Through => null;

    /// <summary>Unter diesem Schluessel liegt ein Pass, der als Kabel kam - kein Pass heisst so.</summary>
    private const string WireKey = "\u0001kabel";

    internal override void Run(NodeRun run)
    {
        var context = run.Context;
        var mask = Mask;
        var sources = context.Sources;

        if (Mask.Kind == MaskKind.Pass && run.Source("Pass") is { } wired)
        {
            mask = Mask.Clone();
            mask.Source = WireKey;
            sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal) { [WireKey] = wired.Frame };
        }

        var sampler = MaskSampler.Prepare(mask, sources, context.Width, context.Height, context.Number);

        // Eine Maske, deren Pass fehlt, faellt weg - die Ebene bleibt ganz, wie im Stapel.
        if (sampler.Kind == MaskKind.None)
        {
            run.Set("Maske", null);
            return;
        }

        var layer = run.Image("Ebene");
        var under = run.Image("Untergrund");

        var values = context.Take(context.Count);

        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;
        int width = context.Width, height = context.Height;

        Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            int y = rows[gy];
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = row + gx;
                int x = columns[gx];

                float lr = 0f, lg = 0f, lb = 0f, ur = 0f, ug = 0f, ub = 0f;

                if (layer is not null)
                {
                    lr = layer.Rgb[at * 3];
                    lg = layer.Rgb[at * 3 + 1];
                    lb = layer.Rgb[at * 3 + 2];
                }

                if (under is not null)
                {
                    ur = under.Rgb[at * 3];
                    ug = under.Rgb[at * 3 + 1];
                    ub = under.Rgb[at * 3 + 2];
                }

                values[at] = sampler.Factor(x, y, width, height, y * width + x, lr, lg, lb, ur, ug, ub);
            }
        });

        run.Set("Maske", new GridValue { V = values });
    }
}

/// <summary>
/// Die Korrektur einer Ebene - Grundkorrektur und Werkzeuge, mit der geliehenen
/// Anzeigeseite (<see cref="LayerGrade"/>).
///
/// Als Einstellungsebene gerechnet (<see cref="Adjustment"/>) bringt das Ergebnis keine
/// eigene Freistellung mit: Es ist eine Korrektur dessen, was darunter liegt, und keine
/// Ebene mit eigenem Umriss.
/// </summary>
public sealed class LayerGradeNode : Node
{
    public const string KindName = "layer-grade";

    public ImageAdjustments? Adjustments { get; set; }

    public GradingStack? Tools { get; set; }

    /// <summary>Ob das Ergebnis als Einstellungsebene gilt - ohne eigene Freistellung.</summary>
    public bool Adjustment { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var image = run.Image("Bild");

        if (image is null)
        {
            run.Set("Bild", null);
            return;
        }

        var grade = LayerGrade.Prepare(Adjustments, Tools);

        if (grade.IsNeutral)
        {
            run.Set("Bild", Adjustment
                ? new GridImage { Rgb = image.Rgb, A = image.A, Matte = false, Contributed = true }
                : image);
            return;
        }

        var input = image.Rgb;
        var alpha = image.A;
        var rgb = run.Context.Take(input.Length);

        Parallel.For(0, run.Context.GridHeight, NodeContext.Parallel, gy =>
        {
            int from = gy * run.Context.GridWidth;
            int to = from + run.Context.GridWidth;

            for (int at = from; at < to; at++)
            {
                if (alpha[at] < 0f)
                {
                    rgb[at * 3] = rgb[at * 3 + 1] = rgb[at * 3 + 2] = 0f;
                    continue;
                }

                float r = input[at * 3], g = input[at * 3 + 1], b = input[at * 3 + 2];

                grade.Apply(ref r, ref g, ref b);

                rgb[at * 3] = r;
                rgb[at * 3 + 1] = g;
                rgb[at * 3 + 2] = b;
            }
        });

        run.Set("Bild", new GridImage
        {
            Rgb = rgb,
            A = alpha,
            Matte = !Adjustment && image.Matte,
            Contributed = Adjustment || image.Contributed,
        });
    }
}

/// <summary>
/// Schneidet ein Bild mit einer Maske aus: dieselben Farben, die Deckung ist die Maske
/// (mal der eigenen). Das Ergebnis ist freigestellt - gemischt traegt es nur dort bei, wo
/// die Maske es zulaesst.
///
/// Die Grundlage fuer "als Ebene ausschneiden" (docs/Projekte-und-Masken.md, Punkt 3 und 4):
/// Was eine Maske zeigt, wird ein Bild fuer sich, das eine eigene Ebene werden und
/// weiterbearbeitet werden kann. Er rechnet jeden Bildpunkt allein - auch im Ausschnitt
/// beim Malen.
///
/// Mit einer Lage (<see cref="Place"/>) wird das ausgeschnittene Stueck versetzt: Die
/// Maske waehlt an der alten Stelle aus, und was sie gewaehlt hat, wandert. Darunter
/// bleibt das Original stehen - es ist ein ausgeschnittenes Stueck, kein Loch.
/// </summary>
public sealed class CutoutNode : Node
{
    public const string KindName = "cutout";

    /// <summary>Wohin das ausgeschnittene Stueck wandert - neutral: bleibt, wo es war.</summary>
    public LayerTransform Place { get; set; } = new();

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Bild", SocketType.Image),
        new Socket("Maske", SocketType.Value),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override string? Through => "Bild";

    internal override void Run(NodeRun run)
    {
        var image = run.Image("Bild");
        var mask = run.Value("Maske");

        // Ohne Maske ist nichts auszuschneiden - das Bild bleibt ganz.
        if (image is null || mask is null)
        {
            run.Set("Bild", image);
            return;
        }

        var context = run.Context;
        var a = context.Take(image.A.Length);
        var own = image.A;
        var cover = mask.V;

        Parallel.For(0, context.GridHeight, NodeContext.Parallel, gy =>
        {
            int start = gy * context.GridWidth;
            int end = start + context.GridWidth;

            for (int at = start; at < end; at++)
            {
                // Wo das Bild gar nicht ist, bleibt es weg - Deckung null waere etwas anderes.
                a[at] = own[at] < 0f
                    ? GridImage.Absent
                    : Math.Clamp(own[at], 0f, 1f) * Math.Clamp(cover[at], 0f, 1f);
            }
        });

        if (Place.IsNeutral)
        {
            // Dieselben Farben - geteilt, nicht kopiert; die Deckung ist neu, und sie
            // begrenzt beim Mischen, was beitraegt.
            run.Set("Bild", new GridImage { Rgb = image.Rgb, A = a, Matte = true, Contributed = image.Contributed });
            return;
        }

        // Die Deckung an der alten Stelle war nur ein Zwischenschritt - sie geht mit dem
        // Ende des Knotens zurueck in den Vorrat.
        run.Set("Bild", Moved(image.Rgb, a, image.Contributed, context));
    }

    /// <summary>
    /// Das ausgeschnittene Stueck an seiner neuen Lage: fuer jeden Punkt der Leinwand
    /// rueckwaerts gefragt, woher er kommt (<see cref="LayerPlacement"/>), dort zwischen den
    /// vier naechsten Rasterpunkten gelesen. Das Stueck ist so gross wie die Leinwand.
    /// </summary>
    private GridImage Moved(float[] rgb, float[] a, bool contributed, NodeContext context)
    {
        var placement = LayerPlacement.Prepare(Place, context.Width, context.Height, context.Width, context.Height);

        int gridWidth = context.GridWidth, gridHeight = context.GridHeight;
        float step = Math.Max(1, context.Step);

        var outRgb = context.Take(context.Count * 3);
        var outA = context.Take(context.Count);
        var columns = context.Columns;
        var rows = context.Rows;

        Parallel.For(0, gridHeight, NodeContext.Parallel, gy =>
        {
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = row + gx;
                float covered = placement.Coverage(columns[gx], rows[gy], out float u, out float v);

                if (covered <= 0f)
                {
                    outRgb[at * 3] = outRgb[at * 3 + 1] = outRgb[at * 3 + 2] = 0f;
                    outA[at] = GridImage.Absent;
                    continue;
                }

                // Rasterpunkt i liegt auf Bildpunkt i mal Schritt - dessen Mitte ist +0,5.
                float fx = Math.Clamp((u - 0.5f) / step, 0f, gridWidth - 1);
                float fy = Math.Clamp((v - 0.5f) / step, 0f, gridHeight - 1);

                int x0 = (int)fx, y0 = (int)fy;
                int x1 = Math.Min(x0 + 1, gridWidth - 1), y1 = Math.Min(y0 + 1, gridHeight - 1);
                float tx = fx - x0, ty = fy - y0;

                int p00 = y0 * gridWidth + x0, p10 = y0 * gridWidth + x1;
                int p01 = y1 * gridWidth + x0, p11 = y1 * gridWidth + x1;

                // Wo das Bild fehlt, deckt es nichts - gelesen als null, nicht als minus eins.
                float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
                float cover = w00 * MathF.Max(0f, a[p00]) + w10 * MathF.Max(0f, a[p10]) +
                              w01 * MathF.Max(0f, a[p01]) + w11 * MathF.Max(0f, a[p11]);

                if (a[p00] < 0f && a[p10] < 0f && a[p01] < 0f && a[p11] < 0f)
                {
                    outRgb[at * 3] = outRgb[at * 3 + 1] = outRgb[at * 3 + 2] = 0f;
                    outA[at] = GridImage.Absent;
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    outRgb[at * 3 + c] = w00 * rgb[p00 * 3 + c] + w10 * rgb[p10 * 3 + c] +
                                         w01 * rgb[p01 * 3 + c] + w11 * rgb[p11 * 3 + c];
                }

                outA[at] = cover * covered;
            }
        });

        return new GridImage { Rgb = outRgb, A = outA, Matte = true, Contributed = contributed };
    }
}

/// <summary>
/// Blendet von "Vorher" nach "Nachher", so weit die Maske reicht - die Korrektur gilt
/// nur dort. Das ist der Umfang "die Maske begrenzt die Farbe" aus dem Stapel, als
/// eigener Knoten: innen die Korrektur, aussen das Bild, dazwischen weich.
/// </summary>
public sealed class RestrictNode : Node
{
    public const string KindName = "restrict";

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Vorher", SocketType.Image),
        new Socket("Nachher", SocketType.Image),
        new Socket("Maske", SocketType.Value),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var before = run.Image("Vorher");
        var after = run.Image("Nachher");
        var mask = run.Value("Maske");

        if (before is null || after is null)
        {
            run.Set("Bild", before ?? after);
            return;
        }

        var rgb = run.Context.Take(before.Rgb.Length);
        var from = before.Rgb;
        var to = after.Rgb;
        var alpha = before.A;

        Parallel.For(0, run.Context.GridHeight, NodeContext.Parallel, gy =>
        {
            int start = gy * run.Context.GridWidth;
            int end = start + run.Context.GridWidth;

            for (int at = start; at < end; at++)
            {
                // Ohne Maske ist der Anteil eins - und gerechnet wird trotzdem ueber
                // dieselbe Formel, wie im Composer. Das Ergebnis ist dann nicht
                // bitgenau "Nachher", sondern Vorher plus der Unterschied.
                float factor = mask is null ? 1f : mask.V[at];

                float lr = from[at * 3], lg = from[at * 3 + 1], lb = from[at * 3 + 2];

                if (alpha[at] >= 0f)
                {
                    lr += (to[at * 3] - lr) * factor;
                    lg += (to[at * 3 + 1] - lg) * factor;
                    lb += (to[at * 3 + 2] - lb) * factor;
                }

                rgb[at * 3] = lr;
                rgb[at * 3 + 1] = lg;
                rgb[at * 3 + 2] = lb;
            }
        });

        run.Set("Bild", new GridImage { Rgb = rgb, A = alpha, Matte = before.Matte, Contributed = before.Contributed });
    }
}

/// <summary>
/// Mischt "Oben" auf "Unten" - mit Modus, Deckkraft, Faktor und der Freistellung des
/// oberen Bildes. Siehe docs/Atelier-Nodes.md, Abschnitt 2.
///
/// Mit <see cref="Clip"/> ist es eine Schnittmaske: Das Ergebnis behaelt Deckung und
/// Freistellung von "Unten". Die angeschnittene Ebene liegt IN ihrem Traeger, und wo
/// der nicht ist, ist auch sie nicht.
/// </summary>
public sealed class MixNode : Node
{
    public const string KindName = "mix";

    public BlendMode Mode { get; set; } = BlendMode.Normal;

    public float Opacity { get; set; } = 1f;

    /// <summary>Im Anzeigeraum mischen statt in linearem Licht - siehe ImageLayer.</summary>
    public bool InDisplay { get; set; }

    /// <summary>Ab welcher Deckung das obere Bild als vorhanden gilt.</summary>
    public float MatteFloor { get; set; }

    /// <summary>Wieviel von dem gezeigt wird, was unter der Freistellung steht.</summary>
    public float Reveal { get; set; }

    /// <summary>An "Unten" anschneiden - eine Schnittmaske.</summary>
    public bool Clip { get; set; }

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Unten", SocketType.Image),
        new Socket("Oben", SocketType.Image),
        new Socket("Faktor", SocketType.Value),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var under = run.Image("Unten");
        var over = run.Image("Oben");
        var factor = run.Value("Faktor");

        if (under is null || over is null)
        {
            // An nichts angeschnitten ist nichts: Fehlt der Traeger, fehlt auch, was an
            // ihm haengt. Ohne oberes Bild bleibt das untere.
            run.Set("Bild", Clip && under is null ? null : under ?? over);
            return;
        }

        float opacity = Math.Clamp(Opacity, 0f, 1f);

        var rgb = run.Context.Take(under.Rgb.Length);
        var a = Clip ? under.A : run.Context.Take(under.A.Length);

        var ur = under.Rgb;
        var ua = under.A;
        var or_ = over.Rgb;
        var oa = over.A;
        bool matte = over.Matte;

        Parallel.For(0, run.Context.GridHeight, NodeContext.Parallel, gy =>
        {
            int start = gy * run.Context.GridWidth;
            int end = start + run.Context.GridWidth;

            for (int at = start; at < end; at++)
            {
                float r0 = ur[at * 3], g0 = ur[at * 3 + 1], b0 = ur[at * 3 + 2];

                // Wo das obere Bild nicht ist, bleibt das untere - samt Deckung.
                if (oa[at] < 0f)
                {
                    rgb[at * 3] = r0;
                    rgb[at * 3 + 1] = g0;
                    rgb[at * 3 + 2] = b0;

                    if (!Clip) a[at] = ua[at];
                    continue;
                }

                // Dieselbe Rechnung und dieselbe Reihenfolge wie im Composer: die
                // Deckkraft, mal dem Faktor, mal der aufbereiteten Freistellung.
                float f = opacity * (factor is null ? 1f : factor.V[at]);

                if (matte)
                    f *= Math.Clamp(ImageLayer.Lift(ImageLayer.CleanMatte(oa[at], MatteFloor), Reveal), 0f, 1f);

                LayerComposer.Blend(Mode, InDisplay, f, r0, g0, b0,
                                    or_[at * 3], or_[at * 3 + 1], or_[at * 3 + 2],
                                    out rgb[at * 3], out rgb[at * 3 + 1], out rgb[at * 3 + 2]);

                if (!Clip)
                {
                    // Wo irgendeine Ebene deckt, deckt das Ergebnis. Eine Freistellung
                    // steckt schon im Anteil; sonst zaehlt die Deckung des Bildes mal
                    // dem Anteil.
                    float covers = matte ? f : oa[at] * f;
                    a[at] = MathF.Max(ua[at], covers);
                }
            }
        });

        run.Set("Bild", new GridImage
        {
            Rgb = rgb,
            A = a,
            Matte = Clip && under.Matte,
            Contributed = under.Contributed || over.Contributed,
        });
    }
}

/// <summary>
/// Der Stapel - oder, wenn keine Ebene zu ihm beigetragen hat, das Bild der Datei.
///
/// Dieselbe Antwort wie im Composer: Ein Rezept von einer Datei mit anderen Passen
/// ergibt das Bild selbst statt einer schwarzen Flaeche. Man sieht, dass die Datei in
/// Ordnung ist, und sucht den Fehler dort, wo er liegt.
/// </summary>
public sealed class FallbackNode : Node
{
    public const string KindName = "fallback";

    public override IReadOnlyList<Socket> Inputs { get; } = new[]
    {
        new Socket("Stapel", SocketType.Image),
        new Socket("Bild", SocketType.Image),
    };

    public override IReadOnlyList<Socket> Outputs { get; } = new[] { new Socket("Bild", SocketType.Image) };

    internal override void Run(NodeRun run)
    {
        var stack = run.Image("Stapel");

        run.Set("Bild", stack is { Contributed: true } ? stack : run.Image("Bild"));
    }
}
