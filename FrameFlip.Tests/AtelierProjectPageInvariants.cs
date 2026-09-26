using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Projekte auf der Seite: Zwei Folgen im Wechsel behalten jede ihr Rezept, der
/// Knotenmodus richtet sich nach dem Projekt, und Speichern tut, was es sagt.
/// </summary>
public static class AtelierProjectPageInvariants
{
    public static void Run()
    {
        Check.Group("Projekte auf der Seite: jede Folge ihr Rezept");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-projektseite-" + Guid.NewGuid().ToString("N")[..8]);
        string first = Path.Combine(root, "eins", "render_0001.png");
        string firstNext = Path.Combine(root, "eins", "render_0002.png");
        string second = Path.Combine(root, "zwei", "shot_0001.png");

        foreach (string file in new[] { first, firstNext, second })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            WritePng(file);
        }

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var tools = (GradingPanel)page.FindName("Tools");
        var exposure = (Slider)tools.FindName("ExposureSlider");
        var save = (Button)page.FindName("SaveButton");
        var saved = (TextBlock)page.FindName("SaveText");

        void Show(string path)
        {
            string name = Path.GetFileName(path);
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("FileText")).Text == name && page.Recipe.Layers is not null &&
                       ((TextBlock)page.FindName("SourceText")).Text.Length > 0 && page.Projects.Current is { } key &&
                       key.Equals(SequenceKey.Of(path)));
            Pump(() => false, 0.2);
        }

        try
        {
            window.Show();
            Check.That(!save.IsEnabled, "ohne Bild gibt es nichts zu speichern");

            Show(first);
            exposure.Value = 0.8;

            Check.That(save.IsEnabled && page.Projects.State == AtelierSaveState.Unsaved,
                       "ein Regler an der ersten Folge: ungespeichert", saved.Text);

            // Ein anderes Bild derselben Folge: dasselbe Projekt, dasselbe Rezept.
            Show(firstNext);
            Check.Near(exposure.Value, 0.8, 1e-6, "ein anderes Bild derselben Folge behaelt das Rezept");

            // Eine andere Folge beginnt frisch.
            Show(second);
            Check.Near(exposure.Value, 0, 1e-6, "eine andere Folge beginnt frisch");

            page.ConvertToNodes();
            Pump(() => page.InNodes);

            // Zurueck: das Rezept der ersten, und im Stapel - sie hatte keinen Graphen.
            Show(first);
            Check.That(!page.InNodes && ((LayerPanel)page.FindName("Layers")).Visibility == Visibility.Visible,
                       "zurueck zur ersten Folge rechnet das Atelier wieder im Stapel");
            Check.Near(exposure.Value, 0.8, 1e-6, "mit ihrem Regler");

            // Und wieder zur zweiten: der Graph ist wieder da.
            Show(second);
            Check.That(page.InNodes && page.Graph is not null, "die zweite Folge kommt mit ihrem Graphen zurueck");

            // Speichern auf Knopfdruck.
            page.SaveProject();
            Pump(() => page.Projects.State == AtelierSaveState.Saved);

            var key = page.Projects.Current!;
            Check.That(page.Projects.State == AtelierSaveState.Saved && saved.Text.StartsWith(Localization.Strings.T("S_ProjectSavedAt", "")[..4], StringComparison.Ordinal) &&
                       File.Exists(AtelierProjectStore.PrimaryPath(key)),
                       "Speichern schreibt die Datei in den Ordner FrameFlip und sagt es", saved.Text);

            var files = Directory.GetFiles(Path.Combine(root, "eins", "FrameFlip"));
            Check.That(files.Length == 1 && Path.GetFileName(files[0]) == "render.png.ffproj",
                       "jede Folge hat genau eine Projektdatei neben ihren Bildern", string.Join(", ", files.Select(Path.GetFileName)));
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void WritePng(string path)
    {
        const int W = 64, H = 40;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            pixels[i * 4] = (byte)(i % W * 4);
            pixels[i * 4 + 1] = 110;
            pixels[i * 4 + 2] = (byte)(i / W * 6);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }
}
