using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FrameFlip.Rendering;

/// <summary>Ein gefundenes Blender.</summary>
/// <param name="Path">Die ausfuehrbare Datei.</param>
/// <param name="Version">Die Fassung, soweit sie sich ablesen liess. Null heisst: unbekannt.</param>
/// <param name="Source">Woher es kommt - fuer die Anzeige: "Steam", "Installiert", "Pfad".</param>
public sealed record BlenderInstall(string Path, Version? Version, string Source)
{
    public bool IsSteam => Source == BlenderFinder.SteamSource;

    /// <summary>Was in der Auswahlliste steht.</summary>
    public string Label => Version is null ? $"Blender ({Source})" : $"Blender {Version} ({Source})";
}

/// <summary>
/// Die Blender-Installationen dieses Rechners finden.
///
/// Gesucht wird dort, wo Blender wirklich landet: im Installationsordner, in Steams
/// Bibliotheken, in der Registrierung bei dem Programm, das .blend-Dateien oeffnet,
/// und im Suchpfad. Nicht gesucht wird auf der ganzen Platte - eine Suche, die
/// Minuten dauert und dabei jede Netzfreigabe anfasst, will niemand.
///
/// Steam ist ein eigener Fall, weil es sein Blender nicht dort ablegt, wo alle
/// anderen es tun: Es kann in jeder eingerichteten Bibliothek liegen, auch auf einer
/// anderen Platte. Wo die liegen, steht in libraryfolders.vdf, und das ist eine
/// Textdatei - man muss sie nur lesen.
/// </summary>
public static class BlenderFinder
{
    public const string SteamSource = "Steam";
    public const string InstalledSource = "Installiert";
    public const string RegisteredSource = "Verknüpft";
    public const string PathSource = "Suchpfad";
    public const string CustomSource = "Selbst eingetragen";

    /// <summary>Blenders Kennung im Steam-Katalog. Steht im Ordnernamen nicht, aber im Katalog.</summary>
    public const int SteamAppId = 365670;

    /// <summary>
    /// Alles, was sich finden laesst - neueste Fassung zuerst.
    ///
    /// Doppelte fallen heraus: Dasselbe Blender steht leicht in der Registrierung
    /// UND im Suchpfad, und zweimal dieselbe Zeile in einer Auswahlliste ist nur
    /// verwirrend.
    /// </summary>
    /// <param name="extra">
    /// Von Hand eingetragene Pfade.
    ///
    /// Ohne die waere die Liste eine Sackgasse: Ein entpacktes Blender auf einer
    /// Datenplatte, ein selbst gebautes, eine Fassung aus dem Archiv - nichts davon
    /// steht an einer Stelle, an der ein Programm es finden koennte. Sie kommen
    /// ZULETZT dazu, damit ein Pfad, der ohnehin gefunden wird, seine eigentliche
    /// Herkunft behaelt und nicht als "selbst eingetragen" dasteht.
    /// </param>
    public static List<BlenderInstall> Find(IEnumerable<string>? extra = null)
    {
        var found = new List<BlenderInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string full;

            try { full = System.IO.Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception) { return; }

            if (!seen.Add(full)) return;
            if (!File.Exists(full)) return;

            found.Add(new BlenderInstall(full, VersionOf(full), source));
        }

        foreach (string path in FromSteam()) Add(path, SteamSource);
        foreach (string path in FromProgramFiles()) Add(path, InstalledSource);

        Add(FromRegistry(), RegisteredSource);

        foreach (string path in FromSearchPath()) Add(path, PathSource);
        foreach (string path in extra ?? Enumerable.Empty<string>()) Add(path, CustomSource);

        return Sorted(found);
    }

    /// <summary>
    /// Sortieren: neueste Fassung oben, Unbekanntes unten.
    ///
    /// Nach Zahlen und nicht nach Text, denn "4.10" kommt nach "4.9" und nicht davor.
    /// Ein Vergleich als Zeichenkette dreht genau das um, und zwar erst in dem Jahr,
    /// in dem die zehnte Unterfassung erscheint - lange nachdem jemand ihn
    /// geschrieben hat.
    /// </summary>
    public static List<BlenderInstall> Sorted(IEnumerable<BlenderInstall> installs)
        => installs.OrderByDescending(install => install.Version is not null)
                   .ThenByDescending(install => install.Version ?? new Version(0, 0))
                   .ThenBy(install => install.Path, StringComparer.OrdinalIgnoreCase)
                   .ToList();

    /// <summary>
    /// Welches vorgeschlagen wird.
    ///
    /// Steam gewinnt, wenn es da ist - wer Blender ueber Steam bezieht, benutzt in
    /// aller Regel auch das, und es aktualisiert sich von selbst. Sonst die neueste
    /// Fassung.
    /// </summary>
    public static BlenderInstall? Preferred(IEnumerable<BlenderInstall> installs)
    {
        var list = Sorted(installs);

        return list.FirstOrDefault(install => install.IsSteam) ?? list.FirstOrDefault();
    }

    // ---------------------------------------------------------------- Quellen

    /// <summary>Steams Bibliotheken durchsehen - auch die auf anderen Platten.</summary>
    private static IEnumerable<string> FromSteam()
    {
        foreach (string library in SteamLibraries())
        {
            string candidate = System.IO.Path.Combine(library, "steamapps", "common", "Blender", "blender.exe");

            if (File.Exists(candidate)) yield return candidate;
        }
    }

    /// <summary>Wo Steam seine Bibliotheken hat. Die erste ist Steam selbst.</summary>
    private static List<string> SteamLibraries()
    {
        var libraries = new List<string>();

        string? steam = SteamRoot();

        if (steam is null) return libraries;

        libraries.Add(steam);

        try
        {
            string vdf = System.IO.Path.Combine(steam, "steamapps", "libraryfolders.vdf");

            if (File.Exists(vdf)) libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        }
        catch (Exception)
        {
            // Ohne die Datei bleibt es bei Steams eigenem Ordner. Das ist der
            // haeufigste Fall und deckt die meisten Rechner ab.
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Die Bibliothekspfade aus libraryfolders.vdf.
    ///
    /// Steams eigenes Format, aber der gesuchte Teil ist schlicht: Zeilen mit "path"
    /// und einem Pfad dahinter. Rueckwaertsschraegstriche stehen darin doppelt.
    /// </summary>
    public static List<string> ParseLibraryFolders(string? text)
    {
        var paths = new List<string>();

        if (string.IsNullOrWhiteSpace(text)) return paths;

        foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"(?<path>[^\"]+)\""))
            paths.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));

        return paths;
    }

    private static string? SteamRoot()
    {
        foreach (var (hive, key, name) in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var branch = root.OpenSubKey(key);

                if (branch?.GetValue(name) is string path && path.Length > 0) return path.Replace('/', '\\');
            }
            catch (Exception)
            {
                // Keine Registrierung, keine Rechte, kein Steam - alles dasselbe.
            }
        }

        return null;
    }

    /// <summary>Die gewoehnlichen Installationen, eine je Fassung.</summary>
    private static IEnumerable<string> FromProgramFiles()
    {
        foreach (string root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            string foundation = System.IO.Path.Combine(root, "Blender Foundation");

            if (!Directory.Exists(foundation)) continue;

            List<string> folders;

            try { folders = Directory.EnumerateDirectories(foundation).ToList(); }
            catch (Exception) { continue; }

            foreach (string folder in folders)
            {
                string candidate = System.IO.Path.Combine(folder, "blender.exe");

                if (File.Exists(candidate)) yield return candidate;
            }
        }
    }

    /// <summary>Womit Windows .blend-Dateien oeffnet - also das, was hier wirklich benutzt wird.</summary>
    private static string? FromRegistry()
    {
        try
        {
            using var command = Registry.ClassesRoot.OpenSubKey(@"blendfile\shell\open\command");

            if (command?.GetValue(null) is not string line || line.Length == 0) return null;

            // Der Eintrag sieht aus wie: "C:\...\blender.exe" "%1"
            var match = Regex.Match(line, "\"(?<path>[^\"]+blender\\.exe)\"", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups["path"].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> FromSearchPath()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");

        if (path is null) yield break;

        foreach (string folder in path.Split(System.IO.Path.PathSeparator))
        {
            if (folder.Length == 0) continue;

            string candidate;

            try { candidate = System.IO.Path.Combine(folder.Trim(), "blender.exe"); }
            catch (Exception) { continue; }

            if (File.Exists(candidate)) yield return candidate;
        }
    }

    // ---------------------------------------------------------------- Fassung

    /// <summary>
    /// Die Fassung aus dem Pfad lesen.
    ///
    /// Der Ordnername traegt sie fast immer - "Blender 4.2", "blender-4.5.1-windows-x64".
    /// Blender danach zu fragen kostet je Fund eine Sekunde und einen Prozessstart;
    /// das lohnt sich erst, wenn der Pfad wirklich nichts hergibt.
    /// </summary>
    public static Version? VersionOf(string path)
    {
        // Von hinten nach vorn: Der letzte Ordner ist der aussagekraeftigste, und in
        // "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe" steht die
        // Zahl genau dort.
        foreach (string part in path.Split('\\', '/').Reverse())
        {
            if (ParseVersion(part) is Version found) return found;
        }

        // Steam legt sein Blender in einen Ordner ohne Fassung. Dann traegt die
        // Datei selbst sie noch - Windows liest das aus dem Dateikopf, ohne dass
        // dafuer ein Prozess starten muesste.
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

            return ParseVersion(info.ProductVersion) ?? ParseVersion(info.FileVersion);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Die erste Fassungsnummer in einem Text, oder null.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Regex.Match(text, @"(?<!\d)(?<major>\d{1,2})\.(?<minor>\d{1,2})(?:\.(?<patch>\d{1,2}))?(?!\d)");

        if (!match.Success) return null;

        int major = int.Parse(match.Groups["major"].Value);
        int minor = int.Parse(match.Groups["minor"].Value);

        // Blender faengt bei 2.x an. Was darunter liegt, ist eine Zahl aus einem
        // Ordnernamen und keine Fassung.
        if (major < 2 || major > 20) return null;

        return match.Groups["patch"].Success
            ? new Version(major, minor, int.Parse(match.Groups["patch"].Value))
            : new Version(major, minor);
    }
}
