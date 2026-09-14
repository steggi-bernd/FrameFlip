using System.Runtime.InteropServices;
using FrameFlip.Imaging;

namespace FrameFlip.Tests;

/// <summary>
/// Die Korrektur auf linearem Szenenlicht. Worum es geht, steht im ersten Abschnitt:
/// dass ein Regler auf Gleitkommamaterial etwas anderes kann als auf acht Bit.
/// </summary>
public static class FloatImagingInvariants
{
    public static void Run()
    {
        RecoversHighlights();
        Ordering();
        ToneCurve();
        Channels();
        HistogramMath();
        Reduction();
        CoarsePreview();
        Performance();
    }

    // ------------------------------------------------------- Der eigentliche Punkt

    /// <summary>
    /// Die Aussage, fuer die EXR ueberhaupt gelesen wird.
    ///
    /// Ein Pixel mit 4,0 / 1,0 / 0,25 - eine ueberstrahlte warme Flaeche. In acht Bit
    /// ist davon (255, 255, 137) uebrig: Rot und Gruen liegen beide oben an, das
    /// Verhaeltnis zwischen ihnen ist verloren. Zwei Blendenstufen herunter muessen
    /// deshalb zwei verschiedene Dinge ergeben:
    ///
    ///   - auf acht Bit wird das Bild dunkler, bleibt aber farblos verwaschen
    ///   - auf Gleitkomma kommt die Farbe zurueck, weil die Zahlen noch da sind
    /// </summary>
    private static void RecoversHighlights()
    {
        Check.Group("Belichtung holt Zeichnung zurueck");

        var view = new StandardViewTransform();
        var frame = Solid(4.0f, 1.0f, 0.25f);

        // Ohne Korrektur laufen Rot und Gruen beide auf Weiss - so wie es ein
        // PNG dieser Szene auch taete.
        var plain = Render(frame, ImageAdjustments.Neutral, view);
        Check.That(plain.R == 255 && plain.G == 255, "unkorrigiert liegen Rot und Gruen oben an",
                   $"{plain.R}/{plain.G}/{plain.B}");

        // Zwei Stufen herunter: 4,0 wird 1,0 und 1,0 wird 0,25. Jetzt trennen sich
        // die Kanaele wieder.
        var lowered = Render(frame, new ImageAdjustments { Exposure = -2 }, view);

        Check.That(lowered.R == 255, "Rot sitzt danach genau auf Weiss", $"{lowered.R}");
        Check.That(lowered.G < 150, "Gruen faellt deutlich darunter", $"{lowered.G}");
        Check.That(lowered.R - lowered.G > 100, "die Kanaele trennen sich wieder",
                   $"Abstand {lowered.R - lowered.G}");

        // Und die Gegenprobe: derselbe Regler auf demselben Bild, nachdem es auf
        // acht Bit gebracht wurde. Dort ist der Abstand zwischen Rot und Gruen weg
        // und kommt auch nicht wieder.
        byte[] eightBit = ToEightBit(frame, view);
        var eightLowered = RenderEightBit(eightBit, new ImageAdjustments { Exposure = -2 });

        Check.That(eightLowered.R == eightLowered.G,
                   "auf acht Bit bleiben Rot und Gruen gleich - die Zeichnung ist fort",
                   $"{eightLowered.R}/{eightLowered.G}");

        Check.That(lowered.R - lowered.G > 100 && eightLowered.R - eightLowered.G == 0,
                   "genau das ist der Unterschied, den EXR ausmacht");

        // Auch nach oben: ein dunkler Wert traegt noch, wo acht Bit nur noch Stufen
        // haetten.
        var dark = Solid(0.002f, 0.001f, 0.0005f);
        var lifted = Render(dark, new ImageAdjustments { Exposure = 6 }, view);
        Check.That(lifted.R > lifted.G && lifted.G > lifted.B,
                   "aufgehellte Schatten behalten ihre Abstufung",
                   $"{lifted.R}/{lifted.G}/{lifted.B}");
    }

    /// <summary>
    /// Wo die Regler in der Kette sitzen. Belichtung gehoert vor die
    /// Sichtumwandlung, Gamma und Kontrast dahinter - wird das vertauscht, arbeiten
    /// die Regler gegen die Umwandlung statt mit ihr.
    /// </summary>
    private static void Ordering()
    {
        Check.Group("Reihenfolge der Korrektur");

        var view = new StandardViewTransform();

        // Belichtung ist eine Multiplikation in linearem Licht: 0,5 mit einer Stufe
        // mehr muss dasselbe Ergebnis geben wie 1,0 ohne.
        var half = Render(Solid(0.5f, 0.5f, 0.5f), new ImageAdjustments { Exposure = 1 }, view);
        var full = Render(Solid(1.0f, 1.0f, 1.0f), ImageAdjustments.Neutral, view);

        Check.That(half.R == full.R, "eine Blendenstufe verdoppelt die Lichtmenge",
                   $"{half.R} gegen {full.R}");

        // Und zwei Stufen vervierfachen sie.
        var quarter = Render(Solid(0.25f, 0.25f, 0.25f), new ImageAdjustments { Exposure = 2 }, view);
        Check.That(quarter.R == full.R, "zwei Stufen vervierfachen sie", $"{quarter.R} gegen {full.R}");

        // Gamma sitzt hinter der Umwandlung und rechnet auf Anzeigewerten.
        // 0,18 linear ergibt in sRGB 0,4613; mit Gamma 2 muss daraus die Wurzel
        // werden.
        var gamma = Render(Solid(0.18f, 0.18f, 0.18f), new ImageAdjustments { Gamma = 2.0 }, view);
        Check.Near(gamma.R, Math.Pow(0.4613, 0.5) * 255, 2, "Gamma rechnet auf der Anzeigeseite");

        // An Gamma allein laesst sich die Reihenfolge allerdings NICHT nachweisen,
        // und das ist kein Mangel des Tests, sondern Mathematik: Gamma ist eine
        // Potenz, die sRGB-Kurve ist fast eine, und Potenzen vertauschen
        // miteinander. Beide Reihenfolgen liegen hier eine Stufe auseinander.
        //
        // Der Kontrast vertauscht nicht, weil er den Wert um einen festen Punkt
        // spreizt. Sein Drehpunkt verraet damit, auf welcher Seite er sitzt: liegt
        // er bei 0,5 der ANZEIGE, arbeitet er hinter der Umwandlung; laege er bei
        // 0,5 im linearen Licht, davor. Die beiden Stellen sind weit auseinander -
        // linear 0,5 erscheint als 0,735.
        float displayMid = Srgb.Decode(0.5f);      // linear rund 0,214
        var atDisplayMid = Render(Solid(displayMid, displayMid, displayMid),
                                  new ImageAdjustments { Contrast = 2.5 }, view);

        Check.Near(atDisplayMid.R, 128, 2, "der Kontrast dreht um die Mitte der Anzeige");

        var atLinearMid = Render(Solid(0.5f, 0.5f, 0.5f),
                                 new ImageAdjustments { Contrast = 2.5 }, view);

        // Saesse der Kontrast auf der linearen Seite, bliebe dieser Wert stehen.
        // Er tut es nicht - er wird nach oben gespreizt.
        Check.That(atLinearMid.R > 220, "und nicht um die Mitte des linearen Lichts",
                   $"{atLinearMid.R} statt der 188, die unveraendert waeren");
    }

    private static void ToneCurve()
    {
        Check.Group("Tonwertkurve auf Gleitkomma");

        var view = new StandardViewTransform();

        var neutral = Render(Solid(0.18f, 0.18f, 0.18f), ImageAdjustments.Neutral, view);
        Check.Near(neutral.R, Srgb.Encode(0.18f) * 255, 1, "unkorrigiert ist es die reine sRGB-Kurve");

        // Schwarzpunkt: was darunter liegt, geht auf null.
        var lowBlack = Render(Solid(0.05f, 0.05f, 0.05f), new ImageAdjustments { BlackPoint = 0.5 }, view);
        Check.That(lowBlack.R == 0, "unter dem Schwarzpunkt wird es schwarz", $"{lowBlack.R}");

        // Kontrast dreht um die Mitte: ein Wert genau auf 0,5 darf sich nicht bewegen.
        float mid = Srgb.Decode(0.5f);
        var pivot = Render(Solid(mid, mid, mid), new ImageAdjustments { Contrast = 2.0 }, view);
        Check.Near(pivot.R, 128, 2, "der Drehpunkt des Kontrasts liegt in der Mitte");

        // Saettigung null ergibt Grau - und zwar dasselbe in allen drei Kanaelen.
        var grey = Render(Solid(0.6f, 0.2f, 0.05f), new ImageAdjustments { Saturation = 0 }, view);
        Check.That(grey.R == grey.G && grey.G == grey.B, "Saettigung null ergibt Grau",
                   $"{grey.R}/{grey.G}/{grey.B}");

        // Der neutrale Fall darf nichts veraendern - auch nicht um eine Stufe.
        var untouched = Render(Solid(0.3f, 0.6f, 0.9f), ImageAdjustments.Neutral, view);
        var expected = (
            ToByte(Srgb.Encode(0.3f)), ToByte(Srgb.Encode(0.6f)), ToByte(Srgb.Encode(0.9f)));

        Check.That(untouched.R == expected.Item1 && untouched.G == expected.Item2 &&
                   untouched.B == expected.Item3,
                   "ohne Korrektur bleibt es bei der reinen Umwandlung",
                   $"{untouched.R}/{untouched.G}/{untouched.B}");
    }

    private static void Channels()
    {
        Check.Group("Kanalansichten auf Gleitkomma");

        var view = new StandardViewTransform();
        var frame = Solid(0.6f, 0.2f, 0.05f, alpha: 0.5f);

        var red = Render(frame, new ImageAdjustments { Channel = ChannelView.Red }, view);
        Check.That(red.R == red.G && red.G == red.B, "die Rotansicht ist grau");
        Check.Near(red.R, Srgb.Encode(0.6f) * 255, 1, "und zeigt den Rotwert");

        var alpha = Render(frame, new ImageAdjustments { Channel = ChannelView.Alpha }, view);
        Check.Near(alpha.R, 128, 1, "die Alphaansicht zeigt den Alphawert");

        var luma = Render(frame, new ImageAdjustments { Channel = ChannelView.Luminance }, view);
        Check.That(luma.R == luma.G && luma.G == luma.B, "die Luminanzansicht ist grau");

        // Alpha geht auch ohne Kanalansicht durch.
        var plain = Render(frame, ImageAdjustments.Neutral, view);
        Check.Near(plain.A, 128, 1, "Alpha kommt im Bild an");

        var noAlpha = Render(Solid(0.5f, 0.5f, 0.5f), ImageAdjustments.Neutral, view);
        Check.That(noAlpha.A == 255, "ohne Alphakanal ist alles undurchsichtig");
    }

    /// <summary>
    /// Das Histogramm - und die Angabe, die es nur auf Gleitkomma geben kann.
    /// </summary>
    private static void HistogramMath()
    {
        Check.Group("Histogramm auf Gleitkomma");

        var view = new StandardViewTransform();
        var histogram = new Histogram();

        // Ein Bild, dessen eine Haelfte ueber Weiss liegt.
        var frame = Gradient(64, 64, (x, y) => x < 32 ? 0.3f : 5.0f);
        FloatFrameProcessor.Measure(frame, ImageAdjustments.Neutral, view, histogram);

        Check.Near(histogram.AboveWhite, 0.5, 0.02, "die Haelfte des Bildes hat Reserve ueber Weiss");
        Check.Near(histogram.ClippedHigh, 0.5, 0.02, "und liegt in der Anzeige oben an");

        // Nach zweieinhalb Stufen herunter ist die Ueberstrahlung in der Anzeige weg -
        // die Reserve in der Datei aber unveraendert, denn die Datei aendert sich nicht.
        FloatFrameProcessor.Measure(frame, new ImageAdjustments { Exposure = -2.5 }, view, histogram);

        Check.That(histogram.ClippedHigh < 0.01, "heruntergezogen liegt nichts mehr an",
                   $"{histogram.ClippedHigh:0.###}");
        Check.Near(histogram.AboveWhite, 0.5, 0.02, "die Reserve in der Datei bleibt, was sie war");

        // Auf einem Bild ohne Ueberstrahlung ist die Angabe null - genau wie bei
        // Material, das aus acht Bit stammt.
        var plain = Gradient(32, 32, (x, y) => 0.5f);
        FloatFrameProcessor.Measure(plain, ImageAdjustments.Neutral, view, histogram);
        Check.That(histogram.AboveWhite == 0, "ohne Ueberstrahlung ist die Reserve null");

        // Schrittweite: jedes vierte Pixel muss dieselbe Verteilung ergeben.
        var histogram4 = new Histogram();
        FloatFrameProcessor.Measure(frame, ImageAdjustments.Neutral, view, histogram4, step: 4);
        Check.Near(histogram4.AboveWhite, 0.5, 0.03, "auch mit Schrittweite vier stimmt der Anteil");
    }

    /// <summary>
    /// Was der Gleitkommaweg kostet. Er gilt dem angehaltenen Bild, nicht der
    /// Wiedergabe - trotzdem darf ein Reglerzug nicht spuerbar haengen.
    /// </summary>
    private static void Performance()
    {
        Check.Group("Tempo des Gleitkommawegs");

        var view = new StandardViewTransform();
        var frame = Gradient(1920, 1080, (x, y) => (x + y) / 1000f);
        var adjustments = new ImageAdjustments { Exposure = -1.5, Gamma = 1.2, Contrast = 1.1, Saturation = 1.2 };

        var buffer = Marshal.AllocHGlobal(1920 * 1080 * 4);

        try
        {
            // Einmal warmlaufen, damit nicht der erste Durchgang gemessen wird.
            FloatFrameProcessor.Apply(frame, adjustments, view, buffer, 1920 * 4);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            FloatFrameProcessor.Apply(frame, adjustments, view, buffer, 1920 * 4);
            watch.Stop();

            double ms = watch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  [i]    1080p, Sichtumwandlung Standard: {ms:0.#} ms");

            // Dieselbe Messung mit AgX, wo je Pixel noch eine dreidimensionale
            // Tabelle abgefragt wird. Das ist die Zahl, die im Betrieb zaehlt - und
            // die entscheidet, ob die Vorschau beim Reglerziehen verkleinert werden
            // muss.
            string? lutPath = null;
            foreach (var install in FrameFlip.Rendering.BlenderFinder.Find())
            {
                lutPath = AgxViewTransform.FindLut(install.Path);
                if (lutPath is not null) break;
            }

            if (lutPath is not null)
            {
                var agx = AgxViewTransform.FromLut(CubeLut.Load(lutPath), lutPath);
                FloatFrameProcessor.Apply(frame, adjustments, agx, buffer, 1920 * 4);

                watch.Restart();
                FloatFrameProcessor.Apply(frame, adjustments, agx, buffer, 1920 * 4);
                watch.Stop();

                double agxMs = watch.Elapsed.TotalMilliseconds;
                Console.WriteLine($"  [i]    1080p, Sichtumwandlung AgX:      {agxMs:0.#} ms" +
                                  $"   (4K rund {agxMs * 4:0} ms)");

                Check.That(agxMs < 1000, "ein Reglerzug auf 1080p mit AgX bleibt im Rahmen",
                           $"{agxMs:0.#} ms");
            }

            // Grosszuegig bemessen: auf einer langsamen Maschine unter Last darf es
            // laenger dauern. Die Grenze soll einen Einbruch um eine Groessenordnung
            // fangen, nicht eine Schwankung.
            Check.That(ms < 500, "ein Reglerzug auf 1080p bleibt im Rahmen", $"{ms:0.#} ms");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // --------------------------------------------------------------- Hilfsmittel

    private static FloatFrame Solid(float r, float g, float b, float? alpha = null)
        => new()
        {
            Width = 2,
            Height = 2,
            R = new[] { r, r, r, r },
            G = new[] { g, g, g, g },
            B = new[] { b, b, b, b },
            A = alpha is null ? null : new[] { alpha.Value, alpha.Value, alpha.Value, alpha.Value },
        };

    private static FloatFrame Gradient(int width, int height, Func<int, int, float> f)
    {
        var r = new float[width * height];
        var g = new float[width * height];
        var b = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float v = f(x, y);
                int i = y * width + x;
                r[i] = v;
                g[i] = v * 0.8f;
                b[i] = v * 0.6f;
            }
        }

        return new FloatFrame { Width = width, Height = height, R = r, G = g, B = b };
    }

    /// <summary>
    /// Die grobe Vorschau fuer den Reglerzug: nur jeder n-te Bildpunkt wird
    /// gerechnet, die uebrigen bekommen seinen Wert.
    ///
    /// Der wichtigste Punkt daran ist nicht das Tempo, sondern dass nichts
    /// uebrigbleibt. Eine falsche Randrechnung hinterlaesst Streifen, die nie
    /// beschrieben wurden - und uninitialisierter Speicher in einer Bitmap ist
    /// schwarz, sieht also wie ein Bildfehler aus und nicht wie ein Rechenfehler.
    /// </summary>
    private static void CoarsePreview()
    {
        Check.Group("Grobe Vorschau beim Ziehen");

        var view = new StandardViewTransform();

        // Ungerade Groessen, damit die Bloecke am Rand nicht aufgehen.
        const int width = 37, height = 23;
        var frame = Gradient(width, height, (x, y) => (x + y) / 40f);
        int stride = width * 4;

        foreach (int step in new[] { 1, 2, 3, 4, 8, 16 })
        {
            var buffer = Marshal.AllocHGlobal(stride * height);

            try
            {
                // Vorbelegen mit einem Wert, der nie herauskommen kann: was danach
                // noch darin steht, wurde nicht geschrieben.
                for (int i = 0; i < stride * height; i++) Marshal.WriteByte(buffer, i, 0xCD);

                FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, view,
                                          FrameFlip.Imaging.Grading.PreparedGrading.None,
                                          buffer, stride, step);

                var pixels = new byte[stride * height];
                Marshal.Copy(buffer, pixels, 0, pixels.Length);

                int untouched = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    // Alpha ist in diesem Frame immer 255; ein Pixel, das noch die
                    // Vorbelegung traegt, wurde ausgelassen.
                    if (pixels[i] == 0xCD && pixels[i + 1] == 0xCD &&
                        pixels[i + 2] == 0xCD && pixels[i + 3] == 0xCD) untouched++;
                }

                Check.That(untouched == 0, $"Schrittweite {step}: jedes Pixel ist beschrieben",
                           $"{untouched} von {width * height} ausgelassen");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // An den Stellen, die tatsaechlich gerechnet werden, muss dasselbe
        // herauskommen wie im vollen Durchgang.
        var full = RenderAll(frame, view, 1, width, height, stride);
        var coarse = RenderAll(frame, view, 4, width, height, stride);

        int mismatched = 0;
        for (int y = 0; y < height; y += 4)
        {
            for (int x = 0; x < width; x += 4)
            {
                int i = (y * width + x) * 4;
                if (full[i] != coarse[i] || full[i + 1] != coarse[i + 1] || full[i + 2] != coarse[i + 2])
                    mismatched++;
            }
        }

        Check.That(mismatched == 0, "die gerechneten Stellen stimmen mit dem vollen Bild ueberein",
                   $"{mismatched} Abweichungen");

        // Und der Zweck der Uebung: es muss schneller sein.
        var big = Gradient(1920, 1080, (x, y) => (x + y) / 1000f);
        var target = Marshal.AllocHGlobal(1920 * 1080 * 4);

        try
        {
            var adjustments = new ImageAdjustments { Exposure = -1, Gamma = 1.2 };

            FloatFrameProcessor.Apply(big, adjustments, view, target, 1920 * 4);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            FloatFrameProcessor.Apply(big, adjustments, view,
                                      FrameFlip.Imaging.Grading.PreparedGrading.None, target, 1920 * 4);
            double fullMs = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            FloatFrameProcessor.Apply(big, adjustments, view,
                                      FrameFlip.Imaging.Grading.PreparedGrading.None, target, 1920 * 4, 4);
            double coarseMs = watch.Elapsed.TotalMilliseconds;

            Console.WriteLine($"  [i]    1080p voll {fullMs:0.#} ms, mit Schrittweite 4 {coarseMs:0.#} ms");

            // Ein Sechzehntel der Punkte; der Rest ist Fuellen und Speicherzugriff.
            // Ein Faktor von wenigstens drei ist die Aussage, auf die es ankommt.
            Check.That(coarseMs * 3 < fullMs, "die grobe Vorschau ist deutlich schneller",
                       $"{coarseMs:0.#} ms gegen {fullMs:0.#} ms");
        }
        finally
        {
            Marshal.FreeHGlobal(target);
        }
    }

    private static byte[] RenderAll(FloatFrame frame, IViewTransform view, int step,
                                    int width, int height, int stride)
    {
        var buffer = Marshal.AllocHGlobal(stride * height);

        try
        {
            FloatFrameProcessor.Apply(frame, ImageAdjustments.Neutral, view,
                                      FrameFlip.Imaging.Grading.PreparedGrading.None,
                                      buffer, stride, step);

            var pixels = new byte[stride * height];
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (byte R, byte G, byte B, byte A) Render(FloatFrame frame, ImageAdjustments adjustments,
                                                           IViewTransform view)
    {
        int stride = frame.Width * 4;
        var buffer = Marshal.AllocHGlobal(stride * frame.Height);

        try
        {
            FloatFrameProcessor.Apply(frame, adjustments, view, buffer, stride);

            var pixels = new byte[4];
            Marshal.Copy(buffer, pixels, 0, 4);
            return (pixels[2], pixels[1], pixels[0], pixels[3]);   // Bgra32
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Dasselbe Bild, wie es nach dem Weg ueber acht Bit aussaehe.</summary>
    private static byte[] ToEightBit(FloatFrame frame, IViewTransform view)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];

        for (int i = 0; i < frame.PixelCount; i++)
        {
            float r = frame.R[i], g = frame.G[i], b = frame.B[i];
            view.Apply(ref r, ref g, ref b);

            pixels[i * 4] = ToByte(b);
            pixels[i * 4 + 1] = ToByte(g);
            pixels[i * 4 + 2] = ToByte(r);
            pixels[i * 4 + 3] = 255;
        }

        return pixels;
    }

    private static (byte R, byte G, byte B) RenderEightBit(byte[] source, ImageAdjustments adjustments)
    {
        var buffer = Marshal.AllocHGlobal(source.Length);

        try
        {
            FrameProcessor.Apply(source, 2, 2, 8, buffer, 8, adjustments);

            var pixels = new byte[4];
            Marshal.Copy(buffer, pixels, 0, 4);
            return (pixels[2], pixels[1], pixels[0]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static byte ToByte(float value)
        => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    /// <summary>
    /// Das Verkleinern. Gebraucht wird es, weil der Gleitkommaframe dieselbe Groesse
    /// haben muss wie der Puffer, an dessen Stelle er tritt - die Anzeige dekodiert
    /// je nach Vorlauf auch auf 50 oder 25 Prozent.
    /// </summary>
    private static void Reduction()
    {
        Check.Group("Gleitkommaframe verkleinern");

        // Ein Schachbrett aus 0 und 4: jeder 2x2-Block enthaelt genau zwei von jedem.
        var frame = Gradient(8, 8, (x, y) => (x + y) % 2 == 0 ? 0f : 4f);
        var half = frame.Reduced(4, 4);

        Check.That(half.Width == 4 && half.Height == 4, "8x8 wird zu 4x4", $"{half.Width}x{half.Height}");

        // Der Mittelwert ist 2,0 - gerechnet im linearen Licht. Waere erst umgewandelt
        // und dann gemittelt worden, kaeme ein anderer Wert heraus, und das Bild fiele
        // sichtbar zu dunkel aus.
        Check.Near(half.R[0], 2.0, 0.001, "gemittelt wird im linearen Licht");
        Check.That(half.R.All(v => Math.Abs(v - 2.0f) < 0.001f), "und zwar ueberall gleich");

        // Werte oberhalb von Weiss ueberleben das Verkleinern.
        var bright = Gradient(4, 4, (x, y) => 8f).Reduced(2, 2);
        Check.Near(bright.R[0], 8.0, 0.001, "Ueberstrahlung ueberlebt das Verkleinern");

        // Passt es schon, wird nichts kopiert - der Frame ist bei 4K ein
        // zweistelliger Megabytebetrag.
        var unchanged = frame.Reduced(8, 8);
        Check.That(ReferenceEquals(unchanged, frame), "passt es bereits, bleibt es derselbe Frame");

        var larger = frame.Reduced(99, 99);
        Check.That(ReferenceEquals(larger, frame), "und vergroessert wird nie");

        // Die Schrittberechnung wird vom Decoder mitbenutzt - dort entscheidet
        // derselbe Faktor ueber die Groesse des Puffers.
        Check.That(FloatFrame.StepFor(1920, 1080, 1920, 1080) == 1, "passt es, ist der Schritt 1");
        Check.That(FloatFrame.StepFor(3840, 2160, 1920, 1080) == 2, "4K auf 1080p ist Schritt 2");
        Check.That(FloatFrame.StepFor(3840, 2160, 960, 540) == 4, "und auf 540p Schritt 4");
        Check.That(FloatFrame.StepFor(100, 100, 999, 999) == 1, "kleiner als die Grenze bleibt kleiner");

        // Die Falle: ein sehr schmales Bild darf nicht auf null Pixel schrumpfen.
        int narrow = FloatFrame.StepFor(1000, 3, 10, 10);
        Check.That(1000 / narrow >= 1 && 3 / narrow >= 1, "ein schmales Bild behaelt beide Seiten",
                   $"Schritt {narrow} bei 1000x3");

        Check.That(FloatFrame.StepFor(1, 1, 1, 1) == 1, "ein Pixel bleibt ein Pixel");

        // Ein Alphakanal wird mitverkleinert, keiner bleibt keiner.
        var withAlpha = new FloatFrame
        {
            Width = 4, Height = 4,
            R = new float[16], G = new float[16], B = new float[16],
            A = Enumerable.Repeat(0.5f, 16).ToArray(),
        };

        var reducedAlpha = withAlpha.Reduced(2, 2);
        Check.That(reducedAlpha.A is not null && Math.Abs(reducedAlpha.A[0] - 0.5f) < 0.001f,
                   "Alpha wird mitverkleinert");

        Check.That(frame.Reduced(4, 4).A is null, "ohne Alpha bleibt es ohne");

        // Ungerade Groessen duerfen nicht ueberlaufen - der Rest faellt weg, das Bild
        // bleibt heil.
        var odd = Gradient(9, 7, (x, y) => 1f).Reduced(4, 3);
        Check.That(odd.Width >= 1 && odd.Height >= 1, "ungerade Groessen ergeben ein Bild",
                   $"{odd.Width}x{odd.Height}");
        Check.That(odd.R.Length == odd.Width * odd.Height, "und ein Feld passender Laenge");
    }
}
