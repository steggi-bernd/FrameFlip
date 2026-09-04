using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FrameFlip.Caching;

/// <summary>
/// Zweite Cachestufe: dekodierte Frames als rohe Bgra32-Bloecke auf der Platte.
///
/// Der Ringpuffer im Arbeitsspeicher ist unschlagbar schnell, aber begrenzt. Faellt
/// ein Frame heraus, kostet ihn das erneute Entpacken des PNG rund 31 ms - gemessen
/// an 1080p-Material. Dieselben Pixel von einer SSD zu lesen kostet 6 ms, also ein
/// Fuenftel. Fuer eine Sequenz, die nicht vollstaendig in den Speicher passt, macht
/// das den Unterschied zwischen fluessigem Loopen und Nachpuffern bei jeder Runde.
///
/// Bewusst ohne Kompression: sie wuerde genau die Rechenzeit zurueckbringen, die
/// hier eingespart werden soll. Der Platz ist auf einer heutigen SSD das kleinere
/// Problem - acht Megabyte je 1080p-Frame.
/// </summary>
public sealed class RawFrameCache : IDisposable
{
    /// <summary>Kennung am Dateianfang, damit Fremddateien nicht als Frames gelesen werden.</summary>
    private const uint Magic = 0x46465243;   // "FFRC"

    private const int HeaderBytes = 28;

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    private long _written;
    private bool _full;
    private bool _disposed;

    /// <summary>Wie oft gelesen, geschrieben und verworfen wurde - fuer die Anzeige.</summary>
    public long Hits { get; private set; }
    public long Writes { get; private set; }
    public long Misses { get; private set; }

    public string Directory => _directory;

    public bool IsFull => _full;

    /// <param name="key">
    /// Beschreibt Sequenz UND Dekodiergroesse. Beides muss einfliessen: dieselbe
    /// Sequenz in halber Groesse ergibt voellig andere Pixel, und ein Treffer
    /// darauf waere ein falsches Bild.
    /// </param>
    public RawFrameCache(string key, long maxBytes)
    {
        _maxBytes = Math.Max(64L * 1024 * 1024, maxBytes);
        _directory = Path.Combine(RootDirectory, Fingerprint(key));

        System.IO.Directory.CreateDirectory(_directory);
    }

    public static string RootDirectory
        => Path.Combine(Path.GetTempPath(), "FrameFlip", "rawcache");

    /// <summary>
    /// Liest einen Frame, wenn er abgelegt ist und die Quelldatei sich seither nicht
    /// geaendert hat.
    ///
    /// Die Pruefung auf Aenderungszeit und Laenge ist nicht Zierde: waehrend eines
    /// laufenden Renders werden Frames ueberschrieben, und ein Treffer auf den alten
    /// Stand zeigte hartnaeckig das Bild von vorhin.
    /// </summary>
    public bool TryRead(int index, string sourcePath, PixelBufferAllocatorDelegate allocate,
                        out byte[] pixels, out int width, out int height, out int stride)
    {
        pixels = Array.Empty<byte>();
        width = height = stride = 0;

        if (_disposed) return false;

        var path = PathFor(index);

        try
        {
            var source = new FileInfo(sourcePath);
            if (!source.Exists) return false;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                              1 << 20, FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[HeaderBytes];
            if (stream.Read(header) != HeaderBytes) return false;

            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic) return false;

            width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            stride = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);

            long sourceTicks = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
            int sourceLength = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);

            // Quelldatei inzwischen neu geschrieben? Dann ist der Block wertlos.
            if (sourceTicks != source.LastWriteTimeUtc.Ticks ||
                sourceLength != unchecked((int)source.Length))
            {
                lock (_gate) Misses++;
                return false;
            }

            if (width <= 0 || height <= 0 || stride != width * 4) return false;

            int payload = stride * height;
            if (stream.Length - HeaderBytes < payload) return false;

            pixels = allocate(payload);

            int read = 0;
            while (read < payload)
            {
                int chunk = stream.Read(pixels, read, payload - read);
                if (chunk <= 0) return false;
                read += chunk;
            }

            lock (_gate) Hits++;
            return true;
        }
        catch (Exception)
        {
            // Nicht vorhanden, gesperrt, halb geschrieben - in jedem Fall wird der
            // Frame eben neu entpackt. Ein Cache darf nie der Grund sein, dass etwas
            // gar nicht geht.
            return false;
        }
    }

    /// <summary>
    /// Legt einen dekodierten Frame ab. Fehler werden geschluckt: der Frame ist ja
    /// bereits da, das Ablegen ist reine Vorsorge fuer spaeter.
    /// </summary>
    public void Write(int index, string sourcePath, byte[] pixels, int width, int height, int stride)
    {
        if (_disposed || _full) return;
        if (width <= 0 || height <= 0 || stride != width * 4) return;

        long payload = (long)stride * height;

        lock (_gate)
        {
            if (_written + payload > _maxBytes) { _full = true; return; }
        }

        var path = PathFor(index);
        var temp = path + ".part";

        try
        {
            var source = new FileInfo(sourcePath);
            if (!source.Exists) return;

            var header = new byte[HeaderBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), stride);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), source.LastWriteTimeUtc.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), unchecked((int)source.Length));

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                               1 << 20, FileOptions.SequentialScan))
            {
                stream.Write(header);
                stream.Write(pixels, 0, (int)payload);
            }

            // Erst umbenennen macht die Datei sichtbar - ein Absturz mittendrin
            // hinterlaesst dann kein halbes Bild, das spaeter als gueltig gilt.
            File.Move(temp, path, overwrite: true);

            lock (_gate)
            {
                _written += payload;
                Writes++;
            }
        }
        catch (Exception)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
        }
    }

    private string PathFor(int index) => Path.Combine(_directory, index.ToString("D8") + ".ffr");

    /// <summary>
    /// Kurzer, stabiler Ordnername aus dem Schluessel. Ein Pfad taugt nicht direkt
    /// als Ordnername, und ein Hash haelt die Namen kurz genug fuer tiefe
    /// Verzeichnisse.
    /// </summary>
    private static string Fingerprint(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes, 0, 10).ToLowerInvariant();
    }

    /// <summary>
    /// Raeumt Ordner weg, die von frueheren Sitzungen uebrig sind - etwa nach einem
    /// Absturz. Laeuft im Hintergrund und darf beliebig fehlschlagen.
    /// </summary>
    public static void CleanOrphans(TimeSpan olderThan)
    {
        try
        {
            if (!System.IO.Directory.Exists(RootDirectory)) return;

            var cutoff = DateTime.UtcNow - olderThan;

            foreach (var directory in System.IO.Directory.GetDirectories(RootDirectory))
            {
                try
                {
                    if (System.IO.Directory.GetLastWriteTimeUtc(directory) > cutoff) continue;
                    System.IO.Directory.Delete(directory, recursive: true);
                }
                catch (Exception) { /* in Benutzung oder gesperrt */ }
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { System.IO.Directory.Delete(_directory, recursive: true); }
        catch (Exception) { /* wird beim naechsten Start aufgeraeumt */ }
    }
}

/// <summary>Puffer-Beschaffung, damit der Rohcache denselben Pool benutzt wie der Decoder.</summary>
public delegate byte[] PixelBufferAllocatorDelegate(int byteCount);
