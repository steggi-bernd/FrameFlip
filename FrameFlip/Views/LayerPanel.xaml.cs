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
    {
        _passes = passes;

        Stack.Layers.Clear();
        if (stack is not null) Stack.Layers.AddRange(stack.Layers.Select(l => l.Clone()));

        if (Stack.Layers.Count == 0) Stack.Layers.Add(BaseLayer());

        _selected = Stack.Layers[^1];
        Rebuild();
    }

    /// <summary>
    /// Ob es ueberhaupt etwas zu schichten gibt. Eine Datei mit einem einzigen Pass
    /// kann trotzdem Ebenen tragen - zweimal dasselbe Bild multipliziert ist ein
    /// gewoehnlicher Griff -, aber die Liste ist dann eine Zeile lang und steht dem
    /// Rest im Weg.
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
        var mode = new TextBlock
        {
            Text = Strings.T(Blending.All.First(m => m.Mode == layer.Mode).Key),
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
        }
        finally
        {
            _filling = false;
        }

        UpdateValues();
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

        HintText.Visibility = _passes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Raise(bool interim) => Changed?.Invoke(interim);
}
