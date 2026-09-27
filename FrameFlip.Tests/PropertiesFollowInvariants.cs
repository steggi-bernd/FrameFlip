using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Das Eigenschaftenfeld folgt dem Ziel (docs/Atelier-Arbeitsablauf.md, C5a): Im Knotenmodus
/// kommt es beim Waehlen eines Knotens nach vorn und klappt auf, danach geht es zurueck, wie
/// es stand - und wer die Anordnung selbst anfasst, behaelt sie.
/// </summary>
public static class PropertiesFollowInvariants
{
    public static void Run()
    {
        Check.Group("Eigenschaften folgen dem Ziel");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-follow-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string first = Picture(Path.Combine(root, "a", "render_0001.png"));
        string second = Picture(Path.Combine(root, "b", "render_0001.png"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var dock = (DockHost)page.FindName("Dock");
        var tools = (GradingPanel)page.FindName("Tools");
        var layers = (LayerPanel)page.FindName("Layers");
        var editor = (NodeEditor)page.FindName("NodeView");

        DockGroup Group()
        {
            var (zone, index) = dock.Layout.Find("colour")!.Value;
            return dock.Layout.Zone(zone)[index];
        }

        try
        {
            window.Show();
            page.Open(first);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            Check.That(DockHost.GetTitleKey(tools) == "S_SectionColour", "im Stapel heisst das Feld weiter \"Farbe\"");

            layers.AddAdjustment();
            layers.AddAdjustment();
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);

            Check.That(DockHost.GetTitleKey(tools) == "S_SectionProperties", "im Knotenmodus heisst es \"Eigenschaften\"");

            var graph = page.Graph!;
            var grade = graph.Nodes.OfType<LayerGradeNode>().First();

            // Die Ebenen liegen vorn - der Nutzer hat sie so gelegt.
            dock.Activate("layers");
            editor.Select(grade);
            Pump(() => false, 0.2);
            Check.That(Group() is { Active: "colour", Collapsed: false } && dock.IsShown("colour"),
                       "ein Knoten gewaehlt: die Eigenschaften kommen nach vorn", $"{Group().Active}");

            editor.Select(null);
            Pump(() => false, 0.2);
            Check.That(Group() is { Active: "layers", Collapsed: false }, "nichts gewaehlt: die Ebenen liegen wieder vorn, wie vorher");

            // Zugeklappt: klappt auf, und danach wieder zu.
            dock.Activate("colour");
            dock.Toggle("colour");
            Check.That(Group().Collapsed, "(Vorbedingung: die Gruppe ist zugeklappt)");

            editor.Select(grade);
            Pump(() => false, 0.2);
            Check.That(Group() is { Active: "colour", Collapsed: false }, "zugeklappt und ein Knoten gewaehlt: das Feld klappt auf");

            editor.Select(null);
            Pump(() => false, 0.2);
            Check.That(Group().Collapsed, "und klappt wieder zu, wenn nichts mehr gewaehlt ist");

            // Wer selbst umstellt, behaelt seine Anordnung.
            editor.Select(grade);
            Pump(() => false, 0.2);
            dock.Activate("layers");
            editor.Select(null);
            Pump(() => false, 0.2);
            Check.That(Group() is { Active: "layers", Collapsed: false },
                       "zwischendurch selbst umgestellt: danach klappt nichts von allein zu");

            // Eine Wahl aus der Liste der Ebenen verdeckt die Liste nicht.
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var shown = list.Shown.Where(l => l.Target is not null).ToList();
            Call(page, "OnLayerChosen", shown[0].Target!);
            Pump(() => false, 0.2);
            Check.That(Group().Active == "layers", "eine Ebene aus der Liste gewaehlt: die Liste bleibt vorn, in der man gerade waehlt");

            // Liegen die Ebenen in einer eigenen Gruppe, kommen die Eigenschaften daneben nach vorn.
            editor.Select(null);
            dock.MovePanel("layers", DockZone.Right, 0, asTab: false);
            dock.Activate("histogram");
            Pump(() => false, 0.2);
            if (dock.Layout.Find("colour") is { } at && dock.Layout.Zone(at.Zone)[at.Group].Panels.Contains("layers"))
                Check.That(false, "(Vorbedingung: die Ebenen liegen in einer eigenen Gruppe)");

            Call(page, "OnLayerChosen", shown[^1].Target!);
            Pump(() => false, 0.2);
            Check.That(dock.IsShown("colour") && dock.IsShown("layers"),
                       "Ebenen in eigener Gruppe: die Wahl aus der Liste zeigt die Eigenschaften daneben");

            // Ein anderes Bild ohne Knoten: das Feld heisst wieder "Farbe".
            page.Open(second);
            Pump(() => !page.InNodes);
            Pump(() => false, 0.3);
            Check.That(!page.InNodes && DockHost.GetTitleKey(tools) == "S_SectionColour", "zurueck im Stapel: wieder \"Farbe\"");
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
            pixels[i] = 90;
            pixels[i + 1] = 120;
            pixels[i + 2] = 160;
            pixels[i + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        return path;
    }

    private static void Call(AtelierPage page, string method, params object[] args)
        => typeof(AtelierPage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
