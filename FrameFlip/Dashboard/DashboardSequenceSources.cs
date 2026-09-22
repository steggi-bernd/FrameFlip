using System.IO;
using FrameFlip.Decoding;
using FrameFlip.Projects;
using FrameFlip.Sequencing;

namespace FrameFlip.Dashboard;

internal sealed record DashboardSequenceSources(
    Func<IReadOnlyList<KnownBlend>> Library,
    Func<string, bool> FolderExists,
    Func<string, bool> FileExists,
    Func<string, bool> Supported,
    Func<string, string?> FirstImage,
    Func<string, ImageSequence?> Scan)
{
    internal static DashboardSequenceSources Default(FrameDecoderRegistry decoders) => new(
        () => ProjectLibrary.Load().Files, Directory.Exists, File.Exists, decoders.IsSupported,
        folder => SequenceScanner.FindFirstImage(folder, decoders),
        seed => SequenceScanner.Scan(seed, decoders));
}

/// <summary>Eine beobachtete Render-Ausgabe oder ein fuer diese Sitzung geoeffneter Ordner.</summary>
internal sealed class DashboardSequenceEntry(Func<string, bool> folderExists)
{
    public string Name { get; init; } = string.Empty;
    public string BlendPath { get; init; } = string.Empty;
    public string Folder { get; init; } = string.Empty;
    public string Seed { get; init; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime SeenUtc { get; init; }
    public bool Adhoc { get; init; }
    public int? Frames { get; internal set; }
    public int Missing { get; internal set; }
    public bool HasOutput => Folder.Length > 0 && folderExists(Folder);

    // Ein Auswahl- oder Live-Scan ist neuer als eine schon laufende Zaehlschleife.
    internal long Revision { get; set; }
}

internal sealed record DashboardSequenceChange(ImageSequence? Previous, ImageSequence Current);
