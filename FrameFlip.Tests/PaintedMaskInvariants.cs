using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Gemalte Masken - und die Sperre, die entscheidet, fuer wie viele Bilder sie gelten.
///
/// Alle anderen Maskenarten sind ABGELEITET: Sie sagen "wo es hell ist" oder "wo
/// dieses Objekt steht". Diese sagt "genau hier", und das ist die eine Frage, die
/// kein Programm fuer einen beantworten kann.
///
/// Weil sie von Hand kommt, ist sie als einzige STATISCH - und damit fuer eine
/// Animation meistens unbrauchbar, solange niemand sagt, ob sie fuer alle Bilder
/// gelten soll. Genau das tut die Sperre, und sie ist deshalb hier die schaerfste
/// Probe.
/// </summary>
public static class PaintedMaskInvariants
{
    public static void Run()
    {
        AStrokeLandsWhereItWasPut();
        PackingLosesNothing();
        TheLockDecidesHowManyFrames();
        TheComposerSeesIt();
        ToolsWorkOnAMaskLayer();
    }

    private static void AStrokeLandsWhereItWasPut()
    {
        Check.Group("Ein Pinselstrich landet, wo er aufgesetzt wurde");

        var mask = PaintedMask.For(400, 300);

        Check.That(mask.Width == 100 && mask.Height == 75,
                   "die Maske ist ein Viertel so gross wie das Bild",
                   $"{mask.Width}x{mask.Height}");

        Check.Near(mask.At(200, 150), 0, 0.001, "und faengt leer an");

        mask.Stroke(200f, 150f, 40f, 1f, 1f);

        Check.Near(mask.At(200, 150), 1.0, 0.02, "in der Mitte deckt sie danach ganz");

        Check.Near(mask.At(20, 20), 0, 0.02, "in der Ecke nicht");

        // Die Kante muss weich sein - sonst waere sie bei einer vierfach groeberen
        // Maske eine Treppe.
        float edge = mask.At(200 + 30, 150);

        Check.That(edge is > 0.05f and < 0.95f,
                   "und dazwischen laeuft sie weich aus", $"{edge:0.00}");

        // Radieren nimmt wieder weg.
        mask.Stroke(200f, 150f, 40f, 0f, 1f);

        Check.Near(mask.At(200, 150), 0, 0.02, "radiert ist sie wieder offen");
    }

    private static void PackingLosesNothing()
    {
        Check.Group("Gepackt und wieder ausgepackt bleibt dieselbe Maske");

        var mask = PaintedMask.For(800, 600);

        mask.Stroke(300f, 200f, 60f, 1f, 1f);
        mask.Stroke(500f, 400f, 30f, 1f, 0.5f);
        mask.Keep();

        Check.That(mask.Data.Length > 0, "gepackt steht etwas da");

        // Eine fast leere Maske muss winzig werden - sonst traegt jedes Rezept ein
        // halbes Megabyte mit sich herum.
        Check.That(mask.Data.Length < 8000,
                   "und zwar wenig - eine Maske ist fast ueberall gleich",
                   $"{mask.Data.Length} Zeichen");

        var again = new PaintedMask { Width = mask.Width, Height = mask.Height, Data = mask.Data };

        int apart = 0;

        for (int i = 0; i < mask.Cover().Length; i++)
            if (mask.Cover()[i] != again.Cover()[i]) apart++;

        Check.That(apart == 0, "ausgepackt steht Byte fuer Byte dasselbe", $"{apart} Abweichungen");

        // Und eine Maske, deren Groesse nicht zu ihren Daten passt, gibt eine LEERE
        // zurueck statt einer halben. Ein halber Anstrich saehe aus wie ein eigener
        // Fehler.
        var wrong = new PaintedMask { Width = 10, Height = 10, Data = mask.Data };

        Check.That(wrong.Cover().Length == 100 && wrong.Cover().All(b => b == 0),
                   "und eine Maske mit falscher Groesse bleibt leer statt halb");
    }

    /// <summary>
    /// Die Sperre: gesperrt gilt ein Anstrich fuer alle Bilder, entsperrt je Bild
    /// einer.
    ///
    /// Das ist die ganze Aussage des Schalters, und beide Haelften muessen stimmen.
    /// Wer nur die erste prueft, hat einen Schalter, der immer sperrt.
    /// </summary>
    private static void TheLockDecidesHowManyFrames()
    {
        Check.Group("Die Sperre entscheidet, fuer wie viele Bilder der Anstrich gilt");

        var mask = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true };

        mask.PaintOn(1, 400, 300).Stroke(200f, 150f, 40f, 1f, 1f);

        Check.That(mask.PaintFor(300) is not null, "gesperrt gilt er auch bei Bild 300");

        Check.Near(mask.PaintFor(300)!.At(200, 150), 1.0, 0.02, "und zwar derselbe");

        // Entsperrt: je Bild ein eigener.
        var own = new LayerMask { Kind = MaskKind.Painted, PaintLocked = false };

        own.PaintOn(1, 400, 300).Stroke(200f, 150f, 40f, 1f, 1f);

        Check.That(own.PaintFor(1) is not null, "entsperrt gibt es einen fuer Bild 1");

        Check.That(own.PaintFor(300) is null,
                   "und keinen fuer Bild 300 - genau das ist der Sinn");

        own.PaintOn(300, 400, 300).Stroke(50f, 50f, 20f, 1f, 1f);

        Check.Near(own.PaintFor(1)!.At(200, 150), 1.0, 0.02, "Bild 1 behaelt seinen");
        Check.Near(own.PaintFor(300)!.At(200, 150), 0, 0.02, "Bild 300 hat einen eigenen");

        // Und die Kopie darf sie nicht teilen - sonst malte ein Rezept in das andere.
        var copy = own.Clone();

        copy.PaintFor(1)!.Stroke(50f, 50f, 20f, 1f, 1f);

        Check.Near(own.PaintFor(1)!.At(50, 50), 0, 0.02,
                   "eine Kopie malt nicht ins Original");
    }

    /// <summary>
    /// Eine Einstellungsebene mit Maske muss auch WIRKEN - mit allen Werkzeugen.
    ///
    /// Der Pinsel legt eine Einstellungsebene an; sie bringt nichts mit und wartet
    /// darauf, dass jemand sagt, was dort geschehen soll. Wenn die Werkzeuge auf ihr
    /// nichts tun, ist die ganze Maskenebene nutzlos - man haette einen Ort markiert,
    /// an dem nichts passieren kann.
    ///
    /// Geprueft werden die, nach denen gefragt wurde: Zonen - Tiefen, Mitten, Lichter
    /// - und Farbbereiche.
    /// </summary>
    private static void ToolsWorkOnAMaskLayer()
    {
        Check.Group("Werkzeuge wirken auf einer Maskenebene");

        const int w = 120, h = 100;

        static FloatFrame Colour(int w, int h, float r, float g, float b)
        {
            int count = w * h;
            var rr = new float[count];
            var gg = new float[count];
            var bb = new float[count];

            Array.Fill(rr, r);
            Array.Fill(gg, g);
            Array.Fill(bb, b);

            return new FloatFrame
            {
                Width = w, Height = h, R = rr, G = gg, B = bb,
                A = null, IsSceneReferred = false,
            };
        }

        var ground = Colour(w, h, 0.35f, 0.22f, 0.12f);

        var mask = new ImageLayer
        {
            Content = LayerContent.Adjustment, Name = "Maske",
            Mode = BlendMode.Normal,
            Mask = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true },
            Tools = new GradingStack(),
        };

        mask.Mask.PaintOn(0, w, h).Stroke(w / 2f, h / 2f, 25f, 1f, 1f);

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Bild" },
                mask,
            },
        };

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal) { [""] = ground };

        int middle = h / 2 * w + w / 2;
        int corner = 5 * w + 5;

        float plainMiddle = LayerComposer.Compose(stack, sources)!.R[middle];

        // --- Zonen: Tiefen, Mitten, Lichter ---
        mask.Tools!.Tools.Add(new LiftGammaGainTool { Gain = new ColourTriplet(2.0f, 1.0f, 1.0f) });

        var lifted = LayerComposer.Compose(stack, sources);

        Console.WriteLine($"         Zonen: Mitte {plainMiddle:0.000} -> {lifted!.R[middle]:0.000}, " +
                          $"Ecke {lifted.R[corner]:0.000}");

        Check.That(MathF.Abs(lifted.R[middle] - plainMiddle) > 0.02f,
                   "Zonen wirken dort, wo gemalt wurde",
                   $"{lifted.R[middle]:0.000} gegen {plainMiddle:0.000}");

        Check.Near(lifted.R[corner], ground.R[corner], 0.01,
                   "und daneben nicht - die Maske haelt");

        // Und zwar in einer Groessenordnung, mit der sich weiterrechnen laesst.
        //
        // Die Anzeigewerkzeuge klemmen bei Weiss, die geliehene Abbildung bildet
        // Weiss aber auf UNENDLICH Licht ab: Vor dem Deckel kam hier 177640 heraus -
        // sichtbar nicht als Fehler, sondern als Glanzschein ueber dem halben Bild
        // und als Unschaerfe, die eine Farbe bekommt.
        Check.That(lifted.R[middle] < 32f,
                   "und in einer Groesse, die spaeter niemanden vergiftet",
                   $"{lifted.R[middle]:0.0}");

        mask.Tools.Tools.Clear();

        // --- Farbbereiche ---
        // Der Grund ist orange - also traegt das Orangeband die Aenderung.
        var bands = new HslTool();

        bands.Bands[1].Luminance = 60f;

        mask.Tools.Tools.Add(bands);

        var banded = LayerComposer.Compose(stack, sources);

        Console.WriteLine($"         Farbbereiche: Mitte {plainMiddle:0.000} -> {banded!.R[middle]:0.000}");

        Check.That(MathF.Abs(banded.R[middle] - plainMiddle) > 0.02f,
                   "Farbbereiche wirken ebenso",
                   $"{banded.R[middle]:0.000} gegen {plainMiddle:0.000}");
    }

    /// <summary>Und der Composer muss sie auch benutzen.</summary>
    private static void TheComposerSeesIt()
    {
        Check.Group("Der Composer rechnet mit dem Anstrich");

        const int w = 200, h = 160;

        static FloatFrame Flat(int w, int h, float value)
        {
            int count = w * h;
            var r = new float[count];

            Array.Fill(r, value);

            return new FloatFrame
            {
                Width = w, Height = h,
                R = r, G = (float[])r.Clone(), B = (float[])r.Clone(),
                A = null, IsSceneReferred = false,
            };
        }

        var ground = Flat(w, h, 0f);
        var bright = Flat(w, h, 1f);

        var layer = new ImageLayer
        {
            Content = LayerContent.Image, Source = "hell", Name = "Hell",
            Mode = BlendMode.Normal,
            Mask = new LayerMask { Kind = MaskKind.Painted, PaintLocked = true },
        };

        layer.Mask.PaintOn(0, w, h).Stroke(w / 2f, h / 2f, 30f, 1f, 1f);

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Bild" },
                layer,
            },
        };

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            [""] = ground,
            ["hell"] = bright,
        };

        var built = LayerComposer.Compose(stack, sources);

        if (built is null)
        {
            Check.That(false, "es kommt ein Bild heraus");
            return;
        }

        float middle = built.R[h / 2 * w + w / 2];
        float corner = built.R[5 * w + 5];

        Console.WriteLine($"         Mitte {middle:0.00}, Ecke {corner:0.00}");

        Check.Near(middle, 1.0, 0.02, "wo gemalt wurde, wirkt die Ebene");
        Check.Near(corner, 0, 0.02, "wo nicht, nicht");

        // Umgedreht muss es genau andersherum sein - sonst waere der Schalter eine
        // Zierde.
        layer.Mask.Invert = true;

        var flipped = LayerComposer.Compose(stack, sources);

        Check.Near(flipped!.R[h / 2 * w + w / 2], 0, 0.02, "umgedreht ist die Mitte frei");
        Check.Near(flipped.R[5 * w + 5], 1.0, 0.02, "und die Ecke belegt");
    }
}
