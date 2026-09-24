using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Werkzeuge der Farbkorrektur. Zuerst die Gradationskurve, weil sie das
/// wichtigste einzelne Werkzeug ist und weil an ihr eine Entscheidung haengt, die
/// man dem Ergebnis ansieht: die monotone Interpolation.
/// </summary>
public static class GradingInvariants
{
    public static void Run()
    {
        Identity();
        Monotone();
        ControlPoints();
        Malformed();
        Channels();
        Stack();
        Persistence();
        InThePipeline();
    }

    private static void Identity()
    {
        Check.Group("Kurve in Grundstellung");

        var curve = new ToneCurve();
        Check.That(curve.IsIdentity, "zwei Punkte auf der Geraden sind die Grundstellung");

        curve.Prepare();

        int wrong = 0;
        for (int i = 0; i <= 100; i++)
        {
            float x = i / 100f;
            if (MathF.Abs(curve.Evaluate(x) - x) > 1e-5f) wrong++;
        }

        Check.That(wrong == 0, "und veraendert nichts", $"{wrong} Abweichungen");

        // Ohne Prepare darf nichts passieren - ein Werkzeug, das vergessen wurde
        // vorzubereiten, soll den Wert durchreichen statt Unsinn zu liefern.
        var unprepared = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.9f), new CurvePoint(1, 1) });
        Check.Near(unprepared.Evaluate(0.5f), 0.5, 1e-5, "ohne Vorbereitung bleibt der Wert unveraendert");

        var tool = new CurvesTool();
        Check.That(tool.IsNeutral, "das Werkzeug meldet sich als neutral");
    }

    /// <summary>
    /// Der Test, fuer den die Interpolation gewaehlt wurde.
    ///
    /// Eine gewoehnliche kubische Spline laeuft durch dieselben Stuetzpunkte,
    /// schwingt zwischen ihnen aber ueber: vor einem steilen Anstieg biegt sie nach
    /// unten aus. Auf einer Tonwertkurve heisst das, dass ein Bereich dunkler wird,
    /// obwohl die Kurve dort angehoben wurde - in einem Verlauf als Umkehrung
    /// sichtbar, und es sieht nach einem Fehler im Bild aus.
    /// </summary>
    private static void Monotone()
    {
        Check.Group("Kurve schwingt nicht ueber");

        // Ein Sprung von 0,05 auf 0,9 in einem Hundertstel - der Fall, an dem eine
        // gewoehnliche Spline sichtbar ausbricht.
        var steep = new ToneCurve(new[]
        {
            new CurvePoint(0, 0),
            new CurvePoint(0.50f, 0.05f),
            new CurvePoint(0.51f, 0.90f),
            new CurvePoint(1, 1),
        });

        steep.Prepare();

        int falling = 0;
        float previous = steep.Evaluate(0);
        float lowest = previous, highest = previous;

        for (int i = 1; i <= 2000; i++)
        {
            float value = steep.Evaluate(i / 2000f);
            if (value < previous - 1e-5f) falling++;
            if (value < lowest) lowest = value;
            if (value > highest) highest = value;
            previous = value;
        }

        Check.That(falling == 0, "eine steigende Kurve faellt nirgends", $"{falling} fallende Stellen");
        Check.That(lowest >= -1e-5f, "und taucht nicht unter Schwarz", $"Tiefstwert {lowest:0.####}");
        Check.That(highest <= 1f + 1e-5f, "und schiesst nicht ueber Weiss", $"Hoechstwert {highest:0.####}");

        // Dasselbe fuer eine fallende Kurve - eine Umkehrung ist eine gueltige
        // Einstellung und darf genauso wenig ausbrechen.
        var inverted = new ToneCurve(new[]
        {
            new CurvePoint(0, 1), new CurvePoint(0.5f, 0.95f),
            new CurvePoint(0.51f, 0.1f), new CurvePoint(1, 0),
        });

        inverted.Prepare();

        int rising = 0;
        previous = inverted.Evaluate(0);
        for (int i = 1; i <= 2000; i++)
        {
            float value = inverted.Evaluate(i / 2000f);
            if (value > previous + 1e-5f) rising++;
            previous = value;
        }

        Check.That(rising == 0, "eine fallende Kurve steigt nirgends", $"{rising} steigende Stellen");

        // Ein waagerechtes Stueck darf nicht ausbeulen.
        var plateau = new ToneCurve(new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.3f, 0.5f),
            new CurvePoint(0.7f, 0.5f), new CurvePoint(1, 1),
        });

        plateau.Prepare();

        float worst = 0;
        for (int i = 0; i <= 100; i++)
        {
            float x = 0.3f + (0.4f * i / 100f);
            worst = MathF.Max(worst, MathF.Abs(plateau.Evaluate(x) - 0.5f));
        }

        Check.That(worst < 0.002f, "ein waagerechtes Stueck bleibt waagerecht", $"groesste Ausbeulung {worst:0.####}");
    }

    private static void ControlPoints()
    {
        Check.Group("Kurve trifft ihre Stuetzpunkte");

        var points = new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.25f, 0.15f),
            new CurvePoint(0.75f, 0.85f), new CurvePoint(1, 1),
        };

        var curve = new ToneCurve(points);
        curve.Prepare();

        foreach (var point in points)
            Check.Near(curve.Evaluate(point.X), point.Y, 0.003, $"Stuetzpunkt bei {point.X:0.##}");

        // Die klassische S-Kurve: Schatten runter, Lichter hoch, Mitte unberuehrt.
        var s = new ToneCurve(new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.25f, 0.18f),
            new CurvePoint(0.5f, 0.5f), new CurvePoint(0.75f, 0.82f), new CurvePoint(1, 1),
        });

        s.Prepare();

        Check.Near(s.Evaluate(0.5f), 0.5, 0.003, "die Mitte bleibt, wo sie ist");
        Check.That(s.Evaluate(0.25f) < 0.25f, "Schatten werden tiefer");
        Check.That(s.Evaluate(0.75f) > 0.75f, "Lichter werden heller");

        // Ausserhalb wird gehalten, nicht fortgesetzt.
        Check.Near(s.Evaluate(-1f), 0.0, 1e-5, "unterhalb bleibt es bei Schwarz");
        Check.Near(s.Evaluate(2f), 1.0, 1e-5, "oberhalb bleibt es bei Weiss");
        Check.Near(s.Evaluate(float.NaN), 0.0, 1e-5, "NaN ergibt Schwarz statt Unsinn");
    }

    /// <summary>
    /// Was beim Ziehen entsteht: ein Punkt wandert ueber seinen Nachbarn, zwei
    /// landen aufeinander, einer laeuft aus dem Feld. Nichts davon darf die Kurve
    /// umwerfen - die Oberflaeche wird das erzeugen, lange bevor eine kaputte Datei es tut.
    /// </summary>
    private static void Malformed()
    {
        Check.Group("Kurve vertraegt unsortierte Punkte");

        var unsorted = new ToneCurve(new[]
        {
            new CurvePoint(1, 1), new CurvePoint(0.5f, 0.7f), new CurvePoint(0, 0),
        });

        unsorted.Prepare();
        Check.Near(unsorted.Evaluate(0.5f), 0.7, 0.01, "unsortierte Punkte werden geordnet");

        // Zwei Punkte auf derselben Stelle: ohne Behandlung eine Division durch null.
        var doubled = new ToneCurve(new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.5f, 0.3f),
            new CurvePoint(0.5f, 0.8f), new CurvePoint(1, 1),
        });

        doubled.Prepare();
        float at = doubled.Evaluate(0.5f);
        Check.That(!float.IsNaN(at) && at >= 0 && at <= 1, "doppelte Stelle ergibt keinen Unsinn", $"{at}");

        var outside = new ToneCurve(new[]
        {
            new CurvePoint(-5f, -2f), new CurvePoint(0.5f, 0.5f), new CurvePoint(9f, 4f),
        });

        outside.Prepare();
        Check.That(outside.Evaluate(0.5f) is >= 0 and <= 1, "Punkte ausserhalb werden hereingeholt");

        var empty = new ToneCurve(Array.Empty<CurvePoint>());
        empty.Prepare();
        Check.Near(empty.Evaluate(0.42f), 0.42, 1e-5, "ohne Punkte bleibt es die Gerade");

        var single = new ToneCurve(new[] { new CurvePoint(0.5f, 0.25f) });
        single.Prepare();
        Check.Near(single.Evaluate(0.8f), 0.25, 0.01, "ein einzelner Punkt ergibt eine waagerechte Linie");
    }

    private static void Channels()
    {
        Check.Group("Kurven je Kanal");

        var tool = new CurvesTool();
        tool.Red = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.75f), new CurvePoint(1, 1) });
        tool.Prepare();

        Check.That(!tool.IsNeutral, "eine gesetzte Kanalkurve macht das Werkzeug wirksam");

        float r = 0.5f, g = 0.5f, b = 0.5f;
        tool.Apply(ref r, ref g, ref b);

        Check.Near(r, 0.75, 0.01, "Rot folgt seiner Kurve");
        Check.Near(g, 0.5, 1e-5, "Gruen bleibt unberuehrt");
        Check.Near(b, 0.5, 1e-5, "Blau bleibt unberuehrt");

        // Die gemeinsame Kurve laeuft zuerst, dann die des Kanals.
        var both = new CurvesTool
        {
            Master = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.25f), new CurvePoint(1, 1) }),
            Red = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.25f, 0.9f), new CurvePoint(1, 1) }),
        };

        both.Prepare();

        r = 0.5f; g = 0.5f; b = 0.5f;
        both.Apply(ref r, ref g, ref b);

        Check.Near(g, 0.25, 0.01, "die gemeinsame Kurve wirkt auf alle Kanaele");
        Check.Near(r, 0.9, 0.02, "und die Kanalkurve danach auf ihr Ergebnis");
    }

    private static void Stack()
    {
        Check.Group("Werkzeugstapel");

        var stack = new GradingStack();
        Check.That(stack.IsNeutral, "ein leerer Stapel ist neutral");

        var neutral = new CurvesTool();
        var active = new CurvesTool
        {
            Master = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.8f), new CurvePoint(1, 1) }),
        };

        stack.Tools.Add(neutral);
        Check.That(stack.IsNeutral, "ein Stapel aus lauter Grundstellungen auch");

        stack.Tools.Add(active);
        Check.That(!stack.IsNeutral, "ein wirksames Werkzeug macht ihn wirksam");

        var prepared = stack.Prepare();

        // Werkzeuge in Grundstellung fallen heraus - bei sieben im Stapel waere es
        // sonst siebenmal Aufwand fuer nichts.
        Check.That(prepared.Display.Length == 1, "nur das wirksame Werkzeug bleibt uebrig",
                   $"{prepared.Display.Length}");
        Check.That(prepared.SceneLinear.Length == 0, "Kurven stehen auf der Anzeigeseite");
        Check.That(!prepared.IsEmpty, "der vorbereitete Stapel ist nicht leer");

        Check.That(PreparedGrading.None.IsEmpty, "der leere Stapel meldet sich als leer");
        Check.That(active.Stage == GradingStage.Display, "das Werkzeug nennt seine Seite");
    }

    /// <summary>
    /// Ein Rezept muss die Konfigurationsdatei ueberstehen. Beim Stapel kommt hinzu,
    /// dass der Typ des Werkzeugs beim Lesen aus der Kennung hervorgehen muss -
    /// eine Liste von Schnittstellen sagt von sich aus nicht, was darin lag.
    /// </summary>
    private static void Persistence()
    {
        Check.Group("Werkzeuge ueberleben das Speichern");

        var stack = new GradingStack();
        stack.Tools.Add(new CurvesTool
        {
            Master = new ToneCurve(new[]
            {
                new CurvePoint(0, 0), new CurvePoint(0.3f, 0.42f), new CurvePoint(1, 1),
            }),
            Blue = new ToneCurve(new[] { new CurvePoint(0, 0.1f), new CurvePoint(1, 1) }),
        });

        string json = JsonSerializer.Serialize(stack);
        var back = JsonSerializer.Deserialize<GradingStack>(json);

        if (back is null || back.Tools.Count != 1)
        {
            Check.That(false, "der Stapel kommt zurueck", json.Length > 200 ? json[..200] : json);
            return;
        }

        Check.That(back.Tools[0] is CurvesTool, "und das Werkzeug mit seinem Typ",
                   back.Tools[0].GetType().Name);

        var curves = (CurvesTool)back.Tools[0];
        Check.That(curves.Master.Points.Count == 3, "samt seinen Stuetzpunkten",
                   $"{curves.Master.Points.Count}");
        Check.Near(curves.Master.Points[1].Y, 0.42, 1e-5, "und deren Werten");
        Check.Near(curves.Blue.Points[0].Y, 0.1, 1e-5, "auch je Kanal");

        // Die Kennung steht im JSON und darf sich nie aendern - sonst liest eine
        // neue Fassung die Rezepte der alten nicht mehr.
        Check.That(json.Contains("\"kind\":\"curves\""), "die Kennung steht im Text");

        // Nach dem Lesen muss vorbereitet werden koennen, ohne dass etwas fehlt.
        curves.Prepare();
        Check.Near(curves.Master.Evaluate(0.3f), 0.42, 0.01, "und die Kurve rechnet wieder");
    }

    /// <summary>Das Werkzeug an seinem Platz: im Durchgang ueber ein Bild.</summary>
    private static void InThePipeline()
    {
        Check.Group("Werkzeuge im Bilddurchgang");

        var view = new StandardViewTransform();

        // Ein mittleres Grau; die Kurve hebt die Anzeigemitte deutlich an.
        float mid = Srgb.Decode(0.5f);
        var frame = new FloatFrame
        {
            Width = 2, Height = 2,
            R = new[] { mid, mid, mid, mid },
            G = new[] { mid, mid, mid, mid },
            B = new[] { mid, mid, mid, mid },
        };

        var stack = new GradingStack();
        stack.Tools.Add(new CurvesTool
        {
            Master = new ToneCurve(new[]
            {
                new CurvePoint(0, 0), new CurvePoint(0.5f, 0.8f), new CurvePoint(1, 1),
            }),
        });

        var prepared = stack.Prepare();

        var withStack = Render(frame, ImageAdjustments.Neutral, view, prepared);
        var without = Render(frame, ImageAdjustments.Neutral, view, PreparedGrading.None);

        Check.Near(without.R, 128, 2, "ohne Stapel bleibt die Mitte in der Mitte");
        Check.Near(withStack.R, 204, 3, "mit Stapel hebt die Kurve sie an");

        // Die Kurve wirkt NACH der Sichtumwandlung. Waere sie davor, traefe sie den
        // linearen Wert 0,214 und das Ergebnis laege voellig woanders.
        Check.That(withStack.R > 180, "und zwar auf der Anzeigeseite", $"{withStack.R}");

        // Der Stapel kommt nach der Grundkorrektur: eine Blendenstufe mehr trifft
        // die Kurve an einer anderen Stelle und muss ein anderes Ergebnis geben.
        var brighter = Render(frame, new ImageAdjustments { Exposure = 1 }, view, prepared);
        Check.That(brighter.R != withStack.R, "die Grundkorrektur wirkt vor dem Stapel",
                   $"{brighter.R} gegen {withStack.R}");

        // Und das Histogramm muss dasselbe sehen wie das Bild.
        var histogram = new Histogram();
        FloatFrameProcessor.Measure(frame, ImageAdjustments.Neutral, view, prepared, histogram);

        int peak = 0, peakAt = 0;
        for (int i = 0; i < 256; i++)
        {
            if (histogram.Red[i] > peak) { peak = histogram.Red[i]; peakAt = i; }
        }

        Check.Near(peakAt, withStack.R, 2, "das Histogramm misst dasselbe wie das Bild zeigt");
    }

    private static (byte R, byte G, byte B) Render(FloatFrame frame, ImageAdjustments adjustments,
                                                   IViewTransform view, PreparedGrading grading)
    {
        int stride = frame.Width * 4;
        var buffer = Marshal.AllocHGlobal(stride * frame.Height);

        try
        {
            FloatFrameProcessor.Apply(frame, adjustments, view, grading, buffer, stride);

            var pixels = new byte[4];
            Marshal.Copy(buffer, pixels, 0, 4);
            return (pixels[2], pixels[1], pixels[0]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
