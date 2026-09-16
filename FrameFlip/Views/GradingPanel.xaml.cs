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
    private ClarityTool _clarity = new();

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

        // Die Werkzeuge gehoeren in den Stapel, bevor der erste Griff kommt.
        //
        // Vorher entstanden sie hier als Felder und wanderten erst in Load() in den
        // Stapel. Wer ein Panel benutzte, ohne vorher zu laden, bewegte damit
        // Werkzeuge, die nirgends standen: Die Regler liefen, die Werte aenderten
        // sich, und im Bild geschah nichts. Ein Fehler, der sich nicht als Fehler
        // meldet, sondern als "geht nicht".
        Load(null, null);

        BuildBandSliders();
        CurveField.Curve = _curves.Master;
        CurveField.LineColour = CurveColours[0];
        CurveField.Changed += () => Raise(interim: true);
        CurveField.Released += () => Raise(interim: false);

        foreach (var wheel in new[] { LiftWheel, GammaWheel, GainWheel })
        {
            wheel.Changed += OnWheelChanged;
            wheel.Released += () => Raise(interim: false);
        }

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
    /// Worauf die Regler gerade wirken. Null heisst: auf das fertige Bild.
    ///
    /// Der Streifen selbst weiss davon nichts weiter - er zeigt es nur an. Dass
    /// dieselben Regler einmal einer Ebene und einmal dem ganzen Bild gehoeren, ist
    /// die eine Sache an dieser Oberflaeche, die man uebersehen kann, und deshalb
    /// steht sie ueber allem und ausserhalb der Bildlaufflaeche.
    /// </summary>
    public string? Target
    {
        set
        {
            TargetText.Text = value is null
                ? Strings.T("S_ToolsTargetImage")
                : Strings.T("S_ToolsTargetLayer", value);

            TargetText.Foreground = (System.Windows.Media.Brush)FindResource(
                value is null ? "MutedBrush" : "AccentBrush");
        }
    }

    /// <summary>
    /// Uebernimmt gespeicherte Einstellungen. Die Werkzeuge werden dabei aus dem
    /// Stapel geholt oder angelegt, damit jedes genau einmal vorkommt.
    /// </summary>
    public void Load(ImageAdjustments? adjustments, GradingStack? stack)
    {
        Adjustments = adjustments?.Clamped() ?? ImageAdjustments.Neutral;

        // Erst abschreiben, dann leeren. Wer den eigenen Stapel hereinreicht - das
        // tut, wer nur die Regler neu fuellen will -, leerte sonst die Quelle, aus
        // der gleich gelesen wird, und stuende mit frisch angelegten, neutralen
        // Werkzeugen da.
        var tools = stack?.Tools.ToList();
        var local = stack?.Local.ToList();

        Stack.Tools.Clear();
        if (tools is not null) Stack.Tools.AddRange(tools);

        // Die oertliche Liste genauso. Sie zu vergessen hiess: Klarheit bliebe beim
        // Umschalten zwischen Ebenen einfach stehen, weil der Bereich sein eigenes
        // Werkzeug behielte - eingestellt an der einen Ebene, wirksam an allen.
        Stack.Local.Clear();
        if (local is not null) Stack.Local.AddRange(local);

        _curves = Take<CurvesTool>();
        _whiteBalance = Take<WhiteBalanceTool>();
        _zones = Take<LiftGammaGainTool>();
        _bands = Take<HslTool>();
        _vibrance = Take<VibranceTool>();
        _lut = Take<LutTool>();

        // Die oertlichen Werkzeuge stehen in ihrer eigenen Liste - sie nehmen einen
        // anderen Weg durch den Bildprozessor.
        _clarity = Stack.Local.OfType<ClarityTool>().FirstOrDefault() ?? Add();

        ClarityTool Add()
        {
            var created = new ClarityTool();
            Stack.Local.Add(created);

            return created;
        }

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
            ClarityBody.IsEnabled = value;
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

        // Die Mitten der acht Bereiche in Grad - dieselben, nach denen das Werkzeug
        // gewichtet. Die Bahn eines Reglers zeigt damit den Bereich, den er meint,
        // und die Beschriftung daneben muss man nicht mehr lesen.
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
                Style = (Style)FindResource("PanelSliderTinted"),
                Background = BandTrack(HslTool.Centres[i], _bandMode),
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

    /// <summary>
    /// Die Bahn eines Bereichsreglers - gerichtet, und je nach Groesse verschieden.
    ///
    /// Ein Verlauf, der zu beiden Seiten dasselbe tut, sagt nur "hier ist Blau". Die
    /// Bahn soll aber zeigen, was der Regler BEWIRKT, und das ist in jeder der drei
    /// Groessen etwas anderes:
    ///
    ///   Ton        von der einen Nachbarfarbe zur anderen - der Regler verschiebt
    ///              den Farbton um bis zu dreissig Grad in beide Richtungen
    ///   Saettigung von grau zur vollen Farbe
    ///   Helligkeit von dunkel nach hell
    ///
    /// So steht links wirklich, was links passiert.
    /// </summary>
    private static System.Windows.Media.Brush BandTrack(float hueDegrees, int mode)
    {
        Color from, to;

        switch (mode)
        {
            case 1:
                // Saettigung: links entsaettigt, rechts voll.
                from = Hsl(hueDegrees, 0.05f, 0.5f);
                to = Hsl(hueDegrees, 0.85f, 0.55f);
                break;

            case 2:
                // Helligkeit: links dunkel, rechts hell - in der Farbe des Bereichs,
                // damit man sieht, welcher gemeint ist.
                from = Hsl(hueDegrees, 0.5f, 0.16f);
                to = Hsl(hueDegrees, 0.5f, 0.82f);
                break;

            default:
                // Ton: der Regler zieht um bis zu dreissig Grad. Die Bahn zeigt
                // genau diesen Weg - links die Farbe, bei der man landet, wenn man
                // ganz nach links zieht, rechts die andere.
                from = Hsl(hueDegrees - 30f, 0.7f, 0.55f);
                to = Hsl(hueDegrees + 30f, 0.7f, 0.55f);
                break;
        }

        var brush = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 0),
        };

        brush.GradientStops.Add(new System.Windows.Media.GradientStop(from, 0));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(to, 1));
        brush.Freeze();

        return brush;

        static Color Hsl(float hue, float saturation, float lightness)
        {
            while (hue < 0f) hue += 360f;
            while (hue >= 360f) hue -= 360f;

            HslTool.HslToRgb(hue, saturation, lightness, out float r, out float g, out float b);
            return Color.FromRgb(Byte(r), Byte(g), Byte(b));
        }

        static byte Byte(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);
    }

    /// <summary>
    /// Die Bahnen nachziehen. Noetig beim Wechsel der Groesse, weil derselbe Regler
    /// dann etwas anderes bewirkt und seine Bahn das zeigen soll.
    /// </summary>
    private void UpdateBandTracks()
    {
        for (int i = 0; i < _bandSliders.Count && i < HslTool.Centres.Length; i++)
            _bandSliders[i].Background = BandTrack(HslTool.Centres[i], _bandMode);
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

            PushZone(LiftWheel, LiftBrightSlider, _zones.Lift, neutral: 0f);
            PushZone(GammaWheel, GammaBrightSlider, _zones.Gamma, neutral: 1f);
            PushZone(GainWheel, GainBrightSlider, _zones.Gain, neutral: 1f);

            // Beim ersten Aufruf aus dem Konstruktor gibt es die Bereichsregler noch
            // nicht; die Schleife laeuft dann ueber nichts und wird nachgeholt,
            // sobald sie stehen.
            _bands.Prepare();
            for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
                _bandSliders[i].Value = BandValue(_bands.Bands[i], _bandMode);

            LutStrengthSlider.Value = _lut.Strength;
            UpdateLutText();

            ClaritySlider.Value = Math.Clamp(_clarity.Amount, ClaritySlider.Minimum, ClaritySlider.Maximum);
            ClarityReachSlider.Value = Math.Clamp(_clarity.Reach,
                                                  ClarityReachSlider.Minimum, ClarityReachSlider.Maximum);
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

        PullZone(LiftWheel, LiftBrightSlider, _zones.Lift, neutral: 0f, scale: LiftScale);
        PullZone(GammaWheel, GammaBrightSlider, _zones.Gamma, neutral: 1f, scale: GammaScale);
        PullZone(GainWheel, GainBrightSlider, _zones.Gain, neutral: 1f, scale: GainScale);

        _bands.Prepare();
        for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
            SetBandValue(_bands.Bands[i], _bandMode, (float)_bandSliders[i].Value);

        _lut.Strength = (float)LutStrengthSlider.Value;

        _clarity.Amount = (float)ClaritySlider.Value;
        _clarity.Reach = (int)Math.Round(ClarityReachSlider.Value);

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
        ClarityValue.Text = $"{ClaritySlider.Value:+0.00;-0.00;0.00}";
        ClarityReachValue.Text = $"{ClarityReachSlider.Value:0}";
        UpdateZoneValues();
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

    /// <summary>
    /// Wie weit der Rand eines Rades traegt. Je Zone verschieden, weil ein Lift von
    /// 0,2 viel und ein Gain von 0,2 wenig waere - Lift verschiebt, Gain skaliert.
    /// </summary>
    private const float LiftScale = 0.12f;
    private const float GammaScale = 0.35f;
    private const float GainScale = 0.25f;

    private void OnWheelChanged()
    {
        if (_filling) return;

        PullZone(LiftWheel, LiftBrightSlider, _zones.Lift, 0f, LiftScale);
        PullZone(GammaWheel, GammaBrightSlider, _zones.Gamma, 1f, GammaScale);
        PullZone(GainWheel, GainBrightSlider, _zones.Gain, 1f, GainScale);

        UpdateZoneValues();
        Raise(interim: true);
    }

    private static void PullZone(ColourWheel wheel, Slider brightness, ColourTriplet target,
                                 float neutral, float scale)
    {
        var (r, g, b) = ColourWheelMath.ToChannels(wheel.Value, (float)brightness.Value, neutral, scale);
        target.R = r;
        target.G = g;
        target.B = b;
    }

    private static void PushZone(ColourWheel wheel, Slider brightness, ColourTriplet source, float neutral)
    {
        wheel.Value = ColourWheelMath.ToPoint(source.R, source.G, source.B);
        brightness.Value = Math.Clamp(ColourWheelMath.ToBrightness(source.R, source.G, source.B, neutral),
                                      brightness.Minimum, brightness.Maximum);
    }

    /// <summary>
    /// Die Zahlen unter den Raedern. Ein Rad zeigt die Richtung, aber nicht, wie
    /// weit - und wer eine Einstellung wiederherstellen oder beschreiben will,
    /// braucht die Werte.
    /// </summary>
    private void UpdateZoneValues()
    {
        if (LiftValues is null) return;

        LiftValues.Text = Triplet(_zones.Lift);
        GammaValues.Text = Triplet(_zones.Gamma);
        GainValues.Text = Triplet(_zones.Gain);

        static string Triplet(ColourTriplet t) => $"{t.R:0.00}  {t.G:0.00}  {t.B:0.00}";
    }

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
        UpdateBandTracks();
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
