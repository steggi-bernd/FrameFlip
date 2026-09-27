using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Projects;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// "Zuletzt geoeffnet" auf der Projektseite (docs/Atelier-Arbeitsablauf.md, Punkt 14): Ein
/// einzelner Eintrag geht ueber sein Kontextmenue aus der Liste - auch einer, dessen Ordner
/// verschwunden ist. Der Ordner und seine Bilder bleiben. In einer eigenen Konfiguration, die
/// Merkliste dessen, der die Probe startet, bleibt unberuehrt.
/// </summary>
public static class RecentForgetInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        Check.Group("Projektseite: einen Eintrag aus \"Zuletzt geoeffnet\" nehmen");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-zuletzt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        try
        {
            string keep = Folder(root, "bleibt");
            string drop = Folder(root, "geht");
            string gone = Folder(root, "verschwunden");

            foreach (string folder in new[] { gone, drop, keep })
                RecentSequences.Remember(new RecentSequence { Folder = folder, Seed = Path.Combine(folder, "bild_0001.png"), First = 1, Last = 1, Count = 1 });

            Directory.Delete(gone, recursive: true);

            var page = new ProjectsPage(_ => { }, () => new List<BlendProject>(), RecentSequences.Load);
            PumpUntil(() => Tiles(page).Count == 3);

            var tile = Tiles(page).Single(t => (string)t.ToolTip == drop);
            var items = tile.ContextMenu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();

            Check.That(items.Any(i => (string)i.Header == T("D_MenuForget")) && items.Any(i => (string)i.Header == T("S_ShowInExplorer")),
                       "eine Kachel bietet \"Aus der Liste nehmen\" und \"Im Explorer zeigen\"",
                       string.Join(", ", items.Select(i => i.Header)));

            items.First(i => (string)i.Header == T("D_MenuForget")).RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            PumpUntil(() => Tiles(page).Count == 2);

            Check.That(RecentSequences.Load().Select(e => e.Folder).SequenceEqual(new[] { keep, gone }),
                       "nur dieser Eintrag ist aus der Merkliste - die anderen bleiben in ihrer Reihenfolge");
            Check.That(Directory.Exists(drop) && File.Exists(Path.Combine(drop, "bild_0001.png")),
                       "der Ordner und seine Bilder sind unberuehrt");
            Check.That(Tiles(page).All(t => (string)t.ToolTip != drop), "und die Seite zeigt ihn nicht mehr");

            // Ein Eintrag, dessen Ordner es nicht mehr gibt: nur "Aus der Liste nehmen".
            var lost = Tiles(page).Single(t => (string)t.ToolTip == gone);
            var lostItems = lost.ContextMenu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();

            Check.That(lostItems.Count == 1 && (string)lostItems[0].Header == T("D_MenuForget"),
                       "ein verschwundener Ordner laesst sich ebenso aus der Liste nehmen - ohne Explorer");

            lostItems[0].RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            PumpUntil(() => Tiles(page).Count == 1);

            Check.That(RecentSequences.Load().Select(e => e.Folder).SequenceEqual(new[] { keep }), "danach steht nur noch der dritte in der Liste");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Die Kacheln unter "Zuletzt geoeffnet" - die mit einem Ordner als Tooltip.</summary>
    private static List<Border> Tiles(ProjectsPage page)
        => Descendants<Border>((Panel)page.FindName("Body"))
               .Where(b => b.ToolTip is string tip && tip.Contains("frameflip-zuletzt-", StringComparison.Ordinal))
               .ToList();

    private static string Folder(string root, string name)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;

        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, "bild_0001.png"));
        encoder.Save(stream);

        return folder;
    }

    private static IEnumerable<TItem> Descendants<TItem>(System.Windows.DependencyObject root) where TItem : System.Windows.DependencyObject
    {
        foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is not System.Windows.DependencyObject node) continue;
            if (node is TItem match) yield return match;
            foreach (var deeper in Descendants<TItem>(node)) yield return deeper;
        }
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
    }
}
