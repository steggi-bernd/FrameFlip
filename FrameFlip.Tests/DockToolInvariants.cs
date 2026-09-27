using System.Windows;
using System.Windows.Media;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Views;
using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Die Werkzeugeinstellungen als Andockfeld (Entscheidung 10) bleiben greifbar - wohin man sie
/// auch zieht. Gemeldet wurde: an eine andere Stelle gezogen, liessen sie sich teilweise nicht
/// mehr bewegen, und nur das Zuruecksetzen der ganzen Anordnung half.
///
/// Geprueft wird jede Stelle, an die ein Zug das Feld bringen kann: jede Zone, jede Lage in
/// ihr, als eigene Gruppe und als Reiter. Danach muss sein Griff zu sehen sein, innerhalb der
/// Flaeche liegen und die Maus bekommen - und von dort muss es zurueck nach oben gehen.
/// </summary>
public static class DockToolInvariants
{
    public static void Run()
    {
        // Mit Bild - dann steht auch die Ausgabe rechts unten - und in mehreren Fenstergroessen:
        // Eine volle Seitenzone in einem niedrigen Fenster ist der Fall, in dem etwas herausfaellt.
        foreach (var (width, height) in new[] { (1400, 900), (1200, 760), (1100, 640), (1000, 560), (1100, 500), (1000, 460) })
            TheToolStaysGrabbable(width, height);
    }

    private static void TheToolStaysGrabbable(int width, int height)
    {
        Check.Group($"Andocken: die Werkzeugeinstellungen bleiben greifbar ({width} x {height})");

        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frameflip-griff-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, "bild.png");

        const int W = 64, H = 40;
        var pixels = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { pixels[i * 4] = (byte)(i % W * 4); pixels[i * 4 + 1] = 120; pixels[i * 4 + 2] = 60; pixels[i * 4 + 3] = 255; }
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            System.Windows.Media.Imaging.BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4)));
        using (var file = System.IO.File.Create(path)) encoder.Save(file);

        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", System.IO.Path.Combine(folder, "config.json"));

        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page, Width = width, Height = height, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();
            page.Open(path);

            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < until && ((System.Windows.Controls.TextBlock)page.FindName("SourceText")).Text.Length == 0)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            var dock = (DockHost)page.FindName("Dock");
            var tool = (PropertiesPanel)page.FindName("Properties");

            // Der Pinsel hat die meisten Regler - der schwerste Fall fuer eine schmale Zone.
            tool.Show(AtelierTool.Brush);
            page.UpdateLayout();

            var lost = new List<string>();
            var stranded = new List<string>();
            var homeless = new List<string>();

            foreach (var (zone, group, tab) in Places(dock))
            {
                dock.ResetLayout();
                page.UpdateLayout();

                dock.MovePanel("tool", zone, group, tab);
                page.UpdateLayout();

                string where = $"{zone} {group}{(tab ? " als Reiter" : "")}";

                if (Grip(dock, "tool") is not { } grip)
                {
                    lost.Add(where + ": kein Griff");
                    continue;
                }

                var area = grip.TransformToAncestor(dock).TransformBounds(new Rect(grip.RenderSize));
                var inside = new Rect(0, 0, dock.ActualWidth, dock.ActualHeight);
                var middle = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);

                bool shown = grip.IsVisible && area.Width >= 10 && area.Height >= 10 && inside.Contains(middle);
                bool hit = shown && dock.InputHitTest(middle) is DependencyObject target && Within(target, grip);

                if (!shown || !hit) lost.Add($"{where}: {(shown ? "bekommt die Maus nicht" : "nicht zu sehen")} ({area})");

                // Und zurueck nach oben - an den oberen Rand des Bildes gezogen.
                var centre = (FrameworkElement)dock.Center!;
                var top = centre.TranslatePoint(new Point(centre.ActualWidth / 2, 20), dock);

                if (dock.Layout.Find("tool") is not { Zone: DockZone.Top } && !dock.DropAt("tool", top))
                    stranded.Add(where);

                // Ein Feld allein an seinen Platz: das Menue an seinem Griff. Die anderen bleiben.
                dock.MovePanel("tool", zone, group, tab);
                page.UpdateLayout();

                if (Grip(dock, "tool")?.ContextMenu is not { } menu ||
                    !menu.Items.OfType<System.Windows.Controls.MenuItem>().Any(i => (string)i.Header == Localization.Strings.T("S_DockPanelHome")))
                    homeless.Add(where + ": kein Menue am Griff");

                dock.HomePanel("tool");
                page.UpdateLayout();

                if (dock.Layout.Find("tool") is not { Zone: DockZone.Top, Group: 0 } ||
                    dock.Layout.Find("colour") is not { Zone: DockZone.Right } || dock.Layout.Find("export") is not { Zone: DockZone.Right })
                    homeless.Add(where + ": nicht zurueck");
            }

            Check.That(lost.Count == 0, "an jeder Stelle hat das Feld einen Griff, der zu sehen ist und die Maus bekommt",
                       string.Join(" | ", lost));
            Check.That(stranded.Count == 0, "und von jeder Stelle geht es zurueck nach oben", string.Join(" | ", stranded));
            Check.That(homeless.Count == 0, "und das Menue am Griff bringt es allein an seinen Platz - die anderen bleiben",
                       string.Join(" | ", homeless));

            // Nach oben auch ueber das Bild hinaus gezogen - auf die Werkzeugleiste.
            dock.ResetLayout();
            dock.MovePanel("tool", DockZone.Right, 3, asTab: false);
            page.UpdateLayout();

            var centreArea = (FrameworkElement)dock.Center!;
            var above = centreArea.TranslatePoint(new Point(centreArea.ActualWidth / 2, -60), dock);
            Check.That(dock.DropAt("tool", above) && dock.Layout.Find("tool") is { Zone: DockZone.Top },
                       "ueber den oberen Rand hinaus losgelassen: es landet oben");

            // Wie es aussieht: unten so hoch wie die Einstellungen, als Reiter oben buendig, links die ganze Spalte.
            dock.MovePanel("tool", DockZone.Bottom, 0, asTab: false);
            page.UpdateLayout();
            Check.That(Math.Abs(tool.ActualHeight - tool.DesiredSize.Height) < 1 && tool.ActualHeight < 200,
                       "unter dem Bild: die Zone ist so hoch wie die Einstellungen, ohne leere Flaeche",
                       $"{tool.ActualHeight:0} zu {tool.DesiredSize.Height:0}");

            dock.ResetLayout();
            dock.MovePanel("tool", DockZone.Right, 1, asTab: true);
            page.UpdateLayout();
            Check.That(Math.Abs(tool.ActualHeight - tool.DesiredSize.Height) < 1,
                       "als Reiter neben der Farbe: oben buendig, nicht mitten in leerer Flaeche");

            dock.ResetLayout();
            dock.MovePanel("tool", DockZone.Left, 0, asTab: false);
            page.UpdateLayout();

            var holder = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(tool));
            while (holder is not null && holder.Tag is not ValueTuple<DockZone, int>) holder = (FrameworkElement)VisualTreeHelper.GetParent(holder);
            var box = holder is null ? Rect.Empty : holder.TransformToAncestor(dock).TransformBounds(new Rect(holder.RenderSize));
            Check.That(!box.IsEmpty && box.Bottom > dock.ActualHeight - 4,
                       "allein links: das Feld fuellt die Spalte, statt oben darin zu haengen", $"{box}");

            dock.ResetLayout();
            page.UpdateLayout();
            Check.That(dock.Layout.Find("tool") is { Zone: DockZone.Top }, "zurueckgesetzt: wieder oben");
        }
        finally
        {
            window.Close();
            Atelier.AtelierProjectStore.WaitForWrites(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { System.IO.Directory.Delete(folder, recursive: true); } catch (System.IO.IOException) { }
        }
    }

    /// <summary>Jede Stelle, an die ein Zug das Feld aus der Grundanordnung bringen kann.</summary>
    private static IEnumerable<(DockZone Zone, int Group, bool Tab)> Places(DockHost dock)
    {
        var fresh = DockLayout.Default();

        foreach (var zone in new[] { DockZone.Left, DockZone.Right, DockZone.Bottom })
        {
            int count = fresh.Zone(zone).Count;

            for (int group = 0; group <= count; group++)
            {
                yield return (zone, group, false);
                if (group < count) yield return (zone, group, true);
            }
        }
    }

    /// <summary>Der Griff eines Feldes: sein Reiter oder, oben, sein Punktegriff.</summary>
    private static FrameworkElement? Grip(DependencyObject root, string panel)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement { Tag: string id } element && id == panel &&
                element.IsHitTestVisible && element.Cursor is not null)
                return element;

            if (Grip(child, panel) is { } found) return found;
        }

        return null;
    }

    private static bool Within(DependencyObject hit, DependencyObject grip)
    {
        for (var at = hit; at is not null; at = VisualTreeHelper.GetParent(at))
            if (ReferenceEquals(at, grip)) return true;

        return false;
    }
}
