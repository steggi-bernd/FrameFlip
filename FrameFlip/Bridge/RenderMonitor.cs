using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FrameFlip.Bridge;

/// <summary>
/// Haelt den Zustand des laufenden Renders und verteilt die Meldungen des Addons
/// darauf. Die Bruecke selbst kennt nur Zeilen; hier entsteht daraus ein Auftrag.
///
/// Faellt irgendetwas aus - Port belegt, Addon nicht installiert, Blender gar nicht
/// gestartet - bleibt einfach alles still. FrameFlip ist zuerst eine Vorschau und
/// erst danach ein Render-Monitor.
/// </summary>
public sealed class RenderMonitor : IDisposable
{
    /// <summary>
    /// Wo Addon und FrameFlip sich finden. Liegt im Benutzerprofil, ist also nur fuer
    /// dasselbe Konto lesbar - genau die Grenze, die hier gebraucht wird.
    /// </summary>
    public static string HandshakeFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FrameFlip", "bridge.json");

    private readonly BridgeServer _server;
    private readonly object _gate = new();
    private bool _disposed;
    private string? _jobConnection;

    /// <summary>Wird auf einem Hintergrundthread ausgeloest.</summary>
    public event Action? Changed;

    /// <summary>Ein neuer Frame liegt auf der Platte. Pfad als Argument.</summary>
    public event Action<string>? FrameWritten;

    public RenderJob? Job { get; private set; }

    public bool IsListening => _server.IsListening;

    public bool HasRunningJob
    {
        get { lock (_gate) return Job?.IsRunning == true; }
    }

    public RenderMonitor(int port)
    {
        string token = CreateToken();

        _server = new BridgeServer(port, token);
        _server.MessageReceivedFrom += OnBridgeMessage;
        _server.DisconnectedFrom += OnBlenderGone;

        _server.Start();

        if (_server.IsListening) WriteHandshake(_server.Port, token);
    }

    // ---------------------------------------------------------------- Meldungen

    /// <summary>
    /// Eine Meldung von hier statt vom Addon.
    ///
    /// Ein Render ohne Fenster hat kein Addon; die Meldungen baut
    /// <see cref="Rendering.RenderRunner"/> aus Blenders Ausgabe. Sie gehen durch
    /// dieselbe Tuer wie alle anderen - damit ist so ein Render fuer den Rest des
    /// Programms von einem gewoehnlichen nicht zu unterscheiden, und Fortschritt,
    /// Vorschau, Warnungen und die Anzeige am Handy funktionieren, ohne dass irgend
    /// etwas davon ein zweites Mal gebaut werden muesste.
    /// </summary>
    public void Feed(BridgeMessage message) => Apply(message, connection: null);

    private void OnBridgeMessage(BridgeMessage message, string connection)
        => Apply(message, connection);

    private void Apply(BridgeMessage message, string? connection)
    {
        bool changed = false;

        lock (_gate)
        {
            switch (message.Type)
            {
                case "init":
                    Job = new RenderJob
                    {
                        Id = message.Job ?? Guid.NewGuid().ToString("N")[..8],
                        BlendFile = message.File ?? string.Empty,
                        Scene = message.Scene ?? string.Empty,
                        Engine = message.Engine ?? string.Empty,
                        FirstFrame = message.First,
                        LastFrame = Math.Max(message.First, message.Last),
                        Width = message.Width,
                        Height = message.Height,
                        OutputDirectory = message.Output ?? string.Empty,
                    };
                    _jobConnection = connection;
                    changed = true;

                    // Damit ein Projekt im Browser auftaucht, ohne dass jemand einen
                    // Ordner eintragen muss: Was gerendert hat, ist bekannt. Auf einem
                    // eigenen Thread, weil hier eine Datei geschrieben wird und der
                    // Zustand des Renders darauf nicht warten soll.
                    if (message.File is { Length: > 0 } blend)
                        Task.Run(() => Projects.ProjectLibrary.Note(blend));

                    break;

                case "pre":
                    if (Current(message, connection) is RenderJob pre)
                    {
                        pre.BeginFrame(message.Frame);
                        changed = true;
                    }
                    break;

                case "write":
                    if (Current(message, connection) is RenderJob write)
                    {
                        write.FrameWritten(message.Frame, message.Path);
                        changed = true;
                    }
                    break;

                // Ein Einzelbild-Render schreibt keine Datei - das Ergebnis liegt
                // nur in Blenders Speicher. Das Addon legt es nach dem Render als
                // JPEG in den Temp-Ordner und meldet den Pfad. Es zaehlt NICHT als
                // geschriebener Frame: Im Ausgabeordner des Benutzers liegt nichts,
                // und der Fortschrittsbalken wuerde sonst luegen.
                case "still":
                    if (Current(message, connection) is RenderJob still)
                    {
                        still.NoteStill(message.Frame, message.Path);
                        changed = true;
                    }
                    break;

                case "stats":
                    if (Current(message, connection) is RenderJob stats)
                    {
                        stats.UpdateStats(StatsParser.Parse(message.Text));
                        changed = true;
                    }
                    break;

                case "done":
                    if (Current(message, connection) is RenderJob done)
                    {
                        done.Finish(JobState.Finished);
                        changed = true;
                    }
                    break;

                case "cancel":
                    if (Current(message, connection) is RenderJob cancel)
                    {
                        cancel.Finish(JobState.Cancelled);
                        changed = true;
                    }
                    break;

                default:
                    return;                                  // unbekannt: stillschweigend
            }
        }

        if (changed) NotifyChanged(message);
    }

    /// <summary>
    /// Blender ist weg.
    ///
    /// Am Ende eines Renders kommt "done" oder "cancel"; danach steht kein Auftrag
    /// mehr offen. Faellt die Verbindung, waehrend noch einer laeuft, ist Blender
    /// mitten darin verschwunden. Ob abgestuerzt, abgeschossen oder zugeklappt,
    /// laesst sich von hier nicht unterscheiden - fuer den, der wartet, ist es
    /// dasselbe: Der Render wird nicht fertig.
    ///
    /// Nur beim LAUFENDEN Auftrag. Blender nach getaner Arbeit zu schliessen ist
    /// der Normalfall und keine Meldung wert.
    /// </summary>
    private void OnBlenderGone(string connection)
    {
        lock (_gate)
        {
            if (Job is not { IsRunning: true } job) return;
            if (!string.Equals(_jobConnection, connection, StringComparison.Ordinal)) return;

            job.NoteGone();
        }

        try { Changed?.Invoke(); } catch (Exception) { }
    }

    /// <summary>
    /// Empfaenger benachrichtigen - ausserhalb der Sperre.
    ///
    /// Ein Empfaenger, der auf den UI-Thread marshallt, hielte sonst eine Sperre
    /// ueber einen Threadwechsel.
    /// </summary>
    private void NotifyChanged(BridgeMessage message)
    {
        if (message.Type == "write" && !string.IsNullOrEmpty(message.Path))
        {
            try { FrameWritten?.Invoke(message.Path); } catch (Exception) { }
        }

        try { Changed?.Invoke(); } catch (Exception) { }
    }

    /// <summary>
    /// Der Auftrag, auf den sich die Meldung bezieht - oder null.
    ///
    /// Die Pruefung der Kennung ist noetig, weil zwei Blender-Instanzen gleichzeitig
    /// melden koennen. Ohne sie schriebe die zweite in den Auftrag der ersten, und
    /// der Fortschrittsbalken spraenge zwischen beiden hin und her.
    /// </summary>
    private RenderJob? Current(BridgeMessage message, string? connection)
    {
        if (Job is null) return null;
        if (!string.Equals(_jobConnection, connection, StringComparison.Ordinal)) return null;
        if (message.Job is null) return Job;

        return string.Equals(Job.Id, message.Job, StringComparison.Ordinal) ? Job : null;
    }

    // ---------------------------------------------------------------- Handschlag

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void WriteHandshake(int port, string token)
    {
        try
        {
            var file = HandshakeFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var payload = JsonSerializer.Serialize(new { port, token });

            // Erst daneben schreiben, dann umbenennen: Ein Addon, das genau in diesem
            // Moment liest, bekommt sonst eine halbe Datei zu sehen.
            var temp = file + ".part";
            File.WriteAllText(temp, payload);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception)
        {
            // Ohne Handschlagdatei findet der Addon nichts. Ein Grund zu scheitern
            // ist das nicht.
        }
    }

    private static void RemoveHandshake()
    {
        try { if (File.Exists(HandshakeFile)) File.Delete(HandshakeFile); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _server.MessageReceivedFrom -= OnBridgeMessage;
        _server.DisconnectedFrom -= OnBlenderGone;
        _server.Dispose();

        // Die Datei nennt einen Port, an dem niemand mehr lauscht.
        RemoveHandshake();
    }
}
