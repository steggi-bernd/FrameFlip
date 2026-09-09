using System.IO;
using System.Reflection;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Localization;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Zuerst gegen die Oeffnungsentscheidungen im AppHost ausgefuehrt.</summary>
public static class ViewerOpeningInvariants
{
    public static void Run()
    {
        HotkeyFailures();
        ExplicitFailures();
        OpeningAndSwitching();
        RealSources();
        RealViewerTarget();
    }

    private static void HotkeyFailures()
    {
        Check.Group("Viewer-Oeffnung - fehlende Explorer-Auswahl");
        var cases = new (string Label, Action<Fixture> Prepare, string Key)[]
        {
            ("kein Explorer", f => f.Target = null, "S_NoExplorer"),
            ("leere Auswahl", f => f.Target = new(null, null, IntPtr.Zero), "S_NoExplorer"),
            ("leerer Ordner", f => { f.Target = new(null, "empty", IntPtr.Zero); f.First = null; }, "S_NoImageInFolder"),
            ("unlesbare Auswahl ohne Ordner", f => f.Target = new("clip.mp4", null, IntPtr.Zero), "S_NoImageInFolder"),
            ("Scan fehlgeschlagen", f => f.Scanned = null, "S_SequenceUnreadable"),
            ("Scan ohne Frames", f => f.Scanned = new(f.Sequence.Pattern, Array.Empty<SequenceFrame>()), "S_SequenceUnreadable"),
        };
        foreach (var item in cases)
        {
            using var f = new Fixture();
            item.Prepare(f);
            f.Toggle();
            Check.That(f.Notices.SequenceEqual(new[] { Strings.T(item.Key) }) && f.Opened.Count == 0 && f.Decoder.Probed.Count == 0,
                       item.Label + ": Hinweis ohne neues Fenster");
            f.Notices.Clear();
            f.Current = new Target();
            f.Toggle();
            Check.That(f.Current.Closes == 1 && f.Notices.Count == 0 && f.Opened.Count == 0,
                       item.Label + ": Hotkey schliesst stattdessen die offene Vorschau");
        }
    }

    private static void ExplicitFailures()
    {
        Check.Group("Viewer-Oeffnung - explizite Datei und Lesefehler");
        var cases = new (string Label, Action<Fixture> Prepare, string Path, string Key)[]
        {
            ("Datei fehlt", f => f.Exists = false, Fixture.Seed, "S_FileUnsupported"),
            ("Format fehlt", _ => { }, "video.mp4", "S_FileUnsupported"),
            ("Scan fehlt", f => f.Scanned = null, Fixture.Seed, "S_SequenceUnreadable"),
            ("Scan leer", f => f.Scanned = new(f.Sequence.Pattern, Array.Empty<SequenceFrame>()), Fixture.Seed, "S_SequenceUnreadable"),
        };
        foreach (var item in cases)
        {
            using var f = new Fixture();
            item.Prepare(f);
            f.Current = new Target();
            f.Host.OpenFile(item.Path);
            Check.That(f.Notices.SequenceEqual(new[] { Strings.T(item.Key) }) && f.Opened.Count == 0
                       && f.Current.Closes == 0 && f.Current.Activations == 0 && f.Current.Loaded.Count == 0,
                       item.Label + ": expliziter Aufruf laesst die offene Vorschau bestehen");
            Check.That(f.ExplorerReads == 0 && f.Decoder.Probed.Count == 0, item.Label + ": weder Explorer noch Bilddaten werden abgefragt");
        }

        using var probe = new Fixture();
        probe.Decoder.Readable = false;
        probe.Toggle();
        Check.That(probe.Notices.SequenceEqual(new[] { Strings.T("S_ImageUnreadable") }) && probe.Opened.Count == 0,
                   "unlesbarer Bildkopf meldet ohne Viewer einen Fehler");
        probe.Notices.Clear();
        probe.Current = new Target { Same = true };
        probe.Toggle();
        probe.Host.OpenFile(Fixture.Seed);
        Check.That(probe.Notices.Count == 0 && probe.Current.Closes == 0 && probe.Current.Activations == 0
                   && probe.Current.Loaded.Count == 0 && probe.Opened.Count == 0,
                   "unlesbarer Bildkopf laesst einen vorhandenen Viewer auch bei gleicher Sequenz unberuehrt");
    }

    private static void OpeningAndSwitching()
    {
        Check.Group("Viewer-Oeffnung - Startframe, Umschalten und Wiederverwenden");
        using var f = new Fixture();
        Check.That(f.ExplorerReads == 0 && f.ScanPaths.Count == 0 && f.Opened.Count == 0, "Host-Konstruktion liest und oeffnet noch nichts");
        f.Toggle();
        var request = f.Opened.Single();
        Check.That(ReferenceEquals(request.Sequence, f.Sequence) && request.Seed == Fixture.Seed && request.StartIndex == 1,
                   "Hotkey uebergibt die gefundene Sequenz und den markierten Startframe");
        Check.That(request.ExplorerWindow == new IntPtr(42) && request.SourceWidth == 1920 && request.SourceHeight == 1080,
                   "neuer Viewer bekommt Explorer-Monitor und gepruefte Bildabmessungen");
        Check.That(f.Decoder.Probed.SequenceEqual(new[] { Fixture.Seed }) && f.FirstFolders.Count == 0,
                   "unterstuetzte Auswahl wird direkt am gewaehlten Frame geprueft");

        f.Opened.Clear();
        f.Current = new Target { Same = true };
        f.Toggle();
        Check.That(f.Current.Closes == 1 && f.Current.Activations == 0 && f.Opened.Count == 0,
                   "Hotkey auf derselben Sequenz schliesst die offene Vorschau");
        f.Host.OpenFile(Fixture.Seed);
        Check.That(f.Current.Closes == 1 && f.Current.Activations == 1 && f.Current.Loaded.Count == 0,
                   "explizite Datei auf derselben Sequenz aktiviert nur das Fenster");
        f.Current.Same = false;
        f.Toggle();
        f.Host.OpenFile(Fixture.Seed);
        Check.That(f.Current.Loaded.Count == 2 && f.Opened.Count == 0 && f.Current.Closes == 1,
                   "andere Sequenz wird bei beiden Aufrufwegen im vorhandenen Viewer geladen");
        Check.That(f.Current.Loaded.All(r => r == (f.Sequence, 1, 1920, 1080)), "Sequenzwechsel erhaelt Startframe und neue Bildgroesse");
        f.Current.AcceptLoad = false;
        f.Toggle();
        Check.That(f.Current.Loaded.Count == 3 && f.Opened.Count == 0,
                   "abgelehnter Wechsel erzeugt waehrend des Viewer-Endes kein zweites Fenster");

        f.Current = null;
        f.Target = new("notes.txt", "fallback-folder", new IntPtr(7));
        f.First = f.Sequence.Frames[0].Path;
        f.Toggle();
        Check.That(f.FirstFolders.SequenceEqual(new[] { "fallback-folder" }) && f.Opened.Single().StartIndex == 0
                   && f.Opened[0].Seed == f.First && f.Opened[0].ExplorerWindow == new IntPtr(7),
                   "unlesbare Auswahl faellt auf das erste Bild im Explorer-Ordner zurueck");
        f.Opened.Clear();
        f.Target = new(null, "folder-only", new IntPtr(8));
        f.Toggle();
        Check.That(f.FirstFolders.Last() == "folder-only" && f.Opened.Single().Seed == f.First,
                   "Ordnerauswahl ohne Datei benutzt denselben Fallback");

        f.Opened.Clear();
        int explorerReads = f.ExplorerReads;
        f.Host.OpenFile(Fixture.Seed.ToUpperInvariant());
        Check.That(f.Opened.Single().StartIndex == 1 && f.Opened[0].ExplorerWindow == IntPtr.Zero && f.ExplorerReads == explorerReads,
                   "explizite Datei findet den Startframe unabhaengig von Grossschreibung und ohne Explorer");
        f.Opened.Clear();
        f.Host.OpenFile(@"C:\isolated\absent_9999.png");
        Check.That(f.Opened.Single().StartIndex == 0 && f.Decoder.Probed.Last() == f.Sequence.Frames[0].Path,
                   "fehlt der Seed im Scan, wird wie bisher der erste Listenframe geprueft");
    }

    private static void RealSources()
    {
        Check.Group("Viewer-Oeffnung - echte Dateiquellen im isolierten Ordner");
        string root = Path.Combine(Path.GetTempPath(), "frameflip-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "frame_0001.png"), second = Path.Combine(root, "frame_0002.png");
            File.WriteAllBytes(second, Array.Empty<byte>());
            File.WriteAllBytes(first, Array.Empty<byte>());
            File.WriteAllText(Path.Combine(root, "notes.txt"), "fixture");
            var decoder = new ProbeDecoder();
            var registry = new FrameDecoderRegistry();
            registry.Register(decoder);
            var sources = new ViewerOpenSources(registry, explorer: () => new(null, root, new IntPtr(3)));
            var opened = new List<ViewerOpenRequest>();
            var notices = new List<string>();
            using var host = new AppHost(sources, () => null, opened.Add, notices.Add);
            host.OpenFile(second);
            Check.That(opened.Single().Sequence.Frames.Select(f => f.Path).SequenceEqual(new[] { first, second })
                       && opened[0].StartIndex == 1, "Standard-Scan findet echte Geschwister in Frame-Reihenfolge");
            typeof(AppHost).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            Check.That(opened.Count == 2 && opened[1].Seed == first && opened[1].ExplorerWindow == new IntPtr(3),
                       "Standard-Fallback ignoriert Textdateien und nimmt das erste Bild");
            host.OpenFile(Path.Combine(root, "missing.png"));
            Check.That(opened.Count == 2 && notices.SequenceEqual(new[] { Strings.T("S_FileUnsupported") }),
                       "Standard-Dateipruefung lehnt eine fehlende Datei ab");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RealViewerTarget()
    {
        Check.Group("Viewer-Oeffnung - Schnittstelle am echten WPF-Viewer");
        string root = Path.Combine(Path.GetTempPath(), "frameflip-viewer-target-" + Guid.NewGuid().ToString("N"));
        var pattern = new SequencePattern(root, "frame_", 4, "", ".png");
        var first = new SequenceFrame(1, Path.Combine(root, "frame_0001.png"), "frame_0001.png");
        var sequence = new ImageSequence(pattern, new[] { first });
        var settings = new AppSettings { RawCacheEnabled = false, MemoryBudgetMb = 32, AdaptiveResources = false };
        var window = new ViewerWindow(sequence, 0, settings, _ => { }, FrameDecoderRegistry.CreateDefault(),
            new PixelRect(0, 0, 80, 40), 8, 4, 1);
        IViewerOpenTarget target = window;
        try
        {
            var growing = new ImageSequence(pattern with { Directory = root.ToUpperInvariant(), Prefix = "FRAME_", Padding = 5, Extension = ".PNG" },
                new[] { first, new SequenceFrame(2, "new-frame.png", "new-frame.png") });
            Check.That(target.ShowsSameSequence(growing), "wachsende Sequenz bleibt bei anderer Schreibweise und Padding dieselbe Vorschau");
            var others = new[]
            {
                pattern with { Directory = root + "-other" }, pattern with { Prefix = "other_" },
                pattern with { Suffix = "_other" }, pattern with { Extension = ".jpg" },
            };
            Check.That(others.All(p => !target.ShowsSameSequence(new ImageSequence(p, new[] { first }))),
                       "anderer Ordner, Praefix, Suffix oder Dateityp bedeutet eine andere Sequenz");
            var next = new ImageSequence(pattern with { Prefix = "next_" },
                new[] { new SequenceFrame(7, Path.Combine(root, "next_0007.png"), "next_0007.png") });
            Check.That(target.TryLoadSequence(next, 0, 12, 6) && target.ShowsSameSequence(next),
                       "interne Schnittstelle wechselt den Inhalt im echten Viewer");
            int DecodeSize(string method) => (int)typeof(ViewerWindow)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            Check.That(DecodeSize("RequiredDecodeWidth") == 12 && DecodeSize("RequiredDecodeHeight") == 6,
                       "Bilder unter 16 Pixeln behalten eine gueltige Dekodiergroesse ohne Hochskalierung");
            Check.That(target.TryLoadSequence(next, 0, 1, 1) && DecodeSize("RequiredDecodeWidth") == 1
                       && DecodeSize("RequiredDecodeHeight") == 1, "auch ein einzelner Pixel laesst sich als neue Sequenz laden");
            target.BeginClose();
            Check.That((bool)typeof(ViewerWindow).GetField("_closeAnimating", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!,
                       "Schliessanforderung benutzt weiterhin die vorhandene Ausblendung");
        }
        finally { window.Close(); }
        Check.That(!target.TryLoadSequence(sequence, 0, 8, 4), "bereits geschlossener Viewer lehnt einen weiteren Sequenzwechsel ab");
        Check.That(!Directory.Exists(root), "Viewer-Test legt keine Projektdateien an");
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Seed = @"C:\isolated\frame_0003.png";
        internal readonly ImageSequence Sequence = new(new SequencePattern(@"C:\isolated", "frame_", 4, "", ".png"),
            new[] { new SequenceFrame(1, @"C:\isolated\frame_0001.png", "frame_0001.png"), new SequenceFrame(3, Seed, "frame_0003.png") });
        internal ExplorerTarget? Target = new(Seed, @"C:\isolated", new IntPtr(42));
        internal bool Exists = true;
        internal string? First = Seed;
        internal ImageSequence? Scanned;
        internal Target? Current;
        internal int ExplorerReads;
        internal readonly List<string> ScanPaths = new(), FirstFolders = new(), Notices = new();
        internal readonly List<ViewerOpenRequest> Opened = new();
        internal readonly ProbeDecoder Decoder = new();
        internal readonly AppHost Host;
        internal Fixture()
        {
            Scanned = Sequence;
            var registry = new FrameDecoderRegistry();
            registry.Register(Decoder);
            var sources = new ViewerOpenSources(registry,
                explorer: () => { ExplorerReads++; return Target; }, exists: _ => Exists,
                firstImage: folder => { FirstFolders.Add(folder); return First; },
                scan: path => { ScanPaths.Add(path); return Scanned; });
            Host = new AppHost(sources, () => Current, Opened.Add, Notices.Add);
        }
        internal void Toggle() => typeof(AppHost).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Host, null);
        public void Dispose() => Host.Dispose();
    }

    internal sealed class Target : IViewerOpenTarget
    {
        internal bool Same, AcceptLoad = true;
        internal int Closes, Activations;
        internal readonly List<(ImageSequence Sequence, int Start, int Width, int Height)> Loaded = new();
        public bool ShowsSameSequence(ImageSequence sequence) => Same;
        public void BeginClose() => Closes++;
        public bool Activate() { Activations++; return true; }
        public bool TryLoadSequence(ImageSequence sequence, int startIndex, int sourceWidth, int sourceHeight)
        { Loaded.Add((sequence, startIndex, sourceWidth, sourceHeight)); return AcceptLoad; }
    }

    internal sealed class ProbeDecoder : IFrameDecoder
    {
        internal bool Readable = true;
        internal readonly List<string> Probed = new();
        public IReadOnlyCollection<string> SupportedExtensions => new[] { ".png" };
        public bool TryProbeSize(string path, out int width, out int height)
        { Probed.Add(path); width = 1920; height = 1080; return Readable; }
        public bool TryDecode(string path, int maxWidth, int maxHeight, PixelBufferAllocator allocate, out DecodedFrame frame)
            => throw new InvalidOperationException("Beim Oeffnen werden nur Header geprueft.");
    }
}
