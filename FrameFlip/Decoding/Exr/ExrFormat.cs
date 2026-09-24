namespace FrameFlip.Decoding.Exr;

/// <summary>Speicherform eines Kanals in der Datei.</summary>
public enum ExrPixelType
{
    UInt = 0,
    Half = 1,
    Float = 2,
}

/// <summary>
/// Die Kompressionsverfahren des Formats. Gelesen werden die ersten vier;
/// die uebrigen stehen hier, damit eine nicht unterstuetzte Datei mit ihrem
/// richtigen Namen abgelehnt werden kann statt mit einer Zahl.
/// </summary>
public enum ExrCompression
{
    None = 0,
    Rle = 1,
    ZipS = 2,
    Zip = 3,
    Piz = 4,
    Pxr24 = 5,
    B44 = 6,
    B44A = 7,
    DwaA = 8,
    DwaB = 9,
}

public enum ExrLineOrder
{
    IncreasingY = 0,
    DecreasingY = 1,
    RandomY = 2,
}

/// <summary>Ganzzahliges Rechteck, wie es das Format fuer Daten- und Anzeigefenster benutzt.</summary>
public readonly record struct ExrBox(int XMin, int YMin, int XMax, int YMax)
{
    /// <summary>Beide Grenzen sind einschliesslich - ein Fenster 0..1919 ist 1920 breit.</summary>
    public int Width => XMax - XMin + 1;

    public int Height => YMax - YMin + 1;

    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// Ein Kanal der Datei. Der Name traegt bei Multilayer-Dateien die ganze Hierarchie:
/// "ViewLayer.Combined.R", "ViewLayer.Depth.Z". Zerlegt wird er nicht hier, sondern
/// erst dort, wo Ebenen gebraucht werden - der Reader soll keine Annahme ueber die
/// Namenskonvention einer bestimmten Blender-Version treffen.
/// </summary>
public sealed record ExrChannel(
    string Name,
    ExrPixelType PixelType,
    bool PerceptuallyLinear,
    int XSampling,
    int YSampling)
{
    public int BytesPerSample => PixelType == ExrPixelType.Half ? 2 : 4;

    /// <summary>
    /// True, wenn der Kanal in voller Aufloesung vorliegt. Unterabtastung kommt bei
    /// Rendern praktisch nicht vor, wohl aber bei YCbCr-Material aus anderen Quellen.
    /// Der Reader lehnt solche Dateien ab, statt die Zeilen falsch zu verteilen.
    /// </summary>
    public bool IsFullResolution => XSampling == 1 && YSampling == 1;
}

/// <summary>
/// Der Kopf einer Scanline-EXR. Enthaelt nur, was zum Lesen der Pixel gebraucht wird -
/// alle uebrigen Attribute werden beim Parsen uebersprungen, ihre Rohdaten liegen
/// in <see cref="RawAttributes"/>, weil dort spaeter das Cryptomatte-Manifest steht.
/// </summary>
public sealed class ExrHeader
{
    public required ExrBox DataWindow { get; init; }

    public required ExrBox DisplayWindow { get; init; }

    public required ExrCompression Compression { get; init; }

    public required ExrLineOrder LineOrder { get; init; }

    /// <summary>In der Reihenfolge der Datei, die das Format alphabetisch vorschreibt.</summary>
    public required IReadOnlyList<ExrChannel> Channels { get; init; }

    public double PixelAspectRatio { get; init; } = 1.0;

    /// <summary>Byteversatz, an dem die Offset-Tabelle beginnt.</summary>
    public required long TableOffset { get; init; }

    /// <summary>
    /// Attribute, die der Reader nicht auswertet - Name auf Rohdaten. Das Manifest
    /// einer Cryptomatte ist so eines und kann mehrere Megabyte gross sein; es wird
    /// deshalb nur gelesen, wenn jemand danach fragt.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> RawAttributes { get; init; }
        = new Dictionary<string, byte[]>();

    /// <summary>
    /// Wie viele Zeilen in einem Block stecken. Das ist keine Eigenschaft der Datei,
    /// sondern des Verfahrens: ZIP komprimiert immer 16 Zeilen am Stueck, ZIPS eine
    /// einzelne. Aus dieser Zahl folgt die Laenge der Offset-Tabelle.
    /// </summary>
    public int ScanlinesPerBlock => Compression switch
    {
        ExrCompression.None or ExrCompression.Rle or ExrCompression.ZipS => 1,
        ExrCompression.Zip or ExrCompression.Pxr24 => 16,
        ExrCompression.Piz or ExrCompression.B44 or ExrCompression.B44A or ExrCompression.DwaA => 32,
        ExrCompression.DwaB => 256,
        _ => 1,
    };

    public int BlockCount
    {
        get
        {
            int perBlock = ScanlinesPerBlock;
            return (DataWindow.Height + perBlock - 1) / perBlock;
        }
    }

    /// <summary>Was dieser Reader tatsaechlich auspacken kann.</summary>
    public bool IsSupportedCompression => Compression is
        ExrCompression.None or ExrCompression.Rle or ExrCompression.ZipS or ExrCompression.Zip;

    /// <summary>Summe der Bytes einer Bildzeile ueber alle Kanaele.</summary>
    public int BytesPerScanline
    {
        get
        {
            int width = DataWindow.Width;
            int total = 0;
            foreach (var channel in Channels) total += width * channel.BytesPerSample;
            return total;
        }
    }
}
