using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Ein Stuetzpunkt der Kurve. Beide Werte liegen zwischen 0 und 1.</summary>
public readonly record struct CurvePoint(float X, float Y);

/// <summary>
/// Eine Gradationskurve ueber Stuetzpunkten.
///
/// Zwischen den Punkten wird **monoton kubisch** interpoliert, nach Fritsch und
/// Carlson - und das ist die eigentliche Entscheidung an dieser Klasse. Eine
/// gewoehnliche kubische Spline laeuft durch dieselben Punkte, schwingt dabei aber
/// ueber: zwischen zwei eng stehenden Stuetzpunkten mit starkem Anstieg biegt sie
/// nach unten aus, bevor sie hochzieht.
///
/// Auf einer Tonwertkurve heisst das, dass ein Bereich dunkler wird, obwohl der
/// Anwender die Kurve dort angehoben hat. In einem Verlauf ist das als Umkehrung zu
/// sehen, und es sieht nach einem Fehler im Bild aus, nicht nach einem der Kurve.
/// Die monotone Variante beschneidet die Tangenten gerade so weit, dass das nicht
/// passieren kann: steigt die Folge der Stuetzpunkte, steigt auch die Kurve.
/// </summary>
public sealed class ToneCurve
{
    /// <summary>
    /// Feinheit der vorberechneten Tabelle.
    ///
    /// Die Kurve wird je Bildpunkt und Kanal abgefragt - bei 4K sind das 25 Millionen
    /// Auswertungen, und jede einzeln zu rechnen hiesse, in jedem Punkt erst das
    /// passende Segment zu suchen. 1024 Stuetzstellen mit linearer Zwischenrechnung
    /// bleiben unter einem Tausendstel Abweichung und damit weit unter dem, was auf
    /// acht Bit sichtbar wird.
    /// </summary>
    public const int TableSize = 1024;

    private float[]? _table;

    public ToneCurve() { }

    public ToneCurve(IEnumerable<CurvePoint> points) => Points = points.ToList();

    /// <summary>
    /// Die Stuetzpunkte. Zwei Punkte auf der Geraden sind die Grundstellung.
    /// </summary>
    public List<CurvePoint> Points { get; set; } = new() { new(0, 0), new(1, 1) };

    /// <summary>True, wenn die Kurve die Gerade ist und nichts zu rechnen waere.</summary>
    [JsonIgnore]
    public bool IsIdentity
    {
        get
        {
            if (Points.Count != 2) return false;
            return Near(Points[0].X, 0) && Near(Points[0].Y, 0) &&
                   Near(Points[1].X, 1) && Near(Points[1].Y, 1);
        }
    }

    private static bool Near(float value, float reference) => MathF.Abs(value - reference) < 0.0005f;

    /// <summary>
    /// Baut die Tabelle. Muss vor <see cref="Evaluate"/> laufen und darf nicht
    /// parallel zu ihm laufen.
    /// </summary>
    public void Prepare()
    {
        if (IsIdentity)
        {
            _table = null;
            return;
        }

        var points = Sanitised();
        var table = new float[TableSize];

        // Tangenten einmal fuer die ganze Kurve, dann die Tabelle abfahren.
        float[] tangents = MonotoneTangents(points);

        int segment = 0;
        for (int i = 0; i < TableSize; i++)
        {
            float x = i / (float)(TableSize - 1);

            while (segment < points.Count - 2 && x > points[segment + 1].X) segment++;

            table[i] = Math.Clamp(Hermite(points, tangents, segment, x), 0f, 1f);
        }

        _table = table;
    }

    /// <summary>
    /// Wertet die Kurve aus. Ohne vorheriges <see cref="Prepare"/> - oder bei der
    /// Geraden - wird der Wert unveraendert zurueckgegeben.
    /// </summary>
    public float Evaluate(float x)
    {
        var table = _table;
        if (table is null) return x;

        if (!(x > 0f)) return table[0];               // faengt auch NaN ab
        if (x >= 1f) return table[TableSize - 1];

        float position = x * (TableSize - 1);
        int index = (int)position;
        float fraction = position - index;

        return table[index] + (table[index + 1] - table[index]) * fraction;
    }

    /// <summary>
    /// Bringt die Stuetzpunkte in eine Form, mit der gerechnet werden kann: sortiert,
    /// im Bereich, ohne zwei Punkte auf derselben Stelle.
    ///
    /// Das ist keine Vorsichtsmassnahme gegen kaputte Dateien allein - beim Ziehen
    /// eines Punktes ueber seinen Nachbarn hinweg entsteht genau diese Lage, und
    /// zwei Punkte mit demselben X ergaeben eine Division durch null.
    /// </summary>
    private List<CurvePoint> Sanitised()
    {
        var ordered = Points
            .Select(p => new CurvePoint(Math.Clamp(p.X, 0f, 1f), Math.Clamp(p.Y, 0f, 1f)))
            .OrderBy(p => p.X)
            .ToList();

        var result = new List<CurvePoint>(ordered.Count);
        foreach (var point in ordered)
        {
            if (result.Count > 0 && point.X - result[^1].X < 1e-4f) continue;
            result.Add(point);
        }

        // Unter zwei Punkten gibt es kein Segment; die Gerade ist dann die
        // ehrlichste Antwort.
        if (result.Count == 0) return new List<CurvePoint> { new(0, 0), new(1, 1) };
        if (result.Count == 1) return new List<CurvePoint> { new(0, result[0].Y), new(1, result[0].Y) };

        return result;
    }

    /// <summary>
    /// Die Tangenten nach Fritsch-Carlson. Der zweite Teil - das Beschneiden - ist
    /// der Unterschied zur gewoehnlichen Spline und verhindert das Ueberschwingen.
    /// </summary>
    private static float[] MonotoneTangents(List<CurvePoint> points)
    {
        int n = points.Count;
        var secants = new float[n - 1];
        var tangents = new float[n];

        for (int i = 0; i < n - 1; i++)
        {
            float dx = points[i + 1].X - points[i].X;
            secants[i] = dx > 0 ? (points[i + 1].Y - points[i].Y) / dx : 0f;
        }

        tangents[0] = secants[0];
        tangents[n - 1] = secants[n - 2];
        for (int i = 1; i < n - 1; i++) tangents[i] = (secants[i - 1] + secants[i]) * 0.5f;

        for (int i = 0; i < n - 1; i++)
        {
            if (MathF.Abs(secants[i]) < 1e-9f)
            {
                // Ein waagerechtes Stueck: beide Tangenten muessen null sein, sonst
                // beult die Kurve dort aus.
                tangents[i] = 0f;
                tangents[i + 1] = 0f;
                continue;
            }

            float a = tangents[i] / secants[i];
            float b = tangents[i + 1] / secants[i];

            // Ausserhalb des Kreises mit Radius 3 verliert die Kurve die Monotonie.
            // Auf ihn zurueckzuziehen ist die Korrektur, die das Verfahren ausmacht.
            float sum = a * a + b * b;
            if (sum > 9f)
            {
                float t = 3f / MathF.Sqrt(sum);
                tangents[i] = t * a * secants[i];
                tangents[i + 1] = t * b * secants[i];
            }
        }

        return tangents;
    }

    private static float Hermite(List<CurvePoint> points, float[] tangents, int segment, float x)
    {
        var left = points[segment];
        var right = points[segment + 1];

        float h = right.X - left.X;
        if (h <= 0) return left.Y;

        // Ausserhalb der Stuetzpunkte wird gehalten, nicht fortgesetzt: eine
        // verlaengerte Kurve schoesse ueber 0 und 1 hinaus.
        if (x <= left.X) return left.Y;
        if (x >= right.X && segment == points.Count - 2) return right.Y;

        float t = (x - left.X) / h;
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2 * t3 - 3 * t2 + 1;
        float h10 = t3 - 2 * t2 + t;
        float h01 = -2 * t3 + 3 * t2;
        float h11 = t3 - t2;

        return h00 * left.Y + h10 * h * tangents[segment] +
               h01 * right.Y + h11 * h * tangents[segment + 1];
    }

    public ToneCurve Clone() => new(Points.ToList());
}
