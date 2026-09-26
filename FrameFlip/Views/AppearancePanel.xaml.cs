using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace FrameFlip.Views;
public partial class AppearancePanel : UserControl
{
    private readonly DesktopLayout _layout;
    private bool _syncing;
    private readonly DispatcherTimer _saveTimer;
    public AppearancePanel(DesktopLayout layout)
    {
        _layout = layout;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _layout.Save(); };
        InitializeComponent(); Sync();
        Loaded += (_, _) => { _layout.Changed += Sync; Sync(); };
        Unloaded += (_, _) => { _layout.Changed -= Sync; if (_saveTimer.IsEnabled) { _saveTimer.Stop(); _layout.Save(); } };
    }
    private void Sync()
    {
        _syncing = true;
        ScaleSlider.Value = _layout.Scale * 100; TileSlider.Value = _layout.TileSize;
        AutoScaleBox.IsChecked = _layout.AutoScale; ScaleSlider.IsEnabled = !_layout.AutoScale;
        ShareSlider.Value = _layout.MonitorShare * 100; NavSlider.Value = _layout.NavigationWidth;
        HeightSlider.Value = _layout.LowerPanelHeight;
        OrderBox.IsChecked = _layout.MonitorFirst; MotionBox.IsChecked = _layout.ReduceMotion;
        UpdateLabels(); _syncing = false;
    }
    private void UpdateLabels()
    {
        ScaleValue.Text = $"{EffectiveScale * 100:0} %"; TileValue.Text = $"{_layout.TileSize:0} px";
        ShareValue.Text = $"{_layout.MonitorShare * 100:0} %"; NavValue.Text = $"{_layout.NavigationWidth:0} px";
        HeightValue.Text = $"{_layout.LowerPanelHeight:0} px";
        double sequence = _layout.NavigationWidth / 100;
        double metrics = Math.Clamp(10 * _layout.MonitorShare, 2.05, 5.2);
        PreviewLeft.Width = new GridLength(_layout.MonitorFirst ? metrics : sequence, GridUnitType.Star);
        PreviewRight.Width = new GridLength(_layout.MonitorFirst ? sequence : metrics, GridUnitType.Star);
        Grid.SetColumn(PreviewSequence, _layout.MonitorFirst ? 4 : 0);
        Grid.SetColumn(PreviewSystem, _layout.MonitorFirst ? 0 : 4);
    }
    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded) return;
        _layout.Scale = ScaleSlider.Value / 100; _layout.TileSize = TileSlider.Value;
        _layout.AutoScale = AutoScaleBox.IsChecked == true; ScaleSlider.IsEnabled = !_layout.AutoScale;
        _layout.MonitorShare = ShareSlider.Value / 100; _layout.NavigationWidth = NavSlider.Value;
        _layout.LowerPanelHeight = HeightSlider.Value;
        _layout.MonitorFirst = OrderBox.IsChecked == true; _layout.ReduceMotion = MotionBox.IsChecked == true;
        UpdateLabels(); _saveTimer.Stop(); _saveTimer.Start();
    }
    private void OnReset(object sender, RoutedEventArgs e) => _layout.Reset();

    /// <summary>Die Skalierung, die gerade gilt: bei der Automatik die des Bildschirms unter dem Fenster.</summary>
    internal double EffectiveScale => _layout.AutoScale ? DesktopLayout.AutoFor(ScreenScale.EffectiveHeight(this)) : _layout.Scale;

    /// <summary>Der Regler fuer die Skalierung - fuer die Kachel der Uebersicht, die ihn spiegelt.</summary>
    internal Slider Scale => ScaleSlider;

    /// <summary>Der Schalter fuer die Automatik - ebenfalls fuer die Uebersicht.</summary>
    internal CheckBox AutoScale => AutoScaleBox;
}