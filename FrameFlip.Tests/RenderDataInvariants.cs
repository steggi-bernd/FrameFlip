using System.Runtime.InteropServices;
using System.Text.Json;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Werkzeuge, die Renderdaten brauchen - bisher die Tiefenschaerfe.
///
/// Sie sind die einzigen, die ohne EXR gar nichts tun koennen, und daran haengen
/// zwei Zusicherungen: Ohne den Pass darf nichts geschehen - ein Werkzeug, das sich
/// ohne Daten etwas ausdenkt, waere schlimmer als eines, das ruht - und mit dem Pass
/// muss die Entfernung ueber den KEHRWERT wirken, nicht linear.
/// </summary>
public static class RenderDataInvariants
{
    public static void Run()
    {
        WithoutThePassNothingHappens();
        TheFocusStaysSharp();
        TheBackgroundGoesSoft();
        ItGrowsWithTheInverse();
        TheSmearFollowsTheVector();
        StillThingsStayStill();
        ThePassIsFound();
        TheDisplacementFollowsThePass();
        ShapesTearsAndTime();
        Persistence();
        WhatItCosts();
    }

    /// <summary>
    /// Ohne Tiefenpass ruht das Werkzeug - und zwar vollstaendig.
    ///
    /// Nicht "es nimmt eine Entfernung an": Eine Datei ohne Tiefe ist kein Fehler,
    /// sondern eine Datei ohne Tiefe. Wer hier etwas erfaende, machte aus einem
    /// fehlenden Pass ein unscharfes Bild, und niemand faende den Grund.
    /// </summary>
    private static void WithoutThePassNothingHappens()
    {
        Check.Group("Ohne Tiefenpass geschieht nichts");

        var frame = Checker(96, 96, 0.1f, 0.9f, 6);
        var stack = Focus(aperture: 1f, focus: 5f);

        var plain = Draw(frame, new GradingStack());
        var blurred = Draw(frame, stack, data: null);

        Check.That(Same(plain, blurred), "ohne Pass bleibt das Bild, wie es war");

        // Und ein leerer Platz in der Liste ist dasselbe wie gar keine Liste.
        var empty = Draw(frame, stack, new FloatFrame?[] { null });
        Check.That(Same(plain, empty), "ein leerer Platz ebenso");

        // Mit Pass dagegen sehr wohl.
        var far = Depth(96, 96, 200f);
        var soft = Draw(frame, stack, new FloatFrame?[] { far });

        Check.That(!Same(plain, soft), "mit Pass wird es unscharf");
    }

    /// <summary>
    /// Was auf der Fokusentfernung liegt, bleibt Byte fuer Byte scharf.
    ///
    /// Das ist die Probe darauf, dass wirklich die Entfernung entscheidet und nicht
    /// etwa das ganze Bild ein wenig weichgezeichnet wird. Gemischt wird nur, wo der
    /// Zerstreuungskreis groesser als null ist.
    /// </summary>
    private static void TheFocusStaysSharp()
    {
        Check.Group("Auf der Fokusentfernung bleibt es scharf");

        var frame = Checker(96, 96, 0.1f, 0.9f, 6);

        // Linke Haelfte auf fuenf Metern, rechte auf zweihundert.
        var depth = Halves(96, 96, 5f, 200f);

        var plain = Draw(frame, new GradingStack());
        var drawn = Draw(frame, Focus(1f, 5f), new FloatFrame?[] { depth });

        int sharpApart = 0, softApart = 0;

        for (int y = 0; y < 96; y++)
        {
            // Der Rand der beiden Haelften bleibt aussen vor: Dort holt die
            // Weichzeichnung von der anderen Seite.
            for (int x = 0; x < 36; x++)
                if (drawn[At(96, x, y)] != plain[At(96, x, y)]) sharpApart++;

            for (int x = 60; x < 96; x++)
                if (drawn[At(96, x, y)] != plain[At(96, x, y)]) softApart++;
        }

        Check.That(sharpApart == 0, "die scharfe Haelfte ist unveraendert",
                   $"{sharpApart} Punkte anders");
        Check.That(softApart > 1000, "die ferne Haelfte nicht", $"{softApart} Punkte anders");
    }

    private static void TheBackgroundGoesSoft()
    {
        Check.Group("Wo nichts getroffen wurde, ist es unendlich weit weg");

        // Breit genug, damit der Zerstreuungskreis ueberhaupt Bildpunkte hat: Er ist
        // auf 1080p bezogen, und auf einem schmalen Testbild bleibt davon nichts.
        var frame = Checker(384, 192, 0.1f, 0.9f, 12);

        var nothing = Depth(384, 192, FloatFrame.NotHit * 10f);
        var near = Depth(384, 192, 5f);

        var background = Draw(frame, Focus(1f, 5f), new FloatFrame?[] { nothing });
        var subject = Draw(frame, Focus(1f, 5f), new FloatFrame?[] { near });

        Check.That(Sharpness(background) < Sharpness(subject) / 4,
                   "der leere Hintergrund wird so unscharf wie moeglich",
                   $"{Sharpness(background):0.0} gegen {Sharpness(subject):0.0}");
    }

    /// <summary>
    /// Die Unschaerfe waechst mit dem Unterschied der KEHRWERTE.
    ///
    /// Optisch liegt zwischen zwei und vier Metern dasselbe wie zwischen vier Metern
    /// und unendlich. Wer linear rechnet, bekommt einen Vordergrund, der gar nicht
    /// unscharf wird - und genau das prueft die letzte Zeile: Bei Fokus auf zehn
    /// Metern muss ein Gegenstand auf fuenf Metern DEUTLICH unschaerfer sein als einer
    /// auf fuenfzehn, obwohl beide in Metern gleich weit daneben liegen.
    /// </summary>
    private static void ItGrowsWithTheInverse()
    {
        Check.Group("Die Unschaerfe folgt dem Kehrwert der Entfernung");

        var frame = Checker(384, 192, 0.1f, 0.9f, 12);

        double At(float distance)
        {
            var drawn = Draw(frame, Focus(0.2f, 10f), new FloatFrame?[] { Depth(384, 192, distance) });
            return Sharpness(drawn);
        }

        // Fuenf und fuenfzehn Meter liegen in Metern gleich weit vom Fokus entfernt.
        // In Kehrwerten nicht: 1/5 liegt dreimal so weit von 1/10 wie 1/15. Die
        // Blende ist klein genug gewaehlt, dass beide noch unter der vollen
        // Unschaerfe bleiben - sonst waeren sie gleich, und der Test bewiese nichts.
        double sharp = At(10f);
        double close = At(5f);
        double far = At(15f);

        Check.That(close < sharp, "naeher als der Fokus wird es unscharf",
                   $"{close:0.0} gegen {sharp:0.0}");
        Check.That(far < sharp, "weiter weg auch", $"{far:0.0} gegen {sharp:0.0}");

        Check.That(close < far, "und der nahe Gegenstand deutlich mehr als der ferne",
                   $"nah {close:0.0}, fern {far:0.0}");
    }

    // ------------------------------------------------- Bewegungsunschaerfe

    /// <summary>
    /// Die Spur laeuft in der Richtung des Vektors - und die Y-Achse ist gedreht.
    ///
    /// Blender rechnet Bildschirmkoordinaten von unten nach oben, die Zeilen eines
    /// Bildes laufen von oben nach unten. Wer das vergisst, bekommt eine Unschaerfe,
    /// die senkrecht in die falsche Richtung zieht - waagerecht aber stimmt, und
    /// genau diese Haelfte-richtig-Haelfte-falsch sieht aus wie ein Fehler im
    /// Vektorpass.
    /// </summary>
    private static void TheSmearFollowsTheVector()
    {
        Check.Group("Die Spur laeuft in die Richtung des Vektors");

        // Ein heller Fleck auf Schwarz: Wohin er sich zieht, ist abzaehlbar.
        var frame = Spot(128, 128, 64, 64, 4);

        // Waagerecht nach rechts: Der Punkt WAR acht Punkte weiter links.
        var sideways = Vector(128, 128, -8f, 0f);
        var drawn = Draw(frame, Blur(1f), new FloatFrame?[] { sideways });

        // Der Fleck liegt auf 62 bis 65, die halbe Strecke ist vier Punkte lang -
        // die Spur reicht also von 58 bis 69. Gemessen wird innerhalb davon.
        int left = At(128, 59, 64);
        int right = At(128, 68, 64);

        Check.That(drawn[left] > 0, "die Spur reicht nach links", $"{drawn[left]}");
        Check.That(drawn[right] > 0, "und nach rechts", $"{drawn[right]}");

        // Senkrecht: Ein Vektor mit positivem Y zeigt bei Blender nach OBEN, also zu
        // kleineren Zeilennummern.
        var upright = Vector(128, 128, 0f, 8f);
        var vertical = Draw(frame, Blur(1f), new FloatFrame?[] { upright });

        int above = At(128, 64, 59);
        int aside = At(128, 59, 64);

        Check.That(vertical[above] > 0, "senkrecht zieht sie nach oben", $"{vertical[above]}");
        Check.That(vertical[aside] == 0, "und nicht zur Seite", $"{vertical[aside]}");
    }

    /// <summary>
    /// Was stillsteht, bleibt Byte fuer Byte stehen.
    ///
    /// Das ist der Normalfall in fast jedem Bild, und es ist zugleich die Probe
    /// darauf, dass wirklich der Vektor entscheidet: Eine Unschaerfe, die auch ohne
    /// Bewegung etwas tut, waere eine Weichzeichnung mit zusaetzlichen Schritten.
    /// </summary>
    private static void StillThingsStayStill()
    {
        Check.Group("Was stillsteht, bleibt stehen");

        var frame = Checker(128, 128, 0.1f, 0.9f, 8);

        var plain = Draw(frame, new GradingStack());
        var still = Draw(frame, Blur(1f), new FloatFrame?[] { Vector(128, 128, 0f, 0f) });

        Check.That(Same(plain, still), "ohne Bewegung aendert sich nichts");

        // Und ohne Vektorpass ebenfalls nicht.
        var without = Draw(frame, Blur(1f), data: null);
        Check.That(Same(plain, without), "ohne Vektorpass auch nicht");

        // Eine halbe Bewegung in einer Bildhaelfte laesst die andere in Ruhe.
        var half = Vector(128, 128, -10f, 0f, onlyRight: true);
        var mixed = Draw(frame, Blur(1f), new FloatFrame?[] { half });

        int quiet = 0, moved = 0;

        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 50; x++)
                if (mixed[At(128, x, y)] != plain[At(128, x, y)]) quiet++;

            for (int x = 78; x < 128; x++)
                if (mixed[At(128, x, y)] != plain[At(128, x, y)]) moved++;
        }

        Check.That(quiet == 0, "die stehende Haelfte ist unveraendert", $"{quiet} Punkte");
        Check.That(moved > 1000, "die bewegte nicht", $"{moved} Punkte");
    }

    /// <summary>
    /// Welcher Pass die Tiefe traegt, entscheidet sich beim Lesen.
    ///
    /// Blender nennt ihn heute "Depth", frueher "Z", und wer ohne Tiefenpass, aber
    /// mit Nebel rendert, hat nur "Mist". Drei Namen, eine Reihenfolge, eine Stelle.
    /// </summary>
    private static void ThePassIsFound()
    {
        Check.Group("Der Tiefenpass wird unter seinen Namen gefunden");

        Check.That(FramePasses.NameFor(PassNeed.Depth, Passes("ViewLayer.Depth")) == "ViewLayer.Depth",
                   "Depth wird gefunden");
        Check.That(FramePasses.NameFor(PassNeed.Depth, Passes("Z")) == "Z", "Z auch");
        Check.That(FramePasses.NameFor(PassNeed.Depth, Passes("ViewLayer.Mist")) == "ViewLayer.Mist",
                   "und Mist als Ersatz");

        // Die Reihenfolge zaehlt: Wer beides hat, bekommt die echte Entfernung.
        var both = Passes("ViewLayer.Mist", "ViewLayer.Depth");
        Check.That(FramePasses.NameFor(PassNeed.Depth, both) == "ViewLayer.Depth",
                   "wer beides fuehrt, bekommt die Tiefe",
                   FramePasses.NameFor(PassNeed.Depth, both) ?? "nichts");

        Check.That(FramePasses.NameFor(PassNeed.Depth, Passes()) is null,
                   "eine Datei ohne Tiefe meldet das");

        // Ein Farbpass mit passendem Namen zaehlt nicht - die Entfernung ist eine
        // Groesse je Bildpunkt, kein Bild.
        var colour = new[] { new ExrPass("ViewLayer.Depth", "R", "G", "B", null, Grey: false) };
        Check.That(FramePasses.NameFor(PassNeed.Depth, colour) is null,
                   "ein dreikanaliger Pass gilt nicht als Tiefe");

        // Und die Normale, andersherum: Sie IST eine Richtung und braucht deshalb
        // drei Kanaele. Ein einkanaliger Pass namens "Normal" enthielte keine, und
        // ein Verzug daraus zeigte ueberall dorthin, wo zufaellig die Helligkeit
        // hinzeigt - was aussaehe wie ein Effekt und keiner waere.
        var normals = new[] { new ExrPass("ViewLayer.Normal", "R", "G", "B", null, Grey: false) };

        Check.That(FramePasses.NameFor(PassNeed.Normal, normals) == "ViewLayer.Normal",
                   "der Normalpass wird gefunden",
                   FramePasses.NameFor(PassNeed.Normal, normals) ?? "nichts");

        Check.That(FramePasses.NameFor(PassNeed.Normal, Passes("ViewLayer.Normal")) is null,
                   "ein einkanaliger Pass gilt nicht als Normale");

        Check.That(FramePasses.NameFor(PassNeed.Normal, colour) is null,
                   "und ein Tiefenpass auch nicht - der Name entscheidet mit");

        Check.That(FramePasses.NameFor(PassNeed.Depth, normals) is null,
                   "umgekehrt ebenso");
    }

    private static void Persistence()
    {
        Check.Group("Die Tiefenschaerfe ueberlebt das Speichern");

        var stack = new GradingStack { Data = { new DepthFieldTool { Aperture = 0.7f, Focus = 3.5f } } };

        var read = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var tool = read.Data.OfType<DepthFieldTool>().FirstOrDefault();
        Check.That(tool is not null, "mit seinem Typ");
        if (tool is null) return;

        Check.Near(tool.Aperture, 0.7, 1e-5, "die Blende bleibt");
        Check.Near(tool.Focus, 3.5, 1e-5, "und die Fokusentfernung");

        var copy = stack.Clone();
        stack.Data.OfType<DepthFieldTool>().First().Focus = 99f;

        Check.Near(copy.Data.OfType<DepthFieldTool>().First().Focus, 3.5, 1e-5,
                   "eine Kopie bewegt sich nicht mit");

        // Die Verschiebung ebenso - und mit ihr die Wahl des Passes, denn die
        // entscheidet, welche Datei ueberhaupt gelesen wird.
        var verzug = new GradingStack
        {
            Data =
            {
                new DisplaceTool
                {
                    From = DisplaceFrom.Motion, Amount = 25f,
                    Wave = 0.6f, Wavelength = 55f, Angle = 210f, Spread = 0.3f,
                },
            },
        };

        var againVerzug = JsonSerializer.Deserialize<GradingStack>(JsonSerializer.Serialize(verzug));
        var back = againVerzug?.Data.OfType<DisplaceTool>().FirstOrDefault();

        Check.That(back is not null, "die Verschiebung kommt zurueck");

        if (back is not null)
        {
            Check.That(back.From == DisplaceFrom.Motion, "samt gewaehltem Pass",
                       back.From.ToString());

            Check.Near(back.Amount, 25f, 1e-4, "die Staerke");
            Check.Near(back.Wave, 0.6f, 1e-4, "die Welle");
            Check.Near(back.Wavelength, 55f, 1e-4, "die Wellenlaenge");
            Check.Near(back.Angle, 210f, 1e-4, "die Richtung");
            Check.Near(back.Spread, 0.3f, 1e-4, "und der Kanalversatz");

            Check.That(back.Needs == PassNeed.Motion,
                       "und sie verlangt danach den Vektorpass, nicht die Normale");
        }

        // In Grundstellung zaehlt es nicht mit - sonst zoege eine Blende von null
        // jedes Bild durch den Puffer und laese den Tiefenpass dazu.
        var quiet = new GradingStack { Data = { new DepthFieldTool { Aperture = 0f } } };

        Check.That(quiet.Prepare().Data.Length == 0, "abgedreht zaehlt es nicht");
        Check.That(quiet.IsNeutral, "und der Stapel gilt als neutral");
        Check.That(!quiet.Prepare().HasLocal, "der Puffer bleibt aus");
        Check.That(stack.Prepare().HasLocal, "aufgedreht braucht es ihn");
    }

    private static void WhatItCosts()
    {
        Check.Group("Die Tiefenschaerfe bleibt bezahlbar");

        var frame = Checker(1920, 1080, 0.2f, 0.8f, 40);
        var depth = Depth(1920, 1080, 40f);

        var plain = new GradingStack();
        var stack = Focus(0.6f, 10f);
        var data = new FloatFrame?[] { depth };

        Draw(frame, stack, data);

        var moving = new GradingStack { Data = { new MotionBlurTool { Shutter = 0.5f, Samples = 16 } } };
        var vectors = new FloatFrame?[] { Vector(1920, 1080, -12f, 4f) };

        Draw(frame, moving, vectors);

        double without = Fastest(() => Draw(frame, plain));
        double with = Fastest(() => Draw(frame, stack, data));
        double coarse = Fastest(() => Draw(frame, stack, data, step: 4));
        double motion = Fastest(() => Draw(frame, moving, vectors));
        double motionCoarse = Fastest(() => Draw(frame, moving, vectors, step: 4));

        Console.WriteLine($"         1080p: ohne {without:0.0} ms, mit Tiefenschaerfe {with:0.0} ms, " +
                          $"beim Ziehen {coarse:0.0} ms");
        Console.WriteLine($"         1080p: Bewegung ueber alles {motion:0.0} ms, " +
                          $"beim Ziehen {motionCoarse:0.0} ms");

        Check.Timing(with < 500, "der volle Durchgang bleibt im Rahmen", $"{with:0.0} ms");
        Check.Timing(coarse < 60, "beim Ziehen bleibt es bedienbar", $"{coarse:0.0} ms");
        Check.Timing(motionCoarse < 80, "auch mit Bewegung ueber dem ganzen Bild",
                   $"{motionCoarse:0.0} ms");
    }

    /// <summary>
    /// Die Verschiebung folgt dem Pass - und nur ihm.
    ///
    /// Das ist der ganze Grund, warum es dieses Werkzeug hier gibt und nicht in
    /// fuenfzig anderen Programmen: Ein Wellenfilter im Bildbearbeiter kennt nur das
    /// fertige Bild und schiebt deshalb alles gleich weit - Vordergrund, Hintergrund
    /// und Himmel. Hier steht in der Datei, wohin jede Flaeche zeigt.
    ///
    /// Geprueft wird deshalb vor allem die Trennung: Wo der Pass eine Richtung nennt,
    /// muss sich etwas bewegen, und wo er schweigt, darf sich NICHTS bewegen. Ein
    /// Verzug, der auch den unbeschriebenen Himmel mitnimmt, waere derselbe Filter
    /// wie ueberall sonst.
    /// </summary>
    private static void TheDisplacementFollowsThePass()
    {
        Check.Group("Die Verschiebung folgt dem Pass");

        const int w = 200, h = 120;

        // Genau EINE harte Kante in der Mitte - links dunkel, rechts hell.
        //
        // Kein Schachbrett: Dort waere in jeder zweiten Zeilenreihe die helle Seite
        // links, und die Kantensuche faende die falsche Kante. Die Zahlen saehen dann
        // nach einem viel groesseren Ausschlag aus, als wirklich da ist - eine Probe,
        // die aus dem falschen Grund gruen ist.
        static FloatFrame Edge(int width, int height)
        {
            int count = width * height;

            var r = new float[count];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    r[y * width + x] = x < width / 2 ? 0.05f : 0.9f;

            return new FloatFrame
            {
                Width = width, Height = height,
                R = r, G = (float[])r.Clone(), B = (float[])r.Clone(),
                A = null, IsSceneReferred = true,
            };
        }

        var frame = Edge(w, h);

        // Ein Normalpass, der nur in der unteren Haelfte nach rechts zeigt. Oben
        // steht null - dort darf nichts geschehen.
        static FloatFrame Normals(int width, int height, float nx, float ny, bool lowerOnly)
        {
            int count = width * height;

            var r = new float[count];
            var g = new float[count];
            var b = new float[count];

            for (int y = 0; y < height; y++)
            {
                if (lowerOnly && y < height / 2) continue;

                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    r[i] = nx;
                    g[i] = ny;
                    b[i] = 1f;
                }
            }

            return new FloatFrame
            {
                Width = width, Height = height, R = r, G = g, B = b,
                A = null, IsSceneReferred = true,
            };
        }

        static GradingStack Shift(float amount, float wave = 0f, float spread = 0f)
            => new()
            {
                Data =
                {
                    new DisplaceTool
                    {
                        From = DisplaceFrom.Normal, Amount = amount,
                        Wave = wave, Wavelength = 40f, Angle = 90f, Spread = spread,
                    },
                },
            };

        var plain = Draw(frame, new GradingStack());

        var pass = Normals(w, h, 1f, 0f, lowerOnly: true);
        var shifted = Draw(frame, Shift(30f), new FloatFrame?[] { pass });

        static int Row(byte[] pixels, int width, int y)
        {
            // Wo die Kante liegt: die erste Spalte, ab der es hell wird.
            for (int x = 1; x < width; x++)
                if (pixels[(y * width + x) * 4] > 128) return x;

            return -1;
        }

        int topPlain = Row(plain, w, h / 4);
        int topShifted = Row(shifted, w, h / 4);

        int lowPlain = Row(plain, w, h * 3 / 4);
        int lowShifted = Row(shifted, w, h * 3 / 4);

        Console.WriteLine($"         Kante oben {topPlain} -> {topShifted}, " +
                          $"unten {lowPlain} -> {lowShifted}");

        Check.That(topShifted == topPlain,
                   "wo der Pass schweigt, bleibt die Kante stehen",
                   $"{topPlain} gegen {topShifted}");

        Check.That(lowShifted != lowPlain,
                   "wo er eine Richtung nennt, wandert sie",
                   $"{lowPlain} gegen {lowShifted}");

        Check.That(Math.Abs(Math.Abs(lowShifted - lowPlain) - 30) <= 3,
                   "und zwar um die eingestellte Strecke",
                   $"{Math.Abs(lowShifted - lowPlain)} statt 30");

        // "Nur Welle": Der eine Fall dieser Werkzeugfamilie, der OHNE Renderdaten
        // laeuft - und damit auf jedem Bild, auch auf einem PNG.
        //
        // Geprueft wird mit data = null, also genau so, wie der Aufrufer es liefert,
        // wenn die Datei den Pass nicht fuehrt. Mit einem leeren Pass zu pruefen
        // waere die falsche Probe: Sie liefe auch dann gruen, wenn das Werkzeug
        // weiterhin einen verlangte.
        var alone = new GradingStack
        {
            Data =
            {
                new DisplaceTool
                {
                    From = DisplaceFrom.Screen, Amount = 30f,
                    Wave = 0f, Wavelength = 40f, Angle = 90f,
                },
            },
        };

        var without = Draw(frame, alone, data: null);

        int aloneEdge = Row(without, w, h / 2);

        Console.WriteLine($"         nur Welle, ohne jeden Pass: Kante {lowPlain} -> {aloneEdge}");

        Check.That(aloneEdge != lowPlain,
                   "auf 'nur Welle' schiebt es auch ganz ohne Pass",
                   $"{lowPlain} gegen {aloneEdge}");

        Check.That(Math.Abs(Math.Abs(aloneEdge - lowPlain) - 30) <= 3,
                   "und zwar um die eingestellte Strecke",
                   $"{Math.Abs(aloneEdge - lowPlain)} statt 30");

        // Und es verlangt dann auch keinen mehr - sonst laese das Programm einen
        // Normalpass, den niemand ansieht.
        var tool = alone.Data.OfType<DisplaceTool>().First();

        Check.That(tool.Needs == PassNeed.None, "es verlangt keinen Pass mehr",
                   tool.Needs.ToString());

        Check.That(tool.Optional, "und sagt, dass es ohne einen laufen darf");

        Check.That(FramePasses.NameFor(PassNeed.None, Passes("ViewLayer.Normal")) is null,
                   "und fuer 'keinen' wird auch keiner gesucht");

        // Die beiden anderen bleiben dabei, was sie waren: Ohne ihren Pass ruhen sie.
        var needs = new GradingStack
        {
            Data = { new DisplaceTool { From = DisplaceFrom.Normal, Amount = 30f } },
        };

        var nothing = Draw(frame, needs, data: null);

        Check.That(Row(nothing, w, h / 2) == lowPlain,
                   "mit Normalpass gewaehlt und ohne Datei bleibt alles stehen",
                   $"{Row(nothing, w, h / 2)}");

        Check.That(!needs.Data.OfType<DisplaceTool>().First().Optional,
                   "und es sagt auch nicht, dass es ohne einen laufen duerfte");

        // Ohne Staerke zaehlt es nicht mit - sonst zoege eine Null jedes Bild durch
        // den Puffer und laese den Normalpass dazu.
        var quiet = Shift(0f);

        Check.That(quiet.Prepare().Data.Length == 0, "abgedreht zaehlt es nicht");
        Check.That(quiet.IsNeutral, "und der Stapel gilt als neutral");

        // Die Welle macht aus dem glatten Verzug Baender. Bei voller Welle schwingt
        // sie um null - dann muss es Zeilen geben, die weiter geschoben sind als der
        // Schnitt, und Zeilen, die zurueckgeschoben sind.
        var waved = Draw(frame, Shift(30f, wave: 1f),
                         new FloatFrame?[] { Normals(w, h, 1f, 0f, lowerOnly: false) });

        int least = int.MaxValue, most = int.MinValue;

        for (int y = 0; y < h; y++)
        {
            int at = Row(waved, w, y);
            if (at < 0) continue;

            least = Math.Min(least, at);
            most = Math.Max(most, at);
        }

        Console.WriteLine($"         mit Welle: Kante zwischen {least} und {most}");

        // Bei voller Welle schwingt die Staerke zwischen -30 und +30, die Kante also
        // zwischen 70 und 130. Geprueft wird beides: dass sie ueberhaupt in Baender
        // zerfaellt UND dass sie dabei nicht weiter laeuft, als eingestellt ist.
        Check.That(most - least > 20,
                   "die Welle legt die Kante in Baender", $"{most - least} Punkte Spanne");

        Check.That(least >= 100 - 33 && most <= 100 + 33,
                   "und keine davon laeuft weiter als die Staerke erlaubt",
                   $"{least} bis {most}");

        // Der Kanalversatz schiebt die Kanaele verschieden weit - das ist der
        // Farbsaum, der den Riss nach Stoerung aussehen laesst.
        var spread = Draw(frame, Shift(30f, spread: 0.4f),
                          new FloatFrame?[] { Normals(w, h, 1f, 0f, lowerOnly: false) });

        int apart = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;

                if (Math.Abs(spread[i] - spread[i + 2]) > 60) apart++;
            }
        }

        Console.WriteLine($"         Kanalversatz: {apart} Punkte mit Farbsaum");

        Check.That(apart > 0, "mit Versatz laufen Rot und Blau auseinander", $"{apart}");
    }

    /// <summary>
    /// Die Formen, der Riss und die Zeit - jede mit dem, was sie verspricht.
    ///
    /// Gemessen wird an einer einzigen harten Kante: Wo sie nach dem Schieben steht,
    /// sagt je Zeile genau, wie weit geschoben wurde. Daraus laesst sich fuer jede
    /// Form eine Zusage ablesen, die keine andere Form erfuellt - und nur so eine
    /// Probe unterscheidet "die Form wirkt" von "irgendetwas bewegt sich".
    /// </summary>
    private static void ShapesTearsAndTime()
    {
        Check.Group("Formen, Risse und Zeit");

        const int w = 200, h = 160;
        const float amount = 30f;

        static FloatFrame Edges(int width, int height, params int[] at)
        {
            // Hell und dunkel im Wechsel, mit harten Kanten an den genannten Stellen.
            int count = width * height;
            var r = new float[count];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int crossed = at.Count(edge => x >= edge);
                    r[y * width + x] = crossed % 2 == 1 ? 0.9f : 0.05f;
                }
            }

            return new FloatFrame
            {
                Width = width, Height = height,
                R = r, G = (float[])r.Clone(), B = (float[])r.Clone(),
                A = null, IsSceneReferred = true,
            };
        }

        static GradingStack Tool(WaveShape shape, float speed = 0f, int seed = 0,
                                 float density = 1f, float wave = 1f, bool wrap = false,
                                 float length = 20f, float move = amount)
            => new()
            {
                Data =
                {
                    new DisplaceTool
                    {
                        From = DisplaceFrom.Screen, Amount = move, Shape = shape,
                        Wavelength = length, Wave = wave, Angle = 90f,
                        Speed = speed, Seed = seed, Density = density, Wrap = wrap,
                    },
                },
            };

        // Die Kante in jeder Zeile - wo es von dunkel nach hell geht, nach einer Stelle.
        static int[] EdgeAt(byte[] pixels, int width, int height, int from)
        {
            var found = new int[height];

            for (int y = 0; y < height; y++)
            {
                found[y] = -1;

                for (int x = Math.Max(1, from); x < width; x++)
                {
                    if (pixels[(y * width + x) * 4] > 128 && pixels[(y * width + x - 1) * 4] <= 128)
                    {
                        found[y] = x;
                        break;
                    }
                }
            }

            return found;
        }

        var frame = Edges(w, h, 100);

        // --- Rechteck: nur zwei Stellungen, nichts dazwischen ------------------
        var square = EdgeAt(Draw(frame, Tool(WaveShape.Square)), w, h, 0);
        var squarePlaces = square.Where(x => x >= 0).Distinct().OrderBy(x => x).ToArray();

        Console.WriteLine($"         Rechteck: Kante an {string.Join(", ", squarePlaces)}");

        Check.That(squarePlaces.Length == 2,
                   "Rechteck kennt genau zwei Stellungen",
                   string.Join(", ", squarePlaces));

        Check.That(squarePlaces.Length == 2 &&
                   Math.Abs(squarePlaces[0] - 70) <= 1 && Math.Abs(squarePlaces[1] - 130) <= 1,
                   "und zwar um die volle Staerke nach beiden Seiten");

        // --- Dreieck: dieselbe Spanne, aber alles dazwischen -------------------
        var triangle = EdgeAt(Draw(frame, Tool(WaveShape.Triangle)), w, h, 0);
        var trianglePlaces = triangle.Where(x => x >= 0).Distinct().ToArray();

        Check.That(trianglePlaces.Length > 4,
                   "Dreieck laeuft durch viele Stellungen",
                   $"{trianglePlaces.Length}");

        Check.That(trianglePlaces.Min() >= 69 && trianglePlaces.Max() <= 131,
                   "und bleibt dabei in derselben Spanne",
                   $"{trianglePlaces.Min()} bis {trianglePlaces.Max()}");

        // --- Streifen: innerhalb eines Bandes gleich, zwischen Baendern nicht --
        //
        // Das ist die Zusage des Glitch: ein Band springt als GANZES. Eine Welle, die
        // innerhalb eines Bandes weiterschwingt, waere ein Sinus mit Zacken.
        var slices = EdgeAt(Draw(frame, Tool(WaveShape.Slices, length: 20f)), w, h, 0);

        int torn = 0, bands = 0;

        for (int band = 0; band < h / 20; band++)
        {
            var inBand = Enumerable.Range(band * 20, 20).Select(y => slices[y]).Distinct().Count();

            if (inBand != 1) torn++;
            bands++;
        }

        var bandPlaces = Enumerable.Range(0, h / 20).Select(b => slices[b * 20 + 10]).Distinct().Count();

        Console.WriteLine($"         Streifen: {bandPlaces} verschiedene Stellungen in {bands} Baendern, " +
                          $"{torn} Baender in sich zerrissen");

        Check.That(torn == 0, "jedes Band springt als Ganzes", $"{torn} Baender nicht");
        Check.That(bandPlaces > 3, "und die Baender springen verschieden weit", $"{bandPlaces}");

        // Dichte null: nichts bewegt sich. Das ist der Regler, der aus "alles
        // zittert" einen Riss macht - bei null muss er ganz zu sein.
        var none = EdgeAt(Draw(frame, Tool(WaveShape.Slices, density: 0f)), w, h, 0);

        Check.That(none.All(x => x == 100), "bei Dichte null bleibt alles stehen");

        var sparse = EdgeAt(Draw(frame, Tool(WaveShape.Slices, density: 0.3f)), w, h, 0);
        int still = sparse.Count(x => x == 100);

        Console.WriteLine($"         Dichte 0,3: {still} von {h} Zeilen unbewegt");

        Check.That(still > h / 3 && still < h,
                   "bei wenig Dichte steht der groessere Teil still, aber nicht alles",
                   $"{still} von {h}");

        // --- Bloecke: ein Band zerfaellt in Stuecke ------------------------------
        //
        // Zwei Kanten hundert Punkte auseinander. Bei Streifen verschieben sich beide
        // immer gleich weit - ein Band ist ein Band. Bei Bloecken liegen sie oft in
        // verschiedenen Stuecken und springen verschieden.
        var two = Edges(w, h, 50, 100, 150);

        int Apart(GradingStack stack)
        {
            var pixels = Draw(two, stack);

            var first = EdgeAt(pixels, w, h, 0);
            var second = EdgeAt(pixels, w, h, 125);

            int differ = 0;

            for (int y = 0; y < h; y++)
            {
                if (first[y] < 0 || second[y] < 0) continue;
                if (Math.Abs((first[y] - 50) - (second[y] - 150)) > 1) differ++;
            }

            return differ;
        }

        int slicesApart = Apart(Tool(WaveShape.Slices, length: 10f));
        int blocksApart = Apart(Tool(WaveShape.Blocks, length: 10f));

        Console.WriteLine($"         zwei Kanten verschieden weit: Streifen {slicesApart}, Bloecke {blocksApart} Zeilen");

        Check.That(slicesApart == 0, "bei Streifen springen zwei Kanten derselben Zeile gleich",
                   $"{slicesApart}");
        Check.That(blocksApart > 0, "bei Bloecken zerfaellt das Band in Stuecke",
                   $"{blocksApart}");

        // --- Wuerfeln: anderer Startwert, anderer Riss ---------------------------
        var again = EdgeAt(Draw(frame, Tool(WaveShape.Slices, seed: 0)), w, h, 0);
        var other = EdgeAt(Draw(frame, Tool(WaveShape.Slices, seed: 1)), w, h, 0);

        Check.That(again.SequenceEqual(slices),
                   "derselbe Startwert gibt denselben Riss - jedes Mal");

        Check.That(!other.SequenceEqual(slices), "ein anderer gibt einen anderen");

        // --- Zeit: das Tempo laesst es wandern -----------------------------------
        //
        // Eine Verschiebung, die ueber alle Bilder gleich steht, sieht aus wie ein
        // Aufkleber auf der Linse.
        byte[] AtFrame(GradingStack stack, int number)
        {
            int stride = w * 4;
            var pixels = new byte[stride * h];
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);

            try
            {
                FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                          stack.Prepare(), buffer, stride, 1, null, number, null);

                System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, pixels.Length);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }

            return pixels;
        }

        var moving = Tool(WaveShape.Sine, speed: 0.1f);
        var frozen = Tool(WaveShape.Sine, speed: 0f);

        Check.That(!AtFrame(moving, 0).SequenceEqual(AtFrame(moving, 7)),
                   "mit Tempo steht die Welle in Bild 7 woanders als in Bild 0");

        Check.That(AtFrame(frozen, 0).SequenceEqual(AtFrame(frozen, 7)),
                   "ohne Tempo steht sie still");

        // Eine ganze Wellenlaenge weiter ist wieder dieselbe Welle. Bei Tempo 0,1
        // ist das nach zehn Bildern - und genau dann muss das Bild gleich sein.
        Check.That(AtFrame(moving, 0).SequenceEqual(AtFrame(moving, 10)),
                   "und nach einer vollen Wellenlaenge ist sie wieder, wo sie war");

        // Beim Riss wechselt der Startwert in ganzen Schritten: Bei Tempo 0,5 steht
        // er zwei Bilder lang und springt dann.
        var flicker = Tool(WaveShape.Slices, speed: 0.5f);

        Check.That(AtFrame(flicker, 0).SequenceEqual(AtFrame(flicker, 1)),
                   "bei Tempo 0,5 haelt ein Riss zwei Bilder");

        Check.That(!AtFrame(flicker, 0).SequenceEqual(AtFrame(flicker, 2)),
                   "und springt im dritten");

        // --- Umschlagen: was links hinausgeht, kommt rechts herein ---------------
        //
        // Glatt um fuenfzig Punkte nach LINKS geschoben (die Staerke ist negativ, und
        // auf 90 Grad zeigt die Richtung nach links). Rechts muss dann etwas
        // nachkommen: Festgehalten ist das die Randfarbe - hell. Umgeschlagen ist es,
        // was links hinausgeschoben wurde - dunkel.
        var held = Draw(frame, Tool(WaveShape.Sine, wave: 0f, move: -50f));
        var rolled = Draw(frame, Tool(WaveShape.Sine, wave: 0f, move: -50f, wrap: true));

        int row = h / 2;
        int right = row * w + (w - 5);

        Console.WriteLine($"         rechts aussen: festgehalten {held[right * 4]}, " +
                          $"umgeschlagen {rolled[right * 4]}");

        Check.That(held[right * 4] > 192, "festgehalten bleibt rechts der helle Rand");
        Check.That(rolled[right * 4] < 64,
                   "umgeschlagen kommt dort herein, was links hinausging");

        // Und die Kante selbst ist in beiden Faellen um dieselbe Strecke gewandert -
        // Umschlagen aendert nur, was am Rand nachkommt.
        Check.That(EdgeAt(held, w, h, 0)[row] == EdgeAt(rolled, w, h, 0)[row],
                   "die Kante wandert in beiden Faellen gleich weit");
    }

    // ------------------------------------------------------------------- Handwerk

    private static GradingStack Focus(float aperture, float focus)
        => new() { Data = { new DepthFieldTool { Aperture = aperture, Focus = focus } } };

    private static GradingStack Blur(float shutter)
        => new() { Data = { new MotionBlurTool { Shutter = shutter, Samples = 16 } } };

    /// <summary>
    /// Ein Vektorpass mit einer einzigen Bewegung: X und Y sagen, wo der Punkt im
    /// vorigen Bild war, Z und W, wo er im naechsten sein wird.
    /// </summary>
    private static FloatFrame Vector(int width, int height, float dx, float dy,
                                     bool onlyRight = false)
    {
        int count = width * height;

        var back = new float[count];
        var backY = new float[count];
        var front = new float[count];
        var frontY = new float[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (onlyRight && x < width / 2) continue;

                int i = y * width + x;

                back[i] = dx;
                backY[i] = dy;
                front[i] = -dx;
                frontY[i] = -dy;
            }
        }

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = back,
            G = backY,
            B = front,
            A = frontY,
            IsSceneReferred = false,
        };
    }

    /// <summary>Ein heller Fleck auf Schwarz.</summary>
    private static FloatFrame Spot(int width, int height, int centreX, int centreY, int size)
    {
        var values = new float[width * height];

        for (int y = centreY - size / 2; y < centreY + size / 2; y++)
            for (int x = centreX - size / 2; x < centreX + size / 2; x++)
                values[y * width + x] = 1f;

        return Frame(width, height, values);
    }

    private static IReadOnlyList<ExrPass> Passes(params string[] names)
        => names.Select(n => new ExrPass(n, n + ".V", n + ".V", n + ".V", null, Grey: true)).ToArray();

    private static int At(int width, int x, int y) => (y * width + x) * 4 + 2;

    /// <summary>
    /// Wie scharf ein Bild ist: der mittlere Unterschied zwischen Nachbarn.
    ///
    /// Auf einem Schachbrett ist das ein gerades Mass fuer die Unschaerfe - je
    /// weicher, desto naeher liegen die Nachbarn beieinander.
    /// </summary>
    private static double Sharpness(byte[] pixels)
    {
        double sum = 0;
        int count = 0;

        for (int i = 2; i + 4 < pixels.Length; i += 4)
        {
            sum += Math.Abs(pixels[i + 4] - pixels[i]);
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private static double Fastest(Action action)
    {
        double best = double.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            action();
            watch.Stop();

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    private static byte[] Draw(FloatFrame frame, GradingStack stack, FloatFrame?[]? data = null,
                               int step = 1)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                      stack.Prepare(), buffer, stride, step, null, 0, data);

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

    private static FloatFrame Checker(int width, int height, float dark, float light, int size)
    {
        var values = new float[width * height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                values[y * width + x] = (x / size + y / size) % 2 == 0 ? dark : light;

        return Frame(width, height, values);
    }

    /// <summary>Ein Tiefenpass mit einer einzigen Entfernung.</summary>
    private static FloatFrame Depth(int width, int height, float distance)
    {
        var values = new float[width * height];
        Array.Fill(values, distance);

        return Frame(width, height, values);
    }

    /// <summary>Zwei Entfernungen, links und rechts.</summary>
    private static FloatFrame Halves(int width, int height, float near, float far)
    {
        var values = new float[width * height];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                values[y * width + x] = x < width / 2 ? near : far;

        return Frame(width, height, values);
    }

    private static FloatFrame Frame(int width, int height, float[] values)
        => new()
        {
            Width = width,
            Height = height,
            R = values,
            G = (float[])values.Clone(),
            B = (float[])values.Clone(),
            A = Enumerable.Repeat(1f, width * height).ToArray(),
            IsSceneReferred = false,
        };
}
