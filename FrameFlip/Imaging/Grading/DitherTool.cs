using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Nach welchem Muster gerastert wird.</summary>
public enum DitherPattern
{
    /// <summary>
    /// Geordnet, nach einer Bayer-Matrix. Das sichtbare Kreuzmuster alter Rechner.
    /// </summary>
    Ordered,

    /// <summary>
    /// Zufaellig je Bildpunkt. Koerniger, ohne Muster - naeher an einem Film als an
    /// einem Bildschirm von 1985.
    /// </summary>
    Noise,

    /// <summary>
    /// Ein Linienraster: die klassische Schraffur, bei der die DICKE der Linie die
    /// Helligkeit traegt.
    ///
    /// Das ist die aelteste Art, Halbtoene zu drucken, und sie sieht voellig anders
    /// aus als die beiden anderen: kein Korn und kein Kreuz, sondern gleichmaessige
    /// Linien, die in hellen Stellen dick werden und in dunklen zu einem Faden
    /// abmagern. Ueber einer gewoelbten Flaeche legen sie sich wie Hoehenlinien -
    /// genau das, was ein Kupferstich tut.
    /// </summary>
    Lines,
}

/// <summary>
/// Rastern: das Bild auf wenige Stufen je Kanal bringen, ohne es in Flaechen zerfallen
/// zu lassen.
///
/// Der Kern ist eine Schwelle, die von der STELLE abhaengt. Wer ohne sie auf acht
/// Stufen rundet, bekommt Streifen - grosse Flaechen derselben Farbe mit harten
/// Kanten dazwischen, weil ein weicher Verlauf ueber hundert Bildpunkte an einer
/// einzigen Stelle umspringt. Eine Schwelle, die von Punkt zu Punkt schwankt, laesst
/// den Umsprung ueber die Flaeche wandern, und das Auge mittelt ihn wieder zusammen.
///
/// Deshalb ist es ein ORTSWERKZEUG und keine Kurve: Es braucht x und y. Genau daran
/// haengt auch, was es NICHT kann - Fehlerdiffusion nach Floyd-Steinberg reicht den
/// Rundungsfehler an die Nachbarn rechts und unten weiter und muss die Bildpunkte
/// deshalb der Reihe nach abarbeiten. Hier wird jeder Punkt fuer sich gerechnet, auf
/// beliebig vielen Faeden und auf einem groben Vorschauraster. Geordnet und zufaellig
/// gehen so, Floyd-Steinberg nicht - und das einzuraeumen ist ehrlicher, als eine
/// Naeherung unter seinem Namen anzubieten.
///
/// Gerechnet wird im ANZEIGERAUM. Rastern ist eine Aussage darueber, wieviele Stufen
/// man SIEHT; in linearem Licht laegen von acht Stufen sechs in den Tiefen.
/// </summary>
public sealed class DitherTool : IOpticsTool
{
    public const string KindName = "dither";

    public string Kind => KindName;

    /// <summary>
    /// Nach dem Korn.
    ///
    /// Ein Korn, das nach dem Rastern kaeme, brauchte Zwischenwerte, die es nach dem
    /// Rastern nicht mehr gibt - es wuerde die Stufen wieder verwischen und damit
    /// genau das aufheben, wofuer jemand gerastert hat.
    /// </summary>
    public OpticsStage Stage => OpticsStage.Film;

    /// <summary>0 bis 1. Wie weit das Ergebnis zum gerasterten Bild gezogen wird.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Wieviele Stufen je Kanal uebrigbleiben. 2 ist reines Schwarzweiss je Kanal.
    ///
    /// Ueber etwa vierzig ist nichts mehr zu sehen: Der Abstand zweier Stufen liegt
    /// dann unter dem, was ein Bildschirm ohnehin trennt.
    /// </summary>
    public int Levels { get; set; } = 6;

    /// <summary>Geordnet, zufaellig oder als Linienraster.</summary>
    public DitherPattern Pattern { get; set; } = DitherPattern.Ordered;

    /// <summary>
    /// Die Richtung der Linien, in Grad. Nur fuer <see cref="DitherPattern.Lines"/>.
    ///
    /// 0 sind waagerechte Linien, 90 senkrechte. Schraege dazwischen: Ein
    /// Linienraster bei 45 Grad ist das, was ein Drucker waehlt, wenn das Motiv
    /// selbst viel Waagerechtes hat - sonst legt sich das Raster ueber die Zeichnung
    /// und man sieht nur noch Streifen.
    /// </summary>
    public float Angle { get; set; }

    /// <summary>
    /// Die Kantenlaenge eines Rasterpunkts, in Bildpunkten bei 1080p.
    ///
    /// Bei 1 ist das Muster bei 4K so fein, dass es verschwindet - und dann hat man
    /// die Stufen ohne den Grund, aus dem man sie wollte. Bezogen auf 1080p, damit
    /// dasselbe Rezept in jeder Aufloesung gleich aussieht; das ist dieselbe Regel
    /// wie beim Korn.
    /// </summary>
    public int Size { get; set; } = 2;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f || Levels >= 64;

    private float _steps;
    private int _size;
    private float _sin, _cos;

    public void Prepare()
    {
        // Die Zahl der INTERVALLE, nicht der Stufen: Bei zwei Stufen - schwarz und
        // weiss - gibt es genau einen Sprung dazwischen.
        _steps = Math.Max(1, Math.Clamp(Levels, 2, 64) - 1);
        _size = Math.Clamp(Size, 1, 16);

        float radians = Angle * MathF.PI / 180f;

        _sin = MathF.Sin(radians);
        _cos = MathF.Cos(radians);
    }

    public void Apply(in OpticsPlace place, int x, int y, ref float r, ref float g, ref float b)
    {
        float amount = Math.Clamp(Amount, 0f, 1f);

        // Ein Rasterpunkt ist bei 4K doppelt so gross wie bei 1080p - sonst saehe
        // dasselbe Rezept in jeder Aufloesung anders aus.
        int cell = Math.Max(1, (int)MathF.Round(_size * place.Detail));

        float threshold = Threshold(Pattern, x, y, cell, _sin, _cos);

        r = Step(r, threshold, amount);
        g = Step(g, threshold, amount);
        b = Step(b, threshold, amount);
    }

    /// <summary>
    /// Ein Kanal: in den Anzeigeraum, runden, zurueck.
    ///
    /// Die Schwelle geht VOR dem Abschneiden hinein, nicht danach. Sie verschiebt den
    /// Wert um weniger als eine Stufe; welche Seite des Sprungs dabei herauskommt,
    /// entscheidet sie - und genau das ist die ganze Idee.
    /// </summary>
    private float Step(float light, float threshold, float amount)
    {
        // Ueber Weiss wird nicht gerastert. Ein Glanzpass mit Wert vierzig hat keine
        // Stufen zwischen null und eins, die sich aufteilen liessen, und ihn auf
        // Weiss zu beschneiden waere ein hoher Preis fuer eine Wirkung, die man dort
        // ohnehin nicht sieht.
        if (light > 1f) return light;

        float shown = Srgb.Encode(Math.Max(light, 0f));

        float stepped = MathF.Floor(shown * _steps + threshold) / _steps;

        stepped = Math.Clamp(stepped, 0f, 1f);

        return Srgb.Decode(shown + (stepped - shown) * amount);
    }

    // ------------------------------------------------------------------ Muster

    /// <summary>
    /// Die Bayer-Matrix, 8 mal 8 - einmal gebaut statt achtundsechzig Zahlen von Hand.
    ///
    /// Sie entsteht durch Verdoppeln: Aus einer Matrix M wird
    /// [[4M, 4M+2], [4M+3, 4M+1]]. Drei Verdopplungen aus dem Anfang [[0,2],[3,1]]
    /// ergeben acht mal acht. So aufgeschrieben ist sie nachvollziehbar; als Liste
    /// von vierundsechzig Zahlen waere sie eine Behauptung.
    /// </summary>
    private static readonly int[] Matrix = Build();

    private const int Side = 8;

    private static int[] Build()
    {
        int[] level = { 0, 2, 3, 1 };
        int side = 2;

        while (side < Side)
        {
            int next = side * 2;
            var grown = new int[next * next];

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int at = level[y * side + x] * 4;

                    grown[y * next + x] = at;
                    grown[y * next + x + side] = at + 2;
                    grown[(y + side) * next + x] = at + 3;
                    grown[(y + side) * next + x + side] = at + 1;
                }
            }

            level = grown;
            side = next;
        }

        return level;
    }

    /// <summary>
    /// Die Schwelle an einer Stelle - fuer alle drei Ortsmuster.
    ///
    /// Oeffentlich und statisch, weil sie an ZWEI Stellen gebraucht wird: hier, wo
    /// Punkt fuer Punkt gerechnet wird, und im Durchgang ueber den Rahmen, wo ganze
    /// Bloecke gerastert werden. Zwei Fassungen derselben Formel liefen frueher oder
    /// spaeter auseinander, und man saehe es nur daran, dass dasselbe Muster bei
    /// zwei Rasterpunktgroessen verschieden aussieht.
    /// </summary>
    public static float Threshold(DitherPattern pattern, int x, int y, int cell,
                                  float sin, float cos)
    {
        if (pattern == DitherPattern.Lines)
        {
            // Beim Linienraster ist der Rasterpunkt der ABSTAND zweier Linien, und
            // gerechnet wird auf dem vollen Gitter: Die Linie soll innerhalb einer
            // Periode anwachsen, und wer vorher auf Zellen rundet, hat genau die
            // Zwischenstufen weggeworfen, aus denen die Dicke entsteht.
            return Line(x, y, Math.Max(2, cell), sin, cos);
        }

        int cx = x / Math.Max(1, cell);
        int cy = y / Math.Max(1, cell);

        return pattern == DitherPattern.Noise ? Hash(cx, cy) : Bayer(cx, cy);
    }

    /// <summary>
    /// Die Schwelle eines Linienrasters - ein Dreieck quer zur Linienrichtung.
    ///
    /// Im Kern der Linie ist sie 1: Dort springt der Wert schon bei der geringsten
    /// Helligkeit um, die Linie steht also immer. Genau in der Mitte zwischen zwei
    /// Linien ist sie 0 und springt erst bei Weiss. Dazwischen waechst die Linie
    /// stetig - und DAS ist die Dicke, die die Helligkeit traegt.
    /// </summary>
    private static float Line(int x, int y, int period, float sin, float cos)
    {
        // Quer zur Linienrichtung: Bei 0 Grad laeuft die Schwelle mit y, die Linien
        // liegen also waagerecht.
        float across = (x * sin + y * cos) / period;

        float within = across - MathF.Floor(across);

        return MathF.Abs(within - 0.5f) * 2f;
    }

    /// <summary>Die Schwelle an einer Stelle, zwischen 0 und 1.</summary>
    private static float Bayer(int x, int y)
        => (Matrix[(y & (Side - 1)) * Side + (x & (Side - 1))] + 0.5f) / (Side * Side);

    /// <summary>
    /// Eine feste Zufallszahl zu einer Stelle.
    ///
    /// Fest und nicht gewuerfelt: Dieselbe Stelle muss in jedem Durchgang dieselbe
    /// Schwelle bekommen. Sonst flimmerte das Bild schon beim Ziehen an einem Regler,
    /// und in einer Sequenz kroche das Muster ueber das Bild.
    /// </summary>
    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393) + (uint)(y * 668265263);

        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;

        return (h & 0xFFFFFFu) / (float)0x1000000;
    }
}
