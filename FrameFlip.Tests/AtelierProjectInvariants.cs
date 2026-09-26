using System.Collections.Concurrent;
using System.IO;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Projektdateien je Sequenz (docs/Projekte-und-Masken.md, Punkt 9): welche Folge ein Bild
/// meint, wo das Projekt liegt, und dass Wechsel, Autosave und die einmalige Uebernahme
/// des bisherigen Rezepts nichts verlieren. Alles in eigenen Ordnern unter %TEMP%.
/// </summary>
public static class AtelierProjectInvariants
{
    public static void Run()
    {
        TheKeyIgnoresTheNumber();
        TheStoreWritesNextToTheSource();
        TheKeeperSwitchesAndSaves();
    }

    private static void TheKeyIgnoresTheNumber()
    {
        Check.Group("Projekte: welche Folge ein Bild meint");

        var a = SequenceKey.Of(@"C:\renders\shot\render_0047.exr")!;
        var b = SequenceKey.Of(@"C:\Renders\shot\render_0048.EXR")!;
        var c = SequenceKey.Of(@"C:\renders\shot\f_99.png")!;
        var d = SequenceKey.Of(@"C:\renders\shot\f_100.png")!;

        Check.That(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "Bild 47 und 48 derselben Folge sind ein Projekt - auch bei anderer Schreibweise");
        Check.That(c.Equals(d), "f_99 und f_100 auch - die Stellenzahl zaehlt nicht");
        Check.That(!a.Equals(SequenceKey.Of(@"C:\renders\shot\render_0047.png")), "eine andere Endung ist eine andere Folge");
        Check.That(!a.Equals(SequenceKey.Of(@"C:\renders\other\render_0047.exr")), "ein anderer Ordner auch");
        Check.That(a.Name == "render" && SequenceKey.Of(@"C:\x\splash.png")!.Name == "splash" &&
                   SequenceKey.Of(@"C:\x\shot-010_v2.0001.png")!.Name == "shot-010_v2",
                   "der Name ist die Folge ohne Nummer und Trennzeichen am Rand",
                   $"{a.Name}, {SequenceKey.Of(@"C:\x\shot-010_v2.0001.png")!.Name}");
    }

    private static void TheStoreWritesNextToTheSource()
    {
        Check.Group("Projekte: die Ablage");

        using var place = new Place();
        var store = new AtelierProjectStore(() => place.Fallback);
        var key = SequenceKey.Of(Path.Combine(place.Source, "render_0001.exr"))!;

        var mask = new LayerMask { Kind = MaskKind.Painted };
        var paint = mask.PaintOn(0, 64, 32);
        paint.Stroke(20, 10, 6, 1, 1);
        paint.Keep();

        var project = new AtelierProject
        {
            Frame = "render_0001.exr",
            Adjustments = new ImageAdjustments { Exposure = 0.7 },
            Grading = new GradingStack { Optics = { new VignetteTool { Amount = -0.4f } } },
            Layers = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Adjustment, Mask = mask } } },
            Nodes = System.Text.Json.Nodes.JsonNode.Parse("{\"Nodes\":[],\"Links\":[]}"),
        };

        Check.That(store.Save(key, project), "das Projekt wird geschrieben");

        string expected = Path.Combine(place.Source, "FrameFlip", "render.exr.ffproj");
        Check.That(File.Exists(expected) && store.LastWritten == expected, "in den Ordner FrameFlip neben den Bildern", store.LastWritten);

        string text = File.ReadAllText(expected);
        Check.That(text.Contains("\"Painted\"") && text.Contains("\"render_#.exr\""),
                   "Aufzaehlungen stehen als Namen darin, die Folge als Muster");

        var back = store.Load(key);
        Check.That(back is not null && back.Frame == "render_0001.exr" && Math.Abs(back.Adjustments!.Exposure - 0.7) < 1e-9 &&
                   back.Grading!.Optics.OfType<VignetteTool>().Single().Amount == -0.4f &&
                   back.Layers!.Layers[0].Mask.Kind == MaskKind.Painted &&
                   back.Layers.Layers[0].Mask.Paint!.Data == paint.Data && back.Nodes is not null,
                   "und kommt vollstaendig zurueck - Werkzeuge, Maskenart, Anstrich, Graph");

        // Laesst sich der Quellordner nicht beschreiben, dann unter den Einstellungen.
        string blocked = Path.Combine(place.Source, "gesperrt");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "FrameFlip"), "eine Datei, wo der Ordner hin muesste");
        var elsewhere = SequenceKey.Of(Path.Combine(blocked, "shot_0001.png"))!;

        Check.That(store.Save(elsewhere, new AtelierProject()) && store.LastWritten!.StartsWith(place.Fallback, StringComparison.OrdinalIgnoreCase) &&
                   store.Load(elsewhere) is not null,
                   "kein Ordner FrameFlip moeglich: unter den Einstellungen, und von dort gelesen", store.LastWritten);

        // Eine defekte Datei haelt das Oeffnen nicht auf.
        File.WriteAllText(expected, "{ kaputt");
        Check.That(store.Load(key) is null, "eine defekte Projektdatei liest sich als keine");
    }

    private static void TheKeeperSwitchesAndSaves()
    {
        Check.Group("Projekte: Wechsel, Autosave, Uebernahme");

        using var place = new Place();

        var settings = new AppSettings
        {
            Grading = new GradingStack { Optics = { new VignetteTool { Amount = -0.25f } } },
            Adjustments = new ImageAdjustments { Exposure = 0.3 },
        };

        var session = new AtelierEditingSession(new SettingsRecipeStore(settings));
        var store = new AtelierProjectStore(() => place.Fallback);

        // Die Pause und der Oberflaechenfaden unter Kontrolle der Probe.
        Action? pending = null;
        var posted = new ConcurrentQueue<Action>();
        void Drain() { while (posted.TryDequeue(out var action)) action(); }

        var keeper = new AtelierProjectKeeper(session, store, settings,
                                              (_, action) => { pending = action; return () => pending = null; },
                                              posted.Enqueue);

        string first = Path.Combine(place.Source, "render_0001.exr");
        string second = Path.Combine(place.Source, "shot", "shot_0001.png");
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);

        // Das erste Projekt ohne Datei bekommt das bisherige Rezept - einmal.
        Check.That(keeper.Enter(first), "eine neue Folge oeffnet ein Projekt");
        keeper.Flush();
        Drain();

        Check.That(session.Grading?.Optics.OfType<VignetteTool>().SingleOrDefault()?.Amount == -0.25f &&
                   settings.AtelierRecipeMoved && File.Exists(AtelierProjectStore.PrimaryPath(keeper.Current!)),
                   "das erste Projekt uebernimmt das Rezept aus den Einstellungen und steht sofort in einer Datei");

        // Eine Aenderung: nach der Ruhepause geschrieben.
        session.Adjustments = new ImageAdjustments { Exposure = 1.1 };
        Check.That(session.Dirty && pending is not null && keeper.State == AtelierSaveState.Unsaved,
                   "eine Aenderung ist ungespeichert, der Autosave wartet");

        pending!();
        keeper.Flush();
        Drain();

        Check.That(!session.Dirty && keeper.State == AtelierSaveState.Saved &&
                   Math.Abs(store.Load(keeper.Current!)!.Adjustments!.Exposure - 1.1) < 1e-9,
                   "nach der Ruhepause steht sie in der Datei");

        // Ein anderes Bild derselben Folge wechselt nichts, es wird nur gemerkt.
        Check.That(!keeper.Enter(Path.Combine(place.Source, "render_0002.exr")), "ein Bild derselben Folge wechselt kein Projekt");
        keeper.Flush();
        Drain();
        Check.That(keeper.RememberedFrame(keeper.Current!) == Path.Combine(place.Source, "render_0002.exr"),
                   "aber es wird als zuletzt gezeigtes gemerkt");

        // Eine andere Folge: das bisherige geschrieben, das neue frisch.
        session.Adjustments = new ImageAdjustments { Exposure = -0.5 };
        var firstKey = keeper.Current!;

        Check.That(keeper.Enter(second), "eine andere Folge wechselt das Projekt");
        keeper.Flush();
        Drain();

        Check.That(session.Adjustments is null && session.Grading is null && !session.Dirty,
                   "die neue Folge beginnt frisch - die Uebernahme gab es nur einmal");
        Check.That(Math.Abs(store.Load(firstKey)!.Adjustments!.Exposure + 0.5) < 1e-9 &&
                   store.Load(firstKey)!.Frame == "render_0002.exr",
                   "und das bisherige Projekt ist mit seiner letzten Aenderung geschrieben");

        // Zurueck: Das Rezept kommt aus der Datei.
        session.Layers = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Adjustment, Name = "zweite" } } };
        keeper.Enter(first);
        keeper.Flush();
        Drain();

        Check.That(Math.Abs(session.Adjustments!.Exposure + 0.5) < 1e-9 &&
                   session.Grading!.Optics.OfType<VignetteTool>().Single().Amount == -0.25f,
                   "zurueck zur ersten Folge steht ihr Rezept wieder da");
        Check.That(store.Load(SequenceKey.Of(second)!)!.Layers!.Layers.Single().Name == "zweite",
                   "und die zweite hat ihres behalten");
    }

    /// <summary>Ein Quellordner und ein Ausweichort unter %TEMP% - nie die Ordner des Nutzers.</summary>
    private sealed class Place : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "frameflip-projekt-" + Guid.NewGuid().ToString("N")[..8]);

        public Place()
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Fallback);
        }

        public string Source => Path.Combine(_root, "quelle");

        public string Fallback => Path.Combine(_root, "einstellungen", "projects");

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }
}
