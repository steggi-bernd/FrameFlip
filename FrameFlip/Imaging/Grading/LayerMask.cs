using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Woher eine Maske ihren Wert nimmt.</summary>
public enum MaskKind
{
    /// <summary>Keine. Die Ebene wirkt ueberall gleich.</summary>
    None,

    /// <summary>Die Helligkeit der Ebene selbst - "nur wo dieser Pass hell ist".</summary>
    Luminance,

    /// <summary>Die Helligkeit dessen, was schon darunter liegt - "nur in den Schatten des Bildes".</summary>
    Underlying,

    /// <summary>Ein anderer Pass der Datei: Nebel, Verschattung, eine Indexmaske.</summary>
    Pass,

    /// <summary>Ein Verlauf ueber das Bild.</summary>
    Gradient,

    /// <summary>
    /// Ein Farbbereich - "nur wo es blau ist".
    ///
    /// Die Frage, die eine Helligkeitsmaske nicht beantworten kann: Himmel und
    /// Hautton koennen gleich hell sein und haben trotzdem nichts miteinander zu
    /// tun. Gemessen wird der Farbton auf dem Farbkreis, nicht der Abstand im
    /// RGB-Wuerfel - sonst waere ein dunkles Blau ein anderer Bereich als ein
    /// helles.
    ///
    /// Gelesen wird wie bei <see cref="Underlying"/> das, was schon DA ist, nicht
    /// die Ebene selbst: Eine weisse Flaeche hat keinen Farbton, und eine Korrektur,
    /// die den Farbton verschiebt, zoege sich sonst die eigene Maske weg.
    /// </summary>
    Colour,

    /// <summary>
    /// Eine Kryptomatte - Objekte oder Materialien, ueber die ganze Sequenz hinweg
    /// dieselben.
    /// </summary>
    Cryptomatte,

    /// <summary>
    /// Eine gemalte Maske - von Hand aufgetragen, mit einem Pinsel.
    ///
    /// Die einzige, die nicht abgeleitet ist. Alle anderen sagen "wo es hell ist"
    /// oder "wo dieses Objekt steht"; diese sagt "genau hier", und das ist die eine
    /// Frage, die kein Programm fuer einen beantworten kann.
    /// </summary>
    Painted,
}

/// <summary>
/// Worauf eine Maske wirkt - und das ist nicht immer dasselbe.
/// </summary>
public enum MaskScope
{
    /// <summary>
    /// Die Maske sagt, WO DIE EBENE ZU SEHEN IST.
    ///
    /// Das Richtige fuer alles, was zum Bild hinzukommt: ein Glanz, der nur oben
    /// links liegen soll, ein Wasserzeichen in einer Ecke, eine zweite Aufnahme, die
    /// nur halb eingeblendet wird. Ausserhalb der Maske traegt die Ebene nichts bei.
    /// </summary>
    Visibility,

    /// <summary>
    /// Die Maske sagt, WO DIE KORREKTUR DIESER EBENE GILT. Die Ebene selbst bleibt
    /// ueberall zu sehen.
    ///
    /// Das Richtige fuer das Grundbild, und der haeufigere Fall: Wer eine Maske auf
    /// sein Bild malt, will dort etwas AENDERN - nicht den Rest wegwerfen. Gemeint
    /// ist dasselbe, als laege ueber der Ebene eine Kopie von ihr, die nur den
    /// ausgewaehlten Bereich zeigt und nur dort korrigiert ist.
    ///
    /// Ohne diese Einstellung war eine Maske auf dem Grundbild eine Falle: Man malte
    /// einen Fleck, um ihn aufzuhellen, und das ganze Bild ausser dem Fleck
    /// verschwand.
    /// </summary>
    Colour,
}

/// <summary>
/// Die Maske einer Ebene: ein Wert zwischen 0 und 1 je Bildpunkt, der sagt, wie
/// stark die Ebene dort wirkt.
///
/// Sie greift an genau einer Stelle an - sie multipliziert die Deckkraft. Damit gilt
/// fuer jede Mischung, jede Schnittmaske und jede Ebene dieselbe Regel, und es gibt
/// keinen Fall, in dem eine Maske "anders" wirkt. Eine Maske, die je nach Mischung
/// etwas anderes bedeutete, waere nicht zu erklaeren.
///
/// Flach statt als Typhierarchie: Die Arten teilen sich fast alle Felder - der
/// Bereich mit weichen Kanten gilt fuer Helligkeit, Untergrund und Pass gleichermassen.
/// Fuenf Klassen fuer acht Felder waeren mehr Geruest als Inhalt, und gespeichert
/// wird es so ohne Typkennung.
/// </summary>
public sealed class LayerMask
{
    public MaskKind Kind { get; set; }

    /// <summary>
    /// Die feste Kennung dieser Maske - an ihr haengen Verlauf und "geteilt mit" (siehe
    /// docs/Projekte-und-Masken.md, Abschnitt 3.4). Leer bei Masken aus der Zeit davor; sie
    /// bekommen eine, sobald jemand fragt (<see cref="EnsureId"/>). Eine Kopie fuer den
    /// Export behaelt sie, ein Duplikat bekommt eine neue.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Die Kennung - vergeben, falls noch keine da ist.</summary>
    public string EnsureId()
    {
        if (Id.Length == 0) Id = Guid.NewGuid().ToString("N")[..12];
        return Id;
    }

    /// <summary>
    /// Ob die gemalte Maske fuer die ganze Sequenz gilt.
    ///
    /// Gesperrt heisst: EIN Anstrich, fuer jedes Bild derselbe. Entsperrt heisst: je
    /// Bild ein eigener, und man kann ein einzelnes Bild retuschieren, ohne die
    /// anderen anzufassen.
    ///
    /// Nur gemalte Masken brauchen diesen Schalter. Eine Kryptomatte passt sich von
    /// selbst an jedes Bild an - sie waehlt ein OBJEKT und keinen Ort -, und eine
    /// Helligkeitsmaske rechnet ohnehin je Bild neu. Statisch ist allein, was jemand
    /// von Hand aufgetragen hat, und genau das ist fuer eine Animation meistens
    /// unbrauchbar, solange es nicht gesperrt ist.
    /// </summary>
    public bool PaintLocked { get; set; } = true;

    /// <summary>Der eine Anstrich, wenn gesperrt.</summary>
    public PaintedMask? Paint { get; set; }

    /// <summary>Ein Anstrich je Bildnummer, wenn entsperrt.</summary>
    public Dictionary<int, PaintedMask> PaintFrames { get; set; } = new();

    /// <summary>
    /// Der Anstrich, der fuer dieses Bild gilt - oder null.
    ///
    /// An einer Stelle, weil die Frage "welcher gilt hier" an vier Stellen aufkommt:
    /// beim Rechnen, beim Malen, beim Speichern und beim Anzeigen. Vier Antworten
    /// waeren vier Gelegenheiten, die Sperre zu uebersehen.
    /// </summary>
    public PaintedMask? PaintFor(int number)
        => PaintLocked ? Paint : PaintFrames.GetValueOrDefault(number);

    /// <summary>Legt den Anstrich fuer dieses Bild an, wenn es noch keinen gibt.</summary>
    public PaintedMask PaintOn(int number, int imageWidth, int imageHeight)
    {
        if (PaintFor(number) is { } found) return found;

        var made = PaintedMask.For(imageWidth, imageHeight);

        if (PaintLocked) Paint = made;
        else PaintFrames[number] = made;

        return made;
    }

    /// <summary>Dreht die Maske um. Aus "nur in den Lichtern" wird "ueberall ausser in den Lichtern".</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Ob die Maske die Sichtbarkeit oder die Korrektur begrenzt - siehe <see
    /// cref="MaskScope"/>.
    ///
    /// Die Grundstellung ist die Sichtbarkeit, und zwar wegen der Rezepte, die es
    /// schon gibt: Ein gespeicherter Glanz mit Verlaufsmaske soll nach dem naechsten
    /// Start dasselbe tun wie vorher. Der Streifen stellt bei einer NEU gewaehlten
    /// Maske auf einer Bildebene von sich aus auf Farbe um - dort ist es fast immer
    /// das Gemeinte.
    /// </summary>
    public MaskScope Scope { get; set; } = MaskScope.Visibility;

    // ---------------------------------------------------------------- der Bereich

    /// <summary>Untere Grenze des Bereichs, der durchlaesst. 0 bis 1.</summary>
    public float Low { get; set; }

    /// <summary>Obere Grenze. 1 heisst: nach oben offen.</summary>
    public float High { get; set; } = 1f;

    /// <summary>
    /// Wie weit die Kanten ausgefranst sind, nach aussen gerechnet.
    ///
    /// Nach AUSSEN und nicht nach innen, damit die Grundstellung nichts tut: Bei 0
    /// bis 1 liegen beide Rampen ausserhalb des Wertebereichs, und die Maske laesst
    /// alles durch. Waeren sie nach innen gerichtet, waere schon die Grundstellung
    /// eine Abdunklung an beiden Enden - und niemand suchte den Grund bei der Maske.
    /// </summary>
    public float Softness { get; set; } = 0.1f;

    // ----------------------------------------------------------------- der Verlauf

    /// <summary>Richtung in Grad. 0 laeuft von links nach rechts, 90 von oben nach unten.</summary>
    public float Angle { get; set; } = 90f;

    /// <summary>Wo die Mitte des Uebergangs liegt, 0 bis 1 laengs der Richtung.</summary>
    public float Centre { get; set; } = 0.5f;

    /// <summary>Wie breit der Uebergang ist. 0 ist eine Kante.</summary>
    public float Width { get; set; } = 0.5f;

    /// <summary>
    /// Der gesuchte Farbton in Grad auf dem Farbkreis - 0 rot, 120 gruen, 240 blau.
    ///
    /// Nur fuer <see cref="MaskKind.Colour"/>. In Grad und nicht als Farbe, weil ein
    /// Farbbereich keine Helligkeit meint: "blau" ist eine Richtung, kein Punkt.
    /// </summary>
    public float Hue { get; set; }

    /// <summary>
    /// Wie weit um den Farbton herum noch dazugehoert, in Grad.
    ///
    /// Dreissig ist ungefaehr ein Sechstel des Kreises - eng genug, um Blau von
    /// Tuerkis zu trennen, weit genug, um einen Himmel nicht zu zerreissen.
    /// </summary>
    public float Spread { get; set; } = 30f;

    // ------------------------------------------------------------------ die Quelle

    /// <summary>
    /// Der Pass, aus dem die Maske kommt - bei <see cref="MaskKind.Pass"/> ein
    /// einzelner, bei <see cref="MaskKind.Cryptomatte"/> der Name des Satzes.
    /// Sonst leer.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Die Stufen einer Kryptomatte, in der Reihenfolge 00, 01, 02.
    ///
    /// Sie stehen hier und werden nicht bei Bedarf erraten, weil ein Rezept
    /// aufschreiben soll, was es liest. Erraten hiesse, bei jedem Bild acht Stufen
    /// zu probieren, von denen es drei gibt.
    /// </summary>
    public List<string> Levels { get; set; } = new();

    /// <summary>
    /// Was ausgewaehlt ist. Leer heisst: nichts - die Maske laesst dann nichts durch,
    /// und das ist richtig so, denn eine Auswahl ohne Gewaehltes ist leer.
    /// </summary>
    public List<CryptoPick> Picks { get; set; } = new();

    /// <summary>
    /// Nimmt ein gewaehltes Objekt auf - oder, wenn es schon darin ist, wieder heraus.
    /// Noch einmal auf dasselbe Objekt zu klicken ist der Griff, den man ohnehin
    /// versucht, und er erspart das Zielen auf ein Kreuzchen in einer schmalen Liste.
    /// </summary>
    public void TogglePick(string name, float id)
    {
        var already = Picks.FirstOrDefault(p => p.Id.Equals(id));

        if (already is not null) Picks.Remove(already);
        else Picks.Add(new CryptoPick { Name = name, Id = id });
    }

    [JsonIgnore]
    public bool IsNeutral => Kind == MaskKind.None;

    /// <summary>Ob die Maske eigene Passe braucht, die gelesen werden muessen.</summary>
    [JsonIgnore]
    public bool NeedsSource
        => (Kind == MaskKind.Pass && Source.Length > 0) ||
           (Kind == MaskKind.Cryptomatte && Levels.Count > 0);

    /// <summary>Die Passe, die diese Maske zu lesen verlangt.</summary>
    public IEnumerable<string> Sources()
    {
        if (Kind == MaskKind.Pass && Source.Length > 0) yield return Source;

        if (Kind != MaskKind.Cryptomatte) yield break;

        foreach (string level in Levels) yield return level;
    }

    public LayerMask Clone() => new()
    {
        Kind = Kind,
        Id = Id,
        Invert = Invert,
        Scope = Scope,
        Low = Low,
        High = High,
        Softness = Softness,
        Angle = Angle,
        Centre = Centre,
        Width = Width,
        Hue = Hue,
        Spread = Spread,
        Source = Source,
        Levels = new List<string>(Levels),
        Picks = Picks.Select(p => p.Clone()).ToList(),
        PaintLocked = PaintLocked,
        Paint = Paint?.Clone(),
        PaintFrames = PaintFrames.ToDictionary(e => e.Key, e => e.Value.Clone()),
    };
}

/// <summary>
/// Ein ausgewaehltes Objekt oder Material einer Kryptomatte.
///
/// Beides wird mitgefuehrt - der Name, weil er lesbar ist und in der Liste steht,
/// und die Kennung, weil nur sie im Bild steht. Die Kennung aus dem Namen neu zu
/// bilden waere moeglich (sie ist sein Hash), hiesse aber, die Hashfunktion
/// nachzubauen und auf ewig genau so zu lassen, wie Blender sie heute hat.
/// </summary>
public sealed class CryptoPick
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Die Kennung, wie sie im Bild steht: der Hash des Namens, als Gleitkomma
    /// umgedeutet. Verglichen wird auf genaue Gleichheit.
    /// </summary>
    public float Id { get; set; }

    public CryptoPick Clone() => new() { Name = Name, Id = Id };
}

/// <summary>Die Rechnung hinter einer Maske.</summary>
public static class Masking
{
    /// <summary>
    /// Ein Bereich mit weichen Kanten.
    ///
    /// Die Rampen liegen AUSSERHALB von [low, high] und laufen nach aussen auf null.
    /// Damit laesst die Grundstellung 0 bis 1 alles durch, egal wie weich sie
    /// eingestellt ist - und "nur die Lichter" ist ein Griff an einen Regler und
    /// nicht ein Abgleich zweier.
    /// </summary>
    public static float Band(float value, float low, float high, float softness)
    {
        softness = MathF.Max(0f, softness);

        float lower = value >= low
            ? 1f
            : softness <= 0f ? 0f : Smooth((value - (low - softness)) / softness);

        float upper = value <= high
            ? 1f
            : softness <= 0f ? 0f : Smooth(((high + softness) - value) / softness);

        return lower * upper;
    }

    /// <summary>
    /// Ein Schwarz- und ein Weisspunkt auf einem fertigen Maskenpass.
    ///
    /// Das ist bewusst NICHT <see cref="Band"/>. Ein Nebel- oder Verschattungspass
    /// ist bereits eine Maske - sein Wert IST der Anteil, und er gehoert
    /// durchgereicht, nicht durch ein Fenster gesehen. Mit dem Bereichsfenster
    /// darauf taete eine frisch gewaehlte Passmaske in Grundstellung gar nichts,
    /// weil jeder Wert zwischen 0 und 1 im Fenster 0 bis 1 liegt: eine Einstellung,
    /// die sich einschalten laesst und schweigt.
    ///
    /// Linear und nicht geglaettet, weil ein Schwarz- und ein Weisspunkt genau das
    /// sind, was jeder kennt, der schon einmal eine Maske angezogen hat - und weil
    /// 0 bis 1 dann wirklich unveraendert durchreicht.
    /// </summary>
    public static float Levels(float value, float low, float high)
    {
        if (high <= low) return value >= low ? 1f : 0f;

        return Math.Clamp((value - low) / (high - low), 0f, 1f);
    }

    /// <summary>
    /// Ein Verlauf ueber das Bild.
    ///
    /// Die Richtung wird in Bildkoordinaten gerechnet, in denen y nach UNTEN
    /// waechst - deshalb heisst 90 Grad "von oben nach unten" und nicht umgekehrt.
    /// Das ist die Richtung, in der jemand den Himmel abdunkelt, und damit die, die
    /// man erwartet.
    /// </summary>
    public static float Gradient(int x, int y, int width, int height,
                                 float cos, float sin, float from, float to)
    {
        float u = (x + 0.5f) / width - 0.5f;
        float v = (y + 0.5f) / height - 0.5f;

        float t = u * cos + v * sin + 0.5f;

        if (to <= from) return t >= from ? 1f : 0f;

        return Smooth((t - from) / (to - from));
    }

    /// <summary>
    /// Lineares Licht in einen Wert zwischen 0 und 1, auf dem sich "Schatten" und
    /// "Lichter" sagen laesst.
    ///
    /// Dieselbe Abbildung, die auch die Kontrastmischungen benutzen: mittleres Grau
    /// landet genau auf 0,5. Eine Maske "nur die Lichter" meint damit dasselbe, was
    /// das Auge meint, und nicht "alles ueber 0,5 Lichtmenge" - das waere schon fast
    /// das ganze Bild.
    /// </summary>
    public static float Perceptual(float light) => Blending.ToDisplay(light);

    /// <summary>
    /// Die Deckung der ausgewaehlten Objekte an einem Bildpunkt.
    ///
    /// Jede Stufe traegt zwei Paare aus Kennung und Deckung - r/g und b/a. Gesucht
    /// wird ueber alle Stufen und alle ausgewaehlten Kennungen, und die Deckungen
    /// werden addiert: Ein Bildpunkt an der Kante zweier ausgewaehlter Objekte
    /// gehoert zu beiden, und zusammen decken sie ihn ganz.
    ///
    /// Verglichen wird auf GENAUE Gleichheit. Das ist hier kein Leichtsinn, sondern
    /// Pflicht: Die Kennungen sind Hashwerte, und zwei benachbarte Hashes gehoeren zu
    /// zwei voellig verschiedenen Objekten. Ein Toleranzband waere eine Verwechslung.
    /// </summary>
    public static float Coverage(IReadOnlyList<FloatFrame> levels, IReadOnlyList<float> ids, int at)
    {
        float sum = 0f;

        for (int l = 0; l < levels.Count; l++)
        {
            var frame = levels[l];

            float firstId = frame.R[at];
            float secondId = frame.B[at];

            for (int k = 0; k < ids.Count; k++)
            {
                float id = ids[k];

                if (firstId == id) sum += frame.G[at];
                if (secondId == id && frame.A is not null) sum += frame.A[at];
            }
        }

        return Math.Clamp(sum, 0f, 1f);
    }

    /// <summary>Die glatte Stufe. Ohne sie haette jede Maske eine sichtbare Kante.</summary>
    private static float Smooth(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;

        return t * t * (3f - 2f * t);
    }
}
