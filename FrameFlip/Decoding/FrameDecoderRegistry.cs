namespace FrameFlip.Decoding;

public sealed class FrameDecoderRegistry
{
    private readonly List<IFrameDecoder> _decoders = new();
    private readonly Dictionary<string, IFrameDecoder> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="blenderExecutable">
    /// Woher die Sichtumwandlung kommt. Blender legt in einem EXR die rohen
    /// Szenenwerte ab, ohne AgX - ohne diese Angabe zeigt FrameFlip das Bild flacher,
    /// als Blender es tut. Gefragt wird die Funktion erst beim ersten EXR, weil die
    /// Einstellungen beim Anlegen der Decoder noch nicht gelesen sind.
    /// </param>
    public static FrameDecoderRegistry CreateDefault(Func<string?>? blenderExecutable = null)
    {
        var registry = new FrameDecoderRegistry();
        registry.Register(new WicFrameDecoder());
        registry.Register(ExrFrameDecoder.Create(blenderExecutable));

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
