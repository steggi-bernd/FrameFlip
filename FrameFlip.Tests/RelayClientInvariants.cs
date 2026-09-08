using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Regressionen fuer die Lebensdauer einer Relay-Verbindung.
///
/// Der Fake spricht nur den WebSocket-Rand nach. Die Kopplung selbst benutzt die
/// echten v2-Kanaele, damit diese Tests kein zweites Protokoll erfinden.
/// </summary>
public static class RelayClientInvariants
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static void Run()
    {
        RunCase("Relay-Client - ein Start, ein Verbindungsloop", ParallelStartAsync);
        RunCase("Relay-Client - Peer-Abbruch beendet den alten Sender", PeerDownAsync);
        RunCase("Relay-Client - Sendefehler verbindet neu", SendFailureReconnectsAsync);
        RunCase("Relay-Client - Beenden wartet auf den Sender", DisposeStopsPumpAsync);
    }

    private static void RunCase(string name, Func<Task> test)
    {
        Check.Group(name);

        try
        {
            test().WaitAsync(Timeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Check.That(false, "Test endet ohne Haenger", ex.GetType().Name + " - " + ex.Message);
        }
    }

    private static async Task ParallelStartAsync()
    {
        var socket = new ScriptedRelaySocket();
        var factory = new SocketFactory(socket);
        await using var client = NewClient(factory);

        Parallel.For(0, 32, _ => client.Start());

        await socket.WaitConnectedAsync();
        await Task.Delay(30);

        Check.That(factory.Created == 1, "gleichzeitiger Start erzeugt genau eine Verbindung", factory.Created.ToString());
    }

    private static async Task PeerDownAsync()
    {
        PairingKey key = PairingKey.Create();
        var socket = new ScriptedRelaySocket();
        await using var client = NewClient(new SocketFactory(socket), key);

        client.Start();
        await socket.WaitConnectedAsync();

        using PhonePeer first = await PairAsync(client, socket, key);

        socket.HoldNextSend();
        client.Send(Encoding.UTF8.GetBytes("alter stand"));
        await socket.WaitSendBlockedAsync();

        socket.Text("{\"t\":\"peer\",\"up\":false}");
        socket.Text("{\"t\":\"peer\",\"up\":true}");

        SentFrame nextHello = await socket.TakeSentAsync();

        Check.That(socket.SendWasCancelled, "peer:false bricht den alten Send ab");
        Check.That(socket.MaximumConcurrentSends == 1, "nie zwei Sends gleichzeitig", socket.MaximumConcurrentSends.ToString());
        Check.That(IsHello(nextHello.Data), "neue Gegenstelle bekommt neue Begruessung");

        using PhonePeer second = await FinishPairAsync(client, socket, key, nextHello);

        byte[] fresh = Encoding.UTF8.GetBytes("neuer stand");
        client.Send(fresh);

        SentFrame application = await socket.TakeSentAsync();
        Check.That(second.Channel.TryOpen(application.Data, out byte[]? opened) && opened!.SequenceEqual(fresh),
                   "der neue Kanal sendet wieder", Describe(application.Data));
    }

    private static async Task SendFailureReconnectsAsync()
    {
        PairingKey key = PairingKey.Create();
        var first = new ScriptedRelaySocket();
        var second = new ScriptedRelaySocket();
        var factory = new SocketFactory(first, second);
        await using var client = NewClient(factory, key);

        client.Start();
        await first.WaitConnectedAsync();

        using PhonePeer peer = await PairAsync(client, first, key);

        first.FailNextSend();
        client.Send(Encoding.UTF8.GetBytes("dieser send scheitert"));

        await factory.WaitForCreatedAsync(2);
        await second.WaitConnectedAsync();

        Check.That(factory.Created == 2, "Sendefehler startet genau einen Neuaufbau", factory.Created.ToString());
        Check.That(first.Disposed, "fehlerhafte Verbindung wird entsorgt");
        Check.That(second.Connected, "neue Verbindung wird aufgebaut");
    }

    private static async Task DisposeStopsPumpAsync()
    {
        PairingKey key = PairingKey.Create();
        var socket = new ScriptedRelaySocket();
        await using var client = NewClient(new SocketFactory(socket), key);

        client.Start();
        await socket.WaitConnectedAsync();

        using PhonePeer peer = await PairAsync(client, socket, key);

        socket.HoldNextSend();
        client.Send(Encoding.UTF8.GetBytes("noch unterwegs"));
        await socket.WaitSendBlockedAsync();

        await client.DisposeAsync().AsTask().WaitAsync(Timeout);

        Check.That(socket.SendWasCancelled, "Dispose bricht den wartenden Send ab");
        Check.That(socket.Disposed, "Socket wird erst nach dem Sender entsorgt");
        Check.That(client.State == RelayState.Off, "Client meldet nach Dispose aus");
    }

    private static RelayClient NewClient(SocketFactory factory, PairingKey? key = null)
    {
        key ??= PairingKey.FromBytes(Enumerable.Range(1, PairingKey.KeyBytes).Select(i => (byte)i).ToArray());
        var invite = new PairingInvite(key, "relay.example.test");

        return new RelayClient(invite, factory.Create, static (_, _) => Task.CompletedTask);
    }

    private static async Task<PhonePeer> PairAsync(RelayClient client, ScriptedRelaySocket socket, PairingKey key)
    {
        socket.Text("{\"t\":\"peer\",\"up\":true}");
        SentFrame hello = await socket.TakeSentAsync();
        return await FinishPairAsync(client, socket, key, hello);
    }

    private static async Task<PhonePeer> FinishPairAsync(
        RelayClient client,
        ScriptedRelaySocket socket,
        PairingKey key,
        SentFrame hostHello)
    {
        byte[]? hostSalt = null;
        bool isHello = hostHello.Kind == WebSocketMessageType.Binary &&
                       SecureChannel.TryReadHello(hostHello.Data, out hostSalt);

        Check.That(isHello, "Host sendet eine v2-Begruessung");

        if (!isHello || hostSalt is null)
            throw new InvalidOperationException("Der Test kann ohne Host-Salz nicht fortfahren.");

        byte[] clientSalt = Enumerable.Range(0, SecureChannel.SaltBytes).Select(i => (byte)(0xD0 + i)).ToArray();
        socket.Binary(Hello(clientSalt));

        SentFrame hostProof = await socket.TakeSentAsync();
        var phone = SecureChannel.Establish(key, RelayRole.Client, hostSalt, clientSalt);

        bool proofValid = phone.TryOpen(hostProof.Data, out byte[]? proof) &&
                          key.IsConfirmation(proof!, RelayRole.Host, hostSalt, clientSalt);

        Check.That(proofValid, "Host weist den Kopplungsschluessel nach");

        byte[] confirmation = key.Confirmation(RelayRole.Client, hostSalt, clientSalt);
        try
        {
            socket.Binary(phone.Seal(confirmation));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(confirmation);
        }

        await WaitUntilAsync(() => client.State == RelayState.Paired);
        return new PhonePeer(phone);
    }

    private static byte[] Hello(byte[] salt)
    {
        byte[] frame = new byte[1 + SecureChannel.SaltBytes];
        frame[0] = SecureChannel.Version;
        salt.CopyTo(frame, 1);
        return frame;
    }

    private static bool IsHello(byte[] frame) => SecureChannel.TryReadHello(frame, out _);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + Timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException("erwarteter Zustand kam nicht");
            await Task.Delay(10);
        }
    }

    private static string Describe(byte[] frame) => Convert.ToHexString(frame.AsSpan(0, Math.Min(frame.Length, 16)));

    private sealed class PhonePeer : IDisposable
    {
        public PhonePeer(SecureChannel channel) => Channel = channel;

        public SecureChannel Channel { get; }

        public void Dispose() => Channel.Dispose();
    }

    private readonly record struct SentFrame(WebSocketMessageType Kind, byte[] Data);

    private readonly record struct ReceivedFrame(WebSocketMessageType Kind, byte[] Data);

    /// <summary>Deterministischer WebSocket ohne Netz und ohne Klartext-Relay.</summary>
    private sealed class ScriptedRelaySocket : IRelaySocket
    {
        private readonly Channel<ReceivedFrame> _received = Channel.CreateUnbounded<ReceivedFrame>();
        private readonly Channel<SentFrame> _sent = Channel.CreateUnbounded<SentFrame>();
        private readonly object _gate = new();
        private readonly TaskCompletionSource<bool> _connected = NewSignal();
        private readonly TaskCompletionSource<bool> _sendBlocked = NewSignal();

        private TaskCompletionSource<bool>? _heldSend;
        private bool _holdNextSend;
        private bool _failNextSend;
        private bool _disposed;
        private bool _sendWasCancelled;
        private int _activeSends;
        private int _maximumConcurrentSends;
        private WebSocketState _state = WebSocketState.None;

        public WebSocketState State => _state;
        public bool Connected => _connected.Task.IsCompletedSuccessfully;
        public bool Disposed => _disposed;
        public bool SendWasCancelled => _sendWasCancelled;
        public int MaximumConcurrentSends => _maximumConcurrentSends;

        public Task ConnectAsync(Uri uri, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _state = WebSocketState.Open;
            _connected.TrySetResult(true);
            return Task.CompletedTask;
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            ReceivedFrame frame = await _received.Reader.ReadAsync(token);
            frame.Data.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(frame.Data.Length, frame.Kind, endOfMessage: true);
        }

        public async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType kind, bool endOfMessage, CancellationToken token)
        {
            int active = Interlocked.Increment(ref _activeSends);
            RaiseMaximum(active);

            try
            {
                TaskCompletionSource<bool>? hold = null;
                bool fail;

                lock (_gate)
                {
                    fail = _failNextSend;
                    _failNextSend = false;

                    if (_holdNextSend)
                    {
                        _holdNextSend = false;
                        hold = _heldSend;
                    }
                }

                if (hold is not null)
                {
                    _sendBlocked.TrySetResult(true);

                    try
                    {
                        await hold.Task.WaitAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        _sendWasCancelled = true;
                        throw;
                    }
                }

                if (fail) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);

                byte[] copy = buffer.AsSpan().ToArray();
                _sent.Writer.TryWrite(new SentFrame(kind, copy));
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        public async Task WaitConnectedAsync() => await _connected.Task.WaitAsync(Timeout);

        public async Task WaitSendBlockedAsync() => await _sendBlocked.Task.WaitAsync(Timeout);

        public async Task<SentFrame> TakeSentAsync()
            => await _sent.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public void Text(string text) => _received.Writer.TryWrite(new ReceivedFrame(WebSocketMessageType.Text, Encoding.UTF8.GetBytes(text)));

        public void Binary(byte[] data) => _received.Writer.TryWrite(new ReceivedFrame(WebSocketMessageType.Binary, data));

        public void HoldNextSend()
        {
            lock (_gate)
            {
                _holdNextSend = true;
                _heldSend = NewSignal();
            }
        }

        public void FailNextSend()
        {
            lock (_gate) _failNextSend = true;
        }

        public void Dispose()
        {
            _disposed = true;
            _state = WebSocketState.Closed;
            _received.Writer.TryComplete();
            _sent.Writer.TryComplete();
        }

        private void RaiseMaximum(int active)
        {
            int seen;

            while ((seen = Volatile.Read(ref _maximumConcurrentSends)) < active &&
                   Interlocked.CompareExchange(ref _maximumConcurrentSends, active, seen) != seen)
            {
            }
        }
    }

    private sealed class SocketFactory
    {
        private readonly ConcurrentQueue<ScriptedRelaySocket> _available;
        private int _created;

        public SocketFactory(params ScriptedRelaySocket[] sockets)
        {
            _available = new ConcurrentQueue<ScriptedRelaySocket>(sockets);
        }

        public int Created => Volatile.Read(ref _created);

        public IRelaySocket Create()
        {
            if (!_available.TryDequeue(out ScriptedRelaySocket? socket))
                throw new InvalidOperationException("Der Test hat mehr Verbindungen erzeugt als vorgesehen.");

            Interlocked.Increment(ref _created);
            return socket;
        }

        public async Task WaitForCreatedAsync(int expected)
        {
            await WaitUntilAsync(() => Created >= expected);
        }
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
