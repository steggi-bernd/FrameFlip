using System.Runtime.CompilerServices;

namespace FrameFlip.Imaging;

/// <summary>Verteilung der Helligkeitswerte, fuer Histogramm und Waveform.</summary>
public sealed class Histogram
{
    public int[] Red { get; } = new int[256];
    public int[] Green { get; } = new int[256];
    public int[] Blue { get; } = new int[256];
    public int[] Luma { get; } = new int[256];

    public int Peak { get; private set; }

    /// <summary>Anteil der Pixel, die oben bzw. unten anliegen - Hinweis auf Clipping.</summary>
    public double ClippedHigh { get; private set; }
    public double ClippedLow { get; private set; }

    public void Clear()
    {
        Array.Clear(Red);
        Array.Clear(Green);
        Array.Clear(Blue);
        Array.Clear(Luma);
        Peak = 0;
        ClippedHigh = 0;
        ClippedLow = 0;
    }

    internal void Finish(long sampled)
    {
        Peak = 0;

        // Die Randklassen bleiben bei der Spitzenwertsuche aussen vor: ein Bild mit
        // grossem schwarzem Hintergrund haette dort sonst einen Ausschlag, gegen den
        // der Rest der Verteilung nicht mehr sichtbar waere.
        for (int i = 1; i < 255; i++)
        {
            if (Luma[i] > Peak) Peak = Luma[i];
            if (Red[i] > Peak) Peak = Red[i];
            if (Green[i] > Peak) Peak = Green[i];
            if (Blue[i] > Peak) Peak = Blue[i];
        }

        if (Peak <= 0) Peak = 1;

        if (sampled > 0)
        {
            ClippedLow = Luma[0] / (double)sampled;
            ClippedHigh = Luma[255] / (double)sampled;
        }
    }
}

/// <summary>
/// Wendet die Anzeigekorrektur an und misst die Helligkeitsverteilung.
///
/// Geschrieben wird direkt in den Rueckpuffer der WriteableBitmap - ein
/// Zwischenpuffer in Framegroesse waere bei 1080p acht Megabyte, die bei jedem Bild
/// noch einmal durch den Speicher wandern. Der Quellpuffer bleibt unangetastet: er
/// gehoert dem Ringpuffer und wird von anderen Frames weiterverwendet.
///
/// Gerechnet wird durchgehend in Ganzzahlen. Die erste Fassung benutzte double je
/// Pixel und brauchte damit 72 ms fuer ein 1080p-Bild - bei 24 fps stehen aber nur
/// 41,7 ms zwischen zwei Bildern zur Verfuegung, die Korrektur haette also Bilder
/// gekostet. Mit Festkomma-Gewichten und einer vorberechneten Tonwertkurve bleibt
/// davon ein Bruchteil.
/// </summary>
public static class FrameProcessor
{
    // Rec.-709-Luminanz als 8-Bit-Festkomma: 0,2126 / 0,7152 / 0,0722 mal 256.
    // Die Summe ist genau 256, damit reines Weiss auch wieder 255 ergibt.
    private const int LumaR = 54;
    private const int LumaG = 183;
    private const int LumaB = 19;

    /// <summary>
    /// Kopiert einen Bgra32-Frame in das Ziel und wendet dabei die Korrektur an.
    ///
    /// Die Zeilen werden ueber die Kerne verteilt: einspurig braucht ein 1080p-Bild
    /// mit voller Korrektur rund 50 ms, mehr als der Bildabstand bei 24 fps zulaesst.
    /// Zeilen sind dafuer die natuerliche Einheit - sie beruehren einander nicht.
    ///
    /// Das Histogramm wird bewusst NICHT hier nebenbei gefuellt: eine Zaehlung waere
    /// der einzige Teil, der ueber die Threads hinweg zusammengefuehrt werden muesste,
    /// und sie wird ohnehin nur gebraucht, wenn das Panel offen ist. Dafuer gibt es
    /// <see cref="Measure"/>.
    /// </summary>
    public static unsafe void Apply(byte[] source, int width, int height, int stride,
                                    IntPtr destination, int destinationStride,
                                    ImageAdjustments adjustments)
    {
        byte* target = (byte*)destination.ToPointer();

        // Der haeufigste Fall ist "keine Korrektur". Der darf nicht mehr kosten als
        // ein Speicherkopiervorgang, sonst zahlt jede Wiedergabe fuer eine Funktion,
        // die gar nicht benutzt wird.
        if (adjustments.IsNeutral)
        {
            CopyRows(source, height, stride, target, destinationStride);
            return;
        }

        byte[]? tone = adjustments.NeedsToneCurve ? adjustments.BuildToneCurve() : null;
        bool mix = adjustments.NeedsChannelMix;

        // Saettigung als 8-Bit-Festkomma, damit die innere Schleife ohne
        // Gleitkomma auskommt.
        int saturation = (int)Math.Round(adjustments.Saturation * 256);
        var channel = adjustments.Channel;

        // Unter etwa einer Viertelmillion Pixel kostet das Verteilen mehr, als es
        // einbringt - dann bleibt es einspurig.
        int lanes = (long)width * height >= 250_000
            ? Math.Clamp(Environment.ProcessorCount, 1, 8)
            : 1;

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(source,
            System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            byte* sourceBase = (byte*)handle.AddrOfPinnedObject();
            byte[]? toneLocal = tone;

            void ProcessRows(int from, int to)
            {
                fixed (byte* toneBase = toneLocal)
                {
                    for (int y = from; y < to; y++)
                    {
                        byte* row = sourceBase + (long)y * stride;
                        byte* outRow = target + (long)y * destinationStride;

                        for (int x = 0; x < width; x++)
                        {
                            byte* pixel = row + x * 4;

                            int b = pixel[0], g = pixel[1], r = pixel[2];
                            byte a = pixel[3];

                            if (toneBase is not null)
                            {
                                b = toneBase[b];
                                g = toneBase[g];
                                r = toneBase[r];
                            }

                            if (mix)
                            {
                                if (saturation != 256)
                                {
                                    int luma = (LumaR * r + LumaG * g + LumaB * b) >> 8;
                                    r = Clamp(luma + (((r - luma) * saturation) >> 8));
                                    g = Clamp(luma + (((g - luma) * saturation) >> 8));
                                    b = Clamp(luma + (((b - luma) * saturation) >> 8));
                                }

                                switch (channel)
                                {
                                    case ChannelView.Red: g = b = r; break;
                                    case ChannelView.Green: r = b = g; break;
                                    case ChannelView.Blue: r = g = b; break;
                                    case ChannelView.Alpha: r = g = b = a; break;
                                    case ChannelView.Luminance:
                                        // Nach der Saettigung neu bilden, sonst zeigt
                                        // die Graustufenansicht die Helligkeit von vorher.
                                        r = g = b = (LumaR * r + LumaG * g + LumaB * b) >> 8;
                                        break;
                                }
                            }

                            byte* outPixel = outRow + x * 4;
                            outPixel[0] = (byte)b;
                            outPixel[1] = (byte)g;
                            outPixel[2] = (byte)r;
                            outPixel[3] = a;
                        }
                    }
                }
            }

            if (lanes <= 1)
            {
                ProcessRows(0, height);
                return;
            }

            int rowsPerLane = (height + lanes - 1) / lanes;
            System.Threading.Tasks.Parallel.For(0, lanes, lane =>
            {
                int from = lane * rowsPerLane;
                int to = Math.Min(height, from + rowsPerLane);
                if (from < to) ProcessRows(from, to);
            });
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Misst die Helligkeitsverteilung, ohne zu schreiben - wahlweise mit
    /// angewandter Tonwertkurve, damit im Histogramm steht, was auch zu sehen ist.
    /// </summary>
    /// <param name="step">Nur jedes n-te Pixel in beiden Richtungen. Bei 4 bleibt
    /// ein Sechzehntel der Arbeit, die Verteilung stimmt trotzdem.</param>
    public static unsafe void Measure(byte[] source, int width, int height, int stride,
                                      Histogram histogram, int step = 1,
                                      ImageAdjustments? adjustments = null)
    {
        histogram.Clear();
        long sampled = 0;
        step = Math.Max(1, step);

        byte[]? tone = adjustments is { NeedsToneCurve: true } ? adjustments.BuildToneCurve() : null;
        int saturation = adjustments is null ? 256 : (int)Math.Round(adjustments.Saturation * 256);

        fixed (byte* sourceBase = source)
        fixed (byte* toneBase = tone)
        {
            for (int y = 0; y < height; y += step)
            {
                byte* row = sourceBase + (long)y * stride;

                for (int x = 0; x < width; x += step)
                {
                    byte* pixel = row + x * 4;
                    int b = pixel[0], g = pixel[1], r = pixel[2];

                    if (toneBase is not null)
                    {
                        b = toneBase[b];
                        g = toneBase[g];
                        r = toneBase[r];
                    }

                    if (saturation != 256)
                    {
                        int luma = (LumaR * r + LumaG * g + LumaB * b) >> 8;
                        r = Clamp(luma + (((r - luma) * saturation) >> 8));
                        g = Clamp(luma + (((g - luma) * saturation) >> 8));
                        b = Clamp(luma + (((b - luma) * saturation) >> 8));
                    }

                    histogram.Red[r]++;
                    histogram.Green[g]++;
                    histogram.Blue[b]++;
                    histogram.Luma[(LumaR * r + LumaG * g + LumaB * b) >> 8]++;
                    sampled++;
                }
            }
        }

        histogram.Finish(sampled);
    }

    /// <summary>Zeilenweise kopieren; bei gleichem Zeilenabstand in einem Zug.</summary>
    private static unsafe void CopyRows(byte[] source, int height, int stride,
                                        byte* target, int destinationStride)
    {
        fixed (byte* sourceBase = source)
        {
            if (stride == destinationStride)
            {
                long bytes = (long)stride * height;
                Buffer.MemoryCopy(sourceBase, target, bytes, bytes);
                return;
            }

            for (int y = 0; y < height; y++)
                Buffer.MemoryCopy(sourceBase + (long)y * stride,
                                  target + (long)y * destinationStride,
                                  destinationStride, stride);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
