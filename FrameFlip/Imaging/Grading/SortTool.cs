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

    /// <summary>
    /// Intensitaet: die Summe der drei Kanaele, ohne Gewichtung. Anders als die
    /// Helligkeit zaehlt hier Blau so viel wie Gruen - kraeftige Blau- und Rottoene
    /// wandern dadurch weiter nach oben, als das Auge sie einordnen wuerde.
    /// </summary>
    Intensity,

    /// <summary>
    /// Der kleinste der drei Kanaele. Reine Farben liegen dabei ganz unten, Weiss
    /// ganz oben - sortiert wird also nach "wie viel Grau steckt darin".
    /// </summary>
    Minimum,
}

/// <summary>Was einen Lauf begrenzt.</summary>
public enum SortInterval
{
    /// <summary>
    /// Die Schwelle allein: Ein Lauf ist, was zusammenhaengend im Fenster liegt.
    /// Das Verfahren von Kim Asendorf, mit dem Pixel Sorting angefangen hat.
    /// </summary>
    Threshold,

    /// <summary>
    /// Kanten: Ein Lauf endet zusaetzlich dort, wo die Helligkeit springt.
    ///
    /// Das haelt die Umrisse: Ein heller Gegenstand vor dunklem Grund verschmiert
    /// nicht in den Grund hinein, sondern zerlaeuft in sich. Der Unterschied zwischen
    /// "das Bild schmilzt" und "die Flaechen im Bild schmelzen".
    /// </summary>
    Edges,

    /// <summary>
    /// Zufaellige Laengen um einen Mittelwert. Kein Lauf gleicht dem naechsten, und
    /// genau das sieht nach Stoerung aus statt nach Muster.
    /// </summary>
    Random,
}

/// <summary>
/// Pixel Sorting: Laeufe von Bildpunkten werden der Reihe nach umsortiert.
///
/// Die eine Wirkung dieser Familie, fuer die es keinen Ersatz gibt, und der Grund
/// laesst sich als Zusicherung schreiben: Es ERFINDET NICHTS UND VERLIERT NICHTS.
/// Ein Raster entscheidet je Bildpunkt, ein Verzug holt je Bildpunkt woanders her -
/// beides denkt sich Werte aus. Umsortieren verschiebt Bildpunkte entlang einer
/// Linie; was herauskommt, enthaelt genau dieselben Farben wie vorher.
///
/// DAS FENSTER IST DER STAERKEREGLER. Umsortiert wird nur, was zwischen zwei
/// Schwellen liegt; dazwischen bleibt alles stehen, und die Form bleibt lesbar,
/// waehrend die Flaechen zerlaufen. In Grundstellung ist es geschlossen.
///
/// Was die Laeufe sonst begrenzt, kommt aus den bekannten Baukaesten: Kanten, die
/// Umrisse halten; zufaellige Laengen; ein Anteil von Laeufen, der unsortiert bleibt,
/// damit nicht jede Zeile gleich zerlaeuft. Dazu ein beliebiger Winkel und, wie bei
/// Asendorf, ein zweiter Durchgang quer zum ersten, der das Bild kreuzweise
/// zerschmelzen laesst.
///
/// Laeuft auf den FERTIGEN Anzeigewerten und beim Ziehen gar nicht - siehe
/// <see cref="IFramePass"/>. Die Linien untereinander sind aber unabhaengig und werden
/// deshalb nebeneinander gerechnet; nur INNERHALB einer Linie zaehlt die Reihenfolge.
/// </summary>
public sealed class SortTool : IFramePass
{
    public const string KindName = "pixelsort";

    public string Kind => KindName;

    /// <summary>Wonach sortiert wird.</summary>
    public SortKey Key { get; set; } = SortKey.Brightness;

    /// <summary>Was einen Lauf begrenzt.</summary>
    public SortInterval Interval { get; set; } = SortInterval.Threshold;

    /// <summary>
    /// Die Richtung der Laeufe in Grad: 0 von links nach rechts, 90 von oben nach
    /// unten, und alles dazwischen.
    /// </summary>
    public float Angle { get; set; }

    /// <summary>
    /// Der alte Schalter "Spalten" - gleichbedeutend mit 90 Grad.
    ///
    /// Er bleibt, damit gespeicherte Rezepte weiter lesbar sind; der Streifen
    /// uebersetzt ihn beim Laden in den Winkel und stellt ihn ab.
    /// </summary>
    public bool Vertical { get; set; }

    /// <summary>
    /// Nach dem ersten Durchgang ein zweiter, um 90 Grad gedreht - Spalten und dann
    /// Zeilen, wie bei Asendorf. Das Bild zerlaeuft kreuzweise.
    /// </summary>
    public bool Cross { get; set; }

    /// <summary>Von welchem Wert an ein Bildpunkt zu einem Lauf gehoert.</summary>
    public float Low { get; set; }

    /// <summary>Bis zu welchem. Gleich der Untergrenze heisst: gar nicht.</summary>
    public float High { get; set; }

    /// <summary>Grosse Werte zuerst.</summary>
    public bool Descending { get; set; }

    /// <summary>
    /// Bei Schwelle und Kanten die hoechste Lauflaenge, bei Zufallslaengen die
    /// mittlere - in Bildpunkten. Null heisst: so lang, wie er zusammenhaengt.
    /// </summary>
    public int Longest { get; set; }

    /// <summary>Wie stark die Helligkeit springen muss, damit ein Lauf dort endet. Nur bei Kanten.</summary>
    public float Edge { get; set; } = 0.12f;

    /// <summary>
    /// Welcher Anteil der Laeufe unsortiert bleibt.
    ///
    /// Der Regler, den jeder Baukasten hat und der hier am meisten fuer das Aussehen
    /// tut: Zerlaeuft jede Zeile gleich, sieht das Bild aus wie gekaemmt. Bleibt jede
    /// dritte stehen, sieht es aus wie gestoert.
    /// </summary>
    public float Skip { get; set; }

    /// <summary>Startwert fuer alles Zufaellige - Zufallslaengen und Auslassen.</summary>
    public int Seed { get; set; }

    /// <summary>
    /// Wie oft das Zufaellige wechselt, ueber die Sequenz: 1 jedes Bild, 0,25 jedes
    /// vierte, 0 nie.
    /// </summary>
    public float Speed { get; set; }

    [JsonIgnore]
    public bool IsNeutral => High - Low < 0.002f;

    /// <summary>Die mittlere Lauflaenge bei Zufallslaengen, wenn keine eingestellt ist.</summary>
    private const int RandomDefault = 60;

    private float _low, _high, _edge, _skip;
    private int _longest;
    private SortKey _key;
    private SortInterval _interval;
    private bool _descending;

    public void Prepare()
    {
        _low = Math.Clamp(Low, 0f, 1f);
        _high = Math.Clamp(High, 0f, 1f);

        if (_high < _low) (_low, _high) = (_high, _low);

        _longest = Math.Max(0, Longest);
        _edge = Math.Clamp(Edge, 0.001f, 1f);
        _skip = Math.Clamp(Skip, 0f, 1f);
        _key = Key;
        _interval = Interval;
        _descending = Descending;
    }

    public void Apply(IntPtr pixels, int width, int height, int stride, int number = 0)
    {
        if (width <= 0 || height <= 0) return;

        float angle = Vertical ? 90f : Angle;
        int seed = Seed + (int)MathF.Floor(number * MathF.Abs(Speed));

        Pass(pixels, width, height, stride, angle, seed);

        // Der zweite Durchgang arbeitet auf dem Ergebnis des ersten - das ist der
        // ganze Sinn. Mit einem anderen Startwert, damit die Laeufe quer nicht an
        // denselben Stellen enden wie laengs.
        if (Cross) Pass(pixels, width, height, stride, angle + 90f, seed + 7919);
    }

    /// <summary>
    /// Ein Durchgang in einer Richtung.
    ///
    /// DIE AUFTEILUNG IN LINIEN ist der Kern, und sie muss genau sein: Jeder Bildpunkt
    /// gehoert zu GENAU EINER Linie. Liefe eine Linie durch einen Punkt, den schon eine
    /// andere hatte, wuerde er zweimal umsortiert - und irgendwo fehlte einer.
    ///
    /// Deshalb keine gedrehten Koordinaten mit Runden, sondern ein Scheren: Entlang der
    /// Hauptachse (der, die dem Winkel naeher liegt) steht jede Linie auf jeder Stelle
    /// genau einmal, und der Versatz auf der Nebenachse ist fuer alle Linien derselbe.
    /// Ein Punkt (m, n) gehoert damit zur Linie n - Versatz(m), und die ist eindeutig.
    /// Solange der Winkel hoechstens 45 Grad von der Hauptachse abweicht, springt der
    /// Versatz je Schritt um hoechstens eins - die Linie bleibt zusammenhaengend.
    /// </summary>
    private void Pass(IntPtr pixels, int width, int height, int stride, float angle, int seed)
    {
        double radians = angle * Math.PI / 180.0;
        double dx = Snap(Math.Cos(radians));
        double dy = Snap(Math.Sin(radians));

        bool alongX = Math.Abs(dx) >= Math.Abs(dy);
        double slope = alongX ? dy / dx : dx / dy;
        bool forward = alongX ? dx > 0 : dy > 0;

        int major = alongX ? width : height;
        int minor = alongX ? height : width;

        var shift = new int[major];
        int lowest = 0, highest = 0;

        for (int m = 0; m < major; m++)
        {
            shift[m] = (int)Math.Floor(m * slope + 0.5);

            lowest = Math.Min(lowest, shift[m]);
            highest = Math.Max(highest, shift[m]);
        }

        int first = -highest;
        int last = minor - 1 - lowest;

        Parallel.For(first, last + 1,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8) },
            () => new Line(major),
            (k, _, line) =>
            {
                int count = 0;

                for (int s = 0; s < major; s++)
                {
                    int m = forward ? s : major - 1 - s;
                    int n = k + shift[m];

                    if (n < 0 || n >= minor) continue;

                    int x = alongX ? m : n;
                    int y = alongX ? n : m;

                    line.Path[count++] = y * stride + x * 4;
                }

                if (count > 1) SortLine(pixels, line, count, seed, k);

                return line;
            },
            _ => { });
    }

    /// <summary>Was eine Linie zum Rechnen braucht - je Faden einmal angelegt.</summary>
    private sealed class Line
    {
        public Line(int length)
        {
            Path = new int[length];
            Keys = new float[length];
            Light = new float[length];
            Order = new int[length];
            Copy = new byte[length * 4];
        }

        public readonly int[] Path;
        public readonly float[] Keys;
        public readonly float[] Light;
        public readonly int[] Order;
        public readonly byte[] Copy;
    }

    /// <summary>
    /// Sucht die Laeufe auf einer Linie und sortiert sie.
    ///
    /// Der ANFANG eines Laufs wird gemerkt, nicht seine Laenge mitgezaehlt. Ein Lauf,
    /// der am Ende abbricht, reicht bis i-1; einer, den eine Grenze abschneidet, bis i.
    /// Wer nur mitzaehlt, hat an einer der beiden Stellen einen Punkt zuviel - das war
    /// hier schon einmal ein Fehler.
    /// </summary>
    private unsafe void SortLine(IntPtr pixels, Line line, int count, int seed, int index)
    {
        var target = (byte*)pixels;

        for (int i = 0; i < count; i++)
        {
            byte* at = target + line.Path[i];

            line.Keys[i] = KeyOf(at[2], at[1], at[0]);
            line.Light[i] = (0.2126f * at[2] + 0.7152f * at[1] + 0.0722f * at[0]) / 255f;
        }

        int begin = -1;
        int limit = 0;

        for (int i = 0; i <= count; i++)
        {
            bool inside = i < count && line.Keys[i] >= _low && line.Keys[i] <= _high;

            // Eine Kante beendet den laufenden Lauf VOR diesem Punkt; der Punkt selbst
            // beginnt gleich darauf einen neuen.
            if (inside && begin >= 0 && _interval == SortInterval.Edges &&
                MathF.Abs(line.Light[i] - line.Light[i - 1]) > _edge)
            {
                Close(target, line, begin, i - begin, seed, index);
                begin = -1;
            }

            if (inside)
            {
                if (begin < 0)
                {
                    begin = i;
                    limit = LimitFor(index, begin, seed);
                }

                if (limit <= 0 || i - begin + 1 < limit) continue;

                Close(target, line, begin, i - begin + 1, seed, index);
                begin = -1;
                continue;
            }

            if (begin < 0) continue;

            Close(target, line, begin, i - begin, seed, index);
            begin = -1;
        }
    }

    /// <summary>
    /// Wie lang dieser Lauf hoechstens werden darf.
    ///
    /// Bei Zufallslaengen zwischen einem Viertel und dem Eindreiviertelfachen des
    /// Mittelwerts - so streuen sie deutlich, und der Mittelwert stimmt trotzdem.
    /// </summary>
    private int LimitFor(int line, int begin, int seed)
    {
        if (_interval != SortInterval.Random) return _longest;

        int mean = _longest > 0 ? _longest : RandomDefault;

        return Math.Max(2, (int)MathF.Round(mean * (0.25f + 1.5f * GlitchNoise.Unit(line, begin, seed, 11))));
    }

    /// <summary>Schliesst einen Lauf - und sortiert ihn, wenn er nicht ausgelassen wird.</summary>
    private unsafe void Close(byte* target, Line line, int from, int length, int seed, int index)
    {
        if (length < 2) return;

        if (_skip > 0f && GlitchNoise.Unit(index, from, seed, 13) < _skip) return;

        SortRun(target, line, from, length);
    }

    /// <summary>
    /// Sortiert einen Lauf - und traegt die Bildpunkte in der neuen Reihenfolge ein.
    ///
    /// Sortiert wird eine INDEXLISTE und nicht der Puffer selbst: Ein Bildpunkt sind
    /// vier Bytes, und sie beim Vergleichen hin- und herzuschieben waere viermal so
    /// viel Arbeit fuer dasselbe Ergebnis.
    /// </summary>
    private unsafe void SortRun(byte* target, Line line, int from, int length)
    {
        var order = line.Order;
        var keys = line.Keys;

        for (int i = 0; i < length; i++) order[i] = from + i;

        var slice = order.AsSpan(0, length);

        if (_descending) slice.Sort((a, b) => keys[b].CompareTo(keys[a]));
        else slice.Sort((a, b) => keys[a].CompareTo(keys[b]));

        // Erst wegschreiben, dann zurueck: Quelle und Ziel sind derselbe Puffer, und
        // ohne Zwischenkopie ueberschriebe der dritte Punkt, was der siebte noch
        // braucht.
        var copy = line.Copy;

        for (int i = 0; i < length; i++)
        {
            byte* at = target + line.Path[order[i]];

            copy[i * 4] = at[0];
            copy[i * 4 + 1] = at[1];
            copy[i * 4 + 2] = at[2];
            copy[i * 4 + 3] = at[3];
        }

        for (int i = 0; i < length; i++)
        {
            byte* at = target + line.Path[from + i];

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

        switch (_key)
        {
            case SortKey.Brightness:
                return 0.2126f * r + 0.7152f * g + 0.0722f * b;

            case SortKey.Intensity:
                return (r + g + b) / 3f;

            case SortKey.Minimum:
                return MathF.Min(r, MathF.Min(g, b));
        }

        float high = MathF.Max(r, MathF.Max(g, b));
        float low = MathF.Min(r, MathF.Min(g, b));
        float chroma = high - low;

        if (_key == SortKey.Saturation) return high <= 1e-6f ? 0f : chroma / high;

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

    /// <summary>
    /// Null und Eins, wo sie gemeint sind.
    ///
    /// cos(90 Grad) ist in Gleitkomma nicht null, sondern 6e-17. Das allein waere
    /// harmlos - aber es entscheidet, welche Achse die Hauptachse ist, und damit bei
    /// genau 45 Grad, ob in Zeilen oder in Spalten geschert wird.
    /// </summary>
    private static double Snap(double value)
    {
        if (Math.Abs(value) < 1e-9) return 0;
        if (Math.Abs(Math.Abs(value) - 1) < 1e-9) return Math.Sign(value);

        return value;
    }

    public SortTool Clone() => new()
    {
        Key = Key,
        Interval = Interval,
        Angle = Angle,
        Vertical = Vertical,
        Cross = Cross,
        Low = Low,
        High = High,
        Descending = Descending,
        Longest = Longest,
        Edge = Edge,
        Skip = Skip,
        Seed = Seed,
        Speed = Speed,
    };
}
