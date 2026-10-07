using System.IO;
using System.Text.Json;
using FrameFlip.Configuration;
namespace FrameFlip.Views;
/// <summary>Ansichtspräferenzen unabhängig von Render- und Remote-Einstellungen.</summary>
public sealed class DesktopLayout
{
    public double Scale { get; set; } = 1;

    /// <summary>
    /// Die Bedienskalierung folgt dem Bildschirm statt einem festen Wert - siehe
    /// <see cref="AutoFor"/>. Strg+Plus/Minus schaltet auf einen festen Wert zurueck,
    /// ausgehend vom automatischen.
    /// </summary>
    public bool AutoScale { get; set; }

    /// <summary>
    /// Die Skalierung fuer einen Bildschirm dieser wirksamen Hoehe: 1080 Punkte ergeben
    /// 100 %, 1440 etwa 115 %, 2160 (4K mit 100 %) 135 %. Die Wurzel, weil ein doppelt so
    /// hoher Schirm nicht doppelt so grosse Schrift braucht - er steht meist weiter weg
    /// und ist groesser, aber nicht doppelt. In Zwanzigsteln und in den Grenzen des Reglers.
    /// </summary>
    public static double AutoFor(double effectiveHeight)
    {
        if (!double.IsFinite(effectiveHeight) || effectiveHeight <= 0) return 1;

        return Math.Clamp(Math.Round(Math.Sqrt(effectiveHeight / 1080.0) * 20) / 20, .85, 1.35);
    }
    public double NavigationWidth { get; set; } = 204;
    public double MonitorShare { get; set; } = .40;
    public double TileSize { get; set; } = 174;
    public double LowerPanelHeight { get; set; } = 240;
    public bool MonitorFirst { get; set; }
    public bool ReduceMotion { get; set; }
    public bool LightQr { get; set; } = true;
    public event Action? Changed;
    public static string FilePath => Path.Combine(SettingsStore.DirectoryPath, "desktop-layout.json");
    public static DesktopLayout Load()
    {
        try { var value = JsonSerializer.Deserialize<DesktopLayout>(File.ReadAllText(FilePath)); if (value is not null) { value.Normalize(); return value; } }
        catch (Exception) { }
        return new DesktopLayout();
    }
    public void Normalize()
    {
        Scale = Limit(Scale, .85, 1.35, 1);
        NavigationWidth = Limit(NavigationWidth, 150, 420, 204);
        MonitorShare = Limit(MonitorShare, .05, .95, .40);
        TileSize = Limit(TileSize, 136, 240, 174);
        LowerPanelHeight = Limit(LowerPanelHeight, 180, 520, 240);
    }
    private static double Limit(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public void Save()
    {
        Normalize();
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception) { }
        Changed?.Invoke();
    }
    public void Reset()
    {
        Scale = 1; AutoScale = false; NavigationWidth = 204; MonitorShare = .40; TileSize = 174;
        LowerPanelHeight = 240;
        MonitorFirst = false; ReduceMotion = false; LightQr = true; Save();
    }
}
