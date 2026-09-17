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
    /// ist kontrastreich und gestreift statt gleichmaessig gekrisselt - und es ist
    /// das, wonach die meisten suchen, die "so wie frueher" sagen.
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
/// Rastern mit Fehlerdiffusion.
///
/// Der Unterschied zum geordneten Rastern steht in einem Satz: Dort entscheidet eine
/// Schwelle, die an der STELLE haengt; hier wird der Rundungsfehler an die Nachbarn
/// WEITERGEREICHT. Das Ergebnis streut organisch statt sich alle acht Punkte zu
/// wiederholen - der Blick alter Drucker statt alter Bildschirme.
///
/// WELCHES Schema man nimmt, entscheidet, wie es aussieht, und die Spanne ist gross:
/// Atkinson wirft ein Viertel des Fehlers weg und gibt harte, gestreifte Flaechen;
/// Jarvis streut ueber zwei Zeilen und gibt fast Korn. Ein einziges Schema
/// anzubieten und es "Fehlerdiffusion" zu nennen, waere so, als gaebe es nur einen
/// Pinsel.
///
/// GESCHLAENGELT statt zeilenweise: Jede zweite Zeile laeuft rueckwaerts. Immer in
/// dieselbe Richtung zu laufen schiebt den Fehler stets nach rechts, und auf grossen
/// gleichmaessigen Flaechen entstehen daraus Schlieren, die nach rechts wegziehen.
/// </summary>
public sealed class DiffusionTool : IFramePass
{
    public const string KindName = "diffusion";

    public string Kind => KindName;

    /// <summary>0 bis 1. Wie weit das Ergebnis zum gerasterten Bild gezogen wird.</summary>
    public float Amount { get; set; }

    /// <summary>Wieviele Stufen je Kanal uebrigbleiben. 2 ist reines Schwarzweiss je Kanal.</summary>
    public int Levels { get; set; } = 2;

    /// <summary>Nach welchem Schema gestreut wird.</summary>
    public DiffusionKernel Kernel { get; set; } = DiffusionKernel.FloydSteinberg;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f || Levels >= 64;

    private float _step;
    private float _amount;
    private (int X, int Y, float Weight)[] _spread = Array.Empty<(int, int, float)>();
    private int _reach;

    public void Prepare()
    {
        // Der Abstand zweier Stufen auf der Byteskala. Bei zwei Stufen ist er 255,
        // es gibt also nur 0 und 255.
        _step = 255f / Math.Max(1, Math.Clamp(Levels, 2, 64) - 1);
        _amount = Math.Clamp(Amount, 0f, 1f);
        _spread = Spread(Kernel);

        // Wie viele Zeilen nach unten das Schema reicht. Danach richtet sich, wie
        // viele Fehlerzeilen mitgefuehrt werden muessen - Floyd-Steinberg kommt mit
        // einer aus, Jarvis und Stucki brauchen zwei.
        _reach = 1;

        foreach (var (_, y, _) in _spread) _reach = Math.Max(_reach, y);
    }

    public unsafe void Apply(IntPtr pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || _spread.Length == 0) return;

        var target = (byte*)pixels;

        // Ein Ringpuffer aus so vielen Zeilen, wie das Schema nach unten reicht, plus
        // der laufenden. Mehr braucht es nie: Was weiter unten liegt, ist noch nicht
        // beschrieben worden.
        int rows = _reach + 1;
        var error = new float[rows][];

        for (int i = 0; i < rows; i++) error[i] = new float[width * 3];

        for (int y = 0; y < height; y++)
        {
            byte* row = target + (long)y * stride;
            float[] here = error[y % rows];

            bool backwards = (y & 1) == 1;

            int from = backwards ? width - 1 : 0;
            int stop = backwards ? -1 : width;
            int walk = backwards ? -1 : 1;

            for (int x = from; x != stop; x += walk)
            {
                for (int c = 0; c < 3; c++)
                {
                    float was = row[x * 4 + c];

                    // Der eigene Wert plus das, was die Nachbarn hierher gereicht
                    // haben. Es darf ueber 255 und unter 0 hinausgehen - der
                    // Ueberschuss wandert weiter, statt verlorenzugehen.
                    float wanted = was + here[x * 3 + c];

                    float stepped = Math.Clamp(MathF.Round(wanted / _step) * _step, 0f, 255f);
                    float rest = wanted - stepped;

                    // Weitergereicht wird der VOLLE Fehler, auch wenn die Staerke
                    // unter eins liegt. Sonst waere es keine Fehlerdiffusion mehr,
                    // sondern eine halbe - und die streut nicht, sie fleckt.
                    for (int s = 0; s < _spread.Length; s++)
                    {
                        var (dx, dy, weight) = _spread[s];

                        // "Rechts" heisst in einer rueckwaerts laufenden Zeile links.
                        // Ohne die Spiegelung schoebe die Umkehr den Fehler dorthin,
                        // wo schon gerechnet wurde, und er ginge verloren.
                        int nx = x + dx * walk;

                        if (nx < 0 || nx >= width) continue;
                        if (y + dy >= height) continue;

                        error[(y + dy) % rows][nx * 3 + c] += rest * weight;
                    }

                    // Und erst das Ergebnis wird gemischt. Bei Staerke null steht
                    // wieder der Ausgangswert da.
                    float shown = was + (stepped - was) * _amount;

                    row[x * 4 + c] = (byte)Math.Clamp((int)MathF.Round(shown), 0, 255);
                }
            }

            // Die abgearbeitete Zeile wird zur untersten des Ringpuffers und muss
            // dafuer leer sein. Sie erst spaeter zu leeren waere billiger und
            // fehleranfaelliger.
            Array.Clear(here);
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
