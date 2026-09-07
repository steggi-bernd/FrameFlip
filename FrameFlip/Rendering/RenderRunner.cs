using System.Diagnostics;
using System.IO;
using FrameFlip.Bridge;
using FrameFlip.Configuration;
using FrameFlip.Projects;

namespace FrameFlip.Rendering;

/// <summary>Was ein Auftrag mitbringt.</summary>
/// <param name="BlendFile">Die Datei. Muss vor dem Aufruf freigegeben worden sein.</param>
/// <param name="Options">Was ueberschrieben werden soll. Was fehlt, bleibt wie in der Datei.</param>
/// <param name="OutputRoot">Wohin. Leer heisst: neben die .blend-Datei.</param>
/// <param name="Blender">
/// Womit gerechnet wird. Leer heisst: das eingestellte.
///
/// Die App darf eine der gefundenen Fassungen waehlen - wer eine alte Szene hat,
/// braucht manchmal ein altes Blender. Geprueft wird das eine Ebene hoeher: Was
/// hier ankommt, ist bereits eines von denen, die dieser Rechner kennt.
/// </param>
public sealed record RenderRequest(string BlendFile, RenderOptions Options, string? OutputRoot = null,
                                   string? Blender = null);

/// <summary>
/// Blender ohne Fenster starten und dabei zusehen.
///
/// Der Trick liegt nicht im Starten, sondern darin, dass ein solcher Render danach
/// AUSSIEHT WIE JEDER ANDERE. FrameFlip kennt Renderfortschritt bereits - vom Addon,
/// ueber die Bruecke, als Strom von Meldungen. Ein Render ohne Fenster hat kein
/// Addon; also baut diese Stelle dieselben Meldungen aus Blenders Ausgabe und
/// schiebt sie in denselben Empfaenger.
///
/// Der Gewinn ist, was dadurch NICHT noch einmal gebaut werden muss: Fortschritt,
/// Restzeit, Vorschaubild, Warnungen, Benachrichtigungen, die Anzeige am Handy. Alles
/// haengt am Auftrag, und der Auftrag ist derselbe.
///
/// Es laeuft immer nur einer. Zwei Renders auf einer Maschine sind langsamer als
/// einer nach dem anderen, und die Anzeige koennte nur einen zeigen.
/// </summary>
public sealed class RenderRunner : IDisposable
{
    /// <summary>Soviel Zeit bekommt Blender beim Abbrechen, bevor er hart beendet wird.</summary>
    private static readonly TimeSpan GraceTime = TimeSpan.FromSeconds(5);

    private readonly Func<AppSettings> _settings;
    private readonly Action<BridgeMessage> _report;
    private readonly object _gate = new();

    private Process? _process;
    private string _job = string.Empty;
    private int _frame;

    public RenderRunner(Func<AppSettings> settings, Action<BridgeMessage> report)
    {
        _settings = settings;
        _report = report;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _process is { HasExited: false };
        }
    }

    /// <summary>Der Ordner, in den der laufende Auftrag schreibt. Leer, wenn keiner laeuft.</summary>
    public string OutputFolder { get; private set; } = string.Empty;

    /// <summary>Ob der Render aus der Ferne ueberhaupt erlaubt und eingerichtet ist.</summary>
    public string? Unavailable()
    {
        var settings = _settings();

        if (!settings.HeadlessRenderEnabled) return "Rendering from the phone is switched off on the machine.";
        if (settings.BlenderPath.Length == 0) return "No path to Blender is set on the machine.";
        if (!File.Exists(settings.BlenderPath)) return "The configured Blender was not found.";

        return null;
    }

    /// <summary>
    /// Nachsehen, was in der Datei eingestellt ist.
    ///
    /// Laeuft in einem eigenen Blender, der nur laedt und wieder geht. Das dauert
    /// ein, zwei Sekunden - deshalb asynchron und mit einer Frist: Eine Datei, die
    /// beim Laden haengt, darf nicht die Leitung blockieren.
    /// </summary>
    public async Task<SceneReport?> ProbeAsync(string blendFile, string? blender = null,
                                              CancellationToken token = default)
    {
        if (Unavailable() is not null) return null;

        try
        {
            using var probe = Begin(RenderProbe.Arguments(blendFile), out _, blender);

            if (probe is null) return null;

            string output = await probe.StandardOutput.ReadToEndAsync(token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            await probe.WaitForExitAsync(timeout.Token);

            return RenderProbe.Parse(output);
        }
        catch (Exception)
        {
            // Ein Blender, der beim Nachsehen aussteigt, ist ein Grund, die
            // Einstellungen von Hand anzugeben - kein Grund fuer eine Ausnahme.
            return null;
        }
    }

    /// <summary>
    /// Einen Render starten. null heisst: laeuft. Sonst steht dort der Grund.
    /// </summary>
    public string? Start(RenderRequest request)
    {
        if (Unavailable() is string trouble) return trouble;

        string blender = request.Blender is { Length: > 0 } chosen ? chosen : _settings().BlenderPath;

        if (!File.Exists(blender)) return "The chosen Blender was not found.";

        lock (_gate)
        {
            if (_process is { HasExited: false }) return "A render is already running on the machine.";
        }

        if (!File.Exists(request.BlendFile)) return "The file is gone.";

        var options = request.Options.Normalized();

        var plan = OutputPlanner.Plan(request.BlendFile, request.OutputRoot, options.Animation,
                                      Directory.Exists, File.Exists);

        try
        {
            Directory.CreateDirectory(plan.Directory);
        }
        catch (Exception)
        {
            return "The output folder could not be created.";
        }

        // Ab jetzt darf das Handy hineinsehen - und nur hier hinein.
        ProjectLibrary.NoteOutput(plan.Directory);

        var arguments = BlenderInvocation.Arguments(request.BlendFile, plan.Pattern, options);

        Process? started = Begin(arguments, out string? failure, blender);

        if (started is null) return failure ?? "Blender could not be started.";

        lock (_gate)
        {
            _process = started;
            _job = Guid.NewGuid().ToString("N")[..8];
            _frame = 0;
            OutputFolder = plan.Directory;
        }

        // Derselbe Auftragsbeginn, den sonst das Addon meldet. Ab hier ist der Render
        // fuer den Rest des Programms nicht mehr von einem gewoehnlichen zu
        // unterscheiden.
        _report(new BridgeMessage
        {
            Type = "init",
            Job = _job,
            File = request.BlendFile,
            Scene = options.Scene,
            Engine = options.Engine?.ToString() ?? string.Empty,
            First = options.Animation ? options.First ?? 1 : options.Frame ?? 1,
            Last = options.Animation ? options.Last ?? 1 : options.Frame ?? 1,
            Width = options.Width ?? 0,
            Height = options.Height ?? 0,
            Output = plan.Directory,
        });

        Watch(started);

        return null;
    }

    /// <summary>
    /// Abbrechen.
    ///
    /// Erst hoeflich, dann bestimmt: Blender bekommt die Gelegenheit, den laufenden
    /// Frame zu Ende zu schreiben, damit keine halbe Datei liegen bleibt. Wer nach
    /// fuenf Sekunden nicht reagiert, wird beendet.
    /// </summary>
    public void Cancel()
    {
        Process? running;

        lock (_gate) running = _process;

        if (running is null || running.HasExited) return;

        try
        {
            running.CloseMainWindow();

            if (!running.WaitForExit((int)GraceTime.TotalMilliseconds)) running.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Ein Prozess, der zwischen Pruefung und Zugriff endet, ist genau das,
            // was man wollte.
        }
    }

    private Process? Begin(List<string> arguments, out string? failure, string? blender = null)
    {
        failure = null;

        string executable = blender is { Length: > 0 } chosen ? chosen : _settings().BlenderPath;

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };

        // Einzeln angehaengt, nicht zu einer Zeile zusammengeklebt: Ein Ordner mit
        // Leerzeichen oder Anfuehrungszeichen im Namen ist damit schlicht ein
        // Argument und kein Anlass fuer Ueberraschungen.
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            return Process.Start(info);
        }
        catch (Exception ex)
        {
            failure = "Blender could not be started: " + ex.Message;
            return null;
        }
    }

    /// <summary>Die Ausgabe mitlesen und daraus Meldungen machen.</summary>
    private void Watch(Process process)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is string line) OnLine(line);
            }
            catch (Exception)
            {
            }
        });

        // Der Fehlerkanal wird geleert, aber nicht ausgewertet: Blender schreibt
        // dorthin auch Harmloses. Ihn ungelesen zu lassen waere trotzdem falsch -
        // ein voller Puffer haelt den Prozess an.
        _ = Task.Run(async () =>
        {
            try { await process.StandardError.ReadToEndAsync(); } catch (Exception) { }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync();

                bool clean = process.ExitCode == 0;

                _report(new BridgeMessage { Type = clean ? "done" : "cancel", Job = _job });
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_process, process)) _process = null;
                }
            }
        });
    }

    private void OnLine(string line)
    {
        if (RenderTrace.Read(line) is not TraceUpdate update) return;

        if (update.Saved is string saved)
        {
            _report(new BridgeMessage { Type = "write", Job = _job, Frame = _frame, Path = saved });
            return;
        }

        // Ein neues Bild faengt an: Das meldet sonst das Addon vor dem Rendern.
        if (update.Frame is int frame && frame != _frame)
        {
            _frame = frame;
            _report(new BridgeMessage { Type = "pre", Job = _job, Frame = frame });
        }

        if (update.Status is string status)
            _report(new BridgeMessage { Type = "stats", Job = _job, Text = status });
    }

    public void Dispose() => Cancel();
}
