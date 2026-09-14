using System.IO;
using System.Text;
using FrameFlip.Imaging;
using FrameFlip.Rendering;

namespace FrameFlip.Tests;

/// <summary>
/// Die Sichtumwandlung: sRGB-Kennlinie, dreidimensionale Tabellen und die
/// AgX-Kette, die aus Blenders eigener Farbverwaltung zusammengesetzt wird.
/// </summary>
public static class ViewTransformInvariants
{
    public static void Run()
    {
        SrgbCurve();
        CubeParsing();
        CubeInterpolation();
        GamutMatrix();
        AgxAgainstBlender();
    }

    // ------------------------------------------------------------------- sRGB

    private static void SrgbCurve()
    {
        Check.Group("sRGB-Kennlinie");

        Check.That(Srgb.Encode(0f) == 0f, "Schwarz bleibt Schwarz");
        Check.That(Srgb.Encode(1f) == 1f, "Weiss bleibt Weiss");

        // Der bekannte Eckwert: mittleres Grau der Renderwelt liegt bei 0,18 linear
        // und landet in sRGB knapp unter der Haelfte.
        Check.Near(Srgb.Encode(0.18f), 0.4613, 0.001, "0,18 linear wird zu 0,461");

        // Unten hat die Norm ein gerades Stueck - wer hier Gamma 2,2 rechnet, liegt
        // in den Schatten daneben.
        Check.Near(Srgb.Encode(0.002f), 0.02584, 0.0001, "das gerade Stueck unten stimmt");
        Check.Near(Srgb.Encode(0.0031308f), 0.040449, 0.0001, "der Knick liegt richtig");

        // Werte ausserhalb duerfen nicht durchschlagen.
        Check.That(Srgb.Encode(-1f) == 0f, "Negatives wird zu Schwarz");
        Check.That(Srgb.Encode(40f) == 1f, "Ueberstrahlung wird zu Weiss");
        Check.That(Srgb.Encode(float.NaN) == 0f, "NaN wird zu Schwarz");

        int worst = 0;
        for (int i = 0; i <= 1000; i++)
        {
            float linear = i / 1000f;
            float back = Srgb.Decode(Srgb.Encode(linear));
            if (Math.Abs(back - linear) > 0.0001f) worst++;
        }

        Check.That(worst == 0, "Hin und zurueck trifft wieder den Ausgangswert", $"{worst} Abweichungen");
    }

    // ------------------------------------------------------- Tabellen einlesen

    private static void CubeParsing()
    {
        Check.Group("Tabellen im .cube-Format");

        var lut = CubeLut.Parse(new StringReader(Identity(2)));
        Check.That(lut.Size == 2, "Groesse gelesen");
        Check.Near(lut.DomainMin, 0, 1e-6, "Untergrenze gelesen");
        Check.Near(lut.DomainMax, 1, 1e-6, "Obergrenze gelesen");

        // Kommentare und Titelzeile duerfen nicht stoeren - Blenders Tabellen haben
        // beides.
        var withNoise = CubeLut.Parse(new StringReader(
            "# ein Kommentar\nTITLE \"irgendwas\"\n\n" + Identity(2)));
        Check.That(withNoise.Size == 2, "Kommentar und Titel werden uebergangen");

        Check.Throws<InvalidDataException>(
            () => CubeLut.Parse(new StringReader("LUT_3D_SIZE 2\n0 0 0\n1 1 1\n")),
            "zu wenige Eintraege");

        Check.Throws<InvalidDataException>(
            () => CubeLut.Parse(new StringReader("0.5 0.5 0.5\n")),
            "Werte ohne Groessenangabe");

        Check.Throws<InvalidDataException>(
            () => CubeLut.Parse(new StringReader("LUT_1D_SIZE 16\n")),
            "eindimensionale Tabelle");

        Check.Throws<InvalidDataException>(
            () => CubeLut.Parse(new StringReader("LUT_3D_SIZE 999\n")),
            "unsinnige Groesse");
    }

    private static void CubeInterpolation()
    {
        Check.Group("Tetraedrische Interpolation");

        var identity = CubeLut.Parse(new StringReader(Identity(2)));

        // Eine Tabelle, die nichts tut, muss wirklich nichts tun - und zwar auch
        // zwischen den Stuetzstellen, nicht nur auf ihnen.
        int wrong = 0;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                float r = i / 20f, g = j / 20f, b = (i + j) / 40f;
                float wantR = r, wantG = g, wantB = b;

                identity.Apply(ref r, ref g, ref b);

                if (Math.Abs(r - wantR) > 1e-5f) wrong++;
                if (Math.Abs(g - wantG) > 1e-5f) wrong++;
                if (Math.Abs(b - wantB) > 1e-5f) wrong++;
            }
        }

        Check.That(wrong == 0, "eine Einheitstabelle laesst alles unveraendert", $"{wrong} Abweichungen");

        // Ausserhalb des Bereichs wird beschnitten, nicht fortgesetzt.
        float or_ = 5f, og = -3f, ob = 0.5f;
        identity.Apply(ref or_, ref og, ref ob);
        Check.Near(or_, 1.0, 1e-5, "ueber der Obergrenze wird beschnitten");
        Check.Near(og, 0.0, 1e-5, "unter der Untergrenze wird beschnitten");

        // Eine gröbere Tabelle mit einer bekannten Kruemmung: der Quadratwert. Auf
        // den Stuetzstellen muss sie exakt sein.
        var squared = CubeLut.Parse(new StringReader(Squared(9)));
        foreach (float v in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            float r = v, g = v, b = v;
            squared.Apply(ref r, ref g, ref b);
            Check.Near(r, v * v, 0.002, $"Stuetzstelle {v:0.##} trifft {v * v:0.####}");
        }

        // Die Graugerade ist der Fall, an dem sich die tetraedrische von der
        // trilinearen Interpolation unterscheidet: hier darf keine Verfaerbung
        // entstehen, auch nicht in der dritten Nachkommastelle.
        int tinted = 0;
        for (int i = 1; i < 60; i++)
        {
            float v = i / 60f;
            float r = v, g = v, b = v;
            squared.Apply(ref r, ref g, ref b);

            if (Math.Abs(r - g) > 1e-6f || Math.Abs(g - b) > 1e-6f) tinted++;
        }

        Check.That(tinted == 0, "Grau bleibt auf der Graugerade", $"{tinted} verfaerbte Stellen");
    }

    /// <summary>
    /// Die Matrix von Rec.709 nach FilmLight E-Gamut wird aus zwei Matrizen der
    /// Konfiguration gerechnet. Ein Fehler darin faerbt das ganze Bild, und zwar
    /// gleichmaessig - also genau so, dass man ihn fuer Absicht halten koennte.
    /// </summary>
    private static void GamutMatrix()
    {
        Check.Group("Farbraummatrix");

        var lut = CubeLut.Parse(new StringReader(Identity(2)));
        var agx = AgxViewTransform.FromLut(lut, "(Test)");

        // Weiss muss weiss bleiben: beide Raeume haben denselben Weisspunkt D65.
        // Mit der Einheitstabelle laesst sich das isoliert pruefen.
        float r = 1f, g = 1f, b = 1f;
        agx.Apply(ref r, ref g, ref b);

        Check.Near(r, g, 0.002, "Weiss bleibt neutral (R gegen G)");
        Check.Near(g, b, 0.002, "Weiss bleibt neutral (G gegen B)");

        // Und Grau ebenso, ueber mehrere Helligkeiten.
        int tinted = 0;
        foreach (float v in new[] { 0.05f, 0.18f, 0.5f, 2f, 8f })
        {
            float vr = v, vg = v, vb = v;
            agx.Apply(ref vr, ref vg, ref vb);
            if (Math.Abs(vr - vg) > 0.004f || Math.Abs(vg - vb) > 0.004f) tinted++;
        }

        Check.That(tinted == 0, "Grau bleibt ueber den ganzen Bereich neutral", $"{tinted} Stellen");
    }

    // --------------------------------------------------------- AgX gegen Blender

    /// <summary>Ein linearer Wert und das, was Blender daraus macht.</summary>
    private readonly record struct AgxSample(float R, float G, float B, byte Red, byte Green, byte Blue);

    /// <summary>
    /// Die eigentliche Probe: dieselben Zahlen, die Blender 4.5 mit AgX erzeugt hat.
    ///
    /// Gemessen wurde so: ein Bild mit Werten von 0,00001 bis 10 - fast 21
    /// Blendenstufen, neutral und gesaettigt - einmal als EXR gespeichert (roh,
    /// linear) und einmal als PNG durch AgX. Der Vergleich ueber alle 1024 Pixel
    /// ergab keine einzige Abweichung; die Zeilen hier sind Stichproben daraus.
    ///
    /// Der Test braucht Blenders Tabelle und laeuft deshalb nur auf einem Rechner,
    /// auf dem Blender installiert ist. Fehlt sie, sagt er das - stillschweigend
    /// durchzuwinken waere die schlechtere Antwort.
    /// </summary>
    private static readonly AgxSample[] Samples =
    {
        new(1.869402e-04f, 2.077114e-05f, 1.661691e-04f, 0, 0, 0),
        new(2.414426e-03f, 2.682696e-04f, 2.146157e-03f, 3, 0, 3),
        new(3.118351e-02f, 3.464835e-03f, 2.771868e-02f, 41, 11, 39),
        new(4.027506e-01f, 4.475006e-02f, 3.580005e-01f, 162, 81, 156),
        new(3.609251e+00f, 4.010279e-01f, 3.208223e+00f, 235, 194, 231),
        new(9e+00f, 1e+00f, 8e+00f, 250, 221, 247),

        new(1.038557e-05f, 8.308456e-05f, 2.077114e-04f, 0, 0, 0),
        new(1.341348e-04f, 1.073078e-03f, 2.682696e-03f, 0, 1, 3),
        new(1.732417e-03f, 1.385934e-02f, 3.464835e-02f, 1, 27, 45),
        new(2.237503e-02f, 1.790003e-01f, 4.475006e-01f, 30, 122, 169),
        new(2.00514e-01f, 1.604112e+00f, 4.010279e+00f, 183, 212, 237),
        new(5e-01f, 4e+00f, 1e+01f, 214, 233, 252),

        new(2.077114e-04f, 4.154228e-05f, 1.038557e-05f, 0, 0, 0),
        new(2.682696e-03f, 5.365392e-04f, 1.341348e-04f, 3, 0, 0),
        new(3.464835e-02f, 6.92967e-03f, 1.732417e-03f, 44, 13, 0),
        new(4.475006e-01f, 8.950013e-02f, 2.237503e-02f, 172, 83, 34),
        new(4.010279e+00f, 8.020558e-01f, 2.00514e-01f, 245, 189, 168),
        new(1e+01f, 2e+00f, 5e-01f, 255, 218, 203),

        new(2.077114e-04f, 2.077114e-04f, 2.077114e-04f, 0, 0, 0),
        new(2.682696e-03f, 2.682696e-03f, 2.682696e-03f, 4, 4, 4),
        new(3.464835e-02f, 3.464835e-02f, 3.464835e-02f, 46, 46, 46),
        new(4.475006e-01f, 4.475006e-01f, 4.475006e-01f, 165, 165, 165),
        new(4.010279e+00f, 4.010279e+00f, 4.010279e+00f, 233, 233, 233),
        new(1e+01f, 1e+01f, 1e+01f, 249, 249, 249),
    };

    private static void AgxAgainstBlender()
    {
        Check.Group("AgX trifft Blender");

        string? lutPath = null;
        foreach (var install in BlenderFinder.Find())
        {
            lutPath = AgxViewTransform.FindLut(install.Path);
            if (lutPath is not null) break;
        }

        if (lutPath is null)
        {
            Console.WriteLine("  [--]   uebersprungen: keine Blender-Installation mit AgX-Tabelle gefunden");
            return;
        }

        var agx = AgxViewTransform.TryLoad(Path.GetDirectoryName(lutPath)!);
        agx ??= AgxViewTransform.FromLut(CubeLut.Load(lutPath), lutPath);

        int worst = 0;
        foreach (var sample in Samples)
        {
            float r = sample.R, g = sample.G, b = sample.B;
            agx.Apply(ref r, ref g, ref b);

            worst = Math.Max(worst, Math.Abs(ToByte(r) - sample.Red));
            worst = Math.Max(worst, Math.Abs(ToByte(g) - sample.Green));
            worst = Math.Max(worst, Math.Abs(ToByte(b) - sample.Blue));
        }

        // Eine Stufe Abstand ist zugelassen, weil Blenders Tabelle sich zwischen
        // Fassungen aendern darf. Null waere die schaerfere Aussage, aber eine, die
        // bei der naechsten Blender-Fassung ohne Grund bricht.
        Check.That(worst <= 1, $"{Samples.Length} Stichproben treffen Blenders AgX",
                   $"groesste Abweichung {worst} von 255");

        Console.WriteLine($"  [i]    Tabelle: {lutPath}");
    }

    private static int ToByte(float value) => (int)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    // --------------------------------------------------------------- Hilfsmittel

    /// <summary>Eine Tabelle, die nichts veraendert.</summary>
    private static string Identity(int size) => Build(size, (r, g, b) => (r, g, b));

    /// <summary>Eine Tabelle mit bekannter Kruemmung.</summary>
    private static string Squared(int size) => Build(size, (r, g, b) => (r * r, g * g, b * b));

    private static string Build(int size, Func<float, float, float, (float, float, float)> f)
    {
        var text = new StringBuilder();
        text.AppendLine($"LUT_3D_SIZE {size}");
        text.AppendLine("DOMAIN_MIN 0 0 0");
        text.AppendLine("DOMAIN_MAX 1 1 1");

        // Rot laeuft am schnellsten - so schreibt es das Format vor.
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var (r, g, b) = f(x / (size - 1f), y / (size - 1f), z / (size - 1f));
                    text.AppendLine(
                        $"{r.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                        $"{g.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                        $"{b.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
        }

        return text.ToString();
    }
}
