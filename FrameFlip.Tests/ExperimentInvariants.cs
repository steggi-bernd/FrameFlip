using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Der Versuch (docs/Atelier-UX-Versuch.md): Einstellungen und Ebenen zugleich, die Zeile
/// "Gesamtbild", der Katalog, Favoriten, Vorher/Nachher und die Folgenleiste.
/// </summary>
public static class ExperimentInvariants
{
    public static void Run()
    {
        Check.Group("Versuch: neu geordnete Bedienung");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-experiment-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 64, H = 32;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            pixels[i * 4] = (byte)(i % W * 3);
            pixels[i * 4 + 1] = 90;
            pixels[i * 4 + 2] = 160;
            pixels[i * 4 + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window { Content = page, Width = 1400, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var layers = (LayerPanel)page.FindName("Layers");
        var dock = (DockHost)page.FindName("Dock");
        var catalog = (CatalogView)page.FindName("Catalog");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            Check.That(dock.Layout.Find("colour") is { } colour && dock.Layout.Find("layers") is { } list &&
                       colour.Group != list.Group && dock.Layout.Find("export") is null,
                       "Einstellungen und Ebenen in eigenen Gruppen - die Ausgabe ist kein Feld mehr, sondern in der Folgenleiste");

            Check.That(layers.WholeChosen && layers.EditedLayer is null && tools.LayerToolsHost.Visibility != Visibility.Visible,
                       "nach dem Laden gilt das Gesamtbild - keine Ebene gewaehlt");

            layers.LayerList.SelectedIndex = 0;
            Pump(() => false, 0.2);

            Check.That(!layers.WholeChosen && layers.EditedLayer is not null &&
                       tools.LayerToolsHost.Visibility == Visibility.Visible && ReferenceEquals(tools.LayerToolsHost.Child, layers.LayerTools),
                       "eine Ebene gewaehlt: Sie ist das Ziel, ihre Einstellungen stehen in den Einstellungen");

            var clarity = ToolCatalog.All.Single(e => e.TitleKey == "S_Clarity");
            var curves = ToolCatalog.All.Single(e => e.TitleKey == "S_Curves");

            Check.That(page.CatalogStateOf(clarity) is { Enabled: false, Alternative: not null },
                       "Katalog an einer Ebene: Klarheit gesperrt, mit dem Ausweg aufs Gesamtbild");
            Check.That(page.CatalogStateOf(curves) is { Enabled: true } add && add.Action == Strings.T("S_CatalogAdd"),
                       "Kurven an der Ebene: hinzufuegen");

            page.UseTool(curves);
            Pump(() => false, 0.3);
            Check.That(tools.IsShown("Curve") && page.CatalogStateOf(curves).Action == Strings.T("S_CatalogEdit"),
                       "danach: bearbeiten - die Karte steht");

            layers.SelectWhole();
            Pump(() => false, 0.2);
            Check.That(layers.WholeChosen && page.CatalogStateOf(clarity).Enabled,
                       "Zeile \"Gesamtbild\": keine Ebene, Klarheit geht");

            ((Button)tools.FindName("AddEffectButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => false, 0.2);
            catalog.Type("kurv");
            Check.That(catalog.IsOpen && catalog.Shown.Any(s => s.Entry.TitleKey == "S_Curves"),
                       "\"+ Effekt\" oeffnet den Katalog, die Suche findet die Kurven");
            catalog.Close();

            int before = settings.AtelierFavourites?.Count ?? 0;
            typeof(AtelierPage).GetMethod("ToggleFavourite", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { "S_Lut" });
            Check.That(settings.AtelierFavourites!.Contains("S_Lut") && settings.AtelierFavourites.Count == Math.Max(before, 3) + 1 &&
                       ((Panel)tools.FindName("FavouriteBar")).Children.Count == settings.AtelierFavourites.Count,
                       "ein Stern: Favorit gespeichert, neben \"+ Effekt\" zu sehen");

            Check.That(page.HandleToolKey(Key.O) && page.ShowingOriginal && page.CompareLatched &&
                       ((FrameworkElement)page.FindName("CompareBadge")).Visibility == Visibility.Visible,
                       "O: Vorher bleibt stehen, mit Abzeichen am Bild");

            ((Slider)tools.FindName("ExposureSlider")).Value = 0.5;
            Pump(() => false, 0.3);
            Check.That(!page.ShowingOriginal && !page.CompareLatched,
                       "eine Aenderung am Bild zeigt wieder das Ergebnis");

            Check.That(((TextBlock)page.FindName("FormatText")).Text.Length > 0 &&
                       ((FrameworkElement)page.FindName("FrameModeHost")) is ContentControl { Content: not null },
                       "Folgenleiste: Format und der Bild/Folge-Schalter stehen unten");
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
