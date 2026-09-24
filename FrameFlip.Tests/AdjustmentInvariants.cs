using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Einstellungsebenen: eine Korrektur mitten im Stapel.
///
/// Sie bringen kein Bild mit, sondern veraendern, was unter ihnen liegt - und mit
/// einer Schnittmaske nur die eine Ebene darunter. Das ist der Griff, um den es von
/// Anfang an ging: "waerme nur den Glanz" ist damit zwei Klicks und nicht ein
/// Knotenbaum.
/// </summary>
public static class AdjustmentInvariants
{
    public static void Run()
    {
        TheBorrowedTransformIsExact();
        NeutralChangesNothing();
        ExposureActsInLinear();
        ADisplayToolComesBackToLight();
        ItChangesWhatIsBelow();
        ClippedItChangesOnlyOneLayer();
        ItCarriesNoLight();
        Persistence();
        TheTwoGridsAgree();
        WhatItCosts();
    }

    /// <summary>
    /// Hin und zurueck muss denselben Wert ergeben.
    ///
    /// Daran haengt die ganze Bauart der Einstellungsebene: Sie sitzt MITTEN im
    /// Stapel, ihr Ergebnis wird darueber weiterverrechnet, und es muss deshalb
    /// wieder lineares Licht sein. AgX kann das nicht - es hat einen Weg und keinen
    /// zurueck. Die geliehene Abbildung kann es, und wenn sie das nicht exakt taete,
    /// wuerde jede Einstellungsebene das Bild ein wenig verschieben, auch wenn an ihr
    /// nichts eingestellt ist.
    /// </summary>
    private static void TheBorrowedTransformIsExact()
    {
        Check.Group("Der geliehene Weg fuehrt genau zurueck");

        Check.Near(Blending.ToDisplay(Blending.MiddleGrey), 0.5, 1e-6,
                   "mittleres Grau liegt auf 0,5");

        float worst = 0f;

        // Ueber zwanzig Blendenstufen, bis weit ueber Weiss.
        foreach (float light in new[] { 0f, 0.001f, 0.02f, 0.18f, 0.5f, 1f, 4f, 20f, 100f })
        {
            float back = Blending.ToLight(Blending.ToDisplay(light));
            worst = MathF.Max(worst, MathF.Abs(back - light) / MathF.Max(0.01f, light));
        }

        Check.That(worst < 0.001f, "und der Rueckweg trifft wieder den Ausgangswert",
                   $"groesste Abweichung {worst * 100:0.###} %");
    }

    private static void NeutralChangesNothing()
    {
        Check.Group("Eine Korrektur in Grundstellung veraendert nichts");

        var grade = LayerGrade.Prepare(ImageAdjustments.Neutral, new GradingStack());
        Check.That(grade.IsNeutral, "sie meldet sich als neutral");

        float r = 0.3f, g = 4.2f, b = 0.07f;
        grade.Apply(ref r, ref g, ref b);

        Check.Near(r, 0.3, 1e-6, "Rot bleibt");
        Check.Near(g, 4.2, 1e-6, "Gruen auch ueber Weiss");
        Check.Near(b, 0.07, 1e-6, "und Blau");
    }

    private static void ExposureActsInLinear()
    {
        Check.Group("Belichtung wirkt am Licht");

        var grade = LayerGrade.Prepare(ImageAdjustments.Neutral with { Exposure = 1.0 },
                                       new GradingStack());

        Check.That(!grade.IsNeutral, "sie hat etwas zu tun");

        float r = 0.3f, g = 4.2f, b = 0.07f;
        grade.Apply(ref r, ref g, ref b);

        // Eine Blendenstufe ist eine Verdopplung - und zwar auch weit ueber Weiss,
        // wo eine Anzeigerechnung laengst beschnitten haette.
        Check.Near(r, 0.6, 1e-5, "eine Blendenstufe verdoppelt");
        Check.Near(g, 8.4, 1e-4, "auch ueber Weiss");
        Check.Near(b, 0.14, 1e-5, "in jedem Kanal");
    }

    /// <summary>
    /// Ein Werkzeug der Anzeigeseite geht hin und kommt zurueck.
    ///
    /// Geprueft mit einer Kurve, die drei Stuetzpunkte auf einer Geraden hat: Sie
    /// gilt nicht als Grundstellung, wird also wirklich gerechnet - und rechnet
    /// mathematisch die Identitaet. Was am Ende herauskommt, misst damit genau den
    /// Weg durch die geliehene Anzeigeseite und zurueck.
    /// </summary>
    private static void ADisplayToolComesBackToLight()
    {
        Check.Group("Ein Anzeigewerkzeug fuehrt zurueck ins Licht");

        var curves = new CurvesTool();
        curves.Master.Points.Clear();
        curves.Master.Points.Add(new CurvePoint(0f, 0f));
        curves.Master.Points.Add(new CurvePoint(0.5f, 0.5f));
        curves.Master.Points.Add(new CurvePoint(1f, 1f));

        Check.That(!curves.IsNeutral, "die Kurve gilt nicht als Grundstellung");

        var stack = new GradingStack { Tools = { curves } };
        var grade = LayerGrade.Prepare(ImageAdjustments.Neutral, stack);

        Check.That(!grade.IsNeutral, "und wird gerechnet");

        float worst = 0f;

        foreach (float light in new[] { 0.01f, 0.18f, 0.5f, 1f, 4f, 20f })
        {
            float r = light, g = light, b = light;
            grade.Apply(ref r, ref g, ref b);

            worst = MathF.Max(worst, MathF.Abs(r - light) / MathF.Max(0.01f, light));
        }

        Check.That(worst < 0.005f, "eine gerade Kurve laesst den Wert stehen",
                   $"groesste Abweichung {worst * 100:0.##} %");

        // Und eine, die etwas tut, tut auch etwas - sonst pruefte das obige nichts.
        curves.Master.Points[1] = new CurvePoint(0.5f, 0.75f);
        var lifted = LayerGrade.Prepare(ImageAdjustments.Neutral, stack);

        float mid = Blending.MiddleGrey, dummyG = mid, dummyB = mid;
        lifted.Apply(ref mid, ref dummyG, ref dummyB);

        Check.That(mid > Blending.MiddleGrey * 1.5f,
                   "eine angehobene Kurve hebt mittleres Grau deutlich", $"{mid:0.###}");
    }

    // --------------------------------------------------------------- im Stapel

    private static void ItChangesWhatIsBelow()
    {
        Check.Group("Eine Einstellungsebene veraendert, was darunter liegt");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["a"] = Frame(2, 2, 1f),
            ["b"] = Frame(2, 2, 2f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "a", Mode = BlendMode.Normal },
                new ImageLayer { Source = "b", Mode = BlendMode.Add },
                Adjustment(exposure: 1.0, clipped: false),
            },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt ein Bild heraus");
        if (built is null) return;

        // Ohne Schnittmaske wirkt sie auf alles darunter: (1 + 2) * 2.
        Check.Near(built.R[0], 6.0, 1e-4, "auf die Summe darunter");

        // Halbe Deckkraft heisst halb dazwischen.
        stack.Layers[2].Opacity = 0.5f;
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 4.5, 1e-4,
                   "und die Deckkraft mischt zum Ergebnis hin");

        // Ausgeblendet gar nicht.
        stack.Layers[2].Opacity = 1f;
        stack.Layers[2].Visible = false;
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 3.0, 1e-4,
                   "ausgeblendet wirkt sie nicht");
    }

    /// <summary>
    /// Die Schnittmaske, und damit der eigentliche Griff: nur die eine Ebene darunter.
    ///
    /// Der Unterschied ist mit Absicht so gewaehlt, dass er sich nicht als Rundung
    /// lesen laesst - 5 gegen 6.
    /// </summary>
    private static void ClippedItChangesOnlyOneLayer()
    {
        Check.Group("Angeschnitten wirkt sie nur auf die Ebene darunter");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(2, 2, 1f),
            ["glanz"] = Frame(2, 2, 2f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer { Source = "glanz", Mode = BlendMode.Add },
                Adjustment(exposure: 1.0, clipped: true),
            },
        };

        // 1 + (2 * 2) = 5. Ohne Schnittmaske waere es (1 + 2) * 2 = 6.
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 5.0, 1e-4,
                   "nur der Glanz wird verdoppelt");

        stack.Layers[2].Clipped = false;
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 6.0, 1e-4,
                   "ohne Schnittmaske alles darunter");

        // Ganz unten kann sie sich an nichts anschneiden - dann wirkt sie auf das
        // Schwarz, auf dem der Stapel beginnt, und das ergibt nichts. Sie darf aber
        // nicht den Rest des Stapels verschlucken.
        var alone = new LayerStack
        {
            Layers =
            {
                Adjustment(exposure: 1.0, clipped: true),
                new ImageLayer { Source = "grund", Mode = BlendMode.Add },
            },
        };

        Check.Near(LayerComposer.Compose(alone, sources)!.R[0], 1.0, 1e-4,
                   "ganz unten bleibt der Stapel trotzdem stehen");
    }

    private static void ItCarriesNoLight()
    {
        Check.Group("Eine Einstellungsebene bringt selbst nichts mit");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.Combined" },
                Adjustment(exposure: 1.0, clipped: false),
            },
        };

        // Sie liest keinen Pass - ihr leerer Quellname darf nicht auf der Leseliste
        // landen und dort das Bild selbst anfordern.
        var needed = stack.NeededSources();
        Check.That(needed.Count == 1, "sie fordert keinen Pass an", string.Join(", ", needed));
        Check.That(needed[0] == "ViewLayer.Combined", "nur der Pass darunter wird gelesen");

        // Ein Stapel aus lauter Korrekturen ist kein Bild - es gibt nichts, worauf
        // sie wirken koennten, und auch keine Bildgroesse.
        var empty = new LayerStack { Layers = { Adjustment(1.0, false), Adjustment(1.0, false) } };
        Check.That(LayerComposer.Compose(empty, new Dictionary<string, FloatFrame>()) is null,
                   "ohne einen einzigen Pass kommt nichts heraus");

        // Und sie deckt nichts ab: Ein durchsichtiger Hintergrund bleibt durchsichtig.
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Transparent(2, 2),
        };

        var over = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                Adjustment(exposure: 1.0, clipped: false),
            },
        };

        var built = LayerComposer.Compose(over, sources)!;
        Check.Near(built.A![0], 0.0, 1e-5, "die Deckung bleibt, wie sie war");
    }

    private static void Persistence()
    {
        Check.Group("Eine Einstellungsebene ueberlebt das Speichern");

        var layer = Adjustment(exposure: -1.5, clipped: true);
        layer.Name = "Kuehler";
        layer.Tools = new GradingStack { Tools = { new WhiteBalanceTool { Kelvin = 8000f, Tint = 12f } } };

        var stack = new LayerStack
        {
            Layers = { new ImageLayer { Source = "ViewLayer.Combined" }, layer },
        };

        var read = JsonSerializer.Deserialize<LayerStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var back = read.Layers[1];
        Check.That(back.Content == LayerContent.Adjustment, "sie ist weiter eine Korrektur");
        Check.That(back.Clipped, "die Schnittmaske bleibt");
        Check.Near(back.Adjustments!.Exposure, -1.5, 1e-5, "die Belichtung auch");

        var balance = back.Tools?.Tools.OfType<WhiteBalanceTool>().FirstOrDefault();
        Check.That(balance is not null, "und das Werkzeug mit seinem Typ");
        Check.Near(balance!.Kelvin, 8000, 1, "samt Wert");

        // Kopieren muss tief sein - sonst zoege ein Reglerzug waehrend des
        // Stapellaufs die laufende Ausgabe mit.
        var copy = stack.Clone();
        layer.Tools.Tools.OfType<WhiteBalanceTool>().First().Kelvin = 3000f;

        var copied = copy.Layers[1].Tools!.Tools.OfType<WhiteBalanceTool>().First();
        Check.Near(copied.Kelvin, 8000, 1, "eine Kopie bewegt sich nicht mit");
    }

    /// <summary>
    /// Der Composer und die Anzeige muessen dasselbe Gitter meinen.
    ///
    /// Beim Ziehen rechnet der Composer nur jeden vierten Bildpunkt, weil die Anzeige
    /// danach auch nur jeden vierten liest. Das geht auf, solange beide dieselben
    /// Stellen meinen - und an genau der Stelle koennen zwei Rechnungen in zwei
    /// Dateien auseinanderlaufen, ohne dass es jemandem auffaellt. Das Bild saehe
    /// dann an einem Rand oder an jeder vierten Spalte falsch aus, und man hielte es
    /// fuer die grobe Vorschau.
    ///
    /// Geprueft wird deshalb nicht das Gitter selbst, sondern was dabei
    /// herauskommt: Grob zusammengesetzt und grob gezeichnet muss dasselbe Bild
    /// ergeben wie voll zusammengesetzt und grob gezeichnet.
    /// </summary>
    private static void TheTwoGridsAgree()
    {
        Check.Group("Composer und Anzeige meinen dasselbe Gitter");

        // Ungerade Masse mit Absicht: Ein Bild, dessen Breite durch die Schrittweite
        // teilbar ist, verzeiht einen Fehler am Rand.
        const int width = 61, height = 37, step = 4;

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Noise(width, height),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                Adjustment(exposure: 0.8, clipped: false),
            },
        };

        // Einmal voll zusammensetzen, dann grob zeichnen.
        var full = LayerComposer.Compose(stack, sources)!;
        byte[] fromFull = Draw(full, step);

        // Und einmal in einen Puffer, der mit Unsinn vorbelegt ist, nur das Gitter
        // zusammensetzen. Was der Composer auslaesst, steht dann auf Unsinn - und
        // faellt sofort auf, wenn die Anzeige es doch liest.
        var scratch = Noise(width, height);
        Array.Fill(scratch.R, -7f);
        Array.Fill(scratch.G, -7f);
        Array.Fill(scratch.B, -7f);

        var coarse = LayerComposer.Compose(stack, sources, scratch, step)!;
        byte[] fromGrid = Draw(coarse, step);

        int different = 0;
        for (int i = 0; i < fromFull.Length; i++)
            if (fromFull[i] != fromGrid[i]) different++;

        Check.That(different == 0, "beide ergeben dasselbe Bild",
                   $"{different} von {fromFull.Length} Bytes weichen ab");

        // Gegenprobe: Das Gitter darf NICHT alles gerechnet haben - sonst pruefte
        // das obige nur, dass zweimal dasselbe herauskommt.
        int untouched = coarse.R.Count(v => v == -7f);
        Check.That(untouched > 0, "und dazwischen wurde wirklich nichts gerechnet",
                   $"{untouched} von {coarse.PixelCount} Bildpunkten ausgelassen");
    }

    /// <summary>Zeichnet wie die Anzeige - mit derselben Schrittweite.</summary>
    private static byte[] Draw(FloatFrame frame, int step)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        unsafe
        {
            fixed (byte* target = pixels)
            {
                FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral,
                                          new StandardViewTransform(), PreparedGrading.None,
                                          (IntPtr)target, stride, step);
            }
        }

        return pixels;
    }

    /// <summary>Ein Bild mit wechselnden Werten - gleichmaessiges verzieht jeden Fehler.</summary>
    private static FloatFrame Noise(int width, int height)
    {
        int count = width * height;
        var r = new float[count];
        var g = new float[count];
        var b = new float[count];

        var random = new Random(7);
        for (int i = 0; i < count; i++)
        {
            r[i] = (float)random.NextDouble() * 2f;
            g[i] = (float)random.NextDouble() * 2f;
            b[i] = (float)random.NextDouble() * 2f;
        }

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = r,
            G = g,
            B = b,
            A = Enumerable.Repeat(1f, count).ToArray(),
        };
    }

    /// <summary>
    /// Was eine Einstellungsebene kostet.
    ///
    /// Sie wird bei JEDEM Reglerzug neu gerechnet - der Stapel liegt vor der
    /// Sichtumwandlung, und was sich dort aendert, aendert das Bild. Waere das teuer,
    /// waere die ganze Bauart falsch, und man merkte es erst beim Bedienen.
    ///
    /// Die Schranke steht bewusst grosszuegig: Gemessen werden soll eine
    /// Groessenordnung, nicht die Tagesform der Maschine. Ein Zehnfaches faellt
    /// auf, drei Millisekunden hin oder her nicht.
    /// </summary>
    private static void WhatItCosts()
    {
        Check.Group("Eine Einstellungsebene kostet wenig");

        const int width = 1920, height = 1080;

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Frame(width, height, 0.4f),
        };

        var curves = new CurvesTool();
        curves.Master.Points.Clear();
        curves.Master.Points.Add(new CurvePoint(0f, 0f));
        curves.Master.Points.Add(new CurvePoint(0.5f, 0.62f));
        curves.Master.Points.Add(new CurvePoint(1f, 1f));

        var plain = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                new ImageLayer { Source = "bild", Mode = BlendMode.Add },
            },
        };

        var graded = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                new ImageLayer { Source = "bild", Mode = BlendMode.Add },
                new ImageLayer
                {
                    Content = LayerContent.Adjustment,
                    Mode = BlendMode.Normal,
                    Name = "Korrektur",
                    Adjustments = ImageAdjustments.Neutral with { Exposure = 0.4, Contrast = 1.2 },
                    Tools = new GradingStack
                    {
                        Tools = { curves, new WhiteBalanceTool { Kelvin = 7200f, Tint = 8f } },
                    },
                },
            },
        };

        // Gemessen wird der Weg, den auch die Seite nimmt: in einen eigenen Frame
        // hinein, der wiederverwendet wird. Jedes Mal neu anzulegen waere bei 1080p
        // ein Drittel der Zeit und bei 4K mehr - und die Seite tut es nicht.
        var buffer = LayerComposer.Compose(plain, sources);
        LayerComposer.Compose(graded, sources, buffer);

        double without = Fastest(() => LayerComposer.Compose(plain, sources, buffer));
        double with = Fastest(() => LayerComposer.Compose(graded, sources, buffer));
        double coarse = Fastest(() => LayerComposer.Compose(graded, sources, buffer, step: 4));

        Console.WriteLine($"         1080p: zwei Passe {without:0.0} ms, " +
                          $"mit Korrektur {with:0.0} ms, beim Ziehen {coarse:0.0} ms");

        Check.Timing(with < 150, "der volle Durchgang bleibt im Rahmen", $"{with:0.0} ms");
        Check.Timing(with > without, "und die Korrektur kostet messbar etwas",
                   $"{with:0.0} gegen {without:0.0} ms");

        // Das ist die Zahl, an der die Bedienbarkeit haengt: Beim Ziehen wird nur
        // das Gitter gerechnet, und das muss deutlich unter einem Bildabstand
        // bleiben, sonst ruckelt jeder Regler.
        Check.Timing(coarse < 20, "beim Ziehen bleibt es bedienbar", $"{coarse:0.0} ms");
        Check.Timing(coarse < with / 4, "das Gitter spart ein Vielfaches",
                   $"{coarse:0.0} gegen {with:0.0} ms");
    }

    private static double Fastest(Action action)
    {
        double best = double.MaxValue;

        // Die schnellste von fuenf: Der Median misst die Maschine mit, die schnellste
        // misst den Weg.
        for (int i = 0; i < 5; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            action();
            watch.Stop();

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    // ------------------------------------------------------------------- Handwerk

    private static ImageLayer Adjustment(double exposure, bool clipped) => new()
    {
        Content = LayerContent.Adjustment,
        Mode = BlendMode.Normal,
        Clipped = clipped,
        Name = "Korrektur",
        Adjustments = ImageAdjustments.Neutral with { Exposure = exposure },
        Tools = new GradingStack(),
    };

    private static FloatFrame Frame(int width, int height, float value)
    {
        int count = width * height;

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = Enumerable.Repeat(value, count).ToArray(),
            G = Enumerable.Repeat(value, count).ToArray(),
            B = Enumerable.Repeat(value, count).ToArray(),
            A = Enumerable.Repeat(1f, count).ToArray(),
        };
    }

    private static FloatFrame Transparent(int width, int height)
    {
        var frame = Frame(width, height, 0.5f);
        Array.Fill(frame.A!, 0f);

        return frame;
    }
}
