namespace FrameFlip.Decoding;

public sealed class FrameDecoderRegistry
{
    private readonly List<IFrameDecoder> _decoders = new();
    private readonly Dictionary<string, IFrameDecoder> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public static FrameDecoderRegistry CreateDefault()
    {
        var registry = new FrameDecoderRegistry();
        registry.Register(new WicFrameDecoder());

        // Hier kaeme ein ExrFrameDecoder hinzu. Bewusst nicht Teil dieser Version:
        // EXR braucht einen eigenen Reader (half-float, Tiles, Kompression) und
        // waere die einzige externe Abhaengigkeit im Projekt.

        return registry;
    }

    public void Register(IFrameDecoder decoder)
    {
        _decoders.Add(decoder);
        foreach (var extension in decoder.SupportedExtensions)
            _byExtension[extension] = decoder;   // spaeter registriert gewinnt
    }

    public IFrameDecoder? For(string extension)
        => string.IsNullOrEmpty(extension) ? null
         : _byExtension.TryGetValue(extension, out var decoder) ? decoder : null;

    public bool IsSupported(string extension) => For(extension) is not null;

    /// <summary>Bildabmessungen einer Datei, ohne sie vollstaendig zu dekodieren.</summary>
    public bool TryProbeSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        var decoder = For(System.IO.Path.GetExtension(path));
        return decoder is not null && decoder.TryProbeSize(path, out width, out height);
    }

    /// <summary>Zusaetzlich Farbtiefe und Format, fuer die Anzeige im Kopfbereich.</summary>
    public bool TryProbeInfo(string path, out ImageInfo info)
    {
        info = default;
        var decoder = For(System.IO.Path.GetExtension(path));
        return decoder is not null && decoder.TryProbeInfo(path, out info);
    }

    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;
}
