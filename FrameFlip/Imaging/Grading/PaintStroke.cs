using System.Text.Json.Serialization;

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

    /// <summary>Rund oder eckig. Fehlt es in einem alten Strich, ist er rund.</summary>
    public BrushShape Shape { get; init; } = BrushShape.Round;

    /// <summary>Breite zu Hoehe der Spitze, ab 1. Rund und 1 ist der alte Pinsel, rund und mehr eine Ellipse.</summary>
    public float Aspect { get; init; } = 1f;

    /// <summary>Der Winkel der Spitze in Grad.</summary>
    public float Angle { get; init; }

    /// <summary>
    /// Der Winkel folgt dem Strich: Zum eingestellten kommt die Richtung des Weges. Ein
    /// flacher Pinsel legt sich dann wie eine Breitfeder in jede Kurve.
    /// </summary>
    public bool Follow { get; init; }

    /// <summary>
    /// Worauf der Strich begrenzt ist - die gepackte Deckung eines Objekts in der Groesse
    /// der Maske, oder null. Sie gehoert zum Strich, damit er ueberall genau so nachspielt,
    /// auch ohne die Datei, aus der sie kam.
    /// </summary>
    public string? Limit { get; init; }

    private byte[]? _limit;
    private bool _limitRead;

    /// <summary>Die Richtung des Weges beim letzten Stueck, in Grad.</summary>
    private float _heading;

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

    /// <summary>
    /// Folgt der Winkel dem Strich, wartet der erste Tupfer, bis der Weg eine Richtung hat
    /// - sonst laege er quer zu allem, was folgt. Kommt keine, setzt <see cref="Finish"/> ihn.
    /// </summary>
    private bool _firstPending;

    /// <summary>Die Spitze eines Tupfers - mit der Richtung des Weges, wenn der Winkel ihr folgt.</summary>
    private BrushTip Tip => new(Shape, Aspect, Follow ? Angle + _heading : Angle);

    /// <summary>Wie weit ein Tupfer hoechstens reicht, in Bildpunkten - fuer das, was ein Zug beruehrt.</summary>
    [JsonIgnore]
    public float Reach => Shape == BrushShape.Round && Aspect <= 1f
        ? Radius
        : Radius * MathF.Sqrt(1f + 1f / (MathF.Max(1f, Aspect) * MathF.Max(1f, Aspect)));

    /// <summary>Der Abstand zweier Tupfer in Bildpunkten.</summary>
    [JsonIgnore]
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

        if (Follow)
        {
            _firstPending = true;
            return PaintBounds.Empty;
        }

        return Dab(mask, x, y);
    }

    /// <summary>
    /// Der Zug ist zu Ende. Hat er sich nie bewegt und wartet der erste Tupfer noch, wird er
    /// jetzt gesetzt - im eingestellten Winkel. Ein Klick malt also auch hier.
    /// </summary>
    public PaintBounds Finish(PaintedMask mask)
    {
        if (!_firstPending) return PaintBounds.Empty;

        _firstPending = false;
        return Dab(mask, _x, _y);
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

        // Die Richtung dieses Stuecks - aus dem Weg, also beim Nachspielen dieselbe.
        _heading = MathF.Atan2(dy, dx) * 180f / MathF.PI;

        // Jetzt hat der Weg eine Richtung - der wartende erste Tupfer kommt in ihr.
        if (_firstPending)
        {
            _firstPending = false;
            bounds = Dab(mask, _x, _y);
        }

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
            Shape = Shape,
            Aspect = Aspect,
            Angle = Angle,
            Follow = Follow,
            Limit = Limit,
        };

        var bounds = PaintBounds.Empty;

        for (int i = 0; i + 1 < Path.Count; i += 2)
            bounds = bounds.Union(i == 0 ? again.Begin(mask, Path[0], Path[1]) : again.To(mask, Path[i], Path[i + 1]));

        bounds = bounds.Union(again.Finish(mask));

        return bounds;
    }

    private PaintBounds Dab(PaintedMask mask, float x, float y)
    {
        if (!_limitRead)
        {
            _limitRead = true;
            _limit = Limit is { Length: > 0 } packed ? PaintedMask.Unpack(packed, mask.Width * mask.Height) : null;
        }

        mask.Stamp(x, y, Radius, Erase ? 0f : 1f, Flow, Hardness, Opacity, Tip, _limit);
        return PaintBounds.Around(x, y, Reach);
    }
}

/// <summary>Die Form einer Pinselspitze.</summary>
public enum BrushShape
{
    Round,
    Square,
}

/// <summary>Eine Pinselspitze: Form, Breite zu Hoehe und Winkel in Grad.</summary>
public readonly record struct BrushTip(BrushShape Shape, float Aspect, float Angle)
{
    public static BrushTip Round { get; } = new(BrushShape.Round, 1f, 0f);

    /// <summary>Der alte, runde Pinsel - er nimmt den alten Rechenweg.</summary>
    public bool IsPlainRound => Shape == BrushShape.Round && Aspect <= 1f;
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
