using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die uebrigen Werkzeuge im Panel: Weissabgleich, Dynamik, die drei Zonen, die
/// Farbbereiche und die Tabelle.
///
/// Alle bis auf die Dynamik stehen in aufklappbaren Abschnitten. Das Panel ist 288
/// Punkte breit, und untereinander gestellt waeren es mehr als dreissig Regler -
/// wer die Belichtung nachziehen will, soll nicht an achtzehn Farbreglern
/// vorbeirollen muessen.
/// </summary>
public sealed partial class ViewerWindow
{
    private WhiteBalanceTool _whiteBalance = new();
    private LiftGammaGainTool _zones = new();
    private HslTool _bands = new();
    private VibranceTool _vibrance = new();
    private LutTool _lut = new();

    /// <summary>Welche der drei Groessen die acht Bereichsregler gerade zeigen.</summary>
    private int _bandMode;

    private readonly List<Slider> _bandSliders = new();

    /// <summary>
    /// True, waehrend die Regler aus den Werkzeugen gefuellt werden. Ohne das
    /// loeste jedes gesetzte Slider.Value seinerseits eine Aenderung aus und
    /// schriebe den Wert zurueck - beim Laden einer Vorlage entstuende daraus ein
    /// Hin und Her, das am Ende irgendwo stehenbleibt.
    /// </summary>
    private bool _fillingTools;

    private void SetUpTools()
    {
        _whiteBalance = Take<WhiteBalanceTool>();
        _zones = Take<LiftGammaGainTool>();
        _bands = Take<HslTool>();
        _vibrance = Take<VibranceTool>();
        _lut = Take<LutTool>();

        BuildBandSliders();
        PushToolsToControls();

        _preparedGrading = _grading.Prepare();

        T Take<T>() where T : IGradingTool, new()
        {
            var found = _grading.Tools.OfType<T>().FirstOrDefault();
            if (found is not null) return found;

            var created = new T();
            _grading.Tools.Add(created);
            return created;
        }
    }

    /// <summary>
    /// Die acht Bereichsregler entstehen im Quelltext, nicht in der XAML: drei
    /// Groessen mal acht Bereiche waeren vierundzwanzig fast gleiche Bloecke, und
    /// der Umschalter macht daraus ohnehin acht, die ihr Ziel wechseln.
    /// </summary>
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
            var caption = new TextBlock
            {
                Text = Strings.T(keys[i]),
                Style = (Style)FindResource("PanelLabel"),
                Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 2),
            };

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

            slider.ValueChanged += OnToolChanged;
            slider.MouseDoubleClick += OnSliderReset;

            BandSliders.Children.Add(caption);
            BandSliders.Children.Add(slider);
            _bandSliders.Add(slider);
        }
    }

    /// <summary>Traegt die Werte der Werkzeuge in die Regler ein.</summary>
    private void PushToolsToControls()
    {
        _fillingTools = true;

        try
        {
            TemperatureSlider.Value = Math.Clamp(_whiteBalance.Kelvin, TemperatureSlider.Minimum,
                                                 TemperatureSlider.Maximum);
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
            _fillingTools = false;
        }

        UpdateToolValues();
    }

    /// <summary>Uebernimmt die Reglerstellungen in die Werkzeuge.</summary>
    private void OnToolChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // IsLoaded ist hier nicht nur Vorsicht: Beim Einlesen der XAML setzt jeder
        // Regler erst Minimum, dann Maximum, dann Value, und schon das Minimum
        // loest eine Aenderung aus. Zu diesem Zeitpunkt sind die spaeter
        // aufgefuehrten Regler noch nicht da - der Zugriff darauf endete in einer
        // NullReferenceException, bevor das Fenster ueberhaupt stand.
        if (_fillingTools || _closing || !IsLoaded) return;

        _whiteBalance.Kelvin = (float)TemperatureSlider.Value;
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

        UpdateToolValues();
        ApplyToolChange();
    }

    /// <summary>
    /// Derselbe Weg wie beim Kurvenfeld: waehrend gezogen wird grob rechnen, nach
    /// einer kurzen Ruhe wieder genau.
    /// </summary>
    private void ApplyToolChange()
    {
        _preparedGrading = _grading.Prepare();
        _settings.Grading = _grading;

        _coarse = true;
        _coarseTimer?.Stop();
        _coarseTimer?.Start();

        UpdateCurveBadge();
        RedrawCurrentFrame();
    }

    private void UpdateToolValues()
    {
        TemperatureValue.Text = $"{TemperatureSlider.Value:0} K";
        TintValue.Text = $"{TintSlider.Value:+0;-0;0}";
        VibranceValue.Text = $"{VibranceSlider.Value:+0.00;-0.00;0.00}";
        LutStrengthValue.Text = $"{LutStrengthSlider.Value:0.00}";
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

    private void OnBandModePicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton picked || picked.Tag is not string tag) return;
        if (!int.TryParse(tag, out int mode)) return;

        foreach (var button in new[] { BandHueButton, BandSatButton, BandLumButton })
        {
            if (!ReferenceEquals(button, picked)) button.IsChecked = false;
        }

        _bandMode = mode;

        // Die Regler zeigen jetzt eine andere Groesse derselben acht Bereiche.
        _fillingTools = true;

        try
        {
            _bands.Prepare();
            for (int i = 0; i < _bandSliders.Count && i < _bands.Bands.Count; i++)
                _bandSliders[i].Value = BandValue(_bands.Bands[i], _bandMode);
        }
        finally
        {
            _fillingTools = false;
        }
    }

    /// <summary>Klappt einen Abschnitt auf oder zu. Das Ziel steht im Tag des Knopfes.</summary>
    private void OnSectionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string name) return;
        if (FindName(name) is not FrameworkElement body) return;

        bool open = body.Visibility != Visibility.Visible;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        button.Content = open ? "−" : "+";

        ShowBar();
    }

    private void OnPickLutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_PickLut"),
            // Wie im Exportfenster: der Filtertext bleibt unuebersetzt, weil er im
            // Systemdialog neben dessen eigenen, ebenfalls festen Beschriftungen steht.
            Filter = "Cube|*.cube|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        if (_lut.Path.Length > 0)
        {
            try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(_lut.Path); }
            catch (Exception) { /* ein ungueltiger Pfad ist kein Grund, den Dialog nicht zu oeffnen */ }
        }

        // Das Vorschaufenster schliesst bei Fokusverlust - waehrend eines eigenen
        // Dialogs darf es das nicht.
        ModalDialogOpen = true;

        try
        {
            if (dialog.ShowDialog(this) != true) return;

            _lut.Path = dialog.FileName;
            _lut.Prepare();
            UpdateLutText();
            ApplyToolChange();
        }
        finally
        {
            ModalDialogOpen = false;
            Activate();
        }
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
