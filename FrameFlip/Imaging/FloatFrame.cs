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

    /// <summary>
    /// True, wenn die Werte Szenenlicht sind - also aus einer EXR stammen und nach
    /// oben offen sind. False, wenn sie aus einem fertigen Bild zurueckgerechnet
    /// wurden und zwischen 0 und 1 liegen.
    ///
    /// Der Unterschied entscheidet ueber die Sichtumwandlung, und zwar folgenreich:
    /// Ein PNG ist bereits durch AgX gegangen, bevor es geschrieben wurde. Es ein
    /// zweites Mal hindurchzuschicken hiesse, die Bildwerdung doppelt anzuwenden -
    /// das Ergebnis waere flau und falsch. Solches Material braucht die einfache
    /// Umwandlung, die genau die Dekodierung umkehrt und damit nichts tut.
    /// </summary>
    public bool IsSceneReferred { get; init; } = true;

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
    /// Liest einen bestimmten Pass aus einer Multilayer-EXR.
    ///
    /// Gelesen werden nur dessen Kanaele. Das ist bei einer Datei mit zwanzig Passen
    /// der Unterschied zwischen drei Kanaelen und sechzig - und der Grund, warum der
    /// Leser ueberhaupt auswaehlen kann, welche er auspackt.
    /// </summary>
    /// <param name="pass">
    /// Der Gruppenname, etwa "ViewLayer.GlossDir". Leer heisst: die Farbkanaele, die
    /// das Bild ohnehin ergeben - dann ist es <see cref="FromExr"/>.
    /// </param>
    public static FloatFrame? FromExrPass(string path, string pass, int maxWidth = 0, int maxHeight = 0)
    {
        if (pass.Length == 0) return FromExr(path, maxWidth, maxHeight);

        try
        {
            using var stream = ExrReader.Open(path);
            var header = ExrHeaderReader.Read(stream);

            var found = ExrPasses.Find(ExrPasses.List(header), pass);
            if (found is not { } picked) return null;

            // Ein Graustufenpass nennt denselben Kanal dreimal; doppelt angefordert
            // liest der Leser ihn auch doppelt aus.
            var wanted = new List<string> { picked.Red };
            if (!wanted.Contains(picked.Green)) wanted.Add(picked.Green);
            if (!wanted.Contains(picked.Blue)) wanted.Add(picked.Blue);
            if (picked.Alpha is not null) wanted.Add(picked.Alpha);

            var image = ExrReader.Read(stream, header, wanted);

            var frame = new FloatFrame
            {
                Width = image.Width,
                Height = image.Height,
                R = image.Channel(picked.Red)!,
                G = image.Channel(picked.Green)!,
                B = image.Channel(picked.Blue)!,
                A = picked.Alpha is null ? null : image.Channel(picked.Alpha),
                Layer = picked.Name,
            };

            return maxWidth > 0 && maxHeight > 0 ? frame.Reduced(maxWidth, maxHeight) : frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Ein fertiges Bild zurueck in lineare Werte rechnen.
    ///
    /// Damit laesst sich auch ein PNG mit denselben Werkzeugen behandeln wie eine
    /// EXR. Was es NICHT zurueckbringt, ist die Zeichnung oberhalb von Weiss - die
    /// stand nie in der Datei. Der Belichtungsregler hebt hier also wirklich nur an,
    /// statt etwas zu holen, und genau deshalb traegt der Frame mit, woher er kommt.
    /// </summary>
    public static FloatFrame FromBgra32(byte[] pixels, int width, int height, int stride)
    {
        int count = width * height;
        var r = new float[count];
        var g = new float[count];
        var b = new float[count];
        var a = new float[count];

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;

            for (int x = 0; x < width; x++)
            {
                int at = row + x * 4;
                int i = y * width + x;

                b[i] = Srgb.Decode(pixels[at] / 255f);
                g[i] = Srgb.Decode(pixels[at + 1] / 255f);
                r[i] = Srgb.Decode(pixels[at + 2] / 255f);
                a[i] = pixels[at + 3] / 255f;
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
            IsSceneReferred = false,
        };
    }

    /// <summary>
    /// Um welchen ganzzahligen Faktor verkleinert werden muss, damit das Bild in die
    /// Grenzen passt. 1 heisst: es passt schon.
    ///
    /// Die Rechnung steht hier und nicht zweimal, weil sie zwei Fallen hat: Die
    /// Abbruchbedingung muss VOR dem Erhoehen pruefen, ob dabei noch ein Bild
    /// uebrigbliebe - sonst laeuft sie bei einem sehr schmalen Bild auf null Pixel
    /// hinaus. Und verkleinert wird nur, nie vergroessert: ein Bild, das ohnehin
    /// kleiner ist als die Grenze, bleibt wie es ist.
    ///
    /// Benutzt wird sie an zwei Stellen mit verschiedenem Ziel - der Decoder
    /// mittelt direkt nach Bgra32, <see cref="Reduced"/> in einen kleineren
    /// Gleitkommaframe. Die Schleifen zusammenzulegen haette den Decoder einen
    /// Zwischenpuffer gekostet, der bei 4K im zweistelligen Megabytebereich liegt
    /// und bei jedem Bild durch den Speicher ginge.
    /// </summary>
    public static int StepFor(int width, int height, int maxWidth, int maxHeight)
    {
        int step = 1;
        while (width / (step + 1) >= 1 && height / (step + 1) >= 1 &&
               (width / step > maxWidth || height / step > maxHeight))
        {
            step++;
        }

        return step;
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
        int step = StepFor(Width, Height, maxWidth, maxHeight);
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
            IsSceneReferred = IsSceneReferred,
        };
    }
}
