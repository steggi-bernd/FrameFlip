using System.IO;
using FrameFlip.Rendering;

namespace FrameFlip.Tests;

/// <summary>
/// Der Render aus der Ferne.
///
/// Zwei Sorten Zusicherung stehen hier, und beide betreffen Fehler, die man erst
/// merkt, wenn es zu spaet ist.
///
/// Die erste: EIN RENDER UEBERSCHREIBT NICHTS. Wer vom Handy aus einen Auftrag
/// lostritt, sieht nicht, was auf der Platte liegt. Ein zweiter Lauf, der den
/// ersten ueberschreibt, faellt niemandem auf, bevor die alten Bilder gebraucht
/// werden - und dann sind sie weg.
///
/// Die zweite: DIE REIHENFOLGE DER ARGUMENTE. Blender startet bei "-f" oder "-a"
/// sofort, und alles danach wirkt nicht mehr. Ein "-o" an der falschen Stelle
/// rendert klaglos in den Temp-Ordner, und man sucht die Bilder woanders.
/// </summary>
public static class RenderInvariants
{
    public static void Run()
    {
        Check.Group("Render - wohin geschrieben wird");

        // Nichts existiert: der erste Lauf.
        var first = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", null, animation: true,
                                       folderExists: _ => false, fileExists: _ => false);

        Check.That(first.Directory == @"F:\P\TRACER\render\tracer_001",
                   "die erste Animation bekommt Lauf 001 im Projektordner", first.Directory);

        Check.That(first.Pattern.StartsWith(first.Directory), "die Bilder liegen darin", first.Pattern);
        Check.That(first.Pattern.EndsWith("frame_"), "und heissen frame_", first.Pattern);

        // 001 und 002 sind belegt - also 003, und keinesfalls 001 noch einmal.
        var third = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", null, animation: true,
                                       folderExists: path => path.EndsWith("_001") || path.EndsWith("_002"),
                                       fileExists: _ => false);

        Check.That(third.Directory.EndsWith("tracer_003"),
                   "belegte Laeufe werden uebersprungen", third.Directory);

        // Der eigentliche Punkt, als Zusicherung: nie ein bestehender Ordner.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int run = 1; run <= 12; run++)
        {
            var plan = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", null, animation: true,
                                          folderExists: taken.Contains, fileExists: _ => false);

            Check.That(taken.Add(plan.Directory), $"Lauf {run} bekommt einen Ordner, den es noch nicht gab",
                       plan.Directory);
        }

        // Einzelbilder sammeln sich in einem Ordner.
        var still = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", null, animation: false,
                                       folderExists: _ => false, fileExists: _ => false);

        Check.That(still.Directory == @"F:\P\TRACER\still", "Einzelbilder kommen nach still", still.Directory);
        Check.That(still.Pattern.EndsWith("tracer_"), "und heissen wie die Datei", still.Pattern);

        // Liegt dort schon eines, bekommt das naechste einen anderen Anfang.
        var second = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", null, animation: false,
                                        folderExists: _ => false,
                                        fileExists: path => path.EndsWith(@"still\tracer_0001.png"));

        Check.That(second.Pattern.EndsWith("tracer_2_"),
                   "ein belegter Name wird nicht ueberschrieben", second.Pattern);

        // Ein eigener Ausgabeordner schlaegt den Projektordner.
        var elsewhere = OutputPlanner.Plan(@"F:\P\TRACER\tracer.blend", @"D:\Ausgabe", animation: true,
                                           folderExists: _ => false, fileExists: _ => false);

        Check.That(elsewhere.Directory.StartsWith(@"D:\Ausgabe"), "der gewaehlte Ordner gilt",
                   elsewhere.Directory);

        // Ein Dateiname mit Zeichen, die kein Ordner tragen kann.
        var odd = OutputPlanner.Plan(@"F:\P\a:b?.blend", @"D:\Aus", animation: true,
                                     folderExists: _ => false, fileExists: _ => false);

        Check.That(!Path.GetFileName(odd.Directory).Any(c => Path.GetInvalidFileNameChars().Contains(c)),
                   "aus einem unmoeglichen Namen wird ein moeglicher", odd.Directory);

        Check.Group("Render - der Aufruf an Blender");

        var options = new RenderOptions
        {
            Animation = true,
            First = 1,
            Last = 250,
            Step = 2,
            Scene = "Szene",
            Format = "PNG",
            Samples = 128,
            Width = 1920,
            Height = 1080,
            Percentage = 50,
            Depth = 16,
            Denoise = true,
            Device = RenderDevice.Gpu,
            Engine = RenderEngine.Cycles,
        };

        var args = BlenderInvocation.Arguments(@"F:\P\TRACER\tracer.blend", @"F:\P\TRACER\render\tracer_001\frame_",
                                              options);

        Check.That(args[0] == "--disable-autoexec", "Auto-Run-Skripte bleiben beim Laden aus", args[0]);
        Check.That(args[1] == "-b", "der Hintergrundlauf folgt darauf", args[1]);
        Check.That(args[2] == @"F:\P\TRACER\tracer.blend", "dann die Datei", args[2]);

        // Der Fallstrick, um den es geht.
        Check.That(args[^1] == "-a", "der Start steht ganz am Ende", args[^1]);

        int start = args.IndexOf("-a");

        Check.That(args.IndexOf("-o") < start, "die Ausgabe wird VOR dem Start gesetzt");
        Check.That(args.IndexOf("-F") < start, "das Format auch");
        Check.That(args.IndexOf("--python-expr") < start, "und der Python-Ausdruck ebenfalls");
        Check.That(args.IndexOf("-S") < args.IndexOf("-o"), "die Szene steht vor allem, was sich auf sie bezieht");

        Check.That(args.Contains("-x") && args[args.IndexOf("-x") + 1] == "1",
                   "die Endung wird angehaengt - sonst heissen die Dateien nach nichts");

        Check.That(args[args.IndexOf("-o") + 1].EndsWith("frame_####"),
                   "das Ausgabemuster traegt die Rautezeichen", args[args.IndexOf("-o") + 1]);

        Check.That(!args[args.IndexOf("-o") + 1].Contains('\\'),
                   "und keine Rueckwaertsschraegstriche", args[args.IndexOf("-o") + 1]);

        Check.That(args.Contains("-s") && args.Contains("-e"), "der Bildbereich wird uebergeben");
        Check.That(args.Contains("-j"), "die Schrittweite auch");

        // Ein Einzelbild startet anders.
        var single = BlenderInvocation.Arguments(@"F:\a.blend", @"F:\still\a_",
                                                 new RenderOptions { Animation = false, Frame = 42 });

        Check.That(single[^2] == "-f" && single[^1] == "42", "ein Einzelbild rendert genau seinen Frame",
                   string.Join(" ", single[^2..]));

        Check.That(!single.Contains("-a"), "und startet keine Animation");

        Check.Group("Render - der Python-Ausdruck");

        string python = BlenderInvocation.Python(options);

        Check.That(python.Contains("resolution_x=1920"), "die Aufloesung steht drin");
        Check.That(python.Contains("resolution_percentage=50"), "die Prozente auch");
        Check.That(python.Contains("color_depth='16'"), "die Farbtiefe als Zeichenkette - Blender will das so");
        Check.That(python.Contains("'samples',128"), "die Samples");
        Check.That(python.Contains("use_denoising"), "das Entrauschen");
        Check.That(python.Contains("'device','GPU'"), "und das Rechenwerk");
        Check.That(python.Contains("try:"), "alles in einem Versuch");

        // Was niemand einstellt, wird auch nicht angefasst - die Datei behaelt ihre
        // eigenen Werte. Das ist die wichtigere Haelfte.
        string quiet = BlenderInvocation.Python(new RenderOptions());

        Check.That(quiet.Length == 0, "ohne Wuensche gibt es keinen Ausdruck", quiet);

        var bare = BlenderInvocation.Arguments(@"F:\a.blend", @"F:\out\f_", new RenderOptions());

        Check.That(!bare.Contains("--python-expr"), "und im Aufruf steht dann auch keiner");
        Check.That(!bare.Contains("-E"), "keine Maschine");
        Check.That(!bare.Contains("-F"), "kein Format");
        Check.That(!bare.Contains("-S"), "keine Szene");

        Check.Group("Render - unsinnige Zahlen werden begrenzt");

        var wild = new RenderOptions
        {
            Samples = 10_000_000,
            Width = -5,
            Percentage = 100_000,
            Depth = 7,
            Quality = 500,
            Step = 0,
            First = 100,
            Last = 10,
        }.Normalized();

        Check.That(wild.Samples <= 100_000, "Samples bekommen eine Obergrenze", wild.Samples?.ToString());
        Check.That(wild.Width >= 4, "eine negative Breite gibt es nicht", wild.Width?.ToString());
        Check.That(wild.Percentage <= 400, "und keine tausend Prozent", wild.Percentage?.ToString());
        Check.That(wild.Depth == 8, "eine krumme Farbtiefe wird auf eine echte gebracht", wild.Depth?.ToString());
        Check.That(wild.Quality == 100, "die Qualitaet bleibt im Bereich", wild.Quality?.ToString());
        Check.That(wild.Step == 1, "die Schrittweite ist mindestens eins", wild.Step?.ToString());

        Check.That(wild.Last >= wild.First,
                   "ein rueckwaerts laufender Bereich wird geradegezogen - sonst rendert er nichts "
                   + "und sieht dabei aus wie ein Haenger",
                   $"{wild.First}–{wild.Last}");

        Check.That(BlenderInvocation.FormatFor(".exr") == "OPEN_EXR", "die Endung bestimmt den Formatnamen");
        Check.That(BlenderInvocation.ExtensionFor("JPEG") == ".jpg", "und umgekehrt");

        Probe();
        Trace();
        Finder();
    }

    /// <summary>
    /// Die Blender-Installationen dieses Rechners.
    ///
    /// Geprueft wird das Rechnen, nicht das Suchen: welche Fassung aus einem Pfad
    /// gelesen wird, in welcher Reihenfolge sie stehen und welche vorgeschlagen
    /// wird. Was auf dieser Platte liegt, ist keine Zusicherung - auf einer anderen
    /// liegt anderes.
    /// </summary>
    private static void Finder()
    {
        Check.Group("Render - Blender finden");

        // Die Fassung steckt im Ordnernamen, in mehreren Schreibweisen.
        Check.That(BlenderFinder.VersionOf(@"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe")
                   == new Version(4, 2), "aus dem Installationsordner");

        Check.That(BlenderFinder.VersionOf(@"D:\blender-4.5.1-windows-x64\blender.exe")
                   == new Version(4, 5, 1), "aus einem entpackten Archiv");

        Check.That(BlenderFinder.VersionOf(@"C:\Steam\steamapps\common\Blender\blender.exe") is null,
                   "Steam schreibt keine Fassung in den Pfad - dann eben keine");

        Check.That(BlenderFinder.ParseVersion("Blender 1.5") is null,
                   "eine Zahl, die keine Blender-Fassung sein kann, zaehlt nicht");

        // Der Fallstrick: 4.10 ist neuer als 4.9. Als Text verglichen waere es
        // umgekehrt - und der Fehler faellt erst in dem Jahr auf, in dem es die
        // zehnte Unterfassung gibt.
        var installs = new[]
        {
            new BlenderInstall(@"C:\a\blender.exe", new Version(4, 9), BlenderFinder.InstalledSource),
            new BlenderInstall(@"C:\b\blender.exe", new Version(4, 10), BlenderFinder.InstalledSource),
            new BlenderInstall(@"C:\c\blender.exe", null, BlenderFinder.PathSource),
            new BlenderInstall(@"C:\d\blender.exe", new Version(3, 6), BlenderFinder.SteamSource),
        };

        var sorted = BlenderFinder.Sorted(installs);

        Check.That(sorted[0].Version == new Version(4, 10), "4.10 steht ueber 4.9",
                   sorted[0].Version?.ToString());

        Check.That(sorted[^1].Version is null, "was keine Fassung nennt, steht unten");

        // Steam gewinnt, auch wenn es nicht das neueste ist.
        var preferred = BlenderFinder.Preferred(installs);

        Check.That(preferred?.IsSteam == true, "die Steam-Fassung wird vorgeschlagen", preferred?.Label);

        var withoutSteam = installs.Where(i => !i.IsSteam).ToList();

        Check.That(BlenderFinder.Preferred(withoutSteam)?.Version == new Version(4, 10),
                   "ohne Steam die neueste");

        Check.That(BlenderFinder.Preferred(Array.Empty<BlenderInstall>()) is null,
                   "und ohne alles nichts");

        // Steams Bibliotheksdatei - so sieht sie wirklich aus.
        string vdf = string.Join("\n", new[]
        {
            "\"libraryfolders\"",
            "{",
            "\t\"0\"",
            "\t{",
            "\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"",
            "\t\t\"label\"\t\t\"\"",
            "\t}",
            "\t\"1\"",
            "\t{",
            "\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"",
            "\t}",
            "}",
        });

        var libraries = BlenderFinder.ParseLibraryFolders(vdf);

        Check.That(libraries.Count == 2, "beide Bibliotheken werden gelesen", libraries.Count.ToString());
        Check.That(libraries[1] == @"D:\SteamLibrary",
                   "und die doppelten Schraegstriche werden aufgeloest", libraries.Count > 1 ? libraries[1] : "");

        Check.That(BlenderFinder.ParseLibraryFolders(null).Count == 0, "keine Datei, keine Bibliotheken");
        Check.That(BlenderFinder.ParseLibraryFolders("kaputt {{{").Count == 0, "und Unsinn wirft nicht");

        // Und der Lauf auf dieser Maschine: Er darf alles ergeben, nur nicht werfen.
        var here = BlenderFinder.Find();

        Check.That(here.Count >= 0, $"die Suche laeuft durch - {here.Count} gefunden",
                   string.Join(", ", here.Select(i => i.Label)));

        Check.That(here.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == here.Count,
                   "und liefert nichts doppelt");

        // Von Hand eingetragen: der portable Build, den kein Programm finden kann.
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-blender-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "blender-4.7.2-windows-x64"));

            string own = Path.Combine(folder, "blender-4.7.2-windows-x64", "blender.exe");
            File.WriteAllText(own, "x");

            var withOwn = BlenderFinder.Find(new[] { own });

            Check.That(withOwn.Any(i => i.Path == own),
                       "ein selbst eingetragener Pfad steht in der Liste");

            var mine = withOwn.First(i => i.Path == own);

            Check.That(mine.Source == BlenderFinder.CustomSource, "und ist als solcher gekennzeichnet",
                       mine.Source);
            Check.That(mine.Version == new Version(4, 7, 2), "die Fassung kommt aus dem Ordnernamen",
                       mine.Version?.ToString());

            // Zweimal derselbe Pfad bleibt eine Zeile.
            var twice = BlenderFinder.Find(new[] { own, own });

            Check.That(twice.Count(i => i.Path == own) == 1, "zweimal eingetragen ist einmal in der Liste");

            // Und was es nicht gibt, steht auch nicht da - sonst waehlte jemand
            // etwas aus, das beim Start scheitert.
            var ghost = BlenderFinder.Find(new[] { Path.Combine(folder, "gibtsnicht", "blender.exe") });

            Check.That(!ghost.Any(i => i.Path.Contains("gibtsnicht")),
                       "ein Pfad ins Leere wird nicht angeboten");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Blenders Ausgabe mitlesen.
    ///
    /// Ein Render ohne Fenster hat kein Addon, das sich meldet - es bleibt die
    /// Standardausgabe. Die ist keine Schnittstelle und aendert sich zwischen
    /// Fassungen, deshalb steht hier ausdruecklich auch, was passieren soll, wenn
    /// eine Zeile nicht passt: nichts.
    /// </summary>
    private static void Trace()
    {
        Check.Group("Render - den Fortschritt mitlesen");

        // So sieht eine Fortschrittszeile von Cycles aus.
        var progress = RenderTrace.Read(
            "Fra:12 Mem:245.10M (Peak 300.00M) | Time:00:12.30 | Remaining:00:45.00 "
            + "| Mem:120.00M, Peak:130.00M | Scene, ViewLayer | Sample 45/128");

        Check.That(progress is not null, "eine Fortschrittszeile wird gelesen");
        Check.That(progress!.Frame == 12, "die Framenummer", progress.Frame?.ToString());
        Check.That(progress.Sample == 45 && progress.SampleTotal == 128, "der Samplestand",
                   $"{progress.Sample}/{progress.SampleTotal}");
        Check.That(progress.Status == "Sample 45/128", "und die Taetigkeit von ganz hinten", progress.Status);

        // Die Zeile, auf die es am Ende ankommt.
        var saved = RenderTrace.Read(@"Saved: 'F:\P\TRACER\render\tracer_001\frame_0012.png'");

        Check.That(saved?.Saved?.EndsWith("frame_0012.png") == true,
                   "eine geschriebene Datei wird erkannt", saved?.Saved);

        Check.That(saved?.Frame is null, "und traegt keine geratene Framenummer");

        // Ein Abschnitt ohne Samples - etwa beim Aufbauen der Szene.
        var syncing = RenderTrace.Read(
            "Fra:3 Mem:75.55M (Peak 75.55M) | Time:00:00.14 | Scene, ViewLayer | Synchronizing object | Cube");

        Check.That(syncing?.Frame == 3, "auch ohne Samples kommt die Framenummer an");
        Check.That(syncing?.Sample is null, "und der Samplestand bleibt offen");
        Check.That(syncing?.Status == "Cube", "die Taetigkeit ist das letzte Stueck der Zeile", syncing?.Status);

        // Und alles, was nichts hergibt, gibt auch nichts.
        foreach (string noise in new[]
                 {
                     "",
                     "   ",
                     "Blender 5.2.0 (hash abc123)",
                     "Info: Addon registriert",
                     "Time: 00:12.34 (Saving: 00:00.12)",
                 })
        {
            Check.That(RenderTrace.Read(noise) is null, $"Gerede wird uebergangen: {noise.Trim()}");
        }

        Check.That(RenderTrace.Read(null) is null, "und nichts bleibt nichts");
    }

    /// <summary>
    /// Was in der Datei steht, ans Handy melden.
    ///
    /// Die Zusicherungen betreffen das Lesen der Antwort, nicht Blender selbst: Ob
    /// die Marke in einem Wust aus Addon-Meldungen wiedergefunden wird, ob eine
    /// halbe Zeile etwas kaputtmacht, und ob die Farbtiefe ankommt - die liefert
    /// Blender als Zeichenkette, alles andere als Zahl.
    /// </summary>
    private static void Probe()
    {
        Check.Group("Render - was in der Datei steht");

        Check.That(RenderProbe.Arguments(@"F:.blend").Contains("-b"), "gefragt wird ohne Fenster");
        Check.That(!RenderProbe.Arguments(@"F:.blend").Contains("-f"), "und ohne zu rendern");

        Check.That(RenderProbe.Arguments("scene.blend").Contains("--disable-autoexec"),
                   "auch das Nachsehen laedt ohne Auto-Run-Skripte");

        foreach (string field in new[] { "resolution_x", "file_format", "samples", "use_denoising", "scenes" })
            Check.That(RenderProbe.Script.Contains(field), $"das Skript fragt nach {field}");

        // So sieht Blenders Ausgabe wirklich aus: Gerede, dann die eine Zeile.
        string settings = RenderProbe.Marker + """{"engine":"CYCLES","scene":"Scene","scenes":["Scene","Kamera2"],"first":1,"last":250,"step":1,"frame":7,"width":1920,"height":1080,"percent":100,"format":"PNG","color":"RGBA","depth":"16","quality":null,"compression":15,"transparent":true,"threads":0,"samples":256,"denoise":true,"device":"GPU","output":"/tmp/"}""";

        string output = string.Join("\n", new[]
        {
            "Blender 5.2.0 (hash abc123 built 2026-08-01)",
            @"Read blend: F:\P\TRACER\tracer.blend",
            "Info: Addon \"irgendwas\" registriert",
            settings,
            "Blender quit",
        });

        var report = RenderProbe.Parse(output);

        Check.That(report is not null, "die Zeile wird im Gerede gefunden");

        var found = report!.Options;

        Check.That(found.Width == 1920 && found.Height == 1080, "die Aufloesung kommt an",
                   $"{found.Width}x{found.Height}");
        Check.That(found.Samples == 256, "die Samples auch", found.Samples?.ToString());
        Check.That(found.Depth == 16, "die Farbtiefe kommt als Zeichenkette und wird zur Zahl",
                   found.Depth?.ToString());
        Check.That(found.Quality == 15, "fehlt die Qualitaet, gilt die Kompression", found.Quality?.ToString());
        Check.That(found.Denoise == true, "das Entrauschen");
        Check.That(found.Device == RenderDevice.Gpu, "das Rechenwerk");
        Check.That(found.Engine == RenderEngine.Cycles, "die Maschine");
        Check.That(found.Color == ColorMode.Rgba, "der Farbmodus");
        Check.That(found.Transparent == true, "der durchsichtige Hintergrund");
        Check.That(found.First == 1 && found.Last == 250, "und der Bildbereich");

        Check.That(report.Scenes.Count == 2 && report.Scenes[0] == "Scene",
                   "die Szenen der Datei stehen dabei", string.Join(", ", report.Scenes));

        // Und was nicht geht, geht ruhig aus.
        Check.That(RenderProbe.Parse(null) is null, "ohne Ausgabe kein Bericht");
        Check.That(RenderProbe.Parse("nur Gerede, keine Marke") is null, "ohne Marke auch nicht");
        Check.That(RenderProbe.Parse(RenderProbe.Marker + "{kaputt") is null,
                   "und eine kaputte Zeile wirft nicht, sondern liefert nichts");

        // Eine aeltere Fassung ohne Cycles: Was fehlt, bleibt offen statt falsch.
        var thin = RenderProbe.Parse(RenderProbe.Marker
                                     + """{"engine":"BLENDER_EEVEE_NEXT","width":1280,"samples":null}""");

        Check.That(thin is not null && thin.Options.Width == 1280, "was da ist, kommt an");
        Check.That(thin!.Options.Samples is null, "was fehlt, bleibt offen");
        Check.That(thin.Options.Engine == RenderEngine.Eevee, "EEVEE wird erkannt, egal wie es gerade heisst");
    }
}
