using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Nach welchem Schema der Rundungsfehler weitergereicht wird.
///
/// Es gibt nicht DIE Fehlerdiffusion, sondern ein knappes Dutzend, und sie sehen
/// deutlich verschieden aus. Der Unterschied liegt in zwei Dingen: wie WEIT der
/// Fehler gestreut wird, und ob ueberhaupt der ganze weitergereicht wird.
/// </summary>
public enum DiffusionKernel
{
    /// <summary>
    /// Floyd-Steinberg, 1976. Der Massstab, an dem alle anderen gemessen werden:
    /// eng gestreut, fein, ohne eigenen Charakter.
    /// </summary>
    FloydSteinberg,

    /// <summary>
    /// Atkinson - der Blick des ersten Macintosh.
    ///
    /// Er reicht nur SECHS Achtel des Fehlers weiter und wirft das letzte Viertel
    /// weg. Das ist kein Versehen: Was verlorengeht, fehlt den Nachbarn, und deshalb
    /// laufen helle Stellen ganz ins Weiss und dunkle ganz ins Schwarz. Das Ergebnis
    /// ist kontrastreich und gestreift statt gleichmaessig gekrisselt.
    /// </summary>
    Atkinson,

    /// <summary>
    /// Jarvis, Judice und Ninke. Streut ueber zwei Zeilen und fuenf Spalten - viel
    /// weicher als Floyd-Steinberg, und auf Verlaeufen fast koernig.
    /// </summary>
    JarvisJudiceNinke,

    /// <summary>Stucki. Wie Jarvis, aber mit mehr Gewicht in der Naehe - schaerfer.</summary>
    Stucki,

    /// <summary>Burkes. Stucki ohne die zweite Zeile: schneller, etwas haerter.</summary>
    Burkes,

    /// <summary>Sierra. Zwischen Floyd-Steinberg und Stucki, mit ruhigen Flaechen.</summary>
    Sierra,
}

/// <summary>
/// Rastern ueber den ganzen Rahmen - mit einer RASTERPUNKTGROESSE.
///
/// Das ist der Unterschied zwischen einem Raster und Gries, und er kostete eine
/// Runde: Ein 4K-Bild auf zwei Stufen zu bringen ergibt Punkte von einem Bildpunkt
/// Kantenlaenge. Aus zwei Metern Abstand ist das kein Muster mehr, sondern ein
/// gleichmaessiges Rauschen - "sieht fritiert aus" trifft es genau. Jedes Programm,
/// das fuer diesen Blick gebaut ist, verkleinert deshalb ERST, rastert DANN und
/// vergroessert hinterher mit harten Kanten zurueck.
///
/// Genau das tut diese Klasse. Sie liest nicht Bildpunkte, sondern BLOECKE: Ein Block
/// wird gemittelt, als ein Wert gerastert und wieder als Ganzes hingeschrieben. Bei
/// Groesse eins ist das der alte Weg, Punkt fuer Punkt - dieselbe Schleife, kein
/// zweiter Code.
///
/// Und sie kann alle neun Verfahren, nicht nur die sechs Fehlerdiffusionen. Die drei
/// ortsabhaengigen rechnen dieselben Schwellen wie das Ortswerkzeug daneben - die
/// Formeln stehen einmal, in DitherTool, und werden von hier aufgerufen. Zwei
/// Fassungen liefen frueher oder spaeter auseinander.
/// </summary>
public sealed class DiffusionTool : IFramePass
{
    public const string KindName = "diffusion";

    public string Kind => KindName;

    /// <summary>0 bis 1. Wie weit das Ergebnis zum gerasterten Bild gezogen wird.</summary>
    public float Amount { get; set; }

    /// <summary>Wieviele Stufen je Kanal uebrigbleiben. 2 ist reines Schwarzweiss je Kanal.</summary>
    public int Levels { get; set; } = 2;

    /// <summary>
    /// Die Kantenlaenge eines Rasterpunkts in Bildpunkten.
    ///
    /// Bei 1 wird jeder Bildpunkt einzeln entschieden - das ist die feinste und
    /// unauffaelligste Rasterung und gleichzeitig die, die bei hoher Aufloesung wie
    /// Rauschen aussieht. Vier bis acht ist der Bereich, in dem das Muster wieder ein
    /// Muster wird.
    ///
    /// NICHT auf 1080p bezogen, anders als beim Korn: Hier geht es um die Groesse auf
    /// dem SCHIRM, und die haengt an Bildpunkten und nicht an der Aufloesung der
    /// Datei.
    /// </summary>
    public int Pixels { get; set; } = 1;

    /// <summary>
    /// True, wenn ein ortsabhaengiges Muster gerechnet wird statt einer Diffusion.
    /// </summary>
    public bool Place { get; set; }

    /// <summary>Welches Ortsmuster - nur wenn <see cref="Place"/> gilt.</summary>
    public DitherPattern Pattern { get; set; } = DitherPattern.Ordered;

    /// <summary>Die Linienrichtung in Grad - nur fuer das Linienraster.</summary>
    public float Angle { get; set; }

    /// <summary>Nach welchem Schema gestreut wird - nur wenn nicht <see cref="Place"/>.</summary>
    public DiffusionKernel Kernel { get; set; } = DiffusionKernel.FloydSteinberg;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f || Levels >= 64;

    private float _step;
    private float _amount;
    private int _block;
    private float _sin, _cos;
    private (int X, int Y, float Weight)[] _spread = Array.Empty<(int, int, float)>();
    private int _reach;

    public void Prepare()
    {
        _step = 255f / Math.Max(1, Math.Clamp(Levels, 2, 64) - 1);
        _amount = Math.Clamp(Amount, 0f, 1f);
        _block = Math.Clamp(Pixels, 1, 64);

        float radians = Angle * MathF.PI / 180f;

        _sin = MathF.Sin(radians);
        _cos = MathF.Cos(radians);

        _spread = Spread(Kernel);
        _reach = 1;

        foreach (var (_, y, _) in _spread) _reach = Math.Max(_reach, y);
    }

    public unsafe void Apply(IntPtr pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;

        var target = (byte*)pixels;

        // Das Bild in Bloecken. Bei Groesse eins ist ein Block ein Bildpunkt, und
        // alles darunter laeuft unveraendert weiter.
        int across = (width + _block - 1) / _block;
        int down = (height + _block - 1) / _block;

        int rows = _reach + 1;
        var error = new float[rows][];

        for (int i = 0; i < rows; i++) error[i] = new float[across * 3];

        for (int by = 0; by < down; by++)
        {
            float[] here = error[by % rows];

            // Geschlaengelt: Jede zweite Zeile rueckwaerts. Immer in dieselbe Richtung
            // zu laufen schiebt den Fehler stets nach rechts, und auf grossen Flaechen
            // entstehen daraus Schlieren, die nach rechts wegziehen.
            bool backwards = !Place && (by & 1) == 1;

            int from = backwards ? across - 1 : 0;
            int stop = backwards ? -1 : across;
            int walk = backwards ? -1 : 1;

            for (int bx = from; bx != stop; bx += walk)
            {
                float threshold = Place
                    ? DitherTool.Threshold(Pattern, bx, by, 1, _sin, _cos)
                    : 0f;

                for (int c = 0; c < 3; c++)
                {
                    float was = Average(target, stride, width, height, bx, by, c);

                    float wanted = Place ? was : was + here[bx * 3 + c];

                    float stepped = Place
                        ? Math.Clamp(MathF.Floor(wanted / _step + threshold) * _step, 0f, 255f)
                        : Math.Clamp(MathF.Round(wanted / _step) * _step, 0f, 255f);

                    if (!Place)
                    {
                        // Weitergereicht wird der VOLLE Fehler, auch wenn die Staerke
                        // unter eins liegt. Sonst waere es keine Fehlerdiffusion mehr,
                        // sondern eine halbe - und die streut nicht, sie fleckt.
                        float rest = wanted - stepped;

                        for (int s = 0; s < _spread.Length; s++)
                        {
                            var (dx, dy, weight) = _spread[s];

                            // "Rechts" heisst in einer rueckwaerts laufenden Zeile
                            // links. Ohne die Spiegelung schoebe die Umkehr den Fehler
                            // dorthin, wo schon gerechnet wurde.
                            int nx = bx + dx * walk;

                            if (nx < 0 || nx >= across) continue;
                            if (by + dy >= down) continue;

                            error[(by + dy) % rows][nx * 3 + c] += rest * weight;
                        }
                    }

                    // Und erst das Ergebnis wird gemischt. Bei Staerke null steht
                    // wieder der Ausgangswert da.
                    Fill(target, stride, width, height, bx, by, c,
                         was + (stepped - was) * _amount);
                }
            }

            Array.Clear(here);
        }
    }

    /// <summary>Der Mittelwert eines Blocks in einem Kanal.</summary>
    private unsafe float Average(byte* target, int stride, int width, int height,
                                 int bx, int by, int c)
    {
        int x0 = bx * _block, y0 = by * _block;
        int x1 = Math.Min(x0 + _block, width);
        int y1 = Math.Min(y0 + _block, height);

        if (x1 <= x0 || y1 <= y0) return 0f;

        int sum = 0;

        for (int y = y0; y < y1; y++)
        {
            byte* row = target + (long)y * stride;

            for (int x = x0; x < x1; x++) sum += row[x * 4 + c];
        }

        return (float)sum / ((x1 - x0) * (y1 - y0));
    }

    /// <summary>Schreibt einen Wert auf den ganzen Block - harte Kanten, kein Verlauf.</summary>
    private unsafe void Fill(byte* target, int stride, int width, int height,
                             int bx, int by, int c, float value)
    {
        byte shown = (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

        int x0 = bx * _block, y0 = by * _block;
        int x1 = Math.Min(x0 + _block, width);
        int y1 = Math.Min(y0 + _block, height);

        for (int y = y0; y < y1; y++)
        {
            byte* row = target + (long)y * stride;

            for (int x = x0; x < x1; x++) row[x * 4 + c] = shown;
        }
    }

    /// <summary>
    /// Die Gewichte eines Schemas: wohin welcher Anteil des Fehlers geht.
    ///
    /// Die Zahlen stehen als Bruch da, wie in den Veroeffentlichungen - Zaehler und
    /// Nenner getrennt. Als Kommazahlen waeren sie nicht mehr gegen die Quelle zu
    /// pruefen, und genau das will man bei einer Tabelle aus vierzig Zahlen koennen.
    ///
    /// Bei Atkinson ist die Summe der Zaehler ABSICHTLICH kleiner als der Nenner:
    /// sechs von acht. Das fehlende Viertel ist der Grund, warum es so aussieht, wie
    /// es aussieht.
    /// </summary>
    private static (int X, int Y, float Weight)[] Spread(DiffusionKernel kernel)
    {
        var (taps, divisor) = kernel switch
        {
            DiffusionKernel.Atkinson => (new[]
            {
                (1, 0, 1), (2, 0, 1),
                (-1, 1, 1), (0, 1, 1), (1, 1, 1),
                (0, 2, 1),
            }, 8),

            DiffusionKernel.JarvisJudiceNinke => (new[]
            {
                (1, 0, 7), (2, 0, 5),
                (-2, 1, 3), (-1, 1, 5), (0, 1, 7), (1, 1, 5), (2, 1, 3),
                (-2, 2, 1), (-1, 2, 3), (0, 2, 5), (1, 2, 3), (2, 2, 1),
            }, 48),

            DiffusionKernel.Stucki => (new[]
            {
                (1, 0, 8), (2, 0, 4),
                (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2),
                (-2, 2, 1), (-1, 2, 2), (0, 2, 4), (1, 2, 2), (2, 2, 1),
            }, 42),

            DiffusionKernel.Burkes => (new[]
            {
                (1, 0, 8), (2, 0, 4),
                (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2),
            }, 32),

            DiffusionKernel.Sierra => (new[]
            {
                (1, 0, 5), (2, 0, 3),
                (-2, 1, 2), (-1, 1, 4), (0, 1, 5), (1, 1, 4), (2, 1, 2),
                (-1, 2, 2), (0, 2, 3), (1, 2, 2),
            }, 32),

            _ => (new[]
            {
                (1, 0, 7),
                (-1, 1, 3), (0, 1, 5), (1, 1, 1),
            }, 16),
        };

        var spread = new (int, int, float)[taps.Length];

        for (int i = 0; i < taps.Length; i++)
        {
            var (x, y, weight) = taps[i];

            spread[i] = (x, y, weight / (float)divisor);
        }

        return spread;
    }
}
