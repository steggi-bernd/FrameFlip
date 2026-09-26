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
    /// Wie weit zwei gegenueberliegende Ecken einer eckigen Spitze auseinandergezogen sind,
    /// 0 bis 1. Aus dem Quadrat wird ein Karo, das in schmale Spalten kommt. Siehe
    /// <see cref="BrushTip.SquishOf"/>.
    /// </summary>
    public float Squish { get; init; }

    /// <summary>
    /// Mit welcher Rechnung der Strich gemalt wurde. Fehlt es, ist es 0 - die Fassung, mit
    /// der er aufgezeichnet wurde, und so spielt er auch nach. Neue Striche bekommen
    /// <see cref="CurrentVersion"/>.
    ///
    /// 1: Folgt der Winkel dem Strich, wird die Richtung geglaettet. In Fassung 0 kam sie
    /// aus dem letzten Mausschritt, und der ist bei langsamem Malen ein, zwei Bildpunkte
    /// lang - auf 0, 45 oder 90 Grad gerastert. Ein leicht zittriger Strich sprang so
    /// zwischen schraeg, hochkant und quer.
    /// </summary>
    public int Version { get; init; }

    public const int CurrentVersion = 1;

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
    /// Der Anker fuer die Richtung (Fassung 1): ein Punkt, der dem Weg an einer Schnur der
    /// Laenge <see cref="TurnDistance"/> nachgezogen wird. Die Richtung ist die vom Anker zum
    /// Stift - also die des Weges ueber gut einen Radius, nicht die des letzten Mausschritts.
    ///
    /// Die Richtungen der einzelnen Schritte zu mitteln taugt nicht: Ein langsamer Strich
    /// nach unten kommt als Zickzack aus 45 und 135 Grad, und deren Mittel ist nicht
    /// "senkrecht", sondern gar nichts. Der Weg selbst geht senkrecht.
    /// </summary>
    private float _anchorX, _anchorY;

    /// <summary>Ob der Weg schon lang genug fuer eine Richtung war (Fassung 1).</summary>
    private bool _headed;

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
    private BrushTip Tip => new(Shape, Aspect, Follow ? Angle + _heading : Angle, Squish);

    /// <summary>Der Winkel, in dem der naechste Tupfer liegt - fuer den Ring waehrend des Malens.</summary>
    [JsonIgnore]
    public float TipAngle => Tip.Angle;

    /// <summary>Wie weit ein Tupfer hoechstens reicht, in Bildpunkten - fuer das, was ein Zug beruehrt.</summary>
    [JsonIgnore]
    public float Reach => Shape == BrushShape.Round && Aspect <= 1f
        ? Radius
        : Radius * MathF.Sqrt(1f + 1f / (MathF.Max(1f, Aspect) * MathF.Max(1f, Aspect)))
                 * (Shape == BrushShape.Square ? 1f + BrushTip.SquishOf(Squish) : 1f);

    /// <summary>
    /// Die Laenge der Schnur, an der der Anker haengt (Fassung 1) - etwa ein Radius. Eine
    /// Richtung gibt es erst ab der halben Laenge: Ein Mausschritt allein ist zu kurz dafuer.
    /// </summary>
    private float TurnDistance => Math.Clamp(Radius, 6f, 32f);

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
        _anchorX = x;
        _anchorY = y;

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

        if (Follow && Version >= 1)
        {
            Steer(x, y);

            // Der erste Tupfer wartet, bis der Weg lang genug fuer eine Richtung ist. Bis
            // dahin bleibt der Ansatz stehen, und das Stueck bis hierher zaehlt als eines.
            if (_firstPending && !_headed) return bounds;
        }
        else
        {
            // Die Richtung dieses Stuecks - aus dem Weg, also beim Nachspielen dieselbe.
            _heading = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        }

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
    /// Zieht den Anker nach (siehe <see cref="_anchorX"/>) und nimmt die Richtung vom Anker
    /// zum Stift - sobald sie mindestens eine halbe Schnur lang ist. Kuerzer, etwa wenn der
    /// Strich umkehrt und am Anker vorbeikommt, bleibt die alte Richtung stehen. Hin und
    /// zurueck sind fuer jede Spitze dasselbe, denn alle sind punktsymmetrisch.
    /// </summary>
    private void Steer(float x, float y)
    {
        float reach = TurnDistance;
        float ax = x - _anchorX;
        float ay = y - _anchorY;
        float distance = MathF.Sqrt(ax * ax + ay * ay);

        if (distance > reach)
        {
            _anchorX = x - ax / distance * reach;
            _anchorY = y - ay / distance * reach;
        }

        if (distance >= reach * 0.5f)
        {
            _heading = MathF.Atan2(ay, ax) * 180f / MathF.PI;
            _headed = true;
        }
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
            Squish = Squish,
            Follow = Follow,
            Limit = Limit,
            Version = Version,
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

/// <summary>
/// Eine Pinselspitze: Form, Breite zu Hoehe, Winkel in Grad und - nur eckig - wie weit sie
/// zum Karo gezogen ist.
/// </summary>
public readonly record struct BrushTip(BrushShape Shape, float Aspect, float Angle, float Squish = 0f)
{
    public static BrushTip Round { get; } = new(BrushShape.Round, 1f, 0f);

    /// <summary>Der alte, runde Pinsel - er nimmt den alten Rechenweg.</summary>
    public bool IsPlainRound => Shape == BrushShape.Round && Aspect <= 1f;

    /// <summary>
    /// Wie weit die lange Diagonale eines Karos ueber die des Quadrats hinausreicht, aus dem
    /// Regler (0 bis 1). Gezogen wird wie an einem Gelenkrahmen: Die Seiten bleiben gleich
    /// lang, zwei Ecken gehen auseinander, die anderen beiden aufeinander zu. Bei gut 0,41
    /// (Wurzel 2 minus 1) waere die kurze Diagonale null. Der Regler geht bis 0,4 - dann ist
    /// das Karo sieben Mal so lang wie breit.
    /// </summary>
    public static float SquishOf(float slider) => Math.Clamp(slider, 0f, 1f) * 0.4f;

    /// <summary>
    /// Die vier Ecken einer eckigen Spitze mit halber Breite und Hoehe, im Rahmen der Spitze
    /// (vor der Drehung) - fuer den Ring am Zeiger, damit er zeigt, was aufgetragen wird.
    /// </summary>
    public (double X, double Y)[] Corners(double halfW, double halfH)
    {
        double along = 1 + SquishOf(Squish);
        double across = Math.Sqrt(Math.Max(0.0001, 2 - along * along));

        var corners = new (double X, double Y)[4];
        var unit = new (double A, double B)[] { (1, 1), (-1, 1), (-1, -1), (1, -1) };

        for (int i = 0; i < 4; i++)
        {
            // Zerlegt in die beiden Diagonalen, jede fuer sich gezogen.
            double c1 = (unit[i].A + unit[i].B) * 0.5 * along;
            double c2 = (unit[i].A - unit[i].B) * 0.5 * across;
            corners[i] = ((c1 + c2) * halfW, (c1 - c2) * halfH);
        }

        return corners;
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
