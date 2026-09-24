using System.IO;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Freigestellte Bildebenen - an echten Dateien, nicht an gerechneten Feldern.
///
/// Der Fehler, um den es hier geht, laesst sich synthetisch nicht finden: Er haengt
/// an dem, was in den DURCHSICHTIGEN Stellen einer PNG steht, und das sieht man einer
/// Datei nicht an. Viele Programme schreiben dort, was zufaellig im Puffer stand.
/// Wer die Deckung nicht beachtet, zeigt genau diesen Muell - als pixeligen Nebel im
/// Farbschema des Bildes, weil der Muell aus demselben Puffer stammt.
///
/// Die Dateien dazu stehen in FrameFlip-Testsequenzen/png_ebenen und werden von
/// werkzeug/pngs_bauen.py erzeugt. Sie decken die Faelle ab, die in freier Wildbahn
/// vorkommen: Muell unter der Deckung, Schwarz darunter, vormultipliziert, harte
/// Kante, Graustufen, Palette, sechzehn Bit, verschraenkt.
/// </summary>
public static class PngLayerInvariants
{
    public static void Run()
    {
        string? folder = Find();

        if (folder is null)
        {
            Check.Group("Freigestellte PNG-Ebenen");
            Check.That(false, "die Testbilder liegen bereit",
                       "FrameFlip-Testsequenzen/png_ebenen fehlt - werkzeug/pngs_bauen.py erzeugt sie");
            return;
        }

        EveryFileReads(folder);
        SixteenBitArrivesLikeEightBit(folder);
        TransparentStaysTransparent(folder);
        OpaqueStaysOpaque(folder);
    }

    /// <summary>
    /// Sechzehn Bit muss dasselbe ergeben wie acht Bit.
    ///
    /// Die Dateien 10 und 11 tragen DIESELBE Deckung - die eine mit sechzehn Bit je
    /// Kanal, die andere mit acht. Beide laufen durch Windows' Decoder und werden
    /// dabei auf Bgra32 gebracht, aber ueber verschiedene Wege: acht Bit geht
    /// durch, sechzehn Bit durch einen Formatwandler.
    ///
    /// Geprueft wird die Deckung und nicht die Farbe: Bei der Farbe ist ein Unterschied
    /// von einer Stufe Rundung, bei der Deckung ist er ein Fehler. Und es ist die
    /// Deckung, an der alles haengt - eine Ebene, deren Schleier statt bei 6 von 255
    /// bei 60 ankommt, zeigt den Muell darunter zehnfach.
    /// </summary>
    private static void SixteenBitArrivesLikeEightBit(string folder)
    {
        Check.Group("Sechzehn Bit kommt an wie acht Bit");

        var tief = Read(folder, "10_sechzehn_bit_mit_deckung.png");
        var flach = Read(folder, "11_verschraenkt.png");

        if (tief is null || flach is null || tief.A is null || flach.A is null) return;

        double worst = 0;
        double sum = 0;

        for (int i = 0; i < tief.A.Length; i++)
        {
            double step = Math.Abs(tief.A[i] - flach.A[i]) * 255.0;

            worst = Math.Max(worst, step);
            sum += step;
        }

        double mean = sum / tief.A.Length;

        Console.WriteLine($"         Deckung: groesster Unterschied {worst:0.0} von 255, " +
                          $"im Mittel {mean:0.00}");

        Check.That(worst <= 1.5, "die Deckung kommt gleich an", $"{worst:0.0} von 255");

        // Und die Farbe ebenso - dort ist mehr Spielraum, aber nicht beliebig viel.
        double colour = 0;

        for (int i = 0; i < tief.R.Length; i++)
        {
            colour = Math.Max(colour, Math.Abs(Srgb.Encode(tief.R[i]) -
                                               Srgb.Encode(flach.R[i])) * 255.0);
        }

        Console.WriteLine($"         Farbe:   groesster Unterschied {colour:0.0} von 255");
    }

    /// <summary>
    /// Jede der Dateien muss ueberhaupt gelesen werden.
    ///
    /// "Manche PNGs werden gar nicht angezeigt" faengt hier an: Palette, Graustufen,
    /// sechzehn Bit und verschraenkt sind vier verschiedene Wege durch den Decoder,
    /// und drei davon kommen in Blender-Ausgaben seltener vor als in dem, was jemand
    /// aus Photoshop danebenlegt.
    /// </summary>
    private static void EveryFileReads(string folder)
    {
        Check.Group("Jede Art PNG laesst sich lesen");

        foreach (string path in Directory.GetFiles(folder, "*.png").OrderBy(p => p))
        {
            var frame = LayeredFrameLoader.Read(new LayerRead(path, LayerContent.Image, false), path);

            Check.That(frame is not null, Path.GetFileNameWithoutExtension(path));
        }
    }

    /// <summary>
    /// Die Probe, um die es geht: Wo eine Bildebene durchsichtig ist, muss der
    /// Untergrund unveraendert stehenbleiben - Byte fuer Byte.
    ///
    /// Geprueft wird mit der Datei, die unter der Deckung Rauschen traegt. Bliebe die
    /// Deckung unbeachtet, stuende genau dieses Rauschen im Ergebnis, und es saehe
    /// aus wie ein Fehler des Bildes statt wie einer des Programms.
    /// </summary>
    private static void TransparentStaysTransparent(string folder)
    {
        Check.Group("Durchsichtig heisst durchsichtig");

        var ground = Read(folder, "00_grundbild.png");
        if (ground is null) return;

        // ALLE Arten, nicht nur die mit acht Bit. Palette, Graustufen, sechzehn Bit
        // und verschraenkt nehmen im Decoder je einen anderen Weg, und ein Alphakanal,
        // der auf einem davon falsch ankommt, faellt nur dort auf.
        foreach (string name in new[]
                 {
                     "02_freigestellt_muell_unter_deckung.png",
                     "03_freigestellt_schwarz_unter_deckung.png",
                     "05_freigestellt_harte_kante.png",
                     "08_graustufen_mit_deckung.png",
                     "09_palette_mit_transparenz.png",
                     "10_sechzehn_bit_mit_deckung.png",
                     "11_verschraenkt.png",
                     "13_wasserzeichen_muell_unter_deckung.png",
                 })
        {
            var layer = Read(folder, name);
            if (layer is null) continue;

            var composed = Compose(ground, layer, Path.Combine(folder, name));
            if (composed is null) continue;

            // Die vier Ecken liegen bei allen vier Dateien ausserhalb der Deckung.
            int worst = 0;

            foreach (var (x, y) in new[] { (2, 2), (ground.Width - 3, 2), (2, ground.Height - 3) })
            {
                int i = y * ground.Width + x;

                worst = Math.Max(worst, (int)MathF.Round(MathF.Abs(composed.R[i] - ground.R[i]) * 255f));
                worst = Math.Max(worst, (int)MathF.Round(MathF.Abs(composed.G[i] - ground.G[i]) * 255f));
                worst = Math.Max(worst, (int)MathF.Round(MathF.Abs(composed.B[i] - ground.B[i]) * 255f));
            }

            Check.That(worst == 0, $"{Path.GetFileNameWithoutExtension(name)}: der Untergrund bleibt",
                       $"groesste Abweichung {worst} von 255");
        }

        // Und die eine Datei, die NICHT byteweise stehenbleiben darf.
        //
        // Ihr durchsichtiger Bereich ist absichtlich nicht ganz durchsichtig - ein paar
        // Stufen Deckung, wie sie eine Freistellung aus der Hand hinterlaesst. Dass
        // davon etwas ankommt, ist richtig; sie hier zu den anderen zu stellen war mein
        // Fehler und nicht der des Programms. Was geprueft wird, ist die Groesse: Ein
        // paar Stufen sind die Datei, dreissig waeren die Rechnung.
        var faint = Read(folder, "14_fast_durchsichtig_mit_rauschen.png");

        if (faint is not null)
        {
            var composed = Compose(ground, faint,
                                   Path.Combine(folder, "14_fast_durchsichtig_mit_rauschen.png"));

            if (composed is not null)
            {
                int worst = 0;

                foreach (var (x, y) in new[] { (2, 2), (ground.Width - 3, 2), (2, ground.Height - 3) })
                {
                    int i = y * ground.Width + x;

                    worst = Math.Max(worst, (int)MathF.Round(
                        MathF.Abs(composed.R[i] - ground.R[i]) * 255f));
                }

                Check.That(worst <= 8,
                           "die absichtlich unsaubere Datei laesst wenig durch, nicht viel",
                           $"groesste Abweichung {worst} von 255");
            }
        }
    }

    /// <summary>
    /// Und die Gegenprobe: Wo die Ebene deckt, muss sie auch zu sehen sein.
    ///
    /// Ohne sie waere die Regel "Deckung beachten" durch ein Werkzeug erfuellt, das
    /// gar nichts mehr zeigt.
    /// </summary>
    private static void OpaqueStaysOpaque(string folder)
    {
        Check.Group("Deckend heisst deckend");

        var ground = Read(folder, "00_grundbild.png");
        var layer = Read(folder, "02_freigestellt_muell_unter_deckung.png");

        if (ground is null || layer is null) return;

        var composed = Compose(ground, layer,
                               Path.Combine(folder, "02_freigestellt_muell_unter_deckung.png"));
        if (composed is null) return;

        int middle = ground.Height / 2 * ground.Width + ground.Width / 2;

        Check.Near(composed.R[middle], layer.R[middle], 0.002, "in der Mitte zeigt die Ebene sich");
        Check.That(MathF.Abs(composed.R[middle] - ground.R[middle]) > 0.01f,
                   "und nicht den Untergrund");

        // Eine ganz deckende Ebene mit Alphakanal verdeckt alles.
        var solid = Read(folder, "06_deckend_mit_alphakanal.png");
        if (solid is null) return;

        var over = Compose(ground, solid, Path.Combine(folder, "06_deckend_mit_alphakanal.png"));
        if (over is null) return;

        int corner = 2 * ground.Width + 2;

        Check.Near(over.R[corner], solid.R[corner], 0.002,
                   "eine deckende Ebene mit Alphakanal deckt auch in der Ecke");
    }

    // ------------------------------------------------------------------- Handwerk

    private static FloatFrame? Read(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        var frame = LayeredFrameLoader.Read(new LayerRead(path, LayerContent.Image, false), path);

        Check.That(frame is not null, $"{Path.GetFileNameWithoutExtension(name)} liest sich");

        return frame;
    }

    /// <summary>Untergrund unten, Bildebene darueber - der einfachste Stapel, den es gibt.</summary>
    private static FloatFrame? Compose(FloatFrame ground, FloatFrame layer, string layerPath)
    {
        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Bild" },
                new ImageLayer { Content = LayerContent.Image, Source = layerPath, Name = "Ebene" },
            },
        };

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            [""] = ground,
            [layerPath] = layer,
        };

        return LayerComposer.Compose(stack, sources);
    }

    /// <summary>Sucht die Testbilder vom Ausfuehrungsverzeichnis aus aufwaerts.</summary>
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
