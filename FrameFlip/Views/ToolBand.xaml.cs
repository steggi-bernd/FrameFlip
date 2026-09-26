using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die Werkzeugleiste des Ateliers (docs/Atelier-Werkzeugplan.md, Entscheidung 6): die
/// Kategorien als Band, darunter die Werkzeuge der gewaehlten, rechts die Suche. Was ein
/// Werkzeug tut, entscheidet die Seite - die Leiste meldet nur, welches gewaehlt wurde.
///
/// Die senkrechte Spalte links bleibt fuer das, was die MAUS tut. Diese Leiste ist fuer das,
/// was dem BILD geschieht.
/// </summary>
public partial class ToolBand : UserControl
{
    private readonly Dictionary<string, ToggleButton> _categories = new(StringComparer.Ordinal);
    private bool _inNodes;

    /// <summary>Das Werkzeug, das gerade wirkt - in der Zeile gedrueckt. Null: keines.</summary>
    private string? _active;

    public ToolBand()
    {
        InitializeComponent();

        foreach (string category in ToolCatalog.Shown)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("OverlayToggle"),
                Margin = new Thickness(0, 0, 4, 0),
                Content = ToolCatalog.GlyphOf(category) + " " + Strings.T(category),
                Tag = category,
            };

            button.Click += (_, _) => Choose(category);
            _categories[category] = button;
            CategoryRow.Children.Add(button);
        }

        // Malen zuerst: Dort ist in jedem Modus etwas zu tun. Die Auswahl steht zwar vorn im
        // Band, ist im Stapel aber ganz gedaempft - als Anfang saehe das aus wie eine kaputte Leiste.
        Choose(ToolCatalog.Paint);
    }

    /// <summary>Ein Werkzeug wurde gewaehlt - aus der Zeile oder aus der Suche.</summary>
    public event Action<ToolEntry>? Chosen;

    /// <summary>Die gewaehlte Kategorie.</summary>
    public string Category { get; private set; } = "";

    /// <summary>Die Knoepfe der Werkzeugzeile - fuer die Probe.</summary>
    internal IReadOnlyList<ToggleButton> ToolButtons => ToolRow.Children.OfType<ToggleButton>().ToList();

    /// <summary>
    /// Welches Werkzeug gerade wirkt - der Pinsel in seiner Art. Die Zeile zeigt es gedrueckt,
    /// wie die Spalte links das Werkzeug der Maus.
    /// </summary>
    public void MarkActive(string? key)
    {
        _active = key;

        foreach (var button in ToolButtons)
            button.IsChecked = button.Tag is ToolEntry entry && entry.Key == key;
    }

    /// <summary>Welches Werkzeug gerade gedrueckt steht - fuer die Probe.</summary>
    internal string? Active => _active;

    /// <summary>
    /// Ob das Atelier im Knotenmodus rechnet. Was nur dort Platz hat - Masken als Knoten,
    /// Effekte ohne Karte im Farbstreifen -, ist im Stapel zu sehen, aber gedaempft.
    /// </summary>
    public bool InNodes
    {
        get => _inNodes;
        set
        {
            if (_inNodes == value) return;

            _inNodes = value;
            Choose(Category);
        }
    }

    /// <summary>Zeigt die Werkzeuge einer Kategorie.</summary>
    public void Choose(string category)
    {
        Category = category;

        foreach (var (key, button) in _categories) button.IsChecked = key == category;

        ToolRow.Children.Clear();

        foreach (var entry in ToolCatalog.All.Where(e => e.Category == category))
        {
            bool usable = !entry.NodesOnly || _inNodes;

            var button = new ToggleButton
            {
                Style = (Style)FindResource("OverlayToggle"),
                IsChecked = entry.Key == _active,
                Margin = new Thickness(0, 0, 4, 4),
                Content = entry.Glyph + " " + Strings.T(entry.TitleKey),
                Tag = entry,
                IsEnabled = usable,
                ToolTip = usable
                    ? entry.HintKey is { } hint ? Strings.T(hint) : Strings.T(entry.TitleKey)
                    : Strings.T("S_ToolNodesOnly"),
            };

            ToolTipService.SetShowOnDisabled(button, true);
            // Gedrueckt steht nur, was die Seite als wirkend meldet - ein Klick allein schaltet nichts um.
            button.Click += (_, _) =>
            {
                button.IsChecked = entry.Key == _active;
                Chosen?.Invoke(entry);
            };
            ToolRow.Children.Add(button);
        }
    }

    // ------------------------------------------------------------------ Folge oder Bild

    private bool _single;

    /// <summary>Umgeschaltet: true - nur dieses Bild, false - die ganze Folge.</summary>
    public event Action<bool>? FrameModeWanted;

    /// <summary>Ob der Umschalter zu sehen ist - fuer die Probe.</summary>
    internal bool FrameSwitchShown => FrameSwitch.Visibility == Visibility.Visible;

    /// <summary>Ob gerade "nur dieses Bild" gilt.</summary>
    internal bool SingleFrame => _single;

    /// <summary>
    /// Zeigt den Umschalter - nur bei einer Folge oder einem daraus herausgeloesten Bild - und
    /// welche Seite gilt. Bei einem gewoehnlichen Einzelbild gibt es nichts zu unterscheiden.
    /// </summary>
    public void ShowFrameSwitch(bool visible, bool single)
    {
        _single = single;
        FrameSwitch.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FrameAll.IsChecked = !single;
        FrameOne.IsChecked = single;
    }

    // Die Knoepfe zeigen, was gilt, und melden nur den Wunsch: Umgeschaltet ist erst, wenn die
    // Seite das andere Bild geoeffnet hat und ShowFrameSwitch ruft.
    private void OnFrameAll(object sender, RoutedEventArgs e)
    {
        FrameAll.IsChecked = !_single;
        if (_single) FrameModeWanted?.Invoke(false);
    }

    private void OnFrameOne(object sender, RoutedEventArgs e)
    {
        FrameOne.IsChecked = _single;
        if (!_single) FrameModeWanted?.Invoke(true);
    }

    // ------------------------------------------------------------------ Suche

    /// <summary>Oeffnet die Suche - mit Strg+K oder am Knopf.</summary>
    public void OpenSearch()
    {
        SearchBox.Text = "";
        SearchResults.ItemsSource = null;
        SearchPopup.IsOpen = true;

        Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(SearchBox)), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Was die Suche zu diesem Text findet - fuer die Probe derselbe Weg wie beim Tippen.</summary>
    internal IReadOnlyList<ToolEntry> Search(string text)
    {
        SearchBox.Text = text;
        return SearchResults.ItemsSource as IReadOnlyList<ToolEntry> ?? Array.Empty<ToolEntry>();
    }

    private void OnSearchClicked(object sender, RoutedEventArgs e) => OpenSearch();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        // Angezeigt wie in der Zeile - Zeichen und Name, dahinter die Kategorie (ToolEntry.ToString).
        var found = ToolCatalog.Find(SearchBox.Text, Strings.T);

        SearchResults.ItemsSource = found;
        if (found.Count > 0) SearchResults.SelectedIndex = 0;
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (SearchResults.SelectedItem is ToolEntry entry) Take(entry);
                e.Handled = true;
                break;

            case Key.Escape:
                SearchPopup.IsOpen = false;
                e.Handled = true;
                break;

            case Key.Down when SearchResults.Items.Count > 0:
                SearchResults.SelectedIndex = Math.Min(SearchResults.Items.Count - 1, SearchResults.SelectedIndex + 1);
                e.Handled = true;
                break;

            case Key.Up when SearchResults.Items.Count > 0:
                SearchResults.SelectedIndex = Math.Max(0, SearchResults.SelectedIndex - 1);
                e.Handled = true;
                break;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (SearchResults.SelectedItem is ToolEntry entry) Take(entry);
    }

    /// <summary>Aus der Suche gewaehlt: die Kategorie zeigen, in der es steht, und es melden.</summary>
    internal void Take(ToolEntry entry)
    {
        SearchPopup.IsOpen = false;
        Choose(entry.Category);

        if (!entry.NodesOnly || _inNodes) Chosen?.Invoke(entry);
    }
}
