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
        TheSettingsLayOut();
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
        var alone = new SettingsEditor(new AppSettings(), _ => null, () => null);
        Check.That(((FrameworkElement)alone.FindName("WatchCardFrame")).Visibility == Visibility.Collapsed &&
                   ((System.Windows.Controls.Primitives.UniformGrid)alone.FindName("ConnectionCards")).Columns == 1,
                   "ohne Wirt: keine Zuschauerkarte, die Kopplung allein");
        alone.Dispose();
    }

    /// <summary>Die Leiste links, bei schmaler Seite oben; die Karten nebeneinander, wenn sie Platz haben.</summary>
    private static void TheSettingsLayOut()
    {
        Check.Group("Einstellungen: Leiste und Karten");

        var editor = new SettingsEditor(new AppSettings(), _ => null, () => null);
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

        tabs.SelectedIndex = 3;
        Size(1200);
        Check.That(tabs.Tag is null && cards.Columns == 2, "breit: Leiste links, die zwei Karten nebeneinander", $"{tabs.Tag}, {cards.Columns}");

        Size(500);
        Check.That(Equals(tabs.Tag, "Narrow") && cards.Columns == 1, "schmal: Leiste oben, die Karten untereinander", $"{tabs.Tag}, {cards.Columns}");

        Check.That(tabs.Items.Count == 6 && ((TabItem)tabs.Items[3]).Header is string header && header == T("S_TabConnections"),
                   "sechs Abschnitte, der vierte heisst Verbindungen");

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
