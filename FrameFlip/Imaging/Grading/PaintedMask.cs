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

        using var output = new MemoryStream();

        using (var pack = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            pack.Write(_cover, 0, _cover.Length);
        }

        Data = Convert.ToBase64String(output.ToArray());
        Kept = true;
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
