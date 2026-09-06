using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FrameFlip.Bridge;
using FrameFlip.Configuration;
using FrameFlip.Remote;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>
/// Das Hauptfenster.
///
/// FrameFlip lebt im Tray - das bleibt so. Was fehlte, war ein Ort, an dem man
/// nachsehen kann, ohne erst eine Vorschau zu oeffnen: Was rendert gerade? Ist das
/// Handy dran? Welche Sequenzen habe ich zuletzt angesehen?
///
/// Der Aufbau ist bewusst der der Handy-App, um neunzig Grad gedreht. Dort liegen
/// die Reiter unten, hier links; alles andere ist gleich - dieselbe Kopfzeile mit
/// Lebenszeichen, dieselben Schriften, dieselben Farben, dieselbe Regel, dass ein
/// nicht gemessener Wert ein Gedankenstrich ist und keine Null. Wer beide Seiten
/// benutzt, soll sich nicht umgewoehnen muessen.
/// </summary>
public partial class MainWindow : Window
{
    private readonly RenderMonitor? _monitor;
    private readonly Func<RelayState?> _remoteState;
    private readonly Action _showSettings;
    private readonly Action<string> _openSequence;

    private readonly DispatcherTimer _ticker;

    private readonly List<(Border Chip, string Key)> _navItems = new();
    private string _page = "live";

    public MainWindow(RenderMonitor? monitor,
                      Func<RelayState?> remoteState,
                      Action showSettings,
                      Action<string> openSequence)
    {
        _monitor = monitor;
        _remoteState = remoteState;
        _showSettings = showSettings;
        _openSequence = openSequence;

        InitializeComponent();

        MachineName.Text = Environment.MachineName.ToUpperInvariant();

        BuildNav();
        Show("live");

        // Ein Takt statt vieler Ereignisse: Der Renderzustand aendert sich ohnehin
        // im Sekundentakt, und ein Fenster, das nur offen ist, waehrend jemand
        // hinsieht, darf so gemessen werden.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => Refresh();
        _ticker.Start();

        Closed += (_, _) => _ticker.Stop();

        Refresh();
    }

    // ---------------------------------------------------------------- Navigation

    private void BuildNav()
    {
        foreach (var (key, label) in new[]
                 {
                     ("live", "LIVE"),
                     ("sequences", "SEQUENZEN"),
                     ("settings", "EINSTELLUNGEN"),
                 })
        {
            var mark = new Border
            {
                Width = 3,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(0, 2, 2, 0),
            };

            var text = new TextBlock
            {
                Text = label,
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Margin = new Thickness(17, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("MutedBrush"),
            };

            var row = new Grid { Height = 42 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            Grid.SetColumn(mark, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(mark);
            row.Children.Add(text);

            var chip = new Border { Child = row, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            chip.MouseLeftButtonUp += (_, _) => Show(key);

            NavItems.Children.Add(chip);
            _navItems.Add((chip, key));
        }
    }

    private void Show(string key)
    {
        _page = key;

        foreach (var (chip, itemKey) in _navItems)
        {
            bool active = itemKey == key;

            var row = (Grid)chip.Child;
            var mark = (Border)row.Children[0];
            var text = (TextBlock)row.Children[1];

            // Der Strich liegt immer da und ist nur farblos - ihn ein- und
            // auszublenden verschoebe die Beschriftung bei jedem Wechsel.
            mark.Background = active ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
            text.Foreground = (Brush)FindResource(active ? "ForegroundBrush" : "MutedBrush");
            chip.Background = active ? (Brush)FindResource("AppSurface") : Brushes.Transparent;
        }

        Page.Content = key switch
        {
            "sequences" => new SequencesPage(_openSequence),
            "settings" => new SettingsPage(_showSettings, _remoteState),
            _ => new LivePage(),
        };

        Refresh();
    }

    // ---------------------------------------------------------------- Auffrischen

    private void Refresh()
    {
        RenderJob? job = _monitor?.Job;

        bool running = job?.IsRunning == true;

        StatusDot.Fill = (Brush)FindResource(running ? "AppCyan" : "DisabledBrush");

        StatusWord.Text = job switch
        {
            null => "LEERLAUF",
            { IsRunning: true } => "RENDERT",
            { Vanished: true } => "BLENDER WEG",
            { State: JobState.Finished } => "FERTIG",
            { State: JobState.Cancelled } => "ABGEBROCHEN",
            _ => "GESCHEITERT",
        };

        StatusWord.Foreground = (Brush)FindResource(
            running ? "AppCyan" : job?.Vanished == true ? "AppWarn" : "MutedBrush");

        HeaderFile.Text = job is null ? string.Empty : System.IO.Path.GetFileName(job.BlendFile);

        // Der Haarstrich nur, wenn er etwas aussagt - bei einem Einzelbild nicht.
        double share = job is { IsAnimation: true } ? job.Progress : 0;
        Hairline.Width = share * Math.Max(0, ActualWidth - 196);

        RefreshLink();

        if (Page.Content is LivePage live) live.Update(job);
    }

    private void RefreshLink()
    {
        var state = _remoteState();

        var (brush, word) = state switch
        {
            RelayState.Paired => ("AppCyan", "HANDY VERBUNDEN"),
            RelayState.Waiting => ("AccentBrush", "WARTET AUF HANDY"),
            RelayState.Connecting => ("MutedBrush", "VERBINDET …"),
            _ => ("DisabledBrush", "FERNSTEUERUNG AUS"),
        };

        LinkDot.Fill = (Brush)FindResource(brush);
        LinkText.Text = word;
        LinkDetail.Text = state is null ? "in den Einstellungen einschalten" : string.Empty;
    }
}
