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

    /// <summary>Ob die Regler dem fertigen Bild gelten und nicht einer Ebene.</summary>
    private bool _localOnImage = true;

    private CurvesTool _curves = new();
    private WhiteBalanceTool _whiteBalance = new();
    private LiftGammaGainTool _zones = new();
    private HslTool _bands = new();
    private VibranceTool _vibrance = new();
    private LutTool _lut = new();
    private ClarityTool _clarity = new();
    private SharpenTool _sharpen = new();
    private NoiseTool _noise = new();
    private BloomTool _bloom = new();
    private HalationTool _halation = new();
    private DehazeTool _dehaze = new();
    private TextureTool _texture = new();
    private VignetteTool _vignette = new();
    private GrainTool _grain = new();

    /// <summary>
    /// Der erste Eintrag der Rasterliste, der eine Fehlerdiffusion ist.
    ///
    /// Davor stehen die Ortsmuster. Die Zahl steht an einer Stelle, weil sie an vier
    /// gebraucht wird und ein vergessenes Vorkommen ein Raster ergaebe, das anders
    /// heisst, als es rechnet.
    /// </summary>
    private const int FirstKernel = 3;

    private DitherTool _dither = new();
    private DiffusionTool _diffusion = new();
    private DistortionTool _distortion = new();
    private ChromaticTool _chromatic = new();
    private DepthFieldTool _depth = new();
    private MotionBlurTool _motion = new();
    private DisplaceTool _displace = new();

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

        Loaded += (_, _) =>
        {
            BuildTiles();
            PushToControls();
        };
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
    /// <summary>
    /// Stellt den Schalter ein und sagt, worauf gerechnet wird.
    ///
    /// <paramref name="layer"/> ist null, wenn gar keine Ebene gewaehlt ist - dann
    /// gibt es nichts zu waehlen. <paramref name="locked"/> gilt fuer eine
    /// Einstellungsebene: Sie BESTEHT aus ihrer Korrektur, und "wirkt auf das ganze
    /// Bild" waere bei ihr keine zweite Moeglichkeit, sondern ein Widerspruch.
    /// </summary>
    public void ShowTarget(string? layer, bool onLayer, bool locked)
    {
        _filling = true;

        try
        {
            TargetLayerButton.Content = layer is null
                ? Strings.T("S_ToolsTargetNoLayer")
                : Strings.T("S_ToolsTargetLayer", layer);

            TargetLayerButton.IsEnabled = layer is not null;
            TargetImageButton.IsEnabled = !locked;

            TargetLayerButton.IsChecked = onLayer;
            TargetImageButton.IsChecked = !onLayer;
        }
        finally
        {
            _filling = false;
        }

        Target = onLayer ? layer : null;
    }

    /// <summary>
    /// Welche Renderdaten die Datei fuehrt - damit ein Werkzeug sagen kann, warum es
    /// nichts tut.
    ///
    /// Das ist der Unterschied zwischen "ruht" und "kaputt". Ein Werkzeug mit
    /// Renderdaten ruht, wenn sein Pass fehlt, und das ist richtig so - eine Datei
    /// ohne Tiefe ist kein Fehler, sondern eine Datei ohne Tiefe. Nur merkt das
    /// niemand: Man zieht am Regler, es passiert nichts, und dann sucht man den
    /// Fehler im Programm statt in der Datei.
    /// </summary>
    public void ShowPasses(bool depth, bool motion, bool normal)
    {
        _hasDepth = depth;
        _hasMotion = motion;
        _hasNormal = normal;

        ShowMissingPasses();
    }

    private bool _hasDepth = true, _hasMotion = true, _hasNormal = true;

    private void ShowMissingPasses()
    {
        MotionMissing.Visibility = _hasMotion ? Visibility.Collapsed : Visibility.Visible;
        DepthMissing.Visibility = _hasDepth ? Visibility.Collapsed : Visibility.Visible;

        // Bei der Verschiebung haengt es daran, welcher Pass gerade gewaehlt ist.
        bool wantsMotion = DisplaceFromBox.SelectedIndex == 1;
        bool there = wantsMotion ? _hasMotion : _hasNormal;

        DisplaceMissing.Text = Strings.T(wantsMotion
            ? "S_PassMissingMotion"
            : "S_PassMissingNormal");

        DisplaceMissing.Visibility = there ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Gewaehlt wurde ein anderes Ziel - wahr heisst "die Ebene".</summary>
    public event Action<bool>? TargetChanged;

    private void OnTargetChanged(object sender, RoutedEventArgs e)
    {
        if (_filling || !IsLoaded) return;

        TargetChanged?.Invoke(ReferenceEquals(sender, TargetLayerButton));
    }

    public string? Target
    {
        set
        {
            // Zwei Gruppen gelten dem fertigen Bild. Die oertlichen Werkzeuge, weil
            // eine Einstellungsebene mitten im Stapel punktweise gerechnet wird und es
            // dort noch gar keine Nachbarschaft gibt. Vignette und Korn, weil sie zur
            // Kamera gehoeren und nicht zu einer Ebene in ihr - Korn auf einer Ebene,
            // ueber die dann noch etwas gelegt wird, waere kein Korn mehr.
            //
            // Sie an einer Ebene bedienbar zu lassen waere schlimmer als sie
            // abzuschalten: Der Regler bewegte sich, und das Bild nicht.
            bool onImage = value is null;

            LocalOnFinalNote.Visibility = onImage ? Visibility.Collapsed : Visibility.Visible;

            _localOnImage = onImage;
            ApplyPictureToolsEnabled();
        }
    }

    /// <summary>
    /// Ob die Abschnitte bedienbar sind, die dem ganzen Bild gelten. Zwei Gruende
    /// koennen sie abschalten - der Gesamtschalter und eine ausgewaehlte Ebene -, und
    /// beide muessen gelten, nicht der zuletzt gesetzte.
    /// </summary>
    private void ApplyPictureToolsEnabled()
    {
        bool enabled = ToolsEnabled && _localOnImage;

        DehazeBody.IsEnabled = enabled;
        BloomBody.IsEnabled = enabled;
        HalationBody.IsEnabled = enabled;
        NoiseBody.IsEnabled = enabled;
        ClarityBody.IsEnabled = enabled;
        TextureBody.IsEnabled = enabled;
        SharpenBody.IsEnabled = enabled;
        MotionBody.IsEnabled = enabled;
        DisplaceBody.IsEnabled = enabled;
        DepthBody.IsEnabled = enabled;
        DistortionBody.IsEnabled = enabled;
        ChromaticBody.IsEnabled = enabled;
        VignetteBody.IsEnabled = enabled;
        GrainBody.IsEnabled = enabled;

        // Rastern gehoert hierher wie das Korn: Es ist eine Eigenschaft des fertigen
        // BILDES und keine einer Ebene. Die Fehlerdiffusion laeuft ohnehin ueber den
        // ganzen Rahmen, und ein Raster auf einer Ebene, ueber die danach noch etwas
        // gelegt wird, waere keines mehr.
        //
        // Es hier zu vergessen war teuer: Der Abschnitt blieb bedienbar, waehrend
        // eine Ebene gewaehlt war, schrieb in deren Stapel - und dort wird er nie
        // gerechnet. Im Fenster sah das aus wie "hat keinen Effekt", und zwar bei
        // jedem Verfahren gleichzeitig.
        DitherBody.IsEnabled = enabled;
    }

    /// <summary>
    /// Setzt die Scharfstellung der Tiefenschaerfe - der Weg der Pipette.
    ///
    /// False heisst: Es gibt keine Tiefenschaerfe im Stapel. Das ist keine Stoerung,
    /// sondern eine Auskunft, und der Aufrufer sagt sie weiter, statt still nichts zu
    /// tun.
    /// </summary>
    public bool SetFocus(float distance)
    {
        var tool = Stack.Data.OfType<DepthFieldTool>().FirstOrDefault();

        if (tool is null) return false;

        tool.Focus = distance;

        PushToControls();
        Changed?.Invoke(false);

        return true;
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
        var optics = stack?.Optics.ToList();
        var geometry = stack?.Geometry.ToList();
        var data = stack?.Data.ToList();
        var frame = stack?.Frame.ToList();

        Stack.Tools.Clear();
        if (tools is not null) Stack.Tools.AddRange(tools);

        // Die oertliche Liste genauso. Sie zu vergessen hiess: Klarheit bliebe beim
        // Umschalten zwischen Ebenen einfach stehen, weil der Bereich sein eigenes
        // Werkzeug behielte - eingestellt an der einen Ebene, wirksam an allen.
        Stack.Local.Clear();
        if (local is not null) Stack.Local.AddRange(local);

        Stack.Optics.Clear();
        if (optics is not null) Stack.Optics.AddRange(optics);

        Stack.Geometry.Clear();
        if (geometry is not null) Stack.Geometry.AddRange(geometry);

        Stack.Data.Clear();
        if (data is not null) Stack.Data.AddRange(data);

        // Und die Durchgaenge ueber den Rahmen. Sie hier zu vergessen war derselbe
        // Fehler wie einst bei der oertlichen Liste, nur schlechter zu finden: Der
        // Bereich behielt sein eigenes Werkzeug, es wurde nie aus dem gespeicherten
        // Stapel gefuellt und beim naechsten Start war die Einstellung weg.
        Stack.Frame.Clear();
        if (frame is not null) Stack.Frame.AddRange(frame);

        _curves = Take<CurvesTool>();
        _whiteBalance = Take<WhiteBalanceTool>();
        _zones = Take<LiftGammaGainTool>();
        _bands = Take<HslTool>();
        _vibrance = Take<VibranceTool>();
        _lut = Take<LutTool>();

        // Die oertlichen Werkzeuge stehen in ihrer eigenen Liste - sie nehmen einen
        // anderen Weg durch den Bildprozessor. In welcher Reihenfolge sie dort
        // liegen, ist gleichgueltig: Der Stapel sortiert sie nach ihrer Stufe.
        _dehaze = TakeLocal<DehazeTool>();
        _bloom = TakeLocal<BloomTool>();
        _halation = TakeLocal<HalationTool>();
        _noise = TakeLocal<NoiseTool>();
        _clarity = TakeLocal<ClarityTool>();
        _texture = TakeLocal<TextureTool>();
        _sharpen = TakeLocal<SharpenTool>();

        // Die Ortswerkzeuge haben ihre eigene Liste - dieselbe Ueberlegung, ein
        // anderer Weg durch den Bildprozessor.
        _vignette = TakeOptics<VignetteTool>();
        _grain = TakeOptics<GrainTool>();
        _dither = TakeOptics<DitherTool>();
        _diffusion = TakeFrame<DiffusionTool>();

        // Und die vierte Liste: die Werkzeuge, die Bildpunkte verschieben.
        _distortion = TakeGeometry<DistortionTool>();
        _chromatic = TakeGeometry<ChromaticTool>();

        // Und die fuenfte: die Werkzeuge, die Renderdaten brauchen.
        _motion = TakeData<MotionBlurTool>();
        _displace = TakeData<DisplaceTool>();
        _depth = TakeData<DepthFieldTool>();

        T TakeLocal<T>() where T : ILocalTool, new()
        {
            var found = Stack.Local.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Local.Add(created);

            return created;
        }

        T TakeFrame<T>() where T : IFramePass, new()
        {
            var found = Stack.Frame.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Frame.Add(created);

            return created;
        }

        T TakeOptics<T>() where T : IOpticsTool, new()
        {
            var found = Stack.Optics.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Optics.Add(created);

            return created;
        }

        T TakeGeometry<T>() where T : IGeometryTool, new()
        {
            var found = Stack.Geometry.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Geometry.Add(created);

            return created;
        }

        T TakeData<T>() where T : IDataTool, new()
        {
            var found = Stack.Data.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            Stack.Data.Add(created);

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
            ApplyPictureToolsEnabled();
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

            SharpenSlider.Value = Math.Clamp(_sharpen.Amount, SharpenSlider.Minimum, SharpenSlider.Maximum);
            SharpenRadiusSlider.Value = Math.Clamp(_sharpen.Reach,
                                                   SharpenRadiusSlider.Minimum, SharpenRadiusSlider.Maximum);
            SharpenThresholdSlider.Value = Math.Clamp(_sharpen.Threshold,
                                                      SharpenThresholdSlider.Minimum, SharpenThresholdSlider.Maximum);

            MotionShutterSlider.Value = Math.Clamp(_motion.Shutter,
                                                   MotionShutterSlider.Minimum, MotionShutterSlider.Maximum);
            DisplaceAmountSlider.Value = Math.Clamp(_displace.Amount,
                DisplaceAmountSlider.Minimum, DisplaceAmountSlider.Maximum);
            DisplaceWaveSlider.Value = Math.Clamp(_displace.Wave,
                DisplaceWaveSlider.Minimum, DisplaceWaveSlider.Maximum);
            DisplaceLengthSlider.Value = Math.Clamp(_displace.Wavelength,
                DisplaceLengthSlider.Minimum, DisplaceLengthSlider.Maximum);
            DisplaceAngleSlider.Value = Math.Clamp(_displace.Angle,
                DisplaceAngleSlider.Minimum, DisplaceAngleSlider.Maximum);
            DisplaceSpreadSlider.Value = Math.Clamp(_displace.Spread,
                DisplaceSpreadSlider.Minimum, DisplaceSpreadSlider.Maximum);

            DisplaceFromBox.SelectedIndex = _displace.From == DisplaceFrom.Motion ? 1 : 0;

            MotionSamplesSlider.Value = Math.Clamp(_motion.Samples,
                                                   MotionSamplesSlider.Minimum, MotionSamplesSlider.Maximum);

            DepthApertureSlider.Value = Math.Clamp(_depth.Aperture,
                                                   DepthApertureSlider.Minimum, DepthApertureSlider.Maximum);
            DepthFocusSlider.Value = Math.Clamp(FocusAt(_depth.Focus),
                                                DepthFocusSlider.Minimum, DepthFocusSlider.Maximum);

            DistortionSlider.Value = Math.Clamp(_distortion.Amount,
                                                DistortionSlider.Minimum, DistortionSlider.Maximum);
            DistortionScaleSlider.Value = Math.Clamp(_distortion.Scale,
                                                     DistortionScaleSlider.Minimum, DistortionScaleSlider.Maximum);

            ChromaticSlider.Value = Math.Clamp(_chromatic.Amount,
                                               ChromaticSlider.Minimum, ChromaticSlider.Maximum);

            VignetteSlider.Value = Math.Clamp(_vignette.Amount,
                                              VignetteSlider.Minimum, VignetteSlider.Maximum);
            VignetteMidpointSlider.Value = Math.Clamp(_vignette.Midpoint,
                                                      VignetteMidpointSlider.Minimum, VignetteMidpointSlider.Maximum);
            VignetteRoundnessSlider.Value = Math.Clamp(_vignette.Roundness,
                                                       VignetteRoundnessSlider.Minimum, VignetteRoundnessSlider.Maximum);
            VignetteFeatherSlider.Value = Math.Clamp(_vignette.Feather,
                                                     VignetteFeatherSlider.Minimum, VignetteFeatherSlider.Maximum);

            // Welches Muster gilt, sagt kein zusaetzlicher Schalter, sondern welches
            // der beiden Werkzeuge etwas zu tun hat. Ein Zustand daneben waere eine
            // dritte Wahrheit, die mit den beiden anderen auseinanderlaufen koennte.
            bool diffusing = _diffusion.Amount > 0.005f && !_diffusion.Place;

            DitherPixelsSlider.Value = Math.Clamp(_diffusion.Pixels,
                                                  DitherPixelsSlider.Minimum,
                                                  DitherPixelsSlider.Maximum);

            DitherDuotoneButton.IsChecked = _diffusion.Duotone;

            DitherHueSlider.Value = Math.Clamp(_diffusion.Hue,
                                               DitherHueSlider.Minimum, DitherHueSlider.Maximum);
            DitherSaturationSlider.Value = Math.Clamp(_diffusion.Saturation,
                                                      DitherSaturationSlider.Minimum,
                                                      DitherSaturationSlider.Maximum);

            DitherPixelsTallSlider.Value = Math.Clamp(
                _diffusion.PixelsTall <= 0 ? _diffusion.Pixels : _diffusion.PixelsTall,
                DitherPixelsTallSlider.Minimum, DitherPixelsTallSlider.Maximum);

            // Die ersten drei Eintraege sind die Ortsmuster, danach folgen die
            // Streuschemata in der Reihenfolge ihrer Aufzaehlung.
            DitherPatternBox.SelectedIndex = diffusing
                ? FirstKernel + (int)_diffusion.Kernel
                : _dither.Pattern switch
                {
                    DitherPattern.Noise => 1,
                    DitherPattern.Lines => 2,
                    _ => 0,
                };

            DitherAngleSlider.Value = Math.Clamp(_dither.Angle,
                                                 DitherAngleSlider.Minimum, DitherAngleSlider.Maximum);

            DitherSlider.Value = Math.Clamp(diffusing ? _diffusion.Amount : _dither.Amount,
                                            DitherSlider.Minimum, DitherSlider.Maximum);
            DitherLevelsSlider.Value = Math.Clamp(diffusing ? _diffusion.Levels : _dither.Levels,
                                                  DitherLevelsSlider.Minimum, DitherLevelsSlider.Maximum);
            DitherSizeSlider.Value = Math.Clamp(_dither.Size,
                                                DitherSizeSlider.Minimum, DitherSizeSlider.Maximum);

            GrainSlider.Value = Math.Clamp(_grain.Amount, GrainSlider.Minimum, GrainSlider.Maximum);
            GrainSizeSlider.Value = Math.Clamp(_grain.Size,
                                               GrainSizeSlider.Minimum, GrainSizeSlider.Maximum);
            GrainRoughnessSlider.Value = Math.Clamp(_grain.Roughness,
                                                    GrainRoughnessSlider.Minimum, GrainRoughnessSlider.Maximum);
            GrainColourSlider.Value = Math.Clamp(_grain.Colour,
                                                 GrainColourSlider.Minimum, GrainColourSlider.Maximum);

            DehazeSlider.Value = Math.Clamp(_dehaze.Amount, DehazeSlider.Minimum, DehazeSlider.Maximum);
            DehazeRadiusSlider.Value = Math.Clamp(_dehaze.Reach,
                                                  DehazeRadiusSlider.Minimum, DehazeRadiusSlider.Maximum);

            TextureSlider.Value = Math.Clamp(_texture.Amount,
                                             TextureSlider.Minimum, TextureSlider.Maximum);
            TextureRadiusSlider.Value = Math.Clamp(_texture.Reach,
                                                   TextureRadiusSlider.Minimum, TextureRadiusSlider.Maximum);

            BloomSlider.Value = Math.Clamp(_bloom.Amount, BloomSlider.Minimum, BloomSlider.Maximum);
            BloomThresholdSlider.Value = Math.Clamp(_bloom.Threshold,
                                                    BloomThresholdSlider.Minimum, BloomThresholdSlider.Maximum);
            BloomRadiusSlider.Value = Math.Clamp(_bloom.Reach,
                                                 BloomRadiusSlider.Minimum, BloomRadiusSlider.Maximum);

            HalationSlider.Value = Math.Clamp(_halation.Amount,
                                              HalationSlider.Minimum, HalationSlider.Maximum);
            HalationThresholdSlider.Value = Math.Clamp(_halation.Threshold,
                                                       HalationThresholdSlider.Minimum, HalationThresholdSlider.Maximum);
            HalationRadiusSlider.Value = Math.Clamp(_halation.Reach,
                                                    HalationRadiusSlider.Minimum, HalationRadiusSlider.Maximum);
            HalationTintSlider.Value = Math.Clamp(_halation.Tint,
                                                  HalationTintSlider.Minimum, HalationTintSlider.Maximum);

            NoiseLumaSlider.Value = Math.Clamp(_noise.Luminance,
                                               NoiseLumaSlider.Minimum, NoiseLumaSlider.Maximum);
            NoiseColourSlider.Value = Math.Clamp(_noise.Colour,
                                                 NoiseColourSlider.Minimum, NoiseColourSlider.Maximum);
            NoiseThresholdSlider.Value = Math.Clamp(_noise.Threshold,
                                                    NoiseThresholdSlider.Minimum, NoiseThresholdSlider.Maximum);
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
    /// <summary>
    /// Das Rastermuster hat gewechselt.
    ///
    /// Ein eigener Weg, weil ein Schalter keinen Zahlenwert meldet - sonst waere es
    /// derselbe: einsammeln, was in den Bedienelementen steht, und weitergeben.
    /// </summary>
    /// <summary>
    /// Was ein zugeklappter Abschnitt vorher war.
    ///
    /// Eine Gruppe zuzuklappen versteckt ihre Abschnitte; sie wieder aufzuklappen
    /// darf sie nicht alle OEFFNEN. Wer eine Gruppe zumacht und wieder auf, will den
    /// Stand von vorher und nicht einen Streifen aus zwanzig offenen Abschnitten.
    /// </summary>
    private readonly Dictionary<string, bool> _wasOpen = new(StringComparer.Ordinal);

    /// <summary>
    /// Eine Gruppe auf- oder zuklappen.
    ///
    /// Einundzwanzig Abschnitte in einer Liste sind keine Liste mehr, sondern ein
    /// Schacht: Wer die Vignette sucht, rollt an siebzehn Dingen vorbei, die er nicht
    /// gesucht hat. Fuenf Gruppen bilden den Rechenweg ab - Grundkorrektur, Licht und
    /// Zeichnung, Optik, Film, Tabelle -, und das ist dieselbe Reihenfolge, in der
    /// auch gerechnet wird.
    ///
    /// Die Gruppe kennt ihre Abschnitte ueber eine Namensliste und nicht ueber die
    /// Schachtelung. Das haelt die XAML flach: Ein Abschnitt, der in eine Gruppe
    /// hineinwandert, aendert nur einen Namen in einer Zeile, statt drei Ebenen
    /// Klammern zu verschieben.
    /// </summary>
    private void OnGroupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string members) return;

        bool open = button.IsChecked == true;

        foreach (string name in members.Split(','))
        {
            if (FindName(name + "Head") is FrameworkElement head)
                head.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

            if (FindName(name + "Body") is not FrameworkElement body) continue;

            if (open)
            {
                body.Visibility = _wasOpen.TryGetValue(name, out bool was) && was
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                _wasOpen[name] = body.Visibility == Visibility.Visible;
                body.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnDitherPattern(object sender, RoutedEventArgs e)
    {
        if (_filling || !IsLoaded) return;

        OnChanged(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));
    }

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

        _sharpen.Amount = (float)SharpenSlider.Value;
        _sharpen.Reach = (int)Math.Round(SharpenRadiusSlider.Value);
        _sharpen.Threshold = (float)SharpenThresholdSlider.Value;

        _motion.Shutter = (float)MotionShutterSlider.Value;
        _motion.Samples = (int)Math.Round(MotionSamplesSlider.Value);

        _displace.Amount = (float)DisplaceAmountSlider.Value;
        _displace.Wave = (float)DisplaceWaveSlider.Value;
        _displace.Wavelength = (float)DisplaceLengthSlider.Value;
        _displace.Angle = (float)DisplaceAngleSlider.Value;
        _displace.Spread = (float)DisplaceSpreadSlider.Value;

        _depth.Aperture = (float)DepthApertureSlider.Value;
        _depth.Focus = FocusFrom(DepthFocusSlider.Value);

        _distortion.Amount = (float)DistortionSlider.Value;
        _distortion.Scale = (float)DistortionScaleSlider.Value;

        _chromatic.Amount = (float)ChromaticSlider.Value;

        _vignette.Amount = (float)VignetteSlider.Value;
        _vignette.Midpoint = (float)VignetteMidpointSlider.Value;
        _vignette.Roundness = (float)VignetteRoundnessSlider.Value;
        _vignette.Feather = (float)VignetteFeatherSlider.Value;

        // Genau eines der beiden rechnet. Beide zugleich waere zweimal rastern, und
        // das zweite fande nichts mehr vor, was sich aufteilen liesse.
        int pick = Math.Max(0, DitherPatternBox.SelectedIndex);
        bool diffuse = pick >= FirstKernel;

        int levels = (int)Math.Round(DitherLevelsSlider.Value);
        float strength = (float)DitherSlider.Value;
        int pixels = (int)Math.Round(DitherPixelsSlider.Value);
        int tall = (int)Math.Round(DitherPixelsTallSlider.Value);

        // Sobald ein Rasterpunkt groesser als ein Bildpunkt ist, muss auch ein
        // Ortsmuster ueber den ganzen Rahmen laufen: Ein Block bekommt EINEN Wert,
        // und dafuer muss jemand den Block mitteln koennen. Ein Werkzeug, das einen
        // Bildpunkt sieht, kann das nicht.
        bool wholeFrame = diffuse || pixels > 1 || tall > 1;

        _dither.Amount = wholeFrame ? 0f : strength;
        _dither.Levels = levels;
        _dither.Size = (int)Math.Round(DitherSizeSlider.Value);
        _dither.Pattern = pick switch
        {
            1 => DitherPattern.Noise,
            2 => DitherPattern.Lines,
            _ => DitherPattern.Ordered,
        };

        _dither.Angle = (float)DitherAngleSlider.Value;

        _diffusion.Amount = wholeFrame ? strength : 0f;
        _diffusion.Levels = levels;
        _diffusion.Pixels = pixels;
        _diffusion.PixelsTall = tall;
        _diffusion.Duotone = DitherDuotoneButton.IsChecked == true;
        _diffusion.Hue = (float)DitherHueSlider.Value;
        _diffusion.Saturation = (float)DitherSaturationSlider.Value;
        _diffusion.Place = !diffuse;
        _diffusion.Pattern = _dither.Pattern;
        _diffusion.Angle = _dither.Angle;
        _diffusion.Kernel = (DiffusionKernel)Math.Max(0, pick - FirstKernel);

        _grain.Amount = (float)GrainSlider.Value;
        _grain.Size = (int)Math.Round(GrainSizeSlider.Value);
        _grain.Roughness = (float)GrainRoughnessSlider.Value;
        _grain.Colour = (float)GrainColourSlider.Value;

        _dehaze.Amount = (float)DehazeSlider.Value;
        _dehaze.Reach = (int)Math.Round(DehazeRadiusSlider.Value);

        _texture.Amount = (float)TextureSlider.Value;
        _texture.Reach = (int)Math.Round(TextureRadiusSlider.Value);

        _bloom.Amount = (float)BloomSlider.Value;
        _bloom.Threshold = (float)BloomThresholdSlider.Value;
        _bloom.Reach = (int)Math.Round(BloomRadiusSlider.Value);

        _halation.Amount = (float)HalationSlider.Value;
        _halation.Threshold = (float)HalationThresholdSlider.Value;
        _halation.Reach = (int)Math.Round(HalationRadiusSlider.Value);
        _halation.Tint = (float)HalationTintSlider.Value;

        _noise.Luminance = (float)NoiseLumaSlider.Value;
        _noise.Colour = (float)NoiseColourSlider.Value;
        _noise.Threshold = (float)NoiseThresholdSlider.Value;

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
        SharpenValue.Text = $"{SharpenSlider.Value:0.00}";
        SharpenRadiusValue.Text = $"{SharpenRadiusSlider.Value:0}";
        SharpenThresholdValue.Text = $"{SharpenThresholdSlider.Value:0.000}";
        MotionShutterValue.Text = $"{MotionShutterSlider.Value:0.00}";
        DisplaceAmountValue.Text = $"{DisplaceAmountSlider.Value:0} px";
        DisplaceWaveValue.Text = $"{DisplaceWaveSlider.Value:0.00}";
        DisplaceLengthValue.Text = $"{DisplaceLengthSlider.Value:0} px";
        DisplaceAngleValue.Text = $"{DisplaceAngleSlider.Value:0} °";
        DisplaceSpreadValue.Text = $"{DisplaceSpreadSlider.Value:0.00}";
        MotionSamplesValue.Text = $"{MotionSamplesSlider.Value:0}";
        DepthApertureValue.Text = $"{DepthApertureSlider.Value:0.00}";
        DepthFocusValue.Text = FocusFrom(DepthFocusSlider.Value) is var metres && metres < 10
            ? $"{metres:0.00}"
            : $"{metres:0.0}";
        DistortionValue.Text = $"{DistortionSlider.Value:+0.00;-0.00;0.00}";
        DistortionScaleValue.Text = $"{DistortionScaleSlider.Value:0.00}";
        ChromaticValue.Text = $"{ChromaticSlider.Value:+0.00;-0.00;0.00}";
        VignetteValue.Text = $"{VignetteSlider.Value:+0.00;-0.00;0.00}";
        VignetteMidpointValue.Text = $"{VignetteMidpointSlider.Value:0.00}";
        VignetteRoundnessValue.Text = $"{VignetteRoundnessSlider.Value:+0.00;-0.00;0.00}";
        VignetteFeatherValue.Text = $"{VignetteFeatherSlider.Value:0.00}";
        DitherValue.Text = $"{DitherSlider.Value:0.00}";
        DitherLevelsValue.Text = $"{DitherLevelsSlider.Value:0}";
        DitherSizeValue.Text = $"{DitherSizeSlider.Value:0}";
        ShowActive();

        DitherPixelsValue.Text = $"{DitherPixelsSlider.Value:0}";
        DitherPixelsTallValue.Text = $"{DitherPixelsTallSlider.Value:0}";
        DitherHueValue.Text = $"{DitherHueSlider.Value:0}\u00b0";
        DitherSaturationValue.Text = $"{DitherSaturationSlider.Value:0.00}";

        // Farbton und Saettigung gelten nur zweifarbig - sonst stuenden zwei Regler
        // da, die nichts tun.
        var tone = DitherDuotoneButton.IsChecked == true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        DitherHueHeader.Visibility = tone;
        DitherHueSlider.Visibility = tone;
        DitherSaturationHeader.Visibility = tone;
        DitherSaturationSlider.Visibility = tone;

        // Fehlerdiffusion kennt keinen Rasterpunkt - sie verteilt, statt zu rastern.
        // Den Regler stehenzulassen hiesse, eine Einstellung anzubieten, die nichts
        // tut, und danach sucht man hinterher lange.
        // Fehlerdiffusion kennt keinen Rasterpunkt; das Linienraster nennt ihn
        // anders, naemlich Linienabstand - aber es benutzt denselben Regler.
        DitherSizeSlider.IsEnabled = DitherPatternBox.SelectedIndex < FirstKernel;

        var angled = DitherPatternBox.SelectedIndex == 2
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

        DitherAngleHeader.Visibility = angled;
        DitherAngleSlider.Visibility = angled;
        DitherAngleValue.Text = $"{DitherAngleSlider.Value:0}°";

        GrainValue.Text = $"{GrainSlider.Value:0.00}";
        GrainSizeValue.Text = $"{GrainSizeSlider.Value:0}";
        GrainRoughnessValue.Text = $"{GrainRoughnessSlider.Value:0.00}";
        GrainColourValue.Text = $"{GrainColourSlider.Value:0.00}";
        DehazeValue.Text = $"{DehazeSlider.Value:0.00}";
        DehazeRadiusValue.Text = $"{DehazeRadiusSlider.Value:0}";
        TextureValue.Text = $"{TextureSlider.Value:+0.00;-0.00;0.00}";
        TextureRadiusValue.Text = $"{TextureRadiusSlider.Value:0}";
        BloomValue.Text = $"{BloomSlider.Value:0.00}";
        BloomThresholdValue.Text = $"{BloomThresholdSlider.Value:0.00}";
        BloomRadiusValue.Text = $"{BloomRadiusSlider.Value:0}";
        HalationValue.Text = $"{HalationSlider.Value:0.00}";
        HalationThresholdValue.Text = $"{HalationThresholdSlider.Value:0.00}";
        HalationRadiusValue.Text = $"{HalationRadiusSlider.Value:0}";
        HalationTintValue.Text = $"{HalationTintSlider.Value:0.00}";
        NoiseLumaValue.Text = $"{NoiseLumaSlider.Value:0.00}";
        NoiseColourValue.Text = $"{NoiseColourSlider.Value:0.00}";
        NoiseThresholdValue.Text = $"{NoiseThresholdSlider.Value:0.000}";
        UpdateZoneValues();
    }

    /// <summary>
    /// Der Fokusregler laeuft nicht gleichmaessig, sondern verdoppelnd.
    ///
    /// Eine gerade Skala von null bis zweihundert Metern draengte alles Brauchbare in
    /// das erste Zwanzigstel: Zwischen zwei und drei Metern entscheidet sich, ob ein
    /// Gesicht scharf ist, und zwischen hundert und hundertzehn nichts mehr. Der
    /// Weg des Reglers ist deshalb der Logarithmus der Entfernung - gleiche
    /// Reglerwege sind gleiche Faktoren.
    /// </summary>
    private const double NearestFocus = 0.1;
    private const double FocusRange = 2000.0;

    private static float FocusFrom(double at)
        => (float)(NearestFocus * Math.Pow(FocusRange, Math.Clamp(at, 0.0, 1.0)));

    private static double FocusAt(float metres)
        => Math.Clamp(Math.Log(Math.Max(NearestFocus, metres) / NearestFocus) / Math.Log(FocusRange), 0.0, 1.0);

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

    /// <summary>
    /// Woher die Verschiebung ihre Richtung nimmt.
    ///
    /// Eigener Griff und nicht bloss ein Regler mehr, weil dahinter ein anderer PASS
    /// gelesen wird: Die Wahl entscheidet, welche Datei ueberhaupt gebraucht wird,
    /// und deshalb muss danach neu gelesen und nicht nur neu gerechnet werden.
    /// </summary>
    private void OnDisplaceFromChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || !IsLoaded) return;

        _displace.From = DisplaceFromBox.SelectedIndex == 1
            ? DisplaceFrom.Motion
            : DisplaceFrom.Normal;

        ShowMissingPasses();
        Raise(interim: false);
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
