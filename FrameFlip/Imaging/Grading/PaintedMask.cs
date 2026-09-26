using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Eine gemalte Maske: ein Deckungswert je Bildpunkt, von Hand aufgetragen.
///
/// Alle anderen Maskenarten sind ABGELEITET - aus einer Helligkeit, einem Pass, einem
/// Objekt. Sie koennen deshalb sagen "wo es hell ist" oder "wo dieses Objekt steht",
/// aber nie "genau hier". Das ist die eine Frage, nach der man zuerst greift, und die
/// einzige, die ein Programm nicht fuer einen beantworten kann.
///
/// GROEBER ALS DAS BILD, und zwar bewusst. Eine Maske ist eine weiche Aussage ueber
/// Flaechen; ihre Kante wird ohnehin verlaufen. Bei 4K waeren es acht Millionen Bytes
/// je Maske und je Bild - gespeichert, geladen, kopiert. Ein Viertel der Kantenlaenge
/// ist ein Sechzehntel der Groesse und sieht man ihr nicht an, sobald der Pinsel
/// weiche Raender hat.
/// </summary>
public sealed class PaintedMask
{
    /// <summary>
    /// Um welchen Faktor gröber als das Bild.
    ///
    /// Vier ist der Wert, an dem eine 4K-Maske noch eine halbe Megabyte gross ist und
    /// eine Kante noch weich aussieht. Er steht hier und nicht an der Aufrufstelle,
    /// weil eine Maske, die mit einem anderen Faktor gemalt wurde als sie gelesen
    /// wird, um genau diesen Faktor verrutscht.
    /// </summary>
    public const int Coarse = 4;

    /// <summary>Breite in Maskenpunkten - also Bildbreite geteilt durch <see cref="Coarse"/>.</summary>
    public int Width { get; set; }

    /// <summary>Hoehe in Maskenpunkten.</summary>
    public int Height { get; set; }

    /// <summary>
    /// Die Deckung, gepackt und in Text gefasst.
    ///
    /// Gepackt, weil eine Maske fast ueberall denselben Wert hat - null oder voll -,
    /// und Deflate genau darauf gut ist: Eine leere Maske von 960 mal 540 schrumpft
    /// von einer halben Megabyte auf ein paar hundert Bytes. In Text gefasst, weil
    /// das Rezept eine JSON-Datei ist und Bytes dort nicht hingehoeren.
    /// </summary>
    public string Data { get; set; } = "";

    /// <summary>Die entpackte Deckung - gemerkt, weil das Entpacken je Bild sonst neu liefe.</summary>
    [JsonIgnore]
    private byte[]? _cover;

    /// <summary>
    /// Ob <see cref="Data"/> genau die Deckung traegt - nach dem Packen, bis zum naechsten
    /// Strich. Der Verlauf nimmt dann das Gepackte, statt ein zweites Mal zu packen.
    /// </summary>
    internal bool Kept { get; private set; }

    /// <summary>Legt eine leere Maske in der Groesse eines Bildes an.</summary>
    public static PaintedMask For(int imageWidth, int imageHeight)
    {
        int w = Math.Max(1, imageWidth / Coarse);
        int h = Math.Max(1, imageHeight / Coarse);

        return new PaintedMask
        {
            Width = w,
            Height = h,
            _cover = new byte[w * h],
            Data = "",
        };
    }

    /// <summary>Eine Maske aus einer fertigen Deckung - fuer den Verlauf, der Staende nachspielt.</summary>
    public static PaintedMask FromCover(int width, int height, byte[] cover) => new()
    {
        Width = width,
        Height = height,
        _cover = cover.ToArray(),
        Data = "",
    };

    /// <summary>
    /// Setzt die ganze Deckung auf einmal - beim Wiederherstellen eines Standes. In dasselbe
    /// Feld, damit wer es schon in der Hand hat, das Neue sieht. Passt die Groesse nicht,
    /// bleibt alles, wie es ist.
    /// </summary>
    public bool Replace(byte[] cover)
    {
        var own = Cover();
        if (cover.Length != own.Length) return false;

        Buffer.BlockCopy(cover, 0, own, 0, own.Length);
        Keep();

        return true;
    }

    /// <summary>
    /// Die Deckung als Feld - entpackt beim ersten Zugriff.
    ///
    /// Passt die gespeicherte Groesse nicht zu Breite und Hoehe, wird ein leeres Feld
    /// zurueckgegeben statt eines halben. Eine Maske, die zur Haelfte aus alten Daten
    /// besteht, waere schlimmer als gar keine: Man saehe einen Fehler und hielte ihn
    /// fuer den eigenen Pinselstrich.
    /// </summary>
    public byte[] Cover()
    {
        if (_cover is { } known && known.Length == Width * Height) return known;

        int count = Math.Max(1, Width * Height);

        if (Data.Length == 0)
        {
            _cover = new byte[count];
            return _cover;
        }

        try
        {
            var packed = Convert.FromBase64String(Data);

            using var input = new MemoryStream(packed);
            using var unpack = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            unpack.CopyTo(output);

            var bytes = output.ToArray();

            _cover = bytes.Length == count ? bytes : new byte[count];
            Kept = bytes.Length == count;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            _cover = new byte[count];
        }

        return _cover;
    }

    /// <summary>
    /// Ein Abdruck dessen, was die Maske gerade deckt - fuer den Zwischenspeicher der
    /// Knoten. Waehrend eines Pinselstrichs steht das Neue nur im entpackten Feld;
    /// <see cref="Data"/> kommt erst mit <see cref="Keep"/> nach. Ein Abdruck aus Data
    /// allein saehe den Strich nicht, und das Bild bliebe beim Malen stehen.
    /// </summary>
    internal string Print()
        => _cover is { } cover
            ? "c" + Convert.ToHexString(SHA256.HashData(cover))
            : "d" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Data)));

    /// <summary>Packt die Deckung wieder ein - nach jedem Pinselstrich.</summary>
    public void Keep()
    {
        if (_cover is null) return;

        Data = Pack(_cover);
        Kept = true;
    }

    /// <summary>Ein Feld von Deckungen gepackt und in Text gefasst - wie <see cref="Data"/>.</summary>
    public static string Pack(byte[] cover)
    {
        using var output = new MemoryStream();

        using (var pack = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            pack.Write(cover, 0, cover.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Entpackt ein Feld - oder null, wenn es nicht passt. Kein halbes Feld.</summary>
    public static byte[]? Unpack(string data, int count)
    {
        try
        {
            using var input = new MemoryStream(Convert.FromBase64String(data));
            using var unpack = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            unpack.CopyTo(output);

            var bytes = output.ToArray();
            return bytes.Length == count ? bytes : null;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Traegt einen runden Pinselstrich auf.
    ///
    /// Die Kante ist weich, und zwar ueber den halben Radius. Eine harte Kante waere
    /// bei einer groberen Maske eine Treppe, und weiche Raender sind ohnehin das, was
    /// man von einer Maske will - wer eine harte Kante braucht, nimmt eine Kryptomatte.
    ///
    /// <paramref name="value"/> ist 1 zum Auftragen und 0 zum Wegnehmen. Aufgetragen
    /// wird das MAXIMUM und weggenommen das Minimum: So baut ein zweiter Strich ueber
    /// demselben Ort nichts weiter auf, und ein Radiergang loescht wirklich.
    ///
    /// <paramref name="hardness"/> sagt, wie weit der volle Kern reicht: 0 ist ein
    /// Verlauf von der Mitte bis zum Rand, 1 eine Scheibe mit Kante. <paramref
    /// name="opacity"/> ist die Grenze, bis zu der ein Strich ueberhaupt auftraegt -
    /// bei 0,3 bleibt die Maske auch nach zehn Strichen bei knapp einem Drittel.
    /// </summary>
    public void Stroke(float imageX, float imageY, float imageRadius, float value, float flow,
                       float hardness = 0.5f, float opacity = 1f)
    {
        var cover = Cover();
        Kept = false;

        float cx = imageX / Coarse;
        float cy = imageY / Coarse;
        float radius = MathF.Max(0.75f, imageRadius / Coarse);

        int x0 = Math.Max(0, (int)MathF.Floor(cx - radius));
        int x1 = Math.Min(Width - 1, (int)MathF.Ceiling(cx + radius));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - radius));
        int y1 = Math.Min(Height - 1, (int)MathF.Ceiling(cy + radius));

        float inner = radius * Math.Clamp(hardness, 0f, 1f);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float away = MathF.Sqrt(dx * dx + dy * dy);

                if (away > radius) continue;

                float edge = away <= inner
                    ? 1f
                    : 1f - (away - inner) / MathF.Max(1e-4f, radius - inner);

                float strength = Math.Clamp(edge * flow, 0f, 1f);
                if (strength <= 0f) continue;

                int at = y * Width + x;
                float want = value * Math.Clamp(opacity, 0f, 1f) * 255f;

                cover[at] = value > 0.5f
                    ? (byte)MathF.Max(cover[at], cover[at] + (want - cover[at]) * strength)
                    : (byte)MathF.Min(cover[at], cover[at] + (want - cover[at]) * strength);
            }
        }
    }

    /// <summary>
    /// Bearbeitet die ganze Maske auf einmal: fuellen, leeren, umkehren, weiche Kante,
    /// ausweiten, schrumpfen. <paramref name="amount"/> ist die Weite in BILDpunkten, fuer
    /// die ersten drei ohne Bedeutung.
    ///
    /// Gerechnet in ganzen Zahlen und immer in derselben Reihenfolge - der Maskenverlauf
    /// spielt eine Bearbeitung nach wie einen Strich, und nachgespielt muss sie Byte fuer
    /// Byte dasselbe ergeben.
    /// </summary>
    public PaintBounds Apply(PaintEdit edit, float amount)
    {
        var cover = Cover();

        switch (edit)
        {
            case PaintEdit.Fill:
                Array.Fill(cover, (byte)255);
                break;

            case PaintEdit.Clear:
                Array.Clear(cover);
                break;

            case PaintEdit.Invert:
                for (int i = 0; i < cover.Length; i++) cover[i] = (byte)(255 - cover[i]);
                break;

            case PaintEdit.Feather:
                // Drei Durchgaenge eines Kastenfilters kommen einer Glocke nahe; der
                // Uebergang wird so etwa doppelt so breit wie die Weite.
                Feather(cover, Math.Max(1, (int)MathF.Round(amount / Coarse / 3f)));
                break;

            case PaintEdit.Grow:
            case PaintEdit.Shrink:
                Morph(cover, amount / Coarse, grow: edit == PaintEdit.Grow);
                break;

            default:
                return PaintBounds.Empty;
        }

        Kept = false;
        return new PaintBounds(0, 0, Width * Coarse, Height * Coarse);
    }

    private void Feather(byte[] cover, int radius)
    {
        var scratch = new byte[cover.Length];

        for (int pass = 0; pass < 3; pass++)
        {
            Box(cover, scratch, radius, along: 1, count: Width, lines: Height, lineStep: Width);
            Box(scratch, cover, radius, along: Width, count: Height, lines: Width, lineStep: 1);
        }
    }

    /// <summary>
    /// Ein Kastenfilter entlang von Zeilen oder Spalten. Am Rand wird der letzte Punkt
    /// wiederholt - so laeuft eine volle Maske am Bildrand nicht aus.
    /// </summary>
    private static void Box(byte[] from, byte[] to, int radius, int along, int count, int lines, int lineStep)
    {
        int n = 2 * radius + 1;

        Parallel.For(0, lines, line =>
        {
            int start = line * lineStep;
            int sum = 0;

            for (int k = -radius; k <= radius; k++) sum += from[start + Math.Clamp(k, 0, count - 1) * along];

            for (int i = 0; i < count; i++)
            {
                to[start + i * along] = (byte)((sum + n / 2) / n);
                sum += from[start + Math.Min(count - 1, i + radius + 1) * along] - from[start + Math.Max(0, i - radius) * along];
            }
        });
    }

    /// <summary>
    /// Ausweiten oder schrumpfen um einen Kreis vom Radius <paramref name="radius"/>
    /// Maskenpunkten: jeder Punkt nimmt den hellsten (dunkelsten) Wert in seiner Naehe. Ecken
    /// werden beim Ausweiten rund. Ausserhalb des Bildes gibt es nichts - der Rand zaehlt
    /// weder als voll noch als leer.
    /// </summary>
    private void Morph(byte[] cover, float radius, bool grow)
    {
        float r = MathF.Max(1f, radius);
        int reach = (int)MathF.Floor(r);

        var offsets = new List<(int X, int Y)>();
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
                if (dx * dx + dy * dy <= r * r + 1e-3f) offsets.Add((dx, dy));

        var source = (byte[])cover.Clone();
        int width = Width, height = Height;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                byte best = source[y * width + x];

                foreach (var (dx, dy) in offsets)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    byte value = source[ny * width + nx];
                    best = grow ? Math.Max(best, value) : Math.Min(best, value);
                }

                cover[y * width + x] = best;
            }
        });
    }

    /// <summary>
    /// Fuellt ein Vieleck - Rechteck, Ellipse oder Lasso. <paramref name="path"/> sind die
    /// Ecken in BILDpunkten, abwechselnd x und y; das Vieleck ist geschlossen, und wo es sich
    /// selbst kreuzt, gilt gerade-ungerade: Eine Schleife im Lasso wird ein Loch.
    ///
    /// Die Kante ist geglaettet: Jeder Maskenpunkt wird vier mal vier Mal abgetastet, und
    /// sein Anteil im Vieleck ist die Staerke, mit der er gefuellt wird - wie bei einem
    /// Tupfer, bis zur Deckkraft und nie ueber das hinaus, was schon da ist.
    ///
    /// Gerechnet Zeile fuer Zeile an den Schnittpunkten mit den Kanten und nicht Punkt fuer
    /// Punkt gegen alle Kanten: Ein Lasso hat Hunderte davon.
    /// </summary>
    public PaintBounds Fill(IReadOnlyList<float> path, float value, float opacity, byte[]? limit)
    {
        int n = path.Count / 2;
        if (n < 3) return PaintBounds.Empty;

        var cover = Cover();
        Kept = false;

        if (limit is not null && limit.Length != cover.Length) limit = null;

        var xs = new float[n];
        var ys = new float[n];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < n; i++)
        {
            xs[i] = path[2 * i] / Coarse;
            ys[i] = path[2 * i + 1] / Coarse;

            minX = MathF.Min(minX, xs[i]);
            minY = MathF.Min(minY, ys[i]);
            maxX = MathF.Max(maxX, xs[i]);
            maxY = MathF.Max(maxY, ys[i]);
        }

        int x0 = Math.Max(0, (int)MathF.Floor(minX));
        int x1 = Math.Min(Width - 1, (int)MathF.Ceiling(maxX));
        int y0 = Math.Max(0, (int)MathF.Floor(minY));
        int y1 = Math.Min(Height - 1, (int)MathF.Ceiling(maxY));

        if (x0 > x1 || y0 > y1) return PaintBounds.Empty;

        const int Sub = 4;
        int across = x1 - x0 + 1;
        var hits = new int[across];
        var crossings = new List<float>();
        float want = value * Math.Clamp(opacity, 0f, 1f) * 255f;

        for (int y = y0; y <= y1; y++)
        {
            Array.Clear(hits);

            for (int s = 0; s < Sub; s++)
            {
                float sy = y + (s + 0.5f) / Sub;
                crossings.Clear();

                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    // Halboffen: Ein Eckpunkt genau auf der Zeile zaehlt nur fuer eine Kante.
                    if ((ys[j] <= sy) == (ys[i] <= sy)) continue;

                    crossings.Add(xs[j] + (sy - ys[j]) * (xs[i] - xs[j]) / (ys[i] - ys[j]));
                }

                crossings.Sort();

                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    // Die Unterspalten, deren Mitte zwischen zwei Schnittpunkten liegt.
                    int first = Math.Max(0, (int)MathF.Ceiling((crossings[k] - x0) * Sub - 0.5f));
                    int last = Math.Min(across * Sub - 1, (int)MathF.Ceiling((crossings[k + 1] - x0) * Sub - 0.5f) - 1);

                    for (int c = first; c <= last; c++) hits[c / Sub]++;
                }
            }

            for (int x = 0; x < across; x++)
            {
                if (hits[x] == 0) continue;

                int at = y * Width + x0 + x;

                float strength = hits[x] / (float)(Sub * Sub);
                if (limit is not null) strength *= limit[at] / 255f;
                if (strength <= 0f) continue;

                cover[at] = value > 0.5f
                    ? (byte)MathF.Max(cover[at], cover[at] + (want - cover[at]) * strength)
                    : (byte)MathF.Min(cover[at], cover[at] + (want - cover[at]) * strength);
            }
        }

        return new PaintBounds(minX * Coarse - Coarse, minY * Coarse - Coarse, maxX * Coarse + Coarse, maxY * Coarse + Coarse);
    }

    /// <summary>
    /// Ein Tupfer mit einer Pinselspitze: rund oder eckig, gestreckt, gedreht - und
    /// wahlweise begrenzt durch ein Feld (<paramref name="limit"/>, gleich gross wie die
    /// Maske), etwa die Deckung eines Objekts aus der Kryptomatte.
    ///
    /// Eine schlichte runde Spitze ohne Begrenzung nimmt den alten Weg
    /// (<see cref="Stroke"/>) - Byte fuer Byte wie vorher, damit alte Striche im
    /// Maskenverlauf genau so nachspielen, wie sie gemalt wurden.
    ///
    /// Der Abstand zur Kante wird im gedrehten Rahmen der Spitze gemessen: bei einer
    /// runden Spitze als Ellipse, bei einer eckigen als Rechteck (der groessere der beiden
    /// Achsenanteile). Er laeuft von 0 in der Mitte bis zum Radius am Rand, und darauf
    /// wirkt die Haerte wie beim runden Pinsel.
    /// </summary>
    public void Stamp(float imageX, float imageY, float imageRadius, float value, float flow,
                      float hardness, float opacity, in BrushTip tip, byte[]? limit)
    {
        if (tip.IsPlainRound && limit is null)
        {
            Stroke(imageX, imageY, imageRadius, value, flow, hardness, opacity);
            return;
        }

        var cover = Cover();
        Kept = false;

        if (limit is not null && limit.Length != cover.Length) limit = null;

        float cx = imageX / Coarse;
        float cy = imageY / Coarse;
        float radius = MathF.Max(0.75f, imageRadius / Coarse);

        // Halbe Breite und Hoehe der Spitze; gestreckt wird die Hoehe schmaler.
        float aspect = Math.Clamp(tip.Aspect, 1f, 16f);
        float halfW = radius;
        float halfH = MathF.Max(0.5f, radius / aspect);

        // Ein gedrehtes Rechteck passt immer in den Kreis um seine Ecke. Beim Karo
        // reicht die lange Diagonale weiter hinaus.
        float squish = tip.Shape == BrushShape.Square ? BrushTip.SquishOf(tip.Squish) : 0f;
        float along = 1f + squish;
        float across = MathF.Sqrt(MathF.Max(0.0001f, 2f - along * along));

        float reach = MathF.Sqrt(halfW * halfW + halfH * halfH);
        if (squish > 0f) reach *= along;

        int x0 = Math.Max(0, (int)MathF.Floor(cx - reach));
        int x1 = Math.Min(Width - 1, (int)MathF.Ceiling(cx + reach));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - reach));
        int y1 = Math.Min(Height - 1, (int)MathF.Ceiling(cy + reach));

        float radians = tip.Angle * MathF.PI / 180f;
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);

        float inner = radius * Math.Clamp(hardness, 0f, 1f);
        float want = value * Math.Clamp(opacity, 0f, 1f) * 255f;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;

                // In den Rahmen der Spitze gedreht.
                float u = dx * cos + dy * sin;
                float v = -dx * sin + dy * cos;

                float nu = MathF.Abs(u) / halfW;
                float nv = MathF.Abs(v) / halfH;

                if (squish > 0f)
                {
                    // Zurueck ins Quadrat: die lange Diagonale gestaucht, die kurze gestreckt.
                    float su = u / halfW, sv = v / halfH;
                    float c1 = (su + sv) * 0.5f / along;
                    float c2 = (su - sv) * 0.5f / across;
                    nu = MathF.Abs(c1 + c2);
                    nv = MathF.Abs(c1 - c2);
                }

                float away = (tip.Shape == BrushShape.Square ? MathF.Max(nu, nv) : MathF.Sqrt(nu * nu + nv * nv)) * radius;

                if (away > radius) continue;

                float edge = away <= inner
                    ? 1f
                    : 1f - (away - inner) / MathF.Max(1e-4f, radius - inner);

                int at = y * Width + x;

                float strength = Math.Clamp(edge * flow, 0f, 1f);
                if (limit is not null) strength *= limit[at] / 255f;
                if (strength <= 0f) continue;

                cover[at] = value > 0.5f
                    ? (byte)MathF.Max(cover[at], cover[at] + (want - cover[at]) * strength)
                    : (byte)MathF.Min(cover[at], cover[at] + (want - cover[at]) * strength);
            }
        }
    }

    /// <summary>
    /// Der Deckungswert an einer Stelle des BILDES - bilinear zwischen vier
    /// Maskenpunkten.
    ///
    /// Bilinear und nicht der naechste Nachbar: Die Maske ist vier Mal groeber als das
    /// Bild, und ohne Zwischenwerte saehe jede Kante nach Treppe aus - genau das, was
    /// die weiche Pinselkante gerade vermeiden soll.
    /// </summary>
    public float At(int imageX, int imageY)
    {
        var cover = Cover();

        float fx = (imageX + 0.5f) / Coarse - 0.5f;
        float fy = (imageY + 0.5f) / Coarse - 0.5f;

        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);

        float tx = fx - x0;
        float ty = fy - y0;

        int x1 = Math.Clamp(x0 + 1, 0, Width - 1);
        int y1 = Math.Clamp(y0 + 1, 0, Height - 1);

        x0 = Math.Clamp(x0, 0, Width - 1);
        y0 = Math.Clamp(y0, 0, Height - 1);

        float a = cover[y0 * Width + x0] / 255f;
        float b = cover[y0 * Width + x1] / 255f;
        float c = cover[y1 * Width + x0] / 255f;
        float d = cover[y1 * Width + x1] / 255f;

        float top = a + (b - a) * tx;
        float bottom = c + (d - c) * tx;

        return top + (bottom - top) * ty;
    }

    /// <summary>Eine eigene Kopie - gebraucht, sobald ein Rezept festgehalten wird.</summary>
    public PaintedMask Clone() => new()
    {
        Width = Width,
        Height = Height,
        Data = Data,
    };
}
