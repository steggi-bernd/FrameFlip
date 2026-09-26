using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>Etwas, das der Hub anbietet - eine Kachel.</summary>
/// <param name="Title">Wie es heisst - und wonach die Suche sucht.</param>
/// <param name="Glyph">Das Zeichen auf einer kleinen Kachel.</param>
/// <param name="Activate">Ein Klick.</param>
public sealed record HubEntry(string Title, string Glyph, Action Activate)
{
    /// <summary>Eine Miniatur - dann wird die Kachel gross.</summary>
    public ImageSource? Thumb { get; init; }

    /// <summary>Was unter dem Namen steht, als Hinweis beim Ueberfahren.</summary>
    public string? Detail { get; init; }

    /// <summary>Umschalt+Klick - etwa ein Pass als Maske statt als Ebene.</summary>
    public Action? Alternate { get; init; }

    /// <summary>Die Palettenkachel, fuer die der Eintrag steht - dann laesst er sich in den Editor ziehen.</summary>
    public string? Section { get; init; }

    /// <summary>Weitere Woerter, unter denen die Suche ihn findet.</summary>
    public string? Words { get; init; }
}

/// <summary>Eine Gruppe von Kacheln unter einer kleinen Ueberschrift.</summary>
public sealed record HubGroup(string Title, IReadOnlyList<HubEntry> Entries, string? Hint = null);

/// <summary>Ein Punkt der Liste links.</summary>
public sealed record HubCategory(string Key, string Title, string Glyph, IReadOnlyList<HubGroup> Groups);

/// <summary>Eine Schaltflaeche - fuer den gewaehlten Knoten oder die Ansicht.</summary>
/// <param name="Label">Ein kurzes Wort, das neben dem Zeichen steht.</param>
/// <param name="Tip">Was beim Ueberfahren steht - gern ausfuehrlicher.</param>
/// <param name="On">Ob es gerade gilt - dann leuchtet das Zeichen.</param>
public sealed record HubAction(string Glyph, string Label, string Tip, Action Act, bool On = false);

/// <summary>
/// Der Hub im Knoteneditor - was frueher das Rechtsklick-Menue war.
///
/// Oben eine Suche: tippen und Enter, wie Umschalt+A in Blender. Links die Kategorien,
/// rechts ihre Kacheln - die Passe der Datei mit Miniaturen, die man als Ebene oder
/// Maske holt, die Ebenen im Graphen zum Hinspringen, die Effekte mit den Zeichen der
/// Palette, die sich auch in den Editor ziehen lassen. Unten, was man mit dem
/// gewaehlten Knoten und der Ansicht tun kann.
///
/// Der Hub weiss nichts vom Graphen: Die Seite reicht ihm, was er zeigt, und was ein
/// Klick tut.
/// </summary>
public sealed class NodeHub : Border
{
    private readonly IReadOnlyList<HubCategory> _categories;
    private readonly StackPanel _list = new();
    private readonly StackPanel _content = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _search;

    private HubCategory? _category;

    /// <summary>Die Treffer der Suche, in ihrer Reihenfolge - Enter nimmt den markierten.</summary>
    private readonly List<(HubEntry Entry, Border Tile)> _hits = new();
    private int _highlight;

    /// <summary>Welche Kategorie zuletzt offen war - der Hub oeffnet wieder dort.</summary>
    private static string? _lastCategory;

    /// <summary>Ein Eintrag wurde gewaehlt - der Hub soll zugehen.</summary>
    public event Action? Done;

    /// <summary>Eine Kachel wird in den Editor gezogen - der Hub soll ihm Platz machen.</summary>
    public event Action<string, Border>? DragWanted;

    public NodeHub(IReadOnlyList<HubCategory> categories, string? note,
                   string? chosenTitle, IReadOnlyList<HubAction> nodeActions, IReadOnlyList<HubAction> viewActions)
    {
        _categories = categories.Where(c => c.Groups.Any(g => g.Entries.Count > 0)).ToList();
        Actions = nodeActions.Concat(viewActions).ToList();

        Width = 660;
        Height = 480;
        Background = FlipUi.Surface;
        BorderBrush = FlipUi.Edge;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        SnapsToDevicePixels = true;

        // Oben: die Suche.
        _search = new TextBox
        {
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = FlipUi.Text,
            CaretBrush = FlipUi.Text,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        _search.TextChanged += (_, _) => ShowSearch();

        var placeholder = new TextBlock
        {
            Text = Strings.T("S_HubSearch"),
            Foreground = FlipUi.Muted,
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
        };

        _search.TextChanged += (_, _) => placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var searchCell = new Grid();
        searchCell.Children.Add(placeholder);
        searchCell.Children.Add(_search);

        var top = new Grid { Margin = new Thickness(14, 10, 14, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lens = new TextBlock { Text = "⌕", Foreground = FlipUi.Muted, FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var keys = new TextBlock { Text = "Umschalt+A", Foreground = FlipUi.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(searchCell, 1);
        Grid.SetColumn(keys, 2);
        top.Children.Add(lens);
        top.Children.Add(searchCell);
        top.Children.Add(keys);

        var head = new StackPanel();
        head.Children.Add(top);

        // Ein Hinweis, wo das Neue landet - etwa: im Kabel unter dem Zeiger.
        if (note is not null)
        {
            head.Children.Add(new TextBlock
            {
                Text = note,
                Foreground = FlipUi.Accent,
                FontSize = 11,
                Margin = new Thickness(14, 0, 14, 8),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        // Mitte: links die Kategorien und darunter die Ansicht, rechts die Kacheln.
        _list.Margin = new Thickness(6);

        var views = new StackPanel { Margin = new Thickness(6, 0, 6, 8) };

        if (viewActions.Count > 0)
        {
            views.Children.Add(FlipUi.Caption(Strings.T("S_HubViewCaption")));
            foreach (var action in viewActions) views.Children.Add(ViewRow(action));
        }

        var left = new DockPanel();
        DockPanel.SetDock(views, Dock.Bottom);
        left.Children.Add(views);
        left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        _scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 4, 10, 10),
        };

        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var listBox = new Border
        {
            Child = left,
            BorderBrush = FlipUi.Edge,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };

        Grid.SetColumn(_scroll, 1);
        middle.Children.Add(listBox);
        middle.Children.Add(_scroll);

        // Unten: was sich mit dem gewaehlten Knoten tun laesst.
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 7, 10, 7) };

        bottom.Children.Add(new TextBlock
        {
            Text = chosenTitle ?? Strings.T("S_HubNoNode"),
            Foreground = FlipUi.Muted,
            FontSize = 11,
            MaxWidth = chosenTitle is null ? 600 : 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 0),
        });

        if (chosenTitle is not null)
            foreach (var action in nodeActions) bottom.Children.Add(ActionButton(action));

        var bottomBox = new Border
        {
            Child = bottom,
            BorderBrush = FlipUi.Edge,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = FlipUi.Tile,
            CornerRadius = new CornerRadius(0, 0, 12, 12),
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headBox = new Border { Child = head, BorderBrush = FlipUi.Edge, BorderThickness = new Thickness(0, 0, 0, 1) };

        Grid.SetRow(middle, 1);
        Grid.SetRow(bottomBox, 2);
        layout.Children.Add(headBox);
        layout.Children.Add(middle);
        layout.Children.Add(bottomBox);

        Child = layout;

        PreviewKeyDown += OnKey;

        // Kein Windows-Menue im Hub - auch nicht das eingebaute des Suchfelds. Es sprang
        // beim Loslassen der rechten Taste auf, die den Hub gerade geoeffnet hatte.
        ContextMenuOpening += (_, e) => e.Handled = true;

        Choose(_categories.FirstOrDefault(c => c.Key == _lastCategory) ?? _categories.FirstOrDefault());
        Loaded += (_, _) => Keyboard.Focus(_search);
    }

    /// <summary>Eine Kategorie nach ihrem Schluessel - oder keine, wenn sie leer ist.</summary>
    internal HubCategory? CategoryByKey(string key) => _categories.FirstOrDefault(c => c.Key == key);

    /// <summary>Die Schaltflaechen der Leiste unten - fuer die Probe.</summary>
    internal IReadOnlyList<HubAction> Actions { get; }

    /// <summary>Die Kategorie, die gerade offen ist - fuer die Probe.</summary>
    internal string? Category => _category?.Key;

    /// <summary>Was gerade zu sehen ist, Kachel fuer Kachel - fuer die Probe.</summary>
    internal IReadOnlyList<HubEntry> Visible => _hits.Select(h => h.Entry).ToList();

    /// <summary>Oeffnet eine Kategorie - fuer die Probe und fuer die Liste links.</summary>
    internal void Choose(HubCategory? category)
    {
        _category = category;
        if (category is not null) _lastCategory = category.Key;

        _search.Text = "";
        ShowList();
        ShowCategory();
    }

    /// <summary>Sucht - wie Tippen ins Feld. Fuer die Probe.</summary>
    internal void Search(string text) => _search.Text = text;

    /// <summary>Wie Enter: der markierte Treffer. Fuer die Probe.</summary>
    internal bool ActivateHighlighted(bool alternate = false)
    {
        if (_highlight < 0 || _highlight >= _hits.Count) return false;

        Fire(_hits[_highlight].Entry, alternate);
        return true;
    }

    private void ShowList()
    {
        _list.Children.Clear();

        foreach (var category in _categories)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            bool chosen = ReferenceEquals(category, _category) && _search.Text.Length == 0;

            var glyph = new TextBlock { Text = category.Glyph, FontSize = 14, Foreground = chosen ? FlipUi.Accent : FlipUi.Muted, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = category.Title, FontSize = 12, Foreground = chosen ? FlipUi.Text : FlipUi.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

            Grid.SetColumn(title, 1);
            row.Children.Add(glyph);
            row.Children.Add(title);

            var cell = new Border { Child = row, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 1) };
            FlipUi.Clickable(cell, _ => Choose(category), () => chosen ? FlipUi.Chosen : Brushes.Transparent);

            // Ueberfahren oeffnet - wie ein Menue, nur ohne das Zittern der Hand.
            cell.MouseEnter += (_, _) =>
            {
                if (_search.Text.Length == 0 && !ReferenceEquals(_category, category)) Choose(category);
            };

            _list.Children.Add(cell);
        }
    }

    private void ShowCategory()
    {
        _content.Children.Clear();
        _hits.Clear();
        _highlight = -1;

        if (_category is null) return;

        foreach (var group in _category.Groups.Where(g => g.Entries.Count > 0))
        {
            _content.Children.Add(FlipUi.Caption(group.Hint is null ? group.Title : group.Title + "  ·  " + group.Hint));
            _content.Children.Add(Tiles(group.Entries));
        }

        _scroll.ScrollToTop();
    }

    /// <summary>
    /// Die Suche: ueber alle Kategorien, ohne Gross- und Kleinschreibung, und "ae" findet
    /// auch "ä". Der erste Treffer ist markiert - Enter nimmt ihn.
    /// </summary>
    private void ShowSearch()
    {
        string query = Fold(_search.Text.Trim());

        if (query.Length == 0)
        {
            ShowList();
            ShowCategory();
            return;
        }

        _content.Children.Clear();
        _hits.Clear();

        var found = _categories
            .SelectMany(c => c.Groups.SelectMany(g => g.Entries.Select(e => (Category: c, Entry: e))))
            .Where(x => query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .All(word => Fold(x.Entry.Title + " " + x.Entry.Detail + " " + x.Entry.Words + " " + x.Category.Title).Contains(word)))
            .GroupBy(x => x.Entry.Title + "|" + x.Category.Key)
            .Select(g => g.First())
            .Take(40)
            .ToList();

        _content.Children.Add(FlipUi.Caption(found.Count == 0 ? Strings.T("S_HubNothing") : Strings.T("S_HubFound")));
        _content.Children.Add(Tiles(found.Select(f => f.Entry).ToList(), large: false));

        _highlight = _hits.Count > 0 ? 0 : -1;
        Mark();
        ShowList();
    }

    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);

        foreach (char c in text.ToLower(CultureInfo.CurrentCulture))
        {
            switch (c)
            {
                case 'ä': folded.Append("ae"); break;
                case 'ö': folded.Append("oe"); break;
                case 'ü': folded.Append("ue"); break;
                case 'ß': folded.Append("ss"); break;
                default: folded.Append(c); break;
            }
        }

        return folded.ToString();
    }

    /// <summary>
    /// Kacheln - gross mit Bild, wenn in der Gruppe eines eines hat, sonst klein mit
    /// Zeichen. Eine Gruppe steht in einer Groesse da, auch wenn einzelne Miniaturen noch
    /// entstehen.
    /// </summary>
    private WrapPanel Tiles(IReadOnlyList<HubEntry> entries, bool? large = null)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        bool big = large ?? entries.Any(e => e.Thumb is not null);

        foreach (var entry in entries)
        {
            var tile = big || entry.Thumb is not null ? Large(entry) : Small(entry);
            _hits.Add((entry, tile));
            wrap.Children.Add(tile);
        }

        return wrap;
    }

    private Border Large(HubEntry entry)
    {
        var picture = new Border
        {
            Width = 96,
            Height = 54,
            CornerRadius = new CornerRadius(5),
            Background = Application.Current?.TryFindResource("CheckerBrush") as Brush ?? FlipUi.Tile,
            BorderBrush = FlipUi.Edge,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = entry.Thumb is null
                ? new TextBlock { Text = entry.Glyph, FontSize = 18, Foreground = FlipUi.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new Image { Source = entry.Thumb, Stretch = Stretch.Uniform },
        };

        var stack = new StackPanel();
        stack.Children.Add(picture);
        stack.Children.Add(new TextBlock
        {
            Text = entry.Title,
            Foreground = FlipUi.Text,
            FontSize = 11,
            Width = 96,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return Tile(entry, stack, new Thickness(4));
    }

    private Border Small(HubEntry entry)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = entry.Glyph, FontSize = 13, Foreground = FlipUi.Accent, Width = 20, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = entry.Title, FontSize = 12, Foreground = FlipUi.Text, VerticalAlignment = VerticalAlignment.Center });

        var tile = Tile(entry, row, new Thickness(8, 6, 12, 6));
        tile.BorderBrush = FlipUi.Edge;
        tile.BorderThickness = new Thickness(1);
        tile.Margin = new Thickness(0, 0, 6, 6);
        return tile;
    }

    private Border Tile(HubEntry entry, UIElement content, Thickness padding)
    {
        var tile = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(7),
            Padding = padding,
            Margin = new Thickness(0, 0, 4, 4),
            ToolTip = entry.Detail,
        };

        FlipUi.Clickable(tile, keys => Fire(entry, (keys & ModifierKeys.Shift) != 0));

        // Eine Palettenkachel laesst sich in den Editor ziehen - frei abgelegt oder in ein
        // Kabel, wie aus der Palette im Farbstreifen.
        if (entry.Section is { } section)
        {
            Point? pressed = null;

            tile.PreviewMouseLeftButtonDown += (_, e) => pressed = e.GetPosition(this);
            tile.MouseLeave += (_, _) => pressed = null;
            tile.MouseMove += (_, e) =>
            {
                if (pressed is not { } start || e.LeftButton != MouseButtonState.Pressed) return;

                var now = e.GetPosition(this);
                if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                pressed = null;
                DragWanted?.Invoke(section, tile);
            };
        }

        return tile;
    }

    private void Fire(HubEntry entry, bool alternate)
    {
        Done?.Invoke();

        if (alternate && entry.Alternate is { } other) other();
        else entry.Activate();
    }

    private void Mark()
    {
        for (int i = 0; i < _hits.Count; i++)
            _hits[i].Tile.Background = i == _highlight ? FlipUi.Chosen : Brushes.Transparent;

        if (_highlight >= 0 && _highlight < _hits.Count) _hits[_highlight].Tile.BringIntoView();
    }

    /// <summary>Eine Schaltflaeche der Leiste unten: Zeichen und kurzes Wort.</summary>
    private Border ActionButton(HubAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = action.Glyph, FontSize = 12, Foreground = action.On ? FlipUi.Accent : FlipUi.Muted, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = action.Label, FontSize = 11, Foreground = action.On ? FlipUi.Accent : FlipUi.Text, VerticalAlignment = VerticalAlignment.Center });

        var button = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 10, 4),
            Margin = new Thickness(0, 0, 5, 0),
            ToolTip = action.Tip,
            BorderBrush = FlipUi.Edge,
            BorderThickness = new Thickness(1),
        };

        FlipUi.Clickable(button, _ =>
        {
            Done?.Invoke();
            action.Act();
        });

        return button;
    }

    /// <summary>Eine Zeile der Ansicht, unter den Kategorien.</summary>
    private Border ViewRow(HubAction action)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new TextBlock { Text = action.Glyph, FontSize = 12, Foreground = action.On ? FlipUi.Accent : FlipUi.Muted, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = action.Label, FontSize = 11, Foreground = FlipUi.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        Grid.SetColumn(label, 1);
        row.Children.Add(glyph);
        row.Children.Add(label);

        var cell = new Border { Child = row, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), ToolTip = action.Tip };

        FlipUi.Clickable(cell, _ =>
        {
            Done?.Invoke();
            action.Act();
        });

        return cell;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Done?.Invoke();
                break;

            case Key.Enter:
                if (!ActivateHighlighted((Keyboard.Modifiers & ModifierKeys.Shift) != 0)) return;
                break;

            case Key.Down or Key.Right when _search.Text.Length > 0 && _hits.Count > 0:
                _highlight = Math.Min(_hits.Count - 1, _highlight + 1);
                Mark();
                break;

            case Key.Up or Key.Left when _search.Text.Length > 0 && _hits.Count > 0:
                _highlight = Math.Max(0, _highlight - 1);
                Mark();
                break;

            case Key.Down or Key.Up when _search.Text.Length == 0 && _categories.Count > 0:
            {
                int at = _category is null ? -1 : _categories.ToList().IndexOf(_category);
                at = (at + (e.Key == Key.Down ? 1 : _categories.Count - 1)) % _categories.Count;
                Choose(_categories[at]);
                break;
            }

            default:
                return;
        }

        e.Handled = true;
    }
}
