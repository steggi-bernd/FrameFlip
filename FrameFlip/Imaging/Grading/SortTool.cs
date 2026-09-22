using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>Wonach sortiert wird - und damit, was der Lauf am Ende zeigt.</summary>
public enum SortKey
{
    /// <summary>Helligkeit. Der uebliche Fall: Laeufe werden zu Verlaeufen von dunkel nach hell.</summary>
    Brightness,

    /// <summary>
    /// Farbton. Ein Lauf wird zum Regenbogen in der Reihenfolge des Farbkreises -
    /// auffaelliger und schneller zuviel, aber es ist der Griff, mit dem aus einem
    /// Verlauf ein Band wird.
    /// </summary>
    Hue,

    /// <summary>
    /// Saettigung. Trennt Blasses von Kraeftigem, ohne die Helligkeit anzufassen -
    /// der leiseste der drei und der einzige, der die Form eines Gegenstands
    /// stehenlaesst.
    /// </summary>
    Saturation,
}

/// <summary>
/// Pixel Sorting: Laeufe von Bildpunkten werden der Reihe nach umsortiert.
///
/// Die eine Wirkung dieser Familie, fuer die es keinen Ersatz gibt. Ein Raster
/// entscheidet je Bildpunkt, ein Verzug holt je Bildpunkt woanders her - beides laesst
/// sich mit genug Reglern annaehern. Umsortieren nicht: Es verschiebt Bildpunkte
/// entlang einer Zeile, ohne einen einzigen zu erfinden oder wegzuwerfen. Was
/// herauskommt, enthaelt genau dieselben Farben wie das Original, nur in anderer
/// Ordnung.
///
/// DIE SCHWELLE IST DAS GANZE WERKZEUG. Sortierte man jede Zeile vollstaendig, bekaeme
/// man einen Farbverlauf und kein Bild - das ist nach dem zweiten Versuch langweilig.
/// Interessant wird es, weil nur die Punkte mitsortiert werden, deren Wert ZWISCHEN
/// zwei Schwellen liegt: Das Bild zerfaellt in zusammenhaengende Laeufe, dazwischen
/// bleibt alles stehen, und die Form bleibt lesbar, waehrend die Flaechen zerlaufen.
///
/// In Grundstellung ist das Fenster geschlossen (von 0 bis 0) und nichts wird
/// umsortiert. Es aufzuziehen ist der Staerkeregler dieses Werkzeugs.
///
/// Laeuft auf den FERTIGEN Anzeigewerten und beim Ziehen gar nicht - siehe
/// <see cref="IFramePass"/>. Die Reihenfolge ist hier nicht Bequemlichkeit, sondern
/// das Verfahren: Ein Lauf ist eine zusammenhaengende Strecke, und auf einem groben
/// Gitter waere er eine andere Strecke.
/// </summary>
public sealed class SortTool : IFramePass
{
    public const string KindName = "pixelsort";

    public string Kind => KindName;

    /// <summary>Wonach sortiert wird.</summary>
    public SortKey Key { get; set; } = SortKey.Brightness;

    /// <summary>Spalten statt Zeilen. Senkrechte Laeufe sehen aus wie Regen.</summary>
    public bool Vertical { get; set; }

    /// <summary>Von welchem Wert an ein Bildpunkt zu einem Lauf gehoert.</summary>
    public float Low { get; set; }

    /// <summary>Bis zu welchem. Gleich der Untergrenze heisst: gar nicht.</summary>
    public float High { get; set; }

    /// <summary>Grosse Werte zuerst.</summary>
    public bool Descending { get; set; }

    /// <summary>
    /// Wie lang ein Lauf hoechstens werden darf, in Bildpunkten. Null heisst: so
    /// lang, wie er zusammenhaengt.
    ///
    /// Der Regler, der aus einem Effekt eine Gestaltung macht. Ohne Grenze frisst ein
    /// einziger Lauf ueber eine gleichmaessige Flaeche die halbe Zeile; mit einer
    /// Grenze entstehen Streifen von erkennbarer Laenge, die sich wiederholen.
    /// </summary>
    public int Longest { get; set; }

    [JsonIgnore]
    public bool IsNeutral => High - Low < 0.002f;

    private float _low, _high;
    private int _longest;

    public void Prepare()
    {
        _low = Math.Clamp(Low, 0f, 1f);
        _high = Math.Clamp(High, 0f, 1f);

        if (_high < _low) (_low, _high) = (_high, _low);

        _longest = Math.Max(0, Longest);
    }

    public unsafe void Apply(IntPtr pixels, int width, int height, int stride, int number = 0)
    {
        if (width <= 0 || height <= 0) return;

        var target = (byte*)pixels;

        // Zeilen oder Spalten - derselbe Rechenweg, nur ein anderer Schritt durch den
        // Puffer. Zwei Schleifen dafuer zu schreiben hiesse, die zweite spaeter zu
        // vergessen.
        int lines = Vertical ? width : height;
        int along = Vertical ? height : width;

        int stepAlong = Vertical ? stride : 4;
        int stepLine = Vertical ? 4 : stride;

        // Einmal angelegt und wiederverwendet: Bei 4K waeren es sonst viertausend
        // Feldanlagen je Bild.
        var keys = new float[along];
        var order = new int[along];
        var copy = new byte[along * 4];

        for (int line = 0; line < lines; line++)
        {
            byte* start = target + line * stepLine;

            // Erst den Schluessel je Punkt, dann die Laeufe darin suchen.
            for (int i = 0; i < along; i++)
            {
                byte* at = start + i * stepAlong;

                keys[i] = KeyOf(at[2], at[1], at[0]);
            }

            // Wo der laufende Lauf anfing. Minus eins heisst: gerade keiner.
            //
            // Der ANFANG wird gemerkt und nicht die Laenge gezaehlt. Das ist nicht
            // dasselbe, und der Unterschied war ein Fehler: Ein Lauf, der am Ende
            // abbricht, reicht bis i-1; einer, den die Laengengrenze abschneidet,
            // reicht bis i. Wer nur mitzaehlt, hat an einer der beiden Stellen einen
            // Punkt zuviel - und greift damit auf einen Schluessel, der gar nicht zum
            // Lauf gehoert.
            int begin = -1;

            for (int i = 0; i <= along; i++)
            {
                bool inside = i < along && keys[i] >= _low && keys[i] <= _high;

                if (inside)
                {
                    if (begin < 0) begin = i;

                    // Eine Laengengrenze schneidet den Lauf ab und faengt sofort einen
                    // neuen an - nicht "und der Rest bleibt stehen". Sonst waere die
                    // Grenze ein Regler, der bei langen Flaechen etwas anderes tut als
                    // bei kurzen.
                    if (_longest <= 0 || i - begin + 1 < _longest) continue;

                    if (i - begin + 1 > 1)
                        SortRun(start, begin, i - begin + 1, stepAlong, keys, order, copy);

                    begin = -1;
                    continue;
                }

                if (begin < 0) continue;

                if (i - begin > 1) SortRun(start, begin, i - begin, stepAlong, keys, order, copy);

                begin = -1;
            }
        }
    }

    /// <summary>
    /// Sortiert einen Lauf - und traegt die Bildpunkte in der neuen Reihenfolge ein.
    ///
    /// Sortiert wird eine INDEXLISTE und nicht der Puffer selbst: Ein Bildpunkt sind
    /// vier Bytes, und sie beim Vergleichen hin- und herzuschieben waere viermal so
    /// viel Arbeit fuer dasselbe Ergebnis.
    /// </summary>
    private unsafe void SortRun(byte* start, int from, int length, int stepAlong,
                                float[] keys, int[] order, byte[] copy)
    {
        for (int i = 0; i < length; i++) order[i] = from + i;

        var slice = order.AsSpan(0, length);
        var by = keys;

        // Array.Sort mit einem Vergleich statt LINQ: Ein Lauf kann ueber tausend
        // Punkte lang sein, und davon gibt es je Bild einige tausend.
        slice.Sort((a, b) => Descending ? by[b].CompareTo(by[a]) : by[a].CompareTo(by[b]));

        // Erst wegschreiben, dann zurueck: Quelle und Ziel sind derselbe Puffer, und
        // ohne Zwischenkopie ueberschriebe der dritte Punkt, was der siebte noch
        // braucht.
        for (int i = 0; i < length; i++)
        {
            byte* at = start + order[i] * stepAlong;

            copy[i * 4] = at[0];
            copy[i * 4 + 1] = at[1];
            copy[i * 4 + 2] = at[2];
            copy[i * 4 + 3] = at[3];
        }

        for (int i = 0; i < length; i++)
        {
            byte* at = start + (from + i) * stepAlong;

            at[0] = copy[i * 4];
            at[1] = copy[i * 4 + 1];
            at[2] = copy[i * 4 + 2];
            at[3] = copy[i * 4 + 3];
        }
    }

    /// <summary>Der Wert, nach dem verglichen wird - und der die Schwelle entscheidet.</summary>
    private float KeyOf(byte red, byte green, byte blue)
    {
        float r = red / 255f, g = green / 255f, b = blue / 255f;

        if (Key == SortKey.Brightness) return 0.2126f * r + 0.7152f * g + 0.0722f * b;

        float high = MathF.Max(r, MathF.Max(g, b));
        float low = MathF.Min(r, MathF.Min(g, b));
        float chroma = high - low;

        if (Key == SortKey.Saturation) return high <= 1e-6f ? 0f : chroma / high;

        // Farbton, auf 0 bis 1 gelegt. Grau hat keinen - es kommt auf null und
        // sammelt sich damit an einem Ende, was richtig ist: Es gehoert nirgends
        // dazwischen.
        if (chroma <= 1e-6f) return 0f;

        float hue = high == r
            ? ((g - b) / chroma) % 6f
            : high == g
                ? (b - r) / chroma + 2f
                : (r - g) / chroma + 4f;

        if (hue < 0f) hue += 6f;

        return hue / 6f;
    }

    public SortTool Clone() => new()
    {
        Key = Key,
        Vertical = Vertical,
        Low = Low,
        High = High,
        Descending = Descending,
        Longest = Longest,
    };
}
