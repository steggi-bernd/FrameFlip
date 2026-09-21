using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Projects;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

public static class DashboardSelectionInvariants
{
    public static void Run()
    {
        Check.Group("Dashboard - Bibliothek, Auswahl und Listen-Neuaufbau");
        using var h = new Harness();
        string empty = h.Folder("empty"), render = h.Folder("render"), manual = h.Folder("manual");
        string seed = h.Frame(render, 5);
        h.Frame(render, 7);
        string opened = h.Frame(manual, 20);
        string other = h.Frame(manual, 21);
        var missing = h.Known("missing", Path.Combine(h.Root, "missing"));
        var waiting = h.Known("waiting", empty);
        var known = h.Known("render", render, seed);
        h.Save(new KnownBlend { Path = "ignored.txt" }, missing, waiting, known);
        var window = h.Open();
        Check.That(h.Buttons.Length == 3, "nur Blender-Projekte erscheinen in Bibliotheksreihenfolge");
        Check.That(h.Current("BlendPath") == waiting.Path && h.Sequence is null,
            "die erste vorhandene Ausgabe wird auch als leerer Ordner ausgewaehlt");
        Check.That(h.Text("SequenceName") == "waiting.blend" && h.Text("StageEmpty") == Strings.T("D_StageNoFrames"),
            "ein wartender Render behaelt seinen Namen und erklaert die fehlenden Frames");
        h.PumpUntil(() => h.Entry(h.Buttons[2], "Frames") is 2);
        Check.That(h.Entry(h.Buttons[2], "Missing") is 1,
            "der Hintergrundscan zaehlt auch die nicht ausgewaehlte Folge samt Luecken");
        Check.That((bool)h.Call("Select", known.Path.ToUpperInvariant())!, "Projektpfade werden ohne Gross-/Kleinschreibung gefunden");
        Check.That(h.Sequence?.StartNumber == 5 && h.Sequence.Count == 2 && h.Buttons.Count(b => b.IsChecked == true) == 1,
            "die Auswahl setzt genau eine Zeile und die zugehoerige Sequenz");
        Set(window, "_playing", true);
        var same = h.Sequence;
        h.Call("Select", seed);
        Check.That(ReferenceEquals(same, h.Sequence) && Read(window, "_playing") is true,
            "die erneute Auswahl derselben Folge unterbricht die Wiedergabe nicht");
        Set(window, "_playing", false);
        Check.That(!(bool)h.Call("Select", "unknown")! && ReferenceEquals(same, h.Sequence),
            "ein unbekanntes Auswahlziel veraendert nichts");

        h.Save(known, waiting, missing);
        h.Call("ReloadSequencesKeepingSelection");
        Check.That(h.Current("BlendPath") == known.Path && h.Buttons[0].IsChecked == true,
            "nach Umordnung der Bibliothek bleibt dasselbe Projekt ausgewaehlt");
        h.Save(waiting, missing);
        h.Call("ReloadSequencesKeepingSelection");
        Check.That(h.Current("BlendPath") == waiting.Path && h.Sequence is null,
            "ein entferntes Projekt faellt auf die erste vorhandene Ausgabe zurueck");
        h.Call("Select", missing.Path);
        Check.That(h.Text("StageEmpty") == Strings.T("D_StageNoOutput"),
            "ein fehlender Ordner wird von einem noch leeren Ausgabeordner unterschieden");

        Check.That((bool)h.Call("OpenPath", opened)!, "ein Bild kann als eigene Sitzung geoeffnet werden");
        Check.That(h.Buttons.Length == 3 && h.Entry(h.Buttons[0], "Adhoc") is true
                   && h.Current("Folder") == manual, "manuell geoeffnete Folgen stehen vor der Bibliothek");
        Check.That((bool)h.Call("OpenPath", other)! && h.Buttons.Length == 3 && h.Current("Seed") == other,
            "erneutes Oeffnen desselben Ordners ersetzt dessen Sitzungseintrag");
        var recent = RecentSequences.Load().Single();
        Check.That(recent.Folder == manual && recent.Seed == other && recent.Count == 2,
            "der Verlauf beschreibt erst die erfolgreich gelesene Auswahl");
        same = h.Sequence;
        string text = Path.Combine(manual, "notes.txt");
        File.WriteAllText(text, "synthetic");
        Check.That(!(bool)h.Call("OpenPath", text)! && !(bool)h.Call("OpenPath", Path.Combine(manual, "absent.png"))!
                   && ReferenceEquals(same, h.Sequence), "ungueltige Dateiziele veraendern Auswahl und Liste nicht");
        h.Save(known);
        h.Call("ReloadSequencesKeepingSelection");
        Check.That(h.Current("Folder") == manual && h.Buttons[0].IsChecked == true,
            "eine manuelle Auswahl ueberlebt einen Bibliotheks-Neuaufbau");
        h.Call("Select", render);
        Check.That(h.Current("BlendPath") == known.Path, "auch der Ausgabeordner ist ein Auswahlziel");
    }

    internal static object? Read(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
           ?? target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class Harness : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "frameflip-selection-" + Guid.NewGuid().ToString("N"));
        private readonly string? _previousConfig = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        private readonly SynchronizationContext? _previousContext = SynchronizationContext.Current;
        private MainWindow? _window;
        internal Harness()
        {
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(Root, "config.json"));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        }
        internal MainWindow Open() => _window = new MainWindow(null, () => null, () => { }, _ => { }, () => { },
            new AppSettings { Prebuffer = false });
        internal string Folder(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        internal KnownBlend Known(string name, string folder, string seed = "")
            => new() { Path = Path.Combine(Root, name + ".blend"), Output = folder, Seed = seed };
        internal void Save(params KnownBlend[] files) => ProjectLibrary.Save(new ProjectKnowledge { Files = files.ToList() });
        internal ToggleButton[] Buttons => ((StackPanel)_window!.FindName("SequenceList")).Children.OfType<ToggleButton>().ToArray();
        internal ImageSequence? Sequence => Read(_window!, "_sequence") as ImageSequence;
        internal object? Entry(ToggleButton button, string name) => button.Tag.GetType().GetProperty(name)!.GetValue(button.Tag);
        internal string? Current(string name) => Read(_window!, "_current")?.GetType().GetProperty(name)!.GetValue(Read(_window!, "_current")) as string;
        internal string Text(string name) => ((TextBlock)_window!.FindName(name)).Text;
        internal object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic, args.Select(a => a.GetType()).ToArray())!.Invoke(_window, args);
        internal string Frame(string folder, int number)
        {
            string path = Path.Combine(folder, $"frame_{number:0000}.png");
            var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 64);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        internal void PumpUntil(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(1);
            }
            if (!ready()) throw new TimeoutException("Die Dashboard-Auswahl wurde nicht aktualisiert.");
        }
        public void Dispose()
        {
            _window?.Close();
            bool removed = false;
            try
            {
                PumpUntil(() =>
                {
                    if (removed) return true;
                    try { Directory.Delete(Root, true); removed = true; return true; }
                    catch (IOException) { return false; }
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", _previousConfig);
                SynchronizationContext.SetSynchronizationContext(_previousContext);
            }
        }
    }
}
