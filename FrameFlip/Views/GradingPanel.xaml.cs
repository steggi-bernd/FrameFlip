using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using Color = System.Windows.Media.Color;

namespace FrameFlip.Views;

/// <summary>
/// Alle Regler der Farbkorrektur an einem Ort: Grundkorrektur, Kurven,
/// Weissabgleich, Dynamik, Zonen, Farbbereiche und Tabelle.
///
/// Als eigenes Steuerelement, damit es die Werkzeuge nur einmal gibt. Sie an zwei
/// Stellen zu pflegen - im Vorschaufenster und auf der Atelierseite - waere die
/// Art Doppelung, bei der ein halbes Jahr spaeter ein Regler an einer Stelle
/// anders rechnet als an der anderen, und niemand weiss mehr, welche stimmt.
///
/// Das Steuerelement kennt weder Bild noch Datei. Es traegt die Einstellungen und
/// meldet, wenn sich etwas geaendert hat; was daraus wird, entscheidet der, der es
/// einhaengt.
/// </summary>
public partial class GradingPanel : UserControl
{
    private readonly List<Slider> _bandSliders = new();
    private int _bandMode;
    private bool _filling;

    private CurvesTool _curves = new();
    private WhiteBalanceTool _whiteBalance = new();
    private LiftGammaGainTool _zones = new();
    private HslTool _bands = new();
    private VibranceTool _vibrance = new();
    private LutTool _lut = new();

    private static readonly Color[] CurveColours =
    {
        Color.FromRgb(0xE8, 0xE8, 0xE8),
        Color.FromRgb(0xE0, 0x6C, 0x6C),
        Color.FromRgb(0x6C, 0xD0, 0x86),
        Color.FromRgb(0x6C, 0x9C, 0xE0),
    };

    public GradingPanel()
    {
        InitializeComponent();

        BuildBandSliders();
        CurveField.Curve = _curves.Master;
        CurveField.LineColour = CurveColours[0];
        CurveField.Changed += () => Raise(interim: true);
        CurveField.Released += () => Raise(interim: false);

        Loaded += (_, _) => PushToControls();
    }

    /// <summary>
    /// Etwas hat sich geaendert. <c>interim</c> heisst: es wird gerade gezogen, eine
    /// grobe Vorschau reicht - beim Loslassen kommt dieselbe Meldung noch einmal
    /// mit false.
    /// </summary>
    public event Action<bool>? Changed;

    /// <summary>Die Grundkorrektur - dieselbe, die auch die Wiedergabe benutzt.</summary>
    public ImageAdjustments Adjustments { get; private set; } = ImageAdjustments.Neutral;

    /// <summary>Die Werkzeuge. Wirken nur auf Gleitkommamaterial.</summary>
    public GradingStack Stack { get; } = new();

    /// <summary>Der vorbereitete Stapel, fertig zum Rechnen.</summary>
    public PreparedGrading Prepared { get; private set; } = PreparedGrading.None;

    /// <summary>
    /// Uebernimmt gespeicherte Einstellungen. Die Werkzeuge werden dabei aus dem
    /// Stapel geholt oder angelegt, damit jedes genau einmal vorkommt.
    /// </summary>
    public void Load(ImageAdjustments? adjustments, GradingStack? stack)
    {
        Adjustments = adjustments?.Clamped() ?? ImageAdjustments.Neutral;

        Stack.Tools.Clear();
        if (stack is not null) Stack.Tools.AddRange(stack.Tools);

        _curves = Take<CurvesTool>();
        _whiteBalance = Take<WhiteBalanceTool>();
        _zones = Take<LiftGammaGainTool>();
        _bands = Take<HslTool>();
        _vibrance = Take<VibranceTool>();
        _lut = Take<LutTool>();

        CurveField.Curve = _curves.Master;
        PushToControls();
        Prepared = Stack.Prepare();

        T Take<T>() where T : IGradingTool, new()
        {
            var found = Stack.Tools.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Tools.Add(created);
            return created;
        }
    }

    /// <summary>Uebernimmt eine Messung in das Histogramm und den Hintergrund der Kurve.</summary>
    public void ShowHistogram(Histogram histogram)
    {
        Histogram.Update(histogram);

        CurveField.Background = ReferenceEquals(CurveField.Curve, _curves.Red) ? histogram.Red
                              : ReferenceEquals(CurveField.Curve, _curves.Green) ? histogram.Green
                              : ReferenceEquals(CurveField.Curve, _curves.Blue) ? histogram.Blue
                              : histogram.Luma;

        CurveField.InvalidateVisual();
        UpdateClipText(histogram);
    }

    /// <summary>
    /// Ob die Werkzeuge etwas ausrichten koennen. Ohne Gleitkommamaterial wirken sie
    /// nicht, und dann sollen sie auch nicht bedienbar aussehen.
    /// </summary>
    public bool ToolsEnabled
    {
        get => CurveBody.IsEnabled;
        set
        {
            CurveBody.IsEnabled = value;
            WhiteBalanceBody.IsEnabled = value;
            ZonesBody.IsEnabled = value;
            BandsBody.IsEnabled = value;
            LutBody.IsEnabled = value;
            VibranceSlider.IsEnabled = value;
        }
    }

    /// <summary>
    /// Dieselben Bausteine wie im Vorschaufenster, damit dort nicht "0,4 % ausgebrannt"
    /// steht und hier etwas anderes.
    /// </summary>
    private void UpdateClipText(Histogram histogram)
    {
        var parts = new List<string>();

        if (histogram.ClippedLow > HistogramView.ClipThreshold)
            parts.Add(Strings.T("S_ClippedLow", $"{histogram.ClippedLow * 100:0.#}"));

        if (histogram.ClippedHigh > HistogramView.ClipThreshold)
            parts.Add(Strings.T("S_ClippedHigh", $"{histogram.ClippedHigh * 100:0.#}"));

        // Die Reserve oberhalb von Weiss gibt es nur auf Gleitkommamaterial, und sie
        // ist hier die interessantere Angabe: ein Bild kann ausgebrannt aussehen und
        // trotzdem voller Zeichnung sein, die der Belichtungsregler noch holt.
        if (histogram.AboveWhite > 0.0005)
            parts.Add(Strings.T("S_AboveWhite", $"{histogram.AboveWhite * 100:0.#}"));

        ClipText.Text = parts.Count == 0 ? Strings.T("S_NoClipping") : string.Join("   ", parts);
        ClipText.Foreground = (System.Windows.Media.Brush)FindResource(
            histogram.ClippedHigh > HistogramView.ClipThreshold ||
            histogram.ClippedLow > HistogramView.ClipThreshold ? "GapBrush" : "MutedBrush");
    }

    private void BuildBandSliders()
    {
        BandSliders.Children.Clear();
        _bandSliders.Clear();

        string[] keys =
        {
            "S_BandRed", "S_BandOrange", "S_BandYellow", "S_BandGreen",
            "S_BandAqua", "S_BandBlue", "S_BandViolet", "S_BandMagenta",
        };

        for (int i = 0; i < keys.Length; i++)
        {
            BandSliders.Children.Add(new TextBlock
            {
                Text = Strings.T(keys[i]),
                Style = (Style)FindResource("PanelLabel"),
                Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 2),
            });

            var slider = new Slider
            {
                Style = (Style)FindResource("PanelSlider"),
                Minimum = -100,
                Maximum = 100,
                Value = 0,
                SmallChange = 1,
                LargeChange = 5,
                Tag = "0",
            };

            slider.ValueChanged += OnChanged;
            slider.MouseDoubleClick += OnSliderReset;

            BandSliders.Children.Add(slider);
            _bandSliders.Add(slider);
        }
    }

    private void PushToControls()
    {
        _filling = true;

        try
        {
            ExposureSlider.Value = Math.Clamp(Adjustments.Exposure, ExposureSlider.Minimum, ExposureSlider.Maximum);
            GammaSlider.Value = Math.Clamp(Adjustments.Gamma, GammaSlider.Minimum, GammaSlider.Maximum);
            ContrastSlider.Value = Math.Clamp(Adjustments.Contrast, ContrastSlider.Minimum, ContrastSlider.Maximum);
            SaturationSlider.Value = Math.Clamp(Adjustments.Saturation, SaturationSlider.Minimum, SaturationSlider.Maximum);
            BlackSlider.Value = Math.Clamp(Adjustments.BlackPoint, BlackSlider.Minimum, BlackSlider.Maximum);
            WhiteSlider.Value = Math.Clamp(Adjustments.WhitePoint, WhiteSlider.Minimum, WhiteSlider.Maximum);

            TemperatureSlider.Value = Math.Clamp(ToMired(_whiteBalance.Kelvin),
                                                 TemperatureSlider.Minimum, TemperatureSlider.Maximum);
            TintSlider.Value = _whiteBalance.Tint;
            VibranceSlider.Value = _vibrance.Amount;

            LiftRSlider.Value = _zones.Lift.R;
            LiftGSlider.Value = _zones.Lift.G;
            LiftBSlider.Value = _zones.Lift.B;
            GammaRSlider.Value = _zones.Gamma.R;
            GammaGSlider.Value = _zones.Gamma.G;
            GammaBSlider.Value = _zones.Gamma.B;
            GainRSlider.Value = _zones.Gain.R;
            GainGSlider.Value = _zones.Gain.G;
            GainBSlider.Value = _zones.Gain.B;

            _bands.Prepare();
            for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
                _bandSliders[i].Value = BandValue(_bands.Bands[i], _bandMode);

            LutStrengthSlider.Value = _lut.Strength;
            UpdateLutText();
        }
        finally
        {
            _filling = false;
        }

        UpdateValues();
    }

    /// <summary>
    /// IsLoaded ist Pflicht, nicht Vorsicht: Beim Einlesen der XAML setzt jeder
    /// Regler erst sein Minimum, und schon das loest eine Aenderung aus. Die weiter
    /// unten stehenden Regler gibt es dann noch nicht.
    /// </summary>
    private void OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || !IsLoaded) return;

        Adjustments = new ImageAdjustments
        {
            Exposure = ExposureSlider.Value,
            Gamma = GammaSlider.Value,
            Contrast = ContrastSlider.Value,
            Saturation = SaturationSlider.Value,
            BlackPoint = BlackSlider.Value,
            WhitePoint = WhiteSlider.Value,
            Channel = Adjustments.Channel,
        }.Clamped();

        // Der Schwarzpunkt kann den Weisspunkt nicht ueberholen - das faengt
        // Clamped() ab. Damit der Regler nicht weiterlaeuft, waehrend der Wert
        // stehenbleibt, wird er zurueckgesetzt: Sonst zieht man ins Leere und
        // merkt es erst, wenn man loslaesst.
        if (Math.Abs(BlackSlider.Value - Adjustments.BlackPoint) > 0.0005)
        {
            _filling = true;
            try { BlackSlider.Value = Adjustments.BlackPoint; }
            finally { _filling = false; }
        }

        _whiteBalance.Kelvin = ToKelvin(TemperatureSlider.Value);
        _whiteBalance.Tint = (float)TintSlider.Value;
        _vibrance.Amount = (float)VibranceSlider.Value;

        _zones.Lift.R = (float)LiftRSlider.Value;
        _zones.Lift.G = (float)LiftGSlider.Value;
        _zones.Lift.B = (float)LiftBSlider.Value;
        _zones.Gamma.R = (float)GammaRSlider.Value;
        _zones.Gamma.G = (float)GammaGSlider.Value;
        _zones.Gamma.B = (float)GammaBSlider.Value;
        _zones.Gain.R = (float)GainRSlider.Value;
        _zones.Gain.G = (float)GainGSlider.Value;
        _zones.Gain.B = (float)GainBSlider.Value;

        _bands.Prepare();
        for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
            SetBandValue(_bands.Bands[i], _bandMode, (float)_bandSliders[i].Value);

        _lut.Strength = (float)LutStrengthSlider.Value;

        UpdateValues();

        // Beim Ziehen eines Reglers reicht die grobe Vorschau; das Loslassen meldet
        // der Schieber selbst nicht, deshalb entscheidet der Aufrufer ueber einen
        // Zeitgeber, wann er wieder genau rechnet.
        Raise(interim: true);
    }

    /// <summary>Baut den Stapel neu und gibt ihn zurueck - nach jeder Aenderung faellig.</summary>
    public PreparedGrading Prepare()
    {
        Prepared = Stack.Prepare();
        return Prepared;
    }

    private void Raise(bool interim)
    {
        Prepare();
        Changed?.Invoke(interim);
    }

    private void UpdateValues()
    {
        ExposureValue.Text = Adjustments.Exposure == 0 ? "0" : $"{Adjustments.Exposure:+0.00;-0.00}";
        GammaValue.Text = $"{Adjustments.Gamma:0.00}";
        ContrastValue.Text = $"{Adjustments.Contrast:0.00}";
        SaturationValue.Text = $"{Adjustments.Saturation:0.00}";
        BlackValue.Text = $"{Adjustments.BlackPoint:0.00}";
        WhiteValue.Text = $"{Adjustments.WhitePoint:0.00}";

        TemperatureValue.Text = $"{ToKelvin(TemperatureSlider.Value):0} K";
        TintValue.Text = $"{TintSlider.Value:+0;-0;0}";
        VibranceValue.Text = $"{VibranceSlider.Value:+0.00;-0.00;0.00}";
        LutStrengthValue.Text = $"{LutStrengthSlider.Value:0.00}";
    }

    /// <summary>
    /// Mired nach Kelvin und zurueck.
    ///
    /// Der Regler laeuft in Mired, weil ein Kelvin-Regler ueber seine Laenge
    /// ungleich wirkt: Ein Schritt von 1000 K bewirkt bei 2000 K das Dreissigfache
    /// dessen, was er bei 14000 K bewirkt. In Mired sind die Schritte gleich gross,
    /// und das entspricht auch dem, was das Auge als gleichen Unterschied sieht.
    ///
    /// Angezeigt wird trotzdem Kelvin - danach fragt jeder, der eine Lichtquelle
    /// beschreiben will.
    /// </summary>
    private static float ToKelvin(double mired) => (float)(1e6 / Math.Max(1.0, mired));

    private static double ToMired(float kelvin) => 1e6 / Math.Max(1f, kelvin);

    private static float BandValue(HslBand band, int mode) => mode switch
    {
        1 => band.Saturation,
        2 => band.Luminance,
        _ => band.Hue,
    };

    private static void SetBandValue(HslBand band, int mode, float value)
    {
        switch (mode)
        {
            case 1: band.Saturation = value; break;
            case 2: band.Luminance = value; break;
            default: band.Hue = value; break;
        }
    }

    // ------------------------------------------------------------------ Bedienung

    private void OnCurveChannelPicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton picked || picked.Tag is not string tag) return;
        if (!int.TryParse(tag, out int channel)) return;

        foreach (var button in new[] { CurveMasterButton, CurveRedButton, CurveGreenButton, CurveBlueButton })
            if (!ReferenceEquals(button, picked)) button.IsChecked = false;

        CurveField.Curve = channel switch
        {
            1 => _curves.Red,
            2 => _curves.Green,
            3 => _curves.Blue,
            _ => _curves.Master,
        };

        CurveField.LineColour = CurveColours[Math.Clamp(channel, 0, 3)];
        CurveField.InvalidateVisual();
    }

    private void OnBandModePicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton picked || picked.Tag is not string tag) return;
        if (!int.TryParse(tag, out int mode)) return;

        foreach (var button in new[] { BandHueButton, BandSatButton, BandLumButton })
            if (!ReferenceEquals(button, picked)) button.IsChecked = false;

        _bandMode = mode;
        _filling = true;

        try
        {
            _bands.Prepare();
            for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
                _bandSliders[i].Value = BandValue(_bands.Bands[i], _bandMode);
        }
        finally
        {
            _filling = false;
        }
    }

    private void OnSectionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string name) return;
        if (FindName(name) is not FrameworkElement body) return;

        bool open = body.Visibility != Visibility.Visible;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        button.Content = open ? "−" : "+";
    }

    private void OnResetBasicClicked(object sender, RoutedEventArgs e)
    {
        Adjustments = ImageAdjustments.Neutral;
        PushToControls();
        Raise(interim: false);
    }

    private void OnResetCurveClicked(object sender, RoutedEventArgs e) => CurveField.Reset();

    private void OnHistogramModeClicked(object sender, RoutedEventArgs e)
    {
        Histogram.ShowChannels = !Histogram.ShowChannels;
        HistogramModeButton.Content = Histogram.ShowChannels ? "RGB" : "Luma";
        Histogram.InvalidateVisual();
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

    private void OnPickLutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_PickLut"),
            Filter = "Cube|*.cube|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        if (_lut.Path.Length > 0)
        {
            try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_lut.Path); }
            catch (Exception) { /* ein ungueltiger Pfad ist kein Grund, den Dialog nicht zu oeffnen */ }
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        _lut.Path = dialog.FileName;
        _lut.Prepare();
        UpdateLutText();
        Raise(interim: false);
    }

    private void UpdateLutText()
    {
        if (_lut.Path.Length == 0)
        {
            LutPathText.Text = Strings.T("S_NoLut");
            return;
        }

        LutPathText.Text = _lut.Error is null
            ? System.IO.Path.GetFileName(_lut.Path)
            : System.IO.Path.GetFileName(_lut.Path) + " — " + _lut.Error;
    }
}
