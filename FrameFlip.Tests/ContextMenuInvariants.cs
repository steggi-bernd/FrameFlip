using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Dashboard;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Rechtsklick-Menues ausserhalb des Knoteneditors: die Karten im Farbstreifen und das
/// Dashboard. Explorer und Zwischenablage werden hier nicht ausgeloest - sie gehoeren dem
/// Rechner, auf dem die Probe laeuft.
/// </summary>
public static class ContextMenuInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        CardsCopyPasteAndSwitch();
        DashboardFramesAndSequences();
        ObjectMaskInTheStack();
    }

    /// <summary>"Objekt hier als Maske" im Stapel: eine Maskenebene mit der Kryptomatte dieses Objekts.</summary>
    private static void ObjectMaskInTheStack()
    {
        Check.Group("Rechtsklick: Objekt als Maske im Stapel");

        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frameflip-objekt-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(folder);

        string path = System.IO.Path.Combine(folder, "render_0001.exr");
        System.IO.File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(Decoding.FrameDecoderRegistry.CreateDefault(() => null), new Configuration.AppSettings(), _ => { });
        var window = new Window
        {
            Content = page,
            Width = 1000,
            Height = 700,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);

            while (DateTime.UtcNow < end && size.Text.Length == 0)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var sets = (IReadOnlyList<Decoding.Exr.CryptomatteSet>)typeof(AtelierPage).GetField("_cryptomattes", flags)!.GetValue(page)!;
            var frame = (Imaging.FloatFrame)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page)!;
            var strip = (LayerPanel)page.FindName("Layers");
            int layers = strip.Stack.All().Count();

            bool made = false;

            for (int y = 0; y < frame.Height && !made; y += 3)
                for (int x = 0; x < frame.Width && !made; x += 3)
                    made = page.MaskObjectAt(sets[0], x, y);

            var layer = strip.Selection;

            Check.That(made && strip.Stack.All().Count() == layers + 1 && layer is { Content: LayerContent.Adjustment } &&
                       layer.Mask.Kind == MaskKind.Cryptomatte && layer.Mask.Picks.Count == 1 && layer.Name == layer.Mask.Picks[0].Name,
                       "im Stapel: eine Maskenebene mit der Kryptomatte genau dieses Objekts, nach ihm benannt", layer?.Name);
        }
        finally
        {
            window.Close();
            try { System.IO.Directory.Delete(folder, recursive: true); } catch (System.IO.IOException) { }
        }
    }

    private static void CardsCopyPasteAndSwitch()
    {
        Check.Group("Rechtsklick: die Karten im Farbstreifen");

        var panel = new GradingPanel();
        var window = new Window
        {
            Content = panel,
            Width = 420,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            var palette = (Panel)panel.FindName("Palette");
            var tiles = palette.Children.OfType<Grid>()
                               .SelectMany(row => row.Children.OfType<Panel>())
                               .SelectMany(p => p.Children.OfType<ToggleButton>())
                               .ToDictionary(b => (string)b.Tag);

            tiles["Vignette"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            panel.UpdateLayout();

            var vignette = panel.Stack.Optics.OfType<VignetteTool>().First();
            vignette.Amount = -0.7f;

            var cards = (Panel)panel.FindName("Cards");
            var card = cards.Children.OfType<Border>().First(b => (string?)b.Tag == "Vignette");

            panel.ShowCardMenu("Vignette", card);
            var items = panel.CardMenu!.Items;

            Check.That(new[] { "S_CardMenuOn", "S_CardMenuReset", "S_CardMenuCopy", "S_CardMenuRemove" }.All(k => items.Contains(T(k))) &&
                       !items.Contains(T("S_CardMenuPaste")),
                       "das Menue einer Karte bietet an, was ihre Knoepfe koennen - Einfuegen erst, wenn kopiert ist",
                       string.Join(", ", items));

            panel.CardMenu.Invoke(T("S_CardMenuCopy"));
            vignette.Amount = 0.3f;

            panel.ShowCardMenu("Vignette", card);
            Check.That(panel.CardMenu!.Items.Contains(T("S_CardMenuPaste")), "nach dem Kopieren laesst sich einfuegen");

            panel.CardMenu.Invoke(T("S_CardMenuPaste"));
            Check.That(Math.Abs(panel.Stack.Optics.OfType<VignetteTool>().First().Amount + 0.7f) < 1e-6,
                       "Einfuegen setzt die kopierten Werte", $"{panel.Stack.Optics.OfType<VignetteTool>().First().Amount}");

            // Kopiert ist eine Kopie: Was danach an der Karte gedreht wird, aendert sie nicht.
            panel.Stack.Optics.OfType<VignetteTool>().First().Amount = 0.1f;
            panel.ShowCardMenu("Vignette", card);
            panel.CardMenu!.Invoke(T("S_CardMenuPaste"));

            Check.That(Math.Abs(panel.Stack.Optics.OfType<VignetteTool>().First().Amount + 0.7f) < 1e-6,
                       "das Kopierte bleibt, wie es war - auch nach dem Einfuegen");

            panel.ShowCardMenu("Vignette", card);
            panel.CardMenu!.Invoke(T("S_CardMenuOn"));
            Check.That(panel.Stack.IsBypassed(vignette.Kind), "Eingeschaltet schaltet die Karte aus - die Werte bleiben");

            panel.ShowCardMenu("Vignette", card);
            panel.CardMenu!.Invoke(T("S_CardMenuOn"));
            Check.That(!panel.Stack.IsBypassed(vignette.Kind), "und wieder ein");

            panel.ShowCardMenu("Vignette", card);
            panel.CardMenu!.Invoke(T("S_CardMenuReset"));
            Check.That(Math.Abs(panel.Stack.Optics.OfType<VignetteTool>().First().Amount - new VignetteTool().Amount) < 1e-6,
                       "Zuruecksetzen stellt die Grundwerte her");
        }
        finally
        {
            window.Close();
        }
    }

    private static void DashboardFramesAndSequences()
    {
        Check.Group("Rechtsklick: Bilder und Sequenzen im Dashboard");

        using var h = new DashboardFrameInvariants.Harness();
        h.Open();
        h.Until(() => h.Image.Source is not null);

        var strip = (Panel)h.Window.FindName("FilmStrip");
        var cell = strip.Children.OfType<ToggleButton>().First(c => c.Visibility == Visibility.Visible && c.Tag is int);
        string path = h.PathFor((int)cell.Tag);

        h.Window.ShowFrameMenu(cell, path);

        Check.That(new[] { "D_MenuOpenInAtelier", "D_MenuShowInExplorer", "D_MenuCopyPath" }.All(k => h.Window.DashboardMenu!.Items.Contains(T(k))),
                   "ein Bild im Streifen: ins Atelier, in den Explorer, der Pfad", string.Join(", ", h.Window.DashboardMenu!.Items));

        var list = (Panel)h.Window.FindName("SequenceList");
        var shown = list.Children.OfType<ToggleButton>().First(b => b.IsChecked == true);

        h.Window.ShowSequenceMenu(shown, (DashboardSequenceEntry)shown.Tag);
        Check.That(h.Window.DashboardMenu!.Items.Contains(T("D_MenuOpenInAtelier")),
                   "die gezeigte Sequenz bietet ihr aktuelles Bild fuers Atelier an");

        var other = list.Children.OfType<ToggleButton>().FirstOrDefault(b => b.IsChecked != true);

        if (other is not null)
        {
            h.Window.ShowSequenceMenu(other, (DashboardSequenceEntry)other.Tag);
            Check.That(!h.Window.DashboardMenu!.Items.Contains(T("D_MenuOpenInAtelier")),
                       "eine andere Sequenz nicht - von ihr ist noch kein Bild gewaehlt");
        }

        h.Window.ShowFrameMenu(cell, path);
        h.Window.DashboardMenu!.Invoke(T("D_MenuOpenInAtelier"));
        h.Pump();

        var atelier = ((ContentControl)h.Window.FindName("PageContent")).Content as AtelierPage;
        string? opened = atelier is null ? null
            : (string?)typeof(AtelierPage).GetField("_path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(atelier);

        Check.That(((RadioButton)h.Window.FindName("NavAtelier")).IsChecked == true && opened == path,
                   "Im Atelier oeffnen wechselt ins Atelier - mit genau diesem Bild", opened);
    }
}
