using System.IO;
using FrameFlip.Projects;

namespace FrameFlip.Tests;

/// <summary>
/// Die Zusicherungen zur Projekterkennung.
///
/// Das Zusammenfassen von Dateien zu Projekten ist geraten - aus einem Namen laesst
/// sich nicht beweisen, was zusammengehoert. Genau deshalb steht die Regel hier als
/// Liste von Faellen: Was die Vermutung tut, ist nachlesbar, und wenn sich die Regel
/// aendert, faellt hier auf, welche Faelle dabei ihr Verhalten wechseln.
///
/// Zwei Faelle sind keine Geschmacksfrage, sondern schlicht falsch, wenn sie kippen:
/// eine Sicherung, die in einem eigenen Projekt landet, und ein Name, der nur aus
/// Ziffern besteht und in seine erste Ziffer zerlegt wird.
/// </summary>
public static class ProjectInvariants
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Eine Datei, so wie der Scanner sie spaeter baut.</summary>
    private static BlendVersion Entry(string name, int minutes = 0)
    {
        var (_, number, backup, autosave) = BlendProjects.Split(name);

        return new BlendVersion($@"C:\szenen\{name}", name, number, backup, autosave,
                                Start.AddMinutes(minutes), 4096);
    }

    public static void Run()
    {
        Check.Group("Projekte - was zu einer Blender-Datei gehoert");

        // Was ueberhaupt eine Blender-Datei ist.
        Check.That(BlendProjects.IsBlendFile(@"C:\a\kitchen.blend"), ".blend wird erkannt");
        Check.That(BlendProjects.IsBlendFile(@"C:\a\KITCHEN.BLEND"), "auch gross geschrieben");
        Check.That(BlendProjects.IsBlendFile(@"C:\a\kitchen.blend1"), "Blenders Sicherung .blend1 gehoert dazu");
        Check.That(BlendProjects.IsBlendFile(@"C:\a\kitchen.blend23"), "und auch .blend23");
        Check.That(!BlendProjects.IsBlendFile(@"C:\a\frame.png"), "ein Bild nicht");
        Check.That(!BlendProjects.IsBlendFile(@"C:\a\notiz.blender"), ".blender ist keine Blender-Datei");
        Check.That(!BlendProjects.IsBlendFile(@"C:\a\notiz.blendx"), ".blendx auch nicht");

        // Die Zerlegung eines Namens.
        Check.That(BlendProjects.Split("kitchen.blend") == ("kitchen", null, null, false),
                   "eine Datei ohne Bezifferung");

        Check.That(BlendProjects.Split("kitchen_v2.blend") == ("kitchen", 2, null, false), "kitchen_v2 ist v2");
        Check.That(BlendProjects.Split("kitchen V03.blend") == ("kitchen", 3, null, false), "kitchen V03 auch");
        Check.That(BlendProjects.Split("kitchen-2.blend") == ("kitchen", 2, null, false), "kitchen-2 ebenso");
        Check.That(BlendProjects.Split("kitchen_003.blend") == ("kitchen", 3, null, false), "und kitchen_003");
        Check.That(BlendProjects.Split("Kitchen.blend").Key == "kitchen",
                   "Gross- und Kleinschreibung trennt nicht");

        Check.That(BlendProjects.Split("cam1.blend").Key == BlendProjects.Split("cam2.blend").Key,
                   "cam1 und cam2 landen im selben Projekt");

        // Der Fall, der schlicht falsch waere: Eine Sicherung traegt ihre Nummer in
        // der Endung, ihr Name ist unveraendert der des Originals.
        Check.That(BlendProjects.Split("kitchen.blend1") == ("kitchen", null, 1, false),
                   "eine Sicherung gehoert zum Original");

        Check.That(BlendProjects.Split("kitchen_v2.blend1") == ("kitchen", 2, 1, false),
                   "auch die Sicherung einer bezifferten Fassung");

        // Und der zweite: Ein Name aus lauter Ziffern IST der Name.
        Check.That(BlendProjects.Split("001.blend") == ("001", null, null, false),
                   "001.blend ist ein Projekt namens 001, nicht Fassung 1 von 0");

        Check.That(BlendProjects.Split("001.blend").Key != BlendProjects.Split("002.blend").Key,
                   "und 002.blend ist ein anderes");

        // Und der dritte Fall, der aus echten Daten kam: In einem Projektordner lagen
        // 90 Autosicherungen gegen 28 Fassungen, und jede einzelne stand als eigenes
        // Projekt in der Liste. Blender haengt Prozessnummer und _autosave an - und
        // sichert auch schon gesicherte Dateien wieder, daher die Ketten.
        Check.That(BlendProjects.Split("kiriko_12_14976_autosave.blend") == ("kiriko", 12, null, true),
                   "eine Autosicherung gehoert zu ihrer Fassung");

        Check.That(BlendProjects.Split("kiriko_12_14976_autosave_22073_autosave_15524_autosave.blend")
                   == ("kiriko", 12, null, true),
                   "auch die Autosicherung einer Autosicherung einer Autosicherung");

        Check.That(BlendProjects.Split("kiriko_28.blend").Key
                   == BlendProjects.Split("kiriko_13_15524_autosave.blend").Key,
                   "Fassung und Autosicherung landen im selben Projekt");

        Check.That(!BlendProjects.Split("kiriko_28.blend").IsAutosave,
                   "eine gewoehnliche Datei ist keine Autosicherung");

        // "autosave" ohne alles davor ist ein Dateiname, kein Anhaengsel.
        Check.That(BlendProjects.Split("autosave.blend").Key == "autosave",
                   "eine Datei, die nur autosave heisst, behaelt ihren Namen");

        Check.Group("Projekte - zusammenfassen und sortieren");

        var kitchen = new[]
        {
            Entry("kitchen.blend", 0),
            Entry("kitchen_v2.blend", 10),
            Entry("Kitchen_v3.blend", 20),
            Entry("kitchen.blend1", 15),
            Entry("kitchen.blend2", 5),
        };

        var projects = BlendProjects.Group(kitchen);

        Check.That(projects.Count == 1, "fuenf Dateien, ein Projekt", $"{projects.Count} Projekte");

        var one = projects[0];

        Check.That(one.Versions.Count == 5, "alle fuenf sind drin", one.Versions.Count.ToString());
        Check.That(one.VersionCount == 3, "gezaehlt werden nur die echten Fassungen",
                   one.VersionCount.ToString());

        Check.That(one.Name == "kitchen", "der Name kommt aus der ersten Datei", one.Name);
        Check.That(one.Newest?.FileName == "Kitchen_v3.blend", "die hoechste Fassung ist die juengste",
                   one.Newest?.FileName);

        Check.That(one.Versions[0].FileName == "Kitchen_v3.blend", "v3 steht oben", one.Versions[0].FileName);
        Check.That(one.Versions[1].FileName == "kitchen_v2.blend", "dann v2", one.Versions[1].FileName);
        Check.That(one.Versions[2].FileName == "kitchen.blend", "die Datei ohne Nummer gilt als die aelteste",
                   one.Versions[2].FileName);

        Check.That(one.Versions[3].FileName == "kitchen.blend1", "Sicherungen kommen hinten - .blend1 zuerst",
                   one.Versions[3].FileName);
        Check.That(one.Versions[4].FileName == "kitchen.blend2", "und .blend2 danach",
                   one.Versions[4].FileName);

        Check.That(one.TouchedUtc == Start.AddMinutes(20), "das Projekt ist so jung wie seine juengste Datei");

        // Ein Projekt mit Autosicherungen: Sie gehoeren dazu, zaehlen aber nicht als
        // Fassung und stehen hinten - hinter den Sicherungen.
        var withAuto = BlendProjects.Group(new[]
        {
            Entry("kiriko_28.blend", 100),
            Entry("kiriko_27.blend", 50),
            Entry("kiriko.blend1", 60),
            Entry("kiriko_12_14976_autosave.blend", 80),
            Entry("kiriko_11_23672_autosave.blend", 20),
        });

        Check.That(withAuto.Count == 1, "alles zusammen ein Projekt", withAuto.Count.ToString());

        var kiriko = withAuto[0];

        Check.That(kiriko.VersionCount == 2, "zwei echte Fassungen", kiriko.VersionCount.ToString());
        Check.That(kiriko.AutosaveCount == 2, "zwei Autosicherungen", kiriko.AutosaveCount.ToString());
        Check.That(kiriko.Newest?.FileName == "kiriko_28.blend", "die juengste Fassung ist keine Sicherung",
                   kiriko.Newest?.FileName);

        Check.That(kiriko.Versions[2].FileName == "kiriko.blend1", "die Sicherung kommt nach den Fassungen",
                   kiriko.Versions[2].FileName);
        Check.That(kiriko.Versions[3].IsAutosave && kiriko.Versions[4].IsAutosave,
                   "und die Autosicherungen ganz hinten");
        Check.That(kiriko.Versions[3].FileName == "kiriko_12_14976_autosave.blend",
                   "bei ihnen zaehlt das Datum, nicht die Zahl im Namen", kiriko.Versions[3].FileName);

        Check.That(Entry("kiriko_12_14976_autosave.blend").Label == "Autosicherung zu v12",
                   "eine Autosicherung nennt ihre Fassung",
                   Entry("kiriko_12_14976_autosave.blend").Label);

        // Beschriftung - was auf der Fassung steht.
        Check.That(Entry("kitchen_v2.blend").Label == "v2", "eine Fassung heisst v2");
        Check.That(Entry("kitchen.blend").Label == "ohne Nummer", "eine ohne Nummer sagt das");
        Check.That(Entry("kitchen.blend1").Label == "Sicherung 1", "eine Sicherung heisst so");
        Check.That(Entry("kitchen_v2.blend1").Label == "Sicherung 1 zu v2", "und nennt ihre Fassung");

        // Die Reihenfolge der Projekte: zuletzt angefasst zuerst.
        var mixed = BlendProjects.Group(new[]
        {
            Entry("alt.blend", 0),
            Entry("neu.blend", 100),
            Entry("mittel.blend", 50),
        });

        Check.That(mixed.Count == 3, "drei Namen, drei Projekte", mixed.Count.ToString());
        Check.That(mixed[0].Name == "neu", "das zuletzt angefasste steht oben", mixed[0].Name);
        Check.That(mixed[2].Name == "alt", "das aelteste unten", mixed[2].Name);

        Check.Group("Projekte - die Vermutung von Hand korrigieren");

        // Der eine Fall: zwei Dateien wurden zusammengefasst, die nicht zusammen
        // gehoeren. cam1 und cam2 koennen zwei Fassungen sein - oder zwei Kameras.
        var cams = new[] { Entry("cam1.blend", 0), Entry("cam2.blend", 10) };

        Check.That(BlendProjects.Group(cams).Count == 1, "geraten: cam1 und cam2 sind ein Projekt");

        var split = BlendProjects.Group(cams, new Dictionary<string, string>
        {
            [@"C:\szenen\cam2.blend"] = "Kamera 2",
        });

        Check.That(split.Count == 2, "von Hand getrennt sind es zwei", split.Count.ToString());
        Check.That(split.Any(p => p.Name == "Kamera 2"), "und das Getrennte heisst, wie es getippt wurde");

        // Der andere: eine Datei heisst anders und steht deshalb allein.
        var stray = new[] { Entry("kitchen_v2.blend", 0), Entry("kueche final.blend", 10) };

        Check.That(BlendProjects.Group(stray).Count == 2, "geraten: zwei Projekte");

        var joined = BlendProjects.Group(stray, new Dictionary<string, string>
        {
            [@"C:\szenen\kueche final.blend"] = "kitchen",
        });

        Check.That(joined.Count == 1, "von Hand zusammengelegt ist es eines", joined.Count.ToString());
        Check.That(joined[0].Versions.Count == 2, "mit beiden Dateien");

        // Eine Zuordnung, die nichts sagt, aendert nichts.
        var empty = BlendProjects.Group(stray, new Dictionary<string, string>
        {
            [@"C:\szenen\kueche final.blend"] = "   ",
        });

        Check.That(empty.Count == 2, "eine leere Zuordnung wird nicht angewandt", empty.Count.ToString());

        // Und eine Zuordnung fuer eine Datei, die gar nicht dabei ist, stoert nicht.
        var unrelated = BlendProjects.Group(stray, new Dictionary<string, string>
        {
            [@"C:\woanders\egal.blend"] = "kitchen",
        });

        Check.That(unrelated.Count == 2, "eine Zuordnung fuer eine fremde Datei bleibt folgenlos");

        Check.That(BlendProjects.Group(Array.Empty<BlendVersion>()).Count == 0,
                   "keine Dateien, keine Projekte");

        Structure();
        Disk();
    }

    /// <summary>
    /// Der Ordner schlaegt den Namen.
    ///
    /// Das kam aus echten Daten: In einem Projektordner lagen acht Dateien, die alle
    /// anders hiessen - tracer_al_8_3e_cage_18_2RED neben tracer_al_8_3e - und die
    /// Namensregel machte daraus acht Projekte. Wer seine Arbeit in Ordner sortiert,
    /// hat die Frage aber schon beantwortet, und ein Programm, das sich darueber
    /// hinwegsetzt, hat unrecht, auch wenn seine Regel schluessig ist.
    /// </summary>
    private static void Structure()
    {
        Check.Group("Projekte - der Ordner entscheidet");

        // Erst die Rechnung ohne Platte: welcher Ordner ein Projekt ausmacht.
        Check.That(ProjectScanner.ProjectFolderFor(@"C:\p", @"C:\p\TRACER\a.blend") == @"C:\p\TRACER",
                   "ein Ordner unterhalb des durchsuchten ist das Projekt");

        Check.That(ProjectScanner.ProjectFolderFor(@"C:\p", @"C:\p\TRACER\beatch\a.blend") == @"C:\p\TRACER",
                   "auch aus zweiter Ebene zaehlt der oberste Ordner");

        Check.That(ProjectScanner.ProjectFolderFor(@"C:\p", @"C:\p\a.blend") == "",
                   "was lose im durchsuchten Ordner liegt, hat keinen Projektordner");

        // Und die Gruppierung mit dieser Angabe.
        var inFolder = BlendProjects.Group(new[]
        {
            Entry("tracer_al_8_3e.blend", 0) with { ProjectFolder = @"C:\p\TRACER" },
            Entry("tracer_al_8_3e_cage_18_2RED.blend", 10) with { ProjectFolder = @"C:\p\TRACER" },
            Entry("ganz_anders.blend", 20) with { ProjectFolder = @"C:\p\TRACER" },
        });

        Check.That(inFolder.Count == 1, "drei verschiedene Namen, ein Ordner, ein Projekt",
                   inFolder.Count.ToString());

        Check.That(inFolder[0].Name == "TRACER", "und das Projekt heisst wie der Ordner", inFolder[0].Name);
        Check.That(inFolder[0].Folder == @"C:\p\TRACER", "es kennt seinen Ordner", inFolder[0].Folder);

        // Der umgekehrte Fall bleibt, wie er war: ohne Ordner zaehlt der Name.
        var loose = BlendProjects.Group(new[]
        {
            Entry("kitchen.blend", 0),
            Entry("kitchen_v2.blend", 10),
            Entry("ganz_anders.blend", 20),
        });

        Check.That(loose.Count == 2, "lose nebeneinander entscheidet weiter der Name", loose.Count.ToString());

        // Zwei Ordner bleiben zwei Projekte, auch wenn die Dateien gleich heissen.
        var twoFolders = BlendProjects.Group(new[]
        {
            Entry("shot.blend", 0) with { ProjectFolder = @"C:\p\eins" },
            Entry("shot.blend", 10) with { ProjectFolder = @"C:\p\zwei" },
        });

        Check.That(twoFolders.Count == 2, "gleicher Name in zwei Ordnern sind zwei Projekte",
                   twoFolders.Count.ToString());

        // Und die Korrektur von Hand schlaegt auch den Ordner. Gezogen wird eine
        // Kachel auf eine andere, gespeichert wird deren Schluessel - genau so, wie
        // es die Oberflaeche tut.
        var apart = new[]
        {
            Entry("shot.blend", 0) with { Path = @"C:\p\eins\shot.blend", ProjectFolder = @"C:\p\eins" },
            Entry("shot.blend", 10) with { Path = @"C:\p\zwei\shot.blend", ProjectFolder = @"C:\p\zwei" },
        };

        string targetKey = BlendProjects.Group(apart).First(p => p.Name == "eins").Key;

        var merged = BlendProjects.Group(apart,
                                         new Dictionary<string, string> { [@"C:\p\zwei\shot.blend"] = targetKey });

        Check.That(merged.Count == 1, "von Hand zusammengelegt schlaegt den Ordner", merged.Count.ToString());
        Check.That(merged[0].Name == "eins", "und das Ziel behaelt seinen Namen", merged[0].Name);
        Check.That(merged[0].Versions.Count == 2, "mit beiden Dateien");

        // Ein Ziel, das es nicht gibt, macht ein eigenes Projekt auf - so wird eine
        // Fassung wieder herausgeloest.
        var detached = BlendProjects.Group(apart,
                                           new Dictionary<string, string>
                                           {
                                               [@"C:\p\zwei\shot.blend"] = "Kamera 2",
                                           });

        Check.That(detached.Count == 2, "ein freier Name bleibt ein eigenes Projekt", detached.Count.ToString());
        Check.That(detached.Any(p => p.Name == "Kamera 2"), "und heisst, wie er eingetippt wurde");
    }

    /// <summary>
    /// Der Teil, der die Platte anfasst - an einem angelegten Ordner geprueft.
    ///
    /// Ohne echte Dateien waere das Wesentliche nicht geprueft: dass .blend1
    /// mitgenommen wird, dass eine Textdatei liegen bleibt, und dass ein Ordner, den
    /// es nicht gibt, nichts umwirft statt zu werfen.
    /// </summary>
    private static void Disk()
    {
        Check.Group("Projekte - auf der Platte");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-projekte-" + Guid.NewGuid().ToString("N")[..8]);
        string outside = root + "-woanders";
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(Path.Combine(root, "render", "cam1"));
            Directory.CreateDirectory(Path.Combine(root, "render", "cam2"));
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));

            Write(root, "kitchen.blend");
            Write(root, "kitchen_v2.blend");
            Write(root, "kitchen.blend1");
            Write(root, "notizen.txt");
            Write(outside, "strasse.blend");

            Write(Path.Combine(root, "sub", "deep"), "keller.blend");

            // Eine heruntergeladene Materialbibliothek im Projekt - kein Projekt.
            Directory.CreateDirectory(Path.Combine(root, "assets", "materials"));
            Write(Path.Combine(root, "assets", "materials"), "black-fabric_2K_57b8c197.blend");
            Write(Path.Combine(root, "render", "cam1"), "cam1_0001.png");
            Write(Path.Combine(root, "render", "cam1"), "cam1_0002.png");
            Write(Path.Combine(root, "render", "cam2"), "cam2_0001.png");

            // Suchen.
            var found = ProjectScanner.Walk(root).ToList();

            Check.That(found.Count == 4, "vier Blender-Dateien im Baum", found.Count.ToString());
            Check.That(found.Any(p => p.EndsWith("kitchen.blend1", StringComparison.OrdinalIgnoreCase)),
                       "die Sicherung kommt mit");
            Check.That(!found.Any(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)),
                       "eine Textdatei nicht");
            Check.That(found.Any(p => p.EndsWith("keller.blend", StringComparison.OrdinalIgnoreCase)),
                       "auch zwei Ebenen tiefer wird gesucht");

            Check.That(!found.Any(p => p.Contains("black-fabric", StringComparison.OrdinalIgnoreCase)),
                       "was in assets liegt, ist Material und kein Projekt");

            // Der Gegenfall: Wer den Materialordner selbst eintraegt, meint ihn auch.
            Check.That(ProjectScanner.Walk(Path.Combine(root, "assets")).Any(),
                       "ein ausdruecklich eingetragener assets-Ordner wird durchsucht");

            Check.That(ProjectScanner.Children(root).Any(t => t.Name == "assets"),
                       "und als Ordner ist er weiterhin zu sehen");

            Check.That(!ProjectScanner.Walk(Path.Combine(root, "gibtsnicht")).Any(),
                       "ein Ordner, den es nicht gibt, liefert nichts - und wirft nicht");

            Check.That(ProjectScanner.Describe(Path.Combine(root, "gibtsnicht.blend")) is null,
                       "eine Datei, die es nicht gibt, ist null");

            var described = ProjectScanner.Describe(Path.Combine(root, "kitchen_v2.blend"));

            Check.That(described?.Number == 2, "die beschriebene Datei kennt ihre Fassung");
            Check.That(described?.Bytes > 0, "und ihre Groesse");

            // Ordner als Kacheln.
            var top = ProjectScanner.Children(root);

            Check.That(top.Count == 3, "drei Ordner im Projekt", top.Count.ToString());
            Check.That(top.Any(t => t.Name == "render"), "render ist dabei");

            var renders = ProjectScanner.Children(Path.Combine(root, "render"));

            Check.That(renders.Count == 2, "cam1 und cam2", renders.Count.ToString());
            Check.That(renders[0].Images == 2, "cam1 hat zwei Bilder - und steht deshalb oben",
                       renders[0].Images.ToString());
            Check.That(renders[0].Thumbnail is not null, "und ein Vorschaubild");
            Check.That(renders.All(t => t.Folders == 0), "tiefer geht es dort nicht");

            var frames = ProjectScanner.Images(Path.Combine(root, "render", "cam1"));

            Check.That(frames.Count == 2, "zwei Frames", frames.Count.ToString());
            Check.That(Path.GetFileName(frames[0]) == "cam1_0001.png", "nach Namen sortiert",
                       Path.GetFileName(frames[0]));

            Check.That(ProjectScanner.Thumbnail(Path.Combine(root, "render"))?.EndsWith("cam1_0002.png") == true,
                       "das Vorschaubild kommt aus dem Unterordner - und ist das letzte der Reihe",
                       ProjectScanner.Thumbnail(Path.Combine(root, "render")));

            Check.That(ProjectScanner.Thumbnail(Path.Combine(root, "sub")) is null,
                       "ein Ordner ohne Bilder hat keines");

            // Merken und wiederfinden.
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

            ProjectLibrary.AddFolder(root);
            ProjectLibrary.AddFolder(root);
            ProjectLibrary.Note(Path.Combine(outside, "strasse.blend"));
            ProjectLibrary.Note(Path.Combine(outside, "keine.txt"));

            var known = ProjectLibrary.Load();

            Check.That(known.Folders.Count == 1, "derselbe Ordner wird nicht zweimal eingetragen",
                       known.Folders.Count.ToString());
            Check.That(known.Files.Count == 1, "und eine Textdatei wird nicht gemerkt",
                       known.Files.Count.ToString());

            var projects = ProjectLibrary.Scan();

            Check.That(projects.Count == 3, "kitchen, keller, strasse", projects.Count.ToString());
            Check.That(projects.Any(p => p.Key == "strasse"),
                       "die einzeln gemerkte Datei ist dabei, obwohl sie ausserhalb liegt");

            // Der Ordner reicht bis hierher durch: keller.blend liegt in sub\deep und
            // gehoert deshalb zum Projekt "sub" - nicht zu einem Projekt "keller".
            Check.That(projects.Any(p => p.Name == "sub" && p.Versions.Any(v => v.FileName == "keller.blend")),
                       "eine Datei im Unterordner gehoert zum Ordnerprojekt",
                       string.Join(", ", projects.Select(p => p.Name)));

            var kitchen = projects.FirstOrDefault(p => p.Key == "kitchen");

            Check.That(kitchen?.Versions.Count == 3, "kitchen hat drei Dateien",
                       kitchen?.Versions.Count.ToString());
            Check.That(kitchen?.VersionCount == 2, "davon zwei echte Fassungen");

            // Von Hand zuordnen - und es bleibt so.
            ProjectLibrary.Assign(Path.Combine(outside, "strasse.blend"), "kitchen");

            Check.That(ProjectLibrary.Scan().Count == 2, "nach der Zuordnung sind es zwei Projekte",
                       ProjectLibrary.Scan().Count.ToString());

            ProjectLibrary.Assign(Path.Combine(outside, "strasse.blend"), "");

            Check.That(ProjectLibrary.Scan().Count == 3, "und aufgehoben wieder drei",
                       ProjectLibrary.Scan().Count.ToString());

            ProjectLibrary.RemoveFolder(root);

            Check.That(ProjectLibrary.Scan().Count == 1,
                       "ohne den Ordner bleibt nur die einzeln gemerkte Datei",
                       ProjectLibrary.Scan().Count.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);

            try { Directory.Delete(root, true); } catch (Exception) { }
            try { Directory.Delete(outside, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Die Reihenfolge, in der Bilder ein Film sind.
    ///
    /// Alphabetisch waere frame_10 der Nachbar von frame_1, und der Film sprunge von
    /// 1 auf 10 auf 100, bevor er die 2 zeigt. Das sieht nicht aus wie eine falsche
    /// Sortierung, sondern wie ein kaputter Render - deshalb steht es hier.
    /// </summary>
    public static void Frames()
    {
        Check.Group("Frames - die Reihenfolge eines Films");

        var mixed = new List<string>
        {
            "frame_10.png", "frame_2.png", "frame_1.png", "frame_100.png", "frame_20.png",
        };

        mixed.Sort(FrameSequence.Order);

        Check.That(string.Join(" ", mixed) == "frame_1.png frame_2.png frame_10.png frame_20.png frame_100.png",
                   "ungepolsterte Nummern laufen der Zahl nach, nicht dem Buchstaben",
                   string.Join(" ", mixed));

        var padded = new List<string> { "0010.png", "0002.png", "0001.png" };

        padded.Sort(FrameSequence.Order);

        Check.That(string.Join(" ", padded) == "0001.png 0002.png 0010.png",
                   "gepolsterte auch - da faellt beides zusammen", string.Join(" ", padded));

        // Fuehrende Nullen sind keine Zahl: 0001 und 1 sind derselbe Frame.
        Check.That(FrameSequence.Compare("cam_0007.png", "cam_7.png") != 0
                   && FrameSequence.Compare("cam_0007.png", "cam_8.png") < 0,
                   "0007 zaehlt als sieben und kommt vor der acht");

        // Verschiedene Renderreihen im selben Ordner bleiben beieinander.
        var two = new List<string> { "b_2.png", "a_10.png", "b_1.png", "a_9.png" };

        two.Sort(FrameSequence.Order);

        Check.That(string.Join(" ", two) == "a_9.png a_10.png b_1.png b_2.png",
                   "zwei Reihen im selben Ordner vermischen sich nicht", string.Join(" ", two));

        // Eine Zahl, die in keinen Zahlentyp passt, ist trotzdem eine Zahl.
        string huge = new string('9', 40);

        Check.That(FrameSequence.Compare($"f_{huge}.png", "f_1.png") > 0,
                   "auch eine vierzigstellige Nummer wird verglichen, nicht geparst");

        // Zwei Namen, die nach dieser Ordnung gleich sind, tauschen nicht die Plaetze.
        Check.That(FrameSequence.Compare("0001.png", "1.png") == FrameSequence.Compare("0001.png", "1.png")
                   && FrameSequence.Compare("0001.png", "1.png") != 0,
                   "und gleichwertige Namen bekommen trotzdem eine feste Ordnung");
    }

    private static void Write(string directory, string name)
        => File.WriteAllText(Path.Combine(directory, name), "x");
}
