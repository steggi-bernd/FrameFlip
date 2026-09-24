using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Masken: der Wert je Bildpunkt, der sagt, wie stark eine Ebene dort wirkt.
///
/// Sie greifen an genau einer Stelle an - sie multiplizieren die Deckkraft. Das ist
/// die Entscheidung, an der alles Weitere haengt, und sie ist hier zu pruefen: Eine
/// Maske muss auf jeder Mischung und in jeder Schnittgruppe dasselbe bedeuten, sonst
/// waere sie nicht zu erklaeren.
/// </summary>
public static class MaskInvariants
{
    public static void Run()
    {
        TheBandPassesEverythingAtRest();
        TheBandHasOuterEdges();
        MiddleGreyIsTheMiddle();
        TheGradientPointsWhereItSays();
        AMaskLimitsTheLayer();
        AMaskReadsAPass();
        ADepthPassGetsItsOwnRange();
        AMissingMaskPassDropsTheMask();
        MaskSourcesAreRead();
        Persistence();
    }

    // ------------------------------------------------------------- die Rechnungen

    /// <summary>
    /// Die Grundstellung darf nichts tun.
    ///
    /// Das ist der Grund, warum die Rampen nach AUSSEN laufen. Lagen sie nach innen,
    /// waere schon 0 bis 1 eine Abdunklung an beiden Enden - und niemand suchte den
    /// Grund bei einer Maske, die er auf "alles" gestellt hat.
    /// </summary>
    private static void TheBandPassesEverythingAtRest()
    {
        Check.Group("Ein Bereich von 0 bis 1 laesst alles durch");

        int wrong = 0;

        foreach (float softness in new[] { 0f, 0.1f, 0.3f, 0.5f })
        {
            for (int i = 0; i <= 20; i++)
            {
                float v = i / 20f;
                if (MathF.Abs(Masking.Band(v, 0f, 1f, softness) - 1f) > 1e-5f) wrong++;
            }
        }

        Check.That(wrong == 0, "bei jeder Weichheit und ueber den ganzen Bereich",
                   $"{wrong} Abweichungen");
    }

    private static void TheBandHasOuterEdges()
    {
        Check.Group("Der Bereich hat weiche Kanten nach aussen");

        // "Nur die Lichter": von 0,6 aufwaerts.
        Check.Near(Masking.Band(0.8f, 0.6f, 1f, 0.1f), 1.0, 1e-5, "im Bereich voll");
        Check.Near(Masking.Band(0.6f, 0.6f, 1f, 0.1f), 1.0, 1e-5, "an der Grenze noch voll");
        Check.Near(Masking.Band(0.55f, 0.6f, 1f, 0.1f), 0.5, 1e-4, "eine halbe Weichheit darunter: halb");
        Check.Near(Masking.Band(0.5f, 0.6f, 1f, 0.1f), 0.0, 1e-5, "eine ganze darunter: nichts");
        Check.Near(Masking.Band(0.2f, 0.6f, 1f, 0.1f), 0.0, 1e-5, "weit darunter erst recht nichts");

        // Ohne Weichheit ist es eine Kante.
        Check.Near(Masking.Band(0.6f, 0.6f, 1f, 0f), 1.0, 1e-5, "ohne Weichheit: an der Grenze voll");
        Check.Near(Masking.Band(0.599f, 0.6f, 1f, 0f), 0.0, 1e-5, "und knapp darunter nichts");

        // Ein Band in der Mitte laesst nach oben UND unten nach.
        Check.Near(Masking.Band(0.5f, 0.4f, 0.6f, 0.1f), 1.0, 1e-5, "ein Band in der Mitte traegt dort voll");
        Check.Near(Masking.Band(0.35f, 0.4f, 0.6f, 0.1f), 0.5, 1e-4, "und faellt nach unten ab");
        Check.Near(Masking.Band(0.65f, 0.4f, 0.6f, 0.1f), 0.5, 1e-4, "wie nach oben");

        // Monoton innerhalb einer Rampe - eine Maske, die auf halbem Weg umkehrt,
        // waere unbedienbar.
        int falling = 0;
        float previous = -1f;

        for (int i = 0; i <= 100; i++)
        {
            float v = 0.5f + i / 100f * 0.2f;      // quer durch die untere Rampe
            float f = Masking.Band(v, 0.6f, 1f, 0.1f);

            if (f < previous - 1e-5f) falling++;
            previous = f;
        }

        Check.That(falling == 0, "die Rampe steigt durchgehend", $"{falling} Faelle");
    }

    /// <summary>
    /// Mittleres Grau muss in der Mitte liegen.
    ///
    /// Sonst meint "nur die Lichter" etwas anderes als das Auge meint: In roher
    /// Lichtmenge liegt schon ein gewoehnlicher Bildpunkt bei 0,05, und alles ueber
    /// 0,5 waere fast nichts.
    /// </summary>
    private static void MiddleGreyIsTheMiddle()
    {
        Check.Group("Mittleres Grau landet auf 0,5");

        Check.Near(Masking.Perceptual(Blending.MiddleGrey), 0.5, 1e-5, "genau in der Mitte");
        Check.Near(Masking.Perceptual(0f), 0.0, 1e-6, "Schwarz auf null");
        Check.That(Masking.Perceptual(1000f) < 1f, "und nichts erreicht die Eins");

        // Streng steigend - dieselbe Bedingung wie bei den Mischungen.
        int falling = 0;
        float previous = -1f;

        foreach (float light in new[] { 0f, 0.01f, 0.05f, 0.18f, 0.5f, 1f, 4f, 40f, 400f })
        {
            float v = Masking.Perceptual(light);
            if (v < previous) falling++;
            previous = v;
        }

        Check.That(falling == 0, "und steigt ueber zwanzig Blendenstufen", $"{falling} Faelle");
    }

    private static void TheGradientPointsWhereItSays()
    {
        Check.Group("Der Verlauf laeuft, wohin er sagt");

        // 0 Grad: von links nach rechts. Breite 0 ergibt eine Kante in der Mitte.
        float left = Masking.Gradient(0, 0, 8, 2, 1f, 0f, 0.5f, 0.5f);
        float right = Masking.Gradient(7, 0, 8, 2, 1f, 0f, 0.5f, 0.5f);

        Check.Near(left, 0.0, 1e-5, "links nichts");
        Check.Near(right, 1.0, 1e-5, "rechts alles");

        // 90 Grad: von oben nach unten. In Bildkoordinaten waechst y nach unten, und
        // genau deshalb ist das die Richtung, in der jemand einen Himmel abdunkelt.
        float top = Masking.Gradient(0, 0, 4, 8, 0f, 1f, 0.5f, 0.5f);
        float bottom = Masking.Gradient(0, 7, 4, 8, 0f, 1f, 0.5f, 0.5f);

        Check.Near(top, 0.0, 1e-5, "oben nichts");
        Check.Near(bottom, 1.0, 1e-5, "unten alles");

        // Mit Breite wird daraus ein Uebergang. Bei acht Punkten liegt die Mitte
        // ZWISCHEN x=3 und x=4 - es gibt dort keinen Bildpunkt. Geprueft wird
        // deshalb, was wirklich gilt: die beiden Nachbarn sind symmetrisch um die
        // Haelfte, ihre Summe ist eins.
        float below = Masking.Gradient(3, 0, 8, 2, 1f, 0f, 0f, 1f);
        float above = Masking.Gradient(4, 0, 8, 2, 1f, 0f, 0f, 1f);

        Check.Near(below + above, 1.0, 1e-4, "der Uebergang ist um die Mitte symmetrisch");
        Check.That(below < 0.5f && above > 0.5f, "und die Mitte liegt zwischen den beiden",
                   $"{below:0.###} / {above:0.###}");
    }

    // -------------------------------------------------------------- im Composer

    /// <summary>
    /// Die Probe, um die es geht: Eine maskierte Ebene wirkt nur dort, wo die Maske
    /// sie laesst.
    /// </summary>
    private static void AMaskLimitsTheLayer()
    {
        Check.Group("Eine Maske begrenzt, wo die Ebene wirkt");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(8, 2, 1f),
            ["dazu"] = Frame(8, 2, 1f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "dazu",
                    Mode = BlendMode.Add,
                    Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 0f, Width = 0f },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt ein Bild heraus");
        if (built is null) return;

        Check.Near(built.R[0], 1.0, 1e-4, "links wirkt die obere Ebene nicht");
        Check.Near(built.R[7], 2.0, 1e-4, "rechts voll");

        // Umgekehrt genau andersherum - und nicht "irgendwie anders".
        stack.Layers[1].Mask.Invert = true;
        var flipped = LayerComposer.Compose(stack, sources)!;

        Check.Near(flipped.R[0], 2.0, 1e-4, "umgekehrt wirkt sie links");
        Check.Near(flipped.R[7], 1.0, 1e-4, "und rechts nicht");

        // Eine Maske aendert nichts daran, dass es dieselbe Ebene ist: Auf halber
        // Deckkraft wirkt sie halb so stark, wo die Maske sie laesst.
        stack.Layers[1].Mask.Invert = false;
        stack.Layers[1].Opacity = 0.5f;

        var halved = LayerComposer.Compose(stack, sources)!;
        Check.Near(halved.R[7], 1.5, 1e-4, "Deckkraft und Maske multiplizieren sich");
    }

    /// <summary>
    /// Eine Maske aus einem anderen Pass - Nebel, Verschattung, eine Indexmaske.
    ///
    /// Das ist derselbe Weg, den spaeter die Kryptomatte nimmt: ein zweiter Pass,
    /// gelesen und je Bildpunkt zu einem Faktor gemacht.
    /// </summary>
    private static void AMaskReadsAPass()
    {
        Check.Group("Eine Maske liest aus einem anderen Pass");

        // Der Maskenpass ist links schwarz und rechts weiss.
        var mask = Ramp(4, 1);

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(4, 1, 1f),
            ["dazu"] = Frame(4, 1, 1f),
            ["nebel"] = mask,
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "dazu",
                    Mode = BlendMode.Add,
                    Mask = new LayerMask { Kind = MaskKind.Pass, Source = "nebel" },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        // Der Pass geht in Grundstellung UNVERAENDERT als Faktor ein: 0, 1/3, 2/3, 1.
        //
        // Das ist der Punkt, an dem die erste Fassung falsch war: Sie schickte den
        // Pass durch dasselbe Bereichsfenster wie eine Helligkeitsmaske, und dort
        // liegt jeder Wert zwischen 0 und 1 drin. Die Maske tat also nichts - man
        // konnte sie einschalten, und das Bild blieb, wie es war.
        for (int x = 0; x < 4; x++)
        {
            Check.Near(built.R[x], 1.0 + x / 3.0, 1e-4,
                       $"an Stelle {x} traegt der Pass seinen eigenen Wert bei");
        }

        // Schwarz- und Weisspunkt ziehen die Maske an: Ab 2/3 aufwaerts alles, davor
        // nichts.
        stack.Layers[1].Mask.Low = 2f / 3f;
        stack.Layers[1].Mask.High = 2f / 3f;

        var tightened = LayerComposer.Compose(stack, sources)!;

        Check.Near(tightened.R[1], 1.0, 1e-4, "unter dem Schwarzpunkt wirkt nichts");
        Check.Near(tightened.R[2], 2.0, 1e-4, "ab dem Weisspunkt alles");
    }

    /// <summary>
    /// Ein Pass in eigenen Einheiten wird auf seine eigene Spanne bezogen.
    ///
    /// Ein Nebelpass liegt zwischen 0 und 1 und IST die Maske - sein Wert geht
    /// unveraendert ein, und das bleibt so. Ein Tiefenpass steht in Metern; ohne
    /// Spanne waere alles ueber einem Meter voll gedeckt, also praktisch das ganze
    /// Bild, und der Regler taete nichts.
    ///
    /// Der Hintergrund zaehlt dabei NICHT mit: Blender schreibt dort eine sehr
    /// grosse Zahl, und die ist keine Entfernung, sondern "hier steht nichts". Wer
    /// sie mitzaehlt, hat eine Spanne von zehn Milliarden, und alles Sichtbare liegt
    /// in ihrem ersten Milliardstel.
    /// </summary>
    private static void ADepthPassGetsItsOwnRange()
    {
        Check.Group("Ein Pass in eigenen Einheiten bekommt seine Spanne");

        // Eine Tiefe von 2 bis 10 Metern, und dahinter der leere Hintergrund.
        var depth = Ramp(8, 1);
        for (int i = 0; i < depth.PixelCount; i++)
        {
            float metres = 2f + depth.R[i] * 8f;
            depth.R[i] = depth.G[i] = depth.B[i] = metres;
        }

        depth.R[7] = depth.G[7] = depth.B[7] = 1e10f;      // nichts getroffen

        var (low, high) = depth.MaskRange;
        Check.Near(low, 2.0, 0.01, "die Spanne faengt beim naechsten Punkt an");
        Check.That(high < 100f, "und hoert vor dem Hintergrund auf", $"{high:0.#}");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(8, 1, 1f),
            ["dazu"] = Frame(8, 1, 1f),
            ["tiefe"] = depth,
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "dazu",
                    Mode = BlendMode.Add,
                    Mask = new LayerMask { Kind = MaskKind.Pass, Source = "tiefe" },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        // Nah wirkt die Ebene nicht, fern voll - und dazwischen steigt es an.
        Check.Near(built.R[0], 1.0, 1e-3, "am naechsten Punkt wirkt sie nicht");
        Check.That(built.R[6] > 1.8f, "am fernsten fast ganz", $"{built.R[6]:0.###}");
        Check.That(built.R[3] > built.R[1] && built.R[5] > built.R[3],
                   "und dazwischen steigt es an");

        // Umgekehrt herum: "nur der Vordergrund".
        stack.Layers[1].Mask.Invert = true;
        var near = LayerComposer.Compose(stack, sources)!;

        Check.That(near.R[0] > near.R[6], "umgekehrt wirkt sie vorn statt hinten",
                   $"{near.R[0]:0.##} gegen {near.R[6]:0.##}");

        // Und die Gegenprobe: Ein Pass, der schon zwischen 0 und 1 liegt, wird NICHT
        // gestreckt - sonst aenderte sich das Verhalten jeder Nebelmaske.
        var mist = Ramp(8, 1);
        for (int i = 0; i < mist.PixelCount; i++)
        {
            // Nur die halbe Spanne: 0 bis 0,5. Gestreckt kaeme am Ende 1 heraus.
            mist.R[i] = mist.G[i] = mist.B[i] = mist.R[i] * 0.5f;
        }

        sources["tiefe"] = mist;
        stack.Layers[1].Mask.Invert = false;

        var unchanged = LayerComposer.Compose(stack, sources)!;
        Check.Near(unchanged.R[7], 1.5, 1e-3, "ein Anteilspass geht unveraendert ein");
    }

    /// <summary>
    /// Fehlt der Maskenpass, faellt die MASKE weg - nicht die Ebene.
    ///
    /// Rezepte wandern zwischen Dateien, und eine Ebene verschwinden zu lassen, weil
    /// ihre Maske nicht gelesen werden konnte, waere die falsche Antwort: Man saehe
    /// ein Bild, in dem etwas fehlt, und suchte den Grund bei der Ebene.
    /// </summary>
    private static void AMissingMaskPassDropsTheMask()
    {
        Check.Group("Ein fehlender Maskenpass nimmt nur die Maske");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(2, 2, 1f),
            ["dazu"] = Frame(2, 2, 1f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "dazu",
                    Mode = BlendMode.Add,
                    Mask = new LayerMask { Kind = MaskKind.Pass, Source = "gibt es nicht" },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;
        Check.Near(built.R[0], 2.0, 1e-4, "die Ebene wirkt weiter, nur ohne Maske");

        // Auch ein Pass in der falschen Groesse zaehlt als fehlend - ihn zu
        // strecken waere Skalieren, und das ist eine andere Aufgabe.
        sources["klein"] = Frame(1, 1, 0f);
        stack.Layers[1].Mask.Source = "klein";

        var mismatched = LayerComposer.Compose(stack, sources)!;
        Check.Near(mismatched.R[0], 2.0, 1e-4, "ein Pass in falscher Groesse ebenso");
    }

    /// <summary>
    /// Der Pass einer Maske muss mitgelesen werden.
    ///
    /// Ihn zu vergessen ergaebe eine Maske, die still nichts tut - und das Bild saehe
    /// aus, als waere sie falsch eingestellt.
    /// </summary>
    private static void MaskSourcesAreRead()
    {
        Check.Group("Der Pass einer Maske steht auf der Leseliste");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.Combined" },
                new ImageLayer
                {
                    Source = "ViewLayer.GlossDir",
                    Mask = new LayerMask { Kind = MaskKind.Pass, Source = "ViewLayer.Mist" },
                },
            },
        };

        var needed = stack.NeededSources();

        Check.That(needed.Contains("ViewLayer.Mist"), "der Maskenpass ist dabei",
                   string.Join(", ", needed));
        Check.That(needed.Count == 3, "und keiner doppelt", $"{needed.Count}");

        // Eine Maske ohne eigenen Pass bringt keinen auf die Liste.
        stack.Layers[1].Mask = new LayerMask { Kind = MaskKind.Luminance };
        Check.That(stack.NeededSources().Count == 2, "eine Helligkeitsmaske braucht keinen");

        // Eine unsichtbare Ebene auch nicht.
        stack.Layers[1].Mask = new LayerMask { Kind = MaskKind.Pass, Source = "ViewLayer.Mist" };
        stack.Layers[1].Visible = false;

        Check.That(!stack.NeededSources().Contains("ViewLayer.Mist"),
                   "und eine unsichtbare Ebene liest gar nichts");
    }

    private static void Persistence()
    {
        Check.Group("Die Maske ueberlebt das Speichern");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Source = "ViewLayer.GlossDir",
                    Mask = new LayerMask
                    {
                        Kind = MaskKind.Gradient,
                        Invert = true,
                        Angle = 215f,
                        Centre = 0.3f,
                        Width = 0.8f,
                        Low = 0.2f,
                        High = 0.7f,
                        Softness = 0.25f,
                        Source = "ViewLayer.Mist",
                    },
                },
            },
        };

        var read = JsonSerializer.Deserialize<LayerStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var mask = read.Layers[0].Mask;
        Check.That(mask.Kind == MaskKind.Gradient, "die Art bleibt");
        Check.That(mask.Invert, "die Umkehrung auch");
        Check.Near(mask.Angle, 215, 1e-3, "die Richtung");
        Check.Near(mask.Centre, 0.3, 1e-4, "die Mitte");
        Check.Near(mask.Width, 0.8, 1e-4, "die Breite");
        Check.Near(mask.Low, 0.2, 1e-4, "und der Bereich");
        Check.Near(mask.High, 0.7, 1e-4, "in beiden Grenzen");
        Check.That(mask.Source == "ViewLayer.Mist", "samt Quelle");

        // Kopieren muss tief sein - sonst zoege ein Reglerzug waehrend des
        // Stapellaufs die Maske der laufenden Ausgabe mit.
        var copy = stack.Clone();
        stack.Layers[0].Mask.Angle = 0f;

        Check.Near(copy.Layers[0].Mask.Angle, 215, 1e-3, "eine Kopie bewegt sich nicht mit");
    }

    // ------------------------------------------------------------------- Handwerk

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

    /// <summary>Ein Graustufenbild, das von links nach rechts von 0 auf 1 laeuft.</summary>
    private static FloatFrame Ramp(int width, int height)
    {
        var values = new float[width * height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                values[y * width + x] = width <= 1 ? 0f : x / (float)(width - 1);

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = values,
            G = (float[])values.Clone(),
            B = (float[])values.Clone(),
            A = Enumerable.Repeat(1f, width * height).ToArray(),
        };
    }
}
