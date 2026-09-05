using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameFlip.Bridge;

/// <summary>Eine Zeile vom Addon. Alles ausser type ist je nach Art belegt.</summary>
public sealed class BridgeMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("job")] public string? Job { get; set; }

    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("scene")] public string? Scene { get; set; }
    [JsonPropertyName("engine")] public string? Engine { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }

    [JsonPropertyName("first")] public int First { get; set; }
    [JsonPropertyName("last")] public int Last { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("frame")] public int Frame { get; set; }

    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}

/// <summary>
/// Nimmt Meldungen des Blender-Addons entgegen.
///
/// Bewusst schlicht: ein TCP-Listener auf der Loopback-Adresse, eine JSON-Zeile je
/// Meldung. Kein HTTP, keine Bibliothek. Der Addon soll ohne Fremdpakete auskommen,
/// und Pythons Standardbibliothek kann Sockets und JSON - mehr braucht es nicht.
///
/// Sicherheit: Der Listener bindet ausschliesslich an 127.0.0.1, ist also aus dem
/// Netz nicht erreichbar. Zusaetzlich muss die erste Zeile ein Token nennen, das in
/// einer Datei im Benutzerprofil steht. Damit kann kein anderes Programm auf dem
/// Rechner - etwa eine Webseite ueber einen lokalen Port - Renders vortaeuschen.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    /// <summary>Ueber diesem Wert wird eine Zeile verworfen statt gelesen.</summary>
    private const int MaxLineBytes = 64 * 1024;

    private readonly int _port;
    private readonly string _token;
    private readonly CancellationTokenSource _stopping = new();

    private TcpListener? _listener;
    private bool _disposed;

    /// <summary>Wird auf einem Hintergrundthread ausgeloest. Empfaenger muss marshallen.</summary>
    public event Action<BridgeMessage>? MessageReceived;

    /// <summary>Zahl der verbundenen Blender-Instanzen.</summary>
    public int Connections => Volatile.Read(ref _connections);
    private int _connections;

    public bool IsListening { get; private set; }

    public int Port => _port;

    public BridgeServer(int port, string token)
    {
        _port = port;
        _token = token;
    }

    public void Start()
    {
        if (IsListening || _disposed) return;

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            IsListening = true;
        }
        catch (SocketException)
        {
            // Port belegt: Der Addon findet dann keinen Empfaenger, und alles andere
            // laeuft weiter. Eine Vorschau ohne Bruecke ist immer noch eine Vorschau.
            IsListening = false;
            return;
        }

        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        var listener = _listener;
        if (listener is null) return;

        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { return; }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        Interlocked.Increment(ref _connections);

        try
        {
            using (client)
            {
                client.NoDelay = true;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 16 * 1024);

                bool greeted = false;

                while (!_stopping.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_stopping.Token);
                    if (line is null) return;                       // Gegenstelle weg
                    if (line.Length == 0) continue;
                    if (line.Length > MaxLineBytes) return;         // nicht unser Protokoll

                    var message = Deserialize(line);
                    if (message is null) continue;

                    if (!greeted)
                    {
                        // Erst begruessen, dann reden. Ein falsches Token beendet die
                        // Verbindung sofort, ohne Hinweis darauf, was falsch war.
                        if (!string.Equals(message.Type, "hello", StringComparison.Ordinal)) return;
                        if (!TokensMatch(message.Token)) return;

                        greeted = true;
                        continue;
                    }

                    try { MessageReceived?.Invoke(message); }
                    catch (Exception) { /* ein Empfaenger darf die Bruecke nicht reissen */ }
                }
            }
        }
        catch (Exception)
        {
            // Verbindungsabbrueche sind der Normalfall - Blender wird geschlossen.
        }
        finally
        {
            Interlocked.Decrement(ref _connections);
        }
    }

    /// <summary>Vergleich in konstanter Zeit, damit die Laufzeit nichts verraet.</summary>
    private bool TokensMatch(string? candidate)
    {
        if (candidate is null || candidate.Length != _token.Length) return false;

        int difference = 0;
        for (int i = 0; i < _token.Length; i++) difference |= _token[i] ^ candidate[i];

        return difference == 0;
    }

    private static BridgeMessage? Deserialize(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<BridgeMessage>(line);
            return string.IsNullOrEmpty(message?.Type) ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsListening = false;

        try { _stopping.Cancel(); } catch (Exception) { }
        try { _listener?.Stop(); } catch (Exception) { }

        _stopping.Dispose();
    }
}
