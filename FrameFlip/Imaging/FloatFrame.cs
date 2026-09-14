using FrameFlip.Decoding.Exr;

namespace FrameFlip.Imaging;

/// <summary>
/// Ein Bild in linearem Szenenlicht. Werte oberhalb von 1 sind der Normalfall und
/// kein Fehler - in ihnen steckt die Zeichnung, die ein PNG nicht mehr hat.
///
/// Das ist der zweite Weg neben dem Ringpuffer, nicht sein Ersatz: Die Wiedergabe
/// laeuft weiter ueber Bgra32, weil sie darauf ausgelegt ist. Dieser hier gilt dem
/// angehaltenen Bild, wo Zeit ist, mit den echten Werten zu rechnen.
/// </summary>
public sealed class FloatFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required float[] R { get; init; }

    public required float[] G { get; init; }

    public required float[] B { get; init; }

    /// <summary>Null, wenn die Datei keinen Alphakanal fuehrt.</summary>
    public float[]? A { get; init; }

    /// <summary>Aus welcher Ebene die Kanaele stammen - bei Multilayer etwa "ViewLayer.Combined".</summary>
    public string? Layer { get; init; }

    public int PixelCount => Width * Height;

    /// <summary>
    /// Der groesste Farbwert im Bild. Sagt, wieviel Reserve oberhalb von Weiss
    /// ueberhaupt vorhanden ist - bei einer aus 8 Bit erzeugten Datei ist er 1.
    /// </summary>
    public float Peak
    {
        get
        {
            float peak = 0;
            for (int i = 0; i < R.Length; i++)
            {
                if (R[i] > peak) peak = R[i];
                if (G[i] > peak) peak = G[i];
                if (B[i] > peak) peak = B[i];
            }

            return peak;
        }
    }

    /// <summary>
    /// Laedt die Farbkanaele einer EXR unveraendert - ohne Sichtumwandlung, ohne
    /// Beschneiden. Null, wenn die Datei sich nicht lesen laesst oder keine
    /// vollstaendigen Farbkanaele hat.
    /// </summary>
    /// <param name="maxWidth">
    /// Obergrenze der Breite, 0 fuer die volle. Gebraucht wird sie, weil der Frame
    /// dieselbe Groesse haben muss wie der Puffer, an dessen Stelle er tritt: die
    /// Anzeige dekodiert je nach Vorlauf auf 100, 50 oder 25 Prozent.
    /// </param>
    public static FloatFrame? FromExr(string path, int maxWidth = 0, int maxHeight = 0)
    {
        try
        {
            using var stream = ExrReader.Open(path);
            var header = ExrHeaderReader.Read(stream);

            var picked = ExrChannelPick.Colour(header);
            if (!picked.IsComplete) return null;

            var wanted = new List<string> { picked.Red!, picked.Green!, picked.Blue! };
            if (picked.Alpha is not null) wanted.Add(picked.Alpha);

            var image = ExrReader.Read(stream, header, wanted);

            var frame = new FloatFrame
            {
                Width = image.Width,
                Height = image.Height,
                R = image.Channel(picked.Red!)!,
                G = image.Channel(picked.Green!)!,
                B = image.Channel(picked.Blue!)!,
                A = picked.Alpha is null ? null : image.Channel(picked.Alpha),
                Layer = picked.Layer,
            };

            return maxWidth > 0 && maxHeight > 0 ? frame.Reduced(maxWidth, maxHeight) : frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Verkleinert ganzzahlig, durch Mittelung im linearen Licht.
    ///
    /// Gemittelt wird hier noch vor jeder Sichtumwandlung - das ist der Grund, warum
    /// die Verkleinerung ueberhaupt hier sitzt und nicht spaeter: ein Mittelwert aus
    /// bereits kodierten Anzeigewerten faellt zu dunkel aus.
    /// </summary>
    public FloatFrame Reduced(int maxWidth, int maxHeight)
    {
        int step = 1;
        while (Width / (step + 1) >= 1 && Height / (step + 1) >= 1 &&
               (Width / step > maxWidth || Height / step > maxHeight))
        {
            step++;
        }

        if (step == 1) return this;

        int width = Math.Max(1, Width / step);
        int height = Math.Max(1, Height / step);
        float divisor = step * step;

        var r = new float[width * height];
        var g = new float[width * height];
        var b = new float[width * height];
        var a = A is null ? null : new float[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sr = 0, sg = 0, sb = 0, sa = 0;

                for (int dy = 0; dy < step; dy++)
                {
                    int line = (y * step + dy) * Width + x * step;

                    for (int dx = 0; dx < step; dx++)
                    {
                        int i = line + dx;
                        sr += R[i];
                        sg += G[i];
                        sb += B[i];
                        if (A is not null) sa += A[i];
                    }
                }

                int at = y * width + x;
                r[at] = sr / divisor;
                g[at] = sg / divisor;
                b[at] = sb / divisor;
                if (a is not null) a[at] = sa / divisor;
            }
        }

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = r,
            G = g,
            B = b,
            A = a,
            Layer = Layer,
        };
    }
}
