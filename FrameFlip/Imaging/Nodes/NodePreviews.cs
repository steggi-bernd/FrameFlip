namespace FrameFlip.Imaging.Nodes;

/// <summary>
/// Kleine Bilder dessen, was Knoten ausgeben - fuer die Vorschau auf dem Knoten und die
/// Ebenenliste. Wie die Vorschau im Compositor von Blender: Man sieht, welche Ebene ein
/// Zweig ist, ohne den Knoten an die Ausgabe zu stecken.
///
/// Abgelesen wird beim Rechnen, aus dem Ergebnis, das ohnehin dasteht - eine eigene
/// Rechnung fuer die Vorschau gibt es nicht. Was der Zwischenspeicher auslaesst, behaelt
/// sein altes Bild; es hat sich ja nicht geaendert.
///
/// Ein Bild in Licht wird durch die Sichtumwandlung gezeigt, eines hinter ihr so, wie es
/// ist; eine Maske grau. Wo die Ebene nicht deckt, scheint der Grund des Knotens durch.
/// </summary>
public sealed class NodePreviews
{
    /// <summary>Wie gross eine Vorschau hoechstens ist - so breit, wie ein Knoten innen Platz hat.</summary>
    public const int Width = 160;

    public const int Height = 90;

    /// <summary>Ein kleines Bild, Bgra32, undurchsichtig.</summary>
    public sealed record Thumb(byte[] Bgra, int Width, int Height, long Version);

    private readonly Dictionary<string, Thumb> _thumbs = new(StringComparer.Ordinal);
    private long _version;

    /// <summary>Von welchen Knoten eine Vorschau gewuenscht ist - die Seite setzt es vor jeder Rechnung.</summary>
    public HashSet<string> Wanted { get; } = new(StringComparer.Ordinal);

    /// <summary>Zaehlt jede neue Vorschau - wer zeichnet, sieht daran, ob sich etwas getan hat.</summary>
    public long Version => Interlocked.Read(ref _version);

    public Thumb? For(string id)
    {
        lock (_thumbs) return _thumbs.GetValueOrDefault(id);
    }

    /// <summary>Vergisst die Vorschauen von Knoten, die es nicht mehr gibt.</summary>
    public void Keep(IEnumerable<string> alive)
    {
        var keep = new HashSet<string>(alive, StringComparer.Ordinal);

        lock (_thumbs)
            foreach (string id in _thumbs.Keys.Where(id => !keep.Contains(id)).ToList())
                _thumbs.Remove(id);
    }

    public void Clear()
    {
        lock (_thumbs) _thumbs.Clear();
    }

    /// <summary>
    /// Liest ein Ergebnis ab. <paramref name="display"/>: Es steht schon hinter der
    /// Sichtumwandlung und wird nicht noch einmal umgewandelt.
    /// </summary>
    internal void Capture(string id, object? value, NodeContext context, bool display)
    {
        if (value is not (GridImage or GridValue or SourceImage))
        {
            lock (_thumbs) _thumbs.Remove(id);
            return;
        }

        var (width, height) = Fit(context.Width, context.Height);
        var pixels = new byte[width * height * 4];

        // Der Grund des Knotens - dort, wo die Ebene nicht deckt.
        const float Ground = 0.14f;

        for (int ty = 0; ty < height; ty++)
        {
            for (int tx = 0; tx < width; tx++)
            {
                float r, g, b, a;

                switch (value)
                {
                    case GridImage image:
                    {
                        int gx = Math.Min(context.GridWidth - 1, tx * context.GridWidth / width);
                        int gy = Math.Min(context.GridHeight - 1, ty * context.GridHeight / height);
                        int i = gy * context.GridWidth + gx;

                        (r, g, b) = (image.Rgb[i * 3], image.Rgb[i * 3 + 1], image.Rgb[i * 3 + 2]);
                        a = Math.Clamp(image.A[i], 0f, 1f);

                        if (!display) context.View.Apply(ref r, ref g, ref b);
                        break;
                    }

                    case GridValue mask:
                    {
                        int gx = Math.Min(context.GridWidth - 1, tx * context.GridWidth / width);
                        int gy = Math.Min(context.GridHeight - 1, ty * context.GridHeight / height);

                        r = g = b = Math.Clamp(mask.V[gy * context.GridWidth + gx], 0f, 1f);
                        a = 1f;
                        break;
                    }

                    case SourceImage source:
                    {
                        var frame = source.Frame;
                        int x = Math.Min(frame.Width - 1, tx * frame.Width / width);
                        int y = Math.Min(frame.Height - 1, ty * frame.Height / height);
                        int i = y * frame.Width + x;

                        (r, g, b) = (frame.R[i], frame.G[i], frame.B[i]);
                        a = frame.A is null ? 1f : Math.Clamp(frame.A[i], 0f, 1f);

                        if (frame.IsSceneReferred) context.View.Apply(ref r, ref g, ref b);
                        break;
                    }

                    default:
                        r = g = b = Ground;
                        a = 1f;
                        break;
                }

                int at = (ty * width + tx) * 4;

                pixels[at] = Byte(b * a + Ground * (1f - a));
                pixels[at + 1] = Byte(g * a + Ground * (1f - a));
                pixels[at + 2] = Byte(r * a + Ground * (1f - a));
                pixels[at + 3] = 255;
            }
        }

        lock (_thumbs) _thumbs[id] = new Thumb(pixels, width, height, Interlocked.Increment(ref _version));
    }

    /// <summary>
    /// Ein kleines Bild eines gelesenen Bildes - fuer die Passe am Dateiknoten. Ein
    /// Graustufenpass, der ueber 0 bis 1 hinausgeht - eine Tiefe in Metern -, wird auf
    /// seine eigene Spanne bezogen wie bei der Passmaske, sonst waere er einfach weiss.
    /// Alles andere geht durch die Sichtumwandlung, wenn es Licht ist.
    /// </summary>
    public static Thumb Draw(FloatFrame frame, IViewTransform view)
    {
        var (width, height) = Fit(frame.Width, frame.Height);
        var pixels = new byte[width * height * 4];

        var (low, high) = frame.MaskRange;
        bool data = Grey(frame) && (high > 1.0001f || low < -0.0001f);
        float span = MathF.Max(1e-6f, high - low);

        const float Ground = 0.14f;

        for (int ty = 0; ty < height; ty++)
        {
            for (int tx = 0; tx < width; tx++)
            {
                int x = Math.Min(frame.Width - 1, tx * frame.Width / width);
                int y = Math.Min(frame.Height - 1, ty * frame.Height / height);
                int i = y * frame.Width + x;

                float r = frame.R[i], g = frame.G[i], b = frame.B[i];
                float a = frame.A is null ? 1f : Math.Clamp(frame.A[i], 0f, 1f);

                if (data)
                {
                    // Was "nicht getroffen" ist, steht ganz hinten.
                    r = g = b = r >= FloatFrame.NotHit || !float.IsFinite(r) ? 1f : Math.Clamp((r - low) / span, 0f, 1f);
                    a = 1f;
                }
                else if (frame.IsSceneReferred)
                {
                    view.Apply(ref r, ref g, ref b);
                }

                int at = (ty * width + tx) * 4;

                pixels[at] = Byte(b * a + Ground * (1f - a));
                pixels[at + 1] = Byte(g * a + Ground * (1f - a));
                pixels[at + 2] = Byte(r * a + Ground * (1f - a));
                pixels[at + 3] = 255;
            }
        }

        return new Thumb(pixels, width, height, 0);
    }

    /// <summary>Ob ein Bild grau ist - drei gleiche Kanaele, wie ein Tiefen- oder Nebelpass.</summary>
    internal static bool Grey(FloatFrame frame)
    {
        if (ReferenceEquals(frame.R, frame.G) && ReferenceEquals(frame.G, frame.B)) return true;

        for (int i = 0; i < frame.R.Length; i += 97)
            if (frame.R[i] != frame.G[i] || frame.G[i] != frame.B[i]) return false;

        return true;
    }

    /// <summary>Die Groesse der Vorschau - im Seitenverhaeltnis der Leinwand, so gross es geht.</summary>
    public static (int Width, int Height) Fit(int canvasWidth, int canvasHeight)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0) return (Width, Height);

        int height = (int)Math.Round((double)Width * canvasHeight / canvasWidth);
        if (height <= Height) return (Width, Math.Max(1, height));

        return ((int)Math.Max(1, Math.Round((double)Height * canvasWidth / canvasHeight)), Height);
    }

    private static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
}
