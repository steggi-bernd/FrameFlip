using System.Diagnostics;
using System.IO;
using System.Text;

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
    private const int MaxVersionOutputChars = 64 * 1024;

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

        try
        {
            // Der Dialog sucht bewusst nach ffmpeg.exe. Skripte und Verknuepfungen
            // duerfen dort nicht als "Encoder" laufen: Sie wuerden ueber cmd.exe
            // gestartet und koennten einen Prozessbaum hinterlassen, den das
            // Programm nicht zuverlaessig kontrollieren kann.
            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
            return File.Exists(path);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Liest die Versionszeile. Damit laesst sich im Dialog belegen, dass der
    /// gewaehlte Pfad wirklich ein lauffaehiges ffmpeg ist - eine gleichnamige Datei
    /// allein sagt darueber nichts.
    /// </summary>
    public static string? TryReadVersion(string executable, int timeoutMs = 4000)
        => TryReadVersionAsync(executable, timeoutMs).GetAwaiter().GetResult();

    /// <summary>
    /// Liest die Versionszeile, ohne einen Aufrufer am UI-Thread festzuhalten.
    /// Beide Ausgabekanaele werden gleichzeitig geleert: Auch ein fremdes Programm,
    /// das nur stderr fuellt oder nie eine Zeile auf stdout schreibt, kann die
    /// Pruefung damit weder blockieren noch unbegrenzt Speicher belegen.
    /// </summary>
    public static async Task<string?> TryReadVersionAsync(string executable, int timeoutMs = 4000,
                                                           CancellationToken cancellation = default)
    {
        if (!IsUsable(executable) || timeoutMs <= 0) return null;

        Process? process = null;
        Task<string>? output = null;
        Task<string>? errors = null;

        try
        {
            var info = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-version");

            process = Process.Start(info);

            if (process is null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            // Der Timeout gilt dem Prozess. Die Reader laufen bis zum Pipe-Ende,
            // damit sie nach dem Kill noch sauber fertig werden koennen.
            output = DrainBoundedAsync(process.StandardOutput);
            errors = DrainBoundedAsync(process.StandardError);
            Task exited = process.WaitForExitAsync(timeout.Token);

            await Task.WhenAll(output, errors, exited).ConfigureAwait(false);

            return FirstLine(await output.ConfigureAwait(false));
        }
        catch (Exception)
        {
            Stop(process);
            await FinishDraining(process, output, errors).ConfigureAwait(false);
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<string> DrainBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var kept = new StringBuilder();

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0) return kept.ToString();

            // Weiter lesen, auch wenn die nutzbare Ausgabe voll ist. Sonst kann der
            // Kindprozess auf einer vollen Pipe haengen. Nur behalten wird, was fuer
            // die erste Versionszeile wirklich reichen kann.
            int left = MaxVersionOutputChars - kept.Length;
            if (left > 0) kept.Append(buffer, 0, Math.Min(left, read));
        }
    }

    private static string? FirstLine(string text)
    {
        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(line)) return line.Trim();

        return null;
    }

    private static void Stop(Process? process)
    {
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Der Prozess kann zwischen Timeout und Kill selbst enden.
        }
    }

    private static async Task FinishDraining(Process? process, params Task?[] tasks)
    {
        var pending = tasks.Where(task => task is not null).Cast<Task>().ToList();

        if (process is not null)
        {
            try { pending.Add(process.WaitForExitAsync()); }
            catch (Exception) { }
        }

        if (pending.Count == 0) return;

        // Ein abgebrochener Reader ist erwartbar. Sein Cancellation-Status darf
        // aber nicht das Warten auf den tatsaechlich beendeten Prozess abkuerzen.
        var settled = pending.Select(IgnoreFailure).ToArray();

        try { await Task.WhenAll(settled).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private static async Task IgnoreFailure(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { }
    }

    /// <summary>Hinweis fuer den Dialog, wenn nichts gefunden wurde.</summary>
    public static string InstallHint =>
        "FrameFlip liefert ffmpeg nicht mit, weil uebliche Builds unter der GPL stehen.\n\n" +
        "Installation per winget:\n" +
        "    winget install Gyan.FFmpeg\n\n" +
        "Danach FrameFlip neu starten oder den Pfad hier von Hand auswählen.";
}
