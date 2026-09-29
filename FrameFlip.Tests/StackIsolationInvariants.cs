using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Isolieren im Stapel (docs/Atelier-Arbeitsablauf.md, C6b): eine Ebene allein, ihre Maske grau
/// oder als roter Schleier - gerechnet vom Composer, nur fuer die Anzeige.
/// </summary>
public static class StackIsolationInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        TheComposerIsolates();
        ThePageShowsIt();
    }

    private static void TheComposerIsolates()
    {
        Check.Group("Isolieren im Stapel: Zusammensetzen");

        // Links dunkel (0,05), rechts hell (0,8) - Licht.
        const int W = 8, H = 2;
        var frame = new FloatFrame { Width = W, Height = H, R = new float[W * H], G = new float[W * H], B = new float[W * H] };
        for (int i = 0; i < W * H; i++) frame.R[i] = frame.G[i] = frame.B[i] = i % W < W / 2 ? 0.05f : 0.8f;

        var sources = new Dictionary<string, FloatFrame> { [""] = frame };

        // Eine Einstellungsebene, eine Blende heller, maskiert auf das Helle.
        var adjustment = new ImageLayer
        {
            Content = LayerContent.Adjustment,
            Exposure = 1f,
            Mask = new LayerMask { Kind = MaskKind.Luminance, Low = 0.5f, High = 1f, Softness = 0f },
        };
        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "" }, adjustment } };

        var whole = LayerComposer.Compose(stack, sources)!;
        Check.That(Near(whole.R[0], 0.05f) && Near(whole.R[W - 1], 1.6f), "(Vorbedingung: die Ebene hellt nur das Helle auf)", $"{whole.R[0]} / {whole.R[W - 1]}");

        var alone = LayerComposer.Compose(stack, sources, solo: new LayerSolo(adjustment, SoloView.Layer))!;
        Check.That(Near(alone.R[0], 0.1f) && Near(alone.R[W - 1], 1.6f) && alone.A![0] == 1f,
                   "die Ebene allein: was sie beitraegt, ohne ihre Maske - das Dunkle ebenfalls aufgehellt", $"{alone.R[0]} / {alone.R[W - 1]}");

        var mask = LayerComposer.Compose(stack, sources, solo: new LayerSolo(adjustment, SoloView.Mask))!;
        Check.That(mask.R[0] == 0f && Near(mask.R[W - 1], 1f) && mask.R[W - 1] == mask.G[W - 1] && !mask.IsSceneReferred,
                   "die Maske allein: schwarz, wo sie nicht wirkt, weiss, wo sie wirkt - als Anzeige, nicht als Licht");

        var veil = LayerComposer.Compose(stack, sources, solo: new LayerSolo(adjustment, SoloView.Veil))!;
        Check.That(Near(veil.R[0], 0.425f) && veil.G[0] < 0.05f && Near(veil.R[W - 1], 1.6f),
                   "der Schleier: wo die Maske nicht wirkt, halb rot - wo sie wirkt, das Bild wie es ist", $"{veil.R[0]} {veil.G[0]}");

        Check.That(Near(LayerComposer.Compose(stack, sources)!.R[W - 1], 1.6f) && stack.Layers[1].Visible,
                   "am Stapel aendert sich nichts");

        adjustment.Visible = false;
        Check.That(LayerComposer.Compose(stack, sources, solo: new LayerSolo(adjustment, SoloView.Layer)) is null,
                   "eine ausgeblendete Ebene laesst sich nicht allein zeigen - es kommt nichts");
    }

    private static void ThePageShowsIt()
    {
        Check.Group("Isolieren im Stapel: im Atelier");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-stacksolo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string first = Picture(Path.Combine(root, "a", "render_0001.png"));
        string second = Picture(Path.Combine(root, "b", "render_0001.png"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");
        var badge = (Border)page.FindName("ViewerBadge");
        var badgeText = (TextBlock)page.FindName("ViewerText");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        FloatFrame? Shown() => (FloatFrame?)typeof(AtelierPage).GetMethod("Shown", flags)!.Invoke(page, null);
        FloatFrame? Whole() => (FloatFrame?)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page);

        try
        {
            window.Show();
            Show(page, first);
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);

            layers.AddAdjustment();
            var layer = layers.Selection!;
            layer.Name = "Licht";
            var box = (ComboBox)layers.FindName("MaskBox");
            box.SelectedIndex = box.Items.IndexOf(T("S_MaskLuminance"));
            Pump(() => false, 0.3);
            int count = layers.Stack.All().Count();

            layers.ShowRowMenu(layer);
            var items = layers.RowMenu!.Items;
            layers.RowMenu.Close();
            Check.That(items.Contains(T("S_LayerMenuAlone")) && items.Contains(T("S_LayerMenuMaskAlone")) && items.Contains(T("S_LayerMenuMaskVeil")),
                       "das Menue der Zeile: allein, Maske allein, Maske als Schleier", string.Join(", ", items));

            Check.That(page.IsolateStack(layer, SoloView.Mask) && page.StackSolo is { View: SoloView.Mask } &&
                       !ReferenceEquals(Shown(), Whole()) && badge.Visibility == Visibility.Visible &&
                       badgeText.Text == T("S_IsolatedMask", "Licht"),
                       "Maske isoliert: die Anzeige zeigt ein eigenes Bild, das Schild sagt es", badgeText.Text);

            Check.That(layer.Visible && layers.Stack.All().Count() == count && Whole() is { } composed && !ReferenceEquals(composed, Shown()),
                       "das zusammengesetzte Bild und der Stapel bleiben - Pipetten und Messung sehen das ganze Bild");

            page.IsolateStack(layer, SoloView.Veil);
            Check.That(page.StackSolo is { View: SoloView.Veil } && badgeText.Text == T("S_IsolatedVeil", "Licht"),
                       "eine andere Ansicht derselben Ebene wechselt");

            page.IsolateStack(layer, SoloView.Veil);
            Check.That(page.StackSolo is null && ReferenceEquals(Shown(), Whole()) && badge.Visibility != Visibility.Visible,
                       "dasselbe noch einmal beendet es");

            page.IsolateStack(layer, SoloView.Layer);

            // Ein offenes Menue aus einer frueheren Gruppe haelt sonst die Maus, und dann gehoert
            // Esc zu Recht ihm - wie in IsolationInvariants.
            Mouse.Capture(null);
            Check.That(page.HandleToolKey(Key.Escape) && page.StackSolo is null && badge.Visibility != Visibility.Visible,
                       "Esc beendet es");

            page.IsolateStack(layer, SoloView.Layer);
            layers.SetVisible(layer, false);
            Pump(() => false, 0.4);
            Check.That(page.StackSolo is null && ReferenceEquals(Shown(), Whole()), "die Ebene ausgeblendet: das Isolieren endet");
            layers.SetVisible(layer, true);

            page.IsolateStack(layer, SoloView.Layer);
            Show(page, second);
            Check.That(page.StackSolo is null && badge.Visibility != Visibility.Visible, "ein anderes Bild beendet es");

            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Check.That(!page.IsolateStack(layers.Stack.Layers[0], SoloView.Layer), "im Knotenmodus isoliert der Betrachter, nicht der Stapel");
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

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.002f;

    private static string Picture(string path)
    {
        const int W = 40, H = 20;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            byte v = i % W < W / 2 ? (byte)50 : (byte)220;
            pixels[i * 4] = v;
            pixels[i * 4 + 1] = v;
            pixels[i * 4 + 2] = v;
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Show(AtelierPage page, string path)
    {
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
        Pump(() => false, 0.4);
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
