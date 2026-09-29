using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Deflicker (docs/Atelier-Werkzeugplan.md, W2f): gleicht Helligkeitsschwankungen von Bild zu Bild
/// aus - etwa bei einem Render, dessen Licht zwischen den Bildern leicht springt.
///
/// Das braucht die ganze Folge. Einmal gemessen, im Hintergrund, steht je Bildnummer die mittlere
/// Helligkeit des linearen Lichts im Werkzeug. Daraus wird ein geglaetteter Verlauf, und jedes Bild
/// bekommt den Faktor, der es auf diesen Verlauf hebt oder senkt.
///
/// Geglaettet wird im Logarithmus, mit einer Geraden durch das Fenster um jedes Bild: Flackern ist
/// ein Faktor, und eine gewollte Blende - ein gleichmaessiger Anstieg - liegt auf ihrer eigenen
/// Geraden und bleibt unberuehrt. An den Enden rueckt das Fenster nach innen, statt zu schrumpfen;
/// so werden auch das erste und das letzte Bild ausgeglichen.
///
/// Gerechnet wird auf der linearen Seite, als Linse: ein Faktor auf das Licht, bevor die
/// Sichtumwandlung es formt. Der Ort ist gleichgueltig, gebraucht wird die Bildnummer.
/// </summary>
public sealed class DeflickerTool : IOpticsTool
{
    public const string KindName = "deflicker";

    public string Kind => KindName;

    public OpticsStage Stage => OpticsStage.Lens;

    /// <summary>Die gemessene Helligkeit je Bildnummer - das Mittel des linearen Lichts.</summary>
    public Dictionary<int, float>? Levels { get; set; }

    /// <summary>Wie weit ausgeglichen wird, 0 bis 1.</summary>
    public float Amount { get; set; }

    /// <summary>Ueber wie viele Bilder geglaettet wird.</summary>
    public int Window { get; set; } = 9;

    [JsonIgnore]
    public bool IsNeutral => Amount < 0.001f || Levels is not { Count: > 1 };

    private float[] _gains = Array.Empty<float>();
    private int _first;

    public void Prepare()
    {
        if (IsNeutral)
        {
            _gains = Array.Empty<float>();
            return;
        }

        var gains = Gains(Levels!, Window, Amount);
        int first = gains.Keys.Min(), last = gains.Keys.Max();
        var table = new float[last - first + 1];

        Array.Fill(table, 1f);
        foreach (var (number, gain) in gains) table[number - first] = gain;

        _first = first;
        _gains = table;
    }

    public void Apply(in OpticsPlace place, int x, int y, ref float r, ref float g, ref float b)
    {
        int i = place.Number - _first;
        if ((uint)i >= (uint)_gains.Length) return;

        float gain = _gains[i];

        r *= gain;
        g *= gain;
        b *= gain;
    }

    /// <summary>
    /// Der Faktor je Bild: der geglaettete Verlauf durch den gemessenen, im Logarithmus - je Bild eine
    /// Gerade durch das Fenster, ueber die Bildnummern, an seiner Stelle abgelesen. Mitten in der Folge
    /// ist das das Mittel des Fensters; an den Enden rueckt das Fenster nach innen.
    /// </summary>
    public static Dictionary<int, float> Gains(IReadOnlyDictionary<int, float> levels, int window, float amount)
    {
        var numbers = levels.Where(p => p.Value > 0f).Select(p => p.Key).OrderBy(n => n).ToArray();
        var logs = numbers.Select(n => Math.Log(levels[n])).ToArray();
        int size = Math.Min(numbers.Length, Math.Max(3, window | 1));
        float strength = Math.Clamp(amount, 0f, 1f);
        var gains = new Dictionary<int, float>();

        for (int i = 0; i < numbers.Length; i++)
        {
            int from = Math.Clamp(i - size / 2, 0, numbers.Length - size), to = from + size - 1;
            double mx = 0, my = 0;

            for (int j = from; j <= to; j++)
            {
                mx += numbers[j];
                my += logs[j];
            }

            mx /= size;
            my /= size;

            double cov = 0, var = 0;

            for (int j = from; j <= to; j++)
            {
                cov += (numbers[j] - mx) * (logs[j] - my);
                var += (numbers[j] - mx) * (numbers[j] - mx);
            }

            double smooth = my + (var > 0 ? cov / var : 0) * (numbers[i] - mx);
            gains[numbers[i]] = Math.Clamp((float)Math.Exp((smooth - logs[i]) * strength), 0.25f, 4f);
        }

        return gains;
    }

    /// <summary>Die mittlere Helligkeit eines Bildes, linear - jeder <paramref name="step"/>-te Punkt.</summary>
    public static float Level(FloatFrame frame, int step = 8)
    {
        double sum = 0;
        long count = 0;
        step = Math.Max(1, step);

        for (int y = 0; y < frame.Height; y += step)
        {
            for (int x = 0; x < frame.Width; x += step)
            {
                int i = y * frame.Width + x;
                sum += 0.2126 * frame.R[i] + 0.7152 * frame.G[i] + 0.0722 * frame.B[i];
                count++;
            }
        }

        return count > 0 ? (float)(sum / count) : 0f;
    }

    public DeflickerTool Clone() => new()
    {
        Levels = Levels is null ? null : new Dictionary<int, float>(Levels),
        Amount = Amount,
        Window = Window,
    };
}
