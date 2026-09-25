using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Wo das Rezept des Ateliers landet - Grundregler und Werkzeuge des fertigen Bildes,
/// der Ebenenstapel, der Graph - und wann.
///
/// Zuerst die Charakterisierung vor der Bearbeitungssitzung (Refactoring-Studio, S2):
/// damals in den Einstellungen. Seit den Projektdateien (docs/Projekte-und-Masken.md,
/// Phase C) im Rezept des Projekts der Folge; die Einstellungen behalten nur das Bild,
/// das zuletzt offen war.
/// </summary>
public static class AtelierRecipeInvariants
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static void Run()
    {
        Check.Group("Atelier: wo das Rezept landet - im Projekt der Folge");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-rezept-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "bild.png");
        WritePng(path, 96, 64);

        var settings = new AppSettings();
        int persisted = 0;
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => persisted++);
        var window = new Window
        {
            Content = page, Width = 1000, Height = 800, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            page.UpdateLayout();

            page.Open(path);
            var size = (TextBlock)page.FindName("SourceText");
            Pump(() => size.Text.Length > 0);

            var strip = (LayerPanel)page.FindName("Layers");
            var tools = (GradingPanel)page.FindName("Tools");
            var exposure = (Slider)tools.FindName("ExposureSlider");

            var recipe = page.Recipe;

            // Nach dem Oeffnen: der Stapel ist der des Streifens, das fertige Bild hat seinen
            // Stand im Rezept, und das Bild ist in den Einstellungen gemerkt und geschrieben.
            Check.That(ReferenceEquals(recipe.Layers, strip.Stack) && recipe.Grading is not null &&
                       recipe.Adjustments is not null && settings.AtelierImage == path && persisted >= 1,
                       "nach dem Oeffnen: Stapel, Werkzeuge und Grundregler des Bildes stehen im Rezept des Projekts");
            Check.That(settings.Layers is null && settings.AtelierRecipeMoved,
                       "die Einstellungen halten keinen Stapel mehr - nur, dass das Rezept uebernommen ist");

            // Ein Regler am fertigen Bild: Grundregler und Werkzeugstand wandern sofort mit.
            int writes = persisted;
            exposure.Value = 0.6;

            Check.Near(recipe.Adjustments!.Exposure, 0.6, 1e-6, "ein Regler am Bild steht sofort im Rezept");
            Check.That(Same(recipe.Grading, tools.Stack), "und der Werkzeugstand des Bildes auch - als eigene Kopie");
            Check.That(!ReferenceEquals(recipe.Grading, tools.Stack), "nicht als der Stapel des Streifens selbst");
            Check.That(persisted == writes && recipe.Dirty, "die Einstellungen werden dabei nicht geschrieben - das Projekt ist ungespeichert");

            // Eine Einstellungsebene: ihre Werte gehoeren der Ebene, das Bild bleibt.
            strip.AddAdjustment();
            var layer = strip.EditedLayer!;
            exposure.Value = -1.25;

            Check.Near(layer.Adjustments!.Exposure, -1.25, 1e-6, "an einer Ebene gehoert der Regler der Ebene");
            Check.Near(recipe.Adjustments!.Exposure, 0.6, 1e-6, "das fertige Bild bleibt, wie es war");
            Check.That(ReferenceEquals(recipe.Layers, strip.Stack) && recipe.Layers!.Layers.Contains(layer),
                       "die Ebene steht im Stapel des Rezepts");

            // Zurueck zum Bild: sein Stand kommt aus den Einstellungen zurueck.
            var pass = strip.Stack.Layers.First(l => l.Content == LayerContent.Pass);
            Select(strip, pass);

            Check.Near(exposure.Value, 0.6, 1e-6, "zurueck am Bild steht der Regler wieder auf dessen Wert");

            // Knoten: Der Graph kommt als Text ins Rezept und mit dem Projekt in die Datei.
            page.ConvertToNodes();
            Pump(() => recipe.Nodes is not null);
            page.Flush();

            var written = page.Projects.Current is { } key ? page.Projects.Store.Load(key) : null;

            Check.That(recipe.Nodes is { Length: > 0 } && page.Graph is not null && recipe.Nodes == page.Graph.Save() &&
                       written?.Nodes is not null && settings.AtelierNodes is null,
                       "umgewandelt steht der Graph im Rezept und in der Projektdatei - nicht in den Einstellungen");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static bool Same(GradingStack? a, GradingStack? b)
        => a is not null && b is not null && JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);

    private static void Select(LayerPanel strip, ImageLayer layer)
    {
        var list = (ListBox)strip.FindName("LayerList");

        foreach (ListBoxItem item in list.Items)
        {
            if (!ReferenceEquals(item.Tag, layer)) continue;

            item.IsSelected = true;
            strip.UpdateLayout();
            return;
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = (byte)(i % width * 255 / width);
            pixels[i * 4 + 1] = 120;
            pixels[i * 4 + 2] = (byte)(i / width * 255 / height);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Pump(Func<bool> until)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < end && !until())
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(5);
        }

        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
