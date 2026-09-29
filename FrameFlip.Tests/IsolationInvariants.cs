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
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Isolieren im Knotenmodus (docs/Atelier-Arbeitsablauf.md, C6): eine Ebene oder ihre Maske
/// allein - ueber den Betrachter, ohne den Aufbau anzufassen, mit Schild und Esc.
/// </summary>
public static class IsolationInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        Check.Group("Isolieren: Ebene und Maske allein");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-isolate-" + Guid.NewGuid().ToString("N")[..8]);
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

        (string Node, string Output)? Viewer()
            => ((string, string)?)typeof(AtelierPage).GetField("_viewer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);

        try
        {
            window.Show();
            page.Open(first);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            layers.AddAdjustment();
            layers.Selection!.Name = "Licht";
            layers.AddAdjustment();
            layers.Selection!.Mask.Kind = MaskKind.Colour;
            layers.Selection.Name = "Himmel";

            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);
            var graph = page.Graph!;
            string before = graph.Save();

            var list = (NodeLayerList)page.FindName("NodeLayers");
            var sky = list.Shown.Single(l => l.Name == "Himmel");
            var light = list.Shown.Single(l => l.Name == "Licht");
            var up = graph.Into(sky.Mix!.Id, "Oben")!;
            var factor = graph.Into(sky.Mix!.Id, "Faktor")!;

            var wired = typeof(NodeLayerList).GetField("IsolateWanted", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(list);
            Check.That(wired is not null, "Alt+Klick auf Auge oder Maske der Liste fuehrt zum Isolieren");

            // Die Ebene allein.
            Check.That(page.Isolate(sky, mask: false) && Viewer() == (up.From, up.Output) &&
                       badge.Visibility == Visibility.Visible && badgeText.Text == T("S_IsolatedLayer", "Himmel"),
                       "Ebene isoliert: der Betrachter zeigt, was oben in ihr Mischen fliesst, das Schild sagt es", badgeText.Text);
            Check.That(graph.Save() == before && !sky.Mix!.Muted && !light.Mix!.Muted, "der Aufbau bleibt, wie er ist - nichts wird ausgeblendet");

            // Ein zweites Mal beendet es.
            page.Isolate(sky, mask: false);
            Check.That(Viewer() is null && badge.Visibility == Visibility.Collapsed, "ein zweites Mal: wieder das ganze Bild");

            // Die Maske allein.
            page.Isolate(sky, mask: true);
            Check.That(Viewer() == (factor.From, factor.Output) && badgeText.Text == T("S_IsolatedMask", "Himmel"),
                       "Maske isoliert: der Betrachter zeigt, was im Faktor ankommt", badgeText.Text);

            // Eine Ebene ohne Maske hat nichts zu isolieren.
            Check.That(!page.Isolate(light, mask: true) && Viewer() == (factor.From, factor.Output),
                       "eine Ebene ohne Maske: \"Maske isolieren\" tut nichts und laesst das Bild, wie es ist");

            // Esc beendet es - aber nur, wenn etwas isoliert ist. Ein offenes Menue aus einer
            // frueheren Gruppe haelt sonst die Maus, und dann gehoert Esc zu Recht ihm.
            Mouse.Capture(null);
            Check.That(page.HandleToolKey(Key.Escape) && Viewer() is null, "Esc beendet es");
            Check.That(!page.HandleToolKey(Key.Escape), "Esc ohne Isolieren: bleibt fuer andere frei");

            // Ein beliebiger Knoten im Betrachter heisst weiter "Betrachter".
            var file = graph.Nodes.OfType<RenderNode>().First();
            page.SetViewer((file.Id, file.Outputs[0].Name));
            Check.That(badgeText.Text.StartsWith(T("S_ViewerShowing", "").Split(':')[0], StringComparison.Ordinal),
                       "ein anderer Knoten im Betrachter: das Schild sagt \"Betrachter\", nicht \"isoliert\"", badgeText.Text);
            page.SetViewer(null);

            // Aus dem Schnellfeld.
            page.ShowQuickPanel(sky.Mix!);
            Check.That(page.QuickPanel!.Items.Contains(T("S_QuickIsolateLayer")) && !page.QuickPanel.Items.Contains(T("S_QuickIsolateMask")),
                       "Schnellfeld am Mischen: \"Ebene isolieren\"", string.Join(", ", page.QuickPanel.Items));
            page.QuickPanel.Invoke(T("S_QuickIsolateLayer"));
            Check.That(Viewer() == (up.From, up.Output), "und es zeigt die Ebene allein");
            page.SetViewer(null);

            var maskNode = graph.Find(factor.From)!;
            page.ShowQuickPanel(maskNode);
            Check.That(page.QuickPanel!.Items.Contains(T("S_QuickIsolateMask")), "Schnellfeld an ihrer Maske: \"Maske isolieren\"");
            page.QuickPanel.Invoke(T("S_QuickIsolateMask"));
            Check.That(Viewer() == (factor.From, factor.Output), "und es zeigt die Maske allein");

            // Ein anderes Bild beendet es.
            page.Open(second);
            Pump(() => !page.InNodes);
            Pump(() => false, 0.3);
            Check.That(Viewer() is null && badge.Visibility == Visibility.Collapsed, "ein anderes Bild: nichts mehr isoliert");
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

    private static string Picture(string path)
    {
        const int W = 40, H = 20;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 1] = 120;
            pixels[i + 2] = 60;
            pixels[i + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
