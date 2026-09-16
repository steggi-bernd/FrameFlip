using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Platzierung und Wasserzeichen: wo eine Ebene liegt, und was ueber allem bleibt.
/// </summary>
public static class LayerPlacementInvariants
{
    public static void Run()
    {
        SameSizeIsOneToOne();
        SmallerIsFittedAndCentred();
        OffsetAndScaleMove();
        CroppingCuts();
        APlacedLayerActsOnlyWhereItLies();
        TheEdgeIsSoft();
        TurningTurnsTheLayer();
        AWatermarkSurvivesTheGrade();
        Persistence();
    }

    /// <summary>
    /// Gleich gross heisst Punkt auf Punkt.
    ///
    /// Die Grundstellung darf nichts tun - sonst zoege jede Ebene beim blossen
    /// Einschalten der Platzierung um einen halben Bildpunkt, und niemand suchte den
    /// Grund dort.
    /// </summary>
    private static void SameSizeIsOneToOne()
    {
        Check.Group("Gleich gross heisst Punkt auf Punkt");

        var place = LayerPlacement.Prepare(new LayerTransform(), 16, 12, 16, 12);

        int wrong = 0, missing = 0;

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (!place.Locate(x, y, out float u, out float v)) { missing++; continue; }

                if (MathF.Abs(u - (x + 0.5f)) > 1e-4f || MathF.Abs(v - (y + 0.5f)) > 1e-4f) wrong++;
            }
        }

        Check.That(missing == 0, "jeder Bildpunkt liegt in der Ebene", $"{missing} daneben");
        Check.That(wrong == 0, "und trifft genau seinen eigenen", $"{wrong} verschoben");
    }

    private static void SmallerIsFittedAndCentred()
    {
        Check.Group("Eine kleinere Ebene wird mittig eingepasst");

        // Ein Logo von 8x6 auf einer Leinwand von 32x24: der Massstab ist vier,
        // und es deckt sie damit ganz.
        var fitted = LayerPlacement.Prepare(new LayerTransform(), 8, 6, 32, 24);

        Check.That(fitted.Locate(0, 0, out _, out _), "eingepasst deckt es die Ecke");
        Check.That(fitted.Locate(31, 23, out _, out _), "und die gegenueberliegende");

        // Ein Logo mit anderem Seitenverhaeltnis laesst Rand - eingepasst und nicht
        // ausgefuellt, damit nichts abgeschnitten wird, was niemand angeschnitten hat.
        var narrow = LayerPlacement.Prepare(new LayerTransform(), 8, 24, 32, 24);

        Check.That(narrow.Locate(16, 12, out _, out _), "ein schmales liegt in der Mitte");
        Check.That(!narrow.Locate(0, 12, out _, out _), "und laesst links Rand");
        Check.That(!narrow.Locate(31, 12, out _, out _), "wie rechts");

        // Mittig heisst mittig: Der Rand ist links so breit wie rechts.
        int left = 0, right = 0;
        for (int x = 0; x < 32; x++)
        {
            if (narrow.Locate(x, 12, out _, out _)) continue;

            if (x < 16) left++;
            else right++;
        }

        Check.That(left == right, "und die Raender sind gleich breit", $"{left} / {right}");
    }

    private static void OffsetAndScaleMove()
    {
        Check.Group("Versatz und Groesse wirken in Anteilen");

        // Eine halbe Bildbreite nach rechts: Was in der Mitte lag, liegt jetzt am
        // rechten Rand.
        var shifted = LayerPlacement.Prepare(new LayerTransform { OffsetX = 0.5f }, 16, 12, 16, 12);

        Check.That(!shifted.Locate(2, 6, out _, out _), "links ist die Ebene weg");
        Check.That(shifted.Locate(14, 6, out _, out _), "rechts liegt sie");

        // Halb so gross: Sie deckt die Mitte und laesst rundum Rand.
        var half = LayerPlacement.Prepare(new LayerTransform { Scale = 0.5f }, 16, 12, 16, 12);

        Check.That(half.Locate(8, 6, out _, out _), "halbiert deckt sie die Mitte");
        Check.That(!half.Locate(1, 1, out _, out _), "und nicht mehr die Ecke");

        // In ANTEILEN: Derselbe Versatz trifft auf einer doppelt so grossen Leinwand
        // dieselbe Stelle. Daran haengt, dass ein Rezept von 1080p auf 4K gilt.
        var small = LayerPlacement.Prepare(new LayerTransform { OffsetX = 0.25f }, 16, 12, 16, 12);
        var large = LayerPlacement.Prepare(new LayerTransform { OffsetX = 0.25f }, 32, 24, 32, 24);

        bool inSmall = small.Locate(12, 6, out _, out _);
        bool inLarge = large.Locate(24, 12, out _, out _);

        Check.That(inSmall == inLarge, "derselbe Anteil trifft bei jeder Groesse dieselbe Stelle");
    }

    private static void CroppingCuts()
    {
        Check.Group("Der Anschnitt schneidet an");

        var cropped = LayerPlacement.Prepare(
            new LayerTransform { CropLeft = 0.5f }, 16, 12, 16, 12);

        Check.That(!cropped.Locate(3, 6, out _, out _), "die linke Haelfte faellt weg");
        Check.That(cropped.Locate(12, 6, out _, out _), "die rechte bleibt");

        var bottom = LayerPlacement.Prepare(
            new LayerTransform { CropBottom = 0.5f }, 16, 12, 16, 12);

        Check.That(bottom.Locate(8, 2, out _, out _), "oben bleibt");
        Check.That(!bottom.Locate(8, 10, out _, out _), "unten faellt weg");

        // Der Anschnitt bezieht sich auf die EIGENE Flaeche - er wandert mit, wenn
        // die Ebene verschoben wird.
        var moved = LayerPlacement.Prepare(
            new LayerTransform { CropLeft = 0.5f, OffsetX = 0.25f }, 16, 12, 16, 12);

        Check.That(moved.Locate(15, 6, out _, out _), "verschoben liegt der Rest weiter rechts");
    }

    /// <summary>
    /// Die Probe im Composer: Eine platzierte Ebene wirkt NUR, wo sie liegt.
    ///
    /// Ausserhalb traegt sie nichts bei - und zwar wirklich nichts, nicht Schwarz. Ein
    /// Wasserzeichen wuerde sonst auf Multiplizieren das halbe Bild ausloeschen.
    /// </summary>
    private static void APlacedLayerActsOnlyWhereItLies()
    {
        Check.Group("Eine platzierte Ebene wirkt nur, wo sie liegt");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Frame(16, 12, 1f),
            ["logo"] = Frame(4, 3, 2f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = "logo",
                    Mode = BlendMode.Add,
                    Place = new LayerTransform { Scale = 0.25f },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt ein Bild heraus");
        if (built is null) return;

        Check.That(built.Width == 16 && built.Height == 12,
                   "die Leinwand bleibt so gross wie der Pass", $"{built.Width}x{built.Height}");

        // In der Mitte liegt das Logo, am Rand nicht.
        int middle = 6 * 16 + 8;
        Check.Near(built.R[middle], 3.0, 1e-4, "in der Mitte kommt es dazu");
        Check.Near(built.R[0], 1.0, 1e-4, "in der Ecke nicht");

        // Auf Multiplizieren darf es ausserhalb erst recht nichts tun - dort waere
        // "nichts beitragen" und "mit Schwarz multiplizieren" der ganze Unterschied.
        stack.Layers[1].Mode = BlendMode.Multiply;
        var multiplied = LayerComposer.Compose(stack, sources)!;

        Check.Near(multiplied.R[0], 1.0, 1e-4, "ausserhalb loescht es nichts aus");
        Check.Near(multiplied.R[middle], 2.0, 1e-4, "und innerhalb multipliziert es");
    }

    /// <summary>
    /// Die Kante ist weich - einen Bildpunkt breit.
    ///
    /// Bei einem gerade liegenden Rechteck faellt eine harte Kante nicht auf: Sie
    /// liegt auf der Punktreihe. Ein gedrehtes hat seine Kanten quer darueber, und
    /// hart entschieden sieht jede der vier Seiten nach Treppe aus - nach
    /// Selbstgebautem, nicht nach einem Bild.
    /// </summary>
    private static void TheEdgeIsSoft()
    {
        Check.Group("Die Kante einer platzierten Ebene ist weich");

        // Ein gedrehtes Rechteck auf einer groesseren Leinwand.
        var place = LayerPlacement.Prepare(
            new LayerTransform { Scale = 0.5f, Rotation = 30f }, 64, 64, 128, 96);

        int inside = 0, outside = 0, edge = 0;

        for (int y = 0; y < 96; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float covered = place.Coverage(x, y, out _, out _);

                if (covered <= 0f) outside++;
                else if (covered >= 1f) inside++;
                else edge++;
            }
        }

        Check.That(inside > 0, "innen deckt sie ganz", $"{inside} Bildpunkte");
        Check.That(outside > 0, "aussen gar nicht", $"{outside} Bildpunkte");
        Check.That(edge > 0, "und dazwischen liegt eine weiche Kante", $"{edge} Bildpunkte");

        // Die Kante ist ein Saum und nicht die halbe Flaeche.
        Check.That(edge < inside / 4, "die aber schmal bleibt",
                   $"{edge} Kante gegen {inside} voll");

        // Ganz ohne Drehung ebenso: Auch eine gerade Kante auf krummem Massstab
        // liegt selten genau auf der Punktreihe.
        var straight = LayerPlacement.Prepare(
            new LayerTransform { Scale = 0.37f }, 64, 64, 128, 96);

        int soft = 0;
        for (int y = 0; y < 96; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float covered = straight.Coverage(x, y, out _, out _);
                if (covered > 0f && covered < 1f) soft++;
            }
        }

        Check.That(soft > 0, "auch ungedreht auf krummem Massstab", $"{soft} Bildpunkte");
    }

    /// <summary>
    /// Gedreht liegt die Ebene woanders - und die Ecken liegen nicht mehr in ihr.
    /// </summary>
    private static void TurningTurnsTheLayer()
    {
        Check.Group("Drehen dreht die Ebene");

        // Eine Ebene in Bildgroesse, um 45 Grad gedreht: Ihre Ecken ragen hinaus,
        // und die Ecken der Leinwand liegen nicht mehr in ihr.
        var turned = LayerPlacement.Prepare(
            new LayerTransform { Rotation = 45f }, 64, 64, 64, 64);

        Check.That(turned.Coverage(32, 32, out _, out _) > 0.99f, "die Mitte bleibt gedeckt");
        Check.That(turned.Coverage(1, 1, out _, out _) <= 0f, "die Ecke oben links nicht mehr");
        Check.That(turned.Coverage(62, 62, out _, out _) <= 0f, "und die unten rechts auch nicht");

        // Ungedreht sind alle vier Ecken drin - sonst pruefte das obige nichts.
        var straight = LayerPlacement.Prepare(new LayerTransform(), 64, 64, 64, 64);

        Check.That(straight.Coverage(1, 1, out _, out _) > 0.99f, "ungedreht ist die Ecke gedeckt");

        // Eine volle Umdrehung ist dasselbe wie keine.
        var round = LayerPlacement.Prepare(new LayerTransform { Rotation = 360f }, 64, 64, 64, 64);
        Check.That(round.Coverage(1, 1, out _, out _) > 0.99f, "nach 360 Grad ist sie wieder da");

        // Und im Composer: Eine gedrehte Ebene deckt die Ecken nicht mehr ab.
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Frame(32, 32, 1f),
            ["auflage"] = Frame(32, 32, 2f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = "auflage",
                    Mode = BlendMode.Add,
                    Place = new LayerTransform { Rotation = 45f },
                },
            },
        };

        var built = LayerComposer.Compose(stack, sources)!;

        Check.Near(built.R[16 * 32 + 16], 3.0, 1e-3, "in der Mitte kommt sie dazu");
        Check.Near(built.R[0], 1.0, 1e-3, "in der Ecke nicht mehr");
    }

    /// <summary>
    /// Die Probe, um die es beim Wasserzeichen geht.
    ///
    /// Eine gewoehnliche Bildebene geht mit dem Bild durch AgX und wird mitkorrigiert.
    /// Ein Wasserzeichen darf das nicht: Es soll in jedem Bild gleich aussehen, egal
    /// was am Bild eingestellt ist. Geprueft wird deshalb nicht, DASS es da ist,
    /// sondern dass es sich nicht aendert, wenn man das Bild um vier Blendenstufen
    /// hochzieht.
    /// </summary>
    private static void AWatermarkSurvivesTheGrade()
    {
        Check.Group("Ein Wasserzeichen ueberlebt die Korrektur");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["bild"] = Frame(16, 12, 0.18f),
            ["zeichen"] = Frame(16, 12, 1f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "bild", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = "zeichen",
                    Mode = BlendMode.Normal,
                    OnTop = true,
                    Name = "Wasserzeichen",
                },
            },
        };

        // Obenauf heisst: NICHT im zusammengesetzten Bild.
        var built = LayerComposer.Compose(stack, sources)!;
        Check.Near(built.R[0], 0.18, 1e-4, "im Stapel steht es nicht");

        var overlays = Overlays.Prepare(stack, sources, 16, 12);
        Check.That(overlays.Length == 1, "es liegt daneben bereit", $"{overlays.Length}");

        // Zweimal zeichnen: einmal ohne Korrektur, einmal vier Blendenstufen heller.
        var plain = Draw(built, ImageAdjustments.Neutral, overlays);
        var lifted = Draw(built, ImageAdjustments.Neutral with { Exposure = 4 }, overlays);

        Check.That(Same(plain, lifted), "und sieht in beiden Faellen gleich aus");

        // Gegenprobe: OHNE Wasserzeichen aendert dieselbe Korrektur das Bild sehr
        // wohl - sonst pruefte das obige nur, dass zweimal dasselbe herauskommt.
        var withoutPlain = Draw(built, ImageAdjustments.Neutral, Overlays.None);
        var withoutLifted = Draw(built, ImageAdjustments.Neutral with { Exposure = 4 }, Overlays.None);

        Check.That(!Same(withoutPlain, withoutLifted), "waehrend das Bild darunter sich aendert");

        // Und es steht wirklich drauf: Ein weisses Zeichen ueber mittlerem Grau
        // muss heller sein als das Bild ohne.
        Check.That(plain[0] > withoutPlain[0] + 20, "das Zeichen ist zu sehen",
                   $"{plain[0]} gegen {withoutPlain[0]}");
    }

    private static void Persistence()
    {
        Check.Group("Platzierung und Wasserzeichen ueberleben das Speichern");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = @"C:\woanders\zeichen.png",
                    OnTop = true,
                    Place = new LayerTransform
                    {
                        OffsetX = 0.35f,
                        OffsetY = -0.4f,
                        Scale = 0.22f,
                        Rotation = -37.5f,
                        CropLeft = 0.1f,
                        CropBottom = 0.2f,
                    },
                },
            },
        };

        var read = JsonSerializer.Deserialize<LayerStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var layer = read.Layers[0];
        Check.That(layer.OnTop, "obenauf bleibt obenauf");
        Check.Near(layer.Place.OffsetX, 0.35, 1e-5, "der Versatz bleibt");
        Check.Near(layer.Place.OffsetY, -0.4, 1e-5, "in beiden Richtungen");
        Check.Near(layer.Place.Scale, 0.22, 1e-5, "die Groesse auch");
        Check.Near(layer.Place.Rotation, -37.5, 1e-4, "und die Drehung");
        Check.Near(layer.Place.CropLeft, 0.1, 1e-5, "und der Anschnitt");
        Check.Near(layer.Place.CropBottom, 0.2, 1e-5, "auf allen Seiten");

        var copy = stack.Clone();
        stack.Layers[0].Place.Scale = 9f;

        Check.Near(copy.Layers[0].Place.Scale, 0.22, 1e-5, "eine Kopie bewegt sich nicht mit");
    }

    // ------------------------------------------------------------------- Handwerk

    /// <summary>Zeichnet wie die Anzeige und gibt die Bytes zurueck.</summary>
    private static byte[] Draw(FloatFrame frame, ImageAdjustments adjustments, OverlayPlan[] overlays)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, adjustments, new StandardViewTransform(),
                                      PreparedGrading.None, buffer, stride, 1, overlays);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static bool Same(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;

        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return false;

        return true;
    }

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
            IsSceneReferred = false,
        };
    }
}
