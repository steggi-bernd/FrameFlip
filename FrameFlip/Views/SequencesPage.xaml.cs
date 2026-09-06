using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Configuration;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>
/// Die Sequenzen, die FrameFlip schon einmal geoeffnet hat.
///
/// Aufgebaut wie die Liste in der App: je Zeile Name, Angaben in Festbreite, und
/// rechts ein Merkmal - wieviele Frames, oder in Rosa, wieviele fehlen.
///
/// Ordner, die es nicht mehr gibt, werden nicht stillschweigend verschwiegen.
/// Sie stehen gedaempft da, mit dem Hinweis, dass sie weg sind - wer eine Sequenz
/// vermisst, soll sehen, dass FrameFlip sie kannte und wo sie lag, statt zu raten,
/// ob er sich das eingebildet hat.
/// </summary>
public partial class SequencesPage : UserControl
{
    private readonly Action<string> _open;

    public SequencesPage(Action<string> open)
    {
        _open = open;

        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        Items.Children.Clear();

        var entries = RecentSequences.Load();

        EmptyNote.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in entries) Items.Children.Add(Row(entry));
    }

    private Border Row(RecentSequence entry)
    {
        bool alive = entry.Exists;

        var name = new TextBlock
        {
            Text = entry.Name,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource(alive ? "ForegroundBrush" : "DisabledBrush"),
        };

        var detail = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            Text = Describe(entry, alive),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource(alive ? "MutedBrush" : "DisabledBrush"),
        };

        var left = new StackPanel();
        left.Children.Add(name);
        left.Children.Add(detail);

        var tag = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(9, 5, 9, 5),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource(entry.Missing > 0 ? "AppWarn" : "PanelBorder"),
            Child = new TextBlock
            {
                Text = entry.Missing > 0 ? $"{entry.Missing} FEHLEN" : $"{entry.Count}",
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 9.5,
                Foreground = (Brush)FindResource(entry.Missing > 0 ? "AppWarn" : "MutedBrush"),
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(left, 0);
        Grid.SetColumn(tag, 1);
        grid.Children.Add(left);
        grid.Children.Add(tag);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(15, 12, 15, 12),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("AppSurface"),
            BorderBrush = (Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(1),
            Cursor = alive ? Cursors.Hand : Cursors.Arrow,
            Child = grid,
            ToolTip = entry.Folder,
        };

        if (alive)
        {
            card.MouseLeftButtonUp += (_, _) => _open(entry.Seed);
            card.MouseEnter += (_, _) => card.Background = (Brush)FindResource("SurfaceBrush");
            card.MouseLeave += (_, _) => card.Background = (Brush)FindResource("AppSurface");
        }

        return card;
    }

    private static string Describe(RecentSequence entry, bool alive)
    {
        if (!alive) return "Ordner nicht mehr da · " + entry.Folder;

        var parts = new List<string>();

        if (entry.Kind.Length > 0) parts.Add(entry.Kind);
        if (entry.Width > 0 && entry.Height > 0) parts.Add($"{entry.Width}×{entry.Height}");

        parts.Add($"{entry.First:0000}–{entry.Last:0000}");
        parts.Add(Ago(entry.OpenedUtc));

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// "vor zwei Stunden" statt eines Zeitstempels.
    ///
    /// Wer eine Sequenz sucht, erinnert sich an "gestern", nicht an "14:32".
    /// </summary>
    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        return span switch
        {
            { TotalMinutes: < 2 } => "gerade eben",
            { TotalMinutes: < 60 } => $"vor {(int)span.TotalMinutes} min",
            { TotalHours: < 24 } => $"vor {(int)span.TotalHours} h",
            { TotalDays: < 2 } => "gestern",
            { TotalDays: < 14 } => $"vor {(int)span.TotalDays} Tagen",
            _ => utc.ToLocalTime().ToString("d"),
        };
    }
}
