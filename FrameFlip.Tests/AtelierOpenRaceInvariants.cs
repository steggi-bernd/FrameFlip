using System.IO;
using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Refactoring-Studio, S0, Befund 3: Ein Oeffnen, das laenger liest als ein spaeteres,
/// darf dessen Bild nicht ueberschreiben - weder bei A, dann B, noch bei A, B, A, wo
/// der Pfad des alten Ergebnisses wieder stimmt.
/// </summary>
public static class AtelierOpenRaceInvariants
{
    public static void Run()
    {
        Check.Group("Atelier: ein spaet gelesenes Bild loest kein neueres ab");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-oeffnen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string a = Path.Combine(folder, "a.png");
        string b = Path.Combine(folder, "b.png");
        File.WriteAllBytes(a, Array.Empty<byte>());
        File.WriteAllBytes(b, Array.Empty<byte>());

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window
        {
            Content = page, Width = 900, Height = 600, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        FloatFrame? Shown() => (FloatFrame?)typeof(AtelierPage).GetField("_base", flags)!.GetValue(page);
        string? OpenPath() => (string?)typeof(AtelierPage).GetField("_path", flags)!.GetValue(page);

        // Jedes Lesen liefert ein erkennbares Bild: die Breite sagt, wessen Ergebnis es ist.
        var slow = new ManualResetEventSlim(false);
        int readsOfA = 0;

        page.Reader = path =>
        {
            int width;

            if (path == a)
            {
                // Das erste Lesen von A haelt an, bis die Probe es loslaesst.
                if (Interlocked.Increment(ref readsOfA) == 1)
                {
                    slow.Wait(TimeSpan.FromSeconds(10));
                    width = 41;
                }
                else
                {
                    width = 40;
                }
            }
            else
            {
                width = 60;
            }

            return (Frame(width), Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());
        };

        try
        {
            window.Show();

            // A (langsam), dann B.
            page.Open(a);
            page.Open(b);
            Pump(TimeSpan.FromSeconds(5), () => Shown()?.Width == 60);

            slow.Set();
            Pump(TimeSpan.FromSeconds(1), () => false);

            Check.That(Shown()?.Width == 60 && OpenPath() == b && settings.AtelierImage == b,
                       "A, dann B: B bleibt, obwohl A zuletzt fertig wird",
                       $"gezeigt {Shown()?.Width}, Pfad {Path.GetFileName(OpenPath())}, gemerkt {Path.GetFileName(settings.AtelierImage)}");

            // A (langsam), B, und wieder A: Das erste A kommt zuletzt, mit einem Pfad, der
            // wieder stimmt - und ist trotzdem das alte.
            slow.Reset();
            readsOfA = 0;

            page.Open(a);
            page.Open(b);
            page.Open(a);
            Pump(TimeSpan.FromSeconds(5), () => Shown()?.Width == 40);

            slow.Set();
            Pump(TimeSpan.FromSeconds(1), () => false);

            Check.That(Shown()?.Width == 40 && OpenPath() == a,
                       "A, B, A: das zweite A bleibt, das erste kommt zu spaet",
                       $"gezeigt {Shown()?.Width}");
        }
        finally
        {
            slow.Set();
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static FloatFrame Frame(int width) => new()
    {
        Width = width,
        Height = 30,
        R = new float[width * 30],
        G = new float[width * 30],
        B = new float[width * 30],
        IsSceneReferred = false,
    };

    private static void Pump(TimeSpan timeout, Func<bool> until)
    {
        var end = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }
}
