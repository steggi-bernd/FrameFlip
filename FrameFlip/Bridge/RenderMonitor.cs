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
        _server.MessageReceived += Apply;

        _server.Start();

        if (_server.IsListening) WriteHandshake(_server.Port, token);
    }

    // ---------------------------------------------------------------- Meldungen

    private void Apply(BridgeMessage message)
    {
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
                    break;

                case "pre":
                    Current(message)?.BeginFrame(message.Frame);
                    break;

                case "write":
                    Current(message)?.FrameWritten(message.Frame, message.Path);
                    break;

                // Ein Einzelbild-Render schreibt keine Datei - das Ergebnis liegt
                // nur in Blenders Speicher. Das Addon legt es nach dem Render als
                // JPEG in den Temp-Ordner und meldet den Pfad. Es zaehlt NICHT als
                // geschriebener Frame: Im Ausgabeordner des Benutzers liegt nichts,
                // und der Fortschrittsbalken wuerde sonst luegen.
                case "still":
                    Current(message)?.NoteStill(message.Frame, message.Path);
                    break;

                case "stats":
                    Current(message)?.UpdateStats(StatsParser.Parse(message.Text));
                    break;

                case "done":
                    Current(message)?.Finish(JobState.Finished);
                    break;

                case "cancel":
                    Current(message)?.Finish(JobState.Cancelled);
                    break;

                default:
                    return;                                  // unbekannt: stillschweigend
            }
        }

        // Ausserhalb der Sperre benachrichtigen: Ein Empfaenger, der auf den
        // UI-Thread marshallt, wuerde hier sonst eine Sperre ueber einen
        // Threadwechsel halten.
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
    private RenderJob? Current(BridgeMessage message)
    {
        if (Job is null) return null;
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

        _server.MessageReceived -= Apply;
        _server.Dispose();

        // Die Datei nennt einen Port, an dem niemand mehr lauscht.
        RemoveHandshake();
    }
}
