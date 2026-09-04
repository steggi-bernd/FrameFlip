namespace FrameFlip.Caching;

/// <summary>
/// Ein dekodierter Frame im Ringpuffer.
///
/// Refcount, weil Praesentation und Eviction nebenlaeufig sind: der UI-Thread haelt
/// den Puffer nur waehrend des Kopiervorgangs, der Decoder-Thread darf ihn in dieser
/// Zeit aus dem Fenster werfen. Der Puffer geht erst zum Pool zurueck, wenn beide
/// fertig sind. So muss der Kopiervorgang nicht unter dem Cache-Lock laufen.
/// </summary>
public sealed class FrameBuffer
{
    private int _references = 1;   // der Erzeuger haelt die erste Referenz

    public FrameBuffer(byte[] pixels, int width, int height, int stride, int index)
    {
        // Bgra32, dicht gepackt. Der Pool darf ein groesseres Array vergeben, aber
        // niemals einen groesseren Stride implizieren: die gueltige Nutzlast ist
        // immer exakt Stride * Height Bytes ab Index 0. Ein Stride aus der
        // Poolgroesse statt aus der Bildbreite laesst den Rest jeder Zeile
        // uninitialisiert - das ergibt genau die schwarzen Raender rechts und unten.
        if (stride != width * 4)
            throw new ArgumentException(
                $"Stride {stride} passt nicht zu Breite {width} (erwartet {width * 4}).", nameof(stride));

        if (pixels.Length < stride * height)
            throw new ArgumentException(
                $"Puffer {pixels.Length} Bytes ist zu klein fuer {width}x{height}.", nameof(pixels));

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        Index = index;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int Index { get; }

    /// <summary>Nutzlast in Bytes - NICHT die Arraylaenge, die groesser sein darf.</summary>
    public int ByteCount => Stride * Height;

    internal void AddRef() => Interlocked.Increment(ref _references);

    /// <summary>True, wenn die letzte Referenz weg ist und der Puffer zurueck in den Pool darf.</summary>
    internal bool Release() => Interlocked.Decrement(ref _references) == 0;
}
