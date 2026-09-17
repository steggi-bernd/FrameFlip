using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Ein Werkzeug mit oertlicher Wirkung: Es braucht die Nachbarschaft eines Punktes.
///
/// Der eigene Weg, den <see cref="IGradingTool"/> seit jeher angekuendigt hat.
/// Klarheit, Schaerfe, Rauschminderung und Glanz lassen sich nicht punktweise
/// ausdruecken - sie fragen, wie es RINGSUM aussieht. Das kostet einen
/// Zwischenpuffer und einen zweiten Durchgang, und genau deshalb stehen sie nicht in
/// derselben Kette wie eine Kurve: Wer sie dort einreihte, zwaenge jedes Bild durch
/// einen Puffer, auch wenn gar keines von ihnen benutzt wird.
///
/// Was sie bekommen, ist der Wert des Punktes UND derselbe Wert weichgezeichnet. Aus
/// diesen beiden laesst sich alles bauen, was hier vorkommt: Die Differenz ist der
/// oertliche Kontrast, ihr Vorzeichen die Richtung, ihr Betrag die Staerke.
///
/// Beides stammt aus DERSELBEN Stufe der Kette - nach der Sichtumwandlung und nach
/// den Anzeigewerkzeugen. Eine Unschaerfe aus einer anderen Stufe waere kein
/// Kontrast, sondern ein Versatz: Auf einer gleichmaessigen Flaeche waeren die
/// beiden Werte verschieden, und die Flaeche kippte, obwohl an ihr nichts zu
/// verstaerken ist.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ClarityTool), ClarityTool.KindName)]
[JsonDerivedType(typeof(SharpenTool), SharpenTool.KindName)]
[JsonDerivedType(typeof(NoiseTool), NoiseTool.KindName)]
[JsonDerivedType(typeof(BloomTool), BloomTool.KindName)]
[JsonDerivedType(typeof(HalationTool), HalationTool.KindName)]
[JsonDerivedType(typeof(DehazeTool), DehazeTool.KindName)]
[JsonDerivedType(typeof(TextureTool), TextureTool.KindName)]
public interface ILocalTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    /// <summary>
    /// Wann das Werkzeug an der Reihe ist. Die Reihenfolge in der Liste entscheidet
    /// NICHT - der Stapel sortiert danach.
    /// </summary>
    LocalStage Stage { get; }

    /// <summary>True, wenn nichts zu rechnen ist - dann faellt auch der Puffer weg.</summary>
    bool IsNeutral { get; }

    /// <summary>
    /// Wie weit die Nachbarschaft reicht, in Bildpunkten bei voller Aufloesung.
    ///
    /// Werkzeuge mit demselben Radius teilen sich eine Weichzeichnung; wer einen
    /// anderen verlangt, bekommt eine eigene. Die erste Fassung nahm fuer alle den
    /// groessten - das ging, solange die Klarheit allein hier stand, und war falsch,
    /// sobald die Schaerfe dazukam: Mit Radius vierzig geschaerft ist nicht Schaerfe,
    /// sondern noch einmal Klarheit.
    /// </summary>
    int Radius { get; }

    void Prepare();

    /// <summary>
    /// Rechnet einen Bildpunkt um. Die Werte liegen zwischen 0 und 1.
    /// </summary>
    /// <param name="br">Derselbe Punkt, weichgezeichnet - die Nachbarschaft.</param>
    void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb);
}

/// <summary>
/// Die Reihenfolge der oertlichen Werkzeuge. Fest, und aus einem Grund.
///
/// Die Reihe laeuft durch die Sichtumwandlung hindurch: <see cref="Light"/> steht
/// davor, alles andere dahinter. Die Seite ist damit keine zweite Angabe, die zur
/// Stufe passen muss, sondern folgt aus ihr - zwei Angaben liefen irgendwann
/// auseinander, und das faellt erst auf, wenn ein Werkzeug am falschen Ort rechnet.
///
/// Dunst zuerst, und zwar VOR der Sichtumwandlung: Er ist Licht, das auf dem Weg zur
/// Kamera dazugekommen ist, und es abzuziehen muss geschehen, solange die Werte noch
/// Lichtmengen sind. Danach das Licht, das ueber seine Kante hinaus streut - Glanz und
/// Halation. Auch das geschieht in der Linse und im Film, bevor irgendeine Kennlinie
/// darauf angewandt wird, und es braucht die Ueberhellen, die es hinter der Umwandlung
/// nicht mehr gibt. Dass der Dunst davor liegt, hat denselben Grund wie alles hier:
/// Ein Schleier, der erst leuchtet und dann abgezogen wird, leuchtet immer noch.
///
/// Rauschen als Naechstes: Was danach kommt, verstaerkt oertliche Unterschiede, und
/// Rauschen IST ein oertlicher Unterschied. Wer erst schaerft und dann entrauscht,
/// hat das Rauschen vorher gross gemacht und nimmt hinterher das Bild mit weg.
///
/// Dann Klarheit, Textur, Schaerfe - von der groben zur feinen Zeichnung. Die Schaerfe
/// zuletzt, weil sie auf dem arbeitet, was am Ende dasteht.
/// </summary>
public enum LocalStage
{
    /// <summary>Vor der Sichtumwandlung: der Schleier, bevor er leuchtet.</summary>
    Haze = 0,

    /// <summary>Vor der Sichtumwandlung, in linearem Licht: Glanz und Halation.</summary>
    Light = 1,

    // Ab hier hinter der Sichtumwandlung. Der Schnitt liegt hinter Light - wer eine
    // Stufe dazwischenschiebt, verschiebt damit auch die Seite, auf der sie rechnet.
    Denoise = 2,
    Contrast = 3,
    Texture = 4,
    Detail = 5,
}

/// <summary>
/// Ein oertliches Werkzeug, das nicht den Wert weichzeichnet, sondern etwas daraus
/// Gewonnenes.
///
/// Der Unterschied ist wesentlich genug fuer eine eigene Schnittstelle. Glanz ist
/// nicht "das Bild weichgezeichnet und dazugezaehlt" - das waere ein Schleier ueber
/// allem. Es ist das, was HELL genug ist, weichgezeichnet und dazugezaehlt; die
/// Schwelle ist das ganze Werkzeug. Der Dunst zieht etwas ganz anderes heraus, den
/// dunklen Kanal, und sucht damit genau den Schleier, den der Glanz nicht sein will.
///
/// Weil der Eingang der Weichzeichnung damit ein anderer ist, kann sich ein solches
/// Werkzeug die Weichzeichnung mit keinem anderen teilen - auch nicht bei gleichem
/// Radius.
/// </summary>
public interface IExtractTool : ILocalTool
{
    /// <summary>
    /// Holt aus einem Bildpunkt heraus, was weichgezeichnet werden soll. Laeuft auf
    /// einer Kopie - der Wert selbst bleibt, wo er ist.
    /// </summary>
    void Extract(ref float r, ref float g, ref float b);
}

/// <summary>
/// Klarheit: oertlicher Kontrast.
///
/// Das erste Werkzeug auf dem oertlichen Weg, und das, welches ihn am deutlichsten
/// rechtfertigt. Was es tut, laesst sich punktweise nicht sagen: Es verstaerkt, was
/// ein Punkt gegenueber seiner Umgebung IST, und laesst gleichmaessige Flaechen in
/// Ruhe. Ein Kontrastregler hebt stattdessen alles an, auch den Himmel.
///
/// Der Radius ist gross, nicht klein - das ist der Unterschied zur Schaerfe. Mit
/// wenigen Punkten Radius entstehen Kanten; mit vielen entsteht Plastizitaet. Wer
/// die Zahl klein dreht, bekommt Schaerfe, und das ist kein Fehler, sondern
/// dasselbe Werkzeug an einem anderen Ende.
/// </summary>
public sealed class ClarityTool : ILocalTool
{
    public const string KindName = "clarity";

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Contrast;

    /// <summary>-1 bis 1. Negativ nimmt oertlichen Kontrast weg und wirkt wie weiche Haut.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Der Radius in Bildpunkten, bezogen auf 1080p.
    ///
    /// Bezogen und nicht absolut: Derselbe Wert soll auf 4K dieselbe Wirkung haben,
    /// und in Bildpunkten waere er dort ein Viertel so gross. Umgerechnet wird beim
    /// Vorbereiten, wo die Bildgroesse bekannt ist.
    /// </summary>
    public int Reach { get; set; } = 40;

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 2, 300);

    private float _amount;

    public void Prepare() => _amount = Math.Clamp(Amount, -1f, 1f);

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        r = Channel(r, br);
        g = Channel(g, bg);
        b = Channel(b, bb);
    }

    /// <summary>
    /// Ein Kanal.
    ///
    /// Die Gewichtung nach der Umgebungshelligkeit ist der Teil, der zaehlt: Sie ist
    /// in der Mitte voll und laeuft an beiden Enden auf null. Ohne sie brennen die
    /// Lichter aus und die Schatten laufen zu, und zwar genau dort, wo ohnehin kein
    /// Platz mehr ist. 4*b*(1-b) ist die einfachste Kurve, die das tut.
    /// </summary>
    private float Channel(float value, float around)
    {
        float weight = 4f * around * (1f - around);
        if (weight <= 0f) return value;

        return Math.Clamp(value + (value - around) * _amount * weight, 0f, 1f);
    }
}

/// <summary>
/// Schaerfe: derselbe Griff wie die Klarheit, nur mit kleinem Radius.
///
/// Dass es ein eigenes Werkzeug ist und nicht bloss ein zweiter Klarheitsregler,
/// liegt an drei Unterschieden, die jeder fuer sich klein aussieht:
///
/// Sie rechnet auf der HELLIGKEIT, nicht je Kanal. An einer farbigen Kante laufen
/// die drei Kanaele verschieden weit auseinander; je Kanal geschaerft wandert die
/// Farbe, und um jede Kante liegt ein bunter Saum. Derselbe Zuschlag auf alle drei
/// laesst den Farbton, wo er war.
///
/// Sie hat eine Schwelle. Feines Rauschen ist ein oertlicher Unterschied wie jeder
/// andere, und ohne Schwelle ist es das erste, was hochkommt.
///
/// Und sie laesst die Enden NICHT los, anders als die Klarheit. Eine Spitzlichtkante
/// liegt am weissen Ende; wer dort aufhoert zu schaerfen, hoert genau da auf, wo das
/// Bild am schaerfsten ist. Begrenzt wird trotzdem - nur eben hart.
/// </summary>
public sealed class SharpenTool : ILocalTool
{
    public const string KindName = "sharpen";

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Detail;

    /// <summary>0 bis 2. Wie stark der Unterschied zur Umgebung zugeschlagen wird.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Der Radius in Bildpunkten, bezogen auf 1080p - wie bei der Klarheit, nur klein.
    ///
    /// Zwei Punkte sind die uebliche Wahl: Ein Radius von eins greift kaum ueber die
    /// Punktreihe selbst hinaus, und ab etwa acht wird aus Schaerfe wieder
    /// Plastizitaet - dafuer gibt es die Klarheit.
    /// </summary>
    public int Reach { get; set; } = 2;

    /// <summary>
    /// Unterhalb dieser Differenz bleibt es liegen. In Anzeigewerten, 0 bis 1.
    ///
    /// Kein harter Schnitt, sondern ein weicher Uebergang: Ein harter liesse sich an
    /// einem Verlauf als Stufe sehen, genau dort, wo das Rauschen die Schwelle
    /// gerade ueberschreitet.
    /// </summary>
    public float Threshold { get; set; } = 0.01f;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 1, 24);

    private float _amount;
    private float _thresholdSquared;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, 0f, 2f);

        float threshold = Math.Clamp(Threshold, 0f, 0.2f);
        _thresholdSquared = threshold * threshold;
    }

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        // Ein Zuschlag fuer alle drei Kanaele, aus der Helligkeit gerechnet.
        float difference = LumaR * (r - br) + LumaG * (g - bg) + LumaB * (b - bb);
        if (difference == 0f) return;

        float gain = _amount;

        if (_thresholdSquared > 0f)
        {
            float squared = difference * difference;
            gain *= squared / (squared + _thresholdSquared);
        }

        float add = difference * gain;

        r = Math.Clamp(r + add, 0f, 1f);
        g = Math.Clamp(g + add, 0f, 1f);
        b = Math.Clamp(b + add, 0f, 1f);
    }
}

/// <summary>
/// Rauschminderung: glaetten, wo nichts ist, und stehenlassen, wo etwas ist.
///
/// Aus denselben zwei Zahlen wie alles hier - Wert und Umgebung. Ist der Unterschied
/// klein, ist es vermutlich Rauschen und der Wert wandert zur Umgebung hin; ist er
/// gross, ist es eine Kante und bleibt stehen. Das ist der Gedanke des
/// Bilateralfilters, mit einer einzigen Weichzeichnung statt mit einem Fenster je
/// Bildpunkt.
///
/// Was das nicht kann, gehoert dazu: Ein Rendern mit zu wenigen Abtastungen rauscht
/// in GROSSEN Flecken, und die sehen von nahem aus wie Bildinhalt. Dagegen hilft
/// dieses Werkzeug nicht, dagegen hilft rendern. Es ist fuer das feine Korn gedacht,
/// das ueber einer sonst fertigen Aufnahme liegt.
///
/// Helligkeit und Farbe getrennt, und das ist der Teil, der wirklich zaehlt:
/// Farbrauschen ist das haessliche - bunte Flecken in einer grauen Wand -, und
/// gleichzeitig ist echte feine FARB-Zeichnung selten. Farbe laesst sich also viel
/// haerter glaetten als Helligkeit, ohne dass jemand etwas vermisst.
/// </summary>
public sealed class NoiseTool : ILocalTool
{
    public const string KindName = "noise";

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Die Schwelle der Farbe, als Vielfaches der eingestellten.
    ///
    /// Farbrauschen ist groesser als Helligkeitsrauschen und Farbzeichnung seltener
    /// als Helligkeitszeichnung - beides zeigt in dieselbe Richtung.
    /// </summary>
    private const float ColourWidening = 4f;

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Denoise;

    /// <summary>0 bis 1. Wie weit Helligkeitsrauschen zur Umgebung hin wandert.</summary>
    public float Luminance { get; set; }

    /// <summary>0 bis 1. Dasselbe fuer die Farbe - hier darf man zugreifen.</summary>
    public float Colour { get; set; }

    /// <summary>
    /// Bis zu welchem Unterschied geglaettet wird. In Anzeigewerten, 0 bis 1.
    ///
    /// Gross gedreht nimmt es auch Zeichnung mit; klein gedreht laesst es das
    /// groebere Korn stehen. Zwei Hundertstel sind fuer ein sonst fertiges Bild eine
    /// brauchbare Ausgangslage.
    /// </summary>
    public float Threshold { get; set; } = 0.02f;

    /// <summary>Der Radius in Bildpunkten, bezogen auf 1080p. Klein, wie bei der Schaerfe.</summary>
    public int Reach { get; set; } = 2;

    [JsonIgnore]
    public bool IsNeutral => Luminance < 0.005f && Colour < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 1, 24);

    private float _luminance;
    private float _colour;
    private float _lumaThresholdSquared;
    private float _colourThresholdSquared;

    public void Prepare()
    {
        _luminance = Math.Clamp(Luminance, 0f, 1f);
        _colour = Math.Clamp(Colour, 0f, 1f);

        float threshold = Math.Clamp(Threshold, 0.001f, 0.2f);
        _lumaThresholdSquared = threshold * threshold;

        float colour = threshold * ColourWidening;
        _colourThresholdSquared = colour * colour;
    }

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        float dr = r - br, dg = g - bg, db = b - bb;

        // Der Unterschied, aufgeteilt: einmal als Helligkeit, und was davon uebrig
        // bleibt, ist Farbe.
        float luma = LumaR * dr + LumaG * dg + LumaB * db;

        float cr = dr - luma, cg = dg - luma, cb = db - luma;

        // Das Gewicht ist bei Gleichheit eins und faellt, je groesser der Unterschied
        // wird. Ein Bruch statt einer Exponentialfunktion: dieselbe Form, und er
        // laeuft je Bildpunkt.
        float lumaSquared = luma * luma;
        float keepLuma = 1f - _luminance * (_lumaThresholdSquared / (_lumaThresholdSquared + lumaSquared));

        float colourSquared = cr * cr + cg * cg + cb * cb;
        float keepColour = 1f - _colour * (_colourThresholdSquared / (_colourThresholdSquared + colourSquared));

        r = Math.Clamp(br + luma * keepLuma + cr * keepColour, 0f, 1f);
        g = Math.Clamp(bg + luma * keepLuma + cg * keepColour, 0f, 1f);
        b = Math.Clamp(bb + luma * keepLuma + cb * keepColour, 0f, 1f);
    }
}

/// <summary>
/// Glanz: das Licht, das ueber seine Kante hinaus leuchtet.
///
/// Es ist das erste Werkzeug, das VOR der Sichtumwandlung rechnet, und das ist der
/// ganze Punkt. Eine Lampe und die Sonne sind auf der Anzeige beide weiss - hinter
/// der Umwandlung ist nicht mehr zu unterscheiden, ob dort 1 stand oder 500. Genau
/// dieser Unterschied ist aber der Glanz: Die Sonne blendet, die Lampe nicht. Auf
/// einem 8-Bit-Bild ist derselbe Filter geraten; auf einem EXR ist er gerechnet.
///
/// Weichgezeichnet wird deshalb nicht das Bild, sondern das, was ueber der Schwelle
/// liegt. Ohne Schwelle waere es ein Schleier ueber allem - vermutlich der
/// haeufigste Fehler in Nachbearbeitungen, die "weich" aussehen sollen und
/// stattdessen milchig sind.
///
/// Was man wissen muss: Ein einzelner Ausreisser aus einem zu kurz gerechneten
/// Rendern - ein Punkt mit 1e6 - wird hier zu einem grossen Fleck. Das ist nicht
/// falsch gerechnet, sondern richtig: So sieht ein sehr helles Licht aus. Wer den
/// Fleck nicht will, muss den Ausreisser loswerden, nicht den Glanz.
/// </summary>
public sealed class BloomTool : IExtractTool
{
    public const string KindName = "bloom";

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Light;

    /// <summary>0 bis 2. Wie viel von dem gestreuten Licht dazukommt.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Ab welcher Helligkeit gestreut wird - in linearem Szenenlicht, nicht in
    /// Anzeigewerten.
    ///
    /// Eins ist die brauchbare Ausgangslage: Das ist eine weisse Flaeche, und was
    /// darueber liegt, ist eine Lichtquelle oder eine Spiegelung. Eine Zahl in
    /// Anzeigewerten waere hier sinnlos, weil dort alles Helle auf 1 liegt.
    /// </summary>
    public float Threshold { get; set; } = 1f;

    /// <summary>Der Radius in Bildpunkten, bezogen auf 1080p. Gross - Glanz ist weit.</summary>
    public int Reach { get; set; } = 60;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 4, 300);

    private float _amount;
    private float _threshold;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, 0f, 2f);
        _threshold = MathF.Max(0f, Threshold);
    }

    public void Extract(ref float r, ref float g, ref float b)
    {
        // Je Kanal und nicht ueber die Helligkeit: Eine rote Lampe soll rot
        // ueberstrahlen.
        r = MathF.Max(0f, r - _threshold);
        g = MathF.Max(0f, g - _threshold);
        b = MathF.Max(0f, b - _threshold);
    }

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        // Dazugezaehlt, nicht gemischt. Gestreutes Licht kommt HINZU - es nimmt dem
        // Punkt, auf den es faellt, nichts weg.
        r += br * _amount;
        g += bg * _amount;
        b += bb * _amount;
    }
}

/// <summary>
/// Halation: der warme Saum um die Lichter.
///
/// Derselbe Vorgang wie der Glanz und doch ein anderer: Im Film geht das Licht durch
/// die Schicht hindurch, wird an der Traegerfolie zurueckgeworfen und belichtet von
/// hinten noch einmal. Rotes Licht dringt am tiefsten ein, also ist der Saum rot bis
/// orange. Enger als der Glanz und immer warm - das ist das meiste von dem, was an
/// einem Bild "nach Film" aussieht.
///
/// Die Faerbung laesst sich verschieben, aber nicht frei waehlen: von tiefem Rot bis
/// ins Orange. Ein gruener Saum waere keine Halation, sondern ein Farbfehler, und ein
/// Regler, der ihn anbietet, behauptete etwas Falsches ueber das, was hier
/// nachgebildet wird.
/// </summary>
public sealed class HalationTool : IExtractTool
{
    public const string KindName = "halation";

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Light;

    /// <summary>0 bis 2.</summary>
    public float Amount { get; set; }

    /// <summary>Ab welcher Helligkeit, in linearem Szenenlicht - wie beim Glanz.</summary>
    public float Threshold { get; set; } = 1f;

    /// <summary>Der Radius in Bildpunkten, bezogen auf 1080p. Enger als der Glanz.</summary>
    public int Reach { get; set; } = 12;

    /// <summary>0 ist tiefes Rot, 1 ist Orange. Dazwischen wird gemischt.</summary>
    public float Tint { get; set; } = 0.35f;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 2, 200);

    private float _amount;
    private float _threshold;
    private float _tintR, _tintG, _tintB;

    public void Prepare()
    {
        _amount = Math.Clamp(Amount, 0f, 2f);
        _threshold = MathF.Max(0f, Threshold);

        float tint = Math.Clamp(Tint, 0f, 1f);

        _tintR = 1f;
        _tintG = 0.12f + (0.55f - 0.12f) * tint;
        _tintB = 0.04f + (0.20f - 0.04f) * tint;
    }

    public void Extract(ref float r, ref float g, ref float b)
    {
        r = MathF.Max(0f, r - _threshold);
        g = MathF.Max(0f, g - _threshold);
        b = MathF.Max(0f, b - _threshold);
    }

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        // Die Farbe des Saums kommt vom Film, nicht vom Licht: Wieviel zurueckkommt,
        // haengt von der Menge ab, welche Farbe es hat, von der Schicht.
        float light = (LumaR * br + LumaG * bg + LumaB * bb) * _amount;

        r += light * _tintR;
        g += light * _tintG;
        b += light * _tintB;
    }
}

/// <summary>
/// Dunst: das Licht, das auf dem Weg zur Kamera dazugekommen ist.
///
/// Zwischen einem Gegenstand und der Kamera liegt Luft, und die streut Licht in den
/// Strahlengang hinein. Was ankommt, ist deshalb nicht der Gegenstand, sondern der
/// Gegenstand plus ein Schleier - und je weiter weg, desto mehr Schleier. Deshalb
/// werden ferne Berge blass und blaeulich, waehrend der Zaun im Vordergrund seine
/// Farbe behaelt.
///
/// Geschaetzt wird der Schleier ueber den DUNKLEN KANAL: An fast jeder Stelle eines
/// Bildes gibt es einen Farbkanal, der nahe null liegt - ein Schatten, eine
/// gesaettigte Farbe, eine dunkle Flaeche. Wo das nicht mehr so ist, wo also auch
/// das Dunkelste noch hell ist, liegt Schleier darueber. Das ist die Annahme, auf
/// der alles hier beruht, und sie ist erstaunlich tragfaehig.
///
/// Weichgezeichnet wird dieser dunkle Kanal, weil der Schleier eine langsam
/// veraenderliche Groesse ist: Dunst hat keine Kanten. Abgezogen wird er nicht nur,
/// sondern auch herausgeteilt - was durch den Dunst kam, kam GESCHWAECHT an, und die
/// Division holt den Kontrast zurueck. Weiss bleibt dabei weiss, das ist die Probe.
///
/// Was das Werkzeug nicht tut: Dunst hinzufuegen. Eine geschaetzte Groesse laesst
/// sich nur dort umkehren, wo sie etwas gefunden hat - in einem klaren Bild ist
/// nichts zu verstaerken. Nebel dazuzugeben braucht die Entfernung, und die steht im
/// Tiefenpass; das gehoert zum Tiefendunst und nicht hierher.
/// </summary>
public sealed class DehazeTool : IExtractTool
{
    public const string KindName = "dehaze";

    /// <summary>
    /// Wie weit die Durchlaessigkeit mindestens heruntergehen darf.
    ///
    /// Ohne diese Grenze teilt ein sehr dunstiges Bild bei voller Staerke durch fast
    /// null, und heraus kommt nicht Kontrast, sondern Rauschen mit Farben.
    /// </summary>
    private const float ThinnestVeil = 0.1f;

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Haze;

    /// <summary>0 bis 1. Wie viel von dem geschaetzten Schleier abgezogen wird.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Der Radius in Bildpunkten, bezogen auf 1080p. Gross, denn Dunst hat keine
    /// Kanten - und ein zu kleiner Radius zoege die Zeichnung selbst als Schleier ab.
    /// </summary>
    public int Reach { get; set; } = 80;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 8, 300);

    private float _amount;

    public void Prepare() => _amount = Math.Clamp(Amount, 0f, 1f);

    public void Extract(ref float r, ref float g, ref float b)
    {
        // Der dunkle Kanal: das Wenigste, was an dieser Stelle ankommt. Alle drei
        // bekommen denselben Wert - der Schleier ist eine Menge, keine Farbe.
        float dark = MathF.Min(r, MathF.Min(g, b));

        r = dark;
        g = dark;
        b = dark;
    }

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        // Alle drei Kanaele tragen denselben Wert - der Auszug hat sie gleichgesetzt.
        float veil = Math.Clamp(br * _amount, 0f, 1f - ThinnestVeil);
        if (veil <= 0f) return;

        float transmission = 1f - veil;

        r = MathF.Max(0f, (r - veil) / transmission);
        g = MathF.Max(0f, (g - veil) / transmission);
        b = MathF.Max(0f, (b - veil) / transmission);
    }
}

/// <summary>
/// Textur: die Zeichnung einer Oberflaeche, in beide Richtungen.
///
/// Im Entwurf stand, dies sei kein eigenes Werkzeug, sondern die Klarheit mit
/// kleinem Radius. Beim Bauen stellte sich das als falsch heraus, und zwar aus zwei
/// Gruenden, die beide in den vorhandenen Werkzeugen stecken:
///
/// Die Klarheit laesst an den Enden los - ihre Gewichtung laeuft bei Schwarz und
/// Weiss auf null. Fuer oertlichen Kontrast ist das richtig; fuer Oberflaechen ist es
/// genau verkehrt, denn helle Haut und dunkler Stoff liegen an genau diesen Enden,
/// und dort taete die Klarheit nichts.
///
/// Und die Schaerfe kann nicht rueckwaerts. Ihre Schwelle laesst kleine Unterschiede
/// liegen, was beim Anheben stimmt und beim Wegnehmen falsch herum zeigt: Negativ
/// geglaettet wuerde sie die Kanten weichzeichnen und das Korn stehenlassen.
///
/// Was bleibt, ist eine Rechnung, die dieselbe ist wie die der Schaerfe, ohne
/// Schwelle, mit Vorzeichen und mit groesserem Radius. Im Gebrauch sind das zwei
/// verschiedene Werkzeuge, und deshalb stehen sie als zwei da.
/// </summary>
public sealed class TextureTool : ILocalTool
{
    public const string KindName = "texture";

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    public string Kind => KindName;

    public LocalStage Stage => LocalStage.Texture;

    /// <summary>-1 bis 1. Negativ nimmt Oberflaechenzeichnung weg, ohne Kanten zu ruehren.</summary>
    public float Amount { get; set; }

    /// <summary>
    /// Der Radius in Bildpunkten, bezogen auf 1080p. Zwischen Schaerfe und Klarheit -
    /// dort liegt die Zeichnung, die eine Oberflaeche ausmacht.
    /// </summary>
    public int Reach { get; set; } = 8;

    [JsonIgnore]
    public bool IsNeutral => MathF.Abs(Amount) < 0.005f;

    [JsonIgnore]
    public int Radius => Math.Clamp(Reach, 2, 60);

    private float _amount;

    public void Prepare() => _amount = Math.Clamp(Amount, -1f, 1f);

    public void Apply(ref float r, ref float g, ref float b, float br, float bg, float bb)
    {
        // Auf der Helligkeit, aus demselben Grund wie bei der Schaerfe: Je Kanal
        // gerechnet wandert an einer farbigen Kante die Farbe.
        float difference = LumaR * (r - br) + LumaG * (g - bg) + LumaB * (b - bb);
        if (difference == 0f) return;

        float add = difference * _amount;

        r = Math.Clamp(r + add, 0f, 1f);
        g = Math.Clamp(g + add, 0f, 1f);
        b = Math.Clamp(b + add, 0f, 1f);
    }
}

/// <summary>
/// Die Weichzeichnung fuer den oertlichen Weg.
///
/// Dreimal ein Kastenfilter statt einmal eine Glocke. Das ist kein Sparweg: Drei
/// Kaesten hintereinander sind einer Glocke schon so aehnlich, dass der Unterschied
/// unter dem Rauschen liegt - und ein Kasten laeuft mit einer laufenden Summe in
/// gleicher Zeit, egal wie gross der Radius ist. Eine echte Glocke kostete bei
/// Radius vierzig das Achtzigfache.
/// </summary>
public static class Blur
{
    /// <summary>
    /// Zeichnet drei Kanaele weich - an Ort und Stelle, mit einem Arbeitsfeld
    /// daneben.
    /// </summary>
    /// <param name="values">Drei Kanaele hintereinander je Punkt: r, g, b.</param>
    public static void Apply(float[] values, int width, int height, int radius, float[] scratch)
    {
        if (radius < 1 || width <= 0 || height <= 0) return;

        for (int pass = 0; pass < 3; pass++)
        {
            Horizontal(values, scratch, width, height, radius);
            Vertical(scratch, values, width, height, radius);
        }
    }

    private static void Horizontal(float[] from, float[] to, int width, int height, int radius)
    {
        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            int row = y * width * 3;
            int span = radius * 2 + 1;

            // Die laufende Summe faengt mit dem gespiegelten Rand an: Ohne ihn
            // liefe die Kante gegen Schwarz, und ein Wasserzeichen am Bildrand
            // bekaeme einen dunklen Saum.
            float sr = 0f, sg = 0f, sb = 0f;

            for (int k = -radius; k <= radius; k++)
            {
                int at = row + Math.Clamp(k, 0, width - 1) * 3;
                sr += from[at];
                sg += from[at + 1];
                sb += from[at + 2];
            }

            for (int x = 0; x < width; x++)
            {
                int at = row + x * 3;
                to[at] = sr / span;
                to[at + 1] = sg / span;
                to[at + 2] = sb / span;

                int leaving = row + Math.Clamp(x - radius, 0, width - 1) * 3;
                int joining = row + Math.Clamp(x + radius + 1, 0, width - 1) * 3;

                sr += from[joining] - from[leaving];
                sg += from[joining + 1] - from[leaving + 1];
                sb += from[joining + 2] - from[leaving + 2];
            }
        });
    }

    private static void Vertical(float[] from, float[] to, int width, int height, int radius)
    {
        Parallel.For(0, width, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        x =>
        {
            int column = x * 3;
            int span = radius * 2 + 1;
            int stride = width * 3;

            float sr = 0f, sg = 0f, sb = 0f;

            for (int k = -radius; k <= radius; k++)
            {
                int at = Math.Clamp(k, 0, height - 1) * stride + column;
                sr += from[at];
                sg += from[at + 1];
                sb += from[at + 2];
            }

            for (int y = 0; y < height; y++)
            {
                int at = y * stride + column;
                to[at] = sr / span;
                to[at + 1] = sg / span;
                to[at + 2] = sb / span;

                int leaving = Math.Clamp(y - radius, 0, height - 1) * stride + column;
                int joining = Math.Clamp(y + radius + 1, 0, height - 1) * stride + column;

                sr += from[joining] - from[leaving];
                sg += from[joining + 1] - from[leaving + 1];
                sb += from[joining + 2] - from[leaving + 2];
            }
        });
    }
}
