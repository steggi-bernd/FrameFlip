using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Views;

internal static partial class Program
{
    private static object? Call(MainWindow window, string method, params object?[] args)
        => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    /// <summary>Wie oben, aber fuer jedes Fenster - die Lupe sitzt in einem anderen.</summary>
    private static object? CallOn(object ziel, string method, params object?[] args)
        => ziel.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ziel, args);

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

    /// <summary>
    /// Die Einstellungszeile: (i) klappt die Erklaerung auf, Fussnote steht von selbst.
    ///
    /// Geprueft wird ueber die HOEHE und nicht ueber den Text. Die Textblöcke einer
    /// Zeile bekommen in den Bedienungshilfen keinen eigenen Knoten - sie werden in
    /// den Namen der Zeile eingefaltet. Wer nach ihnen sucht, findet nichts und haelt
    /// eine funktionierende Zeile fuer kaputt. Die Hoehe luegt nicht.
    /// </summary>
    /// <summary>
    /// Ein Element aus dem VORLAGENBAUM holen.
    ///
    /// FindName taugt dafuer nicht: Es sucht im Namensbereich des XAML, und ein
    /// Vorlagenteil liegt in einem eigenen. Wer das verwechselt, bekommt null und
    /// haelt eine funktionierende Vorlage fuer kaputt.
    /// </summary>
    private static T? InTree<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var kind = VisualTreeHelper.GetChild(root, i);
            if (kind is T treffer) return treffer;
            if (InTree<T>(kind) is { } tiefer) return tiefer;
        }
        return null;
    }

    private static void TestSettingRow()
    {
        var zeile = new SettingRow
        {
            Style = (Style)Application.Current.FindResource("SettingRowStyle"),
            Header = "Eine Einstellung",
            Content = new CheckBox { Style = (Style)Application.Current.FindResource("DesktopCheckBox") },

            // Sonst dehnt sie sich im Gitter auf dessen volle Hoehe, und jede
            // Messung misst das Gitter statt die Zeile.
            VerticalAlignment = VerticalAlignment.Top,
        };

        var holder = new Grid { Width = 460 };
        holder.Children.Add(zeile);

        Layout(holder, 460, 400);
        double schlicht = zeile.ActualHeight;
        Check(schlicht > 20 && schlicht < 90, $"eine Zeile ohne Beiwerk bleibt flach ({schlicht:0} px)");

        zeile.Footnote = "Eine Fussnote, die immer sichtbar bleibt und ueber mehrere Zeilen laufen darf.";
        Layout(holder, 460, 400);
        double mitFuss = zeile.ActualHeight;
        Check(mitFuss > schlicht, $"die Fussnote steht ohne Zutun da ({mitFuss:0} px)");

        zeile.Explain = "Was die Einstellung tut, und wann man sie anfasst - der zweite Teil ist der Gewinn.";
        Layout(holder, 460, 400);
        // Nicht auf den Pixel genau: Mit gesetzter Erklaerung erscheint das (i),
        // und das darf die Kopfzeile um seine eigene Hoehe wachsen lassen. Worauf es
        // ankommt, ist der Unterschied zum AUFGEKLAPPTEN Zustand - der ist ein
        // Vielfaches davon.
        double mitInfo = zeile.ActualHeight;
        Check(mitInfo - mitFuss < 10,
            $"eine gesetzte Erklaerung allein klappt noch nichts auf ({mitInfo:0} px)");

        var info = InTree<System.Windows.Controls.Primitives.ToggleButton>(zeile);
        Check(info is not null && info.Visibility == Visibility.Visible,
            "sie bringt aber ein (i) mit");

        info!.IsChecked = true;
        Layout(holder, 460, 400);
        Check(zeile.ActualHeight > mitInfo + 15,
            $"und das (i) klappt sie auf ({zeile.ActualHeight:0} px)");

        info.IsChecked = false;
        Layout(holder, 460, 400);
        Check(Math.Abs(zeile.ActualHeight - mitInfo) < 1, "nochmal geklickt, wieder zu");

        // Ohne Erklaerung kein (i) - sonst stuende ein Knopf da, der nichts zeigt.
        var ohne = new SettingRow
        {
            Style = (Style)Application.Current.FindResource("SettingRowStyle"),
            Header = "Ohne Erklaerung",
            VerticalAlignment = VerticalAlignment.Top,
        };
        var holder2 = new Grid { Width = 460 };
        holder2.Children.Add(ohne);
        Layout(holder2, 460, 200);
        Check(InTree<System.Windows.Controls.Primitives.ToggleButton>(ohne)?.Visibility == Visibility.Collapsed,
            "ohne Erklaerung erscheint kein (i)");

        Render(holder, 460, 400, "SettingRow.png");
    }

    /// <summary>
    /// Die Lupe der Einzelbildvorschau: Zoom auf den Zeiger, Schieben, Einpassen.
    ///
    /// Gemessen wird die Matrix und nicht das Bild. Von aussen laesst sich der Zoom
    /// gar nicht pruefen - das Fenster haengt als Kind am Hauptfenster, und wie weit
    /// ein Bild hineingezoomt ist, meldet keine Bedienungshilfe. Die Matrix sagt es
    /// genau.
    ///
    /// Die eigentliche Zusicherung ist die dritte: Beim Zoomen muss die Stelle UNTER
    /// DEM ZEIGER dort bleiben, wo der Zeiger ist. Das ist der Unterschied zwischen
    /// einer Lupe und einem Bild, das beim Scrollen in die Ecke wandert.
    /// </summary>
    private static void TestPreviewZoom()
    {
        string ordner = Path.Combine(_out, "Lupe-Beispiel");
        Directory.CreateDirectory(ordner);

        var bilder = new List<string>();

        for (int i = 1; i <= 3; i++)
        {
            var zeichnung = new DrawingVisual();
            using (var dc = zeichnung.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 24, 46)), null, new Rect(0, 0, 320, 180));
                dc.DrawEllipse(Brushes.White, null, new Point(40 * i, 90), 12, 12);
            }

            var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(zeichnung);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string datei = Path.Combine(ordner, $"frame_{i:0000}.png");
            using (var stream = File.Create(datei)) encoder.Save(stream);

            bilder.Add(datei);
        }

        var fenster = new FramePreviewWindow(bilder[0], bilder, _ => { });
        var flaeche = (FrameworkElement)fenster.Content;
        Layout(flaeche, 900, 620);

        var lupe = (MatrixTransform)typeof(FramePreviewWindow)
            .GetField("Lupe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fenster)!;

        void Zoom(double faktor, Point um)
            => CallOn(fenster, "Zoomen", faktor, um);

        Check(lupe.Matrix.IsIdentity, "frisch geoeffnet ist die Ansicht eingepasst");

        var zeiger = new Point(300, 200);
        Zoom(2, zeiger);

        Check(Math.Abs(lupe.Matrix.M11 - 2) < .001, $"zweifach heisst zweifach ({lupe.Matrix.M11:0.###})");

        /* Vor dem Zoomen war die Abbildung die Identitaet - der Bildpunkt unter dem
         * Zeiger hatte also genau dessen Koordinaten. Eine Skalierung UM diesen Punkt
         * laesst ihn liegen: Er muss nach dem Zoomen wieder auf sich selbst fallen.
         * Tut er das nicht, wandert das Bild beim Scrollen davon - genau der Fehler,
         * den die Zuschauerseite im Browser einmal hatte. */
        var getroffen = lupe.Matrix.Transform(zeiger);

        Check(Math.Abs(getroffen.X - zeiger.X) < .5 && Math.Abs(getroffen.Y - zeiger.Y) < .5,
            $"die Stelle unter dem Zeiger bleibt unter dem Zeiger ({getroffen.X:0.#}/{getroffen.Y:0.#})");

        Zoom(1000, zeiger);
        Check(lupe.Matrix.M11 <= 24.001, $"der Zoom hat eine Obergrenze ({lupe.Matrix.M11:0.#})");

        Zoom(0.0001, zeiger);
        Check(lupe.Matrix.M11 >= 1, $"und eine Untergrenze - kleiner als eingepasst gibt es nicht ({lupe.Matrix.M11:0.###})");

        Zoom(4, zeiger);
        CallOn(fenster, "Anpassen");
        Check(lupe.Matrix.IsIdentity, "Einpassen setzt alles zurueck");

        // Beim Bildwechsel ebenfalls: Der Ausschnitt vom vorigen Frame ist am
        // naechsten selten der gesuchte.
        Zoom(3, zeiger);
        CallOn(fenster, "Step", 1);
        Check(lupe.Matrix.IsIdentity, "ein Bildwechsel passt wieder ein");

        fenster.Close();
    }

    private static void TestResourceMerge()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string Locate(string name) => Path.Combine("FrameFlip", "Localization", name);
        var de = XDocument.Load(Locate("Strings.de.xaml")).Root!.Elements().Select(e => (string)e.Attribute(x + "Key")!).ToArray();
        var en = XDocument.Load(Locate("Strings.en.xaml")).Root!.Elements().Select(e => (string)e.Attribute(x + "Key")!).ToArray();
        Check(de.Length == de.Distinct().Count() && en.Length == en.Distinct().Count(), "Merged language dictionaries contain no duplicate keys");
        Check(de.Order().SequenceEqual(en.Order()), "Merged languages contain the same keys");

        /* Jeder benutzte Schluessel muss es auch geben.
         *
         * Der Vergleich darueber faengt das nicht: Er haelt die beiden Woerterbuecher
         * gegeneinander, und ein Schluessel, der in BEIDEN fehlt, faellt dabei nicht
         * auf. DynamicResource auf etwas Unbekanntes wirft auch nicht - es bleibt
         * einfach leer. Genau so ist eine Gruppenueberschrift verschwunden, ohne dass
         * irgendwo etwas gemeldet worden waere. */
        var vorhanden = de.ToHashSet();
        foreach (var datei in Directory.GetFiles(Path.Combine("FrameFlip", "Views"), "*.xaml"))
        {
            var benutzt = System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(datei), @"DynamicResource ([A-Za-z]_[A-Za-z0-9]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Where(k => !vorhanden.Contains(k))
                .ToArray();

            Check(benutzt.Length == 0,
                $"{Path.GetFileName(datei)} uses only keys that exist" +
                (benutzt.Length == 0 ? "" : ": " + string.Join(", ", benutzt)));
        }

        /* Der Wortlaut des ffmpeg-Hinweises gehoert hierher.
         *
         * Frueher stand er als deutsche Zeichenkette im FfmpegLocator und wurde dort
         * geprueft - aber der Sucher kennt kein Fenster und kann einen Schluessel
         * nicht aufloesen. Hier sind die Woerterbuecher geladen, also wird hier
         * geprueft, was der Text leisten muss: einen Weg nennen und begruenden,
         * warum ffmpeg nicht mitgeliefert wird. */
        string Text(string datei, string key) => XDocument.Load(Locate(datei)).Root!.Elements()
            .First(e => (string)e.Attribute(x + "Key")! == key).Value;

        foreach (var (datei, grund) in new[] { ("Strings.de.xaml", "GPL"), ("Strings.en.xaml", "GPL") })
        {
            foreach (var key in new[] { "S_FfmpegHintWinget", "S_FfmpegHintManual" })
            {
                string text = Text(datei, key);
                Check(text.Contains(grund), $"{datei}/{key} says why ffmpeg is not bundled");
                Check(text.Length > 60, $"{datei}/{key} offers a way forward");
            }
        }
    }
}
