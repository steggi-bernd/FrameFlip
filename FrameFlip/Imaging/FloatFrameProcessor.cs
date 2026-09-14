using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging;

/// <summary>
/// Die Anzeigekorrektur auf linearem Szenenlicht.
///
/// Der Unterschied zum <see cref="FrameProcessor"/> ist nicht die Genauigkeit,
/// sondern die Reihenfolge - und die ist hier nicht Geschmack, sondern folgt dem,
/// was die einzelnen Regler physikalisch bedeuten:
///
/// **Belichtung wirkt VOR der Sichtumwandlung.** Sie ist eine Lichtmenge, also eine
/// Multiplikation in linearem Licht. Genau dort sitzt sie auch in Blender. Wer sie
/// hinter AgX anwendet, hellt ein bereits fertiges Bild auf und holt damit nichts
/// zurueck - die Lichter sind dann schon auf Weiss gelaufen. Davor angewandt zieht
/// derselbe Regler die Zeichnung aus der Ueberstrahlung heraus. Das ist der ganze
/// Grund, warum EXR gelesen wird.
///
/// **Schwarzpunkt, Gamma und Kontrast wirken DANACH.** Es sind Anzeigegroessen; sie
/// biegen eine Kurve, die von 0 bis 1 laeuft. Vor der Sichtumwandlung angewandt
/// haetten sie keinen definierten Bezugspunkt, weil es dort kein Weiss gibt.
///
/// Die Saettigung steht dazwischen, auf der linearen Seite: die Rec.-709-Gewichte
/// sind fuer lineares Licht bestimmt.
/// </summary>
public static class FloatFrameProcessor
{
    // Rec.-709-Luminanz, hier als Gleitkomma - dieselben Gewichte wie im
    // Ganzzahlpfad, nur ohne die Festkomma-Rundung.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Rechnet den Frame mit Korrektur und Sichtumwandlung nach Bgra32.
    ///
    /// Geschrieben wird direkt in den Rueckpuffer der WriteableBitmap, wie im
    /// Ganzzahlpfad auch. Der Quellframe bleibt unangetastet: er wird beim naechsten
    /// Reglerzug erneut gebraucht, und ihn jedes Mal neu von der Platte zu lesen
    /// waere der Unterschied zwischen fluessig und zaeh.
    /// </summary>
    public static void Apply(FloatFrame frame, ImageAdjustments adjustments,
                             IViewTransform view, IntPtr destination, int destinationStride)
        => Apply(frame, adjustments, view, PreparedGrading.None, destination, destinationStride);

    /// <inheritdoc cref="Apply(FloatFrame, ImageAdjustments, IViewTransform, IntPtr, int)"/>
    /// <param name="grading">
    /// Die Werkzeuge, bereits vorbereitet und nach Seite getrennt. Sie wirken NACH
    /// der Grundkorrektur: erst Belichtung und Saettigung, dann die linearen
    /// Werkzeuge, dann die Sichtumwandlung, dann Tonwertkurve und die uebrigen.
    /// Die Grundkorrektur ist der schnelle Griff beim Beurteilen und bleibt deshalb
    /// vorn - wer sie umgehen will, laesst sie neutral.
    /// </param>
    /// <param name="step">
    /// Nur jeder n-te Bildpunkt wird gerechnet; die uebrigen bekommen seinen Wert.
    /// Bei 2 bleibt ein Viertel der Arbeit, bei 4 ein Sechzehntel.
    ///
    /// Gebraucht wird das beim Ziehen eines Reglers: ein 4K-Bild mit AgX kostet
    /// rund 367 ms je Aktualisierung, und dreimal je Sekunde neu zu zeichnen ist
    /// keine Bedienung. Dass die Vorschau dabei grob wird, faellt in der Bewegung
    /// nicht auf - beim Loslassen steht wieder das volle Bild.
    ///
    /// Die Bitmap behaelt ihre Groesse. Sie zu verkleinern waere der naheliegende
    /// Weg und der falsche: Zoom und Bildlage haengen daran, und beide duerfen
    /// waehrend eines Reglerzugs nicht springen.
    /// </param>
    public static unsafe void Apply(FloatFrame frame, ImageAdjustments adjustments,
                                    IViewTransform view, PreparedGrading grading,
                                    IntPtr destination, int destinationStride, int step = 1)
    {
        var linearTools = grading.SceneLinear ?? Array.Empty<IGradingTool>();
        var displayTools = grading.Display ?? Array.Empty<IGradingTool>();

        step = Math.Clamp(step, 1, 16);

        byte* target = (byte*)destination.ToPointer();

        float gain = (float)Math.Pow(2.0, adjustments.Exposure);
        float saturation = (float)adjustments.Saturation;
        var channel = adjustments.Channel;

        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        bool needsTone = !Same(black, 0f) || !Same(white, 1f) ||
                         !Same(inverseGamma, 1f) || !Same(contrast, 1f);

        int width = frame.Width;
        var r = frame.R;
        var g = frame.G;
        var b = frame.B;
        var a = frame.A;

        var plan = new ShadePlan(gain, saturation, channel, black, span, inverseGamma, contrast,
                                 needsTone, view, linearTools, displayTools);

        int height = frame.Height;
        int rowBlocks = (height + step - 1) / step;

        Parallel.For(0, rowBlocks, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        block =>
        {
            int y = block * step;
            byte* row = target + (long)y * destinationStride;

            // Wie viele Zeilen und Spalten dieser Block noch abdeckt - am rechten
            // und unteren Rand weniger als step.
            int blockHeight = Math.Min(step, height - y);

            for (int x = 0; x < width; x += step)
            {
                int i = y * width + x;

                float vr = r[i], vg = g[i], vb = b[i];
                float alpha = a is null ? 1f : a[i];

                Shade(in plan, ref vr, ref vg, ref vb, alpha);

                byte blue = ToByte(vb);
                byte green = ToByte(vg);
                byte red = ToByte(vr);
                byte opacity = ToByte(Math.Clamp(alpha, 0f, 1f));

                if (step == 1)
                {
                    byte* pixel = row + x * 4;
                    pixel[0] = blue;
                    pixel[1] = green;
                    pixel[2] = red;
                    pixel[3] = opacity;
                    continue;
                }

                // Den ganzen Block mit dem einen gerechneten Wert fuellen.
                int blockWidth = Math.Min(step, width - x);

                for (int dy = 0; dy < blockHeight; dy++)
                {
                    byte* line = target + (long)(y + dy) * destinationStride + x * 4;

                    for (int dx = 0; dx < blockWidth; dx++)
                    {
                        line[0] = blue;
                        line[1] = green;
                        line[2] = red;
                        line[3] = opacity;
                        line += 4;
                    }
                }
            }
        });
    }

    /// <summary>
    /// Alles, was einmal je Bild feststeht. Als Struktur, damit die innere Schleife
    /// nicht zwoelf Einzelwerte durchreichen muss.
    /// </summary>
    private readonly struct ShadePlan
    {
        public ShadePlan(float gain, float saturation, ChannelView channel,
                         float black, float span, float inverseGamma, float contrast, bool needsTone,
                         IViewTransform view, IGradingTool[] linear, IGradingTool[] display)
        {
            Gain = gain;
            Saturation = saturation;
            Channel = channel;
            Black = black;
            Span = span;
            InverseGamma = inverseGamma;
            Contrast = contrast;
            NeedsTone = needsTone;
            View = view;
            Linear = linear;
            Display = display;
        }

        public readonly float Gain, Saturation, Black, Span, InverseGamma, Contrast;
        public readonly bool NeedsTone;
        public readonly ChannelView Channel;
        public readonly IViewTransform View;
        public readonly IGradingTool[] Linear, Display;
    }

    /// <summary>
    /// Die ganze Kette fuer einen Bildpunkt - von linearem Szenenlicht zu
    /// Anzeigewerten zwischen 0 und 1.
    ///
    /// Steht an einer Stelle, weil sie an zwei gebraucht wird: fuer die Anzeige mit
    /// acht Bit und fuer den Export mit sechzehn. Zweimal abgeschrieben liefe sie
    /// beim naechsten Werkzeug auseinander, und dann saehe das Ergebnis anders aus
    /// als die Vorschau, auf die jemand sich verlassen hat.
    /// </summary>
    private static void Shade(in ShadePlan plan, ref float vr, ref float vg, ref float vb, float alpha)
    {
        // --- lineare Seite ---

        if (plan.Gain != 1f)
        {
            vr *= plan.Gain;
            vg *= plan.Gain;
            vb *= plan.Gain;
        }

        if (!Same(plan.Saturation, 1f))
        {
            float luma = LumaR * vr + LumaG * vg + LumaB * vb;
            vr = luma + (vr - luma) * plan.Saturation;
            vg = luma + (vg - luma) * plan.Saturation;
            vb = luma + (vb - luma) * plan.Saturation;

            // Uebersaettigung kann unter null druecken; negatives Licht gibt es
            // nicht, und die Sichtumwandlung koennte damit nichts anfangen.
            if (vr < 0) vr = 0;
            if (vg < 0) vg = 0;
            if (vb < 0) vb = 0;
        }

        var linear = plan.Linear;
        for (int t = 0; t < linear.Length; t++) linear[t].Apply(ref vr, ref vg, ref vb);

        // --- Sichtumwandlung: ab hier sind es Anzeigewerte von 0 bis 1 ---

        plan.View.Apply(ref vr, ref vg, ref vb);

        // --- Anzeigeseite ---

        if (plan.NeedsTone)
        {
            vr = Tone(vr, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
            vg = Tone(vg, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
            vb = Tone(vb, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
        }

        var display = plan.Display;
        for (int t = 0; t < display.Length; t++) display[t].Apply(ref vr, ref vg, ref vb);

        if (plan.Channel == ChannelView.All) return;

        switch (plan.Channel)
        {
            case ChannelView.Red: vg = vb = vr; break;
            case ChannelView.Green: vr = vb = vg; break;
            case ChannelView.Blue: vr = vg = vb; break;
            case ChannelView.Alpha: vr = vg = vb = Math.Clamp(alpha, 0f, 1f); break;
            case ChannelView.Luminance:
                vr = vg = vb = LumaR * vr + LumaG * vg + LumaB * vb;
                break;
        }
    }

    /// <summary>
    /// Wie <see cref="Apply(FloatFrame, ImageAdjustments, IViewTransform, PreparedGrading, IntPtr, int, int)"/>,
    /// aber nach Rgba64 - sechzehn Bit je Kanal, Reihenfolge R, G, B, A.
    ///
    /// Fuer den Export. Acht Bit wuerden dort genau das wegwerfen, was die Korrektur
    /// eben gewonnen hat: Ein Verlauf, den die Kurve gestreckt hat, zeigt auf acht
    /// Bit Stufen, wo vorher keine waren - und eine Sequenz, die danach noch durch
    /// eine Farbkorrektur soll, hat davon nichts mehr.
    /// </summary>
    public static unsafe void ApplyRgba64(FloatFrame frame, ImageAdjustments adjustments,
                                          IViewTransform view, PreparedGrading grading,
                                          IntPtr destination, int destinationStride)
    {
        var plan = BuildPlan(adjustments, view, grading);
        ushort* target = (ushort*)destination.ToPointer();

        int width = frame.Width;
        var r = frame.R;
        var g = frame.G;
        var b = frame.B;
        var a = frame.A;

        Parallel.For(0, frame.Height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            ushort* row = (ushort*)((byte*)target + (long)y * destinationStride);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                float vr = r[i], vg = g[i], vb = b[i];
                float alpha = a is null ? 1f : a[i];

                Shade(in plan, ref vr, ref vg, ref vb, alpha);

                ushort* pixel = row + x * 4;
                pixel[0] = ToUShort(vr);
                pixel[1] = ToUShort(vg);
                pixel[2] = ToUShort(vb);
                pixel[3] = ToUShort(Math.Clamp(alpha, 0f, 1f));
            }
        });
    }

    private static ShadePlan BuildPlan(ImageAdjustments adjustments, IViewTransform view, PreparedGrading grading)
    {
        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        bool needsTone = !Same(black, 0f) || !Same(white, 1f) ||
                         !Same(inverseGamma, 1f) || !Same(contrast, 1f);

        return new ShadePlan((float)Math.Pow(2.0, adjustments.Exposure), (float)adjustments.Saturation,
                             adjustments.Channel, black, span, inverseGamma, contrast, needsTone, view,
                             grading.SceneLinear ?? Array.Empty<IGradingTool>(),
                             grading.Display ?? Array.Empty<IGradingTool>());
    }

    private static ushort ToUShort(float value)
        => (ushort)Math.Clamp(MathF.Round(value * 65535f), 0f, 65535f);

    /// <summary>
    /// Die Tonwertkurve auf der Anzeigeseite. Dieselben vier Schritte wie im
    /// Ganzzahlpfad, nur ohne die Tabelle mit 256 Eintraegen: die Werte sind hier
    /// nicht mehr abzaehlbar.
    /// </summary>
    private static float Tone(float v, float black, float span, float inverseGamma, float contrast)
    {
        v = (v - black) / span;
        v = Math.Clamp(v, 0f, 1f);
        if (!Same(inverseGamma, 1f)) v = MathF.Pow(v, inverseGamma);
        if (!Same(contrast, 1f)) v = (v - 0.5f) * contrast + 0.5f;

        return v;
    }

    /// <summary>
    /// Misst die Verteilung. Gemessen wird auf dem korrigierten Bild, damit im
    /// Histogramm steht, was zu sehen ist - dieselbe Regel wie im Ganzzahlpfad.
    ///
    /// Dazu kommt eine Angabe, die es nur hier geben kann: wieviele Pixel in der
    /// DATEI oberhalb von Weiss liegen. Das ist etwas anderes als Ueberstrahlung in
    /// der Anzeige - es ist die Reserve, die der Belichtungsregler noch heben kann.
    /// </summary>
    public static void Measure(FloatFrame frame, ImageAdjustments adjustments, IViewTransform view,
                               Histogram histogram, int step = 1)
        => Measure(frame, adjustments, view, PreparedGrading.None, histogram, step);

    /// <inheritdoc cref="Measure(FloatFrame, ImageAdjustments, IViewTransform, Histogram, int)"/>
    public static void Measure(FloatFrame frame, ImageAdjustments adjustments, IViewTransform view,
                               PreparedGrading grading, Histogram histogram, int step = 1)
    {
        var linearTools = grading.SceneLinear ?? Array.Empty<IGradingTool>();
        var displayTools = grading.Display ?? Array.Empty<IGradingTool>();

        histogram.Clear();
        step = Math.Max(1, step);

        float gain = (float)Math.Pow(2.0, adjustments.Exposure);
        float saturation = (float)adjustments.Saturation;

        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        long sampled = 0;
        long aboveWhite = 0;

        for (int y = 0; y < frame.Height; y += step)
        {
            for (int x = 0; x < frame.Width; x += step)
            {
                int i = y * frame.Width + x;
                float vr = frame.R[i], vg = frame.G[i], vb = frame.B[i];

                // Vor jeder Korrektur: liegt hier Reserve oberhalb von Weiss?
                if (vr > 1f || vg > 1f || vb > 1f) aboveWhite++;

                vr *= gain;
                vg *= gain;
                vb *= gain;

                if (!Same(saturation, 1f))
                {
                    float luma = LumaR * vr + LumaG * vg + LumaB * vb;
                    vr = MathF.Max(0f, luma + (vr - luma) * saturation);
                    vg = MathF.Max(0f, luma + (vg - luma) * saturation);
                    vb = MathF.Max(0f, luma + (vb - luma) * saturation);
                }

                for (int t = 0; t < linearTools.Length; t++)
                    linearTools[t].Apply(ref vr, ref vg, ref vb);

                view.Apply(ref vr, ref vg, ref vb);

                vr = Tone(vr, black, span, inverseGamma, contrast);
                vg = Tone(vg, black, span, inverseGamma, contrast);
                vb = Tone(vb, black, span, inverseGamma, contrast);

                for (int t = 0; t < displayTools.Length; t++)
                    displayTools[t].Apply(ref vr, ref vg, ref vb);

                int br = ToByte(vr), bg = ToByte(vg), bb = ToByte(vb);

                histogram.Red[br]++;
                histogram.Green[bg]++;
                histogram.Blue[bb]++;
                histogram.Luma[(int)Math.Clamp(MathF.Round(LumaR * br + LumaG * bg + LumaB * bb), 0f, 255f)]++;
                sampled++;
            }
        }

        histogram.Finish(sampled);
        histogram.AboveWhite = sampled > 0 ? aboveWhite / (double)sampled : 0;
    }

    private static bool Same(float value, float reference) => MathF.Abs(value - reference) < 0.001f;

    private static byte ToByte(float value)
        => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
