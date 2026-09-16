using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Was eine Ebene beitraegt.</summary>
public enum LayerContent
{
    /// <summary>Ein Pass aus der Datei - sie bringt Licht mit.</summary>
    Pass,

    /// <summary>
    /// Eine Korrektur auf das, was darunter liegt - sie bringt nichts mit, sondern
    /// veraendert.
    ///
    /// Mit einer Schnittmaske wirkt sie nur auf die eine Ebene darunter, ohne sie auf
    /// alles. Das ist derselbe Griff wie in Photoshop und der Grund, warum die
    /// Schnittmaske hier schon vor den Einstellungsebenen gebaut wurde: Sie ist es,
    /// die "waerme nur den Glanz" ueberhaupt erst sagbar macht.
    /// </summary>
    Adjustment,
}

/// <summary>
/// Eine Ebene: ein Pass aus der Datei, und wie er auf das wirkt, was unter ihm liegt.
///
/// Die Ebene traegt keine Bilddaten. Sie nennt nur den Pass, aus dem sie kommt -
/// alles andere ist Einstellung. Damit laesst sich derselbe Stapel auf jedes Bild
/// der Sequenz anwenden, und genau das ist der Zweck: ein Bild einrichten,
/// dreihundert rechnen.
///
/// Sie laesst sich mehrfach anlegen. Zweimal derselbe Pass mit verschiedener
/// Mischung ist ein gewoehnlicher Griff - Glanz einmal additiv fuer die Helligkeit
/// und einmal weich fuer den Schimmer.
/// </summary>
public sealed class ImageLayer
{
    /// <summary>
    /// Was die Ebene ist: ein Pass, oder eine Korrektur auf das, was darunter liegt.
    ///
    /// Beides in einer Klasse und nicht in zweien, weil beides dieselben sieben Dinge
    /// hat - Reihenfolge, Sichtbarkeit, Mischung, Deckkraft, Schnittmaske, Maske,
    /// Name - und sich nur darin unterscheidet, woher der Wert kommt. Zwei Klassen
    /// hiessen zwei Listen, zwei Zeilenarten und zwei Wege durch den Composer.
    /// </summary>
    public LayerContent Content { get; set; } = LayerContent.Pass;

    /// <summary>
    /// Der Pass in der Datei, etwa "ViewLayer.GlossDir". Leer heisst: die Farbkanaele,
    /// die das Bild ohnehin ergeben - bei einem PNG die einzige Wahl. Bei einer
    /// Einstellungsebene ohne Bedeutung.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Die Grundkorrektur der Einstellungsebene - dieselben Regler wie unten im
    /// Streifen. Null bei einer Passebene.
    /// </summary>
    public ImageAdjustments? Adjustments { get; set; }

    /// <summary>
    /// Die Werkzeuge der Einstellungsebene. Null bei einer Passebene.
    ///
    /// Derselbe Stapel wie fuer das ganze Bild, und damit dieselben Werkzeuge: Es
    /// gibt sie einmal, und eine Kurve rechnet auf einer Ebene, was sie auch am Ende
    /// rechnet.
    /// </summary>
    public GradingStack? Tools { get; set; }

    /// <summary>
    /// Was in der Liste steht. Frei, weil eine Kopie sonst genauso hiesse wie ihr
    /// Original und niemand die beiden auseinanderhielte.
    /// </summary>
    public string Name { get; set; } = "";

    public bool Visible { get; set; } = true;

    /// <summary>
    /// Wie die Ebene auf die darunter wirkt. Add ist die Grundstellung, weil
    /// Renderpasse additiv zerlegt sind: Alle Passe auf Add ergeben wieder das
    /// Bild, das Blender gerendert hat.
    /// </summary>
    public BlendMode Mode { get; set; } = BlendMode.Add;

    /// <summary>0 bis 1.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// Blendenstufen auf diese Ebene allein. Eine Multiplikation im linearen Licht,
    /// also dasselbe, was der Belichtungsregler mit dem ganzen Bild tut - nur hier
    /// auf einen Pass beschraenkt. "Mehr Glanz" ist damit ein Griff.
    /// </summary>
    public float Exposure { get; set; }

    /// <summary>
    /// Farbe der Ebene, als Faktor je Kanal. 1,1,1 ist unveraendert.
    ///
    /// Multiplikativ und nicht additiv, weil eine Ebene eingefaerbt und nicht
    /// uebermalt werden soll: Was schwarz ist, bleibt schwarz.
    /// </summary>
    public ColourTriplet Tint { get; set; } = new(1, 1, 1);

    /// <summary>
    /// Wirkt nur auf die Ebene direkt darunter, nicht auf alles darunter.
    ///
    /// In Photoshop heisst das Schnittmaske, und fuer Renderpasse ist es keine
    /// Feinheit, sondern die Bedingung dafuer, dass die Rechnung aufgeht. Cycles
    /// zerlegt nicht in Summanden, sondern in Licht und Farbe: Das fertige Bild ist
    ///
    ///     (DiffDir + DiffInd) * DiffCol + (GlossDir + GlossInd) * GlossCol + Emit
    ///
    /// Die Farbpasse sind also Faktoren und keine Summanden. Ohne Beschraenkung
    /// wuerde ein multiplizierender DiffCol auch den Glanz darunter daempfen, und
    /// das Bild waere zu dunkel - richtig aussehend genug, dass es niemand merkt,
    /// und falsch genug, dass die Ausgabe nicht mehr dem Render entspricht.
    /// </summary>
    public bool Clipped { get; set; }

    /// <summary>
    /// Wo die Ebene wirkt. Immer vorhanden, in Grundstellung ohne Wirkung.
    ///
    /// Als Objekt und nicht als Nullwert, weil die Oberflaeche sonst bei jedem
    /// Reglerzug erst eines anlegen muesste - und weil "keine Maske" eine Einstellung
    /// ist und kein Fehlen.
    /// </summary>
    public LayerMask Mask { get; set; } = new();

    /// <summary>
    /// True, wenn an der Ebene selbst nichts eingestellt ist. Die Mischung zaehlt
    /// hier NICHT mit: Sie sagt, wie die Ebene auf die darunter wirkt, und das ist
    /// eine Aussage ueber den Stapel, nicht ueber die Ebene. Auf der untersten
    /// Ebene, die auf Schwarz liegt, sind Add und Normal ohnehin dasselbe.
    /// </summary>
    [JsonIgnore]
    public bool IsNeutral
        => Content == LayerContent.Pass &&
           Opacity >= 0.999f && MathF.Abs(Exposure) < 0.001f && Tint.Near(1f) && Mask.IsNeutral;

    /// <summary>Die Kette dieser Einstellungsebene, fertig vorbereitet.</summary>
    public LayerGrade Grade() => LayerGrade.Prepare(Adjustments, Tools);

    /// <summary>True, wenn die Mischung auf Schwarz nichts anderes ergibt als die Ebene selbst.</summary>
    [JsonIgnore]
    public bool LiesOnBlack => Mode is BlendMode.Add or BlendMode.Normal;

    public ImageLayer Clone() => new()
    {
        Source = Source,
        Name = Name,
        Visible = Visible,
        Mode = Mode,
        Opacity = Opacity,
        Exposure = Exposure,
        Clipped = Clipped,
        Mask = Mask.Clone(),
        Content = Content,
        Adjustments = Adjustments,
        Tools = Tools?.Clone(),
        Tint = Tint.Clone(),
    };
}

/// <summary>
/// Der Ebenenstapel, von unten nach oben.
///
/// Index 0 liegt unten. Das ist die umgekehrte Reihenfolge zu der, in der die Liste
/// angezeigt wird - oben in der Liste ist oben im Bild, wie in jedem Bildprogramm.
/// Die Umkehrung steht in der Oberflaeche und nicht hier, weil das Rechnen von
/// unten nach oben laeuft und eine rueckwaerts gelesene Schleife eine Fehlerquelle
/// mehr ist.
/// </summary>
public sealed class LayerStack
{
    public List<ImageLayer> Layers { get; set; } = new();

    /// <summary>
    /// True, wenn der Stapel nichts anderes ergibt als das Bild selbst: keine Ebene,
    /// oder genau eine unveraenderte auf dem Hauptbild.
    ///
    /// Dann wird gar nicht erst zusammengesetzt - und das ist nicht nur schneller,
    /// sondern auch genauer: Der Weg ohne Zusammensetzung reicht den Frame durch,
    /// wie er gelesen wurde.
    /// </summary>
    [JsonIgnore]
    public bool IsPassThrough
        => Layers.Count == 0 ||
           (Layers.Count == 1 && Layers[0].Visible && Layers[0].Source.Length == 0 &&
            Layers[0].IsNeutral && Layers[0].LiesOnBlack);

    /// <summary>
    /// Die Passe, die tatsaechlich gelesen werden muessen.
    ///
    /// Auch die der Masken: Eine Ebene, die ihre Maske aus dem Nebelpass zieht,
    /// braucht diesen Pass genauso wie ihren eigenen. Ihn zu vergessen ergaebe eine
    /// Maske, die still nichts tut - der unangenehmste Fehler, weil das Bild
    /// aussieht, als waere die Maske falsch eingestellt.
    /// </summary>
    public IReadOnlyList<string> NeededSources()
    {
        var names = new List<string>();

        foreach (var layer in Layers)
        {
            if (!layer.Visible) continue;

            // Eine Einstellungsebene liest keinen Pass - sie rechnet mit dem, was
            // schon da ist. Ihre Maske kann trotzdem einen brauchen.
            if (layer.Content == LayerContent.Pass &&
                !names.Contains(layer.Source, StringComparer.Ordinal))
            {
                names.Add(layer.Source);
            }

            foreach (string source in layer.Mask.Sources())
                if (!names.Contains(source, StringComparer.Ordinal)) names.Add(source);
        }

        return names;
    }

    public LayerStack Clone() => new()
    {
        Layers = Layers.Select(l => l.Clone()).ToList(),
    };
}
