using System.Diagnostics;
using System.IO;

namespace FrameFlip.Export;

/// <summary>
/// Findet ffmpeg zur Laufzeit.
///
/// ffmpeg wird bewusst NICHT mitgeliefert: gaengige Builds enthalten libx264 und
/// stehen damit unter GPL. Waere es Teil des Programms, muesste FrameFlip ebenfalls
/// unter GPL stehen. Zur Laufzeit gesucht bleibt die Lizenzfrage beim Benutzer und
/// FrameFlip permissiv lizenzierbar.
/// </summary>
public static class FfmpegLocator
{
    public const string ExecutableName = "ffmpeg.exe";

    /// <summary>
    /// Suchreihenfolge: eingestellter Pfad, Unterordner neben der Exe, PATH, dann die
    /// Ablageorte der ueblichen Paketverwaltungen.
    ///
    /// Der letzte Schritt ist kein Luxus: nach einer frischen Installation ueber
    /// winget oder scoop kennt eine bereits laufende Anwendung den erweiterten PATH
    /// noch nicht - sie hat ihn beim Start geerbt. Ohne diese Faelle waere die
    /// Antwort "nicht gefunden", obwohl ffmpeg gerade installiert wurde.
    /// </summary>
    public static string? Locate(string? configured = null)
    {
        if (IsUsable(configured)) return configured;

        foreach (var candidate in Candidates())
            if (IsUsable(candidate)) return candidate;

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        // Neben der eigenen Exe - fuer den Fall, dass jemand ffmpeg selbst dazulegt.
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg", ExecutableName);
        yield return Path.Combine(AppContext.BaseDirectory, ExecutableName);

        foreach (var directory in PathDirectories())
        {
            string candidate;
            try { candidate = Path.Combine(directory, ExecutableName); }
            catch (ArgumentException) { continue; }   // ungueltiger PATH-Eintrag
            yield return candidate;
        }

        foreach (var candidate in PackageManagerLocations())
            yield return candidate;
    }

    private static IEnumerable<string> PathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim().Trim('"');
            if (trimmed.Length > 0) yield return trimmed;
        }
    }

    private static IEnumerable<string> PackageManagerLocations()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", ExecutableName);
        yield return Path.Combine(programData, "chocolatey", "bin", ExecutableName);
        yield return Path.Combine(profile, "scoop", "shims", ExecutableName);
        yield return Path.Combine(programFiles, "ffmpeg", "bin", ExecutableName);
        yield return Path.Combine("C:\\", "ffmpeg", "bin", ExecutableName);

        // winget legt die eigentliche Exe unter Packages ab und verlinkt sie nur.
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        var fromPackages = FindUnder(packages, depth: 3);
        foreach (var candidate in fromPackages) yield return candidate;
    }

    /// <summary>Flache, tiefenbegrenzte Suche - ein voller Rekursionslauf waere zu teuer.</summary>
    private static IEnumerable<string> FindUnder(string root, int depth)
    {
        if (depth <= 0 || !Directory.Exists(root)) yield break;

        string[] entries;
        try { entries = Directory.GetDirectories(root); }
        catch (Exception) { yield break; }

        foreach (var directory in entries)
        {
            var candidate = Path.Combine(directory, ExecutableName);
            if (File.Exists(candidate)) yield return candidate;

            var nestedBin = Path.Combine(directory, "bin", ExecutableName);
            if (File.Exists(nestedBin)) yield return nestedBin;

            foreach (var deeper in FindUnder(directory, depth - 1)) yield return deeper;
        }
    }

    private static bool IsUsable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try { return File.Exists(path); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Liest die Versionszeile. Damit laesst sich im Dialog belegen, dass der
    /// gewaehlte Pfad wirklich ein lauffaehiges ffmpeg ist - eine gleichnamige Datei
    /// allein sagt darueber nichts.
    /// </summary>
    public static string? TryReadVersion(string executable, int timeoutMs = 4000)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var first = process.StandardOutput.ReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return null;
            }

            return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Hinweis fuer den Dialog, wenn nichts gefunden wurde.</summary>
    public static string InstallHint =>
        "FrameFlip liefert ffmpeg nicht mit, weil uebliche Builds unter der GPL stehen.\n\n" +
        "Installation per winget:\n" +
        "    winget install Gyan.FFmpeg\n\n" +
        "Danach FrameFlip neu starten oder den Pfad hier von Hand auswählen.";
}
