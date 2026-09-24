using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Werkzeug, das den ORT eines Bildpunktes braucht - und manchmal die Nummer des
/// Bildes dazu.
///
/// Die dritte Art nach den punktweisen und den oertlichen, und sie liegt dazwischen:
/// Sie braucht keine Nachbarschaft und damit keinen Zwischenpuffer, wohl aber die
/// Frage "wo bin ich". Eine Vignette ist an jedem Punkt dieselbe Rechnung mit einem
/// anderen Abstand zur Mitte; Korn ist an jedem Punkt dieselbe Rechnung mit einem
/// anderen Wurf.
///
/// Sie rechnen alle auf der linearen Seite, vor der Sichtumwandlung, und das ist
/// keine Bequemlichkeit: Eine Vignette ist Randabfall einer Linse, also eine
/// Multiplikation am Licht, und Korn ist eine Schwankung der Schwaerzung, also
/// ebenfalls eine am Licht. Hinter der Umwandlung waeren beide ein Effekt auf einem
/// fertigen Bild - das kann man machen, es sieht nur anders aus, und zwar schlechter,
/// weil die Ueberhellen dann schon fort sind.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(VignetteTool), VignetteTool.KindName)]
[JsonDerivedType(typeof(GrainTool), GrainTool.KindName)]
[JsonDerivedType(typeof(DitherTool), DitherTool.KindName)]
public interface IOpticsTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    /// <summary>Wann das Werkzeug an der Reihe ist - die Liste entscheidet nicht.</summary>
    OpticsStage Stage { get; }

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    bool IsNeutral { get; }

    void Prepare();

    /// <summary>
    /// Rechnet einen Bildpunkt um - lineares Licht hinein, lineares Licht heraus.
    /// </summary>
    /// <param name="place">Masse und Nummer des Bildes, einmal je Bild gerechnet.</param>
    void Apply(in OpticsPlace place, int x, int y, ref float r, ref float g, ref float b);
}

/// <summary>
/// Die Reihenfolge: erst die Linse, dann der Film.
///
/// Das ist die Reihenfolge, in der das Licht sie durchlaeuft, und sichtbar wird sie an
/// einer Stelle: Das Korn multipliziert, was die Linse durchgelassen hat, und ist in
/// den Ecken damit RELATIV genauso stark wie in der Mitte. Andersherum ginge das Korn
/// noch durch den Randabfall und waere in den Ecken schwaecher - die Ecken saehen
/// sauberer aus als die Mitte, genau umgekehrt zu echtem Material.
/// </summary>
public enum OpticsStage
{
    Lens = 0,
    Film = 1,
}

/// <summary>
/// Was ein Ortswerkzeug ueber das Bild wissen muss - einmal je Bild gerechnet und
/// dann unveraendert von allen Faeden gelesen.
///
/// Einmal und nicht je Bildpunkt: Der Abstand der Ecke, der Kehrwert der halben
/// Breite und die Umrechnung auf 1080p sind je Bild dieselben. Bei 4K waeren das
/// fuenfundzwanzig Millionen Wurzeln fuer eine Zahl, die sich nicht aendert.
/// </summary>
public readonly struct OpticsPlace
{
    public OpticsPlace(int width, int height, int number)
    {
        Width = width;
        Height = height;
        Number = number;

        CentreX = width / 2f;
        CentreY = height / 2f;

        // Beide Achsen in derselben Einheit - sonst waere die Vignette auf einem
        // breiten Bild ein Oval mit falschem Verhaeltnis.
        Unit = CentreX > 0f ? 1f / CentreX : 1f;

        float reachY = CentreY * Unit;
        Spread = 1f / MathF.Sqrt(1f + reachY * reachY);

        Detail = width / 1920f;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Die Bildnummer. Alles, was sich je Bild aendern MUSS, haengt daran.</summary>
    public int Number { get; }

    public float CentreX { get; }

    public float CentreY { get; }

    /// <summary>Kehrwert der halben Breite: Bildpunkte mal Unit ergibt -1 bis 1 ueber die Breite.</summary>
    public float Unit { get; }

    /// <summary>Faktor, der den Abstand zur Ecke auf genau eins bringt.</summary>
    public float Spread { get; }

    /// <summary>Bildbreite geteilt durch 1920 - fuer Groessen, die in Bildpunkten angegeben sind.</summary>
    public float Detail { get; }
}

/// <summary>
/// Vignette: der Randabfall einer Linse.
///
/// Sie multipliziert das Licht, und deshalb steht sie vor der Sichtumwandlung. Das
/// erspart ihr einen Regler, den andere Programme brauchen: den Lichterschutz. Der
/// ist dort noetig, weil ein fertiges Bild abzudunkeln aus Weiss ein Grau macht und
/// aus einer Lampe eine graue Scheibe. Hier wird eine Lampe mit 60 auf 30 gedaempft,
/// und die ist nach der Sichtumwandlung immer noch weiss. Ein Regler weniger, und
/// zwar nicht durch Weglassen, sondern weil die Frage an dieser Stelle nicht
/// auftritt.
///
/// Die Rundung ohne Potenzen: Zwischen Kreis, Rechteck und Raute wird gemischt.
/// Eine Superellipse waere die schoenere Mathematik und kostete drei Potenzen je
/// Bildpunkt - bei 4K fuenfundsiebzig Millionen fuer einen Unterschied, den niemand
/// benennen koennte.
/// </summary>
public sealed class VignetteTool : IOpticsTool
{
    public const string KindName = "vignette";

    public string Kind => KindName;

    public OpticsStage Stage => OpticsStage.Lens;

    /// <summary>-1 bis 1. Positiv dunkelt die Ecken ab, negativ hellt sie auf.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Wo der Abfall anfaengt: 0 in der Mitte, 1 in der Ecke.
    /// </summary>
    public float Midpoint { get; set; } = 0.5f;

    /// <summary>-1 ist eine Raute, 0 ein Kreis, 1 ein Rechteck.</summary>
    public float Roundness { get; set; }

    /// <summary>Wie weich der Uebergang ist, gemessen vom Anfangspunkt bis zur Ecke.</summary>
    public float Feather { get; set; } = 0.5f;

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.005f;

    private float _amount;
    private float _midpoint;
    private float _roundness;
    private float _inverseFeather;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, -1f, 1f);
        _midpoint = Math.Clamp(Midpoint, 0f, 1f);
        _roundness = Math.Clamp(Roundness, -1f, 1f);
        _inverseFeather = 1f / MathF.Max(0.01f, Math.Clamp(Feather, 0f, 1f));
    }

    public void Apply(in OpticsPlace place, int x, int y, ref float r, ref float g, ref float b)
    {
        float nx = (x + 0.5f - place.CentreX) * place.Unit;
        float ny = (y + 0.5f - place.CentreY) * place.Unit;

        float across = MathF.Abs(nx);
        float down = MathF.Abs(ny);

        float circle = MathF.Sqrt(nx * nx + ny * ny);

        float shape = _roundness >= 0f
            ? circle + (MathF.Max(across, down) - circle) * _roundness
            : circle + (across + down - circle) * -_roundness;

        float distance = shape * place.Spread;

        float t = (distance - _midpoint) * _inverseFeather;
        if (t <= 0f) return;

        // Sanft anfangen und sanft aufhoeren. Ein gerader Uebergang zeigt an seinem
        // Anfang eine Kante, und gerade auf einer glatten Flaeche sieht man sie.
        t = t >= 1f ? 1f : t * t * (3f - 2f * t);

        float gain = 1f - _amount * t;

        r *= gain;
        g *= gain;
        b *= gain;
    }
}

/// <summary>
/// Filmkorn.
///
/// Das eine Werkzeug im ganzen Atelier, das sich je Bild AENDERN muss. Ein Korn, das
/// in jedem Bild an derselben Stelle liegt, ist kein Korn, sondern Schmutz auf der
/// Linse - und es ist der genaue Gegensatz zur wichtigsten Regel des Programms, dass
/// eine Messung eingefroren sein muss. Das eine darf sich nicht bewegen, das andere
/// muss.
///
/// Die Bildnummer kommt aus dem Dateinamen und nicht aus der Stelle im Lauf. Damit
/// bekommt Bild 47 immer dasselbe Korn - in der Vorschau, im Export und auch dann,
/// wenn jemand nur die Bilder 30 bis 60 nachexportiert. Eine laufende Nummer waere
/// einfacher gewesen und haette beim zweiten Lauf ein anderes Korn ergeben.
///
/// Multipliziert, nicht addiert: Korn ist eine Schwankung der SCHWAERZUNG, also des
/// Anteils, der durchkommt. Damit bleibt Schwarz schwarz - addiertes Rauschen wuerde
/// die Schatten aufhellen -, und in den Lichtern faengt die Sichtumwandlung es
/// ohnehin ab. Genau dort ist auf Film auch am wenigsten Korn zu sehen.
/// </summary>
public sealed class GrainTool : IOpticsTool
{
    public const string KindName = "grain";

    /// <summary>
    /// Wieviel die volle Staerke wirklich bedeutet.
    ///
    /// Ein Regler von null bis eins soll ueber seinen ganzen Weg brauchbar sein.
    /// Volle Multiplikation waere eine Schwankung um plus/minus hundert Prozent -
    /// das ist kein Korn mehr, sondern Schnee.
    /// </summary>
    private const float FullSwing = 0.5f;

    public string Kind => KindName;

    public OpticsStage Stage => OpticsStage.Film;

    /// <summary>0 bis 1.</summary>
    public float Amount { get; set; }

    /// <summary>Die Korngroesse in Bildpunkten, bezogen auf 1080p.</summary>
    public int Size { get; set; } = 2;

    /// <summary>0 ist gleichmaessig, 1 mischt eine feinere zweite Lage darunter.</summary>
    public float Roughness { get; set; } = 0.4f;

    /// <summary>0 ist reines Helligkeitskorn, 1 laesst die Kanaele auseinanderlaufen.</summary>
    public float Colour { get; set; } = 0.2f;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f;

    private float _amount;
    private float _roughness;
    private float _colour;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, 0f, 1f) * FullSwing;
        _roughness = Math.Clamp(Roughness, 0f, 1f);
        _colour = Math.Clamp(Colour, 0f, 1f);
    }

    public void Apply(in OpticsPlace place, int x, int y, ref float r, ref float g, ref float b)
    {
        float grid = MathF.Max(1f, Size * place.Detail);
        float step = 1f / grid;

        Noise((x + 0.5f) * step, (y + 0.5f) * step, place.Number,
              out float nr, out float ng, out float nb);

        if (_roughness > 0.005f)
        {
            // Eine zweite, feinere Lage. Sie kostet vier weitere Wuerfe, und deshalb
            // wird sie nur geworfen, wenn jemand sie bestellt hat.
            Noise((x + 0.5f) * step * 2f, (y + 0.5f) * step * 2f, place.Number + 7919,
                  out float fr, out float fg, out float fb);

            nr += (fr - nr) * _roughness * 0.5f;
            ng += (fg - ng) * _roughness * 0.5f;
            nb += (fb - nb) * _roughness * 0.5f;
        }

        // Die Farbe des Korns: Der gemeinsame Anteil ist die Helligkeit, was darueber
        // hinausgeht, ist Farbe - und davon will man selten viel.
        float mean = (nr + ng + nb) * (1f / 3f);

        nr = mean + (nr - mean) * _colour;
        ng = mean + (ng - mean) * _colour;
        nb = mean + (nb - mean) * _colour;

        r *= 1f + nr * _amount;
        g *= 1f + ng * _amount;
        b *= 1f + nb * _amount;
    }

    /// <summary>
    /// Wertrauschen: Zufall an den Gitterpunkten, dazwischen weich verzogen.
    ///
    /// Ein Wurf je Gitterpunkt liefert alle drei Kanaele - eine Zufallszahl hat
    /// zweiunddreissig Bit, und Korn braucht keine zehn je Kanal. Vier Wuerfe je
    /// Bildpunkt statt zwoelf.
    /// </summary>
    private static void Noise(float fx, float fy, int seed,
                              out float r, out float g, out float b)
    {
        int ix = (int)MathF.Floor(fx);
        int iy = (int)MathF.Floor(fy);

        float tx = fx - ix;
        float ty = fy - iy;

        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        Corner(ix, iy, seed, out float r00, out float g00, out float b00);
        Corner(ix + 1, iy, seed, out float r10, out float g10, out float b10);
        Corner(ix, iy + 1, seed, out float r01, out float g01, out float b01);
        Corner(ix + 1, iy + 1, seed, out float r11, out float g11, out float b11);

        r = Mix(Mix(r00, r10, tx), Mix(r01, r11, tx), ty);
        g = Mix(Mix(g00, g10, tx), Mix(g01, g11, tx), ty);
        b = Mix(Mix(b00, b10, tx), Mix(b01, b11, tx), ty);
    }

    private static float Mix(float from, float to, float at) => from + (to - from) * at;

    /// <summary>Ein Gitterpunkt: drei Werte von -1 bis 1 aus einem Wurf.</summary>
    private static void Corner(int x, int y, int seed,
                               out float r, out float g, out float b)
    {
        uint h = Hash(x, y, seed);

        r = (h & 0x3FF) * (2f / 1023f) - 1f;
        g = ((h >> 10) & 0x3FF) * (2f / 1023f) - 1f;
        b = ((h >> 20) & 0x3FF) * (2f / 1023f) - 1f;
    }

    private static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 2246822519u);

            h = (h ^ (h >> 13)) * 1274126177u;

            return h ^ (h >> 16);
        }
    }
}
