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
    public static unsafe void Apply(FloatFrame frame, ImageAdjustments adjustments,
                                    IViewTransform view, IntPtr destination, int destinationStride)
    {
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

        Parallel.For(0, frame.Height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            byte* row = target + (long)y * destinationStride;

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                float vr = r[i], vg = g[i], vb = b[i];

                // --- lineare Seite ---

                if (gain != 1f)
                {
                    vr *= gain;
                    vg *= gain;
                    vb *= gain;
                }

                if (!Same(saturation, 1f))
                {
                    float luma = LumaR * vr + LumaG * vg + LumaB * vb;
                    vr = luma + (vr - luma) * saturation;
                    vg = luma + (vg - luma) * saturation;
                    vb = luma + (vb - luma) * saturation;

                    // Uebersaettigung kann unter null druecken; negatives Licht gibt
                    // es nicht, und die Sichtumwandlung koennte damit nichts anfangen.
                    if (vr < 0) vr = 0;
                    if (vg < 0) vg = 0;
                    if (vb < 0) vb = 0;
                }

                // --- Sichtumwandlung: ab hier sind es Anzeigewerte von 0 bis 1 ---

                view.Apply(ref vr, ref vg, ref vb);

                // --- Anzeigeseite ---

                if (needsTone)
                {
                    vr = Tone(vr, black, span, inverseGamma, contrast);
                    vg = Tone(vg, black, span, inverseGamma, contrast);
                    vb = Tone(vb, black, span, inverseGamma, contrast);
                }

                float alpha = a is null ? 1f : a[i];

                if (channel != ChannelView.All)
                {
                    switch (channel)
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

                byte* pixel = row + x * 4;
                pixel[0] = ToByte(vb);
                pixel[1] = ToByte(vg);
                pixel[2] = ToByte(vr);
                pixel[3] = ToByte(Math.Clamp(alpha, 0f, 1f));
            }
        });
    }

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
    {
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

                view.Apply(ref vr, ref vg, ref vb);

                vr = Tone(vr, black, span, inverseGamma, contrast);
                vg = Tone(vg, black, span, inverseGamma, contrast);
                vb = Tone(vb, black, span, inverseGamma, contrast);

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
