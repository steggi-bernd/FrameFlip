using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Localization;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace FrameFlip.Views;

/// <summary>Was ein Eintrag des Katalogs gerade tut: sein Verb, ob er geht, und warum nicht.</summary>
public sealed record CatalogState(string Action, bool Enabled, string Subtitle, string? Alternative = null);

/// <summary>
/// Versuch (docs/Atelier-UX-Versuch.md): ein Katalog fuer alles - Strg+K, "+ Effekt", der Knopf
/// oben. Dieselben Kategorien wie im Band, und jeder Eintrag sagt VOR dem Klick, was geschieht:
/// hinzufuegen oder bearbeiten, und worauf. Was am Ziel nicht geht, steht ausgegraut mit Grund und,
/// wo es einen gibt, mit einem Ausweg.
///
/// Gebaut im Code und nicht im XAML: Die Zeilen haengen am Zustand der Seite, und ein Aufbau je
/// Oeffnen ist bei ein paar Dutzend Eintraegen billiger als eine Vorlage mit Bindungen.
/// </summary>
public sealed class CatalogView : UserControl
{
    private readonly TextBox _search = new();
    private readonly StackPanel _categories = new();
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll = new();
    private readonly Button _target = new();
    private readonly TextBlock _targetLabel = new();

    private readonly List<(ToolEntry Entry, CatalogState State, Border Row)> _shown = new();
    private string _category = "";
    private int _selected = -1;

    /// <summary>Die Eintraege, die gerade in Frage kommen - im Stapel ohne die reinen Knoten.</summary>
    public Func<IEnumerable<ToolEntry>> Entries { get; set; } = () => Array.Empty<ToolEntry>();

    /// <summary>Was ein Eintrag am aktuellen Ziel tut.</summary>
    public Func<ToolEntry, CatalogState> StateOf { get; set; } = _ => new CatalogState("", true, "");

    /// <summary>Die Favoriten, nach Schluessel.</summary>
    public Func<ICollection<string>> Favourites { get; set; } = () => new List<string>();

    /// <summary>Zuletzt benutzt, das neueste vorn.</summary>
    public List<string> Recent { get; } = new();

    /// <summary>Ein Eintrag wurde gewaehlt.</summary>
    public event Action<ToolEntry>? Chosen;

    /// <summary>Ein Eintrag soll aufs Gesamtbild - Strg+Eingabe oder der Ausweg an einem gesperrten Eintrag.</summary>
    public event Action<ToolEntry>? ChosenOnWhole;

    /// <summary>Ein Stern wurde umgeschaltet.</summary>
    public event Action<string>? FavouriteToggled;

    /// <summary>Das Ziel soll das Gesamtbild werden.</summary>
    public event Action? WholeWanted;

    public event Action? Closed;

    public CatalogView()
    {
        Visibility = Visibility.Collapsed;
        Focusable = false;

        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x07, 0x07, 0x0D)) };
        dim.MouseLeftButtonDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, dim)) Close();
        };

        var dialog = new Border
        {
            Width = 760,
            Height = 600,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x43, 0x66)),
        };
        dialog.SetResourceReference(Border.BackgroundProperty, "PanelBackground");

        var layout = new DockPanel();

        layout.Children.Add(Header());
        layout.Children.Add(Footer());
        layout.Children.Add(Body());

        dialog.Child = layout;
        dim.Child = dialog;
        Content = dim;
    }

    private FrameworkElement Header()
    {
        var row = new DockPanel { Height = 56, Margin = new Thickness(16, 0, 16, 0) };
        DockPanel.SetDock(row, Dock.Top);

        var glass = new TextBlock { Text = "⌕", FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        glass.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        _target.SetResourceReference(StyleProperty, "OverlayButton");
        _target.Click += (_, _) => WholeWanted?.Invoke();
        _target.Margin = new Thickness(8, 0, 0, 0);
        _target.VerticalAlignment = VerticalAlignment.Center;

        _targetLabel.Text = Strings.T("S_CatalogTarget");
        _targetLabel.VerticalAlignment = VerticalAlignment.Center;
        _targetLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        _search.FontSize = 14;
        _search.BorderThickness = new Thickness(0);
        _search.Background = Brushes.Transparent;
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.SetResourceReference(ForegroundProperty, "ForegroundBrush");
        _search.SetResourceReference(TextBox.CaretBrushProperty, "ForegroundBrush");
        _search.TextChanged += (_, _) => Fill();
        _search.PreviewKeyDown += OnKey;
        System.Windows.Automation.AutomationProperties.SetName(_search, Strings.T("S_CatalogSearch"));

        DockPanel.SetDock(glass, Dock.Left);
        DockPanel.SetDock(_target, Dock.Right);
        DockPanel.SetDock(_targetLabel, Dock.Right);

        row.Children.Add(glass);
        row.Children.Add(_target);
        row.Children.Add(_targetLabel);
        row.Children.Add(_search);

        var frame = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        DockPanel.SetDock(frame, Dock.Top);
        return frame;
    }

    private FrameworkElement Footer()
    {
        var text = new TextBlock
        {
            Text = Strings.T("S_CatalogKeys"),
            FontSize = 11,
            Margin = new Thickness(16, 10, 16, 10),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var frame = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = text };
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        DockPanel.SetDock(frame, Dock.Bottom);
        return frame;
    }

    private FrameworkElement Body()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Border
        {
            Padding = new Thickness(8, 12, 8, 12),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x16, 0x1F)),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _categories },
        };
        left.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.Content = _rows;
        _rows.Margin = new Thickness(10, 10, 12, 10);

        Grid.SetColumn(_scroll, 1);
        grid.Children.Add(left);
        grid.Children.Add(_scroll);
        return grid;
    }

    /// <summary>Ob der Katalog offen ist.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Oeffnet den Katalog fuer ein Ziel. <paramref name="onLayer"/>: Das Ziel ist eine Ebene, das Gesamtbild waere eine Wahl.</summary>
    public void Open(string target, bool onLayer)
    {
        _target.Content = target;
        _target.IsEnabled = onLayer;
        _target.ToolTip = onLayer ? Strings.T("S_CatalogTargetHint") : null;

        _search.Text = "";
        _category = "";
        Visibility = Visibility.Visible;

        BuildCategories();
        Fill();

        Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(_search)), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Das Ziel hat sich geaendert, waehrend der Katalog offen ist.</summary>
    public void Retarget(string target, bool onLayer)
    {
        _target.Content = target;
        _target.IsEnabled = onLayer;
        Fill();
    }

    public void Close()
    {
        if (!IsOpen) return;

        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    /// <summary>Was gerade gezeigt wird - fuer die Probe.</summary>
    internal IReadOnlyList<(ToolEntry Entry, CatalogState State)> Shown => _shown.Select(s => (s.Entry, s.State)).ToList();

    /// <summary>Sucht wie beim Tippen - fuer die Probe.</summary>
    internal void Type(string text) => _search.Text = text;

    // ---------------------------------------------------------------- Kategorien

    private const string AllKey = "";
    private const string FavouritesKey = "*favourites";
    private const string RecentKey = "*recent";

    private void BuildCategories()
    {
        _categories.Children.Clear();

        AddCategory(FavouritesKey, "☆ " + Strings.T("S_CatalogFavourites"));
        AddCategory(RecentKey, Strings.T("S_CatalogRecent"));

        var line = new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6) };
        line.SetResourceReference(Border.BackgroundProperty, "PanelBorder");
        _categories.Children.Add(line);

        AddCategory(AllKey, Strings.T("S_CatalogAll"));

        var present = Entries().Select(e => e.Category).ToHashSet();
        foreach (var (key, glyph) in ToolCatalog.Categories)
            if (present.Contains(key)) AddCategory(key, glyph + "  " + Strings.T(key));

        ShowCategory();
    }

    private void AddCategory(string key, string text)
    {
        var button = new Button
        {
            Content = text,
            Tag = key,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        button.SetResourceReference(ForegroundProperty, "ForegroundBrush");
        button.Template = FlatTemplate();
        button.Click += (_, _) =>
        {
            _category = key;
            _search.Text = "";
            ShowCategory();
            Fill();
            Keyboard.Focus(_search);
        };

        _categories.Children.Add(button);
    }

    private void ShowCategory()
    {
        foreach (var button in _categories.Children.OfType<Button>())
            button.Background = Equals(button.Tag, _category)
                ? new SolidColorBrush(Color.FromRgb(0x37, 0x32, 0x4B))
                : Brushes.Transparent;
    }

    /// <summary>Ein Knopf ohne Chrom: nur der Hintergrund und der Inhalt.</summary>
    private static ControlTemplate FlatTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(content);

        template.VisualTree = border;
        return template;
    }

    // ---------------------------------------------------------------- Zeilen

    private void Fill()
    {
        _rows.Children.Clear();
        _shown.Clear();

        var all = Entries().ToList();
        var favourites = Favourites();
        IEnumerable<ToolEntry> list;

        string text = _search.Text.Trim();

        if (text.Length > 0)
        {
            var found = ToolCatalog.Find(text, Strings.T);
            list = found.Where(all.Contains);
        }
        else if (_category == FavouritesKey)
        {
            list = all.Where(e => favourites.Contains(e.Key));
        }
        else if (_category == RecentKey)
        {
            list = Recent.Select(k => all.FirstOrDefault(e => e.Key == k)).OfType<ToolEntry>();
        }
        else if (_category.Length > 0)
        {
            list = all.Where(e => e.Category == _category);
        }
        else
        {
            list = all;
        }

        string? lastCategory = null;
        bool grouped = text.Length == 0 && _category == AllKey;

        foreach (var entry in list)
        {
            if (grouped && entry.Category != lastCategory)
            {
                lastCategory = entry.Category;

                var head = new TextBlock
                {
                    Text = Strings.T(entry.Category),
                    FontSize = 11,
                    Margin = new Thickness(8, _shown.Count == 0 ? 0 : 10, 0, 4),
                };
                head.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                _rows.Children.Add(head);
            }

            var state = StateOf(entry);
            var row = Row(entry, state, favourites.Contains(entry.Key));

            _shown.Add((entry, state, row));
            _rows.Children.Add(row);
        }

        if (_shown.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Strings.T(_category == FavouritesKey && text.Length == 0 ? "S_CatalogNoFavourites" : "S_CatalogNothing"),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _rows.Children.Add(empty);
        }

        _selected = _shown.FindIndex(s => s.State.Enabled);
        ShowSelected();
    }

    private Border Row(ToolEntry entry, CatalogState state, bool favourite)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var glyphBox = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x23, 0x33)),
            Child = new TextBlock
            {
                Text = entry.Glyph,
                FontSize = 15,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var title = new TextBlock { Text = Strings.T(entry.TitleKey), FontWeight = FontWeights.SemiBold };
        var subtitle = new TextBlock { Text = state.Subtitle, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        if (!state.Enabled) title.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        Grid.SetColumn(texts, 1);

        FrameworkElement action;

        if (state.Enabled || state.Alternative is null)
        {
            var verb = new TextBlock { Text = state.Enabled ? state.Action : "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            verb.SetResourceReference(TextBlock.ForegroundProperty, "AccentHoverBrush");
            action = verb;
        }
        else
        {
            var alternative = new Button { Content = state.Alternative, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            alternative.SetResourceReference(StyleProperty, "OverlayButton");
            alternative.Click += (_, e) =>
            {
                e.Handled = true;
                Take(entry, whole: true);
            };
            action = alternative;
        }

        Grid.SetColumn(action, 2);

        var star = new TextBlock
        {
            Text = favourite ? "★" : "☆",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = Strings.T("S_CatalogFavouriteHint"),
            Foreground = favourite ? new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)) : null,
        };
        if (!favourite) star.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        star.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            FavouriteToggled?.Invoke(entry.Key);
            int keep = _selected;
            Fill();
            _selected = Math.Min(keep, _shown.Count - 1);
            ShowSelected();
        };
        Grid.SetColumn(star, 3);

        grid.Children.Add(glyphBox);
        grid.Children.Add(texts);
        grid.Children.Add(action);
        grid.Children.Add(star);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = state.Enabled ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x16, 0x15, 0x1E)),
            Cursor = state.Enabled ? Cursors.Hand : Cursors.Arrow,
            ToolTip = entry.HintKey is { } hint ? Strings.T(hint) : null,
        };

        row.MouseLeftButtonUp += (_, _) =>
        {
            if (state.Enabled) Take(entry, whole: false);
        };

        return row;
    }

    private void ShowSelected()
    {
        for (int i = 0; i < _shown.Count; i++)
        {
            var (_, state, row) = _shown[i];
            bool on = i == _selected;

            row.BorderBrush = on ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
            row.Background = on
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0x25, 0x44))
                : state.Enabled ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x16, 0x15, 0x1E));
        }

        if (_selected >= 0 && _selected < _shown.Count) _shown[_selected].Row.BringIntoView();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(+1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_selected >= 0 && _selected < _shown.Count)
                {
                    var (entry, state, _) = _shown[_selected];
                    bool whole = (Keyboard.Modifiers & ModifierKeys.Control) != 0 || !state.Enabled;

                    if (state.Enabled || state.Alternative is not null) Take(entry, whole);
                }
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Move(int by)
    {
        if (_shown.Count == 0) return;

        _selected = Math.Clamp(_selected + by, 0, _shown.Count - 1);
        ShowSelected();
    }

    private void Take(ToolEntry entry, bool whole)
    {
        Recent.Remove(entry.Key);
        Recent.Insert(0, entry.Key);
        if (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1);

        Close();

        if (whole) ChosenOnWhole?.Invoke(entry);
        else Chosen?.Invoke(entry);
    }
}
