using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Was nach dem Oeffnen eines Bildes im Atelier dasteht - lesbar, unlesbar, nach einem
/// Fehler erneut, und wenn das Lesen selbst eine Ausnahme wirft. Die Charakterisierung
/// vor dem Herausloesen der Quellsitzung (Refactoring-Studio, S1).
/// </summary>
public static class AtelierOpenInvariants
{
    public static void Run()
    {
        Check.Group("Atelier: was nach dem Oeffnen dasteht");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-oeffnen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string good = Path.Combine(folder, "gut.png");
        string bad = Path.Combine(folder, "kaputt.png");
        string throwing = Path.Combine(folder, "wirft.png");
        foreach (string file in new[] { good, bad, throwing }) File.WriteAllBytes(file, Array.Empty<byte>());

        var settings = new AppSettings();
        int persisted = 0;
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => persisted++);
        var window = new Window
        {
            Content = page, Width = 900, Height = 600, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        FloatFrame? Shown() => (FloatFrame?)typeof(AtelierPage).GetField("_base", flags)!.GetValue(page);
        T Named<T>(string name) where T : class => (T)page.FindName(name);

        page.Reader = path =>
        {
            if (path == throwing) throw new IOException("Lesefehler der Probe");

            return (path == good ? Frame(48) : null, Array.Empty<ExrPass>(), Array.Empty<CryptomatteSet>());
        };

        bool Idle() => Named<FrameworkElement>("BusyBadge").Visibility != Visibility.Visible;

        try
        {
            window.Show();

            page.Open(good);
            Check.That(!Idle(), "waehrend gelesen wird, steht das Zeichen dafuer da");
            Pump(() => Shown() is not null && Idle());

            Check.That(Shown()?.Width == 48 && Named<TextBlock>("FileText").Text == "gut.png" && settings.AtelierImage == good &&
                       persisted > 0 && Named<Button>("CompareButton").IsEnabled,
                       "ein lesbares Bild steht da, heisst wie die Datei und ist fuer den naechsten Start gemerkt");

            int before = persisted;
            page.Open(bad);
            Pump(() => Shown() is null && Idle());

            Check.That(Shown() is null && Named<TextBlock>("FileText").Text.Contains(Localization.Strings.T("S_CannotRead")) &&
                       Named<FrameworkElement>("EmptyHint").Visibility == Visibility.Visible &&
                       !Named<Button>("CompareButton").IsEnabled,
                       "ein unlesbares sagt es, und die Flaeche ist leer", Named<TextBlock>("FileText").Text);
            Check.That(settings.AtelierImage == good && persisted == before,
                       "und wird nicht als zuletzt geoeffnet gemerkt");

            page.Open(throwing);
            Pump(() => Idle());

            Check.That(Shown() is null && Named<TextBlock>("FileText").Text.Contains(Localization.Strings.T("S_CannotRead")),
                       "wirft das Lesen, gilt die Datei als unlesbar - ohne dass die Seite etwas davon merkt");

            page.Open(good);
            Pump(() => Shown() is not null && Idle());

            Check.That(Shown()?.Width == 48 && Named<FrameworkElement>("EmptyHint").Visibility != Visibility.Visible,
                       "nach einem Fehler oeffnet das naechste Bild wie immer");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static FloatFrame Frame(int width) => new()
    {
        Width = width,
        Height = 24,
        R = new float[width * 24],
        G = new float[width * 24],
        B = new float[width * 24],
        IsSceneReferred = false,
    };

    private static void Pump(Func<bool> until)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }
}
