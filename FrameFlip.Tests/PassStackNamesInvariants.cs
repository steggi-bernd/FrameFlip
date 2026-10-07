using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Blender 5.2 schreibt die Paesse ausgeschrieben - "Diffuse Direct" statt "DiffDir",
/// "Emission" statt "Emit" -, und mit allen Paessen, aber ohne fertiges Bild, in eine
/// mehrteilige Datei. Der Stapel aus den Paessen muss beide Schreibweisen kennen, die
/// Mischmodi muessen stimmen, und eine Datei ohne fertiges Bild bekommt ihn beim Oeffnen
/// von selbst - sonst zeigt sie nur ihren ersten Farbpass.
/// </summary>
public static class PassStackNamesInvariants
{
    private static readonly Dictionary<string, string> Spelled = new(StringComparer.Ordinal)
    {
        ["DiffDir"] = "Diffuse Direct", ["DiffInd"] = "Diffuse Indirect", ["DiffCol"] = "Diffuse Color",
        ["GlossDir"] = "Glossy Direct", ["GlossInd"] = "Glossy Indirect", ["GlossCol"] = "Glossy Color",
        ["TransDir"] = "Transmission Direct", ["TransInd"] = "Transmission Indirect", ["TransCol"] = "Transmission Color",
        ["VolumeDir"] = "Volume Direct", ["VolumeInd"] = "Volume Indirect",
        ["Emit"] = "Emission", ["Env"] = "Environment",
    };

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "frameflip_passnamen_" + Guid.NewGuid().ToString("N") + ".exr");

        try
        {
            File.WriteAllBytes(path, ExrPassSample.Bytes());
            BothSpellingsBuildTheSameStack(path);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }

        TheModesFollowTheNames();
        AFileWithoutAnImageGetsItsStack();
    }

    /// <summary>Derselbe echte Render, einmal mit Blenders alten, einmal mit den neuen Namen.</summary>
    private static void BothSpellingsBuildTheSameStack(string path)
    {
        Check.Group("Passstapel: beide Schreibweisen");

        var passes = ExrPasses.Of(path);
        var spelled = passes.Where(p => p.ShortName != "Combined")
                            .Select(p => p with { Name = Spelled[p.ShortName] })
                            .ToList();

        var shortStack = PassStack.Rebuild(passes);
        var longStack = PassStack.Rebuild(spelled);

        Check.That(longStack.Layers.Count == shortStack.Layers.Count && longStack.Layers.Count == 16,
                   "ausgeschrieben: derselbe Stapel aus sechzehn Ebenen", $"{longStack.Layers.Count}");
        Check.That(longStack.Layers.Zip(shortStack.Layers).All(p => p.First.Mode == p.Second.Mode && p.First.Clipped == p.Second.Clipped &&
                                                                     p.First.Source == Spelled[p.Second.Source[(p.Second.Source.LastIndexOf('.') + 1)..]]),
                   "Ebene fuer Ebene derselbe Modus, dieselbe Schnittmaske, derselbe Pass");

        // Gerechnet: Mit den Bildern unter den neuen Namen ergibt er dasselbe Bild.
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);
        var spelledSources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);
        foreach (var layer in shortStack.Layers)
        {
            if (sources.ContainsKey(layer.Source) || FloatFrame.FromExrPass(path, layer.Source) is not { } frame) continue;

            sources[layer.Source] = frame;
            spelledSources[Spelled[layer.Source[(layer.Source.LastIndexOf('.') + 1)..]]] = frame;
        }

        var fromShort = LayerComposer.Compose(shortStack, sources);
        var fromLong = LayerComposer.Compose(longStack, spelledSources);
        Check.That(fromShort is not null && fromLong is not null && fromShort.R.AsSpan().SequenceEqual(fromLong.R) &&
                   fromShort.G.AsSpan().SequenceEqual(fromLong.G) && fromShort.B.AsSpan().SequenceEqual(fromLong.B),
                   "und rechnet dasselbe Bild");

        Check.That(PassStack.HasFinishedImage(passes) && !PassStack.HasFinishedImage(spelled),
                   "mit Combined hat die Datei ein fertiges Bild, ohne nicht");
        Check.That(PassStack.HasFinishedImage(new[] { new ExrPass("", "R", "G", "B", null, false) }),
                   "die schmucklosen Kanaele R, G, B sind ein fertiges Bild");
    }

    private static void TheModesFollowTheNames()
    {
        Check.Group("Passstapel: Mischmodi fuer die neuen Namen");

        Check.That(PassRoles.ByName("Diffuse Direct", sceneLinear: true) == BlendMode.Add &&
                   PassRoles.ByName("Glossy Indirect", sceneLinear: true) == BlendMode.Add &&
                   PassRoles.ByName("Emission", sceneLinear: true) == BlendMode.Add,
                   "Licht kommt dazu - auch ausgeschrieben");
        Check.That(PassRoles.ByName("Diffuse Color", sceneLinear: true) == BlendMode.Multiply &&
                   PassRoles.ByName("Transmission Color", sceneLinear: true) == BlendMode.Multiply,
                   "Farbe multipliziert");
        Check.That(PassRoles.ByName("color.png", sceneLinear: false) is null,
                   "eine Bilddatei, die zufaellig color heisst, bleibt ohne Vorgabe");

        Check.That(PassStack.IsBare(null) && PassStack.IsBare(new LayerStack()) &&
                   PassStack.IsBare(new LayerStack { Layers = { new ImageLayer { Source = "", Name = "Farbe" } } }),
                   "ohne Ebenen oder nur mit der unberuehrten Grundebene ist ein Stapel leer");
        Check.That(!PassStack.IsBare(new LayerStack { Layers = { new ImageLayer { Source = "", Name = "Farbe", Opacity = 0.5f } } }) &&
                   !PassStack.IsBare(new LayerStack { Layers = { new ImageLayer(), new ImageLayer { Mode = BlendMode.Add } } }),
                   "eine angefasste Grundebene oder zwei Ebenen sind es nicht");
    }

    /// <summary>
    /// Auf der Seite: eine mehrteilige Datei wie aus Blender 5.2 - Licht, Farbe, Emission, aber
    /// kein fertiges Bild. Sie oeffnet sich mit dem Stapel, der das Bild wieder zusammensetzt.
    /// </summary>
    private static void AFileWithoutAnImageGetsItsStack()
    {
        Check.Group("Passstapel: beim Oeffnen");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-passstapel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string bare = Path.Combine(folder, "ohne", "render_0001.exr");
        string finished = Path.Combine(folder, "mit", "render_0001.exr");
        string converted = Path.Combine(folder, "umgewandelt", "render_0001.exr");
        string worked = Path.Combine(folder, "bearbeitet", "render_0001.exr");
        Directory.CreateDirectory(Path.GetDirectoryName(bare)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finished)!);
        Directory.CreateDirectory(Path.GetDirectoryName(converted)!);
        Directory.CreateDirectory(Path.GetDirectoryName(worked)!);

        static ExrMultipartInvariants.Part Rgba(string name, float r, float g, float b)
            => new(name, "scanlineimage", new[] { $"{name}.A", $"{name}.B", $"{name}.G", $"{name}.R" },
                   (c, _, _) => c switch { 0 => 1f, 1 => b, 2 => g, _ => r });

        var alpha = new ExrMultipartInvariants.Part("Alpha", "scanlineimage", new[] { "Alpha.V" }, (_, _, _) => 1f);
        var light = new[] { alpha, Rgba("Diffuse Direct", 0.5f, 0.4f, 0.2f), Rgba("Diffuse Color", 0.4f, 0.5f, 1f), Rgba("Emission", 0.1f, 0f, 0f) };

        File.WriteAllBytes(bare, ExrMultipartInvariants.Build(light));
        File.WriteAllBytes(finished, ExrMultipartInvariants.Build(light.Append(Rgba("Combined", 0.3f, 0.2f, 0.2f)).ToArray()));
        File.WriteAllBytes(converted, ExrMultipartInvariants.Build(light));
        File.WriteAllBytes(worked, ExrMultipartInvariants.Build(light));

        // Zwei Projekte, wie sie entstehen, wenn jemand die Datei zuerst mit nur einer Ebene sah
        // und in den Knotenmodus ging: einmal nur umgewandelt, einmal mit eigener Arbeit darin.
        var bareStack = new LayerStack { Layers = { new ImageLayer { Source = "", Name = "Image" } } };
        var plain = Imaging.Nodes.StackToGraph.Convert(bareStack, null, null);
        var busy = Imaging.Nodes.StackToGraph.Convert(bareStack, null, null);
        busy.Add(new Imaging.Nodes.OpticsNode { Tool = new VignetteTool() });

        var store = new Atelier.AtelierProjectStore();
        store.Save(Atelier.SequenceKey.Of(converted)!, new Atelier.AtelierProject
        {
            Layers = bareStack, Nodes = System.Text.Json.Nodes.JsonNode.Parse(plain.Save()),
        });
        store.Save(Atelier.SequenceKey.Of(worked)!, new Atelier.AtelierProject
        {
            Layers = bareStack, Nodes = System.Text.Json.Nodes.JsonNode.Parse(busy.Save()),
        });

        Check.That(Imaging.Nodes.StackToGraph.IsPlain(plain) && !Imaging.Nodes.StackToGraph.IsPlain(busy),
                   "ein nur umgewandelter Graph ist leer, einer mit einem Effekt nicht");

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1000, Height = 700, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var layers = (LayerPanel)page.FindName("Layers");

        void Show(string path)
        {
            page.Open(path);
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < end && !(page.Projects.Current is { } key && key.Equals(Atelier.SequenceKey.Of(path)) &&
                                               ((System.Windows.Controls.TextBlock)page.FindName("SourceText")).Text.Length > 0))
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(5);
            }
        }

        try
        {
            window.Show();

            Show(bare);
            var stack = layers.Stack.Layers;
            Check.That(stack.Count == 3 && stack[0].Source == "Diffuse Direct" && stack[0].Mode == BlendMode.Normal &&
                       stack[1].Source == "Diffuse Color" && stack[1].Mode == BlendMode.Multiply && stack[1].Clipped &&
                       stack[2].Source == "Emission" && stack[2].Mode == BlendMode.Add,
                       "ohne fertiges Bild: Licht, darauf seine Farbe, dazu die Emission - von selbst",
                       string.Join(" | ", stack.Select(l => $"{l.Source} {l.Mode}{(l.Clipped ? " ⌐" : "")}")));
            Check.That(page.Recipe.Layers?.Layers.Count == 3, "und der Stapel steht im Rezept des Projekts");

            Show(finished);
            Check.That(layers.Stack.Layers.Count == 1 && layers.Stack.Layers[0].Source == "",
                       "mit fertigem Bild bleibt es beim Bild - Blenders Combined ist entrauscht, die Paesse nicht",
                       string.Join(" | ", layers.Stack.Layers.Select(l => l.Source)));

            // Im Knotenmodus, der Graph nur umgewandelt: neu umgewandelt aus dem Passstapel.
            Show(converted);
            var shown = page.Graph is { } graph ? NodeLayerList.Of(graph) : Array.Empty<NodeLayer>();
            Check.That(page.InNodes && shown.Count == 3 && page.Recipe.Layers?.Layers.Count == 3,
                       "war der Graph nur umgewandelt, bekommt er alle Ebenen - und bleibt im Knotenmodus",
                       $"InNodes={page.InNodes}, {shown.Count} Ebenen im Graphen");

            // Mit eigener Arbeit im Graphen: nichts wird ueberschrieben.
            Show(worked);
            Check.That(page.InNodes && page.Graph!.Nodes.OfType<Imaging.Nodes.OpticsNode>().Any(n => n.Tool is VignetteTool) &&
                       NodeLayerList.Of(page.Graph).Count == 1,
                       "steckt eigene Arbeit im Graphen, bleibt er, wie er ist");
        }
        finally
        {
            window.Close();
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
