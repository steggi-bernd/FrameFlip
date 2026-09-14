using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
namespace FrameFlip.Views;
public partial class MainWindow
{
    private SettingsPage? _settingsPage;
    private bool _arrangingDashboard;
    private bool _draggingDashboard;
    private bool _stackDashboard;
    private void InitializeDashboardLayout()
    {
        foreach (var splitter in new[] { LeftSplitter, RightSplitter, LowerSplitter })
        {
            splitter.DragStarted += (_, _) => _draggingDashboard = true;
            splitter.DragCompleted += (_, _) => { _draggingDashboard = false; SaveDashboardLayout(); };
            splitter.KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) SaveDashboardLayout(); };
            splitter.MouseDoubleClick += (_, _) => _layout.Reset();
        }
        Loaded += (_, _) => ApplyDashboardLayout();
    }
    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs e) => ApplyDashboardLayout();
    private void ApplyDashboardLayout()
    {
        if (_arrangingDashboard || _draggingDashboard || DashboardBody.ActualWidth < 1) return;
        _arrangingDashboard = true;
        try
        {
            double width = DashboardBody.ActualWidth;
            // In einem schmalen, ausreichend hohen Fenster bekommt der Viewer die ganze Breite.
            // In flachen Fenstern bleiben drei Spalten, damit die Bühne ihre Höhe behält.
            _stackDashboard = width < 1120 && DashboardBody.ActualHeight >= 580;
            bool swap = _layout.MonitorFirst;
            double sequence = Math.Clamp(_layout.NavigationWidth, 150, Math.Max(150, Math.Min(420, width - 501)));
            double metrics = Math.Clamp((width - sequence - 16) * _layout.MonitorShare, 205, Math.Max(205, Math.Min(520, width - sequence - 296)));
            double sequenceMax = Math.Max(150, Math.Min(420, width - metrics - 296));
            double metricsMax = Math.Max(205, Math.Min(520, width - sequence - 296));
            LeftColumn.MaxWidth = _stackDashboard ? double.PositiveInfinity : swap ? metricsMax : sequenceMax;
            RightColumn.MaxWidth = _stackDashboard ? double.PositiveInfinity : swap ? sequenceMax : metricsMax;
            LeftColumn.MinWidth = swap ? 205 : 150;
            RightColumn.MinWidth = swap ? 150 : 205;
            CenterColumn.MinWidth = _stackDashboard ? 0 : 280;
            if (_stackDashboard)
            {
                double metricShare = Math.Clamp(width * _layout.MonitorShare / (width * _layout.MonitorShare + _layout.NavigationWidth), .35, .80);
                double first = swap ? metricShare : 1 - metricShare;
                LeftColumn.Width = new GridLength(first, GridUnitType.Star);
                RightColumn.Width = new GridLength(1 - first, GridUnitType.Star);
                CenterColumn.Width = new GridLength(0);
            }
            else
            {
                LeftColumn.Width = new GridLength(swap ? metrics : sequence);
                RightColumn.Width = new GridLength(swap ? sequence : metrics);
                CenterColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            ViewerRow.MinHeight = _stackDashboard ? 280 : 0;
            ViewerRow.Height = new GridLength(1, GridUnitType.Star);
            PanelsRow.MinHeight = _stackDashboard ? 180 : 0;
            PanelsRow.MaxHeight = _stackDashboard ? Math.Max(180, DashboardBody.ActualHeight - 288) : double.PositiveInfinity;
            PanelsRow.Height = new GridLength(_stackDashboard ? Math.Clamp(_layout.LowerPanelHeight, 180, PanelsRow.MaxHeight) : 0);
            Grid.SetColumn(ViewerPanel, _stackDashboard ? 0 : 2);
            Grid.SetColumnSpan(ViewerPanel, _stackDashboard ? 5 : 1);
            Grid.SetColumn(SequencePanel, swap ? 4 : 0);
            Grid.SetColumn(MetricsPanel, swap ? 0 : 4);
            Grid.SetRow(SequencePanel, _stackDashboard ? 2 : 0);
            Grid.SetRow(MetricsPanel, _stackDashboard ? 2 : 0);
            LowerSplitter.Visibility = _stackDashboard ? Visibility.Visible : Visibility.Collapsed;
            LeftSplitter.Visibility = _stackDashboard ? Visibility.Collapsed : Visibility.Visible;
            // Im gestapelten Modus ist die Verhältnissteuerung in den Einstellungen eindeutig;
            // ein Spaltengriff könnte dagegen nur die leere Mittelspalte verbreitern.
            RightSplitter.Visibility = _stackDashboard ? Visibility.Collapsed : Visibility.Visible;
            double tileWidth = _stackDashboard ? width * Math.Clamp(width * _layout.MonitorShare / (width * _layout.MonitorShare + _layout.NavigationWidth), .35, .80) : metrics;
            Tiles.Columns = Math.Clamp((int)Math.Floor((tileWidth - 44) / _layout.TileSize), 1, 3);
            StatusHotkey.Visibility = width < 1200 ? Visibility.Collapsed : Visibility.Visible;
            TitleInfo.Visibility = width < 1000 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _arrangingDashboard = false; }
    }
    private void SaveDashboardLayout()
    {
        if (_stackDashboard)
        {
            _layout.LowerPanelHeight = PanelsRow.ActualHeight;
            _layout.Save();
            return;
        }
        double sequence = SequencePanel.ActualWidth;
        double metrics = MetricsPanel.ActualWidth;
        _layout.NavigationWidth = sequence;
        _layout.MonitorShare = metrics / Math.Max(1, DashboardBody.ActualWidth - sequence - 16);
        _layout.Save();
    }
    private void OnViewerPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = ViewerPanel.ActualWidth < 620;
        Grid.SetColumn(ViewChips, compact ? 0 : 1);
        Grid.SetRow(ViewChips, compact ? 1 : 0);
        ViewChips.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ViewChips.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }
    private void OnStageFocus(object sender, MouseButtonEventArgs e)
    {
        if (!OwnsNavigationKeys(e.OriginalSource as DependencyObject)) StageArea.Focus();
    }
    private static bool OwnsNavigationKeys(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or ComboBox or Slider or Thumb or ButtonBase) return true;
            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private void OnLayoutPreferences(object sender, RoutedEventArgs e)
    {
        ShowSettingsPage(); _settingsPage?.SelectAppearance();
    }
    private void RestorePageFocus()
    {
        if (_page == "dashboard") StageArea.Focus();
        else if (_page == "projects") NavProjects.Focus();
        else NavSettings.Focus();
    }
    private void AnimatePageChange()
    {
        if (_layout.ReduceMotion || !SystemParameters.ClientAreaAnimation) return;
        PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(150)));
    }
}
