using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameFlip.Configuration;

/// <summary>Eine Sequenz, die FrameFlip schon einmal geoeffnet hat.</summary>
public sealed class RecentSequence
{
    /// <summary>Der Ordner. Zugleich die Identitaet - je Ordner ein Eintrag.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Die Datei, mit der zuletzt geoeffnet wurde. Zum Wiederoeffnen.</summary>
    public string Seed { get; set; } = string.Empty;

    public int First { get; set; }
    public int Last { get; set; }
    public int Count { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Endung ohne Punkt, gross - PNG, JPG, EXR.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Der Name, der in der Liste steht.</summary>
    [JsonIgnore]
    public string Name => string.IsNullOrEmpty(Folder) ? "?" : Path.GetFileName(Folder.TrimEnd('\\', '/'));

    /// <summary>Ob die Nummern lueckenlos sind. Fehlende Frames sind der haeufigste Mangel.</summary>
    [JsonIgnore]
    public int Missing => Math.Max(0, (Last - First + 1) - Count);

    /// <summary>Ob der Ordner noch da ist. Wird beim Anzeigen geprueft, nicht gespeichert.</summary>
    [JsonIgnore]
    public bool Exists => Folder.Length > 0 && Directory.Exists(Folder);
}

/// <summary>
/// Was FrameFlip schon einmal geoeffnet hat.
///
/// Eine eigene Datei neben der Konfiguration, nicht darin: Die Liste waechst und
/// aendert sich bei jedem Oeffnen, die Einstellungen aendern sich selten. Zusammen
/// hiesse, bei jedem Oeffnen einer Vorschau die gesamten Einstellungen neu zu
/// schreiben - und ein Absturz mitten darin naehme beides mit.
///
/// Alle Fehler enden still. Eine Liste zuletzt geoeffneter Ordner ist Bequemlichkeit;
/// nichts daran darf einen Start verhindern.
/// </summary>
public static class RecentSequences
{
    /// <summary>Mehr braucht niemand, und die Datei bleibt klein genug zum Lesen.</summary>
    public const int Limit = 40;

    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(SettingsStore.DirectoryPath, "sequences.json");

    /// <summary>Neueste zuerst. Nie null, im Zweifel leer.</summary>
    public static List<RecentSequence> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<RecentSequence>();

                var list = JsonSerializer.Deserialize<List<RecentSequence>>(File.ReadAllText(FilePath));

                return list?.Where(entry => entry.Folder.Length > 0)
                            .OrderByDescending(entry => entry.OpenedUtc)
                            .ToList()
                       ?? new List<RecentSequence>();
            }
            catch (Exception)
            {
                return new List<RecentSequence>();
            }
        }
    }

    /// <summary>
    /// Einen Eintrag aufnehmen oder auffrischen.
    ///
    /// Je Ordner genau einer: Wer dieselbe Sequenz zehnmal oeffnet, will sie nicht
    /// zehnmal in der Liste finden. Der neue Stand ersetzt den alten vollstaendig -
    /// die Zahl der Frames kann sich geaendert haben, waehrend gerendert wurde.
    /// </summary>
    public static void Remember(RecentSequence entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Folder)) return;

        lock (Gate)
        {
            try
            {
                var list = Load();

                list.RemoveAll(existing =>
                    string.Equals(existing.Folder, entry.Folder, StringComparison.OrdinalIgnoreCase));

                entry.OpenedUtc = DateTime.UtcNow;
                list.Insert(0, entry);

                if (list.Count > Limit) list.RemoveRange(Limit, list.Count - Limit);

                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Format));
            }
            catch (Exception)
            {
                // Siehe oben: Bequemlichkeit, kein Grund fuer irgendetwas anderes.
            }
        }
    }

    /// <summary>Einen Eintrag entfernen - etwa, weil der Ordner nicht mehr da ist.</summary>
    public static void Forget(string folder)
    {
        lock (Gate)
        {
            try
            {
                var list = Load();

                if (list.RemoveAll(e => string.Equals(e.Folder, folder, StringComparison.OrdinalIgnoreCase)) == 0)
                    return;

                File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Format));
            }
            catch (Exception)
            {
            }
        }
    }
}
