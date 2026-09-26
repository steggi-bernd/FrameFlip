using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Remote;
using FrameFlip.Views;
using ZXing;

internal static partial class Program
{
    private static int _checks;
    private static string _out = "";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            _out = Path.GetFullPath(args.FirstOrDefault() ?? "review"); Directory.CreateDirectory(_out);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(_out, "test-data", "config.json"));

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Views/Theme.xaml", "Views/DesktopTheme.xaml", "Views/DashboardTokens.xaml", "Localization/Strings.de.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FrameFlip;component/" + resource, UriKind.Relative) });
            TestLayout(); TestPlayback(); TestSettings(); TestQr();
            TestMergedOverlays(); TestConsentFlow(); TestCheckboxTemplates(); TestSettingRow(); TestPreviewZoom(); TestResourceMerge(); TestMaskGraph();
            Console.WriteLine($"PASS: {_checks} UI assertions. Images: {_out}");
            app.Shutdown(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(bool valid, string description)
    { if (!valid) throw new InvalidOperationException(description); _checks++; Console.WriteLine("PASS: " + description); }
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height, string? name = null, double dpi = 96)
    {
        Layout(element, width, height);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        if (name is not null)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(_out, name)); encoder.Save(stream);
        }
        return bitmap;
    }
    private static T Find<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
    private static void TestLayout()
    {
        var layout = new DesktopLayout(); layout.Reset();
        var window = new MainWindow(null, () => null, () => { }, _ => { }, () => { });
        var surface = (FrameworkElement)window.Content;
        void Arrange(int width, int height)
        {
            window.Width = width; window.Height = height;
            typeof(MainWindow).GetMethod("ApplyScale", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Layout(surface, width, height);
            typeof(MainWindow).GetMethod("ApplyDashboardLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Layout(surface, width, height);
        }
        Arrange(1440, 900);
        Check(!string.IsNullOrWhiteSpace(Find<TextBlock>(window,"StageEmpty").Text),"Empty viewer explains how to start");
        Check(!Find<WrapPanel>(window,"TransportControls").IsEnabled,"Playback controls wait for a loaded sequence");
        Check(Find<StackPanel>(window,"StageAnnotations").Visibility==Visibility.Collapsed,"No empty badges cover the welcome view");
        Check(Grid.GetColumn(Find<Grid>(window, "ViewerPanel")) == 2, "Current dashboard keeps its central viewer at desktop width");
        var shared = (DesktopLayout)typeof(MainWindow).GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        shared.NavigationWidth = 300; shared.MonitorShare = .35; shared.Save(); Arrange(1440,900);
        Check(Math.Abs(Find<Border>(window,"SequencePanel").ActualWidth - 300) < 2, "Navigation slider changes the sequence panel width");
        var previousMetrics = Find<Border>(window,"MetricsPanel").ActualWidth;
        shared.MonitorShare = .50; shared.Save(); Arrange(1440,900);
        Check(Find<Border>(window,"MetricsPanel").ActualWidth > previousMetrics, "System ratio changes the actual dashboard panel");
        shared.MonitorFirst = true; shared.Save(); Arrange(1440,900);
        Check(Grid.GetColumn(Find<Border>(window,"MetricsPanel")) == 0, "System panel moves to the left without moving the viewer");
        shared.Reset(); Arrange(1440,900);
        Find<ColumnDefinition>(window,"RightColumn").Width = new GridLength(240);
        Layout(surface,1440,900);
        typeof(MainWindow).GetMethod("SaveDashboardLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,null);
        Arrange(1440,900);
        Check(Math.Abs(Find<Border>(window,"MetricsPanel").ActualWidth-240)<2,"Dragged panel retains its width after saving");
        Check(Math.Abs(DesktopLayout.Load().MonitorShare-shared.MonitorShare)<.001,"Panel division survives reloading from disk");
        shared.Reset(); Arrange(1440,900); Render(surface,1440,900,"dashboard-wide.png");
        Arrange(960,900);
        Check(Grid.GetColumnSpan(Find<Grid>(window,"ViewerPanel")) == 5, "Narrow tall viewport gives viewer full width");
        Check(Grid.GetRow(Find<Border>(window,"MetricsPanel")) == 2, "Narrow dashboard places side panels below viewer");
        Render(surface,960,900,"dashboard-narrow.png");
        shared.LowerPanelHeight=340; shared.Save(); Arrange(960,900);
        Check(Math.Abs(Find<Border>(window,"MetricsPanel").ActualHeight-340)<2,"Lower panels accept a custom height");
        Find<RowDefinition>(window,"PanelsRow").Height=new GridLength(300); Layout(surface,960,900);
        typeof(MainWindow).GetMethod("SaveDashboardLayout",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        Check(Math.Abs(DesktopLayout.Load().LowerPanelHeight-300)<2,"Dragged lower height survives reloading");
        shared.Scale=1.35; shared.Save(); Arrange(900,560);
        Check(Find<Grid>(window,"StageArea").ActualHeight > 80, "Short viewport retains a usable stage at maximum requested scale");
        Render(surface,900,560,"dashboard-small-scaled.png");
        var rate=Find<ComboBox>(window,"RateBox");
        var viewer=Find<Grid>(window,"ViewerPanel");
        var rateEdge=rate.TranslatePoint(new Point(rate.ActualWidth,rate.ActualHeight),viewer);
        Check(rateEdge.X<=viewer.ActualWidth && rateEdge.Y<=viewer.ActualHeight,"Frame rate stays fully reachable in the smallest scaled viewport");
        var options=Find<WrapPanel>(window,"PlayChips");
        Check(options.Children.Cast<FrameworkElement>().All(c=>c.TranslatePoint(new Point(c.ActualWidth,c.ActualHeight),viewer).X<=viewer.ActualWidth),"All playback options wrap inside the viewer");
        shared.Reset(); Arrange(1440,900);
        window.ShowSettingsPage(); Layout(surface,1440,900);
        var page=Find<ContentControl>(window,"PageContent").Content;
        var editor=(SettingsEditor)Find<ContentControl>((SettingsPage)page,"EditorHost").Content;
        Find<TextBox>(editor,"BudgetBox").Text="777";
        shared.MonitorFirst = true; shared.Save();
        Find<RadioButton>(window,"NavDashboard").IsChecked=true;
        Layout(surface,1440,900);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Layout(surface,1440,900);
        Check(Grid.GetColumn(Find<Border>(window,"MetricsPanel")) == 0,"Layout preference applies on return from settings without resizing");
        window.ShowSettingsPage(); Layout(surface,1440,900);
        Check(ReferenceEquals(page,Find<ContentControl>(window,"PageContent").Content),"Settings page instance survives dashboard navigation");
        Check(Find<TextBox>(editor,"BudgetBox").Text=="777","Unsaved settings survive page navigation");
        Render(surface,1440,900,"dashboard-settings.png");
        Check(Application.Current.Windows.Count==1,"Embedded settings do not create a second window");
        Find<RadioButton>(window,"NavDashboard").IsChecked=true;
        window.Close(); layout.Reset();
    }
    private static void TestPlayback()
    {
        string folder=Path.Combine(_out,"Vorschau-Beispiel");
        Directory.CreateDirectory(folder);
        for(int frame=1;frame<=12;frame++)
        {
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(14,18,35),Color.FromRgb(41,22,63),45),null,new Rect(0,0,640,360));
                for(int line=0;line<12;line++)
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40,135,157,195)),1),new Point(0,220+line*13),new Point(640,220+line*13));
                double x=100+frame*34;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70,0,0,0)),null,new Point(x,282),70,12);
                dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(152,222,255),Color.FromRgb(139,55,225)){GradientOrigin=new Point(.3,.25)},null,new Point(x,170-Math.Sin(frame*Math.PI/12)*28),65,65);
                var label=new FormattedText($"FRAMEFLIP  /  VORSCHAU-BEISPIEL\nFRAME {frame:00} / 12",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),16,Brushes.White,1);
                dc.DrawText(label,new Point(26,24));
            }
            var bitmap=new RenderTargetBitmap(640,360,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(folder,$"frame_{frame:0000}.png"));encoder.Save(stream);
        }
        var window=new MainWindow(null,()=>null,()=>{},_=>{},()=>{},new AppSettings{Prebuffer=false,MainWidth=1440,MainHeight=900});
        var surface=(FrameworkElement)window.Content;
        Layout(surface,1440,900);
        var opened=(bool)typeof(MainWindow).GetMethod("OpenPath",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{Path.Combine(folder,"frame_0001.png")})!;
        var stage=Find<Image>(window,"StageImage");
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(stage.Source is null && DateTime.UtcNow<deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
            Thread.Sleep(10);
        }
        Layout(surface,1440,900);
        Check(opened && stage.Source is not null,"Local sequence appears in the dashboard viewer");
        Check(Find<TextBlock>(window,"StageZoom").Text=="1 / 12","Viewer identifies all twelve frames");
        Check(Find<TextBlock>(window,"StageEmpty").Visibility==Visibility.Collapsed,"Loaded image replaces the welcome view");
        Check(Find<WrapPanel>(window,"TransportControls").IsEnabled,"Playback becomes available after loading");
        var keyHandler=typeof(MainWindow).GetMethod("OnWindowKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!;
        using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("FrameFlip UI input check"){Width=1,Height=1,WindowStyle=0});
        foreach(var origin in new FrameworkElement[]{Find<ComboBox>(window,"RateBox"),Find<GridSplitter>(window,"LeftSplitter")})
        {
            var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent,Source=origin};
            keyHandler.Invoke(window,new object[]{origin,key});
            Check(!key.Handled && Find<TextBlock>(window,"StageZoom").Text=="1 / 12","Focused "+origin.Name+" keeps its arrow keys");
        }
        var stageKey=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent,Source=Find<Grid>(window,"StageArea")};
        keyHandler.Invoke(window,new object[]{window,stageKey});
        Check(stageKey.Handled && Find<TextBlock>(window,"StageZoom").Text=="2 / 12","Arrow key on the viewer advances exactly one frame");
        Render(surface,1440,900,"dashboard-sequence.png");
        window.Close();
    }
    private static void TestSettings()
    {
        var current = new AppSettings { MainWidth = 1240, MainHeight = 820, Fps = 24 };
        int applied = 0;
        string? Apply(AppSettings next) { current = next; applied++; return null; }
        var editor = new SettingsEditor(current, Apply, () => null, () => current);
        Layout(editor, 800, 580);
        Check(applied == 0, "Opening settings neither saves nor enables remote control");
        var budget = Find<TextBox>(editor, "BudgetBox"); budget.Text = "invalid";
        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(applied == 0 && Find<TextBlock>(editor, "StatusText").Visibility == Visibility.Visible, "Invalid budget blocks saving and shows feedback");
        budget.Text = "512";
        current.MainWidth = 1470; current.MainHeight = 860;
        Find<Button>(editor, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(applied == 1 && current.MemoryBudgetMb == 512, "Valid preferences apply from embedded settings");
        Check(current.MainWidth == 1470 && current.MainHeight == 860, "Saving preserves newer window state");
        editor.Dispose();
        var page = new SettingsPage(() => current, Apply, () => null, new DesktopLayout());
        Render(page, 910, 650, "settings-general.png");
        page.SelectAppearance(); Render(page, 910, 680, "settings-layout.png");
        var inner = (SettingsEditor)Find<ContentControl>(page, "EditorHost").Content;
        var tabs = Find<TabControl>(inner, "Tabs");
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            Render(page, 490, 610, $"settings-narrow-{i}.png");
            var save = Find<Button>(inner, "SaveButton");
            var right = save.TranslatePoint(new Point(save.ActualWidth, 0), page).X;
            Check(right <= 491, $"Settings tab {i}: apply action stays inside narrow viewport");
        }
        page.Dispose();

        // Verbindungen nach Entwurf A: beide Karten nebeneinander. Ein lokal erzeugter
        // Testschluessel, ein nie gestarteter Dienst - nichts geht ins Netz.
        var watchKey = FrameFlip.Remote.WatchKey.Create("test1");
        var watched = new AppSettings { TermsAccepted = AppSettings.TermsVersion, WatchEnabled = true,
                                        WatchSecret = FrameFlip.Remote.WatchStore.Protect(watchKey) };
        var service = new FrameFlip.Web.WatchService(watchKey, "relay.example.org", null, () => null, () => null);
        var connections = new SettingsPage(() => watched, _ => null, () => null, new DesktopLayout());
        typeof(SettingsPage).GetMethod("ConnectWatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(connections, new object?[] { new Func<FrameFlip.Web.WatchService?>(() => service), null, null,
                                                 new Action<Action>(then => then()), new Action<string>(_ => { }) });
        Render(connections, 1400, 860, "settings-overview.png");
        Render(connections, 900, 860, "settings-overview-medium.png");
        connections.SelectRemote();
        Render(connections, 1100, 760, "settings-connections.png");
        var connEditor = (SettingsEditor)Find<ContentControl>(connections, "EditorHost").Content;
        var pairCard = Find<Border>(connEditor, "PairCard");
        var watchCard = Find<Border>(connEditor, "WatchCardFrame");
        var sideBySide = watchCard.TranslatePoint(new Point(0, 0), pairCard);
        Check(sideBySide.X > pairCard.ActualWidth - 1 && Math.Abs(sideBySide.Y) < 1, "Connections: both cards side by side on a wide page");
        Render(connections, 490, 900, "settings-connections-narrow.png");
        var stacked = watchCard.TranslatePoint(new Point(0, 0), pairCard);
        Check(Math.Abs(stacked.X) < 1 && stacked.Y >= pairCard.ActualHeight - 1, "Connections: cards stack on a narrow page");
        connections.Dispose();
    }
    private static void TestQr()
    {
        // Test key is generated locally, never paired or sent to a relay.
        var text = new PairingInvite(PairingKey.Create(), "relay.example.org").Text;
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryHarder = true; reader.Options.TryInverted = true;
        foreach (bool inverted in new[] { false, true })
        foreach (int size in new[] { 220, 252, 320 })
        foreach (double dpi in new[] { 96.0, 144.0 })
        {
            var qr = new QrCodeView { Text = text, LightModules = inverted, Width = size, Height = size };
            var bitmap = Render(qr, size, size, size == 252 && dpi == 96 ? $"qr-{(inverted ? "dark" : "classic")}.png" : null, dpi);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var decoded = reader.Decode(pixels, bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            Check(decoded?.Text == text, $"QR roundtrip: inverted={inverted}, {size}px, {dpi}dpi");
        }
    }
}
