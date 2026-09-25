using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FrameFlip.Views;

/// <summary>
/// Die Farbspalte als Palette und Stapel.
///
/// Oben eine Palette aus kleinen Symbolen, nach Kategorie in Zeilen: ALLE Effekte auf
/// einen Blick, und was etwas tut, traegt einen Strich. Ein Klick legt den Effekt in
/// den Stapel - oder springt zu ihm, wenn er schon drin liegt.
///
/// Darunter der Stapel: nur die Effekte, die benutzt werden, als Karten. Jede laesst
/// sich zuklappen, ausschalten, zuruecksetzen und entfernen, und mehrere stehen
/// gleichzeitig offen. Das ist die Antwort auf das, was an den Kacheln unpraktisch
/// war: Man sah zweiundzwanzig Kacheln, benutzte drei, und darunter stand immer nur
/// EIN Werkzeug ausgeschrieben - wer an der Belichtung und an der Vignette zugleich
/// drehen wollte, klickte hin und her.
///
/// Die Reihenfolge der Karten ist die des Rechenwegs, nicht die des Hinzufuegens. Wer
/// sie umsortieren koennte, erwartete, dass sich damit die Rechnung aendert - und die
/// steht fest.
/// </summary>
public partial class GradingPanel
{
    /// <summary>
    /// Welches Werkzeug in welche Kategorie gehoert, wie es heisst und welches Zeichen
    /// es in der Palette traegt.
    ///
    /// Eine Tabelle und keine Schachtelung in der XAML. Ein Werkzeug, das die
    /// Kategorie wechselt, aendert hier eine Zeile.
    /// </summary>
    private static readonly (string Tab, string Prefix, string Key, string Glyph)[] Sections =
    {
        ("S_GroupBasics", "Basic", "S_Correction", "☀"),
        ("S_GroupBasics", "Curve", "S_Curves", "∿"),
        ("S_GroupBasics", "WhiteBalance", "S_WhiteBalance", "◑"),
        ("S_GroupBasics", "Zones", "S_Zones", "◐"),
        ("S_GroupBasics", "Bands", "S_ColourBands", "⬡"),

        ("S_GroupLight", "Dehaze", "S_Dehaze", "≋"),
        ("S_GroupLight", "Bloom", "S_Bloom", "✦"),
        ("S_GroupLight", "Halation", "S_Halation", "◎"),
        ("S_GroupLight", "Noise", "S_Noise", "░"),
        ("S_GroupLight", "Clarity", "S_Clarity", "◈"),
        ("S_GroupLight", "Texture", "S_Texture", "▦"),
        ("S_GroupLight", "Sharpen", "S_Sharpen", "△"),

        ("S_GroupOptics", "Motion", "S_Motion", "⇶"),
        ("S_GroupOptics", "Displace", "S_Displace", "≈"),
        ("S_GroupOptics", "Depth", "S_DepthField", "◉"),
        ("S_GroupOptics", "Distortion", "S_Distortion", "◌"),
        ("S_GroupOptics", "Chromatic", "S_Chromatic", "⧉"),
        ("S_GroupOptics", "Vignette", "S_Vignette", "◙"),

        ("S_GroupFilm", "Dither", "S_Dither", "⣿"),
        ("S_GroupFilm", "Sort", "S_Sort", "▤"),
        ("S_GroupFilm", "Grain", "S_Grain", "⁙"),

        ("S_GroupTable", "Lut", "S_Lut", "⊞"),
    };

    /// <summary>Das Zeichen einer Palettenkachel - fuer den Hub im Knoteneditor, der dieselben Zeichen zeigt.</summary>
    internal static string? GlyphOf(string section)
        => Sections.FirstOrDefault(s => s.Prefix == section).Glyph;

    /// <summary>
    /// Die Kategorien, deren Werkzeuge dem GANZEN Bild gelten. An einer Ebene werden
    /// sie nie gerechnet und sind dort gesperrt.
    /// </summary>
    private static readonly HashSet<string> PictureOnly =
        new(StringComparer.Ordinal) { "S_GroupLight", "S_GroupOptics", "S_GroupFilm" };

    /// <summary>Eine Karte im Stapel und was an ihr zu stellen ist.</summary>
    private sealed class Card
    {
        public required Border Frame { get; init; }
        public required Border Host { get; init; }
        public required TextBlock Chevron { get; init; }
        public required ToggleButton Power { get; init; }
        public required Button Remove { get; init; }
        public required TextBlock Title { get; init; }
    }

    private readonly Dictionary<string, ToggleButton> _paletteTiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _activeBars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Card> _cards = new(StringComparer.Ordinal);

    /// <summary>Hinzugefuegt, auch wenn es noch nichts tut - bis zum naechsten Laden.</summary>
    private readonly HashSet<string> _added = new(StringComparer.Ordinal);

    /// <summary>Zugeklappte Karten.</summary>
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);

    private bool _built;

    /// <summary>Baut Palette und Karten - einmal, beim ersten Anzeigen.</summary>
    private void BuildTiles()
    {
        if (_built) return;

        _built = true;

        BuildPalette();
        BuildCards();

        // Die alten Ueberschriften und Gruppenkoepfe verschwinden: Die Karte IST die
        // Ueberschrift. Zwei Wege zu demselben Abschnitt waeren zwei Zustaende, die
        // auseinanderlaufen koennen.
        foreach (var (_, prefix, _, _) in Sections)
            if (FindName(prefix + "Head") is FrameworkElement head)
                head.Visibility = Visibility.Collapsed;

        foreach (string group in new[]
                 { "GroupBasic", "GroupDehaze", "GroupMotion", "GroupDither", "GroupLut" })
        {
            if (FindName(group) is FrameworkElement header) header.Visibility = Visibility.Collapsed;
        }

        ShowActive();
    }

    // ---------------------------------------------------------------- Palette

    /// <summary>
    /// Eine Zeile je Kategorie: links der Name, rechts die Zeichen.
    ///
    /// Die Kategorie steht davor, weil ein Zeichen allein raten laesst - "Welle" und
    /// "Verschiebung" koennten dasselbe Symbol haben. Mit "Optik" davor ist klar,
    /// wo man sucht, und der Name steht im Hinweis.
    /// </summary>
    private void BuildPalette()
    {
        foreach (string tab in Sections.Select(s => s.Tab).Distinct())
        {
            // Die Kategorie am Tag - fuer die Probe, die Zeile fuer Zeile prueft, was an
            // einer Ebene gesperrt sein muss, statt Namen aufzuzaehlen.
            var row = new Grid { Margin = new Thickness(0, 0, 0, 1), Tag = tab };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = Strings.T(tab + "Short"),
                ToolTip = Strings.T(tab),
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var tiles = new WrapPanel();

            Grid.SetColumn(tiles, 1);

            row.Children.Add(label);
            row.Children.Add(tiles);

            foreach (var (_, prefix, key, glyph) in Sections.Where(s => s.Tab == tab))
            {
                // Ein Strich unter dem Zeichen, solange das Werkzeug etwas tut. Der
                // Rahmen - "liegt im Stapel" - kommt vom Haekchen des Knopfes.
                var bar = new Border
                {
                    Height = 2,
                    Margin = new Thickness(6, 0, 6, 2),
                    CornerRadius = new CornerRadius(1),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = (Brush)FindResource("AccentBrush"),
                    Visibility = Visibility.Hidden,
                };

                var face = new Grid();

                face.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2),
                });

                face.Children.Add(bar);

                var tile = new ToggleButton
                {
                    Style = (Style)FindResource("OverlayToggle"),
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 2, 2),
                    Tag = prefix,
                    Content = face,
                    ToolTip = Strings.T(key),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                };

                tile.Click += OnPaletteClicked;
                tile.PreviewMouseLeftButtonDown += OnPalettePressed;
                tile.PreviewMouseMove += OnPaletteDragged;

                _paletteTiles[prefix] = tile;
                _activeBars[prefix] = bar;

                tiles.Children.Add(tile);
            }

            Palette.Children.Add(row);
        }
    }

    /// <summary>Wo auf der Palette gedrueckt wurde - ein Zug beginnt erst ein paar Punkte weiter.</summary>
    private System.Windows.Point? _pressedAt;

    private void OnPalettePressed(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition(this);

    /// <summary>
    /// Im Knotenmodus laesst sich ein Effekt aus der Palette in den Editor ziehen - frei
    /// ablegen oder direkt auf ein Kabel. Ein Klick setzt ihn weiter hinter den gewaehlten
    /// Knoten.
    /// </summary>
    private void OnPaletteDragged(object sender, MouseEventArgs e)
    {
        if (_focus is null || _pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ToggleButton { Tag: string prefix } tile) return;

        var delta = e.GetPosition(this) - start;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pressedAt = null;

        // Die Kachel gibt die Maus ab - sonst kaeme beim Loslassen noch ein Klick, und
        // der Effekt stuende zweimal im Graphen.
        tile.ReleaseMouseCapture();
        DragDrop.DoDragDrop(tile, new DataObject(NodeEditor.SectionFormat, prefix), DragDropEffects.Copy);

        ShowActive();
    }

    /// <summary>
    /// Ein Zeichen der Palette: in den Stapel legen - oder dorthin springen.
    ///
    /// Beides mit demselben Klick. Ein Knopf, der beim zweiten Mal wieder entfernt,
    /// waere eine Falle: Man klickt, um die Karte zu finden, und sie ist weg.
    /// </summary>
    private void OnPaletteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string prefix }) return;

        if (PaletteToNodes(prefix)) return;

        Show(prefix);
    }

    /// <summary>
    /// Legt einen Effekt in den Stapel, klappt ihn auf und rollt zu ihm.
    ///
    /// Oeffentlich, weil die Seite es braucht - die Pipette will die Tiefenschaerfe
    /// zeigen, wenn sie ihr eine Entfernung gibt - und weil die Probe denselben Weg
    /// gehen soll wie die Maus.
    /// </summary>
    public void Show(string prefix)
    {
        if (!_cards.TryGetValue(prefix, out var card)) return;

        _added.Add(prefix);
        _folded.Remove(prefix);

        ShowActive();

        // Erst nach dem Aufbau rollen: Die Karte war eben noch zugeklappt, und eine
        // Flaeche ohne Hoehe laesst sich nicht ins Bild holen.
        Dispatcher.BeginInvoke(new Action(() => card.Frame.BringIntoView()),
                               System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- Karten

    /// <summary>
    /// Eine Karte je Effekt - einmal gebaut, danach nur gezeigt oder versteckt.
    ///
    /// Der Inhalt einer Karte ist der Abschnitt, den es schon gab: Er wird aus der
    /// alten Liste herausgenommen und in die Karte gehaengt. Alle Regler, alle
    /// Namen und alle Handler bleiben, wie sie waren - nur der Rahmen ist neu.
    /// </summary>
    private void BuildCards()
    {
        // Der Hinweis fuer Ebenen gehoert ueber die Karten, nicht zwischen zwei davon.
        if (LocalOnFinalNote.Parent is Panel noteParent)
        {
            noteParent.Children.Remove(LocalOnFinalNote);
            Cards.Children.Add(LocalOnFinalNote);
        }

        foreach (var (_, prefix, key, glyph) in Sections)
        {
            if (FindName(prefix + "Body") is not FrameworkElement body) continue;

            if (body.Parent is Panel parent) parent.Children.Remove(body);

            body.Margin = new Thickness(0);

            var chevron = new TextBlock
            {
                Text = "▾",
                Width = 14,
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Ausschalten ohne zu verlieren: der Vergleich EINES Werkzeugs. Die
            // Grundkorrektur hat keinen - sie ist der Anker der Spalte und steht
            // nicht im Werkzeugstapel.
            var power = new ToggleButton
            {
                Style = (Style)FindResource("StripToggle"),
                Content = "⏻",
                IsChecked = true,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = prefix,
                ToolTip = Strings.T("S_EffectPower"),
                Visibility = prefix == "Basic" ? Visibility.Collapsed : Visibility.Visible,
            };

            power.Click += OnPowerClicked;

            var sign = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("AccentBrush"),
            };

            var title = new TextBlock
            {
                Text = Strings.T(key),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("ForegroundBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var reset = new Button
            {
                Style = (Style)FindResource("StripButton"),
                Content = "↺",
                Tag = prefix,
                ToolTip = Strings.T("S_EffectReset"),
            };

            reset.Click += OnCardReset;

            var remove = new Button
            {
                Style = (Style)FindResource("StripButton"),
                Content = "✕",
                Tag = prefix,
                ToolTip = Strings.T("S_EffectRemove"),
                Visibility = prefix == "Basic" ? Visibility.Collapsed : Visibility.Visible,
            };

            remove.Click += OnCardRemove;

            var head = new Grid
            {
                Height = 32,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = prefix,
            };

            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(power, 1);
            Grid.SetColumn(sign, 2);
            Grid.SetColumn(title, 3);
            Grid.SetColumn(reset, 4);
            Grid.SetColumn(remove, 5);

            head.Children.Add(chevron);
            head.Children.Add(power);
            head.Children.Add(sign);
            head.Children.Add(title);
            head.Children.Add(reset);
            head.Children.Add(remove);

            // Ein Klick auf den Kopf klappt - aber nicht, wenn er einem der Knoepfe
            // darin galt. Die Knoepfe melden ihren Klick selbst und markieren ihn.
            head.MouseLeftButtonUp += OnCardHeadClicked;

            var host = new Border
            {
                Padding = new Thickness(12, 2, 12, 12),
                Child = body,
            };

            var stack = new StackPanel();

            stack.Children.Add(head);
            stack.Children.Add(host);

            var frame = new Border
            {
                Background = (Brush)FindResource("OverlayBackground"),
                BorderBrush = (Brush)FindResource("PanelBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 0, 4, 0),
                Margin = new Thickness(0, 0, 0, 8),
                Child = stack,
                Tag = prefix,
            };

            _cards[prefix] = new Card
            {
                Frame = frame,
                Host = host,
                Chevron = chevron,
                Power = power,
                Remove = remove,
                Title = title,
            };

            Cards.Children.Add(frame);
        }
    }

    private void OnCardHeadClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement { Tag: string prefix }) return;

        // Ein Klick auf einen Knopf im Kopf ist kein Klick auf den Kopf.
        if (e.OriginalSource is DependencyObject hit && Within<ButtonBase>(hit)) return;

        if (!_folded.Remove(prefix)) _folded.Add(prefix);

        ShowActive();
    }

    private static bool Within<T>(DependencyObject at) where T : DependencyObject
    {
        for (var up = at; up is not null; up = VisualTreeHelper.GetParent(up))
            if (up is T) return true;

        return false;
    }

    /// <summary>Ein- und ausschalten - die Einstellung bleibt dabei stehen.</summary>
    private void OnPowerClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string prefix } power) return;

        bool on = power.IsChecked == true;

        foreach (string kind in KindsOf(prefix))
        {
            Stack.Bypassed.Remove(kind);
            if (!on) Stack.Bypassed.Add(kind);
        }

        Raise(interim: false);
        ShowActive();
    }

    /// <summary>Zuruecksetzen: alle Werte auf Anfang, die Karte bleibt.</summary>
    private void OnCardReset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string prefix }) return;

        ResetSection(prefix);

        Raise(interim: false);
        ShowActive();
    }

    /// <summary>
    /// Entfernen: zuruecksetzen und die Karte wegnehmen.
    ///
    /// Beides zusammen, weil eine Karte nur dann im Stapel liegt, wenn sie etwas tut
    /// oder eben hinzugefuegt wurde. Eine entfernte Karte, deren Werte stehen
    /// bleiben, kaeme beim naechsten Laden unaufgefordert zurueck.
    /// </summary>
    private void OnCardRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string prefix }) return;

        ResetSection(prefix);

        _added.Remove(prefix);

        foreach (string kind in KindsOf(prefix)) Stack.Bypassed.Remove(kind);

        Raise(interim: false);
        ShowActive();
    }

    /// <summary>
    /// Setzt die Werkzeuge eines Abschnitts auf ihre Grundstellung - und die Regler mit.
    ///
    /// Ueber die Werkzeuge und nicht ueber die Regler. Nicht jede Einstellung steht
    /// auf einem Regler: Die Farbbereiche haben acht Baender, von denen die Regler
    /// immer nur eines zeigen, die Kurve hat Punkte, die Tabelle eine Datei. Ein
    /// frisch angelegtes Werkzeug kennt seine Grundstellung genau; von ihm werden
    /// die Werte abgeschrieben, und danach folgen die Regler.
    /// </summary>
    private void ResetSection(string prefix)
    {
        if (prefix == "Basic")
        {
            Adjustments = ImageAdjustments.Neutral;
            CopyDefaults(_vibrance);
        }
        else
        {
            foreach (object tool in ToolsOf(prefix)) CopyDefaults(tool);
        }

        PushToControls();
    }

    /// <summary>Schreibt die Grundstellung eines frischen Werkzeugs in ein bestehendes.</summary>
    private static void CopyDefaults(object tool)
    {
        var type = tool.GetType();

        if (Activator.CreateInstance(type) is not { } fresh) return;

        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public |
                                                    System.Reflection.BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetIndexParameters().Length > 0) continue;

            property.SetValue(tool, property.GetValue(fresh));
        }
    }

    /// <summary>Die Werkzeugobjekte hinter einem Abschnitt.</summary>
    private IEnumerable<object> ToolsOf(string prefix) => prefix switch
    {
        "Basic" => new object[] { _vibrance },
        "Curve" => new object[] { _curves },
        "WhiteBalance" => new object[] { _whiteBalance },
        "Zones" => new object[] { _zones },
        "Bands" => new object[] { _bands },
        "Dehaze" => new object[] { _dehaze },
        "Bloom" => new object[] { _bloom },
        "Halation" => new object[] { _halation },
        "Noise" => new object[] { _noise },
        "Clarity" => new object[] { _clarity },
        "Texture" => new object[] { _texture },
        "Sharpen" => new object[] { _sharpen },
        "Motion" => new object[] { _motion },
        "Displace" => new object[] { _displace },
        "Depth" => new object[] { _depth },
        "Distortion" => new object[] { _distortion },
        "Chromatic" => new object[] { _chromatic },
        "Vignette" => new object[] { _vignette },
        "Dither" => new object[] { _dither, _diffusion },
        "Sort" => new object[] { _sort },
        "Grain" => new object[] { _grain },
        "Lut" => new object[] { _lut },
        _ => Array.Empty<object>(),
    };

    /// <summary>Die Kennungen, unter denen ein Abschnitt ausgeschaltet wird.</summary>
    private IEnumerable<string> KindsOf(string prefix)
        => prefix == "Basic"
            ? Array.Empty<string>()
            : ToolsOf(prefix).Select(tool => tool switch
            {
                IGradingTool g => g.Kind,
                ILocalTool l => l.Kind,
                IOpticsTool o => o.Kind,
                IGeometryTool m => m.Kind,
                IDataTool d => d.Kind,
                IFramePass f => f.Kind,
                _ => "",
            }).Where(kind => kind.Length > 0);

    private bool IsOff(string prefix) => KindsOf(prefix).Any(Stack.IsBypassed);

    /// <summary>Ob ein Effekt eine Karte im Stapel hat.</summary>
    private bool InStack(string prefix)
        => prefix == "Basic" || _added.Contains(prefix) || Doing(prefix) || IsOff(prefix);

    // ---------------------------------------------------------------- Stand

    /// <summary>
    /// Stellt Palette und Karten auf den Stand der Werkzeuge ein.
    ///
    /// An einer Stelle und nicht verteilt: Was im Stapel liegt, was etwas tut und was
    /// ausgeschaltet ist, muss in Palette und Karte dasselbe sagen - drei Stellen
    /// waeren drei Gelegenheiten, dass eine davon zurueckbleibt.
    /// </summary>
    private void ShowActive()
    {
        if (!_built) return;

        foreach (var (_, prefix, _, _) in Sections)
        {
            bool inStack = InStack(prefix);
            bool off = IsOff(prefix);
            bool doing = Doing(prefix) && !off;

            if (_paletteTiles.TryGetValue(prefix, out var tile)) tile.IsChecked = inStack;
            if (_activeBars.TryGetValue(prefix, out var bar))
                bar.Visibility = doing ? Visibility.Visible : Visibility.Hidden;

            if (!_cards.TryGetValue(prefix, out var card)) continue;

            // Im Knotenmodus entscheidet der Knoten, welche Karten zu sehen sind - und
            // Ausschalten und Entfernen gibt es dort am Knoten, nicht an der Karte.
            bool shown = _focus is null ? inStack : _focus.Contains(prefix);

            card.Frame.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

            var buttons = _focus is null && prefix != "Basic" ? Visibility.Visible : Visibility.Collapsed;
            card.Power.Visibility = buttons;
            card.Remove.Visibility = buttons;

            bool open = !_folded.Contains(prefix);

            card.Host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            card.Chevron.Text = open ? "▾" : "▸";

            if (FindName(prefix + "Body") is FrameworkElement body) body.Visibility = Visibility.Visible;

            card.Power.IsChecked = !off;

            // Ausgeschaltet heisst: steht da, tut nichts. Der Kopf wird leiser, aber
            // bleibt lesbar - er ist das, was man zum Wiedereinschalten braucht.
            card.Title.Opacity = off ? 0.5 : 1.0;
        }
    }

    /// <summary>Ob dieses Werkzeug gerade etwas tut - ohne Ruecksicht darauf, ob es ausgeschaltet ist.</summary>
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

    /// <summary>
    /// Sperrt in der Palette, was an einer Ebene nicht gerechnet werden kann.
    ///
    /// Aufgerufen, wenn das Ziel wechselt. Ein Zeichen, das an einer Ebene eine Karte
    /// anlegte, deren Regler dort nie wirken, waere genau die Falle, die bei den
    /// Abschnitten schon zweimal zugeschnappt ist.
    /// </summary>
    private void LockPalette(bool enabled)
    {
        foreach (var (tab, prefix, _, _) in Sections)
        {
            if (!PictureOnly.Contains(tab)) continue;
            if (_paletteTiles.TryGetValue(prefix, out var tile)) tile.IsEnabled = enabled;
        }
    }
}
