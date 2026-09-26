using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Remote;
using FrameFlip.Views;
using FrameFlip.Web;

namespace FrameFlip.Tests;

/// <summary>
/// Die Karte der Zuschauerseite in der Kopplungstafel des Dashboards: Schalter, Code,
/// Adresse, Stand, Kennwort und Handgriffe. Die Probe haelt fest, was sie heute tut -
/// bevor ihre Logik in eine eigene Klasse wandert, die auch die Einstellungen benutzen.
///
/// Kein Netz: Der Dienst ist nicht gestartet, und "Link kopieren" wird nicht gedrueckt -
/// die Zwischenablage gehoert dem Rechner, auf dem die Probe laeuft.
/// </summary>
public static class WatchCardInvariants
{
    private static string T(string key) => Localization.Strings.T(key);

    public static void Run()
    {
        ShowsWhatIsThere();
        TogglesThroughTheHost();
        TakesThePassword();
        TheSettingsShowTheSameCard();
        Isolated(TheSettingsLayOut);
        Isolated(TheOverviewMirrorsTheSections);
        ScreensScaleTheSurface();
    }

    /// <summary>
    /// Die Uebersicht (Entwurf 2): Ihre Kacheln sind dieselben Einstellungen wie auf den
    /// Kategorieseiten - wer eine aendert, aendert die andere -, und ein Klick oeffnet die
    /// Kategorie. Die Spalten richten sich nach der Breite.
    /// </summary>
    private static void TheOverviewMirrorsTheSections()
    {
        Check.Group("Einstellungen: die Uebersicht");

        var current = new AppSettings { Fps = 24, Loop = false, MemoryBudgetMb = 2048, BridgeEnabled = false };
        var applied = new List<AppSettings>();
        var layout = new DesktopLayout();
        var editor = new SettingsEditor(current, next => { applied.Add(next); current = next; return null; }, () => null, () => current, layout);
        editor.ConnectWatch(() => null, null, null, then => then(), _ => { });
        editor.ConnectStatus(() => new SettingsStatus("lauscht", "512 von 2048 MB"));

        var window = new Window
        {
            Content = editor, Width = 1400, Height = 900, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Left = -4000, Top = -4000,
        };

        try
        {
            // Die Breite am Editor, nicht am Fenster: Auf einem kleinen Bildschirm - etwa dem
            // der CI - wird ein Fenster nicht so breit, wie man es verlangt.
            editor.Width = 1400;
            window.Show();
            Pump(0.3);

            var tabs = (TabControl)editor.FindName("Tabs");
            Check.That(tabs.SelectedItem == editor.FindName("OverviewTab"), "die Einstellungen beginnen mit der Uebersicht");

            // Die Kacheln spiegeln die Kategorieseiten.
            var tilePlayback = (Border)editor.FindName("TilePlayback");
            var tileLoop = Descendants<CheckBox>(tilePlayback).Single();
            var loop = (CheckBox)editor.FindName("LoopBox");

            tileLoop.IsChecked = true;
            Check.That(loop.IsChecked == true, "Wiederholen in der Kachel ist Wiederholen auf der Seite");

            var tileRate = Descendants<ComboBox>(tilePlayback).Single();
            var rate = (ComboBox)editor.FindName("FpsBox");
            tileRate.SelectedIndex = tileRate.Items.Count - 1;
            Check.That(rate.SelectedItem == tileRate.SelectedItem && rate.SelectedItem is not null, "die Bildrate ebenso");

            var tileBudget = Descendants<TextBox>((Border)editor.FindName("TilePerformance")).First();
            tileBudget.Text = "3072";
            Check.That(((TextBox)editor.FindName("BudgetBox")).Text == "3072", "der Speicher ebenso - schon beim Tippen");

            var tileBridge = Descendants<CheckBox>((Border)editor.FindName("TilePermissions")).First();
            tileBridge.IsChecked = true;
            Check.That(((CheckBox)editor.FindName("BridgeBox")).IsChecked == true, "die Bruecke ebenso");

            Check.That(((CheckBox)editor.FindName("TileWatch")).IsEnabled, "die Zuschauerseite ist schaltbar, sobald ein Dienst verbunden ist");

            // Die Automatik der Skalierung wirkt aus der Kachel, auch wenn der Reiter nie offen war.
            ((CheckBox)editor.FindName("TileAutoScale")).IsChecked = true;
            Check.That(layout.AutoScale && ((TextBlock)editor.FindName("TileScaleValue")).Text.EndsWith("%"),
                       "Nach Bildschirm in der Kachel stellt das Layout um und zeigt den Wert");
            ((CheckBox)editor.FindName("TileAutoScale")).IsChecked = false;
            Check.That(!layout.AutoScale, "und wieder zurueck");

            // Nur die Bildrate traegt eine Einheit.
            Check.That(Equals(((ComboBox)editor.FindName("FpsBox")).Tag, "fps") && ((ComboBox)editor.FindName("LanguageBox")).Tag is null,
                       "die Einheit fps steht an der Bildrate, nicht an der Sprache");

            // Uebernehmen nimmt, was in den Kacheln geaendert wurde.
            ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check.That(applied.Count == 1 && applied[0].Loop && applied[0].MemoryBudgetMb == 3072 && applied[0].BridgeEnabled,
                       "Uebernehmen nimmt die Aenderungen aus den Kacheln");

            // Die Zustandsfelder.
            editor.UpdateStatus();
            Check.That(((TextBlock)editor.FindName("StatusBridge")).Text == "lauscht" &&
                       ((TextBlock)editor.FindName("StatusMemory")).Text == "512 von 2048 MB" &&
                       ((TextBlock)editor.FindName("StatusViewers")).Text == T("S_StatusOff"),
                       "die Zustandsfelder zeigen, was der Wirt meldet - die Zuschauerseite ist aus");

            // Spalten nach Breite.
            var tiles = (System.Windows.Controls.Primitives.UniformGrid)editor.FindName("OverviewTiles");
            int wide = tiles.Columns;
            editor.Width = 820;
            Pump(0.3);
            int middle = tiles.Columns;

            Check.That(wide == 3 && middle < wide, "breit drei Kachelspalten, schmaler weniger", $"{wide} -> {middle}");

            // Ein Klick auf eine Kachel oeffnet ihre Kategorie.
            var header = Descendants<DockPanel>((Border)editor.FindName("TilePerformance")).First();
            header.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            });
            Check.That(tabs.SelectedItem == editor.FindName("PerformanceTab"), "ein Klick auf die Kachel oeffnet ihre Kategorie");
        }
        finally
        {
            window.Close();
            editor.Dispose();
        }
    }

    /// <summary>Die Skalierung nach dem Bildschirm: 1080 Punkte ergeben 100 %, mehr ergibt mehr, in Zwanzigsteln und Grenzen.</summary>
    private static void ScreensScaleTheSurface()
    {
        Check.Group("Einstellungen: Skalierung nach Bildschirm");

        Check.That(DesktopLayout.AutoFor(1080) == 1.0, "1080 Punkte hoch: 100 %");
        Check.That(DesktopLayout.AutoFor(1440) == 1.15, "1440 (etwa 27 Zoll mit 100 % oder 4K mit 150 %): 115 %", $"{DesktopLayout.AutoFor(1440)}");
        Check.That(DesktopLayout.AutoFor(2160) == 1.35, "2160 (4K mit 100 %): 135 %, die Obergrenze", $"{DesktopLayout.AutoFor(2160)}");
        Check.That(DesktopLayout.AutoFor(768) == 0.85, "768: 85 %, die Untergrenze", $"{DesktopLayout.AutoFor(768)}");
        Check.That(DesktopLayout.AutoFor(double.NaN) == 1.0 && DesktopLayout.AutoFor(0) == 1.0, "ohne brauchbare Hoehe: 100 %");

        var layout = new DesktopLayout { AutoScale = true, Scale = 1.2 };
        layout.Normalize();
        Check.That(layout.AutoScale && layout.Scale == 1.2, "die Automatik laesst den festen Wert stehen - fuer den Weg zurueck");
    }

    /// <summary>
    /// Laesst einen Test in einem eigenen Konfigurationsordner laufen. Ein Editor liest und
    /// schreibt sein Layout dort, und nicht in den Einstellungen dessen, der die Probe startet.
    /// </summary>
    private static void Isolated(Action run)
    {
        string root = Path.Combine(Path.GetTempPath(), "frameflip-einstellungen-" + Guid.NewGuid().ToString("N")[..8]);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");

        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(root, "config.json"));

        try
        {
            run();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static void Pump(double seconds)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Die Einstellungen nach Entwurf A: dieselbe Karte unter "Verbindungen", am selben Dienst,
    /// mit derselben Zustimmung - und ohne Wirt gar nicht.
    /// </summary>
    private static void TheSettingsShowTheSameCard()
    {
        Check.Group("Zuschauerkarte: in den Einstellungen");

        using (var host = new Host(terms: true))
        {
            var window = host.Open();

            var key = WatchKey.Create("geheim1");
            host.Settings.WatchEnabled = true;
            host.Settings.WatchSecret = WatchStore.Protect(key);
            host.Service = new WatchService(key, "relay.example", null, () => null, () => null);

            window.ShowSettingsPage();
            var editor = host.SettingsEditor();

            Check.That(editor.Find<FrameworkElement>("WatchCardFrame").Visibility == Visibility.Visible &&
                       editor.Find<ToggleButton>("WatchToggle").IsChecked == true &&
                       editor.Find<QrCodeView>("WatchCode").Text == host.Service.Link &&
                       editor.Find<TextBox>("WatchPass").Text == "geheim1",
                       "unter Verbindungen: die Zuschauerkarte am selben Dienst - Code, Link und Kennwort wie im Dashboard");

            editor.Find<ToggleButton>("WatchToggle").IsChecked = false;
            Check.That(host.Applied.Count == 1 && !host.Applied[0].WatchEnabled, "ihr Schalter wirkt sofort, ueber denselben Wirt");

            // Verwerfen baut die Seite neu - die Karte bleibt verbunden.
            host.Pump();
            editor.Find<Button>("CancelButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var rebuilt = host.SettingsEditor();

            Check.That(!ReferenceEquals(rebuilt.Control, editor.Control) &&
                       rebuilt.Find<FrameworkElement>("WatchCardFrame").Visibility == Visibility.Visible,
                       "nach Verwerfen ist die Karte am neuen Editor wieder da");
        }

        using (var host = new Host(terms: false))
        {
            var window = host.Open();
            window.ShowSettingsPage();
            var editor = host.SettingsEditor();

            editor.Find<ToggleButton>("WatchToggle").IsChecked = true;
            Check.That(host.Applied.Count == 0 && editor.Find<ToggleButton>("WatchToggle").IsChecked == false &&
                       ((FrameworkElement)window.FindName("TermsHost")).Visibility == Visibility.Visible,
                       "ohne Zustimmung fragt auch hier zuerst die Tafel des Hauptfensters");
        }

        // Ein Editor ohne Wirt - etwa ausserhalb des Hauptfensters - kennt keinen Dienst.
        var alone = new SettingsEditor(new AppSettings(), _ => null, () => null, layout: new DesktopLayout());
        Check.That(((FrameworkElement)alone.FindName("WatchCardFrame")).Visibility == Visibility.Collapsed &&
                   ((System.Windows.Controls.Primitives.UniformGrid)alone.FindName("ConnectionCards")).Columns == 1,
                   "ohne Wirt: keine Zuschauerkarte, die Kopplung allein");
        alone.Dispose();
    }

    /// <summary>Die Leiste links, bei schmaler Seite oben; die Karten nebeneinander, wenn sie Platz haben.</summary>
    private static void TheSettingsLayOut()
    {
        Check.Group("Einstellungen: Leiste und Karten");

        var editor = new SettingsEditor(new AppSettings(), _ => null, () => null, layout: new DesktopLayout());
        editor.ConnectWatch(() => null, null, null, then => then(), _ => { });

        var tabs = (TabControl)editor.FindName("Tabs");
        var cards = (System.Windows.Controls.Primitives.UniformGrid)editor.FindName("ConnectionCards");

        void Size(double width)
        {
            editor.Measure(new System.Windows.Size(width, 800));
            editor.Arrange(new Rect(0, 0, width, 800));
            editor.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            editor.UpdateLayout();
        }

        tabs.SelectedItem = editor.FindName("ConnectionsTab");
        Size(1200);
        Check.That(tabs.Tag is null && cards.Columns == 2, "breit: Leiste links, die zwei Karten nebeneinander", $"{tabs.Tag}, {cards.Columns}");

        Size(500);
        Check.That(Equals(tabs.Tag, "Narrow") && cards.Columns == 1, "schmal: Leiste oben, die Karten untereinander", $"{tabs.Tag}, {cards.Columns}");

        Check.That(tabs.Items.Count == 7 && ((TabItem)tabs.Items[0]).Header is string first && first == T("S_TabOverview") &&
                   ((TabItem)tabs.Items[4]).Header is string header && header == T("S_TabConnections"),
                   "sieben Abschnitte: vorn die Uebersicht, Verbindungen an fuenfter Stelle");

        editor.Dispose();
    }

    /// <summary>Aus, an ohne Dienst, an mit Dienst - je ein eigenes Bild.</summary>
    private static void ShowsWhatIsThere()
    {
        Check.Group("Zuschauerkarte: zeigt, was ist");

        using var host = new Host(terms: true);
        var window = host.Open();

        host.Refresh();
        Check.That(host.Toggle.IsChecked == false && host.Tracked("WatchToggleText") == T("D_Off") &&
                   host.Visible("WatchCodeFrame") == false && host.Visible("WatchPassRow") == false &&
                   host.Text("WatchHint") == T("D_WatchOff") && host.Actions.Count == 0,
                   "aus: kein Code, kein Kennwort, keine Handgriffe - und warum", host.Text("WatchHint"));

        host.Settings.WatchEnabled = true;
        host.Refresh();
        Check.That(host.Toggle.IsChecked == true && host.Tracked("WatchToggleText") == T("D_On") &&
                   host.Visible("WatchCodeFrame") == false && host.Text("WatchHint") == T("D_WatchNoRelay"),
                   "an, aber ohne Dienst: der Hinweis auf die Relay-Adresse", host.Text("WatchHint"));

        var key = WatchKey.Create("geheim1");
        host.Settings.WatchSecret = WatchStore.Protect(key);
        host.Service = new WatchService(key, "relay.example", null, () => null, () => null);
        host.Refresh();

        string link = host.Service.Link;

        Check.That(host.Visible("WatchCodeFrame") == true && ((QrCodeView)window.FindName("WatchCode")).Text == link &&
                   host.Text("WatchAddress") == link,
                   "an mit Dienst: der Code und die Adresse sind der Link");
        Check.That(host.Text("WatchHint").StartsWith(T("D_WatchOn")) && host.Text("WatchHint").Contains(T("D_WatchNobody")),
                   "der Stand in Worten: an, niemand sieht zu, freie Plaetze", host.Text("WatchHint"));
        Check.That(host.Visible("WatchPassRow") == true && host.Pass.Text == "geheim1" && host.Text("WatchPassHint") == T("D_WatchPassHint"),
                   "das Kennwort steht sichtbar im Feld");
        Check.That(host.Actions.Count == 2, "zwei Handgriffe: Link kopieren und erneuern", string.Join(", ", host.Actions.Select(Label)));

        // Erneuern ruft den Wirt.
        host.Actions[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check.That(host.Renewed == 1, "Erneuern bittet den Wirt um einen neuen Link");
    }

    /// <summary>Der Schalter geht durch den Wirt - und fragt vorher nach der Zustimmung.</summary>
    private static void TogglesThroughTheHost()
    {
        Check.Group("Zuschauerkarte: der Schalter");

        using (var host = new Host(terms: false))
        {
            var window = host.Open();
            host.Refresh();

            host.Toggle.IsChecked = true;
            Check.That(host.Applied.Count == 0 && host.Toggle.IsChecked == false &&
                       ((FrameworkElement)window.FindName("TermsHost")).Visibility == Visibility.Visible,
                       "ohne Zustimmung: nichts uebernommen, der Schalter springt zurueck, die Tafel fragt");
        }

        using (var host = new Host(terms: true))
        {
            host.Open();
            host.Refresh();

            host.Toggle.IsChecked = true;
            Check.That(host.Applied.Count == 1 && host.Applied[0].WatchEnabled && !ReferenceEquals(host.Applied[0], host.Before),
                       "mit Zustimmung: eingeschaltet uebernommen - als Kopie, nicht am Bestand");

            host.Pump();
            host.Toggle.IsChecked = false;
            Check.That(host.Applied.Count == 2 && !host.Applied[1].WatchEnabled, "und wieder aus");

            // Ein Fehler des Wirts steht in der Tafel.
            host.Error = "geht nicht";
            host.Pump();
            host.Toggle.IsChecked = true;
            Check.That(host.Text("PairHint") == "geht nicht", "ein Fehler beim Uebernehmen steht im Hinweis der Tafel", host.Text("PairHint"));

            // Die Anzeige des Bestands ist kein neuer Auftrag.
            host.Error = null;
            int before = host.Applied.Count;
            host.Refresh();
            Check.That(host.Applied.Count == before, "den Bestand zu zeigen uebernimmt nichts");
        }
    }

    /// <summary>Das Kennwort: zu kurz abgewiesen, gesetzt, geleert - und unveraendert gar nicht.</summary>
    private static void TakesThePassword()
    {
        Check.Group("Zuschauerkarte: das Kennwort");

        using var host = new Host(terms: true);
        host.Open();

        var key = WatchKey.Create("geheim1");
        host.Settings.WatchEnabled = true;
        host.Settings.WatchSecret = WatchStore.Protect(key);
        host.Service = new WatchService(key, "relay.example", null, () => null, () => null);
        host.Refresh();

        host.Leave();
        Check.That(host.Codes.Count == 0, "unveraendert verlassen: nichts gesetzt");

        host.Pass.Text = "ab";
        host.Leave();
        Check.That(host.Codes.Count == 0 && host.Pass.Text == "geheim1", "zu kurz: abgewiesen, das alte steht wieder da");

        host.Pass.Text = "  neu-kennwort ";
        host.Enter();
        Check.That(host.Codes.SequenceEqual(new[] { "neu-kennwort" }), "Enter setzt das neue, ohne Leerraum am Rand",
                   string.Join(", ", host.Codes));

        host.Pass.Text = "";
        host.Leave();
        Check.That(host.Codes.Count == 2 && host.Codes[1] is null, "leer verlassen: kein Kennwort mehr");
    }

    private static string Label(Button button) => (button.Content as TextBlock)?.Text ?? "";

    /// <summary>Ein Stueck Oberflaeche mit eigenem Namensraum.</summary>
    private sealed record Part(FrameworkElement Control)
    {
        internal T Find<T>(string name) where T : class => (T)Control.FindName(name);
    }

    /// <summary>Ein Hauptfenster mit einem Wirt, der mitschreibt.</summary>
    private sealed class Host : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "frameflip-zuschauer-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string? _previousConfig = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        private readonly SynchronizationContext? _previousContext = SynchronizationContext.Current;
        private MainWindow? _window;

        internal AppSettings Settings { get; private set; }
        internal AppSettings Before { get; }
        internal List<AppSettings> Applied { get; } = new();
        internal List<string?> Codes { get; } = new();
        internal WatchService? Service { get; set; }
        internal string? Error { get; set; }
        internal int Renewed { get; private set; }

        internal Host(bool terms)
        {
            Directory.CreateDirectory(_root);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(_root, "config.json"));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

            Settings = new AppSettings { Prebuffer = false, TermsAccepted = terms ? AppSettings.TermsVersion : 0 };
            Before = Settings;
        }

        internal MainWindow Open() => _window = new MainWindow(null, () => null, () => { }, _ => { }, () => { }, Settings,
            _ => { }, Apply, () => Settings, () => Service, () => Renewed++, code => Codes.Add(code));

        private string? Apply(AppSettings next)
        {
            Applied.Add(next);
            if (Error is not null) return Error;

            Settings = next;
            return null;
        }

        internal ToggleButton Toggle => (ToggleButton)_window!.FindName("WatchToggle");
        internal TextBox Pass => (TextBox)_window!.FindName("WatchPass");
        internal List<Button> Actions => ((Panel)_window!.FindName("WatchActions")).Children.OfType<Button>().ToList();

        internal string Text(string name) => ((TextBlock)_window!.FindName(name)).Text;

        /// <summary>Der Editor der Einstellungsseite, die gerade im Hauptfenster steht.</summary>
        internal Part SettingsEditor()
        {
            Pump();
            var page = (SettingsPage)((ContentControl)_window!.FindName("PageContent")).Content;
            return new Part((FrameworkElement)((ContentControl)page.FindName("EditorHost")).Content);
        }

        /// <summary>Was ein Schild zeigen soll - es tippt sich ein, die Absicht steht in Track.Text.</summary>
        internal string? Tracked(string name) => FrameFlip.Views.Track.GetText((TextBlock)_window!.FindName(name));

        internal bool Visible(string name) => ((FrameworkElement)_window!.FindName(name)).Visibility == Visibility.Visible;

        internal void Refresh() => Invoke("RefreshWatch");

        internal void Leave() => Invoke("OnWatchPassDone", Pass, new RoutedEventArgs());

        internal void Enter() => Invoke("OnWatchPassKey", Pass,
            new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });

        private void Invoke(string name, params object[] args)
            => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_window, args);

        internal void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public void Dispose()
        {
            try
            {
                _window?.Close();
                Pump();
                try { Directory.Delete(_root, true); } catch (IOException) { }
            }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", _previousConfig);
                SynchronizationContext.SetSynchronizationContext(_previousContext);
            }
        }
    }

    /// <summary>Eine Quelle fuer den Tastendruck - ohne Fenster.</summary>
    private sealed class FakeSource : PresentationSource
    {
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
    }
}
