using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
/// Die Zielzeile ueber dem Bild (docs/Atelier-Arbeitsablauf.md, C1): Sie sagt, woran gerade
/// gearbeitet wird - aus dem Bearbeitungsziel, also genau das, wohin der naechste Regler geht -,
/// und ein Klick auf ein Glied waehlt es.
/// </summary>
public static class TargetPathInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        ThePathNamesTheTarget();
        ThePageShowsAndFollowsIt();
    }

    /// <summary>Ohne Fenster: welche Glieder zu welchem Ziel gehoeren.</summary>
    private static void ThePathNamesTheTarget()
    {
        Check.Group("Zielzeile: Bild, Ebene, Maske, Knoten");

        var adjustment = new ImageLayer
        {
            Content = LayerContent.Adjustment, Name = "Licht", Adjustments = new ImageAdjustments { Exposure = 0.5 },
            Tools = new GradingStack(), Mask = new LayerMask { Kind = MaskKind.Painted },
        };
        adjustment.Mask.PaintOn(1, 64, 64);

        var stack = new LayerStack { Layers = { new ImageLayer { Content = LayerContent.Pass, Source = "", Name = "Farbe" }, adjustment } };
        var graph = StackToGraph.Convert(stack, ImageAdjustments.Neutral, new GradingStack());

        var layer = NodeLayerList.Of(graph).Single(l => l.Name == "Licht");
        var mix = layer.Mix!;
        var mask = (MaskNode)layer.MaskSource!;
        var grade = graph.Nodes.OfType<LayerGradeNode>().Single(n => LayerEdits.Branch(graph, mix).Contains(n.Id));
        var view = graph.Nodes.OfType<ViewNode>().First();

        string Line(IReadOnlyList<TargetStep> steps) => string.Join(" › ", steps.Select(s => s.Current ? $"[{s.Text}]" : s.Text));

        var picture = TargetPath.For(EditingTarget.Picture, graph).Steps;
        Check.That(Line(picture) == $"[{T("S_TargetPicture")}]", "das Bild: ein Glied, das Ziel selbst", Line(picture));

        var chosen = TargetPath.For(new EditingTarget.GraphNode(mix, FromLayerList: true), graph).Steps;
        Check.That(Line(chosen) == $"[{T("S_TargetLayer", "Licht")}]", "die Ebene aus der Liste: ihr Name", Line(chosen));

        var masked = TargetPath.For(new EditingTarget.GraphNode(mask, FromLayerList: false), graph).Steps;
        Check.That(masked.Count == 2 && masked[0].Text == T("S_TargetLayer", "Licht") && masked[0].GoTo is EditingTarget.GraphNode { FromLayerList: true } back &&
                   ReferenceEquals(back.Node, mix) && masked[1].Current && masked[1].Text == T("S_TargetMask", NodeTitles.MaskName(mask)),
                   "ihre Maske: Ebene › Maske, und die Ebene fuehrt zurueck zur Ebene", Line(masked));

        var inside = TargetPath.For(new EditingTarget.GraphNode(grade, FromLayerList: false), graph).Steps;
        Check.That(inside.Count == 2 && inside[1].Current && inside[1].Text == T("S_TargetNode", NodeTitles.For(grade)),
                   "ein Knoten in ihrem Zweig: Ebene › Knoten", Line(inside));

        var outside = TargetPath.For(new EditingTarget.GraphNode(view, FromLayerList: false), graph).Steps;
        Check.That(outside.Count == 2 && outside[0].GoTo is EditingTarget.PictureTarget && outside[1].Text == T("S_TargetNode", NodeTitles.For(view)),
                   "ein Knoten hinter allen Ebenen: Gesamtbild › Knoten", Line(outside));

        var (onLayer, noNote) = TargetPath.For(new EditingTarget.StackLayer(adjustment, Tools: true), null);
        var (_, note) = TargetPath.For(new EditingTarget.StackLayer(stack.Layers[0], Tools: false), null);
        Check.That(Line(onLayer) == $"[{T("S_TargetLayer", "Licht")}]" && noNote is null && note == T("S_TargetColourOnPicture"),
                   "im Stapel: die Ebene - und ein Hinweis, wenn der Farbstreifen dem Bild gilt");
    }

    /// <summary>Auf der Seite: die Zeile steht da, folgt dem Ziel, und ihre Glieder waehlen.</summary>
    private static void ThePageShowsAndFollowsIt()
    {
        Check.Group("Zielzeile: auf der Seite");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-zielzeile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[160 * 90 * 4];
        for (int i = 0; i < 160 * 90; i++) { pixels[i * 4] = (byte)(i % 160); pixels[i * 4 + 1] = 110; pixels[i * 4 + 2] = (byte)(i / 160 * 2); pixels[i * 4 + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var strip = (FrameworkElement)page.FindName("TargetStrip");
        var note = (TextBlock)page.FindName("TargetNote");
        var layers = (LayerPanel)page.FindName("Layers");
        var editor = (NodeEditor)page.FindName("NodeView");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;

        try
        {
            window.Show();
            Check.That(strip.Visibility != Visibility.Visible, "ohne Bild keine Zielzeile");

            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            Check.That(strip.Visibility == Visibility.Visible && page.TargetSteps.Count == 1 &&
                       page.TargetSteps[0].Text == T("S_TargetLayer", layers.Selection!.Name) &&
                       note.Visibility == Visibility.Visible && note.Text == T("S_TargetColourOnPicture"),
                       "ein Bild offen: die gewaehlte Ebene - und dass der Farbstreifen dem Bild gilt");

            typeof(AtelierPage).GetMethod("OnTargetChosen", flags)!.Invoke(page, new object[] { true });
            Check.That(note.Visibility != Visibility.Visible, "im Farbstreifen \"Ebene\" gewaehlt: der Hinweis geht");

            layers.AddAdjustment();
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);

            var graph = page.Graph!;
            var view = graph.Nodes.OfType<ViewNode>().First();
            editor.Select(view);
            Check.That(page.TargetSteps.Count == 2 && page.TargetSteps[1].Text == T("S_TargetNode", NodeTitles.For(view)),
                       "im Editor gewaehlt: Gesamtbild › Knoten", string.Join(" › ", page.TargetSteps.Select(s => s.Text)));

            page.GoTo(page.TargetSteps[0].GoTo!);
            Check.That(editor.Selected is null && page.Recipe.Target is EditingTarget.PictureTarget && page.TargetSteps.Single().Current,
                       "ein Klick auf \"Gesamtbild\" waehlt ab - das Ziel ist das Bild");

            var list = (NodeLayerList)page.FindName("NodeLayers");
            var layer = list.Shown.First(l => l.Mix is not null);
            typeof(AtelierPage).GetMethod("OnLayerChosen", flags)!.Invoke(page, new object[] { layer.Target! });
            Check.That(page.TargetSteps.Single().Text == T("S_TargetLayer", layer.Name), "in der Liste gewaehlt: die Ebene");

            var grade = graph.Nodes.OfType<LayerGradeNode>().First(n => LayerEdits.Branch(graph, layer.Mix!).Contains(n.Id));
            editor.Select(grade);
            Check.That(page.TargetSteps.Count == 2 && page.TargetSteps[0].GoTo is not null, "ein Knoten in ihr: Ebene › Knoten");

            page.GoTo(page.TargetSteps[0].GoTo!);
            Check.That(ReferenceEquals(editor.Selected, layer.Mix) &&
                       page.Recipe.Target is EditingTarget.GraphNode { FromLayerList: true },
                       "ein Klick auf die Ebene waehlt sie wie in der Liste");

            layer.Mix!.Label = "Glanz";
            typeof(AtelierPage).GetMethod("AfterNodeEdit", flags)!.Invoke(page, null);
            Check.That(page.TargetSteps.Single().Text == T("S_TargetLayer", "Glanz"), "umbenannt: die Zeile heisst mit");
        }
        finally
        {
            window.Close();
            Pump(() => false, 0.2);
            AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
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
