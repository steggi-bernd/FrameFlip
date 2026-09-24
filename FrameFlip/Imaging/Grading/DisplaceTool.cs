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
/// Die Form der Welle - und damit, ob die Verschiebung schwingt oder reisst.
///
/// Die ersten vier sind die aus jedem Wellenfilter (After Effects nennt sie beim
/// Wave Warp genauso). Die letzten beiden sind die eigentlichen Glitch-Formen: Sie
/// schwingen nicht, sie SPRINGEN - und das ist der Unterschied zwischen einer
/// Wasseroberflaeche und einem kaputten Signal.
/// </summary>
public enum WaveShape
{
    /// <summary>Weich hin und her. Wasser, Hitzeflimmern.</summary>
    Sine,

    /// <summary>Gerade Rampen mit Spitzen. Wie Sinus, aber mit Knick - wirkt mechanischer.</summary>
    Triangle,

    /// <summary>
    /// Nur zwei Stellungen, hart umgeschaltet. Die Kante zerfaellt in Treppen, und
    /// zwischen den Stufen liegt nichts.
    /// </summary>
    Square,

    /// <summary>Langsam hinauf, jaeh zurueck. Wie ein Bild, das waagerecht den Halt verliert.</summary>
    Saw,

    /// <summary>
    /// Streifen: Das Bild zerfaellt in Baender, und jedes wird um einen eigenen,
    /// zufaelligen Betrag verschoben.
    ///
    /// Das ist der Riss, den "Glitch" meint - Slice und Shift in jedem Glitch-
    /// Baukasten. Welche Baender sich ueberhaupt bewegen, sagt die Dichte.
    /// </summary>
    Slices,

    /// <summary>
    /// Bloecke: wie Streifen, aber jedes Band ist noch einmal in Stuecke verschiedener
    /// Laenge zerlegt, und jedes Stueck springt fuer sich.
    ///
    /// Das Bild einer beschaedigten Datei, bei der ganze Rechtecke an falscher Stelle
    /// ankommen. Wirkt groeber und digitaler als Streifen.
    /// </summary>
    Blocks,
}

/// <summary>
/// Verschiebung: Jeder Bildpunkt holt seine Farbe woanders her.
///
/// Der Grund, warum das hier steht und nicht in fuenfzig anderen Programmen: Die
/// Richtung kann aus einem RENDERPASS kommen. Ein Wellenfilter in einem Bildbearbeiter
/// kennt nur das fertige Bild und schiebt deshalb alles gleich - Vordergrund,
/// Hintergrund und Himmel. Hier weiss das Programm, wohin jede Flaeche zeigt und was
/// sich bewegt, und kann die Verzerrung daran entlanglegen.
///
/// Ueber die Richtung legt sich eine WELLE mit einer Form - weich, eckig, gezackt oder
/// zerrissen. Sie laeuft ueber das Bild und, wenn man sie laesst, auch ueber die ZEIT:
/// Eine Verschiebung, die ueber alle Bilder einer Sequenz gleich steht, sieht aus wie
/// ein Aufkleber auf der Linse. Das Tempo laesst sie wandern, und bei den zerrissenen
/// Formen springt der Riss von Bild zu Bild.
///
/// Der Kanalversatz schiebt Rot, Gruen und Blau verschieden weit. Ein Riss ohne
/// Farbsaum sieht nach Fehler in der Datei aus, einer mit nach einem Fehler im Signal.
///
/// Am Rand haelt es fest oder schlaegt um. Festgehalten zieht sich der Rand in Streifen;
/// umgeschlagen kommt, was rechts hinausgeschoben wird, links wieder herein - der
/// Rollbalken eines Fernsehers, der das Bild nicht halten kann.
/// </summary>
public sealed class DisplaceTool : IDataTool
{
    public const string KindName = "displace";

    public string Kind => KindName;

    /// <summary>Woher die Richtung kommt - Geometrie, Bewegung oder die Welle allein.</summary>
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

    /// <summary>Die Form der Welle.</summary>
    public WaveShape Shape { get; set; } = WaveShape.Sine;

    /// <summary>
    /// Die Laenge einer Welle in Bildpunkten - bei Streifen und Bloecken die Hoehe
    /// eines Bandes.
    /// </summary>
    public float Wavelength { get; set; } = 80f;

    /// <summary>
    /// Wieviel von der Verschiebung die Welle uebernimmt. 0 ist eine glatte
    /// Verschiebung, 1 eine, die ganz der Welle folgt.
    ///
    /// Eins ist die Grundstellung, und zwar mit Absicht: Eine glatte Verschiebung
    /// ohne Welle verrueckt nur das ganze Bild und sieht nach nichts aus. Wer eine
    /// Welle einstellt, will sie sehen. Gespeicherte Rezepte tragen ihren Wert
    /// selbst und aendern sich dadurch nicht.
    /// </summary>
    public float Wave { get; set; } = 1f;

    /// <summary>In welcher Richtung die Welle ueber das Bild laeuft, in Grad.</summary>
    public float Angle { get; set; } = 90f;

    /// <summary>Wo die Welle beginnt, in Grad einer Schwingung.</summary>
    public float Phase { get; set; }

    /// <summary>
    /// Wie schnell sie sich ueber die Sequenz bewegt.
    ///
    /// Bei den schwingenden Formen in Wellen je Bild: 0,05 laesst sie in zwanzig
    /// Bildern um eine Wellenlaenge weiterlaufen. Bei Streifen und Bloecken ist es,
    /// wie oft der Riss wechselt: 1 heisst jedes Bild neu, 0,25 jedes vierte. Null
    /// steht still - fuer ein Einzelbild das Richtige, fuer eine Sequenz selten.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Bei Streifen und Bloecken: welcher Anteil der Baender sich ueberhaupt bewegt.
    ///
    /// Der Regler, der aus "alles zittert" einen Riss macht. Ein echter Signalfehler
    /// trifft ein paar Zeilen und laesst den Rest stehen; bei voller Dichte wird
    /// daraus ein Muster.
    /// </summary>
    public float Density { get; set; } = 1f;

    /// <summary>Startwert fuer Streifen und Bloecke. Ein anderer Wert, ein anderer Riss.</summary>
    public int Seed { get; set; }

    /// <summary>
    /// Wie weit die Kanaele auseinanderlaufen. 0 schiebt alle drei gleich weit.
    /// </summary>
    public float Spread { get; set; }

    /// <summary>
    /// Am Rand umschlagen statt festhalten: Was rechts hinausgeschoben wird, kommt
    /// links wieder herein.
    /// </summary>
    public bool Wrap { get; set; }

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.01f;

    private float _amount, _wave, _spread, _density;
    private float _length, _sin, _cos, _phase, _speed;
    private WaveShape _shape;
    private int _seed;
    private bool _wrap;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, -400f, 400f);
        _wave = Math.Clamp(Wave, 0f, 1f);
        _spread = Math.Clamp(Spread, 0f, 1f);
        _density = Math.Clamp(Density, 0f, 1f);

        _length = MathF.Max(2f, Wavelength);

        // Auf glatte Werte gerundet, wo sie glatt sein sollen - und das ist kein
        // Schoenheitsfehler, sondern war einer im Bild.
        //
        // In float ist sin(90 Grad) nicht 1, sondern 0,99999994, und cos(90 Grad)
        // nicht 0, sondern -4,4e-8. Damit lag jede Bandgrenze eine Zeile daneben, und
        // in manchen Grenzzeilen kippte der winzige x-Anteil die linke und die rechte
        // Bildhaelfte in VERSCHIEDENE Baender: eine Zeile, die in der Mitte reisst,
        // obwohl ein Band als Ganzes springen soll. Wer 90 Grad einstellt, meint
        // gerade Baender.
        float radians = Angle * MathF.PI / 180f;
        _sin = Snap(MathF.Sin(radians));
        _cos = Snap(MathF.Cos(radians));

        _phase = Phase / 360f;
        _speed = Speed;
        _shape = Shape;
        _seed = Seed;
        _wrap = Wrap;
    }

    public void Run(LocalPass.Scratch scratch, FloatFrame? data, int[] columns, int[] rows,
                    int imageWidth, int step, int number = 0)
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
        float sin = _sin, cos = _cos;
        bool wrap = _wrap;

        // Die Zeit. Bei den schwingenden Formen laeuft die Phase weiter; bei den
        // zerrissenen wechselt der Startwert - und zwar in ganzen Schritten, damit
        // ein Riss eine Weile stehen kann, bevor der naechste kommt.
        //
        // Bei den zerrissenen laeuft die Phase NICHT mit. Sonst verschoebe sich das
        // Bandraster zwischen zwei Wuerfen um Bruchteile eines Bandes, und der Riss,
        // der zwei Bilder stehen soll, zitterte dazwischen.
        bool torn = _shape is WaveShape.Slices or WaveShape.Blocks;

        float phase = torn ? _phase : _phase + number * _speed;
        int seed = _seed + (int)MathF.Floor(number * MathF.Abs(_speed));

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
                        Keep(source, target, alpha, alphaTarget, row + gx);
                        continue;
                    }

                    dirX /= length;
                    dirY /= length;
                }

                // In BILDpunkten gemessen, nicht in Gitterpunkten: Beim Reglerzug
                // ist das Gitter groeber, und eine Welle, die daran haengt, aenderte
                // beim Loslassen ihre Laenge.
                float x = columns[gx];
                float y = rows[gy];

                float along = x * cos + y * sin;
                float across = -x * sin + y * cos;

                float shape = Evaluate(along, across, phase, seed);

                // Ohne Welle die glatte Verschiebung, mit voller Welle eine, die ganz
                // der Form folgt - und dazwischen alles.
                float strength = amount * (1f - wave + wave * shape);

                if (MathF.Abs(strength) < 1e-3f)
                {
                    Keep(source, target, alpha, alphaTarget, row + gx);
                    continue;
                }

                // In Gitterpunkten, nicht in Bildpunkten.
                float baseX = dirX * strength / step;
                float baseY = dirY * strength / step;

                int out0 = (row + gx) * 3;

                if (spread <= 0f)
                {
                    Sample(source, alpha, gx + baseX, gy + baseY, gridWidth, gridHeight, wrap,
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
                       gridWidth, gridHeight, wrap, out float lr, out _, out _, out _);

                Sample(source, alpha, gx + baseX, gy + baseY, gridWidth, gridHeight, wrap,
                       out _, out float mg, out _, out float ma);

                Sample(source, alpha, gx + baseX * (1f + spread), gy + baseY * (1f + spread),
                       gridWidth, gridHeight, wrap, out _, out _, out float hb, out _);

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

    /// <summary>
    /// Die Form an einer Stelle: ein Wert von -1 bis 1.
    ///
    /// <paramref name="along"/> ist der Weg in Laufrichtung der Welle, <paramref
    /// name="across"/> der quer dazu - beides in Bildpunkten. Die schwingenden Formen
    /// brauchen nur den ersten; Bloecke brauchen beide, weil sie ein Band noch einmal
    /// zerlegen.
    /// </summary>
    private float Evaluate(float along, float across, float phase, int seed)
    {
        float turns = along / _length + phase;

        switch (_shape)
        {
            case WaveShape.Sine:
                return MathF.Sin(turns * MathF.Tau);

            case WaveShape.Triangle:
            {
                // Mit dem Sinus ausgerichtet: null am Anfang, oben bei einem Viertel,
                // unten bei drei Vierteln. So kann man zwischen den Formen wechseln,
                // ohne dass die Welle einen Satz macht.
                float u = Frac(turns);

                return u < 0.25f ? 4f * u
                     : u < 0.75f ? 2f - 4f * u
                     : 4f * u - 4f;
            }

            case WaveShape.Square:
                return Frac(turns) < 0.5f ? 1f : -1f;

            case WaveShape.Saw:
                return 2f * Frac(turns + 0.5f) - 1f;

            case WaveShape.Slices:
            {
                int band = (int)MathF.Floor(turns);

                // Welche Baender sich ueberhaupt bewegen: Ein Signalfehler trifft ein
                // paar Zeilen und laesst den Rest stehen.
                if (GlitchNoise.Unit(band, seed, 1) >= _density) return 0f;

                return GlitchNoise.Signed(band, seed, 2);
            }

            case WaveShape.Blocks:
            {
                int band = (int)MathF.Floor(turns);

                // Jedes Band in Stuecke verschiedener Laenge zerlegt - zwischen einer
                // und sechs Bandhoehen. Gleich lange Stuecke saehen aus wie ein
                // Schachbrett und nicht wie eine beschaedigte Datei.
                float piece = _length * (1f + 5f * GlitchNoise.Unit(band, seed, 3));
                float shift = piece * GlitchNoise.Unit(band, seed, 4);

                int block = (int)MathF.Floor((across + shift) / piece);

                if (GlitchNoise.Unit(band, block, seed, 5) >= _density) return 0f;

                return GlitchNoise.Signed(band, block, seed, 6);
            }

            default:
                return 0f;
        }
    }

    private static float Frac(float value) => value - MathF.Floor(value);

    /// <summary>Null und Eins, wo sie gemeint sind - siehe Prepare.</summary>
    private static float Snap(float value)
    {
        if (MathF.Abs(value) < 1e-6f) return 0f;
        if (MathF.Abs(MathF.Abs(value) - 1f) < 1e-6f) return MathF.Sign(value);

        return value;
    }

    private static void Keep(float[] source, float[] target, byte[] alpha, byte[] alphaTarget, int at)
    {
        int flat = at * 3;

        target[flat] = source[flat];
        target[flat + 1] = source[flat + 1];
        target[flat + 2] = source[flat + 2];

        alphaTarget[at] = alpha[at];
    }

    /// <summary>Nicht jede Zahl in einem Renderpass ist eine Richtung.</summary>
    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    /// <summary>
    /// Bilinear aus dem Gitter - am Rand festgehalten oder umgeschlagen.
    ///
    /// Festgehalten zieht sich der Rand in Streifen, und das sieht nach Verzug aus,
    /// was es ja auch ist. Umgeschlagen kommt herein, was auf der anderen Seite
    /// hinausging - der Rollbalken eines Fernsehers, der das Bild nicht halten kann.
    /// </summary>
    private static void Sample(float[] source, byte[] alpha, float x, float y,
                               int width, int height, bool wrap,
                               out float r, out float g, out float b, out float a)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float tx = x - x0;
        float ty = y - y0;

        int left, right, top, bottom;

        if (wrap)
        {
            left = Wrapped(x0, width);
            right = Wrapped(x0 + 1, width);
            top = Wrapped(y0, height);
            bottom = Wrapped(y0 + 1, height);
        }
        else
        {
            left = Math.Clamp(x0, 0, width - 1);
            right = Math.Clamp(x0 + 1, 0, width - 1);
            top = Math.Clamp(y0, 0, height - 1);
            bottom = Math.Clamp(y0 + 1, 0, height - 1);
        }

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

    /// <summary>Ein Rest, der auch fuer negative Zahlen positiv bleibt.</summary>
    private static int Wrapped(int value, int size)
    {
        int rest = value % size;
        return rest < 0 ? rest + size : rest;
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
        Shape = Shape,
        Wavelength = Wavelength,
        Wave = Wave,
        Angle = Angle,
        Phase = Phase,
        Speed = Speed,
        Density = Density,
        Seed = Seed,
        Spread = Spread,
        Wrap = Wrap,
    };
}
