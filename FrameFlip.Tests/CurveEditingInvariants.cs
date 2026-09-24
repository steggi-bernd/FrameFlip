using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Rechnung hinter dem Kurvenfeld. Hier sitzen die Fehler, die man beim
/// Ausprobieren uebersieht: ein Punkt, der beim Ziehen an seinem Nachbarn
/// vorbeispringt, faellt erst auf, wenn er es tut - und dann sieht es aus, als
/// haette die Kurve einen Sprung.
/// </summary>
public static class CurveEditingInvariants
{
    public static void Run()
    {
        Coordinates();
        Finding();
        Dragging();
        Inserting();
        Removing();
    }

    private static void Coordinates()
    {
        Check.Group("Kurvenfeld: Umrechnung");

        const double w = 200, h = 100;

        // Y laeuft auf dem Schirm nach unten und in der Kurve nach oben. Die
        // Umkehrung ist der haeufigste Zahlendreher an so einem Feld.
        var (x, y) = CurveEditing.ToCanvas(new CurvePoint(0, 0), w, h);
        Check.That(x == 0 && y == h, "Schwarz liegt unten links", $"{x}/{y}");

        (x, y) = CurveEditing.ToCanvas(new CurvePoint(1, 1), w, h);
        Check.That(x == w && y == 0, "Weiss oben rechts", $"{x}/{y}");

        (x, y) = CurveEditing.ToCanvas(new CurvePoint(0.5f, 0.5f), w, h);
        Check.That(x == w / 2 && y == h / 2, "die Mitte in der Mitte", $"{x}/{y}");

        // Und zurueck.
        var point = CurveEditing.ToCurve(0, h, w, h);
        Check.That(point.X == 0f && point.Y == 0f, "unten links ist Schwarz", $"{point}");

        point = CurveEditing.ToCurve(w, 0, w, h);
        Check.That(point.X == 1f && point.Y == 1f, "oben rechts ist Weiss", $"{point}");

        // Ausserhalb des Feldes wird eingefangen - beim Ziehen laeuft die Maus
        // regelmaessig darueber hinaus.
        point = CurveEditing.ToCurve(-50, -50, w, h);
        Check.That(point.X == 0f && point.Y == 1f, "links oberhalb wird eingefangen", $"{point}");

        point = CurveEditing.ToCurve(w + 80, h + 80, w, h);
        Check.That(point.X == 1f && point.Y == 0f, "rechts unterhalb ebenso", $"{point}");

        // Ein Feld ohne Ausdehnung darf nicht durch null teilen - das passiert beim
        // ersten Zeichnen, bevor das Layout steht.
        point = CurveEditing.ToCurve(10, 10, 0, 0);
        Check.That(!float.IsNaN(point.X) && !float.IsNaN(point.Y), "ein Feld ohne Groesse ergibt keinen Unsinn");
    }

    private static void Finding()
    {
        Check.Group("Kurvenfeld: Punkt greifen");

        const double w = 200, h = 100;
        var points = new List<CurvePoint> { new(0, 0), new(0.5f, 0.5f), new(1, 1) };

        Check.That(CurveEditing.FindPoint(points, 100, 50, w, h) == 1, "der Punkt in der Mitte wird getroffen");
        Check.That(CurveEditing.FindPoint(points, 0, 100, w, h) == 0, "der erste am unteren Rand");
        Check.That(CurveEditing.FindPoint(points, 200, 0, w, h) == 2, "der letzte am oberen");

        // Knapp daneben zaehlt noch - mit der Maus zielt niemand auf vier Pixel genau.
        Check.That(CurveEditing.FindPoint(points, 106, 56, w, h) == 1, "knapp daneben zaehlt noch");

        // Weit daneben nicht.
        Check.That(CurveEditing.FindPoint(points, 100, 20, w, h) < 0, "weit daneben trifft nichts");

        // Bei zwei Punkten in Reichweite gewinnt der naehere.
        var crowded = new List<CurvePoint> { new(0, 0), new(0.48f, 0.5f), new(0.52f, 0.5f), new(1, 1) };
        int hit = CurveEditing.FindPoint(crowded, 0.52 * w, 50, w, h);
        Check.That(hit == 2, "bei mehreren in Reichweite gewinnt der naechste", $"{hit}");
    }

    /// <summary>
    /// Die Regeln beim Ziehen, und beide kommen aus der Bedienung: Die aeusseren
    /// Punkte bleiben am Rand, und kein Punkt darf an seinem Nachbarn vorbei.
    /// </summary>
    private static void Dragging()
    {
        Check.Group("Kurvenfeld: Punkt ziehen");

        var points = new List<CurvePoint> { new(0, 0), new(0.5f, 0.5f), new(1, 1) };

        // Der erste Punkt laesst sich nur in der Hoehe bewegen. Zoege man ihn in die
        // Mitte, waere der Bereich davor undefiniert.
        var held = CurveEditing.Constrain(points, 0, new CurvePoint(0.4f, 0.3f));
        Check.That(held.X == 0f, "der erste Punkt bleibt am linken Rand", $"{held}");
        Check.Near(held.Y, 0.3, 1e-5, "laesst sich aber anheben");

        held = CurveEditing.Constrain(points, 2, new CurvePoint(0.6f, 0.8f));
        Check.That(held.X == 1f, "der letzte bleibt am rechten Rand", $"{held}");
        Check.Near(held.Y, 0.8, 1e-5, "und laesst sich absenken");

        // Ein mittlerer Punkt darf nicht an den Nachbarn vorbei - die Reihenfolge
        // der Stuetzpunkte IST die Kurve.
        held = CurveEditing.Constrain(points, 1, new CurvePoint(-0.5f, 0.5f));
        Check.That(held.X > 0f, "nach links wird vor dem Nachbarn gestoppt", $"{held.X:0.####}");
        Check.That(held.X >= CurveEditing.MinimumGap * 0.99f, "und zwar mit Abstand");

        held = CurveEditing.Constrain(points, 1, new CurvePoint(1.5f, 0.5f));
        Check.That(held.X < 1f, "nach rechts ebenso", $"{held.X:0.####}");

        // In der Hoehe darf er ueberall hin.
        held = CurveEditing.Constrain(points, 1, new CurvePoint(0.5f, 5f));
        Check.That(held.Y == 1f, "nach oben wird bei Weiss eingefangen");

        held = CurveEditing.Constrain(points, 1, new CurvePoint(0.5f, -5f));
        Check.That(held.Y == 0f, "nach unten bei Schwarz");

        // Stehen die Nachbarn zu eng, bleibt der Punkt stehen statt zu springen.
        var tight = new List<CurvePoint> { new(0, 0), new(0.500f, 0.4f), new(0.502f, 0.5f), new(0.504f, 0.6f), new(1, 1) };
        held = CurveEditing.Constrain(tight, 2, new CurvePoint(0.9f, 0.5f));
        Check.Near(held.X, tight[2].X, 1e-5, "zwischen engen Nachbarn bleibt er stehen");

        // Ein Index ausserhalb darf nicht werfen.
        held = CurveEditing.Constrain(points, 99, new CurvePoint(0.3f, 0.3f));
        Check.That(!float.IsNaN(held.X), "ein Index ausserhalb ergibt keinen Unsinn");
    }

    private static void Inserting()
    {
        Check.Group("Kurvenfeld: Punkt einfuegen");

        var points = new List<CurvePoint> { new(0, 0), new(1, 1) };

        int at = CurveEditing.Insert(points, new CurvePoint(0.5f, 0.7f));
        Check.That(at == 1, "der neue Punkt landet zwischen den beiden", $"{at}");
        Check.That(points.Count == 3, "und die Liste waechst");

        // Die Liste muss nach X geordnet bleiben - die Interpolation verlaesst sich
        // darauf.
        CurveEditing.Insert(points, new CurvePoint(0.25f, 0.3f));
        CurveEditing.Insert(points, new CurvePoint(0.75f, 0.9f));

        bool ordered = true;
        for (int i = 1; i < points.Count; i++)
            if (points[i].X < points[i - 1].X) ordered = false;

        Check.That(ordered, "die Liste bleibt geordnet",
                   string.Join(" ", points.Select(p => p.X.ToString("0.##"))));

        Check.That(points.Count == 5, "alle Punkte sind da", $"{points.Count}");

        // Auf eine belegte Stelle wird nicht eingefuegt: zwei Punkte, die sich nicht
        // mehr einzeln greifen lassen, sind auch ohne Absturz unbrauchbar.
        int rejected = CurveEditing.Insert(points, new CurvePoint(0.5f, 0.2f));
        Check.That(rejected < 0, "auf eine belegte Stelle wird nicht eingefuegt");
        Check.That(points.Count == 5, "und nichts hinzugefuegt");

        // Auch nicht knapp daneben.
        rejected = CurveEditing.Insert(points, new CurvePoint(0.502f, 0.2f));
        Check.That(rejected < 0, "und auch nicht knapp daneben");

        // Ausserhalb wird eingefangen, nicht abgelehnt.
        var fresh = new List<CurvePoint> { new(0, 0), new(1, 1) };
        CurveEditing.Insert(fresh, new CurvePoint(0.5f, 9f));
        Check.That(fresh[1].Y == 1f, "ein Wert ueber Weiss wird eingefangen", $"{fresh[1].Y}");
    }

    private static void Removing()
    {
        Check.Group("Kurvenfeld: Punkt entfernen");

        var points = new List<CurvePoint> { new(0, 0), new(0.3f, 0.4f), new(0.7f, 0.8f), new(1, 1) };

        Check.That(CurveEditing.Remove(points, 1), "ein mittlerer Punkt laesst sich entfernen");
        Check.That(points.Count == 3, "und ist dann fort");

        // Die aeusseren bleiben: ohne sie haette die Kurve keinen Anfang und kein
        // Ende, und was davor gilt, muesste man erfinden.
        Check.That(!CurveEditing.Remove(points, 0), "der erste bleibt");
        Check.That(!CurveEditing.Remove(points, points.Count - 1), "und der letzte auch");
        Check.That(points.Count == 3, "beide sind noch da");

        Check.That(!CurveEditing.Remove(points, 99), "ein Index ausserhalb tut nichts");
        Check.That(!CurveEditing.Remove(points, -1), "ein negativer ebenso");

        // Nach dem Entfernen muss die Kurve weiter rechnen.
        var curve = new ToneCurve(points);
        curve.Prepare();
        Check.That(!float.IsNaN(curve.Evaluate(0.5f)), "die Kurve rechnet danach weiter");
    }
}
