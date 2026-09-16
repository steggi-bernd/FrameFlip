using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace FrameFlip.Tests;

/// <summary>
/// Die beiden letzten Ebenenarten: eine andere Datei, und mehrere Ebenen unter einer
/// Maske.
/// </summary>
public static class ImageAndGroupInvariants
{
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(),
                                     "frameflip-bildebenen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            NumbersAreFound(folder);
            PairingFollowsTheSequence(folder);
            AnImageLayerIsRead(folder);
            TwoVersionsOnDifference(folder);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { /* ein liegengebliebener Rest ist kein Testfehler */ }
        }

        AGroupWithoutSettingsIsInvisible();
        AGroupCarriesItsChildren();
        AGroupSeesWhatIsBelow();
        NestingHasALimit();
        GroupPersistence();
    }

    // --------------------------------------------------------------- Bildebenen

    private static void NumbersAreFound(string folder)
    {
        Check.Group("Die Bildnummer wird gelesen");

        Check.That(SequenceLink.NumberOf(Path.Combine(folder, "render_0047.exr")) == 47,
                   "eine gepolsterte Nummer");
        Check.That(SequenceLink.NumberOf(Path.Combine(folder, "bild7.png")) == 7,
                   "und eine ungepolsterte");

        // Ein Logo hat keine Nummer - und soll auch keine bekommen.
        Check.That(SequenceLink.NumberOf(Path.Combine(folder, "logo.png")) is null,
                   "ein Name ohne Ziffern hat keine");

        // Die LETZTE Zifferngruppe zaehlt, wie ueberall sonst im Programm auch.
        Check.That(SequenceLink.NumberOf(Path.Combine(folder, "szene2_0009.exr")) == 9,
                   "bei zwei Gruppen zaehlt die letzte");
    }

    private static void PairingFollowsTheSequence(string folder)
    {
        Check.Group("Eine Bildebene laeuft mit der Nummer mit");

        // Zwei Fassungen derselben Sequenz.
        for (int frame = 1; frame <= 3; frame++)
        {
            Write(Path.Combine(folder, $"alt_{frame:0000}.png"), 100);
            Write(Path.Combine(folder, $"neu_{frame:0000}.png"), 140);
        }

        string image = Path.Combine(folder, "neu_0001.png");

        string paired = SequenceLink.Pair(image, Path.Combine(folder, "alt_0003.png"), follow: true);
        Check.That(Path.GetFileName(paired) == "neu_0003.png",
                   "bei Bild 3 wird auch dort Bild 3 genommen", Path.GetFileName(paired));

        // Angepinnt bleibt sie stehen, wo sie steht.
        string pinned = SequenceLink.Pair(image, Path.Combine(folder, "alt_0003.png"), follow: false);
        Check.That(Path.GetFileName(pinned) == "neu_0001.png", "angepinnt bleibt sie beim gewaehlten Bild");

        // Ein Einzelbild bleibt ein Einzelbild.
        string logo = Path.Combine(folder, "logo.png");
        Write(logo, 200);

        Check.That(SequenceLink.Pair(logo, Path.Combine(folder, "alt_0003.png"), follow: true) == logo,
                   "ein Name ohne Nummer bleibt unveraendert");

        // Ist die zweite Fassung kuerzer, bleibt das gewaehlte Bild stehen - ein
        // schwarzes Loch waere die formal richtige Antwort und die unbrauchbare.
        string beyond = SequenceLink.Pair(image, Path.Combine(folder, "alt_0099.png"), follow: true);
        Check.That(Path.GetFileName(beyond) == "neu_0001.png",
                   "fehlt das Gegenstueck, bleibt das gewaehlte stehen", Path.GetFileName(beyond));
    }

    private static void AnImageLayerIsRead(string folder)
    {
        Check.Group("Eine Bildebene wird aus ihrer eigenen Datei gelesen");

        string baseImage = Path.Combine(folder, "grund_0001.png");
        string overlay = Path.Combine(folder, "auflage_0001.png");

        Write(baseImage, 60);
        Write(overlay, 180);

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = overlay,
                    Mode = BlendMode.Normal,
                    Name = "Auflage",
                },
            },
        };

        // Die Leseliste muss BEIDE kennen - das Bild selbst und die fremde Datei.
        var reads = stack.Reads();
        Check.That(reads.Count == 2, "zwei Quellen", $"{reads.Count}");
        Check.That(reads.Any(r => r.Kind == LayerContent.Image && r.Key == overlay),
                   "darunter die fremde Datei");

        var built = LayeredFrameLoader.Load(baseImage, stack);
        var plain = LayeredFrameLoader.Load(baseImage, null);

        Check.That(built is not null && plain is not null, "beide lassen sich lesen");
        if (built is null || plain is null) return;

        // Oben liegt die hellere Datei, auf Normal - also gilt sie.
        Check.That(built.R[0] > plain.R[0] * 2f, "die obere Datei gilt",
                   $"{built.R[0]:0.###} statt {plain.R[0]:0.###}");

        // Eine Datei, die es nicht gibt, nimmt nur ihre eigene Ebene weg.
        stack.Layers[1].Source = Path.Combine(folder, "gibtsnicht.png");
        var fallback = LayeredFrameLoader.Load(baseImage, stack);

        Check.That(fallback is not null, "eine fehlende Datei ergibt trotzdem ein Bild");
        Check.Near(fallback!.R[0], plain.R[0], 1e-4, "naemlich das ohne sie");
    }

    /// <summary>
    /// Der Fall, der die Bildebene rechtfertigt: zwei Fassungen auf Differenz.
    /// </summary>
    private static void TwoVersionsOnDifference(string folder)
    {
        Check.Group("Zwei Fassungen auf Differenz zeigen den Unterschied");

        string oldVersion = Path.Combine(folder, "fassung_0001.png");
        string newVersion = Path.Combine(folder, "andere_0001.png");

        Write(oldVersion, 120);
        Write(newVersion, 120);

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Content = LayerContent.Image,
                    Source = newVersion,
                    Mode = BlendMode.Difference,
                },
            },
        };

        var same = LayeredFrameLoader.Load(oldVersion, stack)!;
        Check.Near(same.R[0], 0.0, 1e-4, "zwei gleiche Fassungen ergeben Schwarz");

        // Und eine, die sich unterscheidet, zeigt es.
        Write(newVersion, 180);
        var changed = LayeredFrameLoader.Load(oldVersion, stack)!;

        Check.That(changed.R[0] > 0.01f, "ein Unterschied wird sichtbar", $"{changed.R[0]:0.####}");
    }

    // ----------------------------------------------------------------- Gruppen

    /// <summary>
    /// Die Probe, die eine Gruppe bestehen muss: ohne Einstellungen ist sie
    /// unsichtbar.
    ///
    /// Das ist der Unterschied zwischen einer Gruppe, die durchrechnet, und einer,
    /// die abschottet. Abgeschottet faenden ihre Kinder Schwarz vor - eine Gruppe um
    /// eine Korrektur herum wuerde das Bild loeschen, und niemand kaeme darauf, dass
    /// die Gruppe schuld ist.
    /// </summary>
    private static void AGroupWithoutSettingsIsInvisible()
    {
        Check.Group("Eine Gruppe ohne Einstellungen ist nicht zu bemerken");

        var sources = Sources();

        var flat = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                Pass("glanz", BlendMode.Add),
                Adjustment(1.0),
            },
        };

        var grouped = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Mode = BlendMode.Normal,
                    Name = "Gruppe",
                    Children = { Pass("glanz", BlendMode.Add), Adjustment(1.0) },
                },
            },
        };

        double open = LayerComposer.Compose(flat, sources)!.R[0];
        double inside = LayerComposer.Compose(grouped, sources)!.R[0];

        Check.Near(inside, open, 1e-4, "dasselbe wie ohne Gruppe");
        Check.That(open > 1.5, "und es ist wirklich etwas passiert", $"{open:0.###}");

        // Eine leere Gruppe ebenso.
        var empty = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                new ImageLayer { Content = LayerContent.Group, Mode = BlendMode.Normal, Children = { } },
            },
        };

        Check.Near(LayerComposer.Compose(empty, sources)!.R[0], 1.0, 1e-4,
                   "eine leere Gruppe aendert nichts");
    }

    private static void AGroupCarriesItsChildren()
    {
        Check.Group("Die Gruppe traegt fuer alle ihre Kinder");

        var sources = Sources();

        var stack = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Mode = BlendMode.Normal,
                    Name = "Gruppe",

                    // Zwei Korrekturen: zusammen das Vierfache.
                    Children = { Adjustment(1.0), Adjustment(1.0) },
                },
            },
        };

        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 4.0, 1e-4,
                   "beide Korrekturen wirken");

        // Die Deckkraft der Gruppe mischt das GANZE Ergebnis zurueck - nicht jede
        // Korrektur einzeln. Halb zwischen 1 und 4 ist 2,5.
        stack.Layers[1].Opacity = 0.5f;
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 2.5, 1e-4,
                   "und die Deckkraft gilt fuer beide zusammen");

        // Eine Maske auf der Gruppe ebenso - das ist ihr eigentlicher Zweck.
        stack.Layers[1].Opacity = 1f;
        stack.Layers[1].Mask = new LayerMask { Kind = MaskKind.Gradient, Angle = 0f, Width = 0f };

        var masked = LayerComposer.Compose(stack, sources)!;
        Check.Near(masked.R[0], 1.0, 1e-4, "links wirkt keine der beiden");
        Check.Near(masked.R[7], 4.0, 1e-4, "rechts beide");

        // Die Leseliste muss in die Gruppe hineinsehen.
        var withPass = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Children = { Pass("glanz", BlendMode.Normal) },
                },
            },
        };

        Check.That(withPass.NeededSources().Contains("glanz"),
                   "und ein Pass in der Gruppe steht auf der Leseliste",
                   string.Join(", ", withPass.NeededSources()));

        Check.That(withPass.All().Count() == 2, "alle Ebenen sind zu finden",
                   $"{withPass.All().Count()}");
    }

    private static void AGroupSeesWhatIsBelow()
    {
        Check.Group("Eine Gruppe sieht, was unter ihr liegt");

        var sources = Sources();

        // Eine Gruppe, die NUR eine Korrektur enthaelt. Abgeschottet faende sie
        // Schwarz vor und ergaebe Schwarz; durchgerechnet verdoppelt sie.
        var stack = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Mode = BlendMode.Normal,
                    Children = { Adjustment(1.0) },
                },
            },
        };

        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 2.0, 1e-4,
                   "die Korrektur greift auf das Bild darunter");

        // Eine Schnittmaske INNERHALB der Gruppe bleibt darin: Sie schneidet sich an
        // die Ebene darunter an, nicht an etwas ausserhalb.
        var clipped = new LayerStack
        {
            Layers =
            {
                Pass("grund", BlendMode.Normal),
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Mode = BlendMode.Normal,
                    Children = { Pass("glanz", BlendMode.Add), Clipped(1.0) },
                },
            },
        };

        // 1 + (2 * 2) = 5 - die Korrektur trifft nur den Glanz, nicht den Grund.
        Check.Near(LayerComposer.Compose(clipped, sources)!.R[0], 5.0, 1e-4,
                   "eine Schnittmaske in der Gruppe bleibt in der Gruppe");
    }

    private static void NestingHasALimit()
    {
        Check.Group("Die Schachtelung hat eine Grenze");

        var sources = Sources();

        // Zwoelf Gruppen ineinander - mehr als erlaubt. Der Inhalt darf trotzdem
        // nicht verschwinden.
        var inner = new ImageLayer
        {
            Content = LayerContent.Group,
            Mode = BlendMode.Normal,
            Children = { Adjustment(1.0) },
        };

        for (int i = 0; i < 11; i++)
        {
            inner = new ImageLayer
            {
                Content = LayerContent.Group,
                Mode = BlendMode.Normal,
                Children = { inner },
            };
        }

        var stack = new LayerStack
        {
            Layers = { Pass("grund", BlendMode.Normal), inner },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt trotzdem ein Bild heraus");

        if (built is not null)
        {
            Check.Near(built.R[0], 2.0, 1e-4,
                       "und die Korrektur ganz innen wirkt weiter");
        }
    }

    private static void GroupPersistence()
    {
        Check.Group("Eine Gruppe ueberlebt das Speichern");

        var stack = new LayerStack
        {
            Layers =
            {
                Pass("ViewLayer.Combined", BlendMode.Normal),
                new ImageLayer
                {
                    Content = LayerContent.Group,
                    Name = "Nur das Objekt",
                    Opacity = 0.7f,
                    Mask = new LayerMask { Kind = MaskKind.Luminance, Low = 0.4f },
                    Children =
                    {
                        Adjustment(0.5),
                        new ImageLayer
                        {
                            Content = LayerContent.Image,
                            Source = @"C:\woanders\logo.png",
                            FollowSequence = false,
                        },
                    },
                },
            },
        };

        var read = JsonSerializer.Deserialize<LayerStack>(JsonSerializer.Serialize(stack));
        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        var group = read.Layers[1];
        Check.That(group.Content == LayerContent.Group, "die Gruppe ist eine Gruppe");
        Check.That(group.Children.Count == 2, "mit beiden Kindern",
                   $"{group.Children.Count}");
        Check.Near(group.Opacity, 0.7, 1e-5, "die Deckkraft bleibt");
        Check.That(group.Mask.Kind == MaskKind.Luminance, "die Maske auch");

        var image = group.Children[1];
        Check.That(image.Content == LayerContent.Image, "das Kind ist eine Bildebene");
        Check.That(!image.FollowSequence, "und bleibt angepinnt");

        // Kopieren muss tief sein - bis in die Gruppe hinein.
        var copy = stack.Clone();
        stack.Layers[1].Children[0].Adjustments = ImageAdjustments.Neutral with { Exposure = 9 };

        Check.Near(copy.Layers[1].Children[0].Adjustments!.Exposure, 0.5, 1e-5,
                   "eine Kopie bewegt sich auch in der Gruppe nicht mit");
    }

    // ------------------------------------------------------------------- Handwerk

    private static Dictionary<string, FloatFrame> Sources() => new(StringComparer.Ordinal)
    {
        ["grund"] = Frame(8, 2, 1f),
        ["glanz"] = Frame(8, 2, 2f),
    };

    private static ImageLayer Pass(string source, BlendMode mode)
        => new() { Source = source, Mode = mode, Name = source };

    private static ImageLayer Adjustment(double exposure) => new()
    {
        Content = LayerContent.Adjustment,
        Mode = BlendMode.Normal,
        Name = "Korrektur",
        Adjustments = ImageAdjustments.Neutral with { Exposure = exposure },
        Tools = new GradingStack(),
    };

    private static ImageLayer Clipped(double exposure)
    {
        var layer = Adjustment(exposure);
        layer.Clipped = true;

        return layer;
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
        };
    }

    /// <summary>Ein einfarbiges PNG mit dem gegebenen Grauwert.</summary>
    private static void Write(string path, byte grey)
    {
        var pixels = new byte[4 * 2 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 16);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var file = File.Create(path);
        encoder.Save(file);
    }
}
