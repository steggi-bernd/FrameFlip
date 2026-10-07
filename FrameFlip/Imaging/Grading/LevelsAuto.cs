namespace FrameFlip.Imaging.Grading;

/// <summary>Was Auto am Tonwert einstellt - wie die drei Knoepfe in Photoshop.</summary>
public enum LevelsAutoKind
{
    /// <summary>Schwarz und Weiss je Kanal - streckt jeden Kanal und nimmt dabei einen Farbstich mit.</summary>
    Levels,

    /// <summary>Schwarz und Weiss gemeinsam - mehr Kontrast, die Farben bleiben, wie sie sind.</summary>
    Contrast,

    /// <summary>Je Kanal, dazu die Mitten neutral - ein Grau wird wieder grau.</summary>
    Colour,
}

/// <summary>Welcher Punkt mit der Pipette gesetzt wird.</summary>
public enum LevelsPickKind
{
    Black,
    Gray,
    White,
}

/// <summary>
/// Auto-Tonwert und die Pipetten (W2b): Sie setzen die Punkte des Tonwerts aus dem, was bei
/// ihm ANKOMMT - nicht aus dem fertigen Bild, in dem er schon wirkt. Wer zweimal Auto drueckt,
/// bekommt dasselbe.
///
/// Reine Rechnung auf einer Stichprobe (RGB hintereinander, Anzeigewerte 0 bis 1), damit sie
/// sich ohne Bild und Fenster pruefen laesst. Woher die Stichprobe kommt, entscheidet die Seite.
/// </summary>
public static class LevelsAuto
{
    /// <summary>Wieviel oben und unten abgeschnitten werden darf - ein Promille, wie ueblich.</summary>
    public const double Clip = 0.001;

    /// <summary>Stellt den Tonwert nach der Stichprobe ein.</summary>
    public static void Apply(LevelsTool tool, ReadOnlySpan<float> rgb, LevelsAutoKind kind)
    {
        if (rgb.Length < 3) return;

        switch (kind)
        {
            case LevelsAutoKind.Contrast:
            {
                // Gemeinsam: die Grenzen aller drei Kanaele zusammen, die Kanaele selbst neutral.
                var (low, high) = Bounds(rgb, channel: -1);
                Stretch(tool.Master, low, high);

                foreach (int c in new[] { 1, 2, 3 }) Neutral(tool.Channel(c));
                break;
            }

            case LevelsAutoKind.Levels:
            case LevelsAutoKind.Colour:
            {
                Neutral(tool.Master);

                for (int c = 0; c < 3; c++)
                {
                    var (low, high) = Bounds(rgb, c);
                    var channel = tool.Channel(c + 1);

                    Neutral(channel);
                    Stretch(channel, low, high);
                }

                if (kind == LevelsAutoKind.Colour) NeutralMidtones(tool, rgb);
                break;
            }
        }

        tool.Prepare();
    }

    /// <summary>
    /// Eine Pipette: Der Punkt eines Kanals wird der Wert, der dort ankommt. Schwarz und Weiss je
    /// Kanal, sodass der angeklickte Ton schwarz oder weiss wird. Grau stellt die Mitten jedes
    /// Kanals so, dass der Ton grau wird - das nimmt einen Farbstich. Die gemeinsame Einstellung
    /// bleibt; die Kanaele wirken nach ihr, also zaehlt der Wert nach ihr.
    /// </summary>
    public static void Pick(LevelsTool tool, LevelsPickKind kind, float r, float g, float b)
    {
        tool.Prepare();

        float[] values = { tool.Master.Map(r), tool.Master.Map(g), tool.Master.Map(b) };

        switch (kind)
        {
            case LevelsPickKind.Black:
                for (int c = 0; c < 3; c++)
                {
                    var channel = tool.Channel(c + 1);
                    channel.InBlack = Math.Clamp(values[c], 0f, channel.InWhite - LevelsChannel.MinimumSpan);
                }

                break;

            case LevelsPickKind.White:
                for (int c = 0; c < 3; c++)
                {
                    var channel = tool.Channel(c + 1);
                    channel.InWhite = Math.Clamp(values[c], channel.InBlack + LevelsChannel.MinimumSpan, 1f);
                }

                break;

            case LevelsPickKind.Gray:
            {
                // Nach Schwarz und Weiss jedes Kanals: wo steht der Ton? Das Mittel wird das Ziel.
                float[] stretched = new float[3];

                for (int c = 0; c < 3; c++)
                {
                    var channel = tool.Channel(c + 1);
                    stretched[c] = Math.Clamp((values[c] - channel.InBlack) / Math.Max(LevelsChannel.MinimumSpan, channel.InWhite - channel.InBlack), 0.001f, 0.999f);
                }

                float target = Math.Clamp((stretched[0] + stretched[1] + stretched[2]) / 3f, 0.02f, 0.98f);

                for (int c = 0; c < 3; c++)
                    tool.Channel(c + 1).Gamma = GammaFor(stretched[c], target);

                break;
            }
        }

        tool.Prepare();
    }

    /// <summary>
    /// Die Grenzen eines Kanals (0 bis 2) oder aller zusammen (-1): der Wert, unter dem ein
    /// Promille liegt, und der, ueber dem eines liegt.
    /// </summary>
    public static (float Low, float High) Bounds(ReadOnlySpan<float> rgb, int channel)
    {
        const int bins = 1024;
        var counts = new long[bins];
        long total = 0;

        for (int i = 0; i + 2 < rgb.Length; i += 3)
        {
            for (int c = 0; c < 3; c++)
            {
                if (channel >= 0 && c != channel) continue;

                float v = rgb[i + c];
                if (float.IsNaN(v)) continue;

                counts[(int)Math.Clamp(v * (bins - 1) + 0.5f, 0, bins - 1)]++;
                total++;
            }
        }

        if (total == 0) return (0, 1);

        long cut = (long)Math.Floor(total * Clip);
        long seen = 0;
        int low = 0, high = bins - 1;

        for (int i = 0; i < bins; i++)
        {
            seen += counts[i];
            if (seen > cut) { low = i; break; }
        }

        seen = 0;

        for (int i = bins - 1; i >= 0; i--)
        {
            seen += counts[i];
            if (seen > cut) { high = i; break; }
        }

        return (low / (float)(bins - 1), high / (float)(bins - 1));
    }

    private static void Stretch(LevelsChannel channel, float low, float high)
    {
        // Ein Bild aus einem einzigen Ton hat nichts zu strecken - dann bleibt es, wie es ist.
        if (high - low < 4 * LevelsChannel.MinimumSpan) return;

        channel.InBlack = low;
        channel.InWhite = high;
    }

    private static void Neutral(LevelsChannel channel)
    {
        channel.InBlack = 0;
        channel.InWhite = 1;
        channel.Gamma = 1;
        channel.OutBlack = 0;
        channel.OutWhite = 1;
    }

    /// <summary>Die Mitten jedes Kanals so, dass ihre Mittelwerte nach dem Strecken gleich liegen.</summary>
    private static void NeutralMidtones(LevelsTool tool, ReadOnlySpan<float> rgb)
    {
        tool.Prepare();

        double[] sums = new double[3];
        long count = 0;

        for (int i = 0; i + 2 < rgb.Length; i += 3)
        {
            for (int c = 0; c < 3; c++)
            {
                var channel = tool.Channel(c + 1);
                float span = Math.Max(LevelsChannel.MinimumSpan, channel.InWhite - channel.InBlack);
                sums[c] += Math.Clamp((rgb[i + c] - channel.InBlack) / span, 0f, 1f);
            }

            count++;
        }

        if (count == 0) return;

        float[] means = { (float)(sums[0] / count), (float)(sums[1] / count), (float)(sums[2] / count) };
        float target = Math.Clamp((means[0] + means[1] + means[2]) / 3f, 0.02f, 0.98f);

        for (int c = 0; c < 3; c++)
            tool.Channel(c + 1).Gamma = GammaFor(Math.Clamp(means[c], 0.001f, 0.999f), target);
    }

    /// <summary>Das Gamma, mit dem aus <paramref name="from"/> der Ton <paramref name="to"/> wird: from^(1/g) = to.</summary>
    private static float GammaFor(float from, float to)
        => Math.Clamp(MathF.Log(from) / MathF.Log(to), 0.1f, 10f);
}
