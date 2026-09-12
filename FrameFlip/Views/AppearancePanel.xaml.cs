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
        ShareSlider.Value = _layout.MonitorShare * 100; NavSlider.Value = _layout.NavigationWidth;
        OrderBox.IsChecked = _layout.MonitorFirst; MotionBox.IsChecked = _layout.ReduceMotion;
        UpdateLabels(); _syncing = false;
    }
    private void UpdateLabels()
    {
        ScaleValue.Text = $"{_layout.Scale * 100:0} %"; TileValue.Text = $"{_layout.TileSize:0} px";
        ShareValue.Text = $"{_layout.MonitorShare * 100:0} %"; NavValue.Text = $"{_layout.NavigationWidth:0} px";
        double first = _layout.MonitorFirst ? _layout.MonitorShare : 1 - _layout.MonitorShare;
        PreviewLeft.Width = new GridLength(first, GridUnitType.Star); PreviewRight.Width = new GridLength(1 - first, GridUnitType.Star);
        Grid.SetColumn(PreviewRender, _layout.MonitorFirst ? 4 : 2); Grid.SetColumn(PreviewSystem, _layout.MonitorFirst ? 2 : 4);
    }
    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded) return;
        _layout.Scale = ScaleSlider.Value / 100; _layout.TileSize = TileSlider.Value;
        _layout.MonitorShare = ShareSlider.Value / 100; _layout.NavigationWidth = NavSlider.Value;
        _layout.MonitorFirst = OrderBox.IsChecked == true; _layout.ReduceMotion = MotionBox.IsChecked == true;
        UpdateLabels(); _saveTimer.Stop(); _saveTimer.Start();
    }
    private void OnReset(object sender, RoutedEventArgs e) => _layout.Reset();
}