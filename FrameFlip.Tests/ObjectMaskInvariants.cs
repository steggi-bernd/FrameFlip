using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Objektmaske per Klick (R4 im Werkzeugplan): Ebene waehlen, Chip "Objektmaske", ein
/// Objekt im Bild anklicken - und die Kryptomatte wird die Maske DIESER Ebene. Mit einer
/// synthetischen EXR samt Kryptomatte, ohne Medien.
/// </summary>
public static class ObjectMaskInvariants
{
    public static void Run() => AClickMasksTheChosenLayer();

    private static void AClickMasksTheChosenLayer()
    {
        Check.Group("Objektmaske: ein Klick maskiert die gewaehlte Ebene");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-objektmaske-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "render_0001.exr");
        File.WriteAllBytes(path, CryptoSample.Bytes());

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = 1100, Height = 750, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        try
        {
            window.Show();
            page.Open(path);

            var size = (TextBlock)page.FindName("SourceText");
            if (!Pump(TimeSpan.FromSeconds(10), () => size.Text.Length > 0))
            {
                Check.That(false, "die Datei wird geladen");
                return;
            }

            Pump(TimeSpan.FromSeconds(0.4), () => false);
            page.ConvertToNodes();
            Pump(TimeSpan.FromSeconds(0.4), () => false);

            var graph = page.Graph!;
            var list = (NodeLayerList)page.FindName("NodeLayers");
            var editor = (NodeEditor)page.FindName("NodeView");
            var column = (ToolColumn)page.FindName("MouseTools");
            var badge = (FrameworkElement)page.FindName("ObjectBadge");
            var undo = (System.Collections.IList)typeof(AtelierPage).GetField("_undo", flags)!.GetValue(page)!;
            var frame = (FloatFrame)typeof(AtelierPage).GetField("_frame", flags)!.GetValue(page)!;

            Check.That(list.Chips.Object.IsEnabled, "die Datei fuehrt eine Kryptomatte: der Chip Objektmaske ist an");

            // Eine Ebene, die die Maske bekommen soll - und sie in der Liste waehlen.
            var mixes = graph.Nodes.OfType<MixNode>().ToList();
            list.Chips.Adjust.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            var layer = graph.Nodes.OfType<MixNode>().Except(mixes).Single();
            typeof(AtelierPage).GetMethod("OnLayerChosen", flags)!.Invoke(page, new object[] { layer });
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            list.Chips.Object.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump(TimeSpan.FromSeconds(0.2), () => false);

            Check.That(page.ObjectPicking && column.Tool == AtelierTool.Select && badge.Visibility == Visibility.Visible,
                       "der Chip: Auswaehlen, und ueber dem Bild steht, was ein Klick jetzt tut");

            // Ein Objekt anklicken - gesucht wie mit der Maus, Punkt fuer Punkt.
            int layers = graph.Nodes.OfType<MixNode>().Count();
            int steps = undo.Count;
            (int X, int Y)? spot = null;

            for (int y = 0; y < frame.Height && spot is null; y += 3)
                for (int x = 0; x < frame.Width && spot is null; x += 3)
                    if (page.ObjectMaskAt(x, y)) spot = (x, y);

            MaskNode? Mask() => graph.Into(layer.Id, "Faktor") is { } f ? graph.Find(f.From) as MaskNode : null;

            Check.That(spot is not null && Mask() is { Mask: { Kind: MaskKind.Cryptomatte, Picks.Count: 1 } } &&
                       graph.Nodes.OfType<MixNode>().Count() == layers && undo.Count == steps + 1,
                       "ein Klick: eine Kryptomatte mit genau diesem Objekt im Faktor der Ebene - keine neue Ebene, ein Schritt",
                       Mask()?.Mask.Picks.FirstOrDefault()?.Name);

            Check.That(NodeLayerList.Of(graph).Single(l => ReferenceEquals(l.Mix, layer)).MaskSource is MaskNode { Mask.Kind: MaskKind.Cryptomatte },
                       "die Zeile der Ebene traegt jetzt die Objektmaske (⬢)");

            if (spot is { } at)
            {
                var first = Mask();

                page.ObjectMaskAt(at.X, at.Y);
                Check.That(ReferenceEquals(Mask(), first) && first!.Mask.Picks.Count == 0,
                           "ein zweiter Klick auf dasselbe Objekt nimmt es wieder heraus - in derselben Maske");

                page.ObjectMaskAt(at.X, at.Y);
                Check.That(first.Mask.Picks.Count == 1, "und ein dritter nimmt es wieder auf");

                // Das Bildmenue bietet dasselbe an, solange die Ebene gewaehlt ist.
                column.Select(AtelierTool.Move, notify: true);
                Pump(TimeSpan.FromSeconds(0.2), () => false);

                Check.That(!page.ObjectPicking && badge.Visibility == Visibility.Collapsed,
                           "ein anderes Werkzeug beendet das Waehlen - der Hinweis geht");

                typeof(AtelierPage).GetMethod("OnLayerChosen", flags)!.Invoke(page, new object[] { layer });
                page.ShowPictureMenu(at.X, at.Y);
                var sets = (IReadOnlyList<Decoding.Exr.CryptomatteSet>)typeof(AtelierPage).GetField("_cryptomattes", flags)!.GetValue(page)!;
                string name = NodeLayerList.Of(graph).Single(l => ReferenceEquals(l.Mix, layer)).Name;
                string offer = Localization.Strings.T("S_PicMenuObjectOnLayer", sets[0].ShortName, name);

                Check.That(page.PictureMenu!.Items.Contains(offer),
                           "das Bildmenue mit gewaehlter Ebene: Objekt hier als Maske dieser Ebene",
                           string.Join(" | ", page.PictureMenu.Items));
                page.PictureMenu.Close();

                // Ohne gewaehlte Ebene: eine eigene Maskenebene - und an ihr geht es weiter.
                editor.Select(null);
                Pump(TimeSpan.FromSeconds(0.2), () => false);
                layers = graph.Nodes.OfType<MixNode>().Count();

                Check.That(page.StartObjectMask(null) && page.ObjectMaskAt(at.X, at.Y) &&
                           graph.Nodes.OfType<MixNode>().Count() == layers + 1,
                           "ohne gewaehlte Ebene: der Klick legt eine eigene Maskenebene an");

                page.ObjectMaskAt(at.X, at.Y);
                Check.That(graph.Nodes.OfType<MixNode>().Count() == layers + 1,
                           "und der naechste Klick waehlt an ihr weiter, statt noch eine anzulegen");

                page.EndObjectMask();
            }

            // Ohne Kryptomatte in der Datei ist der Chip aus - mit dem Grund im Hinweis.
            list.ObjectMasksAvailable = false;
            Check.That(!list.Chips.Object.IsEnabled && (list.Chips.Object.ToolTip as string) == Localization.Strings.T("S_NodeLayerObjectNone"),
                       "ohne Kryptomatte: der Chip ist aus, der Hinweis sagt warum");
        }
        finally
        {
            window.Close();
            Pump(TimeSpan.FromSeconds(0.2), () => false);
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static bool Pump(TimeSpan limit, Func<bool> done)
    {
        var end = DateTime.UtcNow + limit;

        while (DateTime.UtcNow < end)
        {
            if (done()) return true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }

        return done();
    }
}
