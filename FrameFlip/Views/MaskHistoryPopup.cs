using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Localization;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace FrameFlip.Views;

/// <summary>Ein Stand im Fenster des Maskenverlaufs.</summary>
/// <param name="Changed">Welcher Anteil der Flaeche sich seit dem Stand davor geaendert hat.</param>
/// <param name="Current">Ob die Maske gerade genau so aussieht.</param>
/// <param name="First">Der aelteste Stand, der noch da ist - der Anfang.</param>
internal sealed record MaskHistoryRow(int Number, DateTime SavedUtc, float Changed, ImageSource Thumb, bool Current, bool First);

/// <summary>
/// Das Fenster des Maskenverlaufs: die Staende einer gemalten Maske, der neueste oben, jeder
/// mit einem kleinen Bild. Ein Klick stellt ihn wieder her. Im Stil der eigenen Menues.
/// </summary>
internal sealed class MaskHistoryPopup
{
    private readonly Popup _popup;
    private readonly List<(int Number, Action Fire)> _rows = new();

    /// <param name="pending">Was sich seit dem letzten Stand geaendert hat - es wird vor dem Wiederherstellen gesichert.</param>
    public MaskHistoryPopup(UIElement target, string title, IReadOnlyList<MaskHistoryRow> rows, float pending, Action<int> chosen)
    {
        var list = new StackPanel();
        list.Children.Add(FlipUi.Caption(title));

        if (pending > 0)
            list.Children.Add(Note(Strings.T("S_MaskHistoryPending", Percent(pending))));

        var items = new StackPanel();

        foreach (var row in rows)
        {
            void Fire()
            {
                _popup!.IsOpen = false;
                chosen(row.Number);
            }

            var element = Row(row);
            FlipUi.Clickable(element, _ => Fire(), () => row.Current ? FlipUi.Chosen : Brushes.Transparent);

            items.Children.Add(element);
            _rows.Add((row.Number, Fire));
        }

        list.Children.Add(new ScrollViewer
        {
            Content = items,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        list.Children.Add(Note(Strings.T(rows.Count <= 1 ? "S_MaskHistoryEmpty" : "S_MaskHistoryHint")));

        var panel = FlipUi.Panel(list);
        panel.Width = 300;
        panel.Focusable = true;
        panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            _popup!.IsOpen = false;
            e.Handled = true;
        };

        _popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = panel,
        };

        _popup.Opened += (_, _) => Keyboard.Focus(panel);
    }

    /// <summary>Die Nummern der gezeigten Staende, der neueste zuerst - fuer die Probe.</summary>
    internal IReadOnlyList<int> Numbers => _rows.Select(r => r.Number).ToList();

    internal bool IsOpen => _popup.IsOpen;

    public void Open() => _popup.IsOpen = true;

    public void Close() => _popup.IsOpen = false;

    /// <summary>Waehlt einen Stand wie ein Klick - fuer die Probe.</summary>
    internal bool Choose(int number)
    {
        var row = _rows.FirstOrDefault(r => r.Number == number);
        if (row.Fire is null) return false;

        row.Fire();
        return true;
    }

    private static Border Row(MaskHistoryRow row)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var picture = new Border
        {
            Width = 96,
            Height = 54,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)),
            BorderBrush = FlipUi.Edge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new Image { Source = row.Thumb, Stretch = Stretch.Uniform },
        };

        grid.Children.Add(picture);

        string name = Strings.T("S_MaskHistoryState", row.Number);
        if (row.Current) name += " · " + Strings.T("S_MaskHistoryCurrent");

        string detail = row.SavedUtc.ToLocalTime().ToString("HH:mm") + " · " +
                        (row.First ? Strings.T("S_MaskHistoryStart") : Strings.T("S_MaskHistoryChanged", Percent(row.Changed)));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, Foreground = FlipUi.Text, FontSize = 12 });
        text.Children.Add(new TextBlock { Text = detail, Foreground = FlipUi.Muted, FontSize = 10.5 });

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 2),
        };
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = FlipUi.Muted,
        FontSize = 10.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 4, 4, 6),
    };

    /// <summary>Ein Anteil in Prozent - unter einem Prozent mit einer Stelle, sonst ganz.</summary>
    private static string Percent(float share)
    {
        double percent = share * 100;

        return percent < 1
            ? Math.Max(0.1, Math.Round(percent, 1)).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)
            : Math.Round(percent).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }
}
