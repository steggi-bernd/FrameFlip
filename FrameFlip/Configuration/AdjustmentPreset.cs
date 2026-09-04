using FrameFlip.Imaging;

namespace FrameFlip.Configuration;

/// <summary>
/// Eine benannte Korrektureinstellung. Wird mit der Konfiguration gespeichert und
/// laesst sich im Panel direkt auswaehlen - etwa "Nachtaufnahme aufhellen" oder
/// "Kontrastpruefung".
/// </summary>
public sealed class AdjustmentPreset
{
    public string Name { get; set; } = string.Empty;

    public ImageAdjustments Adjustments { get; set; } = ImageAdjustments.Neutral;

    /// <summary>Wird in der Auswahlliste angezeigt.</summary>
    public override string ToString() => Name;
}
