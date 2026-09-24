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

    /// <summary>
    /// Der Ressourcenschluessel fuer den Hinweis, wenn nichts gefunden wurde.
    ///
    /// Ein Schluessel und nicht der Text. Frueher stand hier eine fest eingebaute
    /// deutsche Zeichenkette - auf Englisch gestellt erschien sie trotzdem deutsch.
    /// Der Text selbst aufzuloesen waere aber genauso falsch: Diese Klasse ist
    /// absichtlich frei von Oberflaeche, damit sich jede Zeile ohne Fenster pruefen
    /// laesst, und <see cref="Localization.Strings.T"/> gibt ohne laufende
    /// WPF-Anwendung den Schluessel statt des Satzes zurueck. Wer den Satz braucht,
    /// hat ein Fenster - und loest ihn dort auf.
    /// </summary>
    public static string InstallHintKey => WingetAvailable ? "S_FfmpegHintWinget" : "S_FfmpegHintManual";

    // ------------------------------------------------------------- Holen lassen

    /// <summary>Die Kennung im Paketverzeichnis von Windows.</summary>
    public const string WingetPackage = "Gyan.FFmpeg";

    /// <summary>
    /// Steht die Paketverwaltung von Windows zur Verfuegung?
    ///
    /// Einmal ermittelt und behalten: Der Weg aendert sich waehrend einer Sitzung
    /// nicht, und die Bereitschaftsanzeige fragt bei jedem Zeichnen nach.
    /// </summary>
    public static bool WingetAvailable => _winget ??= PathDirectories()
        .Any(dir =>
        {
            try { return File.Exists(Path.Combine(dir, "winget.exe")); }
            catch (ArgumentException) { return false; }   // ungueltiger PATH-Eintrag
        });
    private static bool? _winget;

    /// <summary>
    /// ffmpeg von Windows holen lassen.
    ///
    /// Hier steht die Lizenzentscheidung NICHT im Weg, und der Unterschied ist
    /// wichtig genug, um ihn aufzuschreiben: Nicht mitgeliefert wird ffmpeg, weil
    /// Mitliefern Verteilen waere - uebliche Builds enthalten libx264 und stehen
    /// unter GPL, und was FrameFlip verteilt, bestimmt seine eigene Lizenz mit.
    ///
    /// Auf Klick vom offiziellen Ort holen LASSEN ist etwas anderes. FrameFlip
    /// verteilt dabei nichts; es bittet die Paketverwaltung des Betriebssystems, und
    /// die laedt, prueft die Signatur und traegt den Pfad ein. Dass ffmpeg danach
    /// ueber eine Prozessgrenze aufgerufen wird, aendert sich nicht - es ist genau
    /// dasselbe fremde Programm wie vorher, nur ohne den Umweg ueber eine Anleitung,
    /// die niemand tippt.
    ///
    /// Der Rueckgabewert ist der gefundene Pfad oder null. Erfolg heisst hier
    /// ausdruecklich "danach gefunden", nicht "winget meldete 0": Ein Paket kann
    /// installiert sein und trotzdem nicht auffindbar, und das waere fuer den
    /// Benutzer derselbe Fehlschlag.
    /// </summary>
    public static async Task<string?> InstallAsync(
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (!WingetAvailable) return null;

        var start = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        // Einzeln statt als Zeichenkette: So kann nichts an einer Anfuehrungszeichen-
        // Regel zerbrechen, und es gibt keine Stelle, an der sich etwas einschleusen
        // liesse. Die Zustimmungen sind noetig, weil winget sonst auf eine Eingabe
        // wartet, die in einem Fenster ohne Konsole niemand geben kann.
        foreach (var arg in new[]
                 {
                     "install", "--id", WingetPackage, "--exact",
                     "--accept-package-agreements", "--accept-source-agreements",
                     "--disable-interactivity",
                 })
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;

            // Mitlesen, damit die Anzeige etwas sagen kann - und damit der Puffer
            // nicht volllaeuft und winget an seiner eigenen Ausgabe haengenbleibt.
            var reading = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (line.Trim() is { Length: > 0 } text) progress?.Report(text);
                }
            }, cancellation);

            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            await IgnoreFailure(reading).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }

        /* Der frisch erweiterte PATH erreicht uns nicht mehr.
         *
         * Eine laufende Anwendung hat ihre Umgebung beim Start geerbt; was winget
         * gerade eingetragen hat, steht dort nicht. Genau dafuer sucht Locate auch
         * die Ablageorte der Paketverwaltungen ab - ohne das waere die Antwort
         * unmittelbar nach einer erfolgreichen Installation "nicht gefunden". */
        return Locate();
    }
}
