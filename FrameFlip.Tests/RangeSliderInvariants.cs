using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Der Bereichsregler (docs/Atelier-Arbeitsablauf.md, C7): vier Griffe, je zwei als Paar - das
/// Paar zieht zusammen, der aeussere Griff oder Alt trennt es, und die Kante wird weich.
/// </summary>
public static class RangeSliderInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        HandlesMove();
        MasksKeepTheirEdges();
        ThePanelsUseIt();
    }

    private static void HandlesMove()
    {
        Check.Group("Bereichsregler: Griffe und Paare");

        var unit = RangeScale.Unit;
        var w = new RangeWindow(0.2f, 0.8f, 0.1f, 0.1f);

        var pair = RangeWindows.Drag(w, RangeHandle.Low, 0.1f, alone: false, unit);
        Check.That(Near(pair.Low, 0.3f) && Near(pair.SoftLow, 0.1f) && Near(pair.OuterLow, 0.2f),
                   "der innere Griff zieht sein Paar mit - die Kante bleibt so weich, wie sie war", $"{pair}");

        var outer = RangeWindows.Drag(w, RangeHandle.OuterLow, -0.1f, alone: false, unit);
        Check.That(Near(outer.Low, 0.2f) && Near(outer.SoftLow, 0.2f), "der aeussere Griff allein macht die Kante weicher", $"{outer}");

        var alt = RangeWindows.Drag(w, RangeHandle.High, -0.1f, alone: true, unit);
        Check.That(Near(alt.High, 0.7f) && Near(alt.OuterHigh, 0.9f) && Near(alt.SoftHigh, 0.2f),
                   "mit Alt zieht der innere allein - das Paar trennt sich, der aeussere bleibt stehen", $"{alt}");

        var over = RangeWindows.Drag(w, RangeHandle.Low, 0.9f, alone: false, unit);
        var under = RangeWindows.Drag(w, RangeHandle.Low, -0.9f, alone: false, unit);
        Check.That(over.Low <= over.High && Near(under.Low, 0f), "Von ueberholt Bis nicht und bleibt auf der Skala", $"{over} {under}");

        var hard = new RangeWindow(0.3f, 0.7f, 0f, 0f);
        Check.That(RangeWindows.HandleAt(hard, 0.3f, 0.02f, alt: false, unit) == RangeHandle.Low &&
                   RangeWindows.HandleAt(hard, 0.3f, 0.02f, alt: true, unit) == RangeHandle.OuterLow &&
                   RangeWindows.HandleAt(hard, 0.5f, 0.02f, alt: false, unit) is null,
                   "eine harte Kante: ohne Alt greift man das Paar, mit Alt zieht man es auseinander");

        var softer = RangeWindows.Drag(new RangeWindow(0.2f, 0.8f, 0.1f, 0.8f), RangeHandle.Low, 0.05f, alone: false, unit);
        Check.That(Near(softer.SoftHigh, 0.8f), "ein Zug an einer Seite stutzt die andere nicht, auch wenn sie weicher ist als der Regler");

        // Der Farbkreis: ein Fenster ueber die Null hinweg, und es wandert beliebig oft herum.
        var hue = RangeScale.Hue;
        var red = new RangeWindow(340f, 380f, 0f, 0f);
        Check.That(RangeWindows.Cover(red, 10f, hue) == 1f && RangeWindows.Cover(red, 350f, hue) == 1f &&
                   RangeWindows.Cover(red, 180f, hue) == 0f,
                   "auf dem Farbkreis: ein Fenster ueber Rot reicht ueber die Null");

        // Dreissig Schritte, abwechselnd oben und unten: das Fenster wandert zweieinhalb Mal herum.
        var round = red;
        for (int i = 0; i < 30; i++)
        {
            round = RangeWindows.Drag(round, RangeHandle.High, 30f, alone: false, hue);
            round = RangeWindows.Drag(round, RangeHandle.Low, 30f, alone: false, hue);
        }
        float centre = (round.Low + round.High) / 2f;
        Check.That(centre >= 0f && centre < 360f, "wie oft es auch herumwandert, die Mitte bleibt in einer Runde", $"{round}");

        Check.That(Near(RangeWindows.Cover(w, 0.15f, unit), Masking.Band(0.15f, 0.2f, 0.8f, 0.1f)),
                   "der Regler zeigt dieselbe Kante, die die Maske rechnet");
    }

    private static void MasksKeepTheirEdges()
    {
        Check.Group("Bereichsregler: Masken mit getrennten Kanten");

        Check.That(Masking.Band(0.15f, 0.2f, 0.8f, 0.1f, 0.1f) == Masking.Band(0.15f, 0.2f, 0.8f, 0.1f),
                   "mit zwei gleichen Kanten rechnet der Bereich genau wie vorher");
        Check.That(Masking.Band(0.1f, 0.2f, 0.8f, 0f, 0.3f) == 0f && Masking.Band(0.95f, 0.2f, 0.8f, 0f, 0.3f) > 0f,
                   "getrennt: unten hart, oben weich");

        var mask = new LayerMask { Kind = MaskKind.Luminance };
        RangeWindows.Apply(mask, new RangeWindow(0.3f, 0.6f, 0.1f, 0.1f));
        string joined = JsonSerializer.Serialize(mask);
        Check.That(mask.SoftLow is null && !joined.Contains("SoftLow"),
                   "gleiche Kanten: das Paar ist vereint, das Rezept sieht aus wie vor dem Trennen");

        RangeWindows.Apply(mask, new RangeWindow(0.3f, 0.6f, 0f, 0.25f));
        string split = JsonSerializer.Serialize(mask);
        var back = JsonSerializer.Deserialize<LayerMask>(split)!;
        Check.That(back.LowSoftness == 0f && Near(back.HighSoftness, 0.25f), "getrennte Kanten werden gespeichert und gelesen");

        var old = JsonSerializer.Deserialize<LayerMask>("{\"Kind\":1,\"Low\":0.2,\"High\":0.9,\"Softness\":0.15}")!;
        Check.That(old.SoftLow is null && Near(old.LowSoftness, 0.15f) && Near(old.HighSoftness, 0.15f),
                   "ein altes Rezept ohne getrennte Kanten liest sich unveraendert");

        var clone = back.Clone();
        Check.That(clone.SoftLow == back.SoftLow && clone.SoftHigh == back.SoftHigh, "eine Kopie behaelt die getrennten Kanten");

        // Der Farbbereich: um Blaugruen (180 Grad), unten weich, oben hart.
        var colour = new LayerMask { Kind = MaskKind.Colour };
        RangeWindows.Apply(colour, new RangeWindow(160f, 200f, 60f, 0f));
        Check.That(Near(colour.Hue, 180f) && Near(colour.Spread, 20f), "der Farbbereich: Mitte und Weite aus den beiden Grenzen");

        float Factor(float r, float g, float b)
            => MaskSampler.Prepare(colour, new Dictionary<string, FloatFrame>(), 1, 1, 0).Factor(0, 0, 1, 1, 0, 0f, 0f, 0f, r, g, b);

        // Der Farbbereich liest den Untergrund. Farbton 140 (gruenlich) liegt 20 Grad unter der
        // Grenze, 220 (blaeulich) 20 darueber.
        var (lr, lg, lb) = Rgb(140f);
        var (hr, hg, hb) = Rgb(220f);
        Check.That(Factor(lr, lg, lb) > 0.4f && Factor(hr, hg, hb) == 0f,
                   "im Bild: unter dem Farbton die weiche Kante, darueber die harte",
                   $"{Factor(lr, lg, lb):0.00} / {Factor(hr, hg, hb):0.00}");
    }

    private static void ThePanelsUseIt()
    {
        Check.Group("Bereichsregler: im Streifen und im Knotenmodus");

        // Das Element fuer sich: Griff greifen, ziehen, loslassen - wie mit der Maus.
        var slider = new RangeSlider { Window = new RangeWindow(0.2f, 0.8f, 0.1f, 0.1f) };
        slider.Measure(new Size(212, 32));
        slider.Arrange(new Rect(0, 0, 212, 32));

        var seen = new List<(RangeWindow Window, bool Interim)>();
        slider.Changed += (w, interim) => seen.Add((w, interim));

        double low = slider.ScreenOf(RangeHandle.Low);
        Check.That(slider.Grab(low, alt: false), "am inneren Griff laesst sich greifen");
        slider.MoveTo(low + 20);
        slider.Release();
        Check.That(seen.Count == 2 && seen[0].Interim && !seen[1].Interim && Near(seen[1].Window.Low, 0.3f),
                   "zwanzig Punkte nach rechts auf zweihundert: die Grenze wandert um 0,1, erst waehrend, dann beim Loslassen",
                   string.Join(" ", seen.Select(s => $"{s.Window.Low:0.00}{(s.Interim ? "~" : "")}")));

        slider.Grab(slider.ScreenOf(RangeHandle.High), alt: false);
        slider.MoveTo(slider.ScreenOf(RangeHandle.High) - 100, fine: true);
        slider.Release();
        Check.That(Near(slider.Window.High, 0.75f), "mit Umschalt ein Zehntel so schnell", $"{slider.Window.High}");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-range-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[40 * 20 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 140; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 160)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var layers = (LayerPanel)page.FindName("Layers");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            // Die Ebenen nach vorn: Der Streifen rechnet nur, wenn er zu sehen ist.
            ((DockHost)page.FindName("Dock")).Activate("layers");
            Pump(() => layers.IsLoaded);

            layers.AddAdjustment();
            var box = (ComboBox)layers.FindName("MaskBox");
            box.SelectedIndex = box.Items.IndexOf(T("S_MaskLuminance"));
            var mask = layers.Selection!.Mask;

            var range = (RangeSlider)layers.FindName("MaskRange");
            var row = (FrameworkElement)layers.FindName("MaskRangeRow");
            var lowSlider = (Slider)layers.FindName("MaskLowSlider");
            var softSlider = (Slider)layers.FindName("MaskSoftSlider");

            Check.That(mask.Kind == MaskKind.Luminance && row.Visibility == Visibility.Visible,
                       "Helligkeitsmaske im Streifen: der Bereichsregler steht da");

            range.Measure(new Size(212, 32));
            range.Arrange(new Rect(0, 0, 212, 32));

            double x = range.ScreenOf(RangeHandle.Low);
            range.Grab(x, alt: false);
            range.MoveTo(x + 40);
            range.Release();
            Check.That(Near(mask.Low, 0.2f) && Math.Abs(lowSlider.Value - 0.2) < 0.001,
                       "ein Zug am Bereichsregler: Von steht in der Maske und im Regler darunter", $"{mask.Low} / {lowSlider.Value}");

            x = range.ScreenOf(RangeHandle.OuterHigh);
            range.Grab(x, alt: true);
            range.MoveTo(x + 40);
            range.Release();
            Check.That(mask.SoftHigh is > 0.25f && mask.SoftLow is not null,
                       "der aeussere Griff oben, mit Alt aus der harten Kante am Ende der Skala: das Paar ist getrennt",
                       $"{mask.SoftLow} / {mask.SoftHigh}");

            softSlider.Value = 0.2;
            Check.That(mask.SoftLow is null && mask.SoftHigh is null && Near(mask.Softness, 0.2f),
                       "der Regler Weich fuegt das Paar wieder zusammen");

            typeof(LayerPanel).GetMethod("OnMaskRangeReset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(layers, null);
            Check.That(mask.Low == 0f && mask.High == 1f && Near(mask.Softness, 0.1f), "Doppelklick: das Fenster in Grundstellung");

            // Im Knotenmodus: dieselbe Rechnung, als Feld der Maske - mit Rueckgaengig.
            mask.Low = 0.1f;
            page.ConvertToNodes();
            Pump(() => page.InNodes);
            Pump(() => false, 0.3);

            var editor = (NodeEditor)page.FindName("NodeView");
            var node = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Luminance);
            editor.Select(node);
            Pump(() => false, 0.2);

            var tools = (GradingPanel)page.FindName("Tools");
            var fields = (Panel)tools.FindName("NodeFields");
            var nodeRange = Descendants(fields).OfType<RangeSlider>().SingleOrDefault();
            Check.That(nodeRange is not null && Near(nodeRange.Window.Low, 0.1f), "am Maskenknoten: der Bereichsregler zeigt ihr Fenster");

            nodeRange!.Measure(new Size(212, 32));
            nodeRange.Arrange(new Rect(0, 0, 212, 32));
            x = nodeRange.ScreenOf(RangeHandle.Low);
            nodeRange.Grab(x, alt: false);
            nodeRange.MoveTo(x + 40);
            nodeRange.Release();
            Pump(() => false, 0.3);

            var from = Descendants(fields).OfType<Slider>().First();
            Check.That(Near(node.Mask.Low, 0.3f) && Math.Abs(from.Value - 0.3) < 0.001,
                       "ein Zug am Knoten: die Maske folgt, und der Regler Von darunter zieht nach", $"{node.Mask.Low} / {from.Value}");

            page.StepNodes(back: true);
            var restored = page.Graph!.Nodes.OfType<MaskNode>().Single(m => m.Mask.Kind == MaskKind.Luminance);
            Check.That(Near(restored.Mask.Low, 0.1f), "Rueckgaengig nimmt den Zug zurueck", $"{restored.Mask.Low}");
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    /// <summary>Eine gesaettigte Farbe mit diesem Farbton, linear.</summary>
    private static (float R, float G, float B) Rgb(float hue)
    {
        float h = hue / 60f;
        float x = 1f - MathF.Abs(h % 2f - 1f);

        return (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.002f;

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
