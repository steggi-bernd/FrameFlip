using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>
/// Der Live-Bildschirm, so wie er auf dem Handy aussieht.
///
/// Zwei Regeln aus der App gelten hier genauso, und beide sind wichtiger als das
/// Aussehen:
///
/// Was nicht gemessen wurde, ist ein Gedankenstrich - keine Null. Eine Null im Feld
/// "GPU" sieht aus wie eine schlafende Maschine und ist von einer echten
/// Leerlauf-GPU nicht zu unterscheiden.
///
/// Bei einem Einzelbild verschwinden Fortschrittsbalken und Framezaehler. Sie
/// beziehen sich auf einen Bereich, der gar nicht gerendert wird, und "0 von 250"
/// waere schlicht falsch.
/// </summary>
public partial class LivePage : UserControl
{
    private readonly NvidiaProbe _gpu = new();
    private readonly List<(TextBlock Value, TextBlock Unit, Border Bar)> _tiles = new();

    /// <summary>Woher die Lastwerte kommen. Ohne Messung bleiben die Kacheln leer.</summary>
    public static Func<LoadSnapshot?> Load { get; set; } = () => null;

    public LivePage()
    {
        InitializeComponent();
        BuildTiles();
        Unloaded += (_, _) => _gpu.Dispose();
    }

    private void BuildTiles()
    {
        foreach (var label in new[] { "GPU-LAST", "VRAM", "GPU-TEMPERATUR", "CPU", "RAM", "CYCLES-SPEICHER" })
        {
            var caption = new TextBlock
            {
                Text = label,
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                Foreground = (Brush)FindResource("MutedBrush"),
            };

            var value = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("DisplayFont"),
                FontSize = 34,
                Foreground = (Brush)FindResource("ForegroundBrush"),
            };

            var unit = new TextBlock
            {
                Margin = new Thickness(6, 0, 0, 5),
                VerticalAlignment = VerticalAlignment.Bottom,
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedBrush"),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(value);
            row.Children.Add(unit);

            var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };

            var track = new Border
            {
                Height = 3,
                Margin = new Thickness(0, 10, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = (Brush)FindResource("TrackBrush"),
                ClipToBounds = true,
                Child = bar,
            };

            var stack = new StackPanel();
            stack.Children.Add(caption);
            stack.Children.Add(row);
            stack.Children.Add(track);

            var card = new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(15, 13, 15, 13),
                CornerRadius = new CornerRadius(14),
                Background = (Brush)FindResource("AppSurface"),
                BorderBrush = (Brush)FindResource("PanelBorder"),
                BorderThickness = new Thickness(1),
                Child = stack,
            };

            Tiles.Children.Add(card);
            _tiles.Add((value, unit, bar));
        }
    }

    public void Update(RenderJob? job)
    {
        bool hasJob = job is not null;

        IdleBlock.Visibility = hasJob ? Visibility.Collapsed : Visibility.Visible;
        ProgressBlock.Visibility = hasJob ? Visibility.Visible : Visibility.Collapsed;

        if (job is not null) UpdateProgress(job);

        UpdateTiles(job);

        string? phase = job?.Stats.Activity;
        PhaseBox.Visibility = string.IsNullOrWhiteSpace(phase) ? Visibility.Collapsed : Visibility.Visible;
        PhaseText.Text = phase ?? string.Empty;
    }

    private void UpdateProgress(RenderJob job)
    {
        bool animation = job.IsAnimation;

        double? share = animation ? job.Progress : job.Stats.SampleProgress;

        BigNumber.Text = share is double value ? Math.Round(value * 100).ToString("0") : "—";
        BigUnit.Foreground = (Brush)FindResource(animation ? "AccentBrush" : "AppBlue");

        RemainingLabel.Text = animation ? "RESTZEIT" : "EINZELBILD";
        RemainingValue.Visibility = animation ? Visibility.Visible : Visibility.Collapsed;
        RemainingValue.Text = job.Remaining is TimeSpan left ? Clock(left) : "—";
        ElapsedValue.Text = Clock(job.Elapsed) + " gelaufen";

        BarTrack.Visibility = animation ? Visibility.Visible : Visibility.Collapsed;

        if (animation)
        {
            // Die Breite steht erst, wenn das Fenster gemessen ist. Vorher ist
            // ActualWidth null, und ein Balken der Breite null ist richtig.
            double full = BarTrack.ActualWidth;

            double done = Math.Clamp(job.FramesWritten / (double)job.TotalFrames, 0, 1);
            double live = job.Stats.SampleProgress is double partial
                ? Math.Clamp(partial / job.TotalFrames, 0, 1 - done)
                : 0;

            BarDone.Width = full * done;
            BarLive.Width = full * live;
        }

        FrameCounter.Text = animation
            ? $"FRAME {job.CurrentFrame:0000} / {job.LastFrame:0000}"
            : $"FRAME {job.CurrentFrame:0000}";

        SampleCounter.Text = job.Stats.Sample is int sample && job.Stats.SampleTotal is int total
            ? $"SAMPLE {sample} / {total}"
            : "SAMPLE — / —";

        SampleBar.Width = (job.Stats.SampleProgress ?? 0) * Math.Max(0, ActualWidth - 56);
    }

    private void UpdateTiles(RenderJob? job)
    {
        LoadSnapshot? load = Load();
        GpuReading gpu = _gpu.Read();

        GpuName.Text = gpu.Name ?? string.Empty;

        double? gpuLoad = load?.GpuPercent ?? gpu.UtilizationPercent;

        Set(0, gpuLoad, "%", gpuLoad / 100, "AccentBrush");
        SetBytes(1, gpu.MemoryUsedMb, gpu.MemoryTotalMb, "AccentBrush");
        Set(2, gpu.TemperatureCelsius, "°C", gpu.TemperatureCelsius / 90.0,
            (gpu.TemperatureCelsius ?? 0) > 76 ? "AppWarn" : "AppCyan");
        Set(3, load?.CpuPercent, "%", load?.CpuPercent / 100, "AppBlue");
        SetBytes(4, load is null ? null : Math.Max(0, load.TotalMb - load.AvailableMb), load?.TotalMb, "AppBlue");

        long? cycles = job?.Stats.MemoryMb;
        Set(5, cycles, "M", load?.TotalMb is > 0 && cycles is not null ? cycles / (double)load.TotalMb : null, "AppCyan");
    }

    private void Set(int index, double? value, string unit, double? share, string colour)
    {
        var (text, unitText, bar) = _tiles[index];

        text.Text = value is double number ? Math.Round(number).ToString("0") : "—";
        unitText.Text = unit;

        ShowBar(bar, share, colour);
    }

    private void SetBytes(int index, long? usedMb, long? totalMb, string colour)
    {
        var (text, unitText, bar) = _tiles[index];

        text.Text = usedMb is long used ? (used / 1024.0).ToString("0.0") : "—";
        unitText.Text = totalMb is long total ? $"/ {Math.Round(total / 1024.0):0} GB" : "GB";

        ShowBar(bar, usedMb is long u && totalMb is > 0 ? u / (double)totalMb : null, colour);
    }

    private void ShowBar(Border bar, double? share, string colour)
    {
        // Ohne Bezugsgroesse kein Balken. Ein Balken bei null saehe aus wie null,
        // und "unbekannt" ist etwas anderes als "nichts".
        if (share is not double value)
        {
            bar.Width = 0;
            return;
        }

        var track = (Border)bar.Parent;

        bar.Background = (Brush)FindResource(colour);
        bar.Width = Math.Clamp(value, 0, 1) * track.ActualWidth;
    }

    private static string Clock(TimeSpan span)
        => span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
}
