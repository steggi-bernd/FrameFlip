using System.IO;
using FrameFlip.Decoding;
using FrameFlip.Interop;
using FrameFlip.Sequencing;

namespace FrameFlip.Lifecycle;

/// <summary>Explorer- und Dateiquellen; Tests ersetzen sie ohne Benutzerdaten oder echte Explorer-Auswahl.</summary>
internal sealed class ViewerOpenSources
{
    internal FrameDecoderRegistry Decoders { get; }
    internal Func<ExplorerTarget?> Explorer { get; }
    internal Func<string, bool> Exists { get; }
    internal Func<string, string?> FirstImage { get; }
    internal Func<string, ImageSequence?> Scan { get; }

    internal ViewerOpenSources(FrameDecoderRegistry decoders, Func<ExplorerTarget?>? explorer = null,
        Func<string, bool>? exists = null, Func<string, string?>? firstImage = null,
        Func<string, ImageSequence?>? scan = null)
    {
        Decoders = decoders;
        Explorer = explorer ?? ExplorerSelectionProvider.Resolve;
        Exists = exists ?? File.Exists;
        FirstImage = firstImage ?? (folder => SequenceScanner.FindFirstImage(folder, decoders));
        Scan = scan ?? (seed => SequenceScanner.Scan(seed, decoders));
    }
}

internal sealed record ViewerOpenRequest(ImageSequence Sequence, string Seed, int StartIndex,
    IntPtr ExplorerWindow, int SourceWidth, int SourceHeight);

/// <summary>Die vorhandene Vorschau umschalten, ohne ihren WPF-Lebenszyklus oder Cache zu besitzen.</summary>
internal interface IViewerOpenTarget
{
    bool ShowsSameSequence(ImageSequence sequence);
    void BeginClose();
    bool Activate();
    bool TryLoadSequence(ImageSequence sequence, int startIndex, int sourceWidth, int sourceHeight);
}
