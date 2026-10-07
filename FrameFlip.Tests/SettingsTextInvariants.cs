using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Remote;
using FrameFlip.Views;
using FrameFlip.Web;

using Point = System.Windows.Point;

namespace FrameFlip.Tests;

/// <summary>
/// Die Texte und die Ordnung der Einstellungen (docs/Atelier-Arbeitsablauf.md, Punkte 15 und
/// 16): Jeder Satz steht einmal, eine Gruppe heisst nicht wie ihre Seite oder eine ihrer
/// Zeilen, ein Hinweis nennt den Schalter so, wie er beschriftet ist, jede Zeile erklaert
/// sich, und die Info-Zeichen einer Gruppe stehen untereinander.
///
/// Mit einem lokal erzeugten Kopplungsschluessel und einem nie gestarteten Dienst - nichts
/// geht ins Netz, und die Einstellungen dessen, der die Probe startet, bleiben unberuehrt.
/// </summary>
public static class SettingsTextInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        Check.Group("Einstellungen: jeder Satz einmal, jede Zeile erklaert, alles in einer Flucht");

        string root = Path.Combine(Path.GetTempPath(), "frameflip-texte-" + Guid.NewGuid().ToString("N")[..8]);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        var watchKey = WatchKey.Create("test1");
        var settings = new AppSettings
        {
            TermsAccepted = AppSettings.TermsVersion,
            WatchEnabled = true,
            WatchSecret = WatchStore.Protect(watchKey),
            RelayHost = "relay.example.org",
            RemoteEnabled = true,
            PairingSecret = PairingStore.Protect(PairingKey.Create()),
        };
        var service = new WatchService(watchKey, "relay.example.org", null, () => null, () => null);

        var editor = new SettingsEditor(settings, _ => null, () => null, () => settings, new DesktopLayout());
        editor.ConnectWatch(() => service, null, null, then => then(), _ => { });

        var window = new Window
        {
            Content = editor, Width = 1200, Height = 900, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            window.Show();

            var tabs = (TabControl)editor.FindName("Tabs");
            var twice = new List<string>();
            var unexplained = new List<string>();
            var ragged = new List<string>();
            var edges = new Dictionary<string, (double Title, double Body)>();

            foreach (TabItem tab in tabs.Items)
            {
                tabs.SelectedItem = tab;
                Settle(editor);

                string page = (string)tab.Header;
                var groups = Descendants<SettingGroup>(editor).Where(g => g.IsVisible).ToList();

                foreach (var group in groups)
                {
                    var rows = Descendants<SettingRow>(group).Where(r => r.IsVisible).ToList();

                    if (group.Header is string caption && caption.Length > 0)
                    {
                        if (caption == page) twice.Add($"{page}: Gruppe \"{caption}\" wie die Seite");

                        foreach (var row in rows.Where(r => r.Header as string == caption))
                            twice.Add($"{page}: Gruppe und Zeile \"{caption}\"");
                    }

                    unexplained.AddRange(rows.Where(r => string.IsNullOrEmpty(r.Explain)).Select(r => $"{page}: {r.Header}"));

                    // Die Info-Zeichen einer Gruppe stehen untereinander - wie breit das Bedienelement
                    // daneben auch ist.
                    var dots = rows.Select(r => r.Template.FindName("Info", r) as FrameworkElement)
                                   .Where(dot => dot is { IsVisible: true })
                                   .Select(dot => dot!.TranslatePoint(new Point(0, 0), group).X)
                                   .ToList();

                    if (dots.Count > 1 && dots.Max() - dots.Min() > 1)
                        ragged.Add($"{page} / {group.Header ?? "ohne Titel"}: {string.Join(", ", dots.Select(x => x.ToString("0")))}");
                }

                var title = Descendants<TextBlock>(editor).FirstOrDefault(t => t.IsVisible && t.Text == page && t.FontSize >= 18);
                var body = groups.FirstOrDefault();

                if (title is not null && body is not null)
                    edges[page] = (title.TranslatePoint(new Point(0, 0), editor).X, body.TranslatePoint(new Point(0, 0), editor).X);
            }

            Check.That(twice.Count == 0, "keine Gruppe heisst wie ihre Seite oder wie eine ihrer Zeilen", string.Join(" | ", twice));
            Check.That(unexplained.Count == 0, "jede Zeile hat ihr Info-Zeichen mit einer Erklaerung", string.Join(" | ", unexplained));
            Check.That(ragged.Count == 0, "in jeder Gruppe stehen die Info-Zeichen untereinander", string.Join(" | ", ragged));

            // Titel und erste Gruppe jeder Seite an derselben Kante.
            var reference = edges.GetValueOrDefault(T("S_TabPerformance"));
            Check.That(edges.Count >= 5 && edges.Values.All(e => Math.Abs(e.Title - reference.Title) < 1 && Math.Abs(e.Body - reference.Body) < 1),
                       "jede Seite beginnt an derselben Kante - Titel und Inhalt",
                       string.Join(", ", edges.Select(e => $"{e.Key} {e.Value.Title:0}/{e.Value.Body:0}")));

            // Verbindungen: Der Satz zum Abfotografieren steht einmal - und nur, wenn es etwas
            // abzufotografieren gibt.
            tabs.SelectedItem = editor.FindName("ConnectionsTab");
            Settle(editor);

            var pairCard = (Border)editor.FindName("PairCard");
            int scan = Descendants<TextBlock>(pairCard).Count(t => t.IsVisible && t.Text == T("S_ScanHint"));
            Check.That(((QrCodeView)editor.FindName("PairingCode")).Text is { Length: > 0 } && scan == 1,
                       "mit Kopplungscode: der Hinweis zum Abfotografieren steht einmal", $"{scan}x");

            // Ein Hinweis, der einen Schalter nennt, nennt ihn so, wie er beschriftet ist.
            Check.That(T("D_SaveToPair").Contains(T("S_RemoteEnable")) && T("D_FolderRequired").Contains(T("S_RemoteEnable")),
                       "die Hinweise nennen den Schalter der Fernsteuerung mit seiner Beschriftung");
        }
        finally
        {
            window.Close();
            editor.Dispose();
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static void Settle(FrameworkElement editor)
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            editor.UpdateLayout();
        }
    }

    private static IEnumerable<TItem> Descendants<TItem>(DependencyObject root) where TItem : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TItem match) yield return match;
            foreach (var deeper in Descendants<TItem>(child)) yield return deeper;
        }
    }
}
