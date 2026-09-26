namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ein Rechteck des Bildes in Bildpunkten: links und oben eingeschlossen, rechts und
/// unten nicht.
/// </summary>
public readonly record struct GraphRegion(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    /// <summary>Das Rechteck, beschnitten auf die Leinwand - oder null, wenn nichts bleibt.</summary>
    public GraphRegion? Within(int width, int height)
    {
        var inside = new GraphRegion(Math.Max(0, X0), Math.Max(0, Y0), Math.Min(width, X1), Math.Min(height, Y1));
        return inside.Width > 0 && inside.Height > 0 ? inside : null;
    }

    /// <summary>Das kleinste Rechteck um eine Flaeche in Gleitkomma, mit Rand.</summary>
    public static GraphRegion Around(float x0, float y0, float x1, float y1, int margin)
        => new((int)MathF.Floor(x0) - margin, (int)MathF.Floor(y0) - margin,
               (int)MathF.Ceiling(x1) + margin + 1, (int)MathF.Ceiling(y1) + margin + 1);

    /// <summary>
    /// Nach aussen auf ein Raster gerundet. Ein Pinsel liefert Rechtecke jeder Groesse;
    /// gerundet haben sie wenige, und ihre Felder finden sich im Vorrat wieder.
    /// </summary>
    public GraphRegion Snapped(int grid)
        => grid <= 1 ? this
            : new(FloorTo(X0, grid), FloorTo(Y0, grid), -FloorTo(-X1, grid), -FloorTo(-Y1, grid));

    private static int FloorTo(int value, int grid) => (int)Math.Floor(value / (double)grid) * grid;

    internal int[] Columns() => Enumerable.Range(X0, Width).ToArray();

    internal int[] Rows() => Enumerable.Range(Y0, Height).ToArray();
}

public static partial class GraphEvaluator
{
    /// <summary>
    /// Rechnet nur ein Rechteck des Bildes neu und schreibt es an seine Stelle - voll
    /// aufgeloest, also genau das, was der volle Durchgang dort schriebe.
    ///
    /// Der Anlass ist der Pinsel. Ein Strich aendert ein paar Zentimeter Maske, und
    /// trotzdem rechnete jede Mausbewegung das ganze Bild - bei 4K grob 23 ms, mit vielen
    /// Passen mehr. Hinter einer Maske liegen aber meist nur Mischen und Farbe, und die
    /// rechnen jeden Bildpunkt allein aus demselben Bildpunkt davor. Ausserhalb des
    /// Strichs aendert sich also nichts, und innerhalb genuegt das Rechteck.
    ///
    /// Das geht nur, wenn (1) ein Knoten gewaehlt ist und alles, was von ausserhalb in
    /// ihn und hinter ihn fliesst, voll aufgeloest im Zwischenspeicher liegt, und (2) alles,
    /// was neu zu rechnen ist, punktweise rechnet (<see cref="RegionSafe"/>). Sonst kommt
    /// false zurueck, das Ziel bleibt unberuehrt, und der Aufrufer rechnet wie bisher.
    /// </summary>
    public static bool RenderRegion(NodeGraph graph, GraphInputs inputs, GraphRegion region, IntPtr destination, int stride)
        => Guarded(inputs, () => RenderRegionLocked(graph, inputs, region, destination, stride));

    private static bool RenderRegionLocked(NodeGraph graph, GraphInputs inputs, GraphRegion region, IntPtr destination, int stride)
    {
        var (image, context) = Evaluate(graph, inputs, sixteen: false, region);
        if (image is null || context is null) return false;

        try
        {
            WriteRegion(image, context, destination, stride);
        }
        finally
        {
            context.Drop(image);
        }

        return true;
    }

    /// <summary>
    /// Ob ein Knoten jeden Bildpunkt allein aus demselben Bildpunkt seiner Eingaenge
    /// rechnet - oder aus gelesenen Bildern an einer festen Stelle, wie beim Platzieren.
    ///
    /// Eine Liste der Erlaubten und nicht der Verbotenen: Ein neuer Knoten, den hier
    /// niemand eingetragen hat, rechnet dann voll - langsamer, aber richtig. Stumm ist
    /// jeder Knoten erlaubt, er reicht nur durch.
    /// </summary>
    internal static bool RegionSafe(Node node) => node.Muted || node switch
    {
        // Eine Einstellungsebene mit oertlichen, geometrischen oder Daten-Werkzeugen oder
        // Durchgaengen ueber das fertige Bild liest Nachbarn. Die optischen - Vignette,
        // Korn, Dither - brauchen nur den Ort des Bildpunkts, keine Nachbarn.
        LayerGradeNode grade => grade.Tools is not { } tools ||
                                tools.Local.Count == 0 && tools.Frame.Count == 0 &&
                                tools.Geometry.Count == 0 && tools.Data.Count == 0,

        // Ein versetztes Ausschneiden liest sein Stueck an der alten Stelle - ausserhalb des
        // Ausschnitts. Unversetzt rechnet es jeden Punkt allein.
        CutoutNode cutout => cutout.Place.IsNeutral,

        // OpticsNode: siehe IOpticsTool - dieselbe Rechnung an jedem Punkt, nur mit seinem
        // Ort und der Bildnummer.
        RenderNode or PictureNode or BlackNode or PlaceNode or ExposureTintNode or MaskNode or RestrictNode
            or MixNode or FallbackNode or LightNode or PointToolNode or OpticsNode or ViewNode or ToneNode
            or MaskMathNode or MapRangeNode or ColorRampNode or OutputNode => true,

        // Unschaerfe, Glare, Verzerrung, Tiefe, Sortieren, Formen einer Maske ...
        _ => false,
    };

    /// <summary>
    /// Schneidet aus einem gemerkten, voll aufgeloesten Ergebnis das Stueck fuer den
    /// Ausschnitt. False, wenn es nicht die volle Aufloesung hat - dann passt es nicht.
    /// Was kein Gitter ist (ein gelesenes Bild am Kabel, nichts), bleibt, wie es ist.
    /// </summary>
    private static bool Cut(object? value, int width, int height, GraphRegion box, NodeContext context, out object? cut)
    {
        int count = width * height;
        int w = box.Width, h = box.Height;

        // Aus dem Vorrat des Ausschnitts: Neu angelegt waeren das je Takt einige Felder
        // auf dem grossen Stapel der Speicherbereinigung.
        float[] Take(int length) => context.Pool?.Take(length) ?? new float[length];

        switch (value)
        {
            case GridImage image:
            {
                if (image.Rgb.Length != count * 3 || image.A.Length != count)
                {
                    cut = null;
                    return false;
                }

                var rgb = Take(w * h * 3);
                var a = Take(w * h);

                for (int y = 0; y < h; y++)
                {
                    int from = (box.Y0 + y) * width + box.X0;
                    Array.Copy(image.Rgb, from * 3, rgb, y * w * 3, w * 3);
                    Array.Copy(image.A, from, a, y * w, w);
                }

                cut = new GridImage { Rgb = rgb, A = a, Matte = image.Matte, Contributed = image.Contributed };
                return true;
            }

            case GridValue grid:
            {
                if (grid.V.Length != count)
                {
                    cut = null;
                    return false;
                }

                var v = Take(w * h);

                for (int y = 0; y < h; y++)
                    Array.Copy(grid.V, (box.Y0 + y) * width + box.X0, v, y * w, w);

                cut = new GridValue { V = v };
                return true;
            }

            default:
                cut = value;
                return true;
        }
    }

    /// <summary>Wie <see cref="Write"/> im vollen Durchgang, nur an die Stellen des Ausschnitts.</summary>
    private static unsafe void WriteRegion(GridImage image, NodeContext context, IntPtr destination, int stride)
    {
        byte* target = (byte*)destination.ToPointer();
        var rgb = image.Rgb;
        var a = image.A;
        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;

        Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            byte* row = target + (long)rows[gy] * stride;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int i = gy * gridWidth + gx;
                byte* pixel = row + columns[gx] * 4;

                pixel[0] = FloatFrameProcessor.ToByte(rgb[i * 3 + 2]);
                pixel[1] = FloatFrameProcessor.ToByte(rgb[i * 3 + 1]);
                pixel[2] = FloatFrameProcessor.ToByte(rgb[i * 3]);
                pixel[3] = FloatFrameProcessor.ToByte(Math.Clamp(a[i], 0f, 1f));
            }
        });
    }
}
