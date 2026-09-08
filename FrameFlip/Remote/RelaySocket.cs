using System.Net.WebSockets;

namespace FrameFlip.Remote;

/// <summary>
/// Die kleine WebSocket-Oberflaeche, die der Relay-Client wirklich braucht.
///
/// Sie ist absichtlich intern: Die Anwendung kennt weiterhin nur
/// <see cref="RelayClient"/>. Der schmale Rand erlaubt es aber, den Umgang mit
/// Unterbrechungen ohne einen lokalen Klartext-Relay oder gelockerte TLS-Regeln zu
/// pruefen.
/// </summary>
internal interface IRelaySocket : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken token);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token);

    Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType kind, bool endOfMessage, CancellationToken token);
}

/// <summary>Die Produktionsfassung der kleinen Socket-Oberflaeche.</summary>
internal sealed class ClientRelaySocket : IRelaySocket
{
    private readonly ClientWebSocket _socket = new();

    public ClientRelaySocket()
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
    }

    public WebSocketState State => _socket.State;

    public Task ConnectAsync(Uri uri, CancellationToken token) => _socket.ConnectAsync(uri, token);

    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        => _socket.ReceiveAsync(buffer, token);

    public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType kind, bool endOfMessage, CancellationToken token)
        => _socket.SendAsync(buffer, kind, endOfMessage, token);

    public void Dispose() => _socket.Dispose();
}
