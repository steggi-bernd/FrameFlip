using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

using Image = System.Windows.Controls.Image;

namespace FrameFlip.Tests;

/// <summary>
/// "Auswahl -> Aktion -> Ergebnis" mit Kryptomatte-Objekten (docs/Atelier-Arbeitsablauf.md, C3b):
/// Ohne Kryptomatte-Maske im Ziel legt ein Klick eine vorlaeufige Auswahl an - Klick ersetzt,
/// Umschalt fuegt hinzu, Alt nimmt heraus -, und eine Aktion macht daraus eine Maskenebene.
/// Vorher tat ein Klick im Knotenmodus ohne gewaehlten Maskenknoten still nichts.
/// </summary>
public static class CryptoSelectionInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        Check.Group("Auswahl: waehlen, dann handeln");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-auswahl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string nodes = Render(root, "knoten");
        string stack = Render(root, "stapel");

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var editor = (NodeEditor)page.FindName("NodeView");
        var outline = (Image)page.FindName("CryptoSelection");
        var layers = (LayerPanel)page.FindName("Layers");

        IReadOnlyList<string> Selected() => page.Recipe.Target is EditingTarget.CryptoSelection s ? s.Picks.Select(p => p.Name).ToList() : Array.Empty<string>();

        try
        {
            window.Show();

            // ------------------------------------------------------------ Knoten
            Show(page, nodes);
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            editor.Select(null);
            page.HandleToolKey(Key.W);
            Pump(() => page.PickAt(1, 2));

            Check.That(Selected() is ["Kugel"] && page.TargetSteps.Single().Text == T("S_TargetSelection", "Kugel"),
                       "ohne Maskenknoten: ein Klick legt eine Auswahl an, und die Zielzeile nennt sie",
                       string.Join(", ", Selected()));
            Pump(() => outline.Source is not null);
            Check.That(outline.Source is not null, "die Auswahl ist umrandet");

            Pump(() => page.PickAt(6, 2, AtelierPage.PickMode.Add));
            Check.That(Selected() is ["Kugel", "Boden"], "Umschalt fuegt hinzu");
            page.PickAt(6, 2, AtelierPage.PickMode.Remove);
            Check.That(Selected() is ["Kugel"], "Alt nimmt heraus");
            page.PickAt(6, 2);
            Check.That(Selected() is ["Boden"], "ein Klick ersetzt");
            page.PickAt(6, 2);
            Check.That(page.Recipe.Target is EditingTarget.PictureTarget, "ein Klick auf das einzige gewaehlte Objekt nimmt es heraus - das Ziel ist wieder das Bild");

            page.PickAt(1, 2);
            var graph = page.Graph!;
            int count = graph.Nodes.Count;
            ((Button)page.FindName("TargetCorrect")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var mask = graph.Nodes.OfType<MaskNode>().SingleOrDefault(m => m.Mask.Kind == MaskKind.Cryptomatte);
            Check.That(mask is not null && mask.Mask.Picks.Select(p => p.Name).SequenceEqual(new[] { "Kugel" }) && graph.Nodes.Count > count,
                       "\"+ Korrektur\" macht aus der Auswahl eine Maskenebene mit genau dieser Kryptomatte");
            Check.That(editor.Selected is LayerGradeNode grade && graph.Into(mask!.Id, "Ebene")?.From == grade.Id &&
                       page.TargetSteps.Count == 2 && page.TargetSteps[0].Text == T("S_TargetLayer", "Kugel"),
                       "und ihre Korrektur ist gewaehlt - die Zielzeile sagt Ebene: Kugel",
                       string.Join(" › ", page.TargetSteps.Select(s => s.Text)));

            editor.Select(null);
            page.PickAt(6, 2);
            page.UseTool(ToolCatalog.All.Single(e => e.TitleKey == "S_Vignette"));
            var second = graph.Nodes.OfType<MaskNode>().Single(m => m.Mask.Picks.Any(p => p.Name == "Boden"));
            var layer = NodeLayerList.Of(graph).Single(l => ReferenceEquals(l.MaskSource, second));
            var vignette = graph.Nodes.OfType<OpticsNode>().Single(n => n.Tool is VignetteTool);
            Check.That(LayerEdits.Branch(graph, layer.Mix!).Contains(vignette.Id),
                       "ein Effekt auf eine Auswahl: eine Maskenebene fuer sie, und der Effekt in ihrem Zweig");

            // ------------------------------------------------------------ Stapel
            Show(page, stack);
            page.HandleToolKey(Key.W);
            Pump(() => page.PickAt(1, 2));
            Check.That(Selected() is ["Kugel"], "im Stapel: dieselbe Auswahl");

            ((Button)page.FindName("TargetCorrect")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(layers.Selection is { Content: LayerContent.Adjustment, Name: "Kugel" } made &&
                       made.Mask is { Kind: MaskKind.Cryptomatte, Picks: [{ Name: "Kugel" }] },
                       "\"+ Korrektur\": eine Maskenebene \"Kugel\", gewaehlt - der Farbstreifen gilt ihr");

            Pump(() => page.PickAt(6, 2, AtelierPage.PickMode.Add));
            Check.That(layers.Selection!.Mask.Picks.Select(p => p.Name).SequenceEqual(new[] { "Kugel", "Boden" }),
                       "an der Maske der Ebene: Umschalt fuegt ihr hinzu");
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

    private static void Show(AtelierPage page, string path)
    {
        page.Open(path);
        Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0 && page.Projects.Current is { } key && key.Equals(SequenceKey.Of(path)));
        Pump(() => false, 0.3);
    }

    /// <summary>Ein Render mit Kryptomatte: die Kugel links, der Boden rechts.</summary>
    private static string Render(string root, string folder)
    {
        string path = Path.Combine(root, folder, "render_0001.exr");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, ExrMultipartInvariants.Build(new[]
        {
            new ExrMultipartInvariants.Part("Bild", "scanlineimage", new[] { "B", "G", "R" }, (c, x, y) => 0.2f + c * 0.1f,
                new Dictionary<string, string>
                {
                    ["cryptomatte/abc1234/name"] = "CryptoObject",
                    ["cryptomatte/abc1234/hash"] = "MurmurHash3_32",
                    ["cryptomatte/abc1234/conversion"] = "uint32_to_float32",
                    ["cryptomatte/abc1234/manifest"] = """{"Kugel":"3f800000","Boden":"40000000"}""",
                }),
            new ExrMultipartInvariants.Part("CryptoObject00", "scanlineimage",
                new[] { "CryptoObject00.a", "CryptoObject00.b", "CryptoObject00.g", "CryptoObject00.r" },
                (c, x, y) => c == 3 ? (x < 4 ? 1f : 2f) : c == 2 ? 1f : 0f),
        }));

        return path;
    }

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
