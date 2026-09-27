using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Wohin ein Regler, ein Pinselstrich und ein Effekt aus der Werkzeugleiste gehen - je nach
/// Modus und Auswahl (docs/Atelier-Arbeitsablauf.md, Phase B; Refactoring-Studio, S2 zweiter
/// Teil). Die Probe haelt das heutige Verhalten von aussen fest, bevor das Bearbeitungsziel in
/// die Sitzung wandert, und muss davor und danach unveraendert gruen laufen.
/// </summary>
public static class EditingTargetInvariants
{
    private const int Width = 200, Height = 120;

    public static void Run()
    {
        Check.Group("Bearbeitungsziel: wohin Regler, Strich und Effekt gehen");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-ziel-" + Guid.NewGuid().ToString("N")[..8]);
        string first = Png(root, "eins", "render_0001.png");
        string second = Png(root, "zwei", "shot_0001.png");

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1200, Height = 860, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var tools = (GradingPanel)page.FindName("Tools");
        var exposure = (Slider)tools.FindName("ExposureSlider");
        var layers = (LayerPanel)page.FindName("Layers");
        var editor = (NodeEditor)page.FindName("NodeView");
        var frame = (PlacementAdorner)page.FindName("Placement");

        try
        {
            window.Show();

            // ------------------------------------------------------------ Stapel
            Show(page, first);

            var ground = layers.Stack.Layers.Single();
            exposure.Value = 0.5;
            Check.That(Same(page.Recipe.Adjustments?.Exposure, 0.5) && Same(ground.Adjustments?.Exposure ?? 0, 0),
                       "Stapel, Grundebene gewaehlt: der Regler wirkt auf das fertige Bild");

            Call(page, "OnTargetChosen", true);
            Check.That(Same(exposure.Value, 0), "im Farbstreifen \"Ebene\" gewaehlt: der Regler zeigt die Ebene", $"{exposure.Value}");
            exposure.Value = 0.3;
            Check.That(Same(ground.Adjustments?.Exposure, 0.3) && Same(page.Recipe.Adjustments?.Exposure, 0.5),
                       "und wirkt auf die Ebene - das Bild behaelt seinen Wert");

            Call(page, "OnTargetChosen", false);
            Check.That(Same(exposure.Value, 0.5), "zurueck auf \"Bild\": der Regler zeigt wieder das Bild");

            layers.AddAdjustment();
            var adjustment = layers.Selection!;
            Check.That(adjustment.Content == LayerContent.Adjustment && Same(exposure.Value, 0),
                       "eine Einstellungsebene angelegt und gewaehlt: der Regler gehoert ihr");
            exposure.Value = 0.2;
            Check.That(Same(adjustment.Adjustments?.Exposure, 0.2) && Same(page.Recipe.Adjustments?.Exposure, 0.5),
                       "und wirkt nur auf sie");

            // Der erste Strich auf einer Einstellungsebene ohne gemalte Maske legt eine eigene
            // Maskenebene an - er malt nicht in die gewaehlte.
            page.HandleToolKey(Key.B);
            page.UpdateLayout();
            int before = layers.Stack.Layers.Count;
            Stroke(frame, 30, 30, 60, 40);
            Pump(() => false, 0.3);

            var masked = layers.Stack.Layers.Where(l => l.Mask.Kind == MaskKind.Painted).ToList();
            Check.That(layers.Stack.Layers.Count == before + 1 && masked.Count == 1 && ReferenceEquals(layers.Selection, masked[0]) &&
                       adjustment.Mask.Kind == MaskKind.None,
                       "der erste Strich legt eine Maskenebene an, waehlt sie, und die Einstellungsebene bleibt ohne Maske");

            Stroke(frame, 120, 70, 150, 80);
            Pump(() => false, 0.3);
            Check.That(layers.Stack.Layers.Count == before + 1 && masked[0].Mask.PaintFor(1) is { } paint && paint.At(135, 75) > 0.5f,
                       "der zweite Strich malt in dieselbe Maske");

            page.HandleToolKey(Key.V);

            // ------------------------------------------------------------ Knoten
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);
            var graph = page.Graph!;

            var grade = graph.Nodes.OfType<LayerGradeNode>().First(n => Same(n.Adjustments?.Exposure, 0.2));
            editor.Select(grade);
            Pump(() => false, 0.2);
            Check.That(Same(exposure.Value, 0.2), "Knoten, Ebenenkorrektur gewaehlt: der Regler zeigt sie");
            exposure.Value = 0.7;
            Check.That(Same(grade.Adjustments?.Exposure, 0.7), "und wirkt auf den Knoten");

            // Eine Ebene in der Liste gewaehlt: ein Effekt aus der Leiste kommt in ihren Zweig.
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var layer = list.Shown.First(l => l.Mix is { } mix && LayerEdits.Branch(graph, mix).Any(id => graph.Find(id) == grade));
            Call(page, "OnLayerChosen", layer.Target!);
            Check.That(ReferenceEquals(editor.Selected, layer.Mix), "ein Klick in der Liste waehlt das Mischen der Ebene");

            int nodes = graph.Nodes.Count;
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Vignette"));
            Pump(() => graph.Nodes.Count > nodes);
            var vignette = graph.Nodes.OfType<OpticsNode>().Single(n => n.Tool is VignetteTool);
            Check.That(LayerEdits.Branch(graph, layer.Mix!).Contains(vignette.Id),
                       "in der Liste gewaehlt: der Effekt kommt in den Zweig der Ebene, vor ihr Mischen");

            // Etwas anderes im Editor gewaehlt: die Ebene gilt nicht mehr, der Effekt kommt hinter den Knoten.
            var view = graph.Nodes.OfType<ViewNode>().First();
            editor.Select(view);
            nodes = graph.Nodes.Count;
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Grain"));
            Pump(() => graph.Nodes.Count > nodes);
            var grain = graph.Nodes.OfType<OpticsNode>().Single(n => n.Tool is GrainTool);
            Check.That(graph.Into(grain.Id, "Bild")?.From == view.Id && !LayerEdits.Branch(graph, layer.Mix!).Contains(grain.Id),
                       "im Editor gewaehlt: der Effekt kommt hinter den gewaehlten Knoten, nicht in die Ebene");

            // Der Pinsel malt auf die Maske des gewaehlten Mischens.
            var maskMix = graph.Nodes.OfType<MixNode>().First(m => graph.Into(m.Id, "Faktor") is { } f &&
                                                                   graph.Find(f.From) is MaskNode { Mask.Kind: MaskKind.Painted });
            var maskNode = (MaskNode)graph.Find(graph.Into(maskMix.Id, "Faktor")!.From)!;
            editor.Select(maskMix);
            page.HandleToolKey(Key.B);
            page.UpdateLayout();
            nodes = graph.Nodes.Count;
            Stroke(frame, 20, 90, 50, 100);
            Pump(() => false, 0.3);
            Check.That(graph.Nodes.Count == nodes && maskNode.Mask.PaintFor(1) is { } nodePaint && nodePaint.At(35, 95) > 0.5f,
                       "Mischen mit gemalter Maske gewaehlt: der Strich geht in diese Maske, kein neuer Knoten");

            editor.Select(view);
            page.UpdateLayout();
            nodes = graph.Nodes.Count;
            Stroke(frame, 150, 20, 180, 30);
            Pump(() => graph.Nodes.Count > nodes);
            Check.That(graph.Nodes.Count > nodes && !(maskNode.Mask.PaintFor(1)?.At(165, 25) > 0.01f),
                       "etwas ohne Maske gewaehlt: der Strich legt eine neue Maskenebene an und laesst die alte Maske in Ruhe");

            page.HandleToolKey(Key.V);

            // ------------------------------------------------------------ Projektwechsel
            Show(page, second);
            Check.That(!page.InNodes && Same(exposure.Value, 0) && layers.Stack.Layers.Count == 1,
                       "ein anderes Projekt: im Stapel, der Regler zeigt dessen Bild");
            exposure.Value = 0.4;
            Check.That(Same(page.Recipe.Adjustments?.Exposure, 0.4) && Same(layers.Stack.Layers[0].Adjustments?.Exposure ?? 0, 0),
                       "und wirkt auf das Bild - nicht auf eine Ebene, die im alten Projekt gewaehlt war");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static bool Same(double? a, double b) => a is { } value && Math.Abs(value - b) < 1e-6;

    private static object? Call(AtelierPage page, string name, params object[] arguments)
        => typeof(AtelierPage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);

    private static void Show(AtelierPage page, string path)
    {
        string name = Path.GetFileName(path);
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("FileText")).Text == name &&
                   ((TextBlock)page.FindName("SourceText")).Text.Length > 0 &&
                   page.Projects.Current is { } key && key.Equals(SequenceKey.Of(path)));
        Pump(() => false, 0.3);
    }

    /// <summary>Druecken, Ziehen, Loslassen - ueber die Tasten des Rahmens, wie die Maus sie ausloest.</summary>
    private static void Stroke(PlacementAdorner frame, float x0, float y0, float x1, float y1)
    {
        var type = typeof(PlacementAdorner);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        MouseButtonEventArgs Args(RoutedEvent routed)
            => new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed };

        type.GetMethod("PaintDown", flags)!.Invoke(frame, new object[] { Args(UIElement.MouseLeftButtonDownEvent), x0, y0, false });

        for (int i = 1; i <= 8; i++)
            type.GetMethod("PaintMove", flags)!.Invoke(frame, new object[] { x0 + (x1 - x0) * i / 8f, y0 + (y1 - y0) * i / 8f, 1f });

        type.GetMethod("PaintUp", flags)!.Invoke(frame, new object[] { Args(UIElement.MouseLeftButtonUpEvent) });
    }

    private static string Png(string root, string folder, string name)
    {
        string path = Path.Combine(root, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var pixels = new byte[Width * Height * 4];
        for (int i = 0; i < Width * Height; i++)
        {
            pixels[i * 4] = (byte)(i % Width);
            pixels[i * 4 + 1] = 100;
            pixels[i * 4 + 2] = (byte)(i / Width * 2);
            pixels[i * 4 + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4)));
        using var file = File.Create(path);
        encoder.Save(file);

        return path;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
