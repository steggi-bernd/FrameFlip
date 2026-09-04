using FrameFlip.Sequencing;

namespace FrameFlip.Export;

/// <summary>Was exportiert werden soll. Reine Beschreibung, ohne Prozessbezug.</summary>
public sealed record ExportRequest
{
    public required IReadOnlyList<SequenceFrame> Frames { get; init; }
    public required ExportPreset Preset { get; init; }
    public required string OutputPath { get; init; }
    public required double Fps { get; init; }

    public GapHandling Gaps { get; init; } = GapHandling.HoldLast;

    /// <summary>Zielbreite in Pixeln, 0 fuer die Originalgroesse. Die Hoehe folgt.</summary>
    public int TargetWidth { get; init; }

    /// <summary>Native Bildgroesse, fuer die Abschaetzung der Ausgabemasse.</summary>
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }

    /// <summary>Threadzahl fuer den Encoder, 0 fuer ffmpegs eigene Wahl.</summary>
    public int Threads { get; init; }

    /// <summary>
    /// Anzeigekorrektur, die in das Video eingerechnet werden soll. null laesst das
    /// Material unveraendert - der Regelfall, denn die Korrektur ist zunaechst nur
    /// ein Beurteilungswerkzeug.
    /// </summary>
    public Imaging.ImageAdjustments? Adjustments { get; init; }

    public int FrameCount => Frames.Count;

    /// <summary>
    /// Anzahl der Frames im fertigen Video. Mit "letzten Frame halten" kommen die
    /// fehlenden Nummern als Standbilder dazu, sonst nicht.
    /// </summary>
    public int OutputFrameCount
    {
        get
        {
            if (Gaps != GapHandling.HoldLast || Frames.Count == 0) return Frames.Count;
            return Frames[^1].Number - Frames[0].Number + 1;
        }
    }

    public TimeSpan Duration => Fps > 0
        ? TimeSpan.FromSeconds(OutputFrameCount / Fps)
        : TimeSpan.Zero;
}
