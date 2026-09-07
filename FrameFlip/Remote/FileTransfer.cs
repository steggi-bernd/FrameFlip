using System.IO;

namespace FrameFlip.Remote;

/// <summary>
/// Eine Datei in Stuecken hinueberschieben, ohne den Relay zu ueberfahren.
///
/// Der Relay puffert je Gegenstelle 32 Nachrichten. Wer eine 200-MB-Datei so schnell
/// hineinschiebt, wie die Platte sie hergibt, hat diesen Puffer in Millisekunden
/// voll - und dann fliegt nicht die Uebertragung raus, sondern die Verbindung.
///
/// Deshalb ein Fenster: Es sind hoechstens ein paar Stuecke gleichzeitig unterwegs,
/// und weiter geht es erst, wenn die Gegenseite quittiert. Das ist langsamer als
/// blindes Senden und der Grund, warum ueberhaupt etwas ankommt.
///
/// Die Klasse kennt weder Netzwerk noch Verschluesselung - sie bekommt eine
/// Absendefunktion. Damit laesst sich pruefen, was sonst nur mit einem Handy in der
/// Hand zu sehen waere: dass nie mehr als das Fenster unterwegs ist, und dass am
/// Ende Byte fuer Byte dasselbe herauskommt.
/// </summary>
public sealed class FileTransfer : IDisposable
{
    /// <summary>Soviele Stuecke duerfen unquittiert unterwegs sein.</summary>
    public const int Window = 4;

    private readonly object _gate = new();
    private readonly Action<byte[]> _send;
    private readonly int _chunkBytes;
    private readonly int _window;

    private FileStream? _stream;
    private byte[]? _buffer;

    private int _next;
    private int _acked = -1;
    private bool _finished;
    private bool _closed;

    public FileTransfer(int id, string path, Action<byte[]> send,
                        int chunkBytes = Envelope.ChunkBytes, int window = Window)
    {
        Id = id;
        Path = path;

        _send = send;
        _chunkBytes = Math.Max(1, chunkBytes);
        _window = Math.Max(1, window);

        _stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                 FileShare.ReadWrite | FileShare.Delete, 64 * 1024);

        Bytes = _stream.Length;
        _buffer = new byte[_chunkBytes];
    }

    public int Id { get; }
    public string Path { get; }
    public long Bytes { get; }

    /// <summary>Wieviel schon geschickt wurde - fuer die Anzeige am Rechner.</summary>
    public long Sent { get; private set; }

    /// <summary>Ob alles hinueber ist: letztes Stueck geschickt UND quittiert.</summary>
    public bool Complete
    {
        get
        {
            lock (_gate) return _finished && _acked >= _next - 1;
        }
    }

    public bool Closed
    {
        get
        {
            lock (_gate) return _closed;
        }
    }

    /// <summary>
    /// Schicken, solange das Fenster Platz hat.
    ///
    /// Wird beim Start gerufen und nach jeder Quittung. Fehler beenden den Vorgang
    /// still: Eine Datei, die waehrend der Uebertragung verschwindet, ist ein Grund
    /// aufzuhoeren, kein Grund abzustuerzen.
    /// </summary>
    public void Pump()
    {
        var frames = new List<byte[]>();

        lock (_gate)
        {
            if (_closed || _finished || _stream is null || _buffer is null) return;

            try
            {
                while (_next - _acked <= _window && !_finished)
                {
                    int read = _stream.Read(_buffer, 0, _chunkBytes);
                    bool last = read < _chunkBytes;

                    frames.Add(Envelope.Chunk(Id, _next, last, _buffer.AsSpan(0, read)));

                    Sent += read;
                    _next++;

                    if (last) _finished = true;
                }
            }
            catch (Exception)
            {
                Close();
                return;
            }
        }

        // Ausserhalb der Sperre absenden: Das Absenden geht ins Netz, und ein
        // Netzwerk, das haengt, darf nicht die Quittungen blockieren.
        foreach (byte[] frame in frames) _send(frame);
    }

    /// <summary>Eine Quittung von der Gegenseite. Aeltere zaehlen nicht rueckwaerts.</summary>
    public void Ack(int index)
    {
        lock (_gate)
        {
            if (_closed || index <= _acked) return;

            _acked = Math.Min(index, _next - 1);
        }

        Pump();
    }

    public void Cancel()
    {
        lock (_gate) Close();
    }

    private void Close()
    {
        _closed = true;

        try { _stream?.Dispose(); } catch (Exception) { }

        _stream = null;
        _buffer = null;
    }

    public void Dispose() => Cancel();
}
