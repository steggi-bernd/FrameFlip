using System.Globalization;

namespace FrameFlip.Playback;

public sealed record FpsOption(double Value, string Label)
{
    public static readonly IReadOnlyList<FpsOption> All = new[]
    {
        new FpsOption(12d, "12"),
        new FpsOption(15d, "15"),
        new FpsOption(24000d / 1001d, "23.976"),
        new FpsOption(24d, "24"),
        new FpsOption(25d, "25"),
        new FpsOption(30d, "30"),
        new FpsOption(50d, "50"),
        new FpsOption(60d, "60")
    };

    /// <summary>Naechstgelegener Eintrag zu einem persistierten Wert.</summary>
    public static FpsOption Closest(double fps)
    {
        FpsOption best = All[3];
        double bestDelta = double.MaxValue;

        foreach (var option in All)
        {
            double delta = Math.Abs(option.Value - fps);
            if (delta < bestDelta) { bestDelta = delta; best = option; }
        }

        return best;
    }

    public override string ToString() => Label;

    public string Persisted => Value.ToString("0.####", CultureInfo.InvariantCulture);
}
