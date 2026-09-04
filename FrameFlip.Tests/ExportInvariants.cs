using System.Globalization;
using System.IO;
using FrameFlip.Export;
using FrameFlip.Imaging;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

/// <summary>
/// Invariante 9 der Referenzliste plus die Fallstricke aus Abschnitt 6.
///
/// Der Argumentbau ist bewusst von der Prozessfuehrung getrennt, deshalb laeuft
/// alles hier ohne installiertes ffmpeg - genau das ist auf diesem Rechner der Fall.
/// </summary>
public static class ExportInvariants
{
    public static void Run()
    {
        ConcatList();
        Arguments();
        Presets();
        Locator();
        AdjustmentHandover();
    }

    // ---------------------------------------------------------------- Bildkorrektur

    private static void AdjustmentHandover()
    {
        Check.Group("Bildkorrektur im Export");

        var plain = Request(Frames(1, 2), ExportPreset.H264);
        Check.That(FfmpegArguments.BuildVideoFilter(plain)!.StartsWith("scale="),
            "ohne Korrektur bleibt es beim Massefilter",
            FfmpegArguments.BuildVideoFilter(plain));

        var graded = plain with
        {
            Adjustments = new ImageAdjustments { Exposure = 0.5, Contrast = 1.2 },
        };

        var filter = FfmpegArguments.BuildVideoFilter(graded)!;
        Check.That(filter.Contains("scale=") && filter.Contains("eq="),
            "mit Korrektur stehen beide Filter im Ausdruck", filter);
        Check.That(filter.IndexOf("scale=") < filter.IndexOf("eq="),
            "erst skalieren, dann korrigieren - so arbeitet die Korrektur auf weniger Pixeln",
            filter);

        // Eine reine Kanalansicht ist ein Beurteilungswerkzeug und darf nicht im
        // Video landen.
        var channel = plain with { Adjustments = new ImageAdjustments { Channel = ChannelView.Red } };
        Check.That(!FfmpegArguments.BuildVideoFilter(channel)!.Contains("eq="),
            "eine Kanalansicht wandert nicht in den Export");

        // Der Filter muss auch im GIF-Zweig ankommen, wo der Graph benannt wird.
        var gif = FfmpegArguments.Build(
            plain with { Preset = ExportPreset.Gif, Adjustments = new ImageAdjustments { Gamma = 1.4 } },
            "l.txt", "p.png");
        Check.That(string.Join(" ", gif[1].Arguments).Contains("eq="),
            "auch das GIF bekommt die Korrektur", string.Join(" ", gif[1].Arguments));
        Check.That(string.Join(" ", gif[0].Arguments).Contains("eq="),
            "und schon die Farbpalette wird darauf berechnet");
    }

    private static IReadOnlyList<SequenceFrame> Frames(params int[] numbers)
        => numbers.Select(n => new SequenceFrame(n, $@"C:\r\f_{n:0000}.png", $"f_{n:0000}.png")).ToArray();

    private static ExportRequest Request(IReadOnlyList<SequenceFrame> frames, ExportPreset preset,
                                         int targetWidth = 0, GapHandling gaps = GapHandling.Skip,
                                         int threads = 0)
        => new()
        {
            Frames = frames,
            Preset = preset,
            OutputPath = @"C:\r\out" + preset.Extension,
            Fps = 24,
            Gaps = gaps,
            TargetWidth = targetWidth,
            SourceWidth = 1920,
            SourceHeight = 1080,
            Threads = threads,
        };

    // ---------------------------------------------------------------- Invariante 9

    private static void ConcatList()
    {
        Check.Group("Invariante 9 - die concat-Liste");

        var list = ConcatListWriter.Build(Frames(1, 2, 3), 24, GapHandling.Skip);
        var lines = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Check.That(lines[0] == "ffconcat version 1.0", "Liste beginnt mit der Kennung", lines[0]);

        // Der letzte Dateiname steht bewusst NICHT doppelt: aktuelles ffmpeg wertet
        // die duration des letzten Eintrags aus, die verbreitete Wiederholung ergibt
        // dadurch einen Frame zu viel. Nachgemessen mit ffmpeg 9.0.1.
        var fileLines = lines.Where(l => l.StartsWith("file ")).ToArray();
        Check.That(fileLines.Length == 3, "drei Frames ergeben drei file-Zeilen", $"{fileLines.Length}");
        Check.That(fileLines[^1] != fileLines[^2],
            "der letzte Dateiname steht genau einmal in der Liste",
            $"{fileLines[^2]} / {fileLines[^1]}");
        Check.That(lines.Count(l => l.StartsWith("duration ")) == 3,
            "jeder Eintrag traegt seine Dauer, auch der letzte");

        // Bildrate als Dauer je Eintrag, unabhaengig von der Systemsprache.
        Check.That(list.Contains("duration 0.04166667"),
            "die Dauer folgt der Bildrate und benutzt den Punkt als Dezimaltrennzeichen");

        var at30 = ConcatListWriter.Build(Frames(1, 2), 30, GapHandling.Skip);
        Check.That(at30.Contains("duration 0.03333333"), "andere Bildrate ergibt andere Dauer");

        // Pfade werden mit Vorwaertsschraegstrichen geschrieben - ffmpeg behandelt
        // den Rueckwaertsschraegstrich in der Liste als Fluchtzeichen.
        Check.That(!list.Contains(@"\"), "keine Rueckwaertsschraegstriche in der Liste");

        // --- Luecken ---
        var gapped = Frames(1, 2, 5, 6);   // 3 und 4 fehlen

        var skipped = ConcatListWriter.Build(gapped, 24, GapHandling.Skip);
        Check.That(skipped.Split('\n').Count(l => l.StartsWith("file ")) == 4,
            "beim Ueberspringen bleibt es bei den vier vorhandenen Frames");

        var held = ConcatListWriter.Build(gapped, 24, GapHandling.HoldLast);
        var heldFiles = held.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => l.StartsWith("file ")).ToArray();
        Check.That(heldFiles.Length == 6,
            "beim Halten fuellen zwei Standbilder den Nummernbereich auf sechs",
            $"{heldFiles.Length}");
        Check.That(heldFiles[2].Contains("f_0002") && heldFiles[3].Contains("f_0002"),
            "die Luecke wird mit dem letzten vorhandenen Frame gefuellt",
            $"{heldFiles[2]} / {heldFiles[3]}");

        // Anfuehrungszeichen im Pfad wuerden den Dateinamen sonst mitten darin beenden.
        var tricky = new[] { new SequenceFrame(1, @"C:\it's here\f_0001.png", "f_0001.png") };
        Check.That(ConcatListWriter.Build(tricky, 24, GapHandling.Skip).Contains(@"'\''"),
            "einfache Anfuehrungszeichen im Pfad werden maskiert");

        Check.That(ConcatListWriter.Build(Array.Empty<SequenceFrame>(), 24, GapHandling.Skip)
                       .StartsWith("ffconcat"),
            "eine leere Sequenz erzeugt keine kaputte Liste");
    }

    // ---------------------------------------------------------------- Argumente

    private static void Arguments()
    {
        Check.Group("ffmpeg-Argumente");

        var passes = FfmpegArguments.Build(Request(Frames(1, 2, 3), ExportPreset.H264),
                                           "liste.txt", "palette.png");
        Check.That(passes.Count == 1, "H.264 braucht einen Durchlauf", $"{passes.Count}");

        var args = passes[0].Arguments;
        var line = string.Join(' ', args);

        // -framerate ist eine Option des Bilddatei-Demuxers und existiert beim
        // concat-Demuxer nicht; ffmpeg bricht sonst mit "Option framerate not found"
        // ab. Die Bildrate steht in den duration-Zeilen der Liste.
        Check.That(!args.Contains("-framerate"),
            "kein -framerate: der concat-Demuxer kennt die Option nicht", line);
        int rateIndex = args.ToList().IndexOf("-r");
        int inputIndex = args.ToList().IndexOf("-i");
        Check.That(rateIndex > inputIndex,
            "-r steht nach der Eingabe und erzwingt die Ausgabe-Bildrate", line);

        Check.That(args.Contains("-safe") && args[args.ToList().IndexOf("-safe") + 1] == "0",
            "-safe 0 ist gesetzt, sonst lehnt concat absolute Pfade ab");

        // Ohne yuv420p entsteht yuv444p, das viele Player nicht abspielen.
        Check.That(args.Contains("yuv420p"), "H.264 schreibt yuv420p", line);
        Check.That(args.Contains("+faststart"), "H.264 setzt faststart fuer das Web");

        // Ungerade Bildmasse brechen H.264 - erst beim Encodieren, also spaet.
        Check.That(line.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2"),
            "ungerade Bildmasse werden ohne Skalierung abgefangen", line);

        // Maschinenlesbarer Fortschritt statt der Statuszeile auf stderr.
        Check.That(args.Contains("-progress") && args.Contains("pipe:1") && args.Contains("-nostats"),
            "Fortschritt kommt ueber -progress pipe:1");

        Check.That(args[^1].EndsWith(".mp4"), "die Ausgabedatei steht zuletzt", args[^1]);

        // --- Skalierung ---
        var scaled = FfmpegArguments.Build(
            Request(Frames(1, 2), ExportPreset.H264, targetWidth: 1280), "l.txt", "p.png")[0];
        var scaledLine = string.Join(' ', scaled.Arguments);
        Check.That(scaledLine.Contains("scale=1280:-2:flags=lanczos"),
            "die Zielbreite wird gesetzt, die Hoehe folgt auf ein gerades Mass", scaledLine);

        // Eine ungerade Zielbreite wuerde denselben Fehler ausloesen wie ungerade
        // Quellmasse und muss vorher abgerundet werden.
        var odd = FfmpegArguments.Build(
            Request(Frames(1), ExportPreset.H264, targetWidth: 1281), "l.txt", "p.png")[0];
        Check.That(string.Join(' ', odd.Arguments).Contains("scale=1280:"),
            "eine ungerade Zielbreite wird auf ein gerades Mass gerundet");

        // --- Threads ---
        var limited = FfmpegArguments.Build(
            Request(Frames(1), ExportPreset.H264, threads: 2), "l.txt", "p.png")[0];
        var threadIndex = limited.Arguments.ToList().IndexOf("-threads");
        Check.That(threadIndex >= 0 && limited.Arguments[threadIndex + 1] == "2",
            "die Threadzahl wird durchgereicht, damit der Encoder den Render nicht verdraengt");

        Check.That(!FfmpegArguments.Build(Request(Frames(1), ExportPreset.H264), "l.txt", "p.png")[0]
                        .Arguments.Contains("-threads"),
            "ohne Vorgabe entscheidet ffmpeg selbst");

        // --- GIF: zwei Durchlaeufe ---
        var gif = FfmpegArguments.Build(Request(Frames(1, 2), ExportPreset.Gif), "l.txt", "p.png");
        Check.That(gif.Count == 2, "GIF braucht zwei Durchlaeufe", $"{gif.Count}");
        Check.That(string.Join(' ', gif[0].Arguments).Contains("palettegen=stats_mode=diff"),
            "der erste Durchlauf erzeugt die Farbpalette");
        Check.That(gif[0].Arguments[^1] == "p.png", "die Palette ist die Ausgabe des ersten Durchlaufs");
        Check.That(!gif[0].ReportsProgress, "der Palettenlauf meldet keinen Frame-Fortschritt");

        var second = string.Join(' ', gif[1].Arguments);
        Check.That(second.Contains("paletteuse"), "der zweite Durchlauf benutzt die Palette");
        Check.That(gif[1].Arguments.Count(a => a == "-i") == 2,
            "der zweite Durchlauf hat zwei Eingaenge: Frames und Palette");
        Check.That(second.Contains("-lavfi"),
            "bei zwei Eingaengen wird der Filtergraph benannt, -vf reicht nicht");

        var gifScaled = FfmpegArguments.Build(
            Request(Frames(1), ExportPreset.Gif, targetWidth: 640), "l.txt", "p.png");
        Check.That(string.Join(' ', gifScaled[1].Arguments).Contains("[0:v]scale=640"),
            "auch beim GIF wird die Skalierung in den Graphen eingehaengt",
            string.Join(' ', gifScaled[1].Arguments));

        // --- Befehlszeile fuer Menschen ---
        var command = FfmpegArguments.ToCommandLine(@"C:\Program Files\ff\ffmpeg.exe", args);
        Check.That(command.StartsWith("\"C:\\Program Files"),
            "Pfade mit Leerzeichen werden in Anfuehrungszeichen gesetzt", command[..40]);
    }

    // ---------------------------------------------------------------- Presets

    private static void Presets()
    {
        Check.Group("Presets");

        Check.That(ExportPreset.All.Count == 5, "fuenf Presets stehen bereit");

        foreach (var preset in ExportPreset.All)
        {
            Check.That(preset.Extension.StartsWith('.'), $"{preset.Name}: Endung mit Punkt");
            Check.That(preset.Description is { Length: > 0 }, $"{preset.Name}: hat eine Erlaeuterung");
        }

        // hvc1 statt hev1 - ohne das Tag spielt QuickTime die Datei nicht ab.
        Check.That(ExportPreset.H265.VideoArguments.Contains("hvc1"),
            "H.265 traegt das Apple-taugliche Tag");

        // ProRes braucht 10 bit 4:2:2; yuv420p waere hier ein Qualitaetsverlust.
        Check.That(ExportPreset.ProRes.VideoArguments.Contains("yuv422p10le"),
            "ProRes benutzt yuv422p10le, nicht yuv420p");
        Check.That(!ExportPreset.ProRes.VideoArguments.Contains("yuv420p"),
            "ProRes uebernimmt nicht das Pixelformat von H.264");

        // Ohne -b:v 0 behandelt libvpx den CRF-Wert nur als Obergrenze.
        var vp9 = ExportPreset.Vp9.VideoArguments.ToList();
        Check.That(vp9.Contains("-b:v") && vp9[vp9.IndexOf("-b:v") + 1] == "0",
            "VP9 setzt -b:v 0, damit CRF wirklich als Qualitaetsziel wirkt");

        Check.That(ExportPreset.Gif.TwoPassPalette, "GIF ist als Zweipass-Format markiert");
        Check.That(!ExportPreset.H264.TwoPassPalette, "H.264 ist es nicht");

        // Behaelterpruefung: ProRes in MP4 laesst ffmpeg mit "Could not find tag for
        // codec prores" scheitern. Nachgemessen mit ffmpeg 9.0.1 - genau dieser Fall
        // war der einzige Fehlschlag im End-to-End-Lauf.
        Check.That(!ExportPreset.ProRes.Accepts(".mp4"), "ProRes passt nicht in MP4");
        Check.That(ExportPreset.ProRes.Accepts(".mov"), "ProRes passt in MOV");
        Check.That(ExportPreset.ProRes.Accepts(".MOV"), "die Pruefung ignoriert die Gross-/Kleinschreibung");
        Check.That(ExportPreset.H264.Accepts(".mp4") && ExportPreset.H264.Accepts(".mkv"),
            "H.264 passt in MP4 und MKV");
        Check.That(!ExportPreset.Gif.Accepts(".mp4"), "GIF passt nur in GIF");

        foreach (var preset in ExportPreset.All)
            Check.That(preset.Accepts(preset.Extension),
                $"{preset.Name}: die eigene Endung ist immer erlaubt");
    }

    // ---------------------------------------------------------------- Suche

    private static void Locator()
    {
        Check.Group("ffmpeg-Suche");

        Check.That(FfmpegLocator.Locate(@"C:\gibt\es\nicht\ffmpeg.exe") is null or { Length: > 0 },
            "ein ungueltiger Pfad fuehrt nicht zu einer Ausnahme");

        // Ein eingestellter, existierender Pfad hat Vorrang vor jeder Suche.
        var fake = Path.Combine(Path.GetTempPath(), "frameflip_fake_ffmpeg.exe");
        try
        {
            File.WriteAllText(fake, "nicht wirklich ffmpeg");
            Check.That(FfmpegLocator.Locate(fake) == fake,
                "der eingestellte Pfad wird bevorzugt");

            // Die Datei existiert, ist aber kein ffmpeg - genau dafuer gibt es die
            // Versionspruefung im Dialog.
            Check.That(FfmpegLocator.TryReadVersion(fake) is null,
                "eine gleichnamige Datei besteht die Versionspruefung nicht");
        }
        finally
        {
            try { File.Delete(fake); } catch (IOException) { }
        }

        Check.That(FfmpegLocator.InstallHint.Contains("winget"),
            "der Hinweis nennt einen konkreten Installationsweg");
        Check.That(FfmpegLocator.InstallHint.Contains("GPL"),
            "der Hinweis begruendet, warum ffmpeg nicht mitgeliefert wird");

        // Auf diesem Rechner ist ffmpeg nicht installiert; die Suche muss das ohne
        // Ausnahme melden koennen.
        var found = FfmpegLocator.Locate(null);
        Console.WriteLine(found is null
            ? "  (Hinweis: auf diesem Rechner wurde kein ffmpeg gefunden)"
            : $"  (Hinweis: ffmpeg gefunden unter {found})");
    }

    // ---------------------------------------------------------------- Auftrag

    public static void RequestMath()
    {
        Check.Group("Umfang und Laufzeit des Auftrags");

        var gapped = Frames(1, 2, 5, 6);

        var skip = new ExportRequest
        {
            Frames = gapped, Preset = ExportPreset.H264, OutputPath = "x.mp4",
            Fps = 24, Gaps = GapHandling.Skip,
        };
        Check.That(skip.OutputFrameCount == 4, "beim Ueberspringen zaehlen nur vorhandene Frames",
            $"{skip.OutputFrameCount}");

        var hold = skip with { Gaps = GapHandling.HoldLast };
        Check.That(hold.OutputFrameCount == 6, "beim Halten zaehlt der ganze Nummernbereich",
            $"{hold.OutputFrameCount}");

        Check.Near(hold.Duration.TotalSeconds, 6 / 24.0, 1e-9, "die Laufzeit folgt der Framezahl");

        var empty = skip with { Frames = Array.Empty<SequenceFrame>() };
        Check.That(empty.OutputFrameCount == 0, "eine leere Auswahl ergibt null Frames");
        Check.That(empty.Duration == TimeSpan.Zero, "und keine Laufzeit");
    }
}
