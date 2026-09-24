using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Live-Verhalten am echten, unsichtbaren Dashboard mit eigenen Testbildern.</summary>
public static class DashboardLiveInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Check.Group("Dashboard - wachsende Sequenz, Follow und Bereich");
        string root = Path.Combine(Path.GetTempPath(), "frameflip-dashboard-" + Guid.NewGuid().ToString("N"));
        string first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        string? previousConfig = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        var previousContext = SynchronizationContext.Current;
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        MainWindow? window = null;
        try
        {
            WriteFrame(first, 1);
            WriteFrame(first, 3);
            WriteFrame(second, 11);
            WriteFrame(second, 12);
            window = new MainWindow(null, () => null, () => { }, _ => { }, () => { },
                new AppSettings { Prebuffer = false });
            Check.That((bool)Call(window, "OpenPath", Frame(first, 1))!, "eine eigene Testsequenz wird geoeffnet");
            Check.That(Sequence(window).Count == 2 && Field<int>(window, "_head") == 1,
                "Oeffnen beginnt beim ersten vorhandenen Frame");
            Check.That(Field<IReadOnlyList<int>>(window, "_missing").SequenceEqual(new[] { 2 })
                       && Field<int>(window, "_outPoint") == 3, "Luecken und Bereich folgen der Ausgangssequenz");

            WriteFrame(first, 4);
            PumpFor(100);
            Check.That(Sequence(window).Count == 2, "Dateimeldungen lesen den Ordner nicht sofort neu ein");
            PumpUntil(() => Sequence(window).Count == 3);
            Check.That(Field<int>(window, "_head") == 4 && Field<int>(window, "_outPoint") == 4,
                "Follow und ein offenes Bereichsende wachsen mit dem Render");

            Set(window, "_follow", false);
            Call(window, "ShowFrame", 1);
            Set(window, "_outPoint", 3);
            WriteFrame(first, 5);
            PumpUntil(() => Sequence(window).Count == 4);
            Check.That(Field<int>(window, "_head") == 1 && Field<int>(window, "_outPoint") == 3,
                "ohne Follow bleiben Kopf und ein von Hand gesetztes Ende erhalten");

            Set(window, "_follow", true);
            Set(window, "_playing", true);
            WriteFrame(first, 6);
            PumpUntil(() => Sequence(window).Count == 5);
            Check.That(Field<int>(window, "_head") == 1,
                "waehrend Playback springt auch eingeschaltetes Follow nicht ans Ende");
            Set(window, "_playing", false);
            WriteFrame(first, 2);
            PumpUntil(() => Sequence(window).Count == 6);
            Check.That(Field<IReadOnlyList<int>>(window, "_missing").Count == 0
                       && Field<int>(window, "_head") == 1,
                "eine gefuellte Luecke aktualisiert die Folge ohne Follow-Sprung");

            WriteFrame(first, 7);
            Check.That((bool)Call(window, "OpenPath", Frame(second, 11))!, "eine zweite Folge ersetzt die Auswahl");
            PumpFor(750);
            Check.That(Sequence(window).Count == 2 && Sequence(window).StartNumber == 11
                       && Field<int>(window, "_head") == 11,
                "Meldungen der alten Folge veraendern die neue Auswahl nicht");

            File.Delete(Frame(second, 11));
            WriteFrame(second, 13);
            PumpUntil(() => Sequence(window).EndNumber == 13);
            Check.That(Sequence(window).StartNumber == 12 && Sequence(window).Count == 2,
                "nach Entfernen des Seeds wird ein anderer Frame im gewaehlten Ordner gefunden");
            Check.That(Field<int>(window, "_inPoint") == 12 && Field<int>(window, "_outPoint") == 13,
                "Start und offenes Ende werden an die verbliebene Folge angepasst");

            var before = Sequence(window);
            Call(window, "OnFrameWritten", Frame(second, 13));
            PumpFor(750);
            Check.That(ReferenceEquals(before, Sequence(window)),
                "eine doppelte Brueckenmeldung ersetzt die unveraenderte Folge nicht");
        }
        finally
        {
            window?.Close();
            try
            {
                bool removed = false;
                PumpUntil(() =>
                {
                    if (removed) return true;
                    try { Directory.Delete(root, recursive: true); removed = true; return true; }
                    catch (IOException) { return false; }
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previousConfig);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }

    private static string Frame(string folder, int number) => Path.Combine(folder, $"frame_{number:0000}.png");
    private static void WriteFrame(string folder, int number)
    {
        var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Frame(folder, number));
        encoder.Save(stream);
    }
    private static ImageSequence Sequence(MainWindow window) => Field<ImageSequence>(window, "_sequence");
    private static T Field<T>(MainWindow window, string name) => (T)DashboardSelectionInvariants.Read(window, name)!;
    private static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Hidden)!.SetValue(window, value);
    private static object? Call(MainWindow window, string name, params object[] args)
        => typeof(MainWindow).GetMethod(name, Hidden)!.Invoke(window, args);
    private static void PumpFor(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        PumpUntil(() => watch.ElapsedMilliseconds >= milliseconds);
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        if (!ready()) throw new TimeoutException("Das Dashboard hat seine Live-Folge nicht aktualisiert.");
    }
}
