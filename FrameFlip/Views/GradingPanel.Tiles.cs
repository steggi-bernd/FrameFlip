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
        ("S_GroupOptics", "Depth", "S_DepthField"),
        ("S_GroupOptics", "Distortion", "S_Distortion"),
        ("S_GroupOptics", "Chromatic", "S_Chromatic"),
        ("S_GroupOptics", "Vignette", "S_Vignette"),

        ("S_GroupFilm", "Dither", "S_Dither"),
        ("S_GroupFilm", "Grain", "S_Grain"),

        ("S_GroupTable", "Lut", "S_Lut"),
    };

    private readonly List<ToggleButton> _tabs = new();
    private readonly Dictionary<string, ToggleButton> _tiles = new(StringComparer.Ordinal);

    private string _tab = "S_GroupBasics";
    private string _chosen = "Basic";
    private bool _arranging;

    /// <summary>Baut Reiter und Kacheln - einmal, beim ersten Anzeigen.</summary>
    private void BuildTiles()
    {
        if (_tabs.Count > 0) return;

        foreach (string tab in Sections.Select(s => s.Tab).Distinct())
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("OverlayToggle"),
                Content = Strings.T(tab),
                Tag = tab,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(9, 1, 9, 1),
                FontSize = 10,
            };

            button.Checked += OnTabChosen;
            button.Unchecked += OnTabChosen;

            _tabs.Add(button);
            Tabs.Children.Add(button);
        }

        foreach (var (tab, prefix, key) in Sections)
        {
            var tile = new ToggleButton
            {
                Style = (Style)FindResource("ToolTile"),
                Tag = prefix,
                Content = Strings.T(key),
                ToolTip = Strings.T(key),
            };

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

            tile.Opacity = Doing(prefix) ? 1.0 : 0.62;
            tile.FontWeight = Doing(prefix) ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>Ob dieses Werkzeug gerade etwas tut.</summary>
    private bool Doing(string prefix) => prefix switch
    {
        "Basic" => !Adjustments.IsNeutral,
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
        "Depth" => !_depth.IsNeutral,
        "Distortion" => !_distortion.IsNeutral,
        "Chromatic" => !_chromatic.IsNeutral,
        "Vignette" => !_vignette.IsNeutral,
        "Dither" => !_dither.IsNeutral || !_diffusion.IsNeutral,
        "Grain" => !_grain.IsNeutral,
        "Lut" => !_lut.IsNeutral,
        _ => false,
    };
}
