using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Configuration;
using FrameFlip.Dashboard;
using FrameFlip.Projects;
using FrameFlip.Sequencing;

namespace FrameFlip.Tests;

/// <summary>
/// Ordner in der Sequenzliste (docs/Projekte-und-Masken.md, Punkt 8): Was abgelegt oder
/// geoeffnet wurde, steht auch beim naechsten Start da, einmal je Ordner, und laesst sich
/// wieder herausnehmen - ohne dass eine Datei angefasst wird.
/// </summary>
public static class SequenceFolderInvariants
{
    public static void Run()
    {
        TheControllerListsRememberedFolders();
        KeepingContext(TheWindowTakesDroppedFolders);
    }

    /// <summary>
    /// Laesst eine Probe laufen und stellt danach den Synchronisationskontext wieder her.
    /// Der Testaufbau des Dashboards und der Schnell-Export setzen den des
    /// Oberflaechenfadens; stehen gelassen, landeten die Fortschrittsmeldungen spaeterer
    /// Gruppen in einer Warteschlange, die niemand abarbeitet.
    /// </summary>
    private static void KeepingContext(Action probe)
    {
        var previous = SynchronizationContext.Current;

        try { probe(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void TheControllerListsRememberedFolders()
    {
        Check.Group("Sequenzliste: gemerkte Ordner im Controller");

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\r\blend", @"C:\r\eins", @"C:\r\zwei" };
        var forgotten = new List<string>();

        var sources = new DashboardSequenceSources(
            () => new[] { new KnownBlend { Path = @"C:\r\szene.blend", Output = @"C:\r\blend", Seed = @"C:\r\blend\f_0001.png" } },
            folders.Contains, _ => false, _ => true, _ => null, _ => null)
        {
            Folders = () => new[]
            {
                new RecentSequence { Folder = @"C:\r\eins", Seed = @"C:\r\eins\a_0001.png" },
                new RecentSequence { Folder = @"C:\r\weg", Seed = @"C:\r\weg\a_0001.png" },
                new RecentSequence { Folder = @"C:\R\BLEND", Seed = @"C:\r\blend\f_0001.png" },
                new RecentSequence { Folder = @"C:\r\zwei", Seed = @"C:\r\zwei\b_0001.png" },
            },
            ForgetFolder = forgotten.Add,
        };

        using var controller = new DashboardSequenceController(sources, action => action(), _ => { });
        controller.Reload();

        var names = controller.Entries.Select(e => e.Folder).ToList();

        Check.That(names.SequenceEqual(new[] { @"C:\r\blend", @"C:\r\eins", @"C:\r\zwei" }),
                   "nach den Blend-Projekten die gemerkten Ordner - ohne verschwundene und ohne doppelte",
                   string.Join(", ", names));
        Check.That(controller.Entries.Skip(1).All(e => e.Adhoc && e.Remembered) && !controller.Entries[0].Adhoc,
                   "gemerkte Ordner sind als solche erkennbar, Blend-Projekte nicht");

        Check.That(!controller.Forget(controller.Entries[0]) && forgotten.Count == 0,
                   "ein Blend-Projekt laesst sich hier nicht herausnehmen");
        Check.That(controller.Forget(controller.Entries[1]) && forgotten.SequenceEqual(new[] { @"C:\r\eins" }),
                   "ein gemerkter Ordner schon - vergessen wird nur der Eintrag");
    }

    private static void TheWindowTakesDroppedFolders()
    {
        Check.Group("Sequenzliste: Ordner ablegen, neu starten, herausnehmen");

        using var h = new DashboardFrameInvariants.Harness();

        // Ein Ordner mit einer kleinen Folge - im eigenen Probenordner, nicht beim Nutzer.
        string dropped = Directory.CreateDirectory(Path.Combine(h.Root, "abgelegt")).FullName;
        for (int i = 1; i <= 3; i++) WritePng(Path.Combine(dropped, $"licht_{i:0000}.png"));

        h.Open();
        h.Until(() => h.Image.Source is not null);

        h.Window.OpenDropped(new[] { dropped });
        h.Pump();

        var list = (System.Windows.Controls.Panel)h.Window.FindName("SequenceList");
        ToggleButton? Row(string folder) => list.Children.OfType<ToggleButton>()
            .FirstOrDefault(b => b.Tag is DashboardSequenceEntry e && string.Equals(e.Folder, folder, StringComparison.OrdinalIgnoreCase));

        Check.That(Row(dropped) is { IsChecked: true },
                   "ein abgelegter Ordner wird ein Eintrag und gleich gezeigt");
        Check.That(RecentSequences.Load().Any(r => string.Equals(r.Folder, dropped, StringComparison.OrdinalIgnoreCase)),
                   "und gemerkt - in der Probenkonfiguration");

        // Ein neuer Start: Er steht wieder da.
        h.Close();
        h.Open();
        h.Until(() => h.Image.Source is not null);
        list = (System.Windows.Controls.Panel)h.Window.FindName("SequenceList");

        var row = Row(dropped);
        Check.That(row?.Tag is DashboardSequenceEntry { Remembered: true },
                   "nach einem neuen Start steht der Ordner wieder in der Liste");

        // Herausnehmen: aus der Liste und aus dem Gemerkten, die Bilder bleiben.
        h.Window.ForgetSequence((DashboardSequenceEntry)row!.Tag);
        h.Pump();

        Check.That(Row(dropped) is null && !RecentSequences.Load().Any(r => string.Equals(r.Folder, dropped, StringComparison.OrdinalIgnoreCase)) &&
                   Directory.GetFiles(dropped).Length == 3,
                   "herausgenommen steht er nicht mehr da und ist vergessen - die Bilder liegen noch dort");
    }

    private static void WritePng(string path)
    {
        const int W = 32, H = 20;
        var pixels = Enumerable.Repeat((byte)140, W * H * 4).ToArray();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
