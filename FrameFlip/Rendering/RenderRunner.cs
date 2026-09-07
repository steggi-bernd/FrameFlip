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
    private readonly SemaphoreSlim _probeGate = new(1, 1);

    // Ein Prozess, seine Job-ID und die letzte Framenummer gehoeren untrennbar
    // zusammen. Globale, beim naechsten Start ueberschriebene Felder wuerden einen
    // alten Watcher sonst auf den neuen Auftrag berichten lassen.
    private sealed class RenderSession
    {
        public required Process Process { get; init; }
        public required string Job { get; init; }
        public int Frame { get; set; }
    }

    private RenderSession? _session;
    private bool _starting;
    private bool _cancelStarting;

    public RenderRunner(Func<AppSettings> settings, Action<BridgeMessage> report)
    {
        _settings = settings;
        _report = report;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _starting || _session?.Process is { HasExited: false };
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

        bool entered = false;

        try
        {
            // Mehrere "scene"-Befehle duerfen nicht mehrere unsichtbare Blender
            // starten. Die Probe ist bewusst einzeln und wird bei Abbruch sauber
            // freigegeben.
            await _probeGate.WaitAsync(token);
            entered = true;

            Process? probe = null;
            Task<string>? output = null;
            Task<string>? errors = null;

            try
            {
                probe = Begin(RenderProbe.Arguments(blendFile), out _, blender);

                if (probe is null) return null;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));

                // Beide Roehren parallel leeren. Eine volle stderr-Pipe kann einen
                // Blender anhalten, selbst wenn stdout nichts mehr liefert.
                output = probe.StandardOutput.ReadToEndAsync();
                errors = probe.StandardError.ReadToEndAsync();
                Task exited = probe.WaitForExitAsync(timeout.Token);

                // Die Frist muss VOR dem Lesen gelten; danach anzufangen wuerde
                // einen beim Laden haengenden Blender nie erreichen.
                await Task.WhenAll(output, errors, exited);

                return RenderProbe.Parse(await output);
            }
            catch (Exception)
            {
                Stop(probe);
                await FinishDraining(probe, output, errors);

                // Ein Blender, der beim Nachsehen aussteigt, ist ein Grund, die
                // Einstellungen von Hand anzugeben - kein Grund fuer eine Ausnahme.
                return null;
            }
            finally
            {
                probe?.Dispose();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (entered) _probeGate.Release();
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
            // Die Reservierung gilt schon waehrend Ausgabeordner und Prozess
            // vorbereitet werden. Ohne sie konnten zwei nahezu gleichzeitige
            // Fernbefehle beide die Pruefung passieren und zwei Blender starten.
            if (_starting || _session is not null)
                return "A render is already running on the machine.";

            _starting = true;
            _cancelStarting = false;
        }

        try
        {
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

            var arguments = BlenderInvocation.Arguments(request.BlendFile, plan.Pattern, options);

            Process? started = Begin(arguments, out string? failure, blender);

            if (started is null) return failure ?? "Blender could not be started.";

            var session = new RenderSession
            {
                Process = started,
                Job = Guid.NewGuid().ToString("N")[..8],
            };

            bool cancelled;

            lock (_gate)
            {
                cancelled = _cancelStarting;
                _starting = false;
                _cancelStarting = false;

                if (!cancelled)
                {
                    _session = session;
                    OutputFolder = plan.Directory;
                }
            }

            if (cancelled)
            {
                Stop(started);
                return "The render was cancelled before it started.";
            }

            // Ab jetzt darf das Handy hineinsehen - und nur hier hinein.
            ProjectLibrary.NoteOutput(plan.Directory);

            // Derselbe Auftragsbeginn, den sonst das Addon meldet. Ab hier ist der Render
            // fuer den Rest des Programms nicht mehr von einem gewoehnlichen zu
            // unterscheiden.
            _report(new BridgeMessage
            {
                Type = "init",
                Job = session.Job,
                File = request.BlendFile,
                Scene = options.Scene,
                Engine = options.Engine?.ToString() ?? string.Empty,
                First = options.Animation ? options.First ?? 1 : options.Frame ?? 1,
                Last = options.Animation ? options.Last ?? 1 : options.Frame ?? 1,
                Width = options.Width ?? 0,
                Height = options.Height ?? 0,
                Output = plan.Directory,
            });

            // Erst die Sitzung veroeffentlichen, dann ihre Ausgabe lesen. Blender
            // kann sofort enden; ein "done" vor "init" bliebe sonst als ewiger
            // Preparing-Auftrag im Monitor stehen.
            Watch(session);

            return null;
        }
        finally
        {
            lock (_gate)
            {
                if (_starting)
                {
                    _starting = false;
                    _cancelStarting = false;
                }
            }
        }
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
        RenderSession? session;

        lock (_gate)
        {
            if (_starting)
            {
                _cancelStarting = true;
                return;
            }

            session = _session;
        }

        Process? running = session?.Process;

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

    private static void Stop(Process? process)
    {
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Beim Abbruch kann Blender genau in diesem Moment selbst enden.
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

        var settled = pending.Select(IgnoreFailure).ToArray();

        try { await Task.WhenAll(settled).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { }
    }

    private static async Task IgnoreFailure(Task task)
    {
        try { await task; }
        catch (Exception) { }
    }

    /// <summary>Die Ausgabe mitlesen und daraus Meldungen machen.</summary>
    private void Watch(RenderSession session)
    {
        Process process = session.Process;

        _ = Task.Run(async () =>
        {
            // Erst alle Ausgaben lesen, dann das Abschlussereignis. Gerade die
            // letzte "Saved"-Zeile kann noch im Pipe-Puffer stehen, nachdem der
            // Prozess bereits sein Ende gemeldet hat.
            Task output = ReadOutputAsync(session, process);
            Task errors = DrainErrorsAsync(process);

            try
            {
                await process.WaitForExitAsync();
                await Task.WhenAll(output, errors);

                bool clean = process.ExitCode == 0;

                _report(new BridgeMessage { Type = clean ? "done" : "cancel", Job = session.Job });
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session)) _session = null;
                }
            }
        });
    }

    private async Task ReadOutputAsync(RenderSession session, Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is string line)
            {
                // Ein fehlerhafter Empfaenger darf die Pipe nicht stehen lassen:
                // sonst kann Blender auf seiner vollen Ausgabe blockieren.
                try { OnLine(session, line); } catch (Exception) { }
            }
        }
        catch (Exception)
        {
        }
    }

    // Der Fehlerkanal wird geleert, aber nicht ausgewertet: Blender schreibt
    // dorthin auch Harmloses. Ihn ungelesen zu lassen waere trotzdem falsch -
    // ein voller Puffer haelt den Prozess an.
    private static async Task DrainErrorsAsync(Process process)
    {
        try { await process.StandardError.ReadToEndAsync(); }
        catch (Exception) { }
    }

    private void OnLine(RenderSession session, string line)
    {
        if (RenderTrace.Read(line) is not TraceUpdate update) return;

        if (update.Saved is string saved)
        {
            _report(new BridgeMessage { Type = "write", Job = session.Job, Frame = session.Frame, Path = saved });
            return;
        }

        // Ein neues Bild faengt an: Das meldet sonst das Addon vor dem Rendern.
        if (update.Frame is int frame && frame != session.Frame)
        {
            session.Frame = frame;
            _report(new BridgeMessage { Type = "pre", Job = session.Job, Frame = frame });
        }

        if (update.Status is string status)
            _report(new BridgeMessage { Type = "stats", Job = session.Job, Text = status });
    }

    public void Dispose() => Cancel();
}
