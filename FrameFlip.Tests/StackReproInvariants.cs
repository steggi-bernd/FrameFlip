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
