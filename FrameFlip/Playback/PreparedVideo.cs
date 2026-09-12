using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrameFlip.Playback;

/// <summary>
/// Woran sich erkennen laesst, ob eine vorbereitete Datei noch zu dem passt, was
/// exportiert werden soll.
///
/// Alles, was das Ergebnis veraendert, steht hier drin - Bereich, Bildrate, Format,
/// Zielbreite, Umgang mit Luecken - und dazu der Zeitstempel des juengsten Bildes.
/// Rendert jemand einen Frame nach, ist die Vorbereitung hinfaellig, und das faellt
/// nur an dieser Stelle auf.
/// </summary>
public sealed record VideoFingerprint(
    string Pattern,
    int First,
    int Last,
    int Count,
    double Fps,
    int SourceWidth,
    int SourceHeight,
    int TargetWidth,
    string Preset,
    string Gaps,
    long NewestTicks)
{
    /// <summary>Kurz, stabil und als Dateiname brauchbar.</summary>
    public string Key
    {
        get
        {
            string text = string.Join('|',
                Pattern, First, Last, Count,
                Fps.ToString("0.###", CultureInfo.InvariantCulture),
                SourceWidth, SourceHeight, TargetWidth, Preset, Gaps, NewestTicks);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }
    }
}

/// <summary>
/// Fertig kodierte Faelle, die noch niemand angefordert hat.
///
/// Der Gedanke dahinter: Beim Vorausladen werden ohnehin alle Bilder gelesen. Wer
/// danach exportiert, laesst dieselben Bilder ein zweites Mal durch ffmpeg laufen -
/// und wartet dabei. Entsteht das Video schon waehrend des Zusehens, ist der Export
/// nur noch ein Kopiervorgang.
///
/// GESPEICHERT WIRD NICHTS, WAS NIEMAND WOLLTE. Die Datei liegt im Temp-Ordner und
/// kommt nur dann an ihren Platz, wenn wirklich exportiert wird. Wer nie exportiert,
/// hat eine Datei im Temp-Ordner, die beim naechsten Start aufgeraeumt wird - und
/// nichts in seinen eigenen Ordnern.
/// </summary>
public static class PreparedVideo
{
    /// <summary>Nach dieser Zeit wird eine unbenutzte Vorbereitung weggeraeumt.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(12);

    public static string Folder => Path.Combine(Path.GetTempPath(), "FrameFlip", "prepared");

    /// <summary>Wohin eine Vorbereitung geschrieben wird.</summary>
    public static string PathFor(VideoFingerprint print, string extension)
        => Path.Combine(Folder, print.Key + extension);

    /// <summary>
    /// Eine fertige Datei zu diesem Fall suchen.
    ///
    /// Geprueft wird nicht nur, ob die Datei da ist, sondern auch, ob sie zu dem
    /// Zettel daneben passt: Ein Namensschluessel allein koennte durch einen
    /// Zusammenstoss zweier Faelle stimmen, ohne dass der Inhalt stimmt.
    /// </summary>
    public static bool TryFind(VideoFingerprint print, string extension, out string path)
    {
        path = PathFor(print, extension);

        try
        {
            string note = path + ".json";

            if (!File.Exists(path) || !File.Exists(note)) return false;
            if (new FileInfo(path).Length == 0) return false;

            var stored = JsonSerializer.Deserialize<VideoFingerprint>(File.ReadAllText(note));

            if (stored != print) return false;

            // Angefasst heisst: noch gebraucht. So faellt sie beim Aufraeumen nicht
            // weg, solange jemand mit derselben Sequenz arbeitet.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Den Zettel neben die fertige Datei legen.</summary>
    public static void Note(VideoFingerprint print, string path)
    {
        try
        {
            File.WriteAllText(path + ".json", JsonSerializer.Serialize(print));
        }
        catch (Exception)
        {
            // Ohne Zettel gilt die Datei als nicht vorhanden. Mehr passiert nicht.
        }
    }

    public static void Prepare()
    {
        try { Directory.CreateDirectory(Folder); }
        catch (Exception) { }
    }

    /// <summary>
    /// Was laenger liegt, als es sich lohnt, verschwindet.
    ///
    /// Ein vorbereitetes Video kann hunderte Megabyte gross sein. Es unbegrenzt zu
    /// behalten hiesse, dem Benutzer die Platte vollzuschreiben fuer einen Export,
    /// den er offensichtlich nicht wollte.
    /// </summary>
    public static void CleanOld()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;

            var cutoff = DateTime.UtcNow - KeepFor;

            foreach (string file in Directory.EnumerateFiles(Folder))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;

                    File.Delete(file);
                }
                catch (Exception)
                {
                    // Eine gesperrte Datei bleibt eben noch eine Runde liegen.
                }
            }
        }
        catch (Exception)
        {
            // Aufraeumen ist Kuer.
        }
    }

    /// <summary>
    /// Der Zeitstempel des juengsten Bildes im Bereich.
    ///
    /// Geht auf die Platte und gehoert deshalb nicht in den Oberflaechen-Thread.
    /// </summary>
    public static long NewestTicks(IEnumerable<string> paths)
    {
        long newest = 0;

        foreach (string path in paths)
        {
            try
            {
                long ticks = File.GetLastWriteTimeUtc(path).Ticks;

                if (ticks > newest) newest = ticks;
            }
            catch (Exception)
            {
                // Eine Datei, die verschwunden ist, zaehlt nicht mit.
            }
        }

        return newest;
    }
}
