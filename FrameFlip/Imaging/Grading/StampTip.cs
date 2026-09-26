using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Die Spitze eines Stempelpinsels (docs/Atelier-Werkzeugplan.md, W1): ein kleines
/// Graustufenbild - Laub, ein Kratzer, ein Spritzer -, das der Pinsel bei jedem Tupfer
/// aufsetzt. Weiss malt, Schwarz nicht; hat die Datei Transparenz, zaehlt die Deckkraft.
///
/// Gepackt wie eine Maske und kaum groesser als ein paar Kilobyte: Sie reist in jedem Strich
/// mit (<see cref="PaintStroke.Tip"/>), damit der Maskenverlauf ihn ohne die PNG genau
/// nachspielt - auch wenn die Datei laengst woanders liegt.
/// </summary>
public sealed record StampTip(int Width, int Height, string Data)
{
    /// <summary>
    /// Die laengste Seite. Die Maske rechnet ohnehin in Feldern von 4 mal 4 Bildpunkten;
    /// eine Spitze von 128 reicht fuer einen Pinsel von 500 Bildpunkten Durchmesser.
    /// </summary>
    public const int MaxSide = 128;

    private byte[]? _values;

    /// <summary>Die Werte, zeilenweise - oder null, wenn die Daten nicht passen.</summary>
    public byte[]? Values() => _values ??= PaintedMask.Unpack(Data, Width * Height);

    /// <summary>
    /// Aus Pixeln in BGRA, 8 Bit je Kanal. Null, wenn nichts darin malt - eine leere Spitze
    /// waere ein Pinsel, der nichts tut, und das saehe aus wie ein Fehler.
    /// </summary>
    public static StampTip? FromBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;

        // Transparenz, sobald irgendein Punkt nicht deckt - dann traegt sie die Form, und
        // die Farbe darunter ist Zufall des Programms, das sie geschrieben hat.
        bool alpha = false;
        for (int i = 3; i < width * height * 4 && !alpha; i += 4) alpha = bgra[i] < 250;

        var full = new float[width * height];
        for (int i = 0; i < full.Length; i++)
        {
            int at = i * 4;
            full[i] = alpha
                ? bgra[at + 3]
                : 0.0722f * bgra[at] + 0.7152f * bgra[at + 1] + 0.2126f * bgra[at + 2];
        }

        float scale = MathF.Min(1f, MaxSide / (float)Math.Max(width, height));
        int w = Math.Max(1, (int)MathF.Round(width * scale));
        int h = Math.Max(1, (int)MathF.Round(height * scale));

        // Verkleinert als Mittel ueber die Punkte, die in ein Feld fallen.
        var values = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = y * height / h, y1 = Math.Max(y0 + 1, (y + 1) * height / h);

            for (int x = 0; x < w; x++)
            {
                int x0 = x * width / w, x1 = Math.Max(x0 + 1, (x + 1) * width / w);

                double sum = 0;
                for (int sy = y0; sy < y1; sy++)
                    for (int sx = x0; sx < x1; sx++)
                        sum += full[sy * width + sx];

                values[y * w + x] = (byte)Math.Clamp(Math.Round(sum / ((x1 - x0) * (y1 - y0))), 0, 255);
            }
        }

        return values.Any(v => v > 0) ? new StampTip(w, h, PaintedMask.Pack(values)) { _values = values } : null;
    }

    /// <summary>Liest eine Bilddatei als Spitze - oder null, wenn sie sich nicht lesen laesst oder nichts malt.</summary>
    public static StampTip? Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            BitmapSource source = decoder.Frames[0];
            if (source.Format != PixelFormats.Bgra32) source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = source.PixelWidth, height = source.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            var pixels = new byte[width * height * 4];
            source.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            return FromBgra(pixels, width, height);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or
                                        FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
