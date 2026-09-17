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
        ThePassIsFound();
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

        double without = Fastest(() => Draw(frame, plain));
        double with = Fastest(() => Draw(frame, stack, data));
        double coarse = Fastest(() => Draw(frame, stack, data, step: 4));

        Console.WriteLine($"         1080p: ohne {without:0.0} ms, mit Tiefenschaerfe {with:0.0} ms, " +
                          $"beim Ziehen {coarse:0.0} ms");

        Check.That(with < 500, "der volle Durchgang bleibt im Rahmen", $"{with:0.0} ms");
        Check.That(coarse < 60, "beim Ziehen bleibt es bedienbar", $"{coarse:0.0} ms");
    }

    // ------------------------------------------------------------------- Handwerk

    private static GradingStack Focus(float aperture, float focus)
        => new() { Data = { new DepthFieldTool { Aperture = aperture, Focus = focus } } };

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
