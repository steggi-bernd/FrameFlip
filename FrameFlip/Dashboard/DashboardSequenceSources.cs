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
    /// <summary>
    /// Ordner, die frueher geoeffnet wurden - per Ablegen, ueber Projekte oder das Atelier.
    /// Ohne Angabe keine: Die Proben bestimmen selbst, was in der Liste steht.
    /// </summary>
    internal Func<IReadOnlyList<Configuration.RecentSequence>> Folders { get; init; } = () => Array.Empty<Configuration.RecentSequence>();

    /// <summary>Vergisst einen solchen Ordner - er steht dann beim naechsten Aufbau nicht mehr da.</summary>
    internal Action<string> ForgetFolder { get; init; } = _ => { };

    internal static DashboardSequenceSources Default(FrameDecoderRegistry decoders) => new(
        () => ProjectLibrary.Load().Files, Directory.Exists, File.Exists, decoders.IsSupported,
        folder => SequenceScanner.FindFirstImage(folder, decoders),
        seed => SequenceScanner.Scan(seed, decoders))
    {
        Folders = Configuration.RecentSequences.Load,
        ForgetFolder = Configuration.RecentSequences.Forget,
    };
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

    /// <summary>Ein Ordner aus einer frueheren Sitzung - er laesst sich aus der Liste nehmen.</summary>
    public bool Remembered { get; init; }
    public int? Frames { get; internal set; }
    public int Missing { get; internal set; }
    public bool HasOutput => Folder.Length > 0 && folderExists(Folder);

    // Ein Auswahl- oder Live-Scan ist neuer als eine schon laufende Zaehlschleife.
    internal long Revision { get; set; }
}

internal sealed record DashboardSequenceChange(ImageSequence? Previous, ImageSequence Current);
