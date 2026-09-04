using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FrameFlip.Configuration;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Sonst wird das Pluszeichen im Hotkey als + geschrieben - die Datei
        // soll von Hand lesbar und bearbeitbar bleiben.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Alternativer Konfigurationspfad ueber die Umgebungsvariable FRAMEFLIP_CONFIG.
    /// Gedacht fuer automatisierte Tests, damit die Einstellungen des Benutzers
    /// unangetastet bleiben.
    /// </summary>
    public static string? Override => Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

    public static string DirectoryPath =>
        Override is { Length: > 0 } path
            ? Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameFlip");

    public static string FilePath =>
        Override is { Length: > 0 } path ? path : Path.Combine(DirectoryPath, "config.json");

    /// <summary>
    /// Diagnoseausgabe neben die Testkonfiguration. Nur aktiv, wenn FRAMEFLIP_CONFIG
    /// gesetzt ist - im normalen Betrieb schreibt FrameFlip keine Protokolle.
    /// </summary>
    public static void Trace(string message)
    {
        if (Override is not { Length: > 0 } path) return;

        try { File.AppendAllText(path + ".log", DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine); }
        catch (Exception) { }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Defekte oder unlesbare Konfiguration darf den Start nicht verhindern.
        }

        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    /// <summary>Schreibt ueber eine temporaere Datei, damit ein Absturz die Konfiguration nicht zerreisst.</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Nicht schreibbares APPDATA ist kein Grund, die laufende Sitzung abzubrechen.
        }
    }
}
