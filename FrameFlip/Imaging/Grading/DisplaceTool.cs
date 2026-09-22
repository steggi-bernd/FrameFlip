using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Woher die Verschiebung ihre Richtung nimmt.</summary>
public enum DisplaceFrom
{
    /// <summary>
    /// Der Normalpass: wohin eine Flaeche zeigt.
    ///
    /// Die Verzerrung folgt damit der GEOMETRIE. Eine Kugel schiebt rundherum nach
    /// aussen, eine Wand gar nicht, eine Kante genau an der Kante - das Bild wird
    /// verzogen, als haette man eine Glasscheibe davor, die die Form des Gegenstands
    /// hat.
    /// </summary>
    Normal,

    /// <summary>
    /// Der Vektorpass: wohin sich ein Punkt bewegt.
    ///
    /// Die Verzerrung folgt der BEWEGUNG. Was steht, bleibt scharf und unverzogen;
    /// was laeuft, zerreisst in Laufrichtung. Das ist der Riss, den ein
    /// Zeilensprungbild bei schneller Bewegung zeigt - nur ohne Zufall und genau
    /// dort, wo sich wirklich etwas bewegt.
    /// </summary>
    Motion,

    /// <summary>
    /// Keine Renderdaten: Die Welle allein schiebt, quer zu ihrer Laufrichtung.
    ///
    /// Der klassische Wellenfilter, und der einzige der drei, der auf JEDEM Bild
    /// laeuft - auch auf einem PNG. Er weiss nichts ueber die Szene und schiebt
    /// deshalb alles gleich: Vordergrund, Hintergrund und Himmel.
    ///
    /// Quer und nicht laengs, weil das der Riss ist, den man meint: Bei einer Welle,
    /// die von oben nach unten laeuft, verschieben sich waagerechte Baender nach
    /// links und rechts. Laengs waere ein Zoom in Streifen und sieht nach nichts aus.
    /// </summary>
    Screen,
}

/// <summary>
/// Verschiebung: Jeder Bildpunkt holt seine Farbe woanders her.
///
/// Der Grund, warum das hier steht und nicht in fuenfzig anderen Programmen: Die
/// Richtung kommt aus einem RENDERPASS. Ein Wellenfilter in einem Bildbearbeiter
/// kennt nur das fertige Bild und schiebt deshalb alles gleich - Vordergrund,
/// Hintergrund und Himmel. Hier weiss das Programm, wohin jede Flaeche zeigt und was
/// sich bewegt, und kann die Verzerrung daran entlanglegen.
///
/// Ueber die Richtung legt sich eine WELLE. Sie laeuft als Sinus ueber das Bild und
/// macht aus der gleichmaessigen Verschiebung Baender, die abwechselnd vor- und
/// zurueckschieben - das, was den Effekt nach Stoerung aussehen laesst statt nach
/// Weichzeichner. Ohne Welle bleibt die reine Verschiebung; mit voller Welle
/// schwingt sie um null.
///
/// Der Kanalversatz ist das Dritte: Rot, Gruen und Blau werden verschieden weit
/// geschoben. Ein Riss ohne Farbsaum sieht nach Fehler in der Datei aus, einer mit
/// nach einem Fehler im Signal - und das ist das Bild, das gemeint ist.
///
/// RUHT OHNE SEINEN PASS, wie alle Werkzeuge dieser Art - ausser auf "nur Welle"
/// gestellt. Dann braucht es nichts als das Bild und laeuft auch auf einem PNG. Eine
/// Datei ohne Normalpass ist kein Fehler, sondern eine Datei ohne Normalpass; ein
/// Werkzeug, das deswegen gar nicht zu gebrauchen ist, schon eher.
/// </summary>
public sealed class DisplaceTool : IDataTool
{
    public const string KindName = "displace";

    public string Kind => KindName;

    /// <summary>Woher die Richtung kommt - Geometrie oder Bewegung.</summary>
    public DisplaceFrom From { get; set; } = DisplaceFrom.Normal;

    /// <inheritdoc />
    [JsonIgnore]
    public PassNeed Needs => From switch
    {
        DisplaceFrom.Motion => PassNeed.Motion,
        DisplaceFrom.Screen => PassNeed.None,
        _ => PassNeed.Normal,
    };

    /// <summary>
    /// Auf "nur Welle" gestellt braucht es keinen Pass - und darf dann auch ohne
    /// einen laufen.
    /// </summary>
    [JsonIgnore]
    public bool Optional => From == DisplaceFrom.Screen;

    /// <summary>
    /// Vor der Tiefenschaerfe, zusammen mit der Bewegungsunschaerfe.
    ///
    /// Beides greift an der Szene an, nicht am Objektiv: Was verzogen ist, soll
    /// danach unscharf werden koennen - andersherum waere die Unschaerfe vom Verzug
    /// mitgerissen und liefe als scharfer Streifen mit.
    /// </summary>
    [JsonIgnore]
    public DataStage Stage => DataStage.Motion;

    /// <summary>Wie weit geschoben wird, in Bildpunkten.</summary>
    public float Amount { get; set; }

    /// <summary>Die Laenge einer Welle in Bildpunkten.</summary>
    public float Wavelength { get; set; } = 80f;

    /// <summary>
    /// Wieviel von der Verschiebung die Welle uebernimmt. 0 ist eine glatte
    /// Verschiebung, 1 eine, die um null schwingt.
    /// </summary>
    public float Wave { get; set; }

    /// <summary>In welcher Richtung die Welle ueber das Bild laeuft, in Grad.</summary>
    public float Angle { get; set; } = 90f;

    /// <summary>
    /// Wie weit die Kanaele auseinanderlaufen. 0 schiebt alle drei gleich weit.
    /// </summary>
    public float Spread { get; set; }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.01f;

    private float _amount, _wave, _spread, _turns;
    private float _sin, _cos;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, -400f, 400f);
        _wave = Math.Clamp(Wave, 0f, 1f);
        _spread = Math.Clamp(Spread, 0f, 1f);

        // Als Windungen je Bildpunkt, damit in der Schleife eine Multiplikation
        // steht und keine Division.
        float length = MathF.Max(2f, Wavelength);
        _turns = MathF.Tau / length;

        float radians = Angle * MathF.PI / 180f;
        _sin = MathF.Sin(radians);
        _cos = MathF.Cos(radians);
    }

    public void Run(LocalPass.Scratch scratch, FloatFrame? data, int[] columns, int[] rows,
                    int imageWidth, int step)
    {
        int gridWidth = columns.Length;
        int gridHeight = rows.Length;

        var source = scratch.Values;
        var target = scratch.Work;

        var alpha = scratch.Alpha;
        var alphaTarget = scratch.AlphaWork;

        // Ohne Pass schiebt die Welle allein - quer zu ihrer Laufrichtung. Das gilt
        // auch, wenn jemand den Pass verlangt hat und die Datei ihn nicht fuehrt:
        // Dann kaeme das Werkzeug hier ohnehin nicht an, ausser es ist auf "nur
        // Welle" gestellt.
        bool flat = data is null;

        var passX = data?.R;
        var passY = data?.G;

        int dataWidth = data?.Width ?? 1;
        int dataHeight = data?.Height ?? 1;

        float amount = _amount;
        float wave = _wave;
        float spread = _spread;
        float turns = _turns;
        float sin = _sin, cos = _cos;

        Parallel.For(0, gridHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            int line = Math.Min(rows[gy], dataHeight - 1) * dataWidth;
            int row = gy * gridWidth;


            for (int gx = 0; gx < gridWidth; gx++)
            {
                float dirX, dirY;

                if (flat)
                {
                    // Quer zur Laufrichtung der Welle: Laeuft sie von oben nach
                    // unten, schieben sich waagerechte Baender seitwaerts. Laengs
                    // waere ein Zoom in Streifen.
                    dirX = -sin;
                    dirY = cos;
                }
                else
                {
                    int at = line + Math.Min(columns[gx], dataWidth - 1);

                    dirX = Finite(passX![at]);
                    dirY = Finite(passY![at]);

                    // Y umgedreht, und zwar bei beiden Passen aus demselben Grund: In
                    // einem Bild zaehlt Y nach UNTEN, in einer Szene nach oben. Ohne
                    // das schoebe eine nach oben zeigende Flaeche nach unten - was
                    // niemand als Fehler erkennt, weil verzogen ja ohnehin verzogen
                    // aussieht.
                    dirY = -dirY;

                    float length = MathF.Sqrt(dirX * dirX + dirY * dirY);

                    // Wo der Pass nichts sagt, wird nichts geschoben. Das ist der
                    // Normalfall im Himmel und bei allem, was steht.
                    if (length < 1e-4f)
                    {
                        int quiet = (row + gx) * 3;

                        target[quiet] = source[quiet];
                        target[quiet + 1] = source[quiet + 1];
                        target[quiet + 2] = source[quiet + 2];

                        alphaTarget[row + gx] = alpha[row + gx];
                        continue;
                    }

                    dirX /= length;
                    dirY /= length;
                }

                // Die Welle laeuft in ihrer eigenen Richtung ueber das BILD, nicht
                // ueber das Gitter: Beim Reglerzug ist das Gitter groeber, und eine
                // Welle, die daran haengt, aenderte beim Loslassen ihre Laenge.
                float along = (columns[gx] * cos + rows[gy] * sin) * turns;

                // Ohne Welle die glatte Verschiebung, mit voller Welle eine, die um
                // null schwingt - und dazwischen alles.
                float strength = amount * (1f - wave + wave * MathF.Sin(along));

                // In Gitterpunkten, nicht in Bildpunkten.
                float baseX = dirX * strength / step;
                float baseY = dirY * strength / step;

                int out0 = (row + gx) * 3;

                if (spread <= 0f)
                {
                    Sample(source, alpha, gx + baseX, gy + baseY, gridWidth, gridHeight,
                           out float r, out float g, out float b, out float a);

                    target[out0] = r;
                    target[out0 + 1] = g;
                    target[out0 + 2] = b;

                    alphaTarget[row + gx] = (byte)Math.Clamp(MathF.Round(a), 0f, 255f);
                    continue;
                }

                // Drei Kanaele, drei Weiten. Die Deckung kommt aus der Mitte - ein
                // Rand, der in drei Farben verschieden weit reicht, saehe nach drei
                // Bildern aus statt nach einem Riss.
                Sample(source, alpha, gx + baseX * (1f - spread), gy + baseY * (1f - spread),
                       gridWidth, gridHeight, out float lr, out _, out _, out _);

                Sample(source, alpha, gx + baseX, gy + baseY, gridWidth, gridHeight,
                       out _, out float mg, out _, out float ma);

                Sample(source, alpha, gx + baseX * (1f + spread), gy + baseY * (1f + spread),
                       gridWidth, gridHeight, out _, out _, out float hb, out _);

                target[out0] = lr;
                target[out0 + 1] = mg;
                target[out0 + 2] = hb;

                alphaTarget[row + gx] = (byte)Math.Clamp(MathF.Round(ma), 0f, 255f);
            }
        });

        scratch.Values = target;
        scratch.Work = source;

        scratch.Alpha = alphaTarget;
        scratch.AlphaWork = alpha;
    }

    /// <summary>Nicht jede Zahl in einem Renderpass ist eine Richtung.</summary>
    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    /// <summary>
    /// Bilinear aus dem Gitter - und am Rand festgehalten statt gekachelt.
    ///
    /// Gekachelt liefe der Himmel von oben unten wieder herein, und eine Verschiebung
    /// nach unten holte den Boden an den Himmel. Festgehalten zieht sich der Rand in
    /// Streifen, und das sieht nach Verzug aus, was es ja auch ist.
    /// </summary>
    private static void Sample(float[] source, byte[] alpha, float x, float y,
                               int width, int height,
                               out float r, out float g, out float b, out float a)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float tx = x - x0;
        float ty = y - y0;

        int left = Math.Clamp(x0, 0, width - 1);
        int right = Math.Clamp(x0 + 1, 0, width - 1);
        int top = Math.Clamp(y0, 0, height - 1);
        int bottom = Math.Clamp(y0 + 1, 0, height - 1);

        int topLeft = (top * width + left) * 3;
        int topRight = (top * width + right) * 3;
        int bottomLeft = (bottom * width + left) * 3;
        int bottomRight = (bottom * width + right) * 3;

        r = Mix(source[topLeft], source[topRight], source[bottomLeft], source[bottomRight], tx, ty);
        g = Mix(source[topLeft + 1], source[topRight + 1],
                source[bottomLeft + 1], source[bottomRight + 1], tx, ty);
        b = Mix(source[topLeft + 2], source[topRight + 2],
                source[bottomLeft + 2], source[bottomRight + 2], tx, ty);

        a = Mix(alpha[top * width + left], alpha[top * width + right],
                alpha[bottom * width + left], alpha[bottom * width + right], tx, ty);
    }

    private static float Mix(float topLeft, float topRight, float bottomLeft, float bottomRight,
                             float tx, float ty)
    {
        float top = topLeft + (topRight - topLeft) * tx;
        float bottom = bottomLeft + (bottomRight - bottomLeft) * tx;

        return top + (bottom - top) * ty;
    }

    public DisplaceTool Clone() => new()
    {
        From = From,
        Amount = Amount,
        Wavelength = Wavelength,
        Wave = Wave,
        Angle = Angle,
        Spread = Spread,
    };
}
