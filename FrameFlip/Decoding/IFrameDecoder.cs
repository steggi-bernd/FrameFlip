namespace FrameFlip.Decoding;

/// <summary>Puffer-Beschaffung. Der Cache reicht hier seinen Pool durch, damit
/// Decoder nicht selbst allokieren und das RAM-Budget exakt bleibt.</summary>
public delegate byte[] PixelBufferAllocator(int byteCount);

/// <summary>Fertig dekodierter Frame, immer Bgra32.</summary>
public readonly record struct DecodedFrame(byte[] Pixels, int Width, int Height, int Stride);

/// <summary>
/// Erweiterungspunkt fuer weitere Formate. Ein EXR- oder DPX-Decoder waere eine
/// zusaetzliche Implementierung, die sich in der FrameDecoderRegistry registriert -
/// am Cache und an der Wiedergabe aendert sich dadurch nichts.
/// </summary>
public interface IFrameDecoder
{
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Dekodiert auf hoechstens maxWidth x maxHeight herunter. Es wird nie hochskaliert.
    /// Wird auf dem Decoder-Thread aufgerufen, muss also threadsicher sein und darf
    /// keine Dispatcher-Affinitaet erzeugen.
    /// </summary>
    bool TryDecode(string path, int maxWidth, int maxHeight, PixelBufferAllocator allocate, out DecodedFrame frame);

    /// <summary>
    /// Liest nur die Bildabmessungen, ohne die Pixel zu dekodieren. Wird gebraucht,
    /// um das Fenster in Mediengroesse zu oeffnen, bevor der erste Frame da ist.
    /// </summary>
    bool TryProbeSize(string path, out int width, out int height);

    /// <summary>
    /// Wie <see cref="TryProbeSize"/>, zusaetzlich Farbtiefe und Formatname fuer die
    /// Anzeige. Die Standardimplementierung faellt auf die Abmessungen zurueck, damit
    /// ein Decoder, der das nicht liefern kann, nicht angepasst werden muss.
    /// </summary>
    bool TryProbeInfo(string path, out ImageInfo info)
    {
        info = default;
        if (!TryProbeSize(path, out int width, out int height)) return false;

        info = new ImageInfo(width, height, 0, null);
        return true;
    }
}

/// <summary>Was sich ohne vollstaendiges Dekodieren aus dem Dateikopf lesen laesst.</summary>
public readonly record struct ImageInfo(int Width, int Height, int BitsPerPixel, string? Format)
{
    /// <summary>Bit je Kanal - das ist die Angabe, die in einer Renderpipeline zaehlt.</summary>
    public int BitsPerChannel => Channels > 0 ? BitsPerPixel / Channels : 0;

    public int Channels => Format switch
    {
        null => 0,
        var f when f.Contains("Cmyk", StringComparison.OrdinalIgnoreCase) => 4,
        var f when f.Contains("Gray", StringComparison.OrdinalIgnoreCase) => 1,
        var f when f.Contains("BlackWhite", StringComparison.OrdinalIgnoreCase) => 1,
        var f when f.Contains("Indexed", StringComparison.OrdinalIgnoreCase) => 1,
        var f when f.Contains("a", StringComparison.Ordinal) => 4,   // Bgra32, Pbgra32, Rgba64
        _ => 3,
    };
}
