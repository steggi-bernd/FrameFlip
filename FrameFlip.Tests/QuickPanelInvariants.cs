using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
/// Das Schnellfeld am Knoten (docs/Atelier-Arbeitsablauf.md, C5b): was es je Knoten anbietet,
/// und dass das Neue dort landet, wo auch die Zielzeile es hinsetzt.
/// </summary>
public static class QuickPanelInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        Check.Group("Schnellfeld am Knoten");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-quick-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 40, H = 20;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 60;
            pixels[i + 1] = 140;
            pixels[i + 2] = 200;
            pixels[i + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");
        var editor = (NodeEditor)page.FindName("NodeView");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            // Eine Ebene mit Farbbereich-Maske - im Graphen eine Maske, die ihrer Ebene gehoert.
            layers.AddAdjustment();
            layers.Selection!.Mask.Kind = MaskKind.Colour;
            layers.Selection.Name = "Himmel";

            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);
            var graph = page.Graph!;

            var wired = typeof(NodeEditor).GetField("QuickWanted", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor);
            Check.That(wired is not null, "ein Doppelklick im Editor fuehrt zum Schnellfeld");

            // Ein Knoten mit Bild: Korrektur, Belichtung, Tonwert, Effekt, Betrachter.
            var grade = graph.Nodes.OfType<LayerGradeNode>().First();
            page.ShowQuickPanel(grade);
            var items = page.QuickPanel!.Items;
            Check.That(page.QuickPanel.IsOpen && ReferenceEquals(editor.Selected, grade) &&
                       items.SequenceEqual(new[] { T("S_Correction"), T("S_NodeLight"), T("S_Levels"), T("S_QuickEffect"), T("S_HubViewer") }),
                       "an einer Ebenenkorrektur: Korrektur, Belichtung & Saettigung, Tonwert, Effekt, Betrachter", string.Join(", ", items));
            Check.That(Heading(page.QuickPanel) == T("S_QuickTitle", NodeTitles.For(grade)), "oben steht, fuer welchen Knoten es gilt", Heading(page.QuickPanel));

            int count = graph.Nodes.Count;
            page.QuickPanel.Invoke(T("S_Levels"));
            Pump(() => graph.Nodes.Count > count);
            var levels = editor.Selected;
            Check.That(graph.Nodes.Count == count + 1 && levels is not null && !page.QuickPanel.IsOpen &&
                       graph.Links.Any(l => l.From == grade.Id && l.To == levels.Id),
                       "Tonwert: ein neuer Knoten direkt hinter der Korrektur, gewaehlt, das Feld geschlossen");

            page.StepNodes(back: true);
            Check.That(page.Graph!.Nodes.Count == count, "Rueckgaengig nimmt ihn in einem Schritt zurueck");
            graph = page.Graph!;

            // Eine Maske, die einer Ebene gehoert: Das Neue kommt in den Zweig ihrer Ebene.
            var owned = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Colour);
            var ownedMix = graph.Nodes.OfType<MixNode>().Single(m => graph.Into(m.Id, "Faktor")?.From == owned.Id);
            page.ShowQuickPanel(owned);
            Check.That(page.QuickPanel!.Items.Contains(T("S_Correction")) && !page.QuickPanel.Items.Contains(T("S_QuickCorrectWithMask")),
                       "an der Maske einer Ebene: dieselben Eintraege wie an einem Bild", string.Join(", ", page.QuickPanel.Items));

            count = graph.Nodes.Count;
            page.QuickPanel.Invoke(T("S_Correction"));
            Pump(() => graph.Nodes.Count > count);
            var added = editor.Selected!;
            Check.That(LayerEdits.Branch(graph, ownedMix).Contains(added.Id),
                       "Korrektur an dieser Maske: in den Zweig ihrer Ebene, die Maske begrenzt sie schon");

            // Eine freie Maske: Sie wird die Maske einer neuen Korrektur.
            var free = graph.Add(new MaskNode { Mask = new LayerMask { Kind = MaskKind.Cryptomatte }, Label = "Auto" });
            editor.InvalidateVisual();
            page.ShowQuickPanel(free);
            items = page.QuickPanel!.Items;
            Check.That(items.SequenceEqual(new[] { T("S_QuickCorrectWithMask"), T("S_QuickSelectObjects"), T("S_HubViewer") }),
                       "an einer freien Kryptomatte-Maske: Korrektur mit ihr, Objekte waehlen, Betrachter", string.Join(", ", items));

            count = graph.Nodes.Count;
            page.QuickPanel.Invoke(T("S_QuickCorrectWithMask"));
            Pump(() => graph.Nodes.Count > count);
            var mix = graph.Nodes.OfType<MixNode>().SingleOrDefault(m => graph.Into(m.Id, "Faktor")?.From == free.Id);
            Check.That(mix is not null && editor.Selected is LayerGradeNode newGrade && LayerEdits.Branch(graph, mix).Contains(newGrade.Id) &&
                       graph.Nodes.Count == count + 2,
                       "Korrektur mit dieser Maske: eine neue Ebene, die Maske im Faktor, ihre Korrektur gewaehlt");
            Check.That(NodeLayerList.Of(graph).Any(l => ReferenceEquals(l.Mix, mix)), "und die neue Ebene steht in der Liste der Ebenen");

            page.StepNodes(back: true);
            Check.That(page.Graph!.Nodes.Count == count && !page.Graph.Links.Any(l => l.From == free.Id),
                       "Rueckgaengig nimmt Ebene und Kabel in einem Schritt zurueck");
            graph = page.Graph!;

            // Objekte waehlen: Die Maske bleibt das Ziel, das Werkzeug wird "Auswaehlen".
            var crypto = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Cryptomatte);
            page.ShowQuickPanel(crypto);
            page.QuickPanel!.Invoke(T("S_QuickSelectObjects"));
            Pump(() => false, 0.1);
            Check.That((AtelierTool)typeof(AtelierPage).GetField("_tool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)! == AtelierTool.Select && ReferenceEquals(editor.Selected, crypto),
                       "Objekte waehlen: das Werkzeug Auswaehlen, und die Maske bleibt gewaehlt");

            // Ohne Kryptomatte in der Datei gibt es kein "Objekt als Maske".
            page.ShowQuickPanel(ownedMix);
            Check.That(!page.QuickPanel!.Items.Contains(T("S_QuickObjectMask")), "ohne Kryptomatte in der Datei: kein \"Objekt als Maske\"");
            page.QuickPanel.Close();

            // Die Ausgabe: nichts hinzuzufuegen, kein Betrachter.
            var output = graph.Nodes.OfType<OutputNode>().Single();
            page.ShowQuickPanel(output);
            Check.That(page.QuickPanel is null, "an der Ausgabe: kein Feld, denn dort taete nichts etwas");
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

    private static string? Heading(FlipMenu menu)
        => ((Panel)((Border)menu.Surface).Child).Children.OfType<TextBlock>().FirstOrDefault()?.Text;

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
