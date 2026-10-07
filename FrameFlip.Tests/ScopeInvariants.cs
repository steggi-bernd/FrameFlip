using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Messgeraete (docs/Atelier-Werkzeugplan.md, W2d): Waveform, RGB-Parade und Vektorskop aus
/// dem angezeigten Bild, der logarithmische Massstab des Histogramms, und das Feld "Verteilung",
/// das zwischen ihnen wechselt.
/// </summary>
public static class ScopeInvariants
{
    public static void Run()
    {
        Counting();
        Cost();
        ThePageShowsThem();
    }

    private static unsafe void Counting()
    {
        Check.Group("Messgeraete: was gezaehlt wird");

        // Ein Verlauf von Schwarz nach Weiss: Jede Spalte hat genau eine Helligkeit.
        const int W = 256, H = 4;
        var gradient = Picture(W, H, x => ((byte)x, (byte)x, (byte)x));

        fixed (byte* p = gradient)
        {
            var counts = Scopes.Waveform((IntPtr)p, W, H, W * 4, W, -1, 1);
            bool each = Enumerable.Range(0, W).All(x => counts[(Scopes.Levels - 1 - (int)MathF.Round(x * (Scopes.Levels - 1) / 255f)) * W + x] == H);

            Check.That(each && counts.Sum() == W * H,
                       "Waveform: jede Spalte des Verlaufs steht auf ihrer Hoehe - Schwarz unten, Weiss oben");
        }

        // Rot allein: in der Parade oben im roten Drittel, unten in den beiden anderen.
        var red = Picture(16, 16, _ => (255, 0, 0));

        fixed (byte* p = red)
        {
            var parade = Scopes.Measure(ScopeKind.Parade, (IntPtr)p, 16, 16, 16 * 4);
            (byte R, byte G, byte B) At(int x, int y)
            {
                int i = (y * parade.Width + x) * 4;
                return (parade.Pixels[i + 2], parade.Pixels[i + 1], parade.Pixels[i]);
            }

            int bottom = Scopes.Levels - 1, third = parade.Width / 3;

            Check.That(parade.Width == 3 * third && parade.Height == Scopes.Levels &&
                       At(0, 0).R > 0 && At(0, bottom) == (0, 0, 0) &&
                       At(third, 0) == (0, 0, 0) && At(third, bottom).G > 0 &&
                       At(2 * third, 0) == (0, 0, 0) && At(2 * third, bottom).B > 0,
                       "RGB-Parade: Rot oben im roten Drittel, Gruen und Blau unten in ihren");

            var vector = Scopes.Vectorscope((IntPtr)p, 16, 16, 16 * 4, 1);
            var (rx, ry) = Scopes.VectorAt(Scopes.Chroma(255, 0, 0).Cb, Scopes.Chroma(255, 0, 0).Cr);

            Check.That(vector[ry * Scopes.VectorSize + rx] == 256 && rx < Scopes.VectorSize / 2 && ry < Scopes.VectorSize / 2,
                       "Vektorskop: Rot liegt oben links, alle Punkte an einer Stelle", $"({rx},{ry})");
        }

        var grey = Picture(16, 16, _ => (128, 128, 128));
        var cyan = Picture(16, 16, _ => (0, 255, 255));

        fixed (byte* g = grey)
        fixed (byte* c = cyan)
        {
            var greyVector = Scopes.Vectorscope((IntPtr)g, 16, 16, 16 * 4, 1);
            var (mx, my) = Scopes.VectorAt(0f, 0f);
            var (cx, cy) = Scopes.VectorAt(Scopes.Chroma(0, 255, 255).Cb, Scopes.Chroma(0, 255, 255).Cr);

            Check.That(greyVector[my * Scopes.VectorSize + mx] == 256 && mx == Scopes.VectorSize / 2 &&
                       cx > mx && cy > my,
                       "Vektorskop: Grau in der Mitte, Cyan gegenueber von Rot");
        }

        Check.That(HistogramView.Height01(0, 10_000, true) == 0 &&
                   HistogramView.Height01(10_000, 10_000, true) == 1 && HistogramView.Height01(10_000, 10_000, false) == 1 &&
                   HistogramView.Height01(1, 10_000, true) > 0.05 && HistogramView.Height01(1, 10_000, false) < 0.02,
                   "Histogramm, log: ein einzelner Bildpunkt wird sichtbar, die Spitze bleibt oben");
    }

    /// <summary>Ein Messgeraet misst nach jedem vollen Durchgang - es darf nicht mehr kosten als das Histogramm.</summary>
    private static unsafe void Cost()
    {
        Check.Group("Messgeraete: Kosten");

        const int W = 3840, H = 2160;
        var random = new Random(5);
        var pixels = new byte[W * H * 4];
        random.NextBytes(pixels);

        fixed (byte* p = pixels)
        {
            var start = (IntPtr)p;

            var times = Measure.Fastest(5, t => t.Skip(1).All(s => s < t[0] * 1.5 + 2),
                () => FrameProcessor.Measure(pixels, W, H, W * 4, new Histogram(), step: 4),
                () => Scopes.Measure(ScopeKind.Waveform, start, W, H, W * 4),
                () => Scopes.Measure(ScopeKind.Parade, start, W, H, W * 4),
                () => Scopes.Measure(ScopeKind.Vectorscope, start, W, H, W * 4));

            Check.Timing(times.Skip(1).All(s => s < times[0] * 1.5 + 2),
                         "4K: Waveform, Parade und Vektorskop kosten nicht mehr als das Histogramm daneben",
                         string.Join(" / ", times.Select(t => $"{t:0.0} ms")));
        }
    }

    private static void ThePageShowsThem()
    {
        Check.Group("Messgeraete: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-scope-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 256, H = 8;
        string path = Path.Combine(root, "render_0001.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null,
                                                                  Picture(W, H, x => ((byte)x, (byte)x, (byte)x)), W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var scope = (ScopeView)tools.FindName("Scope");
        var histogram = (HistogramView)tools.FindName("Histogram");

        // Ein Punkt des Messbilds: ob dort etwas steht.
        bool Lit(int x, int y)
        {
            var image = scope.Shown!;
            int i = (y * image.Width + x) * 4;
            return image.Pixels[i] + image.Pixels[i + 1] + image.Pixels[i + 2] > 0;
        }

        int Row(float value) => Scopes.Levels - 1 - (int)MathF.Round(value * (Scopes.Levels - 1));

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            Check.That(tools.ScopeShown == ScopeKind.Histogram && histogram.Visibility == Visibility.Visible &&
                       scope.Visibility == Visibility.Collapsed && scope.Shown is null,
                       "anfangs steht das Histogramm da - und gemessen wird sonst nichts");

            ((Button)tools.FindName("ScopeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => false, 0.1);

            Check.That(tools.ScopeShown == ScopeKind.Waveform && scope.Visibility == Visibility.Visible &&
                       histogram.Visibility == Visibility.Collapsed && scope.Shown is { Kind: ScopeKind.Waveform },
                       "ein Klick: die Waveform, gleich gemessen");
            Check.That(Lit(0, Row(0f)) && Lit(255, Row(1f)) && !Lit(0, Row(1f)) && !Lit(255, Row(0f)),
                       "der Verlauf: links unten, rechts oben");

            // Mit einer Sichthilfe misst das Geraet das Bild darunter - Schwarz bleibt unten, nicht
            // auf der Hoehe des Blaus, das die Sichthilfe dort zeigt.
            page.SetViewAid(ViewAid.Clipping);
            tools.SetScope(ScopeKind.Waveform);
            Check.That(Lit(0, Row(0f)) && !Lit(0, Row(ViewAids.Luma(20, 70, 255))),
                       "mit Clipping misst die Waveform das Bild unter der Sichthilfe");
            page.SetViewAid(ViewAid.None);

            tools.SetScope(ScopeKind.Parade);
            Check.That(scope.Shown is { Kind: ScopeKind.Parade } && Lit(0, Row(0f)) && Lit(scope.Shown.Width - 1, Row(1f)),
                       "die Parade: jedes Drittel zeigt den Verlauf");

            tools.SetScope(ScopeKind.Vectorscope);
            var (mx, my) = Scopes.VectorAt(0f, 0f);
            Check.That(scope.Shown is { Kind: ScopeKind.Vectorscope } && Lit(mx, my) && Enumerable.Range(0, 20).All(x => !Lit(x, 0)),
                       "das Vektorskop: ein grauer Verlauf liegt ganz in der Mitte");

            // Im Knotenmodus misst die Seite nach jedem Durchgang das fertige Bild - das Geraet mit.
            tools.SetScope(ScopeKind.Waveform);
            var stack = scope.Shown;
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => !ReferenceEquals(scope.Shown, stack));

            Check.That(!ReferenceEquals(scope.Shown, stack) && scope.Shown is { Kind: ScopeKind.Waveform } &&
                       Lit(0, Row(0f)) && Lit(255, Row(1f)),
                       "im Knotenmodus misst die Waveform das Bild des Graphen");

            ((Button)tools.FindName("ScopeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((Button)tools.FindName("ScopeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ((Button)tools.FindName("ScopeButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(tools.ScopeShown == ScopeKind.Histogram && histogram.Visibility == Visibility.Visible &&
                       scope.Visibility == Visibility.Collapsed,
                       "Waveform, Parade, Vektorskop - und wieder das Histogramm");

            var log = (Button)tools.FindName("HistogramLogButton");
            log.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(histogram.Logarithmic && (string)log.Content == "log", "der Schalter stellt das Histogramm auf log");
            log.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(!histogram.Logarithmic && (string)log.Content == "lin", "und wieder zurueck");
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

    /// <summary>Ein Bild in Bgra32, jede Spalte in ihrer Farbe.</summary>
    private static byte[] Picture(int width, int height, Func<int, (byte R, byte G, byte B)> colour)
    {
        var pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = colour(x);
                int i = (y * width + x) * 4;
                pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
