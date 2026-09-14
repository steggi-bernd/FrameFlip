using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Views;

internal static partial class Program
{
    private static object? Call(MainWindow window, string method, params object?[] args)
        => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void Flush()
        => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void UseLanguageResources(string language)
    {
        // Der Testlaeufer ist die Entry-Assembly. Die echten Sprachressourcen
        // deshalb explizit aus FrameFlip laden, ohne Produktionszustand zu patchen.
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        foreach (var dictionary in dictionaries.Where(d => d.Source?.OriginalString.Contains("Strings.") == true).ToArray())
            dictionaries.Remove(dictionary);
        dictionaries.Add(new ResourceDictionary { Source = new Uri($"/FrameFlip;component/Localization/Strings.{language}.xaml", UriKind.Relative) });
    }

    private static bool Inside(FrameworkElement child, FrameworkElement parent)
    {
        var top = child.TranslatePoint(new Point(), parent);
        var bottom = child.TranslatePoint(new Point(child.ActualWidth, child.ActualHeight), parent);
        return top.X >= -1 && top.Y >= -1 && bottom.X <= parent.ActualWidth + 1 && bottom.Y <= parent.ActualHeight + 1;
    }

    private static bool DescendsFrom(DependencyObject? child, DependencyObject ancestor)
    {
        while (child is Visual)
        {
            if (ReferenceEquals(child, ancestor)) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private static KeyEventArgs SendKey(MainWindow window, HwndSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = Find<Grid>(window, "StageArea"),
        };
        Call(window, "OnWindowKeyDown", window, args);
        return args;
    }

    private static void TestMergedOverlays()
    {
        foreach (string language in new[] { "de", "en" })
        {
            UseLanguageResources(language);
            var current = new AppSettings { Language = language };
            var window = new MainWindow(null, () => null, () => { }, _ => { }, () => { }, current);
            var surface = (FrameworkElement)window.Content;
            var layout = (DesktopLayout)typeof(MainWindow).GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var terms = Find<Grid>(window, "TermsHost");
            var pair = Find<Grid>(window, "PairHost");
            var siblings = (Panel)terms.Parent;
            Check(siblings.Children.IndexOf(terms) > siblings.Children.IndexOf(pair), language + ": terms remain after pairing in markup");
            Call(window, "AskTerms", (Action)(() => { }), null);

            foreach (var size in new[] { (1440, 900), (960, 900), (900, 560) })
            foreach (double scale in new[] { .85, 1.0, 1.35 })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                layout.Scale = scale; layout.Save();
                Layout(surface, size.Item1, size.Item2); Flush();
                var card = Find<Border>(window, "TermsCard");
                Check(Inside(card, terms), $"{language}: terms card stays inside {size}, scale {scale}");
                pair.Visibility = Visibility.Visible;
                Find<Border>(window, "PairCodeFrame").Visibility = Visibility.Visible;
                Find<Border>(window, "WatchCodeFrame").Visibility = Visibility.Visible;
                Find<StackPanel>(window, "WatchPassRow").Visibility = Visibility.Visible;
                Find<TextBlock>(window, "PairHint").Text = Strings.T("S_ScanHint");
                Find<TextBlock>(window, "WatchHint").Text = Strings.T("D_WatchOn");
                Find<TextBlock>(window, "WatchPassHint").Text = Strings.T("D_WatchPassHint");
                Find<TextBlock>(window, "WatchAddress").Text = "https://example.invalid/watch/local-ui-test";
                Layout(surface, size.Item1, size.Item2);
                var center = card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), surface);
                Check(DescendsFrom(VisualTreeHelper.HitTest(surface, center)?.VisualHit, terms), $"{language}: terms receive clicks above the QR overlay at {size}, scale {scale}");
                var scroll = Find<ScrollViewer>(window, "TermsScroll");
                scroll.ScrollToBottom(); Layout(surface, size.Item1, size.Item2);
                Check(Inside(Find<Button>(window, "TermsGo"), terms), $"{language}: consent action is reachable at {size}, scale {scale}");
                Check(!Find<StackPanel>(window, "NavigationControls").IsEnabled && !Find<Grid>(window, "DashboardBody").IsEnabled, language + ": modal blocks background controls");
                if (size == (900, 560) && scale == 1.35)
                    Render(surface, size.Item1, size.Item2, $"terms-small-{language}.png");
                terms.Visibility = Visibility.Collapsed;
                Layout(surface, size.Item1, size.Item2);
                Check(Inside(Find<Border>(window, "PairCard"), pair), $"{language}: both QR sections fit the pairing viewport at {size}, scale {scale}");
                var pairScroll = Find<ScrollViewer>(window, "PairScroll");
                pairScroll.ScrollToBottom(); Layout(surface, size.Item1, size.Item2);
                Check(Inside(Find<StackPanel>(window, "WatchPassRow"), pair), $"{language}: watch controls can be reached at {size}, scale {scale}");
                if (size == (900, 560) && scale == 1.35)
                    Render(surface, size.Item1, size.Item2, $"pair-small-{language}.png");
                pair.Visibility = Visibility.Collapsed;
                terms.Visibility = Visibility.Visible;
                scroll.ScrollToTop();
            }
            Check(Find<TextBlock>(window, "TermsBody").GetValue(Track.TextProperty) is null
                && !Find<TextBlock>(window, "TermsBody").Text.Contains('\u200a'), language + ": terms paragraphs have no tracking spaces");
            Call(window, "OnTermsLater", window, new RoutedEventArgs());
            window.Close();
            layout.Reset();
        }
        UseLanguageResources("de");
    }

    private static void TestConsentFlow()
    {
        var current = new AppSettings();
        int calls = 0;
        string? Apply(AppSettings next)
        {
            calls++;
            bool wantsNetwork = next.RemoteEnabled || next.WatchEnabled;
            next.Normalize();
            if (wantsNetwork && !next.TermsOk) return Strings.T("S_TermsMissing");
            current = next.Clone();
            return null;
        }
        var window = new MainWindow(null, () => null, () => { }, _ => { }, () => { }, current, null, Apply, () => current);
        var surface = (FrameworkElement)window.Content;
        Layout(surface, 1440, 900);
        var apply = (Func<AppSettings, string?>)typeof(MainWindow).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var request = current.Clone();
        request.WatchEnabled = true;
        request.RelayHost = "custom.example.invalid";
        Check(apply(request) is not null && calls == 0 && !current.WatchEnabled, "Apply waits for consent before calling the host");
        Check(Find<TextBlock>(window, "TermsBody").Text == Strings.T("D_TermsBodyOwn"), "Consent describes the requested relay, not the previously saved relay");
        Check(Find<TextBlock>(window, "TermsLinks").Visibility == Visibility.Collapsed, "Own-relay prompt hides the default operator links");
        current.MainWidth = 1310; current.MainHeight = 810;
        Find<CheckBox>(window, "TermsAgree").IsChecked = true;
        Call(window, "OnTermsAccept", window, new RoutedEventArgs());
        Flush();
        Check(calls == 2 && current.TermsOk && current.WatchEnabled && current.RelayHost == request.RelayHost, "Acceptance saves consent then retries the original request once");
        Check(current.MainWidth == 1310 && current.MainHeight == 810, "Consent retry keeps window changes made while the question was open");

        current = new AppSettings();
        calls = 0;
        request = current.Clone(); request.RemoteEnabled = true;
        apply(request);
        using var source = new HwndSource(new HwndSourceParameters("Overlay input check") { Width = 1, Height = 1, WindowStyle = 0 });
        Check(!SendKey(window, source, Key.Right).Handled, "Terms overlay does not run playback shortcuts");
        Check(SendKey(window, source, Key.Escape).Handled && Find<Grid>(window, "TermsHost").Visibility == Visibility.Collapsed, "Escape declines the pending request");
        Flush();
        Check(calls == 0 && !current.TermsOk && !current.RemoteEnabled, "Declining neither saves consent nor enables the service");
        Call(window, "OnShowPairing", window, new RoutedEventArgs());
        Check(Find<Grid>(window, "TermsHost").Visibility == Visibility.Visible && Find<Grid>(window, "PairHost").Visibility == Visibility.Collapsed, "Pairing asks before showing a QR code");
        Find<CheckBox>(window, "TermsAgree").IsChecked = true;
        Call(window, "OnTermsAccept", window, new RoutedEventArgs()); Flush();
        Check(current.TermsOk && Find<Grid>(window, "PairHost").Visibility == Visibility.Visible, "Pairing resumes after acceptance");
        Check(!SendKey(window, source, Key.Space).Handled, "QR overlay does not start playback");
        Check(SendKey(window, source, Key.Escape).Handled && Find<Grid>(window, "PairHost").Visibility == Visibility.Collapsed, "Escape closes the QR overlay");
        window.Close();
    }

    private static void TestCheckboxTemplates()
    {
        foreach (string style in new[] { "DialogCheckBox", "DesktopCheckBox" })
        {
            var checkbox = new CheckBox
            {
                Style = (Style)Application.Current.FindResource(style),
                Content = "Eine lange Beschriftung, die in einer schmalen Fläche zuverlässig in mehrere Zeilen umbrechen soll.",
                VerticalAlignment = VerticalAlignment.Top,
            };
            var holder = new Grid(); holder.Children.Add(checkbox);
            Render(holder, 230, 140, style + "-wrapping.png");
            Check(checkbox.ActualHeight > 30 && checkbox.ActualHeight < 140, style + " wraps a plain string label");
            var rich = new TextBlock { Text = (string)checkbox.Content, TextWrapping = TextWrapping.Wrap };
            checkbox.Content = rich;
            Layout(holder, 230, 140);
            Check(rich.ActualHeight > 30 && Inside(rich, holder), style + " preserves and wraps a TextBlock label");
        }
    }

    private static void TestResourceMerge()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string Locate(string name) => Path.Combine("FrameFlip", "Localization", name);
        var de = XDocument.Load(Locate("Strings.de.xaml")).Root!.Elements().Select(e => (string)e.Attribute(x + "Key")!).ToArray();
        var en = XDocument.Load(Locate("Strings.en.xaml")).Root!.Elements().Select(e => (string)e.Attribute(x + "Key")!).ToArray();
        Check(de.Length == de.Distinct().Count() && en.Length == en.Distinct().Count(), "Merged language dictionaries contain no duplicate keys");
        Check(de.Order().SequenceEqual(en.Order()), "Merged languages contain the same keys");
    }
}
