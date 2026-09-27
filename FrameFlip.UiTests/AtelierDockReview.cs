using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Views;

internal static partial class Program
{
    /// <summary>
    /// Das Atelier mit einem synthetischen Bild, als Bilder zum Ansehen (Entscheidung 10): die
    /// Werkzeugeinstellungen an jeder Stelle, an die man sie ziehen kann - oben, links, rechts
    /// oben und unten, als Reiter, unter dem Bild. Keine Medien, nur ein Verlauf.
    /// </summary>
    private static void TestAtelierDock()
    {
        string folder = Path.Combine(Path.GetTempPath(), "frameflip-andocken-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "verlauf.png");

        const int W = 320, H = 180;
        var pixels = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                pixels[i] = (byte)(60 + x * 150 / W);
                pixels[i + 1] = (byte)(40 + y * 120 / H);
                pixels[i + 2] = 90;
                pixels[i + 3] = 255;
            }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page,
            Width = 1400,
            Height = 860,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x13, 0x1A)),
        };

        try
        {
            window.Show();
            page.Open(path);

            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < until && ((TextBlock)page.FindName("SourceText")).Text.Length == 0)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var tool = (PropertiesPanel)page.FindName("Properties");
            var dock = (DockHost)page.FindName("Dock");
            tool.Show(AtelierTool.Brush);

            foreach (var (name, zone, group, tab) in new[]
                     {
                         ("oben", DockZone.Top, 0, false),
                         ("links", DockZone.Left, 0, false),
                         ("rechts-oben", DockZone.Right, 0, false),
                         ("rechts-unten", DockZone.Right, 3, false),
                         ("rechts-reiter", DockZone.Right, 1, true),
                         ("unten", DockZone.Bottom, 0, false),
                     })
            {
                dock.ResetLayout();
                if (zone != DockZone.Top) dock.MovePanel("tool", zone, group, tab);
                page.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Render(page, 1400, 860, $"Werkzeug-{name}.png");

                Check(dock.Layout.Find("tool") is { } at && at.Zone == zone, $"Werkzeug {name}: steht, wo es hin sollte");
            }

            dock.ResetLayout();
        }
        finally
        {
            window.Close();
            Thread.Sleep(300);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
