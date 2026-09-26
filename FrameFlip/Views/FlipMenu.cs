using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FrameFlip.Views;

/// <summary>
/// Die Bausteine der eigenen Menues - im Stil der App statt des Windows-Menues.
///
/// Das Menue von Windows passte nicht dazu: hell, eckig, mit einer Schrift, die sonst
/// nirgends steht, und Untermenues, die man mit ruhiger Hand ansteuern musste. Hier
/// steht alles auf derselben Flaeche wie die Seitenleisten, mit denselben Farben.
/// </summary>
internal static class FlipUi
{
    public static Brush Resource(string key, Color fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    public static Brush Surface => Resource("PanelBackground", Color.FromRgb(0x1B, 0x1A, 0x23));
    public static Brush Edge => Resource("PanelBorder", Color.FromRgb(0x37, 0x32, 0x4B));
    public static Brush Text => Resource("ForegroundBrush", Color.FromRgb(0xF2, 0xF0, 0xF7));
    public static Brush Muted => Resource("MutedBrush", Color.FromRgb(0xA0, 0x9A, 0xB4));
    public static Brush Accent => Resource("AccentBrush", Color.FromRgb(0xA4, 0x7B, 0xF0));
    public static Brush Hover => Resource("ControlHoverBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    public static Brush Tile => Resource("ControlBrush", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    /// <summary>Eine gewaehlte Zeile - dieselbe Toenung wie in der Ebenenliste.</summary>
    public static readonly Brush Chosen = Frozen(Color.FromArgb(0x55, 0xA4, 0x7B, 0xF0));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Die Flaeche eines Menues: dunkel, gerundet, mit feinem Rand.</summary>
    public static Border Panel(UIElement child) => new()
    {
        Child = child,
        Background = Surface,
        BorderBrush = Edge,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(6),
        SnapsToDevicePixels = true,
    };

    /// <summary>Eine kleine Ueberschrift ueber einer Gruppe.</summary>
    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 11,
        Margin = new Thickness(4, 8, 4, 4),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// Macht ein Element zur Schaltflaeche: Es hellt beim Ueberfahren auf und loest beim
    /// Loslassen der linken Taste aus - mit den Umschalttasten, die dabei gedrueckt waren.
    /// <paramref name="rest"/> sagt, wie es aussieht, wenn die Maus nicht darauf ist.
    /// </summary>
    public static void Clickable(Border element, Action<ModifierKeys> clicked, Func<Brush>? rest = null)
    {
        element.Cursor = Cursors.Hand;

        Brush Rest() => rest?.Invoke() ?? Brushes.Transparent;

        element.Background = Rest();
        element.MouseEnter += (_, _) => element.Background = Hover;
        element.MouseLeave += (_, _) => element.Background = Rest();
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            clicked(Keyboard.Modifiers);
        };
    }
}

/// <summary>
/// Ein Menue auf der Flaeche der App: Zeilen mit Zeichen, Text und Kuerzel, Trennlinien,
/// und eine Zeile, die sich in ein Eingabefeld verwandelt - zum Umbenennen.
///
/// Mit der Tastatur zu bedienen: hoch und runter waehlen, Enter loest aus, Escape
/// schliesst. Ein Klick daneben schliesst ebenfalls.
/// </summary>
public sealed class FlipMenu
{
    private readonly Popup _popup;
    private readonly StackPanel _rows = new();

    /// <summary>Die Zeilen, die etwas tun - mit dem, was sie tun, wenn man sie ausloest.</summary>
    private readonly List<(Border Row, string Text, Action Fire)> _items = new();

    private int _highlight = -1;

    public FlipMenu(UIElement target)
    {
        var panel = FlipUi.Panel(_rows);
        panel.MinWidth = 200;
        panel.Focusable = true;
        panel.PreviewKeyDown += OnKey;

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

    /// <summary>Die Flaeche des Menues - fuer Aufnahmen, die sie ins Bild der Seite legen.</summary>
    internal FrameworkElement Surface => (FrameworkElement)_popup.Child;

    /// <summary>Ob das Menue offen ist - fuer die Probe.</summary>
    internal bool IsOpen => _popup.IsOpen;

    /// <summary>Die Zeilen, die etwas tun - fuer die Probe.</summary>
    internal IReadOnlyList<string> Items => _items.Select(i => i.Text).ToList();

    /// <summary>Eine Zeile. <paramref name="enabled"/> falsch: Sie steht da, grau, und tut nichts.</summary>
    public FlipMenu Item(string glyph, string text, Action act, string? hint = null, bool enabled = true)
    {
        var row = Row(glyph, text, hint);
        row.Opacity = enabled ? 1 : 0.4;
        _rows.Children.Add(row);

        if (!enabled) return this;

        // Erst schliessen, dann ausfuehren: Ein Dialog aus dem Menue soll nicht hinter
        // einem offenen Menue aufgehen.
        void Fire()
        {
            _popup.IsOpen = false;
            act();
        }

        FlipUi.Clickable(row, _ => Fire());
        _items.Add((row, text, Fire));
        return this;
    }

    /// <summary>Ein Schalter: Das Zeichen sagt, wie er steht.</summary>
    public FlipMenu Toggle(string text, bool on, Action act) => Item(on ? "☑" : "☐", text, act);

    public FlipMenu Separator()
    {
        _rows.Children.Add(new Border { Height = 1, Background = FlipUi.Edge, Margin = new Thickness(4) });
        return this;
    }

    /// <summary>
    /// Umbenennen: Die Zeile wird beim Klick zum Eingabefeld, mit dem alten Namen darin.
    /// Enter uebernimmt, Escape laesst alles, wie es war.
    /// </summary>
    public FlipMenu Rename(string text, string current, Action<string> commit)
    {
        var row = Row("✎", text, "F2");
        int at = _rows.Children.Count;
        _rows.Children.Add(row);

        void Open()
        {
            var box = new TextBox { Text = current, MinWidth = 180, FontSize = 12, Margin = new Thickness(4, 2, 4, 2) };
            if (Application.Current?.TryFindResource("DialogTextBox") is Style style) box.Style = style;

            box.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Escape)) return;

                e.Handled = true;
                _popup.IsOpen = false;
                if (e.Key == Key.Enter) commit(box.Text.Trim());
            };

            box.Loaded += (_, _) =>
            {
                Keyboard.Focus(box);
                box.SelectAll();
            };

            // Tauschen heisst bei WPF: heraus, dann hinein - ueberschreiben laesst sich ein Element nicht.
            _rows.Children.RemoveAt(at);
            _rows.Children.Insert(at, box);
            _items.RemoveAll(i => ReferenceEquals(i.Row, row));
            Editing = box;
            _commit = commit;
        }

        FlipUi.Clickable(row, _ => Open());
        _items.Add((row, text, Open));
        return this;
    }

    /// <summary>Das Eingabefeld, wenn gerade umbenannt wird - fuer die Probe.</summary>
    internal TextBox? Editing { get; private set; }

    private Action<string>? _commit;

    /// <summary>Wie Enter im Eingabefeld: uebernimmt einen Namen. Fuer die Probe, die keine Taste drueckt.</summary>
    internal bool Commit(string text)
    {
        if (Editing is null || _commit is null) return false;

        Editing.Text = text;
        _popup.IsOpen = false;
        _commit(text.Trim());
        return true;
    }

    private static Border Row(string glyph, string text, string? hint)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = new TextBlock { Text = glyph, Foreground = FlipUi.Muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text, Foreground = FlipUi.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(label, 1);
        grid.Children.Add(mark);
        grid.Children.Add(label);

        if (hint is not null)
        {
            var key = new TextBlock
            {
                Text = hint,
                Foreground = FlipUi.Muted,
                FontSize = 11,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(key, 2);
            grid.Children.Add(key);
        }

        return new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 10, 5) };
    }

    public void Open() => _popup.IsOpen = true;

    public void Close() => _popup.IsOpen = false;

    /// <summary>Loest eine Zeile nach ihrem Text aus - wie ein Klick. Fuer die Probe.</summary>
    internal bool Invoke(string text)
    {
        var entry = _items.FirstOrDefault(i => i.Text == text);
        if (entry.Fire is null) return false;

        entry.Fire();
        return true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;

        switch (e.Key)
        {
            case Key.Escape:
                _popup.IsOpen = false;
                break;

            case Key.Down or Key.Up when _items.Count > 0:
                _highlight = (_highlight + (e.Key == Key.Down ? 1 : _items.Count - 1)) % _items.Count;

                foreach (var (row, _, _) in _items) row.Background = Brushes.Transparent;
                _items[_highlight].Row.Background = FlipUi.Hover;
                break;

            case Key.Enter when _highlight >= 0 && _highlight < _items.Count:
                _items[_highlight].Fire();
                break;

            case Key.F2 when _items.FirstOrDefault(i => i.Text.Length > 0 && i.Row.Child is Grid g &&
                                                         g.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "✎") is { Fire: not null } rename:
                rename.Fire();
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}
