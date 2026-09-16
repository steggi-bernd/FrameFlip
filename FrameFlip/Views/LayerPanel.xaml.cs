using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Ebenenstapel als Bedienelement.
///
/// Oben die Liste, darunter die Regler der gewaehlten Ebene - dieselbe Anordnung
/// wie in jedem Bildprogramm, und aus demselben Grund: Erst die Ebene, dann was
/// damit geschieht. Umgekehrt stellte man etwas ein und suchte danach, woran.
///
/// Die Liste zeigt oben, was im Bild oben liegt. Der Stapel selbst laeuft
/// andersherum - Index 0 ist unten, weil von unten nach oben gerechnet wird. Die
/// Umkehrung steht hier und nicht im Modell: eine rueckwaerts gelesene Schleife im
/// Rechenweg waere eine Fehlerquelle, eine rueckwaerts gefuellte Liste in der
/// Oberflaeche ist keine.
/// </summary>
public partial class LayerPanel : UserControl
{
    private IReadOnlyList<ExrPass> _passes = Array.Empty<ExrPass>();
    private ImageLayer? _selected;
    private bool _filling;

    /// <summary>
    /// Die vollen Passnamen hinter den Eintraegen in <c>MaskSourceBox</c>.
    ///
    /// Angezeigt wird der Kurzname, gespeichert der volle - in einer 300 Punkte
    /// breiten Spalte stuende sonst vierzehnmal "ViewLayer." davor.
    /// </summary>
    private readonly List<string> _maskSources = new();

    /// <summary>Die Kryptomatten, die die Datei fuehrt. Meist zwei: Objekt und Material.</summary>
    private IReadOnlyList<CryptomatteSet> _cryptomattes = Array.Empty<CryptomatteSet>();

    /// <summary>
    /// Die Maskenarten in der Reihenfolge der Auswahl.
    ///
    /// Eine Tabelle statt der blossen Aufzaehlung, weil nicht jede Art schon
    /// bedienbar ist: Die Kryptomatte steht im Modell, hat aber noch keine Auswahl
    /// von Objekten - sie hier anzubieten hiesse, einen Eintrag zu zeigen, der nichts
    /// tut.
    /// </summary>
    private static readonly (MaskKind Kind, string Key)[] AllMaskKinds =
    {
        (MaskKind.None, "S_MaskNone"),
        (MaskKind.Luminance, "S_MaskLuminance"),
        (MaskKind.Underlying, "S_MaskUnderlying"),
        (MaskKind.Pass, "S_MaskPass"),
        (MaskKind.Gradient, "S_MaskGradient"),
        (MaskKind.Cryptomatte, "S_MaskCryptomatte"),
    };

    /// <summary>
    /// Die Arten, die diese Datei hergibt. Die Kryptomatte faellt heraus, wenn keine
    /// in der Datei steht - ein Eintrag, der nach einer Auswahl verlangt, die es
    /// nicht gibt, ist schlimmer als keiner.
    /// </summary>
    private (MaskKind Kind, string Key)[] MaskKinds
        => _cryptomattes.Count > 0
            ? AllMaskKinds
            : AllMaskKinds.Where(m => m.Kind != MaskKind.Cryptomatte).ToArray();

    /// <summary>
    /// Wie weit der Rand des Farbrades traegt. 0,5 heisst: ein Kanal reicht von der
    /// Haelfte bis zum Anderthalbfachen - genug fuer eine deutliche Einfaerbung und
    /// wenig genug, dass ein Kanal nie auf null faellt.
    /// </summary>
    private const float TintScale = 0.5f;

    public LayerPanel()
    {
        InitializeComponent();

        foreach (var (_, key) in Blending.All) ModeBox.Items.Add(Strings.T(key));
        ModeBox.SelectedIndex = 0;

        FillMaskKinds();

        TintWheel.Changed += OnTintChanged;
        TintWheel.Released += () => Raise(interim: false);

        Rebuild();
    }

    /// <summary>
    /// Etwas hat sich geaendert. <c>interim</c> heisst: es wird gerade gezogen, eine
    /// grobe Vorschau reicht.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>Der Stapel. Wird an Ort und Stelle veraendert, nie ersetzt.</summary>
    public LayerStack Stack { get; } = new();

    /// <summary>
    /// Nimmt eine Datei entgegen: welche Passe sie fuehrt, und welcher Stapel
    /// darauf gelten soll.
    ///
    /// Ein leerer Stapel bekommt eine Ebene auf dem Hauptbild. Das ist dieselbe
    /// Ansicht wie ohne Ebenen ueberhaupt - nur steht sie jetzt in der Liste, und
    /// damit ist zu sehen, dass sich daran etwas machen laesst.
    /// </summary>
    public void Load(IReadOnlyList<ExrPass> passes, LayerStack? stack)
        => Load(passes, Array.Empty<CryptomatteSet>(), stack);

    /// <inheritdoc cref="Load(IReadOnlyList{ExrPass}, LayerStack?)"/>
    /// <param name="cryptomattes">
    /// Was die Datei an Kryptomatten fuehrt. Leer heisst: keine - dann steht die Art
    /// gar nicht erst zur Wahl.
    /// </param>
    public void Load(IReadOnlyList<ExrPass> passes, IReadOnlyList<CryptomatteSet> cryptomattes,
                     LayerStack? stack)
    {
        _passes = passes;
        _cryptomattes = cryptomattes;

        FillMaskKinds();
        FillMaskSources();

        Stack.Layers.Clear();
        if (stack is not null) Stack.Layers.AddRange(stack.Layers.Select(l => l.Clone()));

        if (Stack.Layers.Count == 0) Stack.Layers.Add(BaseLayer());

        _selected = Stack.Layers[^1];
        Rebuild();

        // Eingeklappt, wenn es nichts zu waehlen und nichts zu sehen gibt: Bei einem
        // PNG ist die Liste eine Zeile lang, und zweihundert Punkte Hoehe dafuer
        // waeren im schmalen Streifen der teuerste Platz, den es gibt. Die
        // Ueberschrift bleibt stehen - wer schichten will, findet sie.
        Fold(open: HasChoice || Stack.Layers.Count > 1);
    }

    private void Fold(bool open)
    {
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FoldButton.Content = open ? "−" : "+";
    }

    /// <summary>
    /// Ob die Datei mehrere Passe zur Wahl stellt.
    ///
    /// Das ist NICHT die Frage, ob sich schichten laesst - die stellt sich nicht.
    /// Ein PNG fuehrt genau ein Bild, aber dasselbe Bild ein zweites Mal und auf
    /// Multiplizieren gestellt ist der Griff, mit dem in Photoshop jeder Kontrast
    /// anfaengt, und der rechnet hier genauso.
    ///
    /// Die Antwort entscheidet nur darueber, ob der Streifen aufgeklappt beginnt.
    /// </summary>
    public bool HasChoice => _passes.Count > 1;

    /// <summary>
    /// Die unterste Ebene: das Bild, wie die Datei es ohnehin hergibt.
    ///
    /// Auf Normal und nicht auf Add, weil sie auf Schwarz liegt und beides dort
    /// dasselbe ergibt - aber wer sie ansieht, soll "das Bild" lesen und nicht
    /// "das Bild, addiert zu etwas".
    /// </summary>
    private static ImageLayer BaseLayer() => new()
    {
        Source = "",
        Name = Strings.T("S_LayerColour"),
        Mode = BlendMode.Normal,
    };

    // ------------------------------------------------------------------- die Liste

    private void Rebuild()
    {
        _filling = true;

        try
        {
            LayerList.Items.Clear();

            // Rueckwaerts: oben in der Liste ist oben im Bild.
            for (int i = Stack.Layers.Count - 1; i >= 0; i--)
                LayerList.Items.Add(Row(Stack.Layers[i]));

            if (_selected is not null && !Stack.Layers.Contains(_selected))
                _selected = Stack.Layers.Count > 0 ? Stack.Layers[^1] : null;

            foreach (ListBoxItem item in LayerList.Items)
                if (ReferenceEquals(item.Tag, _selected)) item.IsSelected = true;
        }
        finally
        {
            _filling = false;
        }

        PushToControls();
        UpdateButtons();
    }

    private ListBoxItem Row(ImageLayer layer)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var eye = new ToggleButton
        {
            Style = (Style)FindResource("LayerEye"),
            IsChecked = layer.Visible,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = layer,
        };

        eye.Click += OnVisibilityClicked;
        Grid.SetColumn(eye, 0);
        grid.Children.Add(eye);

        bool missing = layer.Source.Length > 0 && ExrPasses.Find(_passes, layer.Source) is null;

        // Eine angeschnittene Ebene rueckt ein und bekommt einen Pfeil davor -
        // dieselbe Schreibweise wie in Photoshop, und sie sagt in einem Zeichen,
        // was sonst ein Satz waere: "das hier gilt nur fuer die Zeile darunter".
        bool clipped = layer.Clipped && Stack.Layers.IndexOf(layer) > 0;

        var name = new TextBlock
        {
            Margin = new Thickness(clipped ? 12 : 0, 0, 0, 0),
            Text = (clipped ? "↳ " : "") + (layer.Name.Length > 0 ? layer.Name : layer.Source),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = layer.Visible ? 1.0 : 0.45,
            Foreground = (System.Windows.Media.Brush)FindResource(missing ? "GapBrush" : "ForegroundBrush"),
            ToolTip = missing ? layer.Source + " — " + Strings.T("S_PassMissing") : layer.Source,
        };

        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        // Die Mischung steht in der Zeile, nicht nur im Auswahlfeld darunter: Ein
        // Stapel, in dem eine Ebene multipliziert und der Rest addiert, erklaert
        // sich damit auf einen Blick.
        // Ein Punkt vor der Mischung, wenn die Ebene maskiert ist. Ohne ihn waere
        // eine Ebene, die nur an einer Stelle wirkt, in der Liste nicht von einer zu
        // unterscheiden, die ueberall wirkt - und man suchte den Grund woanders.
        var mode = new TextBlock
        {
            Text = (layer.Mask.IsNeutral ? "" : "\u25D0 ") +
                   Strings.T(Blending.All.First(m => m.Mode == layer.Mode).Key),
            FontSize = 9,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
        };

        Grid.SetColumn(mode, 2);
        grid.Children.Add(mode);

        return new ListBoxItem { Content = grid, Tag = layer };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        if (LayerList.SelectedItem is not ListBoxItem item || item.Tag is not ImageLayer layer) return;

        _selected = layer;
        PushToControls();
        UpdateButtons();
    }

    private void OnVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not ImageLayer layer) return;

        layer.Visible = button.IsChecked == true;
        _selected = layer;

        // Ein neuer Pass kann dadurch gebraucht werden, der noch nicht gelesen ist -
        // deshalb die vollstaendige Meldung und nicht die vorlaeufige.
        Rebuild();
        Raise(interim: false);
    }

    // ------------------------------------------------------------------- Bedienung

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        // Ohne Passe zur Wahl gibt es nichts auszuwaehlen. Ein Menue mit einem
        // einzigen Eintrag ist eine Ruecktrage: Der Knopf legt dann gleich die
        // Ebene an.
        if (_passes.Count == 0)
        {
            Add(null);
            return;
        }

        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = PlacementMode.Bottom };

        if (_passes.Count > 1)
        {
            // Ein Eintrag, der den ganzen Stapel aufbaut. Zwanzig Passe einzeln
            // anzuklicken ist die Art Arbeit, die niemand zweimal macht.
            var all = new MenuItem { Header = Strings.T("S_AddAllPasses") };
            all.Click += (_, _) => RebuildFromPasses();
            menu.Items.Add(all);
            menu.Items.Add(new Separator());
        }

        foreach (var pass in _passes)
        {
            var captured = pass;
            var item = new MenuItem { Header = pass.ShortName };
            item.Click += (_, _) => Add(captured);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            // Kein Pass zur Wahl heisst nicht "nichts geht": Dasselbe Bild ein
            // zweites Mal ist der Griff, mit dem in Photoshop jeder Kontrast anfaengt.
            var again = new MenuItem { Header = Strings.T("S_LayerColour") };
            again.Click += (_, _) => Add(null);
            menu.Items.Add(again);
        }

        menu.IsOpen = true;
    }

    private void Add(ExrPass? pass)
    {
        var layer = new ImageLayer
        {
            Source = pass?.Name ?? "",
            Name = pass?.ShortName ?? Strings.T("S_LayerColour"),
            Mode = Stack.Layers.Count == 0 ? BlendMode.Normal : BlendMode.Add,
        };

        Stack.Layers.Add(layer);
        _selected = layer;

        Rebuild();
        Raise(interim: false);
    }

    /// <summary>
    /// Baut den Stapel, der die Zerlegung wieder zusammensetzt.
    ///
    /// Wie das geht, steht in <see cref="PassStack"/> - es ist nicht "alle
    /// addieren", sondern Licht mal Farbe. Fuehrt die Datei keine Lichtpasse, bleibt
    /// es beim Bild selbst.
    ///
    /// Oeffentlich, weil der Eintrag im Menue nur ein Weg hierher ist und nicht der
    /// einzige: Ein Menuepunkt laesst sich nicht pruefen, dieser Aufruf schon.
    /// </summary>
    public void RebuildFromPasses()
    {
        var built = PassStack.Rebuild(_passes);
        if (built.Layers.Count == 0) built.Layers.Add(BaseLayer());

        Stack.Layers.Clear();
        Stack.Layers.AddRange(built.Layers);

        _selected = Stack.Layers[^1];
        Rebuild();
        Raise(interim: false);
    }

    private void OnDuplicateClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var copy = _selected.Clone();
        copy.Name = _selected.Name + " ·";

        Stack.Layers.Insert(Stack.Layers.IndexOf(_selected) + 1, copy);
        _selected = copy;

        Rebuild();
        Raise(interim: false);
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        // Die letzte Ebene bleibt stehen. Ein leerer Stapel ergaebe ein schwarzes
        // Bild, und das sieht aus wie ein Fehler statt wie eine Einstellung.
        if (_selected is null || Stack.Layers.Count <= 1) return;

        int at = Stack.Layers.IndexOf(_selected);
        Stack.Layers.RemoveAt(at);
        _selected = Stack.Layers[Math.Clamp(at, 0, Stack.Layers.Count - 1)];

        Rebuild();
        Raise(interim: false);
    }

    /// <summary>
    /// Schaltet die Schnittmaske der gewaehlten Ebene um.
    ///
    /// Die unterste Ebene kann sich an nichts anschneiden - dort tut der Knopf
    /// nichts, und er sagt es auch, indem er gesperrt bleibt.
    /// </summary>
    private void OnClipClicked(object sender, RoutedEventArgs e)
    {
        if (_selected is null || Stack.Layers.IndexOf(_selected) <= 0) return;

        _selected.Clipped = !_selected.Clipped;

        Rebuild();
        Raise(interim: false);
    }

    private void OnUpClicked(object sender, RoutedEventArgs e) => Move(+1);

    private void OnDownClicked(object sender, RoutedEventArgs e) => Move(-1);

    private void Move(int direction)
    {
        if (_selected is null) return;

        int at = Stack.Layers.IndexOf(_selected);
        int to = at + direction;
        if (to < 0 || to >= Stack.Layers.Count) return;

        Stack.Layers.RemoveAt(at);
        Stack.Layers.Insert(to, _selected);

        Rebuild();

        // Die Reihenfolge aendert das Bild nur, wenn die Mischungen nicht alle
        // vertauschbar sind - bei Add ist sie es. Trotzdem wird gerechnet: zu
        // pruefen, ob es sich lohnt, kostet mehr Gedanken als der Durchgang.
        Raise(interim: false);
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        Stack.Layers.Clear();
        Stack.Layers.Add(BaseLayer());
        _selected = Stack.Layers[0];

        Rebuild();
        Raise(interim: false);
    }

    private void OnFoldClicked(object sender, RoutedEventArgs e)
    {
        bool open = Body.Visibility != Visibility.Visible;
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FoldButton.Content = open ? "−" : "+";
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        _selected.Mode = Blending.All[Math.Clamp(ModeBox.SelectedIndex, 0, Blending.All.Length - 1)].Mode;

        Rebuild();
        Raise(interim: false);
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Wie im Werkzeugstreifen: Beim Einlesen der XAML setzt jeder Regler erst
        // sein Minimum, und die weiter unten stehenden gibt es dann noch nicht.
        if (_filling || !IsLoaded || _selected is null) return;

        _selected.Opacity = (float)OpacitySlider.Value;
        _selected.Exposure = (float)LayerExposureSlider.Value;

        UpdateValues();
        Raise(interim: true);
    }

    private void OnTintChanged()
    {
        if (_filling || _selected is null) return;

        var (r, g, b) = ColourWheelMath.ToChannels(TintWheel.Value, 0f, 1f, TintScale);
        _selected.Tint.R = r;
        _selected.Tint.G = g;
        _selected.Tint.B = b;

        Raise(interim: true);
    }

    private void OnSliderReset(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        if (!double.TryParse(slider.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double neutral))
            return;

        e.Handled = true;
        slider.Value = neutral;
    }

    // ------------------------------------------------------------------- Abgleich

    private void PushToControls()
    {
        _filling = true;

        try
        {
            LayerTools.IsEnabled = _selected is not null;
            if (_selected is null) return;

            int mode = Array.FindIndex(Blending.All, m => m.Mode == _selected.Mode);
            ModeBox.SelectedIndex = Math.Max(0, mode);

            OpacitySlider.Value = Math.Clamp(_selected.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            LayerExposureSlider.Value = Math.Clamp(_selected.Exposure,
                                                   LayerExposureSlider.Minimum, LayerExposureSlider.Maximum);

            TintWheel.Value = ColourWheelMath.ToPoint(_selected.Tint.R / TintScale,
                                                      _selected.Tint.G / TintScale,
                                                      _selected.Tint.B / TintScale);

            PushMaskToControls();
        }
        finally
        {
            _filling = false;
        }

        UpdateValues();
        ShowMaskControls();
    }

    private void UpdateValues()
    {
        OpacityValue.Text = $"{OpacitySlider.Value:0.00}";
        LayerExposureValue.Text = LayerExposureSlider.Value == 0
            ? "0"
            : $"{LayerExposureSlider.Value:+0.00;-0.00}";
    }

    private void UpdateButtons()
    {
        int at = _selected is null ? -1 : Stack.Layers.IndexOf(_selected);

        DuplicateButton.IsEnabled = at >= 0;
        RemoveButton.IsEnabled = at >= 0 && Stack.Layers.Count > 1;
        UpButton.IsEnabled = at >= 0 && at < Stack.Layers.Count - 1;
        DownButton.IsEnabled = at > 0;
        ClipButton.IsEnabled = at > 0;
        ClipButton.Opacity = at > 0 && _selected!.Clipped ? 1.0 : 0.55;

        // Der Hinweis sagt in beiden Faellen etwas anderes: bei Passen, wie sie
        // zusammengehoeren; bei einem Einzelbild, dass Schichten trotzdem geht.
        HintText.Text = Strings.T(HasChoice ? "S_LayersHint" : "S_SinglePass");
    }

    private void Raise(bool interim) => Changed?.Invoke(interim);

    // --------------------------------------------------------------------- Masken

    private void FillMaskKinds()
    {
        _filling = true;

        try
        {
            MaskBox.Items.Clear();
            foreach (var (_, key) in MaskKinds) MaskBox.Items.Add(Strings.T(key));
            MaskBox.SelectedIndex = 0;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// Fuellt die Quellenauswahl - je nach Art mit Passen oder mit Kryptomatten.
    ///
    /// Dasselbe Feld fuer beides, weil es dieselbe Frage ist: "woraus kommt die
    /// Maske?". Zwei Felder uebereinander, von denen immer eines leer waere, kosteten
    /// im schmalen Streifen Platz und erklaerten nichts.
    /// </summary>
    private void FillMaskSources()
    {
        _filling = true;

        try
        {
            MaskSourceBox.Items.Clear();
            _maskSources.Clear();

            if (_selected?.Mask.Kind == MaskKind.Cryptomatte)
            {
                foreach (var set in _cryptomattes)
                {
                    MaskSourceBox.Items.Add(set.ShortName);
                    _maskSources.Add(set.Prefix);
                }

                return;
            }

            foreach (var pass in _passes)
            {
                // Eine Kryptomattenstufe ist kein Bild - als Maske gelesen waere sie
                // ein Hashwert. Sie gehoert in die Kryptomattenauswahl und nicht hier
                // in die Passliste.
                if (Cryptomatte.IsLevel(pass.ShortName)) continue;

                MaskSourceBox.Items.Add(pass.ShortName);
                _maskSources.Add(pass.Name);
            }
        }
        finally
        {
            _filling = false;
        }
    }

    // ----------------------------------------------------------- Kryptomatten

    /// <summary>
    /// Der Anwender moechte ins Bild klicken, um zu waehlen - oder nicht mehr.
    ///
    /// Der Streifen kann das nicht selbst: Er hat kein Bild. Er sagt nur Bescheid,
    /// und wer das Bild zeigt, holt die Kennung und meldet sie zurueck.
    /// </summary>
    public event Action<bool>? PickMode;

    /// <summary>
    /// Die unterste Stufe der Kryptomatte, aus der die Kennung zu lesen ist. Null,
    /// wenn gerade keine Kryptomattenmaske gewaehlt ist.
    /// </summary>
    public string? PickLevel
        => _selected?.Mask is { Kind: MaskKind.Cryptomatte, Levels.Count: > 0 } mask
            ? mask.Levels[0]
            : null;

    /// <summary>Nimmt ein gewaehltes Objekt auf. Ein zweites Mal nimmt es wieder weg.</summary>
    public void AddPick(string name, float id)
    {
        if (_selected is null || _selected.Mask.Kind != MaskKind.Cryptomatte) return;

        var picks = _selected.Mask.Picks;
        var already = picks.FirstOrDefault(p => p.Id.Equals(id));

        // Noch einmal auf dasselbe Objekt zu klicken nimmt es heraus. Das ist der
        // Griff, den man ohnehin versucht, und er erspart das Zielen auf ein
        // Kreuzchen in einer schmalen Liste.
        if (already is not null) picks.Remove(already);
        else picks.Add(new CryptoPick { Name = name, Id = id });

        ShowPicks();
        Rebuild();
        Raise(interim: false);
    }

    private void OnCryptoPickToggled(object sender, RoutedEventArgs e)
    {
        bool on = CryptoPickButton.IsChecked == true;

        CryptoPickButton.Content = Strings.T(on ? "S_CryptoPickOn" : "S_CryptoPick");
        if (!_filling) PickMode?.Invoke(on);
    }

    /// <summary>Beendet den Klickmodus - etwa, wenn ein anderes Bild geoeffnet wird.</summary>
    public void StopPicking()
    {
        if (CryptoPickButton.IsChecked != true) return;

        _filling = true;

        try
        {
            CryptoPickButton.IsChecked = false;
            CryptoPickButton.Content = Strings.T("S_CryptoPick");
        }
        finally
        {
            _filling = false;
        }
    }

    private void ShowPicks()
    {
        CryptoPicks.Items.Clear();

        var picks = _selected?.Mask.Picks ?? new List<CryptoPick>();

        foreach (var pick in picks)
        {
            var row = new Grid { Margin = new Thickness(8, 2, 4, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = pick.Name.Length > 0 ? pick.Name : Strings.T("S_CryptoUnknown"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var drop = new Button
            {
                Style = (Style)FindResource("LinkButton"),
                Content = "\u2715",
                Tag = pick,
            };

            drop.Click += OnDropPickClicked;
            Grid.SetColumn(drop, 1);
            row.Children.Add(drop);

            CryptoPicks.Items.Add(row);
        }

        CryptoEmpty.Visibility = picks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDropPickClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CryptoPick pick) return;
        if (_selected is null) return;

        _selected.Mask.Picks.Remove(pick);

        ShowPicks();
        Rebuild();
        Raise(interim: false);
    }

    /// <summary>
    /// Setzt eine Kryptomatte als Quelle: ihren Namen und die Stufen, die dazu in
    /// der Datei stehen.
    /// </summary>
    private void UseCryptomatte(LayerMask mask, string prefix)
    {
        mask.Source = prefix;
        mask.Levels = Cryptomatte.Levels(_passes, prefix).ToList();

        // Die Auswahl gilt nur fuer IHREN Satz: Die Kennungen von Objekten und
        // Materialien sind verschiedene Hashes, und eine mitgenommene Auswahl traefe
        // im anderen Satz nichts - eine Maske, die stillschweigend leer ist.
        mask.Picks.Clear();
    }

    private void OnMaskFoldClicked(object sender, RoutedEventArgs e)
    {
        bool open = MaskBody.Visibility != Visibility.Visible;
        MaskBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MaskFoldButton.Content = open ? "\u2212" : "+";
    }

    private void OnMaskKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        var mask = _selected.Mask;
        var kinds = MaskKinds;
        mask.Kind = kinds[Math.Clamp(MaskBox.SelectedIndex, 0, kinds.Length - 1)].Kind;

        // Beim Umschalten gleich die erste Quelle nehmen. Eine Maskenart ohne Quelle
        // waere eine Einstellung, die stillschweigend nichts tut.
        FillMaskSources();

        if (mask.Kind == MaskKind.Cryptomatte)
        {
            if (_cryptomattes.Count > 0 &&
                !_cryptomattes.Any(s => s.Prefix.Equals(mask.Source, StringComparison.Ordinal)))
            {
                UseCryptomatte(mask, _cryptomattes[0].Prefix);
            }
        }
        else if (mask.Kind == MaskKind.Pass && mask.Source.Length == 0 && _maskSources.Count > 0)
        {
            mask.Source = _maskSources[0];
        }

        ShowMaskControls();
        PushMaskToControls();
        Rebuild();
        Raise(interim: false);
    }

    private void OnMaskSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _selected is null) return;

        int at = MaskSourceBox.SelectedIndex;
        if (at < 0 || at >= _maskSources.Count) return;

        if (_selected.Mask.Kind == MaskKind.Cryptomatte)
        {
            UseCryptomatte(_selected.Mask, _maskSources[at]);
            ShowPicks();
        }
        else
        {
            _selected.Mask.Source = _maskSources[at];
        }

        // Ein neuer Pass muss gelesen werden - deshalb die vollstaendige Meldung.
        Raise(interim: false);
    }

    private void OnMaskInvertChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || _selected is null) return;

        _selected.Mask.Invert = MaskInvertButton.IsChecked == true;
        Raise(interim: false);
    }

    private void OnMaskSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || !IsLoaded || _selected is null) return;

        var mask = _selected.Mask;

        mask.Low = (float)MaskLowSlider.Value;
        mask.High = (float)MaskHighSlider.Value;
        mask.Softness = (float)MaskSoftSlider.Value;
        mask.Angle = (float)MaskAngleSlider.Value;
        mask.Centre = (float)MaskCentreSlider.Value;
        mask.Width = (float)MaskWidthSlider.Value;

        // Von darf Bis nicht ueberholen - sonst laesst die Maske nichts mehr durch,
        // und das sieht aus, als waere die Ebene verschwunden.
        if (mask.Low > mask.High)
        {
            _filling = true;

            try
            {
                if (ReferenceEquals(sender, MaskLowSlider)) MaskHighSlider.Value = mask.Low;
                else MaskLowSlider.Value = mask.High;
            }
            finally
            {
                _filling = false;
            }

            mask.Low = (float)MaskLowSlider.Value;
            mask.High = (float)MaskHighSlider.Value;
        }

        UpdateMaskValues();
        Raise(interim: true);
    }

    /// <summary>Zeigt die Regler, die zur gewaehlten Art gehoeren - und nur die.</summary>
    private void ShowMaskControls()
    {
        var kind = _selected?.Mask.Kind ?? MaskKind.None;

        bool range = kind is MaskKind.Luminance or MaskKind.Underlying or MaskKind.Pass
                          or MaskKind.Cryptomatte;
        bool gradient = kind == MaskKind.Gradient;
        bool source = kind is MaskKind.Pass or MaskKind.Cryptomatte;
        bool crypto = kind == MaskKind.Cryptomatte;

        MaskCryptoBody.Visibility = crypto ? Visibility.Visible : Visibility.Collapsed;
        if (!crypto) StopPicking();

        MaskRangeBody.Visibility = range ? Visibility.Visible : Visibility.Collapsed;
        MaskGradientBody.Visibility = gradient ? Visibility.Visible : Visibility.Collapsed;
        MaskSourceBox.Visibility = source ? Visibility.Visible : Visibility.Collapsed;
        MaskInvertButton.IsEnabled = kind != MaskKind.None;

        // Dieselben zwei Regler, aber nicht dieselbe Rechnung - und deshalb auch
        // nicht dieselbe Beschriftung. Bei der Helligkeit sind sie ein Fenster: was
        // dazwischen liegt, wirkt. Auf einem Maskenpass sind sie Schwarz- und
        // Weisspunkt: was darunter liegt, faellt weg, was darueber liegt, wirkt voll.
        // Gleich beschriftet waere das eine Falle.
        // Eine Deckung ist wie ein Maskenpass schon ein Anteil - dort sind es
        // Schwarz- und Weisspunkt, nicht ein Fenster.
        bool levels = kind is MaskKind.Pass or MaskKind.Cryptomatte;

        MaskLowLabel.Text = Strings.T(levels ? "S_MaskBlack" : "S_MaskFrom");
        MaskHighLabel.Text = Strings.T(levels ? "S_MaskWhite" : "S_MaskTo");

        // Die Weichheit gehoert zum Fenster. Ein Schwarz- und ein Weisspunkt haben
        // ihre Kante schon im Abstand zueinander.
        MaskSoftRow.Visibility = levels ? Visibility.Collapsed : Visibility.Visible;
        MaskSoftSlider.Visibility = levels ? Visibility.Collapsed : Visibility.Visible;

        MaskHint.Text = kind switch
        {
            MaskKind.Pass => Strings.T("S_MaskHintPass"),
            MaskKind.Gradient => Strings.T("S_MaskHintGradient"),
            MaskKind.Luminance or MaskKind.Underlying => Strings.T("S_MaskHintLuma"),
            MaskKind.Cryptomatte => Strings.T("S_CryptoHint"),
            _ => "",
        };

        MaskHint.Visibility = MaskHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PushMaskToControls()
    {
        var mask = _selected?.Mask ?? new LayerMask();

        int kind = Array.FindIndex(MaskKinds, m => m.Kind == mask.Kind);
        MaskBox.SelectedIndex = Math.Max(0, kind);

        ShowPicks();

        MaskInvertButton.IsChecked = mask.Invert;

        MaskLowSlider.Value = Math.Clamp(mask.Low, MaskLowSlider.Minimum, MaskLowSlider.Maximum);
        MaskHighSlider.Value = Math.Clamp(mask.High, MaskHighSlider.Minimum, MaskHighSlider.Maximum);
        MaskSoftSlider.Value = Math.Clamp(mask.Softness, MaskSoftSlider.Minimum, MaskSoftSlider.Maximum);
        MaskAngleSlider.Value = Math.Clamp(mask.Angle, MaskAngleSlider.Minimum, MaskAngleSlider.Maximum);
        MaskCentreSlider.Value = Math.Clamp(mask.Centre, MaskCentreSlider.Minimum, MaskCentreSlider.Maximum);
        MaskWidthSlider.Value = Math.Clamp(mask.Width, MaskWidthSlider.Minimum, MaskWidthSlider.Maximum);

        int source = _maskSources.FindIndex(s => s.Equals(mask.Source, StringComparison.Ordinal));
        MaskSourceBox.SelectedIndex = source;

        // Die Quellen haengen an der Art - wechselt die Auswahl auf eine Ebene mit
        // Kryptomatte, muss dort auch die Kryptomattenliste stehen.
        if (source < 0 && mask.Source.Length > 0) FillMaskSources();

        UpdateMaskValues();
    }

    private void UpdateMaskValues()
    {
        MaskLowValue.Text = $"{MaskLowSlider.Value:0.00}";
        MaskHighValue.Text = $"{MaskHighSlider.Value:0.00}";
        MaskSoftValue.Text = $"{MaskSoftSlider.Value:0.00}";
        MaskAngleValue.Text = $"{MaskAngleSlider.Value:0} \u00B0";
        MaskCentreValue.Text = $"{MaskCentreSlider.Value:0.00}";
        MaskWidthValue.Text = $"{MaskWidthSlider.Value:0.00}";
    }
}
