using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Dashboard;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Uebersicht und Atelier zeigen dieselbe Folge (docs/Projekte-und-Masken.md, Punkt 7):
/// beim Start, beim Wechsel ins Atelier, wenn das Atelier ein anderes Bild oeffnet, und
/// bei "Im Atelier oeffnen".
/// </summary>
public static class WorkspaceInvariants
{
    private const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    public static void Run()
    {
        KeepingContext(TheDashboardStartsWhereTheAtelierStopped);
        KeepingContext(TheyFollowEachOther);
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

    private static void TheDashboardStartsWhereTheAtelierStopped()
    {
        Check.Group("Arbeitsbereich: der Start");

        using var h = new DashboardFrameInvariants.Harness();

        string other = Directory.CreateDirectory(Path.Combine(h.Root, "zweit")).FullName;
        string shot = Path.Combine(other, "shot_0001.png");
        WritePng(shot);

        h.Open(settings: new AppSettings { Prebuffer = true, PrepareVideo = false, MemoryBudgetMb = 128, AtelierImage = shot });
        h.Pump();

        Check.That(Current(h)?.Folder is { } folder && string.Equals(folder, other, StringComparison.OrdinalIgnoreCase),
                   "die Uebersicht beginnt bei der Folge, an der das Atelier zuletzt gearbeitet hat",
                   Current(h)?.Folder);

        h.Close();
        AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
    }

    private static void TheyFollowEachOther()
    {
        Check.Group("Arbeitsbereich: Uebersicht und Atelier folgen einander");

        using var h = new DashboardFrameInvariants.Harness();

        string other = Directory.CreateDirectory(Path.Combine(h.Root, "zweit")).FullName;
        string shot = Path.Combine(other, "shot_0001.png");
        WritePng(shot);

        h.Open();
        h.Until(() => h.Image.Source is not null);

        Check.That(Current(h)?.Folder == h.Render, "die Uebersicht zeigt die Ausgabe des Blend-Projekts");

        // Ins Atelier: Es oeffnet das Bild, auf dem die Uebersicht steht.
        ((RadioButton)h.Window.FindName("NavAtelier")).IsChecked = true;
        var atelier = (AtelierPage)typeof(MainWindow).GetField("_atelierPage", Hidden)!.GetValue(h.Window)!;
        string? Opened() => (string?)typeof(AtelierPage).GetField("_path", Hidden)!.GetValue(atelier);

        h.Until(() => atelier.Projects.Current is not null);

        string head = (string)typeof(MainWindow).GetMethod("FramePath", Hidden)!.Invoke(h.Window, new object[] { h.Playback.Head })!;

        Check.That(Opened() == head && atelier.Projects.Current!.Equals(SequenceKey.Of(head)),
                   "ins Atelier gewechselt, oeffnet es das Bild der Uebersicht - mit dem Projekt dieser Folge",
                   $"{Path.GetFileName(Opened())} gegen {Path.GetFileName(head)}");

        // Ein anderes Bild derselben Folge im Atelier bleibt beim Hin und Her.
        atelier.Open(h.PathFor(3));
        h.Until(() => Opened() == h.PathFor(3) && atelier.Projects.Current is not null);

        ((RadioButton)h.Window.FindName("NavDashboard")).IsChecked = true;
        h.Pump();
        ((RadioButton)h.Window.FindName("NavAtelier")).IsChecked = true;
        h.Pump();

        Check.That(Opened() == h.PathFor(3), "bei derselben Folge behaelt das Atelier sein eigenes Bild");

        // Das Atelier oeffnet eine andere Folge: Die Uebersicht zeigt sie und merkt sie sich.
        atelier.Open(shot);
        h.Until(() => Current(h)?.Folder is { } folder && string.Equals(folder, other, StringComparison.OrdinalIgnoreCase));

        Check.That(string.Equals(Current(h)?.Folder, other, StringComparison.OrdinalIgnoreCase) &&
                   RecentSequences.Load().Any(r => string.Equals(r.Folder, other, StringComparison.OrdinalIgnoreCase)),
                   "ein im Atelier geoeffnetes Bild waehlt seine Folge in der Uebersicht und bleibt in der Liste");

        // "Im Atelier oeffnen" nimmt genau dieses Bild, nicht das der Uebersicht.
        ((RadioButton)h.Window.FindName("NavDashboard")).IsChecked = true;
        h.Pump();
        h.Window.OpenInAtelier(h.PathFor(2));
        h.Until(() => Opened() == h.PathFor(2) && atelier.Projects.Current is not null && atelier.Projects.Current.Equals(SequenceKey.Of(h.PathFor(2))));

        Check.That(Opened() == h.PathFor(2), "\"Im Atelier oeffnen\" oeffnet genau das gewaehlte Bild");

        h.Close();
        AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
    }

    private static DashboardSequenceEntry? Current(DashboardFrameInvariants.Harness h)
        => (DashboardSequenceEntry?)typeof(MainWindow).GetProperty("_current", Hidden)?.GetValue(h.Window)
           ?? (DashboardSequenceEntry?)typeof(MainWindow).GetField("_current", Hidden)?.GetValue(h.Window);

    private static void WritePng(string path)
    {
        const int W = 30, H = 20;
        var pixels = Enumerable.Repeat((byte)90, W * H * 4).ToArray();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
