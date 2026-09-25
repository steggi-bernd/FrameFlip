namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Pinselzug: womit er gemalt wurde und welchen Weg er nahm.
///
/// Die Tupfer entlang des Weges setzt der Zug selbst, im Abstand, den der Pinsel
/// vorgibt - nicht die Maus. Vorher setzte die Anzeige einen Tupfer je Mausmeldung und
/// dazwischen so viele, dass die Luecke unter einem Viertel des Radius blieb. Wie
/// dicht ein Strich wurde, hing damit davon ab, wie oft Windows die Maus meldete:
/// Langsam gezogen trug derselbe Strich mehr auf als schnell.
///
/// Und der Zug ist ein Wert fuer sich. Er laesst sich aufzeichnen und auf einer Maske
/// nachspielen, und das Nachspielen ergibt dasselbe Raster - die Grundlage fuer den
/// Verlauf einer Maske (docs/Projekte-und-Masken.md, Abschnitt 3.5).
/// </summary>
public sealed class PaintStroke
{
    /// <summary>Ein Viertel des Radius - so dicht wie bisher bei zuegigem Strich.</summary>
    public const float DefaultSpacing = 0.25f;

    public const float MinSpacing = 0.05f;

    /// <summary>
    /// Vierfacher Radius. Gemessen wird am Radius, nicht am Durchmesser: Bei 200 %
    /// beruehren sich die Tupfer gerade, darueber liegen Luecken - fuer gepunktete
    /// Striche.
    /// </summary>
    public const float MaxSpacing = 4f;

    /// <summary>Der Radius in Bildpunkten der Leinwand.</summary>
    public float Radius { get; init; } = 40f;

    public float Hardness { get; init; } = 0.5f;

    public float Flow { get; init; } = 0.7f;

    public float Opacity { get; init; } = 1f;

    /// <summary>Der Abstand zweier Tupfer als Anteil des Radius.</summary>
    public float Spacing { get; init; } = DefaultSpacing;

    /// <summary>Wegnehmen statt Auftragen.</summary>
    public bool Erase { get; init; }

    /// <summary>
    /// Der Weg in Bildpunkten, abwechselnd x und y - so, wie er gemeldet wurde, und
    /// nicht die gesetzten Tupfer. Aus ihm und den Einstellungen folgen die Tupfer
    /// eindeutig.
    /// </summary>
    public List<float> Path { get; init; } = new();

    private float _x, _y;

    /// <summary>Der Weg seit dem letzten Tupfer.</summary>
    private float _walked;

    private bool _started;

    /// <summary>Der Abstand zweier Tupfer in Bildpunkten.</summary>
    public float Step => MathF.Max(0.5f, Radius * Math.Clamp(Spacing, MinSpacing, MaxSpacing));

    /// <summary>Setzt an: ein Tupfer am ersten Punkt.</summary>
    public PaintBounds Begin(PaintedMask mask, float x, float y)
    {
        _started = true;
        _x = x;
        _y = y;
        _walked = 0;

        Path.Add(x);
        Path.Add(y);

        return Dab(mask, x, y);
    }

    /// <summary>
    /// Zieht weiter: Tupfer im Abstand <see cref="Step"/> entlang des Weges, gezaehlt
    /// ab dem letzten Tupfer und nicht ab der letzten Mausmeldung. Liegt die Meldung
    /// naeher als ein Abstand, wird nur der Weg gemerkt.
    /// </summary>
    public PaintBounds To(PaintedMask mask, float x, float y)
    {
        if (!_started) return Begin(mask, x, y);

        Path.Add(x);
        Path.Add(y);

        float dx = x - _x;
        float dy = y - _y;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        var bounds = PaintBounds.Empty;
        if (length <= 0f) return bounds;

        float step = Step;
        float at = step - _walked;

        while (at <= length)
        {
            float t = at / length;
            bounds = bounds.Union(Dab(mask, _x + dx * t, _y + dy * t));
            at += step;
        }

        _walked = length - (at - step);
        _x = x;
        _y = y;

        return bounds;
    }

    /// <summary>
    /// Spielt den Zug auf einer Maske nach - mit denselben Einstellungen und demselben
    /// Weg, also mit denselben Tupfern in derselben Reihenfolge.
    /// </summary>
    public PaintBounds Replay(PaintedMask mask)
    {
        var again = new PaintStroke
        {
            Radius = Radius,
            Hardness = Hardness,
            Flow = Flow,
            Opacity = Opacity,
            Spacing = Spacing,
            Erase = Erase,
        };

        var bounds = PaintBounds.Empty;

        for (int i = 0; i + 1 < Path.Count; i += 2)
            bounds = bounds.Union(i == 0 ? again.Begin(mask, Path[0], Path[1]) : again.To(mask, Path[i], Path[i + 1]));

        return bounds;
    }

    private PaintBounds Dab(PaintedMask mask, float x, float y)
    {
        mask.Stroke(x, y, Radius, Erase ? 0f : 1f, Flow, Hardness, Opacity);
        return PaintBounds.Around(x, y, Radius);
    }
}

/// <summary>Was ein Zug beruehrt hat, in Bildpunkten der Leinwand. Leer: nichts.</summary>
public readonly record struct PaintBounds(float X0, float Y0, float X1, float Y1)
{
    public static PaintBounds Empty { get; } = new(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

    public bool IsEmpty => X1 < X0 || Y1 < Y0;

    public static PaintBounds Around(float x, float y, float radius) => new(x - radius, y - radius, x + radius, y + radius);

    public PaintBounds Union(PaintBounds other)
        => other.IsEmpty ? this
            : IsEmpty ? other
            : new(MathF.Min(X0, other.X0), MathF.Min(Y0, other.Y0), MathF.Max(X1, other.X1), MathF.Max(Y1, other.Y1));
}
