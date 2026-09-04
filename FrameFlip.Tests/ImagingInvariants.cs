using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameFlip.Imaging;

namespace FrameFlip.Tests;

/// <summary>Die Anzeigekorrektur: Tonwertkurve, Kanalmischung, Histogramm, Tempo.</summary>
public static class ImagingInvariants
{
    public static void Run()
    {
        ToneCurve();
        PixelPipeline();
        HistogramMath();
        FfmpegMapping();
        Persistence();
        Performance();
    }

    // ---------------------------------------------------------------- Speichern

    /// <summary>
    /// Korrektur und Vorlagen muessen die Konfigurationsdatei ueberstehen. Ein
    /// record mit init-Eigenschaften ist fuer System.Text.Json nicht
    /// selbstverstaendlich - ohne diese Pruefung faende man den Verlust erst beim
    /// naechsten Start.
    /// </summary>
    private static void Persistence()
    {
        Check.Group("Korrektur ueberlebt das Speichern");

        var original = new FrameFlip.Configuration.AppSettings
        {
            Adjustments = new ImageAdjustments
            {
                Exposure = -1.25, Gamma = 1.4, Contrast = 1.1,
                Saturation = 0.8, BlackPoint = 0.05, WhitePoint = 0.95,
                Channel = ChannelView.Luminance,
            },
        };

        original.AdjustmentPresets.Add(new FrameFlip.Configuration.AdjustmentPreset
        {
            Name = "Nachtaufnahme",
            Adjustments = new ImageAdjustments { Exposure = 1.5, Gamma = 1.6 },
        });

        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<FrameFlip.Configuration.AppSettings>(json, options)!;

        Check.That(restored.Adjustments is not null, "die Korrektur ist nach dem Laden da");
        Check.Near(restored.Adjustments!.Exposure, -1.25, 1e-9, "Belichtung erhalten");
        Check.Near(restored.Adjustments.Gamma, 1.4, 1e-9, "Gamma erhalten");
        Check.Near(restored.Adjustments.WhitePoint, 0.95, 1e-9, "Weisspunkt erhalten");
        Check.That(restored.Adjustments.Channel == ChannelView.Luminance, "Kanalansicht erhalten",
            restored.Adjustments.Channel.ToString());

        Check.That(restored.AdjustmentPresets.Count == 1, "die Vorlage ist erhalten",
            $"{restored.AdjustmentPresets.Count}");
        Check.That(restored.AdjustmentPresets[0].Name == "Nachtaufnahme", "mit ihrem Namen");
        Check.Near(restored.AdjustmentPresets[0].Adjustments.Exposure, 1.5, 1e-9,
            "und ihren Werten");

        // Eine Konfiguration ohne diese Felder - also eine aeltere Datei - darf nicht
        // stolpern, sondern faellt auf die Vorgaben zurueck.
        var old = System.Text.Json.JsonSerializer.Deserialize<FrameFlip.Configuration.AppSettings>(
            """{"Hotkey":"Ctrl+Alt+Space","Fps":24}""", options)!;
        old.Normalize();

        Check.That(old.Adjustments is null, "eine aeltere Datei bringt keine Korrektur mit");
        Check.That(old.AdjustmentPresets.Count == 0, "und keine Vorlagen");
        Check.That(old.ExportApplyAdjustments is null,
            "die Exportfrage ist noch unbeantwortet und wird deshalb gestellt");
    }

    // ---------------------------------------------------------------- Tonwertkurve

    private static void ToneCurve()
    {
        Check.Group("Tonwertkurve");

        Check.That(ImageAdjustments.Neutral.IsNeutral, "die Vorgabe ist neutral");

        // Der Fall aus der Praxis: Regler bewegt und wieder zurueckgezogen. Die Werte
        // landen dann nicht exakt auf 0 und 1, sondern knapp daneben. Ohne Toleranz
        // gilt das als Korrektur und laesst bei jedem Bild den vollen Rechenweg
        // laufen - 9,6 statt 0,3 ms, bei 60 fps die halbe Zeit fuer nichts.
        var noise = new ImageAdjustments
        {
            Exposure = -9.51849710162378E-13,
            WhitePoint = 1.0000000000000238,
            Gamma = 0.9999999999995005,
            Contrast = 1.0000000000000973,
            Saturation = 1.0000000000000857,
        };

        Check.That(noise.IsNeutral, "Gleitkomma-Rauschen gilt nicht als Korrektur");
        Check.That(!noise.NeedsToneCurve, "und loest keine Tonwertkurve aus");
        Check.That(!noise.NeedsChannelMix, "und keine Kanalmischung");
        Check.That(noise.ToFfmpegFilter() is null, "und landet nicht im Export");

        // Clamped raeumt das Rauschen weg, damit es gar nicht erst gespeichert wird.
        var cleaned = noise.Clamped();
        Check.That(cleaned.Exposure == 0 && cleaned.Gamma == 1.0 && cleaned.Saturation == 1.0,
            "Clamped rastet auf die Ausgangswerte ein",
            $"EV {cleaned.Exposure}, γ {cleaned.Gamma}, S {cleaned.Saturation}");

        // Eine echte, kleine Korrektur darf dabei nicht verschluckt werden.
        var small = new ImageAdjustments { Gamma = 1.05 };
        Check.That(!small.IsNeutral, "eine echte kleine Korrektur bleibt erhalten");
        Check.That(small.Clamped().Gamma == 1.05, "und wird nicht weggerundet",
            $"{small.Clamped().Gamma}");

        // Die abgeleiteten Eigenschaften gehoeren nicht in die Konfigurationsdatei.
        var json = System.Text.Json.JsonSerializer.Serialize(ImageAdjustments.Neutral);
        Check.That(!json.Contains("IsNeutral") && !json.Contains("NeedsToneCurve"),
            "berechnete Eigenschaften werden nicht gespeichert", json);
        Check.That(!ImageAdjustments.Neutral.NeedsToneCurve, "und braucht keine Kurve");
        Check.That(!ImageAdjustments.Neutral.NeedsChannelMix, "und keine Kanalmischung");

        var identity = ImageAdjustments.Neutral.BuildToneCurve();
        bool unchanged = true;
        for (int i = 0; i < 256; i++) if (identity[i] != i) unchanged = false;
        Check.That(unchanged, "die neutrale Kurve bildet jeden Wert auf sich selbst ab");

        // Eine Blendenstufe verdoppelt die Lichtmenge.
        var brighter = new ImageAdjustments { Exposure = 1 }.BuildToneCurve();
        Check.That(brighter[64] == 128, "+1 EV verdoppelt den Mittelwert", $"{brighter[64]}");
        Check.That(brighter[200] == 255, "helle Werte laufen sauber in die Saettigung", $"{brighter[200]}");

        var darker = new ImageAdjustments { Exposure = -1 }.BuildToneCurve();
        Check.That(darker[128] == 64, "-1 EV halbiert", $"{darker[128]}");

        // Gamma hebt die Mitten, laesst die Enden stehen.
        var gamma = new ImageAdjustments { Gamma = 2.0 }.BuildToneCurve();
        Check.That(gamma[0] == 0 && gamma[255] == 255, "Gamma laesst Schwarz und Weiss unberuehrt");
        Check.That(gamma[128] > 128, "Gamma ueber 1 hellt die Mitten auf", $"{gamma[128]}");

        // Kontrast dreht um die Bildmitte.
        var contrast = new ImageAdjustments { Contrast = 2.0 }.BuildToneCurve();
        Check.That(contrast[128] is >= 127 and <= 128, "die Mitte bleibt stehen", $"{contrast[128]}");
        Check.That(contrast[64] < 64, "darunter wird dunkler", $"{contrast[64]}");
        Check.That(contrast[192] > 192, "darueber heller", $"{contrast[192]}");

        // Schwarz- und Weisspunkt spreizen den benutzten Bereich.
        var levels = new ImageAdjustments { BlackPoint = 0.25, WhitePoint = 0.75 }.BuildToneCurve();
        Check.That(levels[64] == 0, "unter dem Schwarzpunkt wird alles zu Schwarz", $"{levels[64]}");
        Check.That(levels[192] == 255, "ueber dem Weisspunkt alles zu Weiss", $"{levels[192]}");
        Check.That(levels[128] is >= 125 and <= 130, "die Mitte bleibt die Mitte", $"{levels[128]}");

        // Die Kurve ist monoton - eine Korrektur darf keine Tonwerte vertauschen.
        foreach (var adjustment in new[]
        {
            new ImageAdjustments { Exposure = 0.7 },
            new ImageAdjustments { Gamma = 0.45 },
            new ImageAdjustments { Contrast = 1.8 },
            new ImageAdjustments { BlackPoint = 0.1, WhitePoint = 0.9 },
            new ImageAdjustments { Exposure = -1.5, Gamma = 2.2, Contrast = 1.3 },
        })
        {
            var table = adjustment.BuildToneCurve();
            bool monotone = true;
            for (int i = 1; i < 256; i++) if (table[i] < table[i - 1]) monotone = false;
            Check.That(monotone, $"monoton bei {adjustment.Describe()}");
        }

        // Unsinnige Eingaben duerfen nicht in eine Division durch null laufen.
        var degenerate = new ImageAdjustments { BlackPoint = 0.5, WhitePoint = 0.5 }.BuildToneCurve();
        Check.That(degenerate.Length == 256, "gleicher Schwarz- und Weisspunkt ergibt trotzdem eine Kurve");

        var clamped = new ImageAdjustments { Exposure = 99, Gamma = -5, Saturation = -2 }.Clamped();
        Check.That(clamped.Exposure <= 6 && clamped.Gamma > 0 && clamped.Saturation >= 0,
            "Clamped begrenzt auf sinnvolle Bereiche",
            $"EV {clamped.Exposure}, γ {clamped.Gamma}, S {clamped.Saturation}");
    }

    // ---------------------------------------------------------------- Pixelpfad

    private static (byte b, byte g, byte r, byte a) Process(byte b, byte g, byte r, byte a,
                                                            ImageAdjustments adjustments)
    {
        var source = new byte[] { b, g, r, a };
        var destination = Marshal.AllocHGlobal(4);

        try
        {
            FrameProcessor.Apply(source, 1, 1, 4, destination, 4, adjustments);
            var result = new byte[4];
            Marshal.Copy(destination, result, 0, 4);
            return (result[0], result[1], result[2], result[3]);
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
        }
    }

    private static void PixelPipeline()
    {
        Check.Group("Pixelpfad");

        var (b, g, r, a) = Process(10, 20, 30, 200, ImageAdjustments.Neutral);
        Check.That(b == 10 && g == 20 && r == 30 && a == 200,
            "neutral kopiert unveraendert", $"{b},{g},{r},{a}");

        // Alpha bleibt immer unberuehrt - es ist keine Farbe.
        (_, _, _, a) = Process(100, 100, 100, 123,
            new ImageAdjustments { Exposure = 2, Contrast = 3, Saturation = 0 });
        Check.That(a == 123, "Alpha bleibt von der Korrektur unberuehrt", $"{a}");

        // Saettigung 0 ergibt Graustufen: alle drei Kanaele gleich.
        (b, g, r, _) = Process(30, 150, 220, 255, new ImageAdjustments { Saturation = 0 });
        Check.That(b == g && g == r, "Saettigung 0 ergibt Grau", $"{b},{g},{r}");

        int expected = (int)(0.2126 * 220 + 0.7152 * 150 + 0.0722 * 30 + 0.5);
        Check.That(Math.Abs(r - expected) <= 1, "und zwar nach Rec.-709-Luminanz",
            $"{r} statt {expected}");

        // Kanalisolierung legt einen Kanal auf alle drei.
        (b, g, r, _) = Process(11, 22, 33, 255, new ImageAdjustments { Channel = ChannelView.Red });
        Check.That(b == 33 && g == 33 && r == 33, "Rotkanal isoliert", $"{b},{g},{r}");

        (b, g, r, _) = Process(11, 22, 33, 77, new ImageAdjustments { Channel = ChannelView.Alpha });
        Check.That(b == 77 && g == 77 && r == 77, "Alphakanal wird als Graustufe sichtbar",
            $"{b},{g},{r}");

        // Ein groesseres Bild mit Zeilenabstand: der Prozessor darf die Zeilen nicht verwechseln.
        const int w = 7, h = 5;
        var source = new byte[w * 4 * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w * 4 + x * 4;
                source[i + 0] = (byte)(x * 10);
                source[i + 1] = (byte)(y * 10);
                source[i + 2] = (byte)(x + y);
                source[i + 3] = 255;
            }

        var buffer = Marshal.AllocHGlobal(w * 4 * h);
        try
        {
            FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4, ImageAdjustments.Neutral);
            var copy = new byte[w * 4 * h];
            Marshal.Copy(buffer, copy, 0, copy.Length);
            Check.That(copy.SequenceEqual(source), "ein mehrzeiliges Bild wird zeilentreu kopiert");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---------------------------------------------------------------- Histogramm

    private static void HistogramMath()
    {
        Check.Group("Histogramm");

        const int w = 64, h = 64;
        var source = new byte[w * 4 * h];

        // Halb schwarz, halb weiss - die Verteilung muss beide Enden zeigen.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w * 4 + x * 4;
                byte v = y < h / 2 ? (byte)0 : (byte)255;
                source[i + 0] = source[i + 1] = source[i + 2] = v;
                source[i + 3] = 255;
            }

        var histogram = new Histogram();
        FrameProcessor.Measure(source, w, h, w * 4, histogram);

        Check.That(histogram.Luma[0] == w * h / 2, "die Haelfte liegt bei Schwarz",
            $"{histogram.Luma[0]}");
        Check.That(histogram.Luma[255] == w * h / 2, "die andere bei Weiss",
            $"{histogram.Luma[255]}");
        Check.Near(histogram.ClippedLow, 0.5, 0.01, "die Haelfte liegt unten an");
        Check.Near(histogram.ClippedHigh, 0.5, 0.01, "die Haelfte oben");

        // Der Spitzenwert laesst die Randklassen aussen vor - sonst waere bei einem
        // Bild mit grossem schwarzem Hintergrund vom Rest nichts mehr zu sehen.
        Check.That(histogram.Peak == 1, "die Randklassen zaehlen nicht als Spitzenwert",
            $"{histogram.Peak}");

        // Gemessen wird das KORRIGIERTE Bild: was man sieht, steht im Histogramm.
        var corrected = new Histogram();
        var buffer = Marshal.AllocHGlobal(w * 4 * h);
        try
        {
            FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4,
                                 new ImageAdjustments { Exposure = -2 });
            FrameProcessor.Measure(source, w, h, w * 4, corrected,
                                   adjustments: new ImageAdjustments { Exposure = -2 });
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Check.That(corrected.ClippedHigh < 0.01,
            "nach -2 EV liegt nichts mehr oben an", $"{corrected.ClippedHigh:0.###}");
        Check.That(corrected.Luma[63] + corrected.Luma[64] > 0,
            "die weisse Haelfte ist auf ein Viertel gefallen",
            $"63:{corrected.Luma[63]} 64:{corrected.Luma[64]}");

        // Stichprobenweise messen liefert dieselbe Verteilung, nur grober.
        var sampled = new Histogram();
        FrameProcessor.Measure(source, w, h, w * 4, sampled, step: 4);
        Check.Near(sampled.ClippedLow, 0.5, 0.02, "auch die Stichprobe trifft die Verteilung");
    }

    // ---------------------------------------------------------------- ffmpeg

    private static void FfmpegMapping()
    {
        Check.Group("Uebernahme in den Export");

        Check.That(ImageAdjustments.Neutral.ToFfmpegFilter() is null,
            "ohne Korrektur entsteht kein Filter");

        var filter = new ImageAdjustments { Exposure = 1, Contrast = 1.2, Saturation = 0.8, Gamma = 1.1 }
            .ToFfmpegFilter();
        Check.That(filter is not null && filter.Contains("eq="), "Korrekturen ergeben einen eq-Filter", filter);
        Check.That(filter!.Contains("contrast=1.2"), "Kontrast wird uebernommen", filter);
        Check.That(filter.Contains("saturation=0.8"), "Saettigung wird uebernommen", filter);
        Check.That(!filter.Contains(","), "die Zahlen benutzen den Punkt, nicht das Komma", filter);

        var levels = new ImageAdjustments { BlackPoint = 0.1, WhitePoint = 0.9 }.ToFfmpegFilter();
        Check.That(levels is not null && levels.Contains("curves="),
            "Schwarz- und Weisspunkt gehen ueber curves", levels);

        // Kanalansichten sind Beurteilungswerkzeuge und gehoeren nicht in den Export.
        var channelOnly = new ImageAdjustments { Channel = ChannelView.Red }.ToFfmpegFilter();
        Check.That(channelOnly is null, "eine Kanalansicht allein erzeugt keinen Filter");

        Check.That(new ImageAdjustments { Channel = ChannelView.Red }.IsNeutral == false,
            "eine Kanalansicht gilt trotzdem nicht als neutral");
    }

    // ---------------------------------------------------------------- Tempo

    private static void Performance()
    {
        Check.Group("Tempo der Korrektur");

        const int w = 1920, h = 1080;
        var source = new byte[w * 4 * h];
        new Random(1).NextBytes(source);

        var buffer = Marshal.AllocHGlobal(w * 4 * h);
        try
        {
            var full = new ImageAdjustments { Exposure = 0.5, Gamma = 1.2, Contrast = 1.1, Saturation = 1.3 };

            // Warmlaufen, damit der JIT nicht in die Messung faellt.
            FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4, full);

            var neutral = Time(() => FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4,
                                                          ImageAdjustments.Neutral));
            var tone = Time(() => FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4,
                                                       new ImageAdjustments { Exposure = 0.5 }));
            var everything = Time(() => FrameProcessor.Apply(source, w, h, w * 4, buffer, w * 4, full));

            Console.WriteLine($"         1080p: neutral {neutral:0.0} ms, " +
                              $"Tonwerte {tone:0.0} ms, alles {everything:0.0} ms");

            // Bei 24 fps stehen 41,7 ms je Bild zur Verfuegung. Die Korrektur darf
            // davon nur einen kleinen Teil brauchen, sonst kostet sie Bilder.
            Check.That(everything < 20,
                "die volle Korrektur bleibt deutlich unter dem Bildabstand bei 24 fps (41,7 ms)",
                $"{everything:0.0} ms");
            Check.That(neutral < 5,
                "ohne Korrektur ist es ein reiner Speicherkopiervorgang", $"{neutral:0.0} ms");

            var histogram = new Histogram();
            var measured = Time(() => FrameProcessor.Measure(source, w, h, w * 4, histogram, 4, full));
            Console.WriteLine($"         Histogramm getrennt (jedes 4. Pixel): {measured:0.0} ms");
            Check.That(measured < 15, "das Histogramm laesst sich nebenher messen",
                $"{measured:0.0} ms");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double Time(Action action)
    {
        var times = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times[times.Count / 2];
    }
}
