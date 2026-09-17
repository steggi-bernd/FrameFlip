using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Der Raum, in dem gemischt wird - und die Mischungen, die dazugekommen sind.
///
/// Der Schalter ist nicht Geschmack, sondern eine Antwort auf eine Frage, die sich
/// nicht umgehen laesst: Ist diese Ebene LICHT oder ein BILD? Licht mischt sich
/// linear, und ein Glanzpass mit Wert 40 ueberlebt nichts anderes. Ein Bild ueber
/// einem Bild dagegen soll sich so verhalten, wie es jeder aus Photoshop kennt.
///
/// Der Unterschied ist bei schwachen Beitraegen gross - Faktor fuenf bis zehn -, und
/// genau das steht hier als Zahl, weil es eine lange Suche gekostet hat, bis es
/// jemandem auffiel.
/// </summary>
public static class BlendSpaceInvariants
{
    public static void Run()
    {
        FaintContributionsDiffer();
        DisplayMatchesPhotoshop();
        LinearKeepsTheOverbrights();
        TheNewModesHaveNeutralValues();
        TheColourModesKeepWhatTheyPromise();
        EverythingStaysInRange();
    }

    /// <summary>
    /// Die Messung, um die es ging: Derselbe Schleier, zweimal gemischt.
    ///
    /// In linearem Licht kommt ein schwacher Beitrag um ein Vielfaches heller heraus,
    /// weil die sRGB-Kurve die Tiefen staucht und die Rueckkodierung sie dehnt. Das
    /// ist der Grund, warum eine freigestellte Ebene mit unsauberem Alphakanal hier
    /// rauscht und in Photoshop nicht.
    /// </summary>
    private static void FaintContributionsDiffer()
    {
        Check.Group("Schwache Beitraege kommen linear viel heller heraus");

        // Ein Bildpunkt mit Wert 200, mit vier von 255 Deckung auf Schwarz.
        float over = Srgb.Decode(200 / 255f);
        float opacity = 4 / 255f;

        Blending.Mix(BlendMode.Normal, opacity, 0f, 0f, 0f, over, over, over,
                     out float lr, out _, out _);

        Blending.MixDisplay(BlendMode.Normal, opacity, 0f, 0f, 0f, over, over, over,
                            out float dr, out _, out _);

        int linear = (int)MathF.Round(Srgb.Encode(lr) * 255f);
        int display = (int)MathF.Round(Srgb.Encode(dr) * 255f);

        Console.WriteLine($"         Muell 200 mit Deckung 4/255: linear {linear}, " +
                          $"im Anzeigeraum {display}");

        Check.That(display <= 4, "im Anzeigeraum bleibt es unter der Sichtbarkeit", $"{display}");
        Check.That(linear > 15, "in linearem Licht nicht", $"{linear}");
        Check.That(linear > display * 4, "der Unterschied ist ein Vielfaches",
                   $"{linear} gegen {display}");
    }

    /// <summary>
    /// Der Anzeigeraum rechnet, was Photoshop rechnet - auf dem Byte.
    ///
    /// Geprueft an der einfachsten Mischung, weil dort jede Abweichung sofort
    /// auffaellt: Normal mit Deckkraft ist eine Strecke zwischen zwei Bytes.
    /// </summary>
    private static void DisplayMatchesPhotoshop()
    {
        Check.Group("Im Anzeigeraum kommt heraus, was Photoshop rechnet");

        foreach (int under in new[] { 0, 40, 128, 220 })
        {
            foreach (int over in new[] { 0, 90, 255 })
            {
                foreach (float opacity in new[] { 0.25f, 0.5f, 1f })
                {
                    float u = Srgb.Decode(under / 255f);
                    float o = Srgb.Decode(over / 255f);

                    Blending.MixDisplay(BlendMode.Normal, opacity, u, u, u, o, o, o,
                                        out float r, out _, out _);

                    // Photoshop rechnet auf den Bytes: a + (b - a) * Deckkraft.
                    double wanted = under + (over - under) * opacity;
                    double got = Srgb.Encode(r) * 255f;

                    Check.That(Math.Abs(got - wanted) < 1.0,
                               $"{under} auf {over} bei {opacity:0.00}",
                               $"{got:0.0} statt {wanted:0.0}");
                }
            }
        }

        // Und Multiplizieren, weil es die Mischung ist, bei der der Entwurf den
        // Unterschied zu Photoshop ausdruecklich eingeraeumt hat.
        float pu = Srgb.Decode(0.5f), po = Srgb.Decode(0.5f);

        Blending.MixDisplay(BlendMode.Multiply, 1f, pu, pu, pu, po, po, po,
                            out float mr, out _, out _);

        Check.Near(Srgb.Encode(mr), 0.25, 0.004, "Multiplizieren trifft Photoshops Wert");
    }

    /// <summary>
    /// Und der lineare Weg behaelt, was ueber Weiss liegt.
    ///
    /// Das ist der Preis des Schalters, und er muss messbar sein: Der Anzeigeraum
    /// beschneidet, lineares Licht nicht. Ein Glanzpass mit Wert vierzig ist genau
    /// der Fall, fuer den die EXR gelesen wird.
    /// </summary>
    private static void LinearKeepsTheOverbrights()
    {
        Check.Group("Lineares Licht behaelt die Ueberhellen");

        Blending.Mix(BlendMode.Add, 1f, 40f, 40f, 40f, 2f, 2f, 2f,
                     out float lr, out _, out _);

        Check.Near(lr, 42.0, 0.01, "linear bleibt die Summe eine Summe");

        Blending.MixDisplay(BlendMode.Add, 1f, 40f, 40f, 40f, 2f, 2f, 2f,
                            out float dr, out _, out _);

        Check.That(dr <= 1.001f, "im Anzeigeraum wird auf Weiss beschnitten", $"{dr:0.###}");
    }

    /// <summary>
    /// Jede neue Mischung hat einen Wert, bei dem sie nichts tut.
    ///
    /// Das ist die schaerfste einzelne Probe auf eine Mischformel: Trifft sie ihren
    /// neutralen Wert nicht, ist irgendwo ein Vorzeichen oder ein Faktor falsch, und
    /// im Bild sieht man es erst bei genauem Hinsehen.
    /// </summary>
    private static void TheNewModesHaveNeutralValues()
    {
        Check.Group("Jede neue Mischung hat ihren neutralen Wert");

        var neutral = new (BlendMode Mode, float Over, string Name)[]
        {
            (BlendMode.ColourDodge, 0f, "Farbig abwedeln bei Schwarz"),
            (BlendMode.ColourBurn, 1f, "Farbig nachbelichten bei Weiss"),
            (BlendMode.LinearBurn, 1f, "Linear nachbelichten bei Weiss"),
            (BlendMode.LinearLight, 0.5f, "Lineares Licht bei Grau"),
            (BlendMode.VividLight, 0.5f, "Strahlendes Licht bei Grau"),
            (BlendMode.PinLight, 0.5f, "Lichtpunkt bei Grau"),
            (BlendMode.Exclusion, 0f, "Ausschluss bei Schwarz"),
            (BlendMode.Subtract, 0f, "Subtrahieren bei Schwarz"),
            (BlendMode.Divide, 1f, "Dividieren bei Weiss"),
        };

        foreach (var (mode, over, name) in neutral)
        {
            foreach (float under in new[] { 0.1f, 0.35f, 0.7f })
            {
                float got = Blending.OnDisplay(mode, under, over);

                Check.Near(got, under, 0.002, $"{name} laesst {under:0.00} stehen");
            }
        }
    }

    /// <summary>
    /// Die vier Farbmischungen halten, was ihr Name sagt.
    ///
    /// Sie lassen sich nicht Kanal fuer Kanal pruefen - ihre Aussage ist eine ueber
    /// das Tripel. Geprueft wird deshalb genau die Aussage: Luminanz nimmt die
    /// Helligkeit der oberen Ebene, Farbe laesst sie unten.
    /// </summary>
    private static void TheColourModesKeepWhatTheyPromise()
    {
        Check.Group("Farbton, Saettigung, Farbe und Luminanz");

        static float Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

        // Unten eine kraeftige Farbe, oben ein anderes Grau.
        float ur = 0.8f, ug = 0.2f, ub = 0.1f;
        float or_ = 0.45f, og = 0.45f, ob = 0.45f;

        Blending.MixDisplay(BlendMode.Luminosity, 1f,
                            Srgb.Decode(ur), Srgb.Decode(ug), Srgb.Decode(ub),
                            Srgb.Decode(or_), Srgb.Decode(og), Srgb.Decode(ob),
                            out float r, out float g, out float b);

        float got = Lum(Srgb.Encode(r), Srgb.Encode(g), Srgb.Encode(b));

        Check.Near(got, Lum(or_, og, ob), 0.01, "Luminanz nimmt die Helligkeit von oben");

        Blending.MixDisplay(BlendMode.Colour, 1f,
                            Srgb.Decode(ur), Srgb.Decode(ug), Srgb.Decode(ub),
                            Srgb.Decode(or_), Srgb.Decode(og), Srgb.Decode(ob),
                            out r, out g, out b);

        got = Lum(Srgb.Encode(r), Srgb.Encode(g), Srgb.Encode(b));

        Check.Near(got, Lum(ur, ug, ub), 0.01, "Farbe laesst die Helligkeit unten");

        // Saettigung auf ein Grau angewandt kann nichts faerben - ein Grau hat keinen
        // Farbton, den man saettigen koennte.
        Blending.MixDisplay(BlendMode.Saturation, 1f,
                            Srgb.Decode(0.5f), Srgb.Decode(0.5f), Srgb.Decode(0.5f),
                            Srgb.Decode(ur), Srgb.Decode(ug), Srgb.Decode(ub),
                            out r, out g, out b);

        Check.That(Math.Abs(Srgb.Encode(r) - Srgb.Encode(g)) < 0.01f &&
                   Math.Abs(Srgb.Encode(g) - Srgb.Encode(b)) < 0.01f,
                   "Saettigung auf einem Grau bleibt grau",
                   $"{Srgb.Encode(r):0.00}/{Srgb.Encode(g):0.00}/{Srgb.Encode(b):0.00}");
    }

    /// <summary>
    /// Keine Mischung darf aus Werten zwischen 0 und 1 etwas machen, das nicht mehr
    /// darstellbar ist.
    ///
    /// Im Anzeigeraum wird am Ende beschnitten; was hier zaehlt, ist, dass unterwegs
    /// nichts unendlich oder unbestimmt wird. Eine Division durch null und ein
    /// Logarithmus von null sind die beiden Stellen, an denen so etwas entsteht.
    /// </summary>
    private static void EverythingStaysInRange()
    {
        Check.Group("Keine Mischung laeuft aus dem Ruder");

        int bad = 0;

        foreach (var (mode, _) in Blending.All)
        {
            for (int a = 0; a <= 10; a++)
            {
                for (int b = 0; b <= 10; b++)
                {
                    Blending.MixOn(mode, 1f, a / 10f, a / 10f, a / 10f,
                                   b / 10f, b / 10f, b / 10f,
                                   out float r, out float g, out float bl);

                    if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(bl) ||
                        r < -0.001f || r > 1.001f) bad++;
                }
            }
        }

        Check.That(bad == 0, "alle Mischungen bleiben zwischen null und eins", $"{bad} Ausreisser");

        // Und in linearem Licht: Nichts darf unendlich werden, auch nicht bei
        // Ueberhellen.
        int wild = 0;

        foreach (var (mode, _) in Blending.All)
        {
            foreach (float u in new[] { 0f, 0.5f, 1f, 8f, 40f })
            {
                foreach (float o in new[] { 0f, 0.5f, 1f, 8f, 40f })
                {
                    Blending.Mix(mode, 1f, u, u, u, o, o, o, out float r, out _, out _);

                    if (!float.IsFinite(r) || r < -0.001f) wild++;
                }
            }
        }

        Check.That(wild == 0, "und in linearem Licht bleibt alles endlich und nicht negativ",
                   $"{wild} Ausreisser");
    }
}
