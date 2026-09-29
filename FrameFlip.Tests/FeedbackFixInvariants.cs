using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Rueckmeldungen aus dem Test vom 29. September: Die Maske liess sich nicht umkehren - der Knopf
/// hiess "±" und lag in einem zugeklappten Abschnitt -, und "Original" zeigte das Original nur,
/// solange man den Knopf hielt.
/// </summary>
public static class FeedbackFixInvariants
{
    public static void Run()
    {
        WithPage((page, layers, tools) =>
        {
            Check.Group("Maske: Umkehren zu finden");

            var body = (FrameworkElement)layers.FindName("MaskBody");
            var invert = (ToggleButton)layers.FindName("MaskInvertButton");

            Check.That(body.Visibility != Visibility.Visible, "ohne Maske bleibt ihr Abschnitt zu");

            layers.Stack.Layers[0].Mask.Kind = MaskKind.Luminance;
            layers.ShowThumbnails();
            Pump(() => false, 0.2);

            Check.That(body.Visibility == Visibility.Visible && invert.Content as string == Strings.T("S_MaskInvertShort") &&
                       Strings.T("S_MaskInvertShort") == "Umkehren",
                       "mit Maske steht ihr Abschnitt offen, der Knopf heisst \"Umkehren\"");

            invert.IsChecked = true;
            Pump(() => false, 0.1);
            Check.That(layers.Stack.Layers[0].Mask.Invert, "und er kehrt die Maske um");

            Check.Group("Original: ein Klick bleibt stehen");

            var compare = (Button)page.FindName("CompareButton");
            var badge = (FrameworkElement)page.FindName("CompareBadge");

            // Ein kurzer Klick mit der Maus: druecken, gleich loslassen.
            Press(compare, down: true);
            Check.That(page.ShowingOriginal && badge.Visibility == Visibility.Visible, "beim Druecken sofort das Original, mit Abzeichen");
            Press(compare, down: false);
            Pump(() => false, 0.1);
            Check.That(page.ShowingOriginal && page.CompareLatched, "kurz geklickt: das Original bleibt stehen");

            Press(compare, down: true);
            Press(compare, down: false);
            Pump(() => false, 0.1);
            Check.That(!page.ShowingOriginal && !page.CompareLatched && badge.Visibility != Visibility.Visible,
                       "der naechste Klick schaltet zurueck, das Abzeichen geht");

            // Gehalten: nur ein Blick.
            Press(compare, down: true);
            Pump(() => false, 0.45);
            Press(compare, down: false);
            Pump(() => false, 0.1);
            Check.That(!page.ShowingOriginal && !page.CompareLatched, "gedrueckt gehalten: nur ein Blick, danach wieder das Ergebnis");

            Check.That(page.HandleToolKey(Key.O) && page.ShowingOriginal && page.CompareLatched, "die Taste O schaltet ebenso");

            ((Slider)tools.FindName("ExposureSlider")).Value = 0.4;
            Pump(() => false, 0.3);
            Check.That(!page.ShowingOriginal && !page.CompareLatched && badge.Visibility != Visibility.Visible,
                       "eine Aenderung am Bild zeigt wieder das Ergebnis");

            compare.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(page.CompareLatched, "ohne Maus - Tastatur, Bedienhilfen - schaltet der Klick um");
            page.HandleToolKey(Key.O);
        });
    }

    private static void Press(UIElement element, bool down)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = down ? UIElement.PreviewMouseLeftButtonDownEvent : UIElement.PreviewMouseLeftButtonUpEvent,
        };
        element.RaiseEvent(args);
    }

    private static void WithPage(Action<AtelierPage, LayerPanel, GradingPanel> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "frameflip-feedback-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 64, H = 32;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            pixels[i * 4] = (byte)(i % W * 3);
            pixels[i * 4 + 1] = 110;
            pixels[i * 4 + 2] = 150;
            pixels[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            body(page, (LayerPanel)page.FindName("Layers"), (GradingPanel)page.FindName("Tools"));
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
