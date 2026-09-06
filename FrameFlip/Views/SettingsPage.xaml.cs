using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Remote;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>
/// Die Einstellungsseite des Hauptfensters.
///
/// Bewusst keine zweite Fassung aller Regler: Zwei Orte, an denen dasselbe steht,
/// laufen frueher oder spaeter auseinander, und dann sucht jemand den Fehler an der
/// falschen Stelle. Der Dialog bleibt der eine Ort dafuer.
///
/// Was hier steht, ist das, was der Dialog NICHT zeigen kann, weil es sich laufend
/// aendert: ob das Handy gerade dran ist.
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly Action _showDialog;
    private readonly Func<RelayState?> _remoteState;
    private readonly DispatcherTimer _ticker;

    private readonly Action _showPairing;

    public SettingsPage(Action showDialog, Func<RelayState?> remoteState, Action showPairing)
    {
        _showDialog = showDialog;
        _remoteState = remoteState;
        _showPairing = showPairing;

        InitializeComponent();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => Refresh();
        _ticker.Start();

        Unloaded += (_, _) => _ticker.Stop();

        Refresh();
    }

    private void Refresh()
    {
        var state = _remoteState();

        var (brush, word, detail) = state switch
        {
            RelayState.Paired => ("AppCyan", "Handy verbunden",
                "Der Renderfortschritt geht gerade an das gekoppelte Gerät."),

            RelayState.Waiting => ("AccentBrush", "Gekoppelt, Handy nicht da",
                "Die Leitung steht. Sobald die App geöffnet wird, kommen die Werte an."),

            RelayState.Connecting => ("MutedBrush", "Verbindet …",
                "Der nächste Versuch läuft."),

            RelayState.Off => ("DisabledBrush", "Aus", "Die Fernsteuerung ist abgeschaltet."),

            _ => ("DisabledBrush", "Nicht eingerichtet",
                "Im Einstellungsdialog unter Fernsteuerung den QR-Code mit der App abfotografieren."),
        };

        LinkDot.Fill = (Brush)FindResource(brush);
        LinkText.Text = word;
        LinkDetail.Text = detail;
    }

    private void OnPairing(object sender, System.Windows.Input.MouseButtonEventArgs e) => _showPairing();

    private void OnOpenDialog(object sender, RoutedEventArgs e) => _showDialog();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.DirectoryPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ohne Explorer geht es eben nicht. Kein Grund fuer mehr.
        }
    }
}
