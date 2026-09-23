using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Ein gelesenes Bild in voller Aufloesung - so, wie es aus der Datei kommt.
/// </summary>
/// <param name="Matte">
/// Ob seine Deckung eine Freistellung ist, die beim Mischen die Farbe begrenzt. Bei
/// einem Bild aus PNG oder JPEG ja; bei einem Pass aus einer EXR nicht - dort heisst
/// Deckung null "hier wurde nichts getroffen", und die Farbe daneben ist trotzdem
/// Licht. Dieselbe Unterscheidung wie im Composer.
/// </param>
internal sealed record SourceImage(FloatFrame Frame, bool Matte);

/// <summary>
/// Ein gerechnetes Bild auf dem Gitter der Anzeige.
///
/// Die Felder gehoeren niemandem allein: Ein Knoten, der nur die Farbe aendert, darf
/// die Deckung seines Eingangs weitergeben, ohne sie zu kopieren. Deshalb gilt - und
/// jeder Knoten haelt sich daran -: Was einmal als Ergebnis dasteht, wird nicht mehr
/// beschrieben.
/// </summary>
internal sealed class GridImage
{
    /// <summary>Drei Werte je Gitterpunkt: R, G, B.</summary>
    public required float[] Rgb { get; init; }

    /// <summary>
    /// Die Deckung je Gitterpunkt. <see cref="Absent"/> heisst: Hier ist die Ebene gar
    /// nicht - eine platzierte Ebene ausserhalb ihrer Flaeche. Das ist etwas anderes
    /// als Deckung null: Dort traegt die Ebene nichts bei, auch nicht mit Aufdecken.
    /// </summary>
    public required float[] A { get; init; }

    /// <summary>Ob die Deckung beim Mischen die Farbe begrenzt - siehe <see cref="SourceImage"/>.</summary>
    public bool Matte { get; init; }

    /// <summary>
    /// Ob irgendeine Ebene zu diesem Bild beigetragen hat. Ein Stapel, zu dem keine
    /// beitraegt, zeigt das Bild der Datei statt einer schwarzen Flaeche - dieselbe
    /// Antwort wie im Composer.
    /// </summary>
    public bool Contributed { get; init; }

    /// <summary>Die Marke fuer "hier ist die Ebene nicht".</summary>
    public const float Absent = -1f;
}

/// <summary>Eine Zahl je Gitterpunkt - eine Maske.</summary>
internal sealed class GridValue
{
    public required float[] V { get; init; }
}

/// <summary>
/// Alles, was fuer ein Bild feststeht: die Leinwand, das Gitter, die gelesenen Quellen.
/// </summary>
internal sealed class NodeContext
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Jeder wievielte Bildpunkt gerechnet wird - 1 im vollen Durchgang.</summary>
    public required int Step { get; init; }

    public required int[] Columns { get; init; }

    public required int[] Rows { get; init; }

    public int GridWidth => Columns.Length;

    public int GridHeight => Rows.Length;

    public int Count => Columns.Length * Rows.Length;

    public required int Number { get; init; }

    public required IViewTransform View { get; init; }

    public required IReadOnlyDictionary<string, FloatFrame> Sources { get; init; }

    public required IReadOnlyDictionary<PassNeed, FloatFrame?> Data { get; init; }

    /// <summary>
    /// Ob die Durchgaenge ueber das fertige Bild ausbleiben - beim groben Raster und im
    /// Sechzehn-Bit-Ausgang, genau wie im Stapel.
    /// </summary>
    public required bool SkipFramePasses { get; init; }

    public OpticsPlace Place => new(Width, Height, Number);

    // ------------------------------------------------------------ Vorrat

    /// <summary>Woher die Felder kommen - ohne Vorrat werden sie neu angelegt.</summary>
    public GridPool? Pool { get; init; }

    /// <summary>Felder aus dem Vorrat, die in dieser Rechnung ausgegeben wurden.</summary>
    private readonly HashSet<float[]> _owned = new(ReferenceEqualityComparer.Instance);

    /// <summary>Wie viele Ergebnisse ein Feld gerade halten.</summary>
    private readonly Dictionary<float[], int> _holds = new(ReferenceEqualityComparer.Instance);

    /// <summary>Was der laufende Knoten genommen hat.</summary>
    private readonly List<float[]> _taken = new();

    /// <summary>
    /// Ein Feld fuer ein Ergebnis. Was darin steht, ist unbestimmt - wer es nimmt,
    /// beschreibt jede Stelle.
    /// </summary>
    public float[] Take(int length)
    {
        if (Pool is null) return new float[length];

        var array = Pool.Take(length);
        _owned.Add(array);
        _taken.Add(array);

        return array;
    }

    /// <summary>
    /// Der Puffer der oertlichen Wege, bereit fuer so viele Punkte, wie das Gitter hat.
    /// Werte- und Arbeitsfeld sind frisch genommen; das Wertefeld geht mit dem Ergebnis
    /// hinaus.
    /// </summary>
    internal LocalPass.Scratch Scratch()
    {
        if (Pool is null)
        {
            var own = new LocalPass.Scratch();
            own.Hold(Count);
            return own;
        }

        var scratch = Pool.Scratch(Count);
        scratch.Values = Take(Count * 3);
        scratch.Work = Take(Count * 3);

        return scratch;
    }

    /// <summary>Ein Ergebnis steht jetzt da - seine Felder werden gehalten.</summary>
    internal void Hold(object? value)
    {
        if (Pool is null) return;

        foreach (var array in Fields(value))
            if (_owned.Contains(array)) _holds[array] = _holds.GetValueOrDefault(array) + 1;
    }

    /// <summary>Ein Ergebnis wird nicht mehr gebraucht. Haelt niemand mehr ein Feld, geht es zurueck.</summary>
    internal void Drop(object? value)
    {
        if (Pool is null) return;

        foreach (var array in Fields(value))
        {
            if (!_holds.TryGetValue(array, out int holds)) continue;

            if (holds > 1)
            {
                _holds[array] = holds - 1;
                continue;
            }

            _holds.Remove(array);
            Pool.Give(array);
        }
    }

    /// <summary>
    /// Ein Knoten ist fertig: Was er genommen hat und nicht in einem Ergebnis steht,
    /// geht zurueck - Zwischenbilder, abgelesene Quellen, das Arbeitsfeld.
    /// </summary>
    internal void EndUnit()
    {
        if (Pool is null) return;

        foreach (var array in _taken)
            if (!_holds.ContainsKey(array)) Pool.Give(array);

        _taken.Clear();
    }

    private static IEnumerable<float[]> Fields(object? value)
    {
        switch (value)
        {
            case GridImage image:
                yield return image.Rgb;
                yield return image.A;
                break;

            case GridValue mask:
                yield return mask.V;
                break;
        }
    }

    public static readonly ParallelOptions Parallel = new()
    {
        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
    };
}

/// <summary>
/// Ein Knoten beim Rechnen: seine Eingaenge, seine Ausgaenge, und die Umwandlungen
/// dazwischen.
/// </summary>
internal sealed class NodeRun
{
    private readonly Dictionary<string, object?> _inputs;
    private readonly Dictionary<string, object?> _outputs = new(StringComparer.Ordinal);

    public NodeRun(NodeContext context, Dictionary<string, object?> inputs)
    {
        Context = context;
        _inputs = inputs;
    }

    public NodeContext Context { get; }

    public IReadOnlyDictionary<string, object?> Outputs => _outputs;

    public object? Raw(string input) => _inputs.TryGetValue(input, out var value) ? value : null;

    /// <summary>Ein gelesenes Bild - nur wenn eines ankommt.</summary>
    public SourceImage? Source(string input) => Raw(input) as SourceImage;

    /// <summary>
    /// Ein Bild auf dem Gitter. Kommt ein gelesenes an, wird es an den Gitterpunkten
    /// abgelesen; passt seine Groesse nicht zur Leinwand, ist es hier kein Bild - an
    /// einen anderen Ort gehoert es erst ueber "Platzieren".
    /// </summary>
    public GridImage? Image(string input) => Raw(input) switch
    {
        GridImage image => image,
        SourceImage source => Sample(source, Context),
        _ => null,
    };

    /// <summary>Eine Maske. Kommt ein Bild an, zaehlt seine Helligkeit.</summary>
    public GridValue? Value(string input)
    {
        switch (Raw(input))
        {
            case GridValue value:
                return value;

            case GridImage or SourceImage:
                var image = Image(input);
                if (image is null) return null;

                var v = Context.Take(Context.Count);

                for (int i = 0; i < v.Length; i++)
                    v[i] = 0.2126f * image.Rgb[i * 3] + 0.7152f * image.Rgb[i * 3 + 1] + 0.0722f * image.Rgb[i * 3 + 2];

                return new GridValue { V = v };

            default:
                return null;
        }
    }

    public void Set(string output, object? value) => _outputs[output] = value;

    /// <summary>
    /// Liest ein gelesenes Bild an den Gitterpunkten ab. Die Deckung ist die der Datei -
    /// fehlt sie, deckt es ganz; negativ gibt es sie nicht, denn negativ heisst hier
    /// "nicht da".
    /// </summary>
    public static GridImage? Sample(SourceImage source, NodeContext context)
    {
        var frame = source.Frame;
        if (frame.Width != context.Width || frame.Height != context.Height) return null;

        int count = context.Count;
        var rgb = context.Take(count * 3);
        var a = context.Take(count);

        var columns = context.Columns;
        var rows = context.Rows;
        int gridWidth = context.GridWidth;
        int width = frame.Width;

        System.Threading.Tasks.Parallel.For(0, rows.Length, NodeContext.Parallel, gy =>
        {
            int line = rows[gy] * width;
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int i = line + columns[gx];
                int at = row + gx;

                rgb[at * 3] = frame.R[i];
                rgb[at * 3 + 1] = frame.G[i];
                rgb[at * 3 + 2] = frame.B[i];
                a[at] = frame.A is null ? 1f : MathF.Max(0f, frame.A[i]);
            }
        });

        // Ohne eigenen Alphakanal gibt es nichts, was die Farbe begrenzen koennte - dann
        // ist es auch keine Freistellung. Der Composer haelt es genauso (hasOwnAlpha),
        // und nur so bleibt eine Mattegrenze ueber 0,9 ohne Wirkung auf ein Bild ohne
        // Alpha.
        return new GridImage { Rgb = rgb, A = a, Matte = source.Matte && frame.A is not null, Contributed = true };
    }
}
