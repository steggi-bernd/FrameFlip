using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Tastenhilfe in der Statuszeile (docs/Atelier-Tastenhilfe.md): was sie je Lage zeigt, dass
/// jede Taste in ihr auf der Seite wirklich etwas tut, und dass die Seite Wechsel meldet.
/// </summary>
public static class AtelierHintInvariants
{
    public static void Run()
    {
        Table();
        ThePageReports();
    }

    private static bool Has(IReadOnlyList<KeyHint> hints, string action, params string[] keys)
        => hints.Any(h => h.ActionKey == action && (keys.Length == 0 || h.Keys.SequenceEqual(keys)));

    private static void Table()
    {
        Check.Group("Tastenhilfe: je Lage");

        var image = new HintContext { HasImage = true };

        var select = AtelierHints.For(image with { Tool = AtelierTool.Select });
        Check.That(Has(select, "S_HintSelectPick", "Click") && Has(select, "S_HintSelectAdd", "Shift", "Click") &&
                   Has(select, "S_HintSelectRemove", "Alt", "Click"),
                   "Auswaehlen: Klick waehlt, Umschalt fuegt hinzu, Alt nimmt weg");

        var brush = AtelierHints.For(image with { Tool = AtelierTool.Brush });
        Check.That(Has(brush, "S_HintPaint", "Drag") && Has(brush, "S_HintErase", "RightDrag") && Has(brush, "S_HintLine", "Shift", "Click"),
                   "Pinsel: ziehen malt, rechts radiert, Umschalt+Klick zieht eine Linie");

        var alt = AtelierHints.For(image with { Tool = AtelierTool.Brush, Held = ModifierKeys.Alt });
        Check.That(alt.Count > 0 && alt.All(h => (h.Needs & ModifierKeys.Alt) != 0) && Has(alt, "S_HintErase", "Alt", "Drag"),
                   "Alt gehalten: nur noch, was mit Alt geht");

        var rectangle = AtelierHints.For(image with { Tool = AtelierTool.Brush, Area = PaintArea.Rectangle });
        Check.That(Has(rectangle, "S_HintArea") && Has(rectangle, "S_HintAreaEven", "Shift", "Drag") && !Has(rectangle, "S_HintLine"),
                   "Rechteck: aufziehen, mit Umschalt gleichmaessig - keine gerade Linie");

        var waiting = AtelierHints.For(image with { Tool = AtelierTool.Pick, ColourWaiting = true });
        Check.That(waiting.Count == 2 && Has(waiting, "S_HintTakeColour", "Click") && Has(waiting, "S_HintCancel", "Esc"),
                   "eine Pipette wartet: wohin klicken und wie heraus, sonst nichts");

        var layers = AtelierHints.For(image with { OverLayers = true });
        var layersAlt = AtelierHints.For(image with { OverLayers = true, Held = ModifierKeys.Alt });
        Check.That(Has(layers, "S_HintLayerChoose") && Has(layersAlt, "S_HintLayerSolo") && Has(layersAlt, "S_HintMaskSolo") &&
                   layersAlt.All(h => (h.Needs & ModifierKeys.Alt) != 0),
                   "ueber der Ebenenliste: waehlen, und mit Alt Ebene oder Maske allein");

        Check.That(AtelierHints.For(new HintContext()) is { Count: 1 } empty && Has(empty, "S_HintSearch"),
                   "ohne Bild: nur die Suche");

        Check.That(Has(AtelierHints.For(image with { CurvesShown = true }), "S_HintCurvePoint", "Ctrl", "Click") &&
                   !Has(AtelierHints.For(image), "S_HintCurvePoint"),
                   "Strg+Klick fuer den Kurvenpunkt nur, wenn die Kurven zu sehen sind");

        // Jede Lage passt in eine Zeile, und jeder Text steht im Woerterbuch.
        var contexts = new List<HintContext> { new(), image };
        foreach (AtelierTool tool in Enum.GetValues<AtelierTool>())
            foreach (PaintArea area in Enum.GetValues<PaintArea>())
                foreach (var held in new[] { ModifierKeys.None, ModifierKeys.Alt, ModifierKeys.Control, ModifierKeys.Shift })
                    foreach (bool over in new[] { false, true })
                        contexts.Add(image with { Tool = tool, Area = area, Held = held, OverLayers = over, CurvesShown = over, Escapable = over });

        var every = contexts.Select(AtelierHints.For).ToList();
        var missing = every.SelectMany(h => h).SelectMany(h => h.Keys.Select(AtelierHints.KeyName).Append(h.Action))
                           .Where(text => text.StartsWith("S_")).Distinct().ToList();

        Check.That(every.All(h => h.Count is > 0 and <= 9), "jede Lage hat Hinweise, hoechstens neun");
        Check.That(missing.Count == 0, "jeder Hinweis und jede Taste hat einen Text", string.Join(", ", missing));
    }

    private static void ThePageReports()
    {
        Check.Group("Tastenhilfe: auf der Seite");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-hints-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        const int W = 64, H = 32;
        string path = Path.Combine(root, "render_0001.png");
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 90; pixels[i + 1] = 120; pixels[i + 2] = 150; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = File.Create(path)) encoder.Save(file);

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var column = (ToolColumn)page.FindName("MouseTools");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);

            int reports = 0;
            page.HintsChanged += _ => reports++;

            // Jede Werkzeugtaste der Hilfe wirkt auf der Seite - sonst luegt die Hilfe.
            var wrong = AtelierHints.ToolKeys.Where(t => t.Tool != AtelierTool.Nodes)
                                   .Where(t => !page.HandleToolKey(t.Key) || column.Tool != t.Tool)
                                   .Select(t => t.Key.ToString()).ToList();
            Check.That(wrong.Count == 0, "jede Werkzeugtaste der Hilfe waehlt ihr Werkzeug", string.Join(", ", wrong));
            Check.That(page.HandleToolKey(Key.J) && page.HandleToolKey(Key.J) && page.HandleToolKey(Key.J),
                       "J wirkt (Sichthilfe einmal ringsum)");
            Check.That(page.HandleToolKey(Key.O) && page.ShowingOriginal && page.HandleToolKey(Key.O) && !page.ShowingOriginal,
                       "O wirkt (Original hin und zurueck)");

            column.Select(AtelierTool.Brush, notify: true);
            page.RefreshHints();
            Check.That(Has(page.CurrentHints, "S_HintPaint") && reports > 0, "Pinsel gewaehlt: die Hilfe zeigt Malen und meldet es");

            int before = reports;
            page.RefreshHints();
            Check.That(reports == before, "ohne Aenderung keine neue Meldung");

            column.Select(AtelierTool.Select, notify: true);
            page.RefreshHints();
            Check.That(Has(page.CurrentHints, "S_HintSelectAdd") && reports == before + 1, "Auswaehlen gewaehlt: Umschalt und Alt stehen da");
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

    private static void Pump(Func<bool> until, double seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until())
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
