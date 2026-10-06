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
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Die Toenung am Knoten "Belichtung &amp; Toenung" nimmt eine Farbe (docs/Atelier-Arbeitsablauf.md,
/// C4d) - aus dem Bild oder aus dem Farbspeicher, wie das Rad im Ebenenstreifen.
/// </summary>
public static class NodeTintPickInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        Check.Group("Toenung am Knoten aus einer Farbe");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-nodetint-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string warm = Picture(Path.Combine(root, "a", "warm_0001.png"), 210, 170, 120);
        string cool = Picture(Path.Combine(root, "b", "cool_0001.png"), 110, 150, 210);

        var settings = new AppSettings();
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var tools = (GradingPanel)page.FindName("Tools");
        var editor = (NodeEditor)page.FindName("NodeView");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        (float R, float G, float B) Linear(int x, int y)
            => ((float, float, float))typeof(AtelierPage).GetMethod("LinearAt", flags)!.Invoke(page, new object[] { x, y })!;

        Button PickButton() => Descendants((Panel)tools.FindName("NodeFields")).OfType<Button>()
                                   .Single(b => b.Content as string == T("S_NodeTintPick"));

        try
        {
            window.Show();

            // Eine Farbe aus dem kuehlen Bild merken.
            Show(page, cool);
            page.HandleToolKey(Key.I);
            typeof(AtelierPage).GetMethod("ReadAt", flags)!.Invoke(page, new object[] { 5, 5 });
            var stored = settings.AtelierColours[0];
            page.HandleToolKey(Key.V);

            Show(page, warm);
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);

            var node = page.Graph!.Add(new ExposureTintNode());
            string id = node.Id;
            editor.Select(node);
            Pump(() => false, 0.2);

            PickButton().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            page.PipetteAt(5, 5, new Point(60, 60));
            Check.That(page.ColourPicking && page.LoupeText?.Contains(NodeTitles.For(node)) == true,
                       "der Knopf am Knoten macht die Maus zur Pipette, die Lupe nennt den Knoten", page.LoupeText);

            var (lr, lg, lb) = Linear(5, 5);
            Check.That(page.ColourPickAt(5, 5) && !page.ColourPicking &&
                       Ratio(node.Tint.R, node.Tint.B, lr, lb) && Ratio(node.Tint.G, node.Tint.B, lg, lb) &&
                       MathF.Abs((node.Tint.R + node.Tint.G + node.Tint.B) / 3f - 1f) < 0.01f,
                       "ein Klick: die Toenung hat die Verhaeltnisse der Farbe, im Mittel eins",
                       $"{node.Tint.R:0.000} {node.Tint.G:0.000} {node.Tint.B:0.000}");

            page.StepNodes(back: true);
            var restored = (ExposureTintNode)page.Graph!.Find(id)!;
            Check.That(restored.Tint.Near(1f), "Rueckgaengig nimmt die Toenung in einem Schritt zurueck");

            // Ein Feld aus dem Speicher: die Farbe des kuehlen Bildes.
            editor.Select(restored);
            Pump(() => false, 0.2);
            PickButton().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(page.TakeStoredColour(stored) && Ratio(restored.Tint.B, restored.Tint.R, stored.B, stored.R) && !page.ColourPicking,
                       "ein Feld aus dem Speicher wird die Toenung des Knotens");

            // Etwas anderes gewaehlt: Die Pipette galt dem Knoten und endet.
            PickButton().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            editor.Select(NodeGroups.File(page.Graph)!);
            var before = (restored.Tint.R, restored.Tint.G, restored.Tint.B);
            Check.That(!page.ColourPickAt(5, 5) && !page.ColourPicking && before == (restored.Tint.R, restored.Tint.G, restored.Tint.B),
                       "ein anderer Knoten gewaehlt: die Pipette endet, die Toenung bleibt");
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

    private static bool Ratio(float a, float b, float c, float d) => MathF.Abs(a / b - c / d) < 0.02f * (c / d);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static string Picture(string path, byte r, byte g, byte b)
    {
        const int W = 40, H = 20;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Show(AtelierPage page, string path)
    {
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("FileText")).Text == Path.GetFileName(path) &&
                   ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
        Pump(() => false, 0.4);
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
