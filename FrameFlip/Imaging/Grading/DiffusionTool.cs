using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Rastern mit Fehlerdiffusion nach Floyd-Steinberg.
///
/// Der Unterschied zum geordneten Rastern steht in einem Satz: Dort entscheidet eine
/// Schwelle, die an der STELLE haengt; hier wird der Rundungsfehler an die Nachbarn
/// WEITERGEREICHT. Sieben Sechzehntel nach rechts, drei nach links unten, fuenf nach
/// unten, eines nach rechts unten - die Zahlen sind von 1976 und bis heute die
/// gebraeuchlichsten.
///
/// Das Ergebnis sieht anders aus, und das ist der Grund, warum es beides gibt:
/// Geordnet ergibt ein Kreuzmuster, das sich alle acht Punkte wiederholt.
/// Fehlerdiffusion streut organisch - auf weichen Verlaeufen bildet sie die
/// charakteristischen waagerechten Zuege, und im Hintergrund liegen die Punkte
/// verteilt statt gerastert. Wer den Blick alter Bildschirme will, nimmt das
/// geordnete; wer den Blick alter DRUCKER will, dieses.
///
/// GESCHLAENGELT statt zeilenweise: Jede zweite Zeile laeuft rueckwaerts. Immer in
/// dieselbe Richtung zu laufen schiebt den Fehler stets nach rechts, und auf grossen
/// gleichmaessigen Flaechen entstehen daraus Schlieren, die nach rechts wegziehen.
/// Die Umkehr kostet nichts und nimmt der Sache das Gerichtete.
/// </summary>
public sealed class DiffusionTool : IFramePass
{
    public const string KindName = "diffusion";

    public string Kind => KindName;

    /// <summary>0 bis 1. Wie weit das Ergebnis zum gerasterten Bild gezogen wird.</summary>
    public float Amount { get; set; }

    /// <summary>Wieviele Stufen je Kanal uebrigbleiben. 2 ist reines Schwarzweiss je Kanal.</summary>
    public int Levels { get; set; } = 2;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f || Levels >= 64;

    private float _step;
    private float _amount;

    public void Prepare()
    {
        // Der Abstand zweier Stufen auf der Byteskala. Bei zwei Stufen ist er 255,
        // es gibt also nur 0 und 255.
        _step = 255f / Math.Max(1, Math.Clamp(Levels, 2, 64) - 1);
        _amount = Math.Clamp(Amount, 0f, 1f);
    }

    public unsafe void Apply(IntPtr pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;

        var target = (byte*)pixels;

        // Zwei Zeilen Fehler: die laufende und die naechste. Mehr braucht es nicht -
        // Floyd-Steinberg reicht nur eine Zeile nach unten. Drei Kanaele je Punkt;
        // die Deckung bleibt unberuehrt, sie ist keine Farbe.
        var current = new float[width * 3];
        var below = new float[width * 3];

        for (int y = 0; y < height; y++)
        {
            byte* row = target + (long)y * stride;

            bool backwards = (y & 1) == 1;

            int from = backwards ? width - 1 : 0;
            int to = backwards ? -1 : width;
            int walk = backwards ? -1 : 1;

            for (int x = from; x != to; x += walk)
            {
                for (int c = 0; c < 3; c++)
                {
                    int at = x * 3 + c;

                    float was = row[x * 4 + c];

                    // Der eigene Wert plus das, was die Nachbarn hierher gereicht
                    // haben. Es darf ueber 255 und unter 0 hinausgehen - der
                    // Ueberschuss wandert weiter, statt verlorenzugehen.
                    float wanted = was + current[at];

                    float stepped = Math.Clamp(MathF.Round(wanted / _step) * _step, 0f, 255f);
                    float error = wanted - stepped;

                    // Weitergereicht wird der VOLLE Fehler, auch wenn die Staerke
                    // unter eins liegt. Sonst waere es keine Fehlerdiffusion mehr,
                    // sondern eine halbe - und die streut nicht, sie fleckt.
                    Spread(current, below, x, c, width, error, walk);

                    // Und erst das Ergebnis wird gemischt. Bei Staerke null steht
                    // wieder der Ausgangswert da.
                    float shown = was + (stepped - was) * _amount;

                    row[x * 4 + c] = (byte)Math.Clamp((int)MathF.Round(shown), 0, 255);
                }
            }

            // Die naechste Zeile wird zur laufenden, und der Platz der alten wird
            // wiederverwendet. Zwei Felder je Bild statt eines je Zeile.
            (current, below) = (below, current);

            Array.Clear(below);
        }
    }

    /// <summary>
    /// Verteilt den Fehler auf die vier Nachbarn - gespiegelt, wenn rueckwaerts
    /// gelaufen wird.
    ///
    /// "Rechts" heisst in einer rueckwaerts laufenden Zeile links. Ohne die
    /// Spiegelung schoebe die Umkehr den Fehler dorthin, wo schon gerechnet wurde,
    /// und er ginge verloren.
    /// </summary>
    private static void Spread(float[] current, float[] below,
                               int x, int c, int width, float error, int walk)
    {
        int ahead = x + walk;
        int behind = x - walk;

        if (ahead >= 0 && ahead < width) current[ahead * 3 + c] += error * (7f / 16f);
        if (behind >= 0 && behind < width) below[behind * 3 + c] += error * (3f / 16f);

        below[x * 3 + c] += error * (5f / 16f);

        if (ahead >= 0 && ahead < width) below[ahead * 3 + c] += error * (1f / 16f);
    }
}
