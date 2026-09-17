using System.IO;
using System.Runtime.InteropServices;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Der gemeldete Stapel, nachgebaut: zwei freigestellte Ebenen uebereinander, darueber
/// zwei Glanzebenen auf Screen.
///
/// Gemeldet wurde, dass in den freigestellten Stellen pixeliges Rauschen erscheint,
/// sobald eine der Figurenebenen VERSCHOBEN wird - und dass es verschwindet, wenn die
/// Glanzebenen fehlen. Beides zusammen ist ein Hinweis auf eine bestimmte Stelle, und
/// dieser Test ist der Versuch, sie einzukreisen, statt danach zu suchen.
///
/// Gemessen wird nicht "sieht es gut aus", sondern die Streuung zwischen benachbarten
/// Punkten in einer Ecke, die nur Untergrund zeigen darf. Rauschen ist genau das:
/// Nachbarn, die weit auseinanderliegen.
/// </summary>
public static class StackReproInvariants
{
    public static void Run()
    {
        string? folder = Find();
        if (folder is null) return;

        PlacedCutoutStaysClean(folder);
        ScreenOnTopStaysClean(folder);
        TheWholeReportedStack(folder);
        ScreenOverBlack(folder);
        ByTheNumbersOfTheRealFiles();
    }

    /// <summary>
    /// Der Versuch, den der Nutzer selbst gefunden hat: eine freigestellte Ebene
    /// DUPLIZIERT, auf Negativ multiplizieren gestellt und ueber die durchsichtige
    /// Stelle der anderen geschoben - auf Schwarz.
    ///
    /// Auf Schwarz zeigt sich jede Verunreinigung, weil nichts sie ueberdeckt: Ein
    /// Anteil von vier Tausendstel steht dort noch als Byte elf. Und Screen laesst
    /// nie etwas dunkler werden, also bleibt alles stehen, was hineinkommt.
    ///
    /// Die zwei Faelle trennen die Frage: Eine Datei mit Deckung GENAU NULL darf
    /// nichts durchlassen - tut sie es doch, ist es ein Fehler im Programm. Eine
    /// Datei, deren Deckung im Hintergrund ein paar Stufen ueber null liegt, LAESST
    /// rechnerisch etwas durch, und dann liegt es an der Datei.
    /// </summary>
    private static void ScreenOverBlack(string folder)
    {
        Check.Group("Negativ multiplizieren auf Schwarz");

        var black = Read(folder, "00_grund_schwarz.png");
        var clean = Read(folder, "02_freigestellt_muell_unter_deckung.png");
        var dirty = Read(folder, "14_fast_durchsichtig_mit_rauschen.png");

        if (black is null || clean is null || dirty is null) return;

        double Build(FloatFrame layer, float matte = 0f, bool display = false)
        {
            var stack = Base(black);

            // Zwei Figurenebenen uebereinander, eine davon verschoben.
            stack.Layers.Add(new ImageLayer
            {
                Content = LayerContent.Image, Source = "figur", Name = "Figur",
                Mode = BlendMode.Normal, MatteFloor = matte, BlendInDisplay = display,
            });

            stack.Layers.Add(new ImageLayer
            {
                Content = LayerContent.Image, Source = "figur", Name = "Figur 2",
                Mode = BlendMode.Normal, MatteFloor = matte, BlendInDisplay = display,
                Place = new LayerTransform { OffsetY = 0.03f },
            });

            // Und die dritte auf Screen, nach oben geschoben.
            stack.Layers.Add(new ImageLayer
            {
                Content = LayerContent.Image, Source = "figur", Name = "Screen",
                Mode = BlendMode.Screen, MatteFloor = matte, BlendInDisplay = display,
                Place = new LayerTransform { OffsetY = -0.12f },
            });

            var drawn = Draw(stack, Sources(black, ("figur", layer)));

            return drawn is null ? 0 : Noise(drawn, black.Width, 2, 2, 40);
        }

        double exact = Build(clean);
        double faint = Build(dirty);

        Console.WriteLine($"         Deckung genau null: {exact:0.00}   " +
                          $"Deckung knapp ueber null: {faint:0.00}");

        Check.That(exact < 1.0,
                   "bei Deckung genau null kommt nichts durch - auch nicht auf Screen",
                   $"{exact:0.00} Stufen zwischen Nachbarn");

        Check.That(faint > 4.0,
                   "bei Deckung knapp ueber null sehr wohl - und das ist die Datei, nicht das Programm",
                   $"{faint:0.00} Stufen zwischen Nachbarn");

        // Und dagegen gibt es den Regler: Was unter der eingestellten Deckung liegt,
        // gilt als nicht vorhanden.
        double cleaned = Build(dirty, matte: 0.04f);

        Console.WriteLine($"         mit gesaeuberter Deckung (10/255): {cleaned:0.00}");

        Check.That(cleaned < 1.0, "mit gesaeuberter Deckung ist es wieder weg",
                   $"{cleaned:0.00} Stufen zwischen Nachbarn");

        // Und der andere Weg: derselbe Stapel, aber im Anzeigeraum gemischt. Das ist
        // die Probe darauf, dass der Schalter nicht nur in der Mischformel ankommt,
        // sondern durch den ganzen Composer - vier Stellen mischen dort.
        double photoshop = Build(dirty, display: true);

        Console.WriteLine($"         im Anzeigeraum gemischt: {photoshop:0.00}");

        Check.That(photoshop < faint / 2,
                   "im Anzeigeraum kommt deutlich weniger durch",
                   $"{photoshop:0.00} gegen {faint:0.00}");
    }

    /// <summary>Eine VERSCHOBENE freigestellte Ebene darf den Untergrund nicht rauschen lassen.</summary>
    private static void PlacedCutoutStaysClean(string folder)
    {
        Check.Group("Eine verschobene freigestellte Ebene bleibt sauber");

        var ground = Read(folder, "00_grundbild.png");
        var figure = Read(folder, "02_freigestellt_muell_unter_deckung.png");

        if (ground is null || figure is null) return;

        var stack = Base(ground);

        // Verschoben und etwas kleiner - genau das macht aus ihr eine "platzierte"
        // Ebene und schickt sie durch den anderen Zweig des Composers.
        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Image,
            Source = "figur",
            Name = "Figur",
            Mode = BlendMode.Normal,
            Place = new LayerTransform { OffsetX = 0.08f, OffsetY = -0.04f, Scale = 0.9f },
        });

        var drawn = Draw(stack, Sources(ground, ("figur", figure)));
        if (drawn is null) return;

        Check.That(Noise(drawn, ground.Width, 2, 2, 40) < 2.0,
                   "in der Ecke rauscht nichts", $"{Noise(drawn, ground.Width, 2, 2, 40):0.00}");
    }

    /// <summary>Eine Glanzebene obenauf auf Screen darf es auch nicht.</summary>
    private static void ScreenOnTopStaysClean(string folder)
    {
        Check.Group("Eine Glanzebene obenauf bleibt sauber");

        var ground = Read(folder, "00_grundbild.png");
        var glare = Read(folder, "13_wasserzeichen_muell_unter_deckung.png");

        if (ground is null || glare is null) return;

        var stack = Base(ground);

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Image,
            Source = "glanz",
            Name = "Glanz",
            Mode = BlendMode.Screen,
            OnTop = true,
        });

        var drawn = Draw(stack, Sources(ground, ("glanz", glare)));
        if (drawn is null) return;

        Check.That(Noise(drawn, ground.Width, 2, 2, 40) < 2.0,
                   "in der Ecke rauscht nichts", $"{Noise(drawn, ground.Width, 2, 2, 40):0.00}");
    }

    /// <summary>Und der ganze gemeldete Stapel auf einmal.</summary>
    private static void TheWholeReportedStack(string folder)
    {
        Check.Group("Der gemeldete Stapel bleibt sauber");

        var ground = Read(folder, "00_grundbild.png");
        var figure = Read(folder, "02_freigestellt_muell_unter_deckung.png");
        var second = Read(folder, "05_freigestellt_harte_kante.png");
        var glare = Read(folder, "13_wasserzeichen_muell_unter_deckung.png");

        if (ground is null || figure is null || second is null || glare is null) return;

        var stack = Base(ground);

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Image, Source = "figur", Name = "Figur",
            Mode = BlendMode.Normal,
            Place = new LayerTransform { OffsetX = 0.08f, OffsetY = -0.04f, Scale = 0.9f },
        });

        stack.Layers.Add(new ImageLayer
        {
            Content = LayerContent.Image, Source = "zweite", Name = "Zweite",
            Mode = BlendMode.Normal,
        });

        foreach (string name in new[] { "glanz", "glanz2" })
        {
            stack.Layers.Add(new ImageLayer
            {
                Content = LayerContent.Image, Source = name, Name = name,
                Mode = BlendMode.Screen, OnTop = true,
            });
        }

        var sources = Sources(ground, ("figur", figure), ("zweite", second),
                              ("glanz", glare), ("glanz2", glare));

        foreach (int step in new[] { 1, 4 })
        {
            var drawn = Draw(stack, sources, step);
            if (drawn is null) continue;

            double noise = Noise(drawn, ground.Width, 2, 2, 40);

            Check.That(noise < 2.0, $"bei Schrittweite {step} rauscht die Ecke nicht",
                       $"{noise:0.00}");
        }
    }

    /// <summary>
    /// Der gemeldete Aufbau, gebaut nach den ZAHLEN der echten Dateien.
    ///
    /// Die Dateien selbst bekomme ich nicht zu sehen, ihre Statistik schon - und die
    /// enthaelt drei Befunde, die zusammen alles erklaeren, was gemeldet wurde:
    ///
    ///   Die Figurenebene hat KEINEN EINZIGEN ganz durchsichtigen Punkt. 59 Prozent
    ///   liegen dazwischen. Wo sie frei aussieht, ist sie es nicht.
    ///
    ///   Unter der Freistellung des Renders steht Muell mit Mittel 19/30/34 und
    ///   Nachbarabstand 32 - das ist kein Schleier, das ist ein Bild.
    ///
    ///   Und die beiden Glanzebenen sind gar nicht freigestellt: hundert Prozent
    ///   Deckung. Damit steht in den freien Stellen nicht mehr "nichts", sondern
    ///   "Glanz ueber dem, was dort steht" - und was dort steht, ist der Muell.
    ///
    /// Gerechnet wird kleiner als 3840x2160; nur die Verhaeltnisse zaehlen.
    /// </summary>
    private static void ByTheNumbersOfTheRealFiles()
    {
        Check.Group("Der gemeldete Aufbau nach den Zahlen der echten Dateien");

        const int w = 640, h = 360;

        var random = new Random(7);

        static float L(double b) => Srgb.Decode((float)Math.Clamp(b / 255.0, 0.0, 1.0));

        var render = Blank(w, h);
        var figure = Blank(w, h);
        var glare = Blank(w, h);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;

                // Der obere Viertelstreifen ist die freigestellte Stelle.
                bool free = y < h / 4;

                // Render: dort Deckung genau null, darunter Muell 19/30/34 mit
                // Nachbarabstand 32.
                render.R[i] = L(19 + random.NextDouble() * 32 - 16);
                render.G[i] = L(30 + random.NextDouble() * 32 - 16);
                render.B[i] = L(34 + random.NextDouble() * 32 - 16);
                render.A[i] = free ? 0f : 1f;

                if (!free)
                {
                    render.R[i] = L(90);
                    render.G[i] = L(95);
                    render.B[i] = L(100);
                }

                // Figur: NIE ganz durchsichtig - das ist der Befund, um den es geht.
                figure.R[i] = L(12 + random.NextDouble() * 32 - 16);
                figure.G[i] = L(13 + random.NextDouble() * 32 - 16);
                figure.B[i] = L(14 + random.NextDouble() * 32 - 16);
                figure.A[i] = free ? (float)(1 + random.NextDouble() * 5) / 255f : 1f;

                // Glanz: deckt ueberall, ist fast ueberall schwarz.
                glare.R[i] = glare.G[i] = glare.B[i] = 0f;
                glare.A[i] = 1f;
            }
        }

        double Run(bool withGlare, bool display = false, float matte = 0f,
                   LayerTransform? place = null)
        {
            var stack = new LayerStack
            {
                Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Bild" } },
            };

            foreach (string name in new[] { "render", "figur" })
            {
                stack.Layers.Add(new ImageLayer
                {
                    Content = LayerContent.Image, Source = name, Name = name,
                    Mode = BlendMode.Normal, BlendInDisplay = display, MatteFloor = matte,
                    Place = name == "figur" && place is not null ? place : new LayerTransform(),
                });
            }

            if (withGlare)
            {
                stack.Layers.Add(new ImageLayer
                {
                    Content = LayerContent.Image, Source = "glanz", Name = "Glanz",
                    Mode = BlendMode.Screen, OnTop = true,
                    BlendInDisplay = display, MatteFloor = matte,
                });
            }

            var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
            {
                [""] = Blank(w, h),
                ["render"] = render,
                ["figur"] = figure,
                ["glanz"] = glare,
            };

            var drawn = Draw(stack, sources);

            return drawn is null ? -1 : Noise(drawn, w, 2, 2, 40);
        }

        double ohne = Run(withGlare: false);
        double mit = Run(withGlare: true);
        double alsPs = Run(withGlare: true, display: true);
        double gesaeubert = Run(withGlare: true, matte: 0.04f);

        Console.WriteLine($"         ohne Glanzebene:        {ohne:0.00}");
        Console.WriteLine($"         mit Glanzebene:         {mit:0.00}");
        Console.WriteLine($"         mit Glanz, Modus wie PS:{alsPs:0.00}");
        Console.WriteLine($"         mit Glanz, Matte 10/255:{gesaeubert:0.00}");

        // Und jetzt dasselbe, aber die Figur VERSCHOBEN - so, wie es gemeldet wurde.
        //
        // Ganze Bildpunkte kosten nichts: Der Abtaster landet genau auf der Punktreihe
        // und interpoliert nicht. Bruchteile dagegen mischen vier Nachbarn - und er
        // mischt Farbe und Deckung GETRENNT. Wo unter einer unsauberen Deckung Muell
        // steht, ist das nicht dasselbe wie richtig gemischt, und die Differenz ist
        // von Punkt zu Punkt zufaellig. Das ist Rauschen.
        double ganz = Run(withGlare: true, place: new LayerTransform { OffsetY = 10f / h });
        double halb = Run(withGlare: true, place: new LayerTransform { OffsetY = 10.5f / h });
        double klein = Run(withGlare: true, place: new LayerTransform { OffsetY = 10.5f / h, Scale = 0.97f });

        Console.WriteLine($"         verschoben um ganze Punkte:  {ganz:0.00}");
        Console.WriteLine($"         verschoben um halbe Punkte:  {halb:0.00}");
        Console.WriteLine($"         verschoben und kleingezogen: {klein:0.00}");

        // WAS DIESER TEST SAGT - und was nicht.
        //
        // Er reproduziert das gemeldete Rauschen NICHT. Schwache Deckung ueber Muell,
        // eine deckende Glanzebene auf Screen darueber, verschoben um ganze und um
        // halbe Punkte: alles bleibt unter einer halben Stufe. Das ist ein Ergebnis
        // und kein Fehlschlag - es schliesst diese Erklaerung aus, und ich hatte sie
        // zweimal fuer die richtige gehalten.
        //
        // Was fehlt, ist die wirkliche Verteilung der Deckung in den gemeldeten
        // Dateien. "59 Prozent dazwischen" kann ein sauberer weicher Rand sein oder
        // eine Ebene, die ueberall halb da ist; das eine ist harmlos, das andere legt
        // einen Schleier ueber alles. Solange das nicht gemessen ist, raet dieser Test.
        //
        // Er bleibt trotzdem stehen: Er haelt fest, dass FrameFlip diesen Fall
        // beherrscht, und faellt um, wenn jemand ihn spaeter kaputt macht.
        Check.That(mit < 1.0,
                   "schwache Deckung unter deckendem Glanz bleibt ruhig",
                   $"{mit:0.00}");

        Check.That(halb < 1.0, "auch um halbe Bildpunkte verschoben", $"{halb:0.00}");
    }

    /// <summary>Ein leeres Feld in Bildgroesse - Schwarz, ganz durchsichtig.</summary>
    private static FloatFrame Blank(int w, int h) => new()
    {
        Width = w, Height = h,
        R = new float[w * h], G = new float[w * h], B = new float[w * h], A = new float[w * h],
        IsSceneReferred = false,
    };

    // ------------------------------------------------------------------- Handwerk

    private static LayerStack Base(FloatFrame ground)
        => new() { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Bild" } } };

    private static Dictionary<string, FloatFrame> Sources(
        FloatFrame ground, params (string Key, FloatFrame Frame)[] rest)
    {
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal) { [""] = ground };

        foreach (var (key, frame) in rest) sources[key] = frame;

        return sources;
    }

    /// <summary>
    /// Wie stark benachbarte Punkte in einem Ausschnitt auseinanderliegen.
    ///
    /// Auf einem glatten Untergrund ist das nahe null. Rauschen ist genau der
    /// Gegensatz dazu, und es laesst sich so mit einer Zahl fassen statt mit einem
    /// Blick.
    /// </summary>
    private static double Noise(byte[] pixels, int width, int x0, int y0, int size)
    {
        double sum = 0;
        int count = 0;

        for (int y = y0; y < y0 + size; y++)
        {
            for (int x = x0; x < x0 + size - 1; x++)
            {
                int at = (y * width + x) * 4;

                sum += Math.Abs(pixels[at + 4] - pixels[at]);
                sum += Math.Abs(pixels[at + 5] - pixels[at + 1]);
                sum += Math.Abs(pixels[at + 6] - pixels[at + 2]);

                count += 3;
            }
        }

        return count > 0 ? sum / count : 0;
    }

    private static byte[]? Draw(LayerStack stack, Dictionary<string, FloatFrame> sources, int step = 1)
    {
        var frame = LayerComposer.Compose(stack, sources, null, step);
        if (frame is null) return null;

        var overlays = Overlays.Prepare(stack, sources, frame.Width, frame.Height);

        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];

        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, new StandardViewTransform(),
                                      PreparedGrading.None, buffer, stride, step, overlays);

            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static FloatFrame? Read(string folder, string name)
    {
        string path = Path.Combine(folder, name);

        return LayeredFrameLoader.Read(new LayerRead(path, LayerContent.Image, false), path);
    }

    private static string? Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName,
                                            "FrameFlip-Testsequenzen", "png_ebenen");

            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
