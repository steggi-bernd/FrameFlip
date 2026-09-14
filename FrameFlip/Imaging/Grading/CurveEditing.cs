namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Die Rechnung hinter dem Kurvenfeld: zwischen Bildschirmpunkten und Kurvenwerten
/// umrechnen, den angefassten Punkt finden, und einen gezogenen Punkt dort halten,
/// wo er hingehoert.
///
/// Getrennt vom Zeichnen, weil sich genau dieser Teil pruefen laesst und weil hier
/// die Fehler sitzen, die man beim Ausprobieren uebersieht - ein Punkt, der beim
/// Ziehen ueber seinen Nachbarn springt, faellt erst auf, wenn er es tut.
/// </summary>
public static class CurveEditing
{
    /// <summary>
    /// Wie weit ein Klick von einem Punkt entfernt sein darf, um ihn zu meinen.
    /// Grosszuegiger als der gezeichnete Punkt, weil man mit der Maus nicht auf
    /// vier Pixel genau zielt.
    /// </summary>
    public const double GrabRadius = 11.0;

    /// <summary>
    /// Mindestabstand zwischen zwei Punkten auf der X-Achse, als Anteil der Breite.
    ///
    /// Zwei Punkte auf derselben Stelle waeren eine Division durch null in der
    /// Interpolation. Die Kurve faengt das zwar ab, aber ein Punkt, der sich nicht
    /// mehr einzeln greifen laesst, ist auch ohne Absturz unbrauchbar.
    /// </summary>
    public const float MinimumGap = 0.008f;

    public static (double X, double Y) ToCanvas(CurvePoint point, double width, double height)
        => (point.X * width, (1.0 - point.Y) * height);

    /// <summary>
    /// Bildschirmpunkt nach Kurvenwert. Y laeuft auf dem Schirm nach unten und in
    /// der Kurve nach oben - die Umkehrung ist der Grund, warum das hier steht und
    /// nicht an drei Stellen im Zeichencode.
    /// </summary>
    public static CurvePoint ToCurve(double x, double y, double width, double height)
    {
        float cx = width > 0 ? (float)Math.Clamp(x / width, 0.0, 1.0) : 0f;
        float cy = height > 0 ? (float)Math.Clamp(1.0 - y / height, 0.0, 1.0) : 0f;

        return new CurvePoint(cx, cy);
    }

    /// <summary>
    /// Welcher Punkt an dieser Stelle liegt, oder -1. Bei mehreren in Reichweite
    /// gewinnt der naechste.
    /// </summary>
    public static int FindPoint(IReadOnlyList<CurvePoint> points, double x, double y,
                                double width, double height, double radius = GrabRadius)
    {
        int found = -1;
        double best = radius * radius;

        for (int i = 0; i < points.Count; i++)
        {
            var (px, py) = ToCanvas(points[i], width, height);
            double dx = px - x;
            double dy = py - y;
            double distance = dx * dx + dy * dy;

            if (distance <= best)
            {
                best = distance;
                found = i;
            }
        }

        return found;
    }

    /// <summary>
    /// Haelt einen gezogenen Punkt in seinen Grenzen.
    ///
    /// Zwei Regeln, beide aus der Bedienung und nicht aus der Mathematik: Der erste
    /// und der letzte Punkt bleiben am Rand und lassen sich nur in der Hoehe
    /// bewegen - sonst zieht man versehentlich den Anfang der Kurve in die Mitte und
    /// der Bereich davor ist undefiniert. Und kein Punkt darf an seinem Nachbarn
    /// vorbei: die Reihenfolge der Stuetzpunkte ist die Kurve, und ein Punkt, der
    /// vorbeizieht, vertauscht sie unter der Hand.
    /// </summary>
    public static CurvePoint Constrain(IReadOnlyList<CurvePoint> points, int index, CurvePoint wanted)
    {
        if (index < 0 || index >= points.Count) return wanted;

        float y = Math.Clamp(wanted.Y, 0f, 1f);

        if (index == 0) return new CurvePoint(points[0].X, y);
        if (index == points.Count - 1) return new CurvePoint(points[^1].X, y);

        float lower = points[index - 1].X + MinimumGap;
        float upper = points[index + 1].X - MinimumGap;

        // Stehen die Nachbarn zu eng, bleibt der Punkt, wo er ist - lieber
        // unbeweglich als springend.
        if (lower > upper) return new CurvePoint(points[index].X, y);

        return new CurvePoint(Math.Clamp(wanted.X, lower, upper), y);
    }

    /// <summary>
    /// Fuegt einen Punkt ein und gibt seinen Index zurueck; -1, wenn an dieser
    /// Stelle schon einer steht.
    ///
    /// Eingefuegt wird an der Stelle, die die Sortierung verlangt - die Liste muss
    /// nach X geordnet bleiben, weil die Interpolation sich darauf verlaesst.
    /// </summary>
    public static int Insert(List<CurvePoint> points, CurvePoint point)
    {
        float x = Math.Clamp(point.X, 0f, 1f);
        float y = Math.Clamp(point.Y, 0f, 1f);

        foreach (var existing in points)
        {
            if (MathF.Abs(existing.X - x) < MinimumGap) return -1;
        }

        int at = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].X > x)
            {
                at = i;
                break;
            }
        }

        points.Insert(at, new CurvePoint(x, y));
        return at;
    }

    /// <summary>
    /// Entfernt einen Punkt, wenn er sich entfernen laesst.
    ///
    /// Die beiden aeusseren bleiben: ohne sie haette die Kurve keinen Anfang und
    /// kein Ende, und was dann zwischen 0 und dem ersten verbliebenen Punkt gilt,
    /// muesste man erfinden.
    /// </summary>
    public static bool Remove(List<CurvePoint> points, int index)
    {
        if (index <= 0 || index >= points.Count - 1) return false;

        points.RemoveAt(index);
        return true;
    }
}
