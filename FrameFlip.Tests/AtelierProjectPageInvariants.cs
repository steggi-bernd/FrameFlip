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
        string third = Path.Combine(root, "drei", "neu_0001.png");
        string fourth = Path.Combine(root, "vier", "neu_0001.png");
        string fifth = Path.Combine(root, "fuenf", "neu_0001.png");

        foreach (string file in new[] { first, firstNext, second, third, fourth, fifth })
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

            // Ein Bild aus einem dritten Ordner, das noch kein Projekt hat. Die Anzeige
            // spricht von DIESEM Projekt - nicht von der Datei, die zuletzt irgendwo
            // geschrieben wurde, und nicht mit deren Uhrzeit. Vorher stand hier "Gespeichert"
            // mit der Zeit und im Tooltip mit der Datei der Folge davor: Das neue Bild sah
            // aus, als laege es in einem fremden Projekt.
            string savedPrefix = Localization.Strings.T("S_ProjectSavedAt", "")[..4];

            Show(third);
            var thirdKey = page.Projects.Current!;
            var layerPanel = (LayerPanel)page.FindName("Layers");
            int fresh = layerPanel.Stack.Layers.Count;
            Check.That(saved.ToolTip as string == AtelierProjectStore.PrimaryPath(thirdKey),
                       "ein neues Projekt: der Tooltip nennt seine Datei im eigenen Ordner", saved.ToolTip as string);
            Check.That(!saved.Text.StartsWith(savedPrefix, StringComparison.Ordinal) && page.Projects.SavedAt is null,
                       "und behauptet nicht, gespeichert zu sein", saved.Text);

            // Zurueck zur zweiten: ihre Datei, und die Zeit, zu der sie geschrieben wurde.
            var secondSaved = new AtelierProjectStore().Load(key)!.SavedUtc.ToLocalTime();

            Show(second);
            Check.That(saved.ToolTip as string == AtelierProjectStore.PrimaryPath(key) &&
                       page.Projects.SavedAt is { } at && Math.Abs((at - secondSaved).TotalSeconds) < 1,
                       "zurueck zur zweiten Folge: ihre Datei und ihre Zeit", $"{page.Projects.SavedAt} | {saved.ToolTip}");

            // Ein Projekt mit Ebenen im Stapel, dann ein Bild aus einem neuen Ordner: Rechts
            // stehen nicht die Ebenen des vorigen Projekts ueber dem neuen Bild.
            Show(first);
            layerPanel.AddAdjustment();
            layerPanel.AddAdjustment();
            Pump(() => false, 0.3);
            int had = layerPanel.Stack.Layers.Count;

            Show(fourth);
            Check.That(had >= fresh + 2 && layerPanel.Stack.Layers.Count == fresh && (page.Recipe.Layers?.Layers.Count ?? 0) == fresh,
                       "aus dem Stapel mit Ebenen in ein neues Projekt: kein fremder Stapel", $"{had} -> {layerPanel.Stack.Layers.Count}");

            // Dasselbe aus dem Knotenmodus - der Graph und seine Ebenen bleiben beim alten Projekt.
            Show(second);
            Pump(() => page.InNodes);
            Show(fifth);
            Check.That(!page.InNodes && page.Graph is null && layerPanel.Stack.Layers.Count == fresh,
                       "aus dem Knotenmodus in ein neues Projekt: kein fremder Graph, kein fremder Stapel",
                       $"InNodes={page.InNodes} Ebenen={layerPanel.Stack.Layers.Count}");

            // Und die Dateien: Das neue Projekt traegt nichts vom alten.
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            var fourthProject = new AtelierProjectStore().Load(SequenceKey.Of(fourth)!);
            Check.That(fourthProject is null || (fourthProject.Layers?.Layers.Count ?? 0) == fresh,
                       "auch in der Projektdatei des neuen Ordners steht kein fremder Stapel");
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
