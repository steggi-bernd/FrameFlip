using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die Farbspalte als Reiter mit Kacheln.
///
/// Einundzwanzig aufklappbare Faecher untereinander waren ein Schacht: Wer die
/// Vignette suchte, rollte an siebzehn Dingen vorbei, die er nicht gesucht hatte -
/// und sah dabei von keinem einzigen, ob es gerade etwas tut. Ein zugeklapptes Fach
/// sieht aus wie ein zugeklapptes Fach, ob die Vignette nun steht oder nicht.
///
/// Kacheln loesen beides auf einmal. Eine Kachel je Werkzeug, alle eines Reiters auf
/// einen Blick, und die, die etwas tun, tragen einen Punkt. Darunter steht GENAU EIN
/// Werkzeug ausgeschrieben - das gewaehlte. Damit ist die Frage "was ist hier alles
/// an?" eine Blickfrage statt einer Klickfrage.
///
/// Die Reiter folgen dem Rechenweg und nicht dem Alphabet: Grundkorrektur, Licht und
/// Zeichnung, Optik, Film, Tabelle. Das ist dieselbe Reihenfolge, in der gerechnet
/// wird, und sie beantwortet nebenbei die Frage, worauf ein Regler eigentlich wirkt.
/// </summary>
public partial class GradingPanel
{
    /// <summary>
    /// Welches Werkzeug in welchen Reiter gehoert - und wie es heisst.
    ///
    /// Eine Tabelle und keine Schachtelung in der XAML. Ein Werkzeug, das den Reiter
    /// wechselt, aendert hier eine Zeile; in der XAML waeren es drei Ebenen Klammern,
    /// und ein vergessenes Ende faellt erst beim Oeffnen des Fensters auf.
    /// </summary>
    private static readonly (string Tab, string Prefix, string Key)[] Sections =
    {
        ("S_GroupBasics", "Basic", "S_Correction"),
        ("S_GroupBasics", "Curve", "S_Curves"),
        ("S_GroupBasics", "WhiteBalance", "S_WhiteBalance"),
        ("S_GroupBasics", "Zones", "S_Zones"),
        ("S_GroupBasics", "Bands", "S_ColourBands"),

        ("S_GroupLight", "Dehaze", "S_Dehaze"),
        ("S_GroupLight", "Bloom", "S_Bloom"),
        ("S_GroupLight", "Halation", "S_Halation"),
        ("S_GroupLight", "Noise", "S_Noise"),
        ("S_GroupLight", "Clarity", "S_Clarity"),
        ("S_GroupLight", "Texture", "S_Texture"),
        ("S_GroupLight", "Sharpen", "S_Sharpen"),

        ("S_GroupOptics", "Motion", "S_Motion"),
        ("S_GroupOptics", "Displace", "S_Displace"),
        ("S_GroupOptics", "Depth", "S_DepthField"),
        ("S_GroupOptics", "Distortion", "S_Distortion"),
        ("S_GroupOptics", "Chromatic", "S_Chromatic"),
        ("S_GroupOptics", "Vignette", "S_Vignette"),

        ("S_GroupFilm", "Dither", "S_Dither"),
        ("S_GroupFilm", "Sort", "S_Sort"),
        ("S_GroupFilm", "Grain", "S_Grain"),

        ("S_GroupTable", "Lut", "S_Lut"),
    };

    private readonly List<ToggleButton> _tabs = new();
    private readonly Dictionary<string, ToggleButton> _tiles = new(StringComparer.Ordinal);

    /// <summary>Der Punkt auf jeder Kachel - sichtbar, solange ihr Werkzeug etwas tut.</summary>
    private readonly Dictionary<string, System.Windows.Shapes.Ellipse> _dots = new(StringComparer.Ordinal);

    private string _tab = "S_GroupBasics";
    private string _chosen = "Basic";
    private bool _arranging;

    /// <summary>Baut Reiter und Kacheln - einmal, beim ersten Anzeigen.</summary>
    private void BuildTiles()
    {
        if (_tabs.Count > 0) return;

        foreach (string tab in Sections.Select(s => s.Tab).Distinct())
        {
            // Der kurze Name auf dem Reiter, der lange im Hinweis. "Licht und
            // Zeichnung" passt nicht neben vier andere in eine Zeile, "Licht" schon.
            var button = new ToggleButton
            {
                Style = (Style)FindResource("OverlayToggle"),
                Content = Strings.T(tab + "Short"),
                ToolTip = Strings.T(tab),
                Tag = tab,
                Margin = new Thickness(0, 0, 3, 0),
                Padding = new Thickness(2, 1, 2, 1),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            button.Checked += OnTabChosen;
            button.Unchecked += OnTabChosen;

            _tabs.Add(button);
            Tabs.Children.Add(button);
        }

        foreach (var (tab, prefix, key) in Sections)
        {
            // Name und ein Punkt davor, der zeigt, ob das Werkzeug etwas tut. Der
            // Punkt ersetzt die Blaesse von frueher: Eine blasse Kachel sah aus wie
            // eine gesperrte, und die meisten Kacheln sind blass - die Spalte wirkte,
            // als liesse sich fast nichts bedienen.
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Hidden,
            };

            var label = new TextBlock
            {
                Text = Strings.T(key),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0),
            };

            var face = new Grid();

            face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(label, 1);

            face.Children.Add(dot);
            face.Children.Add(label);

            var tile = new ToggleButton
            {
                Style = (Style)FindResource("ToolTile"),
                Tag = prefix,
                Content = face,
                ToolTip = Strings.T(key),
            };

            _dots[prefix] = dot;

            tile.Checked += OnTileChosen;
            tile.Unchecked += OnTileChosen;

            _tiles[prefix] = tile;

            _ = tab;
        }

        // Die alten Ueberschriften und Gruppenkoepfe verschwinden: Die Kachel IST die
        // Ueberschrift. Zwei Wege zu demselben Abschnitt waeren zwei Zustaende, die
        // auseinanderlaufen koennen.
        foreach (var (_, prefix, _) in Sections)
            if (FindName(prefix + "Head") is FrameworkElement head)
                head.Visibility = Visibility.Collapsed;

        foreach (string group in new[]
                 { "GroupBasic", "GroupDehaze", "GroupMotion", "GroupDither", "GroupLut" })
        {
            if (FindName(group) is FrameworkElement header) header.Visibility = Visibility.Collapsed;
        }

        Arrange();
    }

    private void OnTabChosen(object sender, RoutedEventArgs e)
    {
        if (_arranging || sender is not ToggleButton button || button.Tag is not string tab) return;
        if (button.IsChecked != true) return;

        _tab = tab;

        // Der erste Eintrag des Reiters wird gewaehlt. Ein Reiter, der nichts zeigt,
        // waere ein Klick, der nichts beantwortet.
        _chosen = Sections.First(s => s.Tab == tab).Prefix;

        Arrange();
    }

    private void OnTileChosen(object sender, RoutedEventArgs e)
    {
        if (_arranging || sender is not ToggleButton button || button.Tag is not string prefix) return;
        if (button.IsChecked != true) return;

        _chosen = prefix;

        Arrange();
    }

    /// <summary>
    /// Stellt Reiter, Kacheln und den ausgeschriebenen Abschnitt aufeinander ein.
    ///
    /// An einer Stelle und nicht verteilt: Es sind drei Dinge, die zusammenpassen
    /// muessen, und drei Stellen waeren drei Gelegenheiten, dass eines davon
    /// zurueckbleibt.
    /// </summary>
    private void Arrange()
    {
        _arranging = true;

        try
        {
            foreach (var button in _tabs)
                button.IsChecked = (string)button.Tag == _tab;

            Tiles.Children.Clear();

            foreach (var (tab, prefix, _) in Sections)
            {
                var tile = _tiles[prefix];

                tile.IsChecked = prefix == _chosen;

                if (tab == _tab) Tiles.Children.Add(tile);

                // Ausgeschrieben steht immer genau eines.
                if (FindName(prefix + "Body") is FrameworkElement body)
                {
                    body.Visibility = prefix == _chosen
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }
        finally
        {
            _arranging = false;
        }

        ShowActive();
    }

    /// <summary>
    /// Der Punkt auf den Kacheln, die etwas tun.
    ///
    /// Das ist der eigentliche Gewinn gegenueber den Faechern: Ein zugeklapptes Fach
    /// sieht aus wie ein zugeklapptes Fach, ob die Vignette nun steht oder nicht. Eine
    /// Kachel kann es zeigen, und dann beantwortet der Blick die Frage "was ist hier
    /// alles an?".
    /// </summary>
    private void ShowActive()
    {
        foreach (var (_, prefix, _) in Sections)
        {
            if (!_tiles.TryGetValue(prefix, out var tile)) continue;

            bool doing = Doing(prefix);

            // Voll lesbar, ob es etwas tut oder nicht - der Unterschied steht im
            // Punkt und im Gewicht der Schrift, nicht in der Deckkraft.
            tile.FontWeight = doing ? FontWeights.SemiBold : FontWeights.Normal;

            if (_dots.TryGetValue(prefix, out var dot))
                dot.Visibility = doing ? Visibility.Visible : Visibility.Hidden;
        }
    }

    /// <summary>Ob dieses Werkzeug gerade etwas tut.</summary>
    private bool Doing(string prefix) => prefix switch
    {
        "Basic" => !Adjustments.IsNeutral || !_vibrance.IsNeutral,
        "Curve" => !_curves.IsNeutral,
        "WhiteBalance" => !_whiteBalance.IsNeutral,
        "Zones" => !_zones.IsNeutral,
        "Bands" => !_bands.IsNeutral,
        "Dehaze" => !_dehaze.IsNeutral,
        "Bloom" => !_bloom.IsNeutral,
        "Halation" => !_halation.IsNeutral,
        "Noise" => !_noise.IsNeutral,
        "Clarity" => !_clarity.IsNeutral,
        "Texture" => !_texture.IsNeutral,
        "Sharpen" => !_sharpen.IsNeutral,
        "Motion" => !_motion.IsNeutral,
        "Displace" => !_displace.IsNeutral,
        "Depth" => !_depth.IsNeutral,
        "Sort" => !_sort.IsNeutral,
        "Distortion" => !_distortion.IsNeutral,
        "Chromatic" => !_chromatic.IsNeutral,
        "Vignette" => !_vignette.IsNeutral,
        "Dither" => !_dither.IsNeutral || !_diffusion.IsNeutral,
        "Grain" => !_grain.IsNeutral,
        "Lut" => !_lut.IsNeutral,
        _ => false,
    };
}
