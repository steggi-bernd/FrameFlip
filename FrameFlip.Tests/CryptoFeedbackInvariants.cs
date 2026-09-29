using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Rueckmeldung beim Waehlen von Kryptomatte-Objekten (docs/Atelier-Arbeitsablauf.md, C3):
/// Name und Hervorhebung unter dem Zeiger, Umriss der Auswahl, Chips in den Werkzeugeinstellungen.
/// Vorher nahm ein Klick ein Objekt auf, ohne dass davon etwas zu sehen war.
/// </summary>
public static class CryptoFeedbackInvariants
{
    private static string T(string key, params object[] args) => Localization.Strings.T(key, args);

    public static void Run()
    {
        CoverageAndOutline();
        ThePageShowsWhatIsPicked();
    }

    /// <summary>Ohne Fenster: Deckung eines Objekts und ihr Umriss.</summary>
    private static void CoverageAndOutline()
    {
        Check.Group("Kryptomatte: Deckung und Umriss");

        const int W = 8, H = 6;
        var level = new FloatFrame { Width = W, Height = H, R = new float[W * H], G = new float[W * H], B = new float[W * H] };
        for (int i = 0; i < W * H; i++)
        {
            level.R[i] = i % W < 4 ? 1f : 2f;
            level.G[i] = 1f;
        }

        var cover = CryptoCoverage.Build(new[] { level }, new[] { 1f }, 1, out int cols, out int rows);
        Check.That(cols == W && rows == H && Enumerable.Range(0, W * H).All(i => cover[i] == (i % W < 4 ? 255 : 0)),
                   "die Deckung eines Objekts: links voll, rechts nichts");

        var edge = CryptoCoverage.Outline(cover, cols, rows);
        Check.That(edge[2 * W + 3] == 255 && edge[2 * W + 1] == 0 && edge[2 * W + 5] == 0,
                   "der Umriss liegt an seiner Kante, nicht innen und nicht daneben");

        var coarse = CryptoCoverage.Build(new[] { level }, new[] { 1f }, 2, out int half, out int halfRows);
        Check.That(half == 4 && halfRows == 3 && coarse[0] == 255 && coarse[3] == 0, "jeder zweite Bildpunkt: halb so gross, dieselbe Deckung");
    }

    /// <summary>Auf der Seite: ein Render mit Kugel und Boden.</summary>
    private static void ThePageShowsWhatIsPicked()
    {
        Check.Group("Kryptomatte: Rueckmeldung auf der Seite");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-krypto-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        string path = Path.Combine(root, "render_0001.exr");
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

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window { Content = page, Width = 1200, Height = 800, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000 };
        var selection = (Image)page.FindName("CryptoSelection");
        var hover = (Image)page.FindName("CryptoHover");
        var properties = (PropertiesPanel)page.FindName("Properties");
        var picks = (WrapPanel)properties.FindName("SelectPicks");
        var under = (TextBlock)properties.FindName("SelectHover");

        try
        {
            window.Show();
            page.Open(path);
            Pump(() => ((TextBlock)page.FindName("SourceText")).Text.Length > 0);
            Pump(() => false, 0.3);

            page.HandleToolKey(Key.W);
            Pump(() => page.HoverAt(1, 2, new Point(40, 40)));

            Check.That(page.HoverAt(1, 2, new Point(40, 40)) && page.HoverText == "Kugel" && hover.Source is not null && under.Text == "Kugel",
                       "ueber der Kugel: ihr Name am Zeiger und in der Leiste, und sie ist hervorgehoben", page.HoverText ?? "kein Name");

            page.HoverAt(6, 2, new Point(80, 40));
            Check.That(page.HoverText == "Boden", "ueber dem Boden: sein Name");

            Check.That(picks.Children.Count == 1 && picks.Children[0] is TextBlock { Text: var none } && none == T("S_SelectNoTarget"),
                       "ohne Kryptomatte-Maske als Ziel: Die Objekte werden nur angezeigt, und das steht da");

            // Die Kugel als Maske (Bildmenue): eine Ebene mit Kryptomatte, die sie waehlt.
            var set = Cryptomatte.Of(path)[0];
            page.HandleToolKey(Key.V);
            Check.That(page.MaskObjectAt(set, 1, 2), "Vorbereitung: die Kugel als Maske");
            page.HandleToolKey(Key.W);
            Pump(() => selection.Source is not null);

            Check.That(selection.Source is not null && picks.Children.Count == 1 && picks.Children[0] is Border,
                       "gewaehlt: der Umriss steht ueber dem Bild, und die Kugel steht als Chip in der Leiste",
                       $"Umriss={selection.Source is not null} Chips={picks.Children.Count} {picks.Children.OfType<object>().FirstOrDefault()?.GetType().Name} " +
                       $"Maske={((LayerPanel)page.FindName("Layers")).Selection?.Mask.Kind} Picks={((LayerPanel)page.FindName("Layers")).Selection?.Mask.Picks.Count} Quelle={((LayerPanel)page.FindName("Layers")).Selection?.Mask.Source}");

            var remove = ((StackPanel)((Border)picks.Children[0]).Child).Children.OfType<Button>().Single();
            remove.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(() => false, 0.2);

            var layers = (LayerPanel)page.FindName("Layers");
            Check.That(layers.Selection!.Mask.Picks.Count == 0 && selection.Source is null && picks.Children.Count == 0,
                       "das Kreuz am Chip nimmt sie aus der Maske - der Umriss geht mit");

            page.HandleToolKey(Key.V);
            Check.That(hover.Source is null && selection.Source is null && page.HoverText is null,
                       "ein anderes Werkzeug: keine Rueckmeldung mehr ueber dem Bild");
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
