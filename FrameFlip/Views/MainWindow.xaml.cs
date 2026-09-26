using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Bridge;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Dashboard;
using FrameFlip.Export;
using FrameFlip.Diagnostics;
using FrameFlip.Localization;
using FrameFlip.Remote;
using FrameFlip.Playback;
using FrameFlip.Projects;
using FrameFlip.Sequencing;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Das Dashboard.
///
/// Aufbau nach dem Entwurf FrameFlipDesktop: drei Spalten - Sequenzen links, der
/// Frame mit Zeitleiste und Filmstreifen in der Mitte, Metriken, Protokoll und
/// Luecken rechts. Darueber eine Titelzeile, die zugleich die Fensterleiste ist,
/// darunter eine Statuszeile.
///
/// Projekte und Einstellungen sind Reiter in der Titelzeile - dort, wo der Entwurf
/// die Menues zeigt. Sie legen sich ueber die drei Spalten, statt sie zu ersetzen:
/// Wer zurueckwechselt, findet dieselbe Sequenz an derselben Stelle wieder.
///
/// Auswahl, Ordnerbeobachtung, Bildspeicher, Videovorbereitung und Wiedergabe haben
/// eigene Controller. Das Fenster verbindet deren Entscheidungen mit Zeitgebern,
/// WPF-Eingaben und Anzeigen.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Zellenbreite des Filmstreifens samt Abstand, wie im Entwurf.</summary>
    private const double StripCellWidth = 72;

    /// <summary>Kaesten im Lueckenraster: zwoelf Spalten, acht Reihen.</summary>
    private const int GapCells = 96;

    private readonly RenderMonitor? _monitor;
    private readonly Func<RelayState?> _remoteState;
    private readonly Action<string> _openSequence;
    private readonly Func<AppSettings, string?> _apply;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings>? _persist;
    private readonly DesktopLayout _layout;

    /// <summary>Woher die Adresse der Zusehen-Seite kommt. Leer, wenn sie nicht laeuft.</summary>
    private readonly Func<Web.WatchService?> _watch;
    private readonly Action? _renewWatch;
    private readonly Action<string?>? _setWatchCode;

    /// <summary>Die Karte der Zuschauerseite in der Kopplungstafel - ihre Logik steht in <see cref="WatchCard"/>.</summary>
    private WatchCard? _watchCard;

    private readonly FrameDecoderRegistry _decoders = FrameDecoderRegistry.CreateDefault();
    private readonly DashboardFrameController _frames;
    private readonly DashboardVideoController _videos;

    private readonly DispatcherTimer _ticker;
    private readonly DispatcherTimer _player;

    private readonly List<(ToggleButton Button, DashboardSequenceEntry Entry)> _sequenceButtons = new();

    private readonly DashboardSequenceController _sequences;
    private readonly List<string> _log = new();
    private readonly List<MetricTile> _tiles = new();

    private ImageSequence? _sequence => _sequences.Sequence;
    private DashboardSequenceEntry? _current => _sequences.Current;

    /// <summary>Die zuletzt gesehene Auftragsnummer - daran haengt der Neuaufbau der Liste.</summary>
    private string _lastJobId = string.Empty;
    private IReadOnlyList<int> _missing = Array.Empty<int>();

    /// <summary>Kopf, Bereich, Abspielzustand, Follow und Bildrate.</summary>
    private readonly DashboardPlaybackController _playback;

    private bool _scrubbing;

    /// <summary>Ob Abspielen erst vorauslaedt. Wird mit den Einstellungen gemerkt.</summary>
    private bool _prebuffer = true;

    /// <summary>Ob beim Vorausladen nebenher ein Video entsteht.</summary>
    private bool _prepareVideo;

    /// <summary>Format und Farbtiefe der Sequenz - einmal ermittelt, dann angezeigt.</summary>
    private string _format = string.Empty;

    private string _page = "dashboard";
    private string _panel = "metrics";

    private ToggleButton? _loopChip;

    /// <summary>Der Zeiger auf der Zeitleiste - wandert, statt neu zu entstehen.</summary>
    private Rectangle? _headMark;

    /// <summary>Ordnerbeobachtung und Ruhefrist der aktuell gewaehlten Folge.</summary>
    private readonly DashboardLiveController _live;

    /// <summary>Der zuletzt gemeldete Ladestand - fuer den Balken beim Groessenwechsel.</summary>
    private PreloadProgress? _lastPreload;

    /// <summary>
    /// Steht erst am Ende des Konstruktors auf true.
    ///
    /// Beim Fuellen der Ratenliste meldet die ComboBox eine Auswahl, und der
    /// Handler lief damit los, bevor es die Zeitgeber ueberhaupt gab. Ein Feld
    /// zu fuellen darf nicht dasselbe ausloesen wie eine Wahl des Benutzers.
    /// </summary>
    private bool _ready;

    /// <summary>
    /// Zwei Rueckrufe kommen herein und werden nicht gebraucht: <paramref name="showSettings"/>
    /// und <paramref name="showPairing"/> oeffneten frueher eigene Fenster. Beides
    /// liegt jetzt im Dashboard - die Einstellungen als Reiter, der Kopplungscode
    /// als Tafel. Die Unterschrift bleibt trotzdem unveraendert, weil der Wirt sie
    /// so aufruft und ein zweiter Umbau an dieser Stelle nichts besser machte.
    /// </summary>
    public MainWindow(RenderMonitor? monitor, Func<RelayState?> remoteState, Action showSettings,
        Action<string> openSequence, Action showPairing, AppSettings? settings = null,
        Action<AppSettings>? persist = null, Func<AppSettings, string?>? applySettings = null,
        Func<AppSettings>? getSettings = null, Func<Web.WatchService?>? watch = null,
        Action? renewWatch = null, Action<string?>? setWatchCode = null)
        : this(DashboardFrameSources.Default, monitor, remoteState, showSettings, openSequence, showPairing,
            settings, persist, applySettings, getSettings, watch, renewWatch, setWatchCode)
    {
    }

    internal MainWindow(DashboardFrameSources frameSources, RenderMonitor? monitor, Func<RelayState?> remoteState,
        Action showSettings, Action<string> openSequence, Action showPairing, AppSettings? settings = null,
        Action<AppSettings>? persist = null, Func<AppSettings, string?>? applySettings = null,
        Func<AppSettings>? getSettings = null, Func<Web.WatchService?>? watch = null,
        Action? renewWatch = null, Action<string?>? setWatchCode = null, DashboardVideoSources? videoSources = null)
    {
        _frames = new DashboardFrameController(frameSources, DispatchFrame, ShowDecodedFrame,
            ShowPreloadProgress, () => DecodeWidth(atLeast: 960), CurrentPace);
        _watch = watch ?? (() => null);
        _renewWatch = renewWatch;
        _setWatchCode = setWatchCode;

        _monitor = monitor;
        _videos = new DashboardVideoController(videoSources ?? DashboardVideoSources.Default,
            () => _monitor?.Job?.IsRunning == true
                ? System.Diagnostics.ProcessPriorityClass.Idle
                : System.Diagnostics.ProcessPriorityClass.BelowNormal);
        _remoteState = remoteState;
        _openSequence = openSequence;
        _persist = persist;

        AppSettings current = settings ?? new AppSettings();
        _getSettings = getSettings ?? (() => current);

        _apply = next =>
        {
            /* Ohne Zustimmung nach draussen: nicht nur melden, sondern auch fragen.
             *
             * Der Riegel selbst sitzt in AppSettings.Normalize und ist dicht. Hier geht
             * es um den Weg dorthin. Die Tafel ist der einzige Ort, an dem die
             * Bedingungen ueberhaupt verlinkt sind - und sie hing an genau einem
             * Schalter. Wer die Kopplung benutzte oder den Haken in den Einstellungen,
             * las "ohne Zustimmung geht das nicht" und fand die Bedingungen nirgends.
             *
             * Deshalb steht das hier und nicht an den Aufrufstellen: Jede einzeln zu
             * versorgen hiesse, die naechste zu vergessen. Alles, was Einstellungen
             * uebernimmt - Schalter, Kopplung, Einstellungsseite - geht durch diese
             * eine Tuer.
             *
             * TermsHost kann noch fehlen: Der Aufruf hier steht vor InitializeComponent. */
            if ((next.RemoteEnabled || next.WatchEnabled)
                && !_getSettings().TermsOk
                && !next.TermsOk)
            {
                if (TermsHost is { Visibility: not Visibility.Visible })
                    AskTerms(Wiederholung(next), next.RelayHost);
                // Den angeforderten Stand erst nach Zustimmung an den Wirt geben.
                // Auch eine Vorschau oder ein anderer Apply-Rueckruf darf vorher
                // weder speichern noch den Entwurf durch Normalize veraendern.
                return Strings.T("S_TermsMissing");
            }

            var error = applySettings?.Invoke(next);
            if (error is not null) return error;

            // Ein von Hand eingetragener ffmpeg-Pfad aendert die Antwort auf die
            // Frage, ob es gefunden wird. Die gemerkte Antwort gilt dann nicht mehr.
            ForgetFfmpeg();

            current = next;

            if (applySettings is null)
            {
                persist?.Invoke(next);
                Strings.Apply(Strings.Parse(next.Language));
            }

            return null;
        };

        _sequences = new DashboardSequenceController(DashboardSequenceSources.Default(_decoders),
            action => Dispatcher.BeginInvoke(action), RefreshSequenceItem);
        _playback = new DashboardPlaybackController(() => _sequences.Sequence);
        _live = new DashboardLiveController(DashboardLiveSources.Default(Dispatcher), _decoders.IsSupported, RescanLive);
        _layout = DesktopLayout.Load();

        InitializeComponent();

        _watchCard = new WatchCard(
            new WatchCard.Parts(WatchToggle, WatchToggleText, WatchCodeFrame, WatchCode, WatchAddress, WatchHint,
                                WatchPassRow, WatchPass, WatchPassHint, WatchActions),
            new WatchCard.Host(_getSettings, _apply, _watch, _renewWatch, _setWatchCode, () => _layout.LightQr, () => _ready,
                               then => AskTerms(then), Note, error => PairHint.Text = error, PairAction, CopyInvite));

        ApplyScale();
        InitializeDashboardLayout();

        if (settings is not null)
        {
            WindowPlacer.Restore(this, settings.MainLeft, settings.MainTop,
                                 settings.MainWidth, settings.MainHeight, settings.MainMaximized);
        }

        SourceInitialized += (_, _) =>
        {
            WindowPlacer.EnsureVisible(this);
            ShellChrome.Attach(this);
        };

        StateChanged += (_, _) => RefreshMaximizeGlyph();
        PreviewKeyDown += OnWindowKeyDown;
        LocationChanged += (_, _) => Remember();

        SizeChanged += (_, _) =>
        {
            ApplyScale();
            Remember();
        };

        BuildPlayChips();
        BuildRates();
        BuildRemoteActions();
        LoadSequences();

        // Ein Takt fuer alles, was sich langsam aendert. Der Renderzustand kommt
        // ohnehin nur im Sekundenrhythmus herein.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => Refresh();
        _ticker.Start();

        _player = new DispatcherTimer { Interval = _playback.Interval };
        _player.Tick += (_, _) => Advance();

        if (_monitor is not null)
        {
            _monitor.FrameWritten += OnFrameWritten;
            Closed += (_, _) => _monitor.FrameWritten -= OnFrameWritten;
        }

        Strings.Changed += OnLanguageChanged;

        // Die Skalierung sitzt in den Einstellungen - und die liegen als Reiter im
        // selben Fenster. Ohne das hier wuerde der Regler erst beim naechsten Start
        // wirken, waehrend der Benutzer direkt danebensteht und zusieht.
        _layout.Changed += OnLayoutChanged;

        Closed += (_, _) =>
        {
            _frames.Dispose();
            _videos.Dispose();
            Strings.Changed -= OnLanguageChanged;
            _layout.Changed -= OnLayoutChanged;
            _settingsPage?.Dispose();
            _ticker.Stop();
            _player.Stop();
            _live.Dispose();
            _sequences.Dispose();
            CancelPreload();
            DropCache();
        };

        _ready = true;

        Note(Strings.T("D_LogStarted"));
        RefreshMaximizeGlyph();
        Refresh();
    }

    // ================================================================ Titelzeile

    /// <summary>Fenstermass, auf das der Entwurf gezeichnet ist.</summary>
    private const double ReferenceWidth = 1440;

    private const double ReferenceHeight = 900;

    /// <summary>
    /// Wie weit die Oberflaeche schrumpfen darf.
    ///
    /// Die drei Spalten brauchen zusammen rund 640 Punkte, das kleinste erlaubte
    /// Fenster ist 900 breit. 0,7 laesst also auch dort noch Luft - und tiefer
    /// duerfte es ohnehin nicht gehen, ohne dass die Beschriftungen aufhoeren,
    /// Beschriftungen zu sein.
    /// </summary>
    private const double MinScale = 0.7;

    private const double MaxScale = 1.25;

    /// <summary>
    /// Was die drei Spalten zusammen mindestens brauchen - samt Griffen und Raendern.
    ///
    /// 150 + 280 + 205 fuer die Spalten, dazu die beiden Ziehgriffe und etwas Luft.
    /// Unterhalb dieser Breite hilft nur noch Verkleinern; oberhalb ist Verkleinern
    /// reine Verschlechterung.
    /// </summary>
    private const double NeededWidth = 700;

    /// <summary>Titelzeile, Haarstrich, Statuszeile, Kopfzeile, Transport und etwas Buehne.</summary>
    private const double NeededHeight = 520;

    /// <summary>
    /// Die Stufe zur Fenstergroesse.
    ///
    /// SKALIERT WIRD NUR, WENN ES SEIN MUSS. Jeder krumme Faktor zieht Schrift und
    /// Linien zwischen die Bildpunkte, und das sieht man. Massgeblich ist deshalb
    /// nicht, wie das Fenster zum Entwurfsmass steht, sondern ob der Inhalt
    /// hineinpasst - und er passt bis hinunter zur kleinsten erlaubten Fenstergroesse.
    ///
    /// Der erste Anlauf verglich mit dem Entwurfsmass von 1440x900 und verkleinerte
    /// alles darunter. Ein Fenster von 1400x900 Punkten - auf einem Full-HD-Schirm
    /// voellig normal - landete damit bei 0,95 und war grob, obwohl die Spalten mit
    /// 640 Punkten ausgekommen waeren.
    ///
    /// Die Bildschirmdichte ist hier schon heraus: Gezaehlt wird in
    /// geraeteunabhaengigen Punkten, um die Dichte kuemmert sich Windows selbst.
    /// </summary>
    private static double ElasticFor(double width, double height)
    {
        if (width <= 0 || height <= 0) return 1.0;

        // Deutlich groesser als der Entwurf: eine Stufe hoeher, und dabei bleibt es.
        if (Math.Min(width / ReferenceWidth, height / ReferenceHeight) >= 1.35) return MaxScale;

        double fit = Math.Min(width / NeededWidth, height / NeededHeight);

        // Es passt - also unangetastet lassen. Das ist der Normalfall.
        if (fit >= 1.0) return 1.0;

        // Es passt nicht: in Zwanzigstelschritten so weit verkleinern, dass es passt.
        return Math.Clamp(Math.Floor(fit * 20) / 20, MinScale, 1.0);
    }

    /// <summary>
    /// Die Oberflaeche mit dem Fenster mitwachsen lassen.
    ///
    /// Die Spalten teilen sich den Platz bereits nach Anteilen, aber Schrift,
    /// Abstaende und Knopfgroessen taten das nicht: Auf einem 4K-Schirm sass das
    /// Dashboard als Briefmarkenschrift in einer riesigen Flaeche, und in einem
    /// schmalen Fenster stiessen die Spalten an ihre Mindestbreiten und zerbrachen
    /// das Layout.
    ///
    /// Der Faktor folgt der KLEINEREN der beiden Richtungen - sonst waere ein breites,
    /// flaches Fenster vergroessert worden, bis unten etwas herausfaellt.
    ///
    /// Die Grenzen sind eng gesetzt. Eine Oberflaeche, die sich ueber den Daumen
    /// verdoppelt, ist keine Oberflaeche mehr, sondern ein Zoom - und der Regler in
    /// den Einstellungen bleibt das Wort des Benutzers: Sein Wert wird multipliziert,
    /// nicht ersetzt.
    /// </summary>
    private void ApplyScale()
    {
        if (UiScale is null) return;

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;

        double elastic = 1;

        if (width > 0 && height > 0 && !double.IsNaN(width) && !double.IsNaN(height))
            elastic = ElasticFor(width, height);

        double scale = Math.Min(_layout.Scale * elastic, Math.Min(width / NeededWidth, height / NeededHeight));

        // Unter einem Promille sieht niemand etwas, aber jede Zuweisung stoesst ein
        // neues Layout an - und SizeChanged feuert waehrend des Ziehens dauernd.
        if (Math.Abs(UiScale.ScaleX - scale) < 0.001) return;

        UiScale.ScaleX = UiScale.ScaleY = scale;

        // Schrift und Skalierung vertragen sich nur in eine Richtung.
        //
        // "Display" rastet die Buchstaben aufs Pixelraster - bei kleiner Schrift die
        // schaerfste Darstellung, die WPF kann. Gerastet wird aber VOR der
        // Transformation, und was danach um einen krummen Faktor vergroessert wird,
        // ist genau das, was auf einem Full-HD-Schirm grob aussah. "Ideal" rechnet
        // mit den Umrissen und traegt jede Skalierung sauber.
        //
        // Also: unskaliert rasten, skaliert rechnen.
        bool crisp = Math.Abs(scale - 1.0) < 0.001;

        TextOptions.SetTextFormattingMode(this, crisp ? TextFormattingMode.Display : TextFormattingMode.Ideal);

        // Die Titelzeile ist 44 Punkte hoch - im skalierten Inhalt. Die Ziehflaeche
        // des Fensterrahmens zaehlt dagegen in unskalierten Punkten. Ohne diese
        // Zeile laege bei 1,25 ein Streifen der sichtbaren Leiste ausserhalb des
        // Griffs: Man zieht die Leiste an, und das Fenster bleibt liegen.
        System.Windows.Shell.WindowChrome.GetWindowChrome(this).CaptionHeight = 44 * scale;
    }


    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    /// <summary>Das Zeichen zeigt, was der Klick tut - nicht, wie das Fenster steht.</summary>
    private void RefreshMaximizeGlyph()
    {
        bool max = WindowState == WindowState.Maximized;

        MaximizeGlyph.Width = max ? 8 : 10;
        MaximizeGlyph.Height = max ? 8 : 10;
        MaximizeButton.ToolTip = TryFindResource(max ? "D_Restore" : "D_Maximize");
    }

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string key) return;
        if (DashboardBody is null) return;

        ShowPage(key);
    }

    /// <summary>
    /// Die Atelierseite wird gehalten, nicht bei jedem Wechsel neu gebaut: Auf ihr
    /// steht ein geoeffnetes Bild samt aller Reglerstellungen, und das waere nach
    /// einem Blick in die Einstellungen sonst weg.
    /// </summary>
    private AtelierPage? _atelierPage;

    private void ShowPage(string key)
    {
        _page = key;

        // Eine Tafel, die ueber der Uebersicht liegt, hat auf den Einstellungen
        // nichts zu suchen - und beim Zurueckwechseln nicht wieder aufzutauchen.
        PairHost.Visibility = Visibility.Collapsed;

        bool dashboard = key == "dashboard";

        DashboardBody.Visibility = dashboard ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Visibility = dashboard ? Visibility.Collapsed : Visibility.Visible;

        if (dashboard)
        {
            PageContent.Content = null;
            // Die eingeklappte Dashboard-Fläche hat während der Einstellungen
            // keine nutzbaren Maße. Erst nach dem Einblenden neu aufteilen.
            Dispatcher.BeginInvoke(new Action(ApplyDashboardLayout), DispatcherPriority.Loaded);
            return;
        }

        AnimatePageChange();
        PageContent.Content = key switch
        {
            "projects" => new ProjectsPage(OpenFromProjects),
            "atelier" => _atelierPage ??= new AtelierPage(_decoders, _getSettings(), next => _persist?.Invoke(next)),
            _ => _settingsPage ??= CreateSettingsPage(),
        };
    }

    /// <summary>
    /// Die Einstellungsseite - mit der Karte der Zuschauerseite am selben Dienst, derselben
    /// Zustimmung und demselben Protokoll wie die Kopplungstafel.
    /// </summary>
    private SettingsPage CreateSettingsPage()
    {
        var page = new SettingsPage(_getSettings, _apply, _remoteState, _layout);
        page.ConnectWatch(_watch, _renewWatch, _setWatchCode, then => AskTerms(then), Note);
        return page;
    }

    /// <summary>
    /// Von aussen auf die Einstellungen schalten - der Weg, den AppHost geht, wenn
    /// im Ablagebereich "Einstellungen" gewaehlt wird oder ein Kopplungscode her soll.
    /// Kein zweites Fenster: Der Reiter wechselt, das Fenster bleibt eines.
    /// </summary>
    public void ShowSettingsPage(bool pairing = false)
    {
        NavSettings.IsChecked = true;

        if (pairing && PageContent.Content is SettingsPage page)
        {
            // Erst nach dem Layout - vorher hat die Seite noch keine Bildlaufhoehe,
            // und der Sprung zum Kopplungsabschnitt liefe ins Leere.
            Dispatcher.BeginInvoke(new Action(page.SelectRemote), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Aus der Projektliste heraus oeffnen - auch das, was dort "zusammenfuegen"
    /// heisst: einzelne Bilder als eine Sequenz lesen.
    ///
    /// Vorher wurde hier nur unter den protokollierten Renders gesucht, und lose
    /// Bilder sind dort nie zu finden. Der Aufruf lief dann in den Rueckruf nach
    /// aussen, der in dieser Kopie leer ist - der Knopf tat also nichts. Jetzt
    /// landet jeder Pfad auf der Buehne, und zwar auf derselben, die daneben steht.
    /// </summary>
    private void OpenFromProjects(string path)
    {
        NavDashboard.IsChecked = true;

        if (Select(path)) return;
        if (OpenPath(path)) return;

        _openSequence(path);
    }

    // ================================================================ Sequenzen

    private void LoadSequences() => LoadSequenceList(keepSelection: false);

    private void LoadSequenceList(bool keepSelection)
    {
        _sequences.Reload(keepSelection);
        SequenceList.Children.Clear();
        _sequenceButtons.Clear();
        foreach (var entry in _sequences.Entries)
        {
            var button = BuildSequenceItem(entry);
            SequenceList.Children.Add(button);
            _sequenceButtons.Add((button, entry));
        }
        if (_sequences.Entries.Count == 0)
        {
            SequenceList.Children.Add(new TextBlock
            {
                Text = Strings.T("D_NoRenders"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 10, 8, 0),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("DesktopFaint"),
            });
        }
        ApplySequenceSelection();
    }

    private ToggleButton BuildSequenceItem(DashboardSequenceEntry entry)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 46,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("DesktopSubtle"),
            BorderBrush = (Brush)FindResource("DesktopLine"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.Adhoc ? "IMG" : "BL",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 8.5,
                Foreground = (Brush)FindResource("DesktopFaint"),
            },
        };

        var texts = new StackPanel { Margin = new Thickness(9, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };

        texts.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontFamily = (FontFamily)FindResource("HeadFont"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("DesktopInk"),
        });

        var meta = new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("DesktopMuted"),
        };

        texts.Children.Add(meta);

        var tag = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 4, 7, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("HeadFont"),
                FontWeight = FontWeights.ExtraBold,
                FontSize = 8.5,
            },
        };

        Grid.SetColumn(thumb, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(tag, 2);
        row.Children.Add(thumb);
        row.Children.Add(texts);
        row.Children.Add(tag);

        var button = new ToggleButton
        {
            Style = (Style)FindResource("DashSequenceItem"),
            Content = row,
            Tag = entry,
            ToolTip = entry.HasOutput ? entry.Folder : entry.BlendPath,
        };

        // Der Inhalt ist ein Raster aus drei Teilen, kein Text - daraus leitet WPF
        // keinen Namen ab, und die Zeile stand fuer eine Sprachausgabe als
        // namenloser Schalter da.
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);

        button.Checked += (_, _) => Select(entry);
        button.Click += (_, _) => button.IsChecked = true;

        button.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowSequenceMenu(button, entry);
        };

        Dress(entry, meta, tag);
        return button;
    }

    /// <summary>Beschriftung und Marke einer Zeile - getrennt, weil sie nachgetragen werden.</summary>
    private void Dress(DashboardSequenceEntry entry, TextBlock meta, Border tag)
    {
        meta.Text = entry.HasOutput
            ? Path.GetFileName(entry.Folder.TrimEnd('\\', '/')) + " \u00b7 " + Ago(entry.SeenUtc)
            : Strings.T("D_NoOutput");

        bool warn = entry.Missing > 0;
        var label = (TextBlock)tag.Child;

        label.Text = entry.Frames is { } frames
            ? warn ? Strings.T("D_TagGaps") : frames.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        tag.BorderBrush = (Brush)FindResource(warn ? "DesktopWarn" : "DesktopLine");
        tag.Visibility = label.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        label.Foreground = (Brush)FindResource(warn ? "DesktopWarn" : "DesktopMuted");
    }

    private void RefreshSequenceItem(DashboardSequenceEntry entry)
    {
        foreach (var (button, other) in _sequenceButtons)
        {
            if (!ReferenceEquals(other, entry)) continue;
            if (button.Content is not Grid row) continue;

            var texts = row.Children.OfType<StackPanel>().FirstOrDefault();
            var tag = row.Children.OfType<Border>().LastOrDefault();

            if (texts is { Children.Count: > 1 } && texts.Children[1] is TextBlock meta && tag is not null)
                Dress(entry, meta, tag);

            return;
        }
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        if (span < TimeSpan.FromHours(20)) return Strings.T("D_Today");
        if (span < TimeSpan.FromDays(2)) return Strings.T("D_Yesterday");

        return utc.ToLocalTime().ToString("dd.MM.", CultureInfo.CurrentCulture);
    }

    private bool Select(string seedOrFolder)
    {
        var entry = _sequences.Find(seedOrFolder);
        if (entry is null) return false;
        Select(entry);
        return true;
    }

    private void Select(DashboardSequenceEntry entry)
    {
        if (_sequences.Select(entry)) ApplySequenceSelection();
    }

    private void ApplySequenceSelection()
    {
        Pause();
        DropCache();
        foreach (var (button, other) in _sequenceButtons)
            button.IsChecked = ReferenceEquals(other, _current);

        if (_current is not { } entry)
        {
            _live.WatchFolder(null);
            ShowEmptyStage();
            return;
        }

        if (_sequence is null || _sequence.Count == 0)
        {
            ShowEmptyStage();
            _live.WatchFolder(entry.Folder);

            // Der Name bleibt stehen, auch ohne Bild: Die Zeile ist ausgewaehlt,
            // und eine leere Kopfzeile sieht aus wie ein Fehler statt wie ein
            // Render, der noch nichts geschrieben hat.
            SequenceName.Text = entry.Name;
            StageEmpty.Text = Strings.T(entry.HasOutput ? "D_StageNoFrames" : "D_StageNoOutput");
            return;
        }

        StageEmpty.Text = Strings.T("D_StageEmpty");
        SequenceName.Text = entry.Name;

        _missing = _sequence.MissingNumbers();
        _playback.ResetRange(_sequence);

        RefreshSequenceItem(entry);

        Note(Strings.T("D_LogOpened", entry.Name, _sequence.Count));

        _live.WatchFolder(entry.Folder);
        ShapeStage();
        BuildStrip();
        BuildGaps();
        DrawScrub();
        ShowFrame(_sequence.StartNumber);
        Refresh();
    }

    /// <summary>
    /// Die Liste neu einlesen und wieder auf dasselbe stellen wie zuvor.
    ///
    /// Ohne das Merken spraenge waehrend eines laufenden Renders die Auswahl auf die
    /// oberste Zeile zurueck - mitten im Zusehen.
    /// </summary>
    private void ReloadSequencesKeepingSelection() => LoadSequenceList(keepSelection: true);

    /// <summary>
    /// Eine Bildsequenz von Hand oeffnen.
    ///
    /// Sie erscheint oben in der Liste, abgesetzt von den protokollierten Renders:
    /// Was FrameFlip beim Rendern gesehen hat, ist etwas anderes als das, was jemand
    /// gerade herausgesucht hat, und die Liste soll das nicht verwischen.
    /// </summary>
    private void OnOpenSequence(object sender, RoutedEventArgs e) => ShowOpenDialog();

    private void ShowOpenDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("D_Open"),
            Filter = Strings.T("D_ImageFilter") + "|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.webp",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true) return;

        OpenPath(dialog.FileName);
    }

    /// <summary>
    /// Einen Bildpfad als Sequenz auf die Buehne holen. Liefert false, wenn daraus
    /// nichts zu lesen war.
    /// </summary>
    private bool OpenPath(string path)
    {
        if (!_sequences.AddPath(path)) return false;

        LoadSequences();
        Select(path);

        if (_sequence is not { Count: > 0 } opened) return false;

        // Damit die Projektseite unter "zuletzt geoeffnet" dasselbe zeigt. Erst
        // hier, weil erst die Auswahl weiss, wieviele Bilder es geworden sind.
        RecentSequences.Remember(new RecentSequence
        {
            Folder = _current!.Folder,
            Seed = path,
            First = opened.StartNumber,
            Last = opened.EndNumber,
            Count = opened.Count,
            Kind = opened.Pattern.Extension.TrimStart('.').ToLowerInvariant(),
            OpenedUtc = DateTime.UtcNow,
        });

        return true;
    }

    // ================================================================ Live

    /// <summary>Die Sequenz neu einlesen, ohne die Auswahl oder die Stelle zu verlieren.</summary>
    private void RescanLive()
    {
        var change = _sequences.Rescan();
        if (change is null) return;
        if (change.Previous is null)
        {
            ApplySequenceSelection();
            return;
        }
        var fresh = change.Current;
        _missing = fresh.MissingNumbers();

        // Der Render hat weitergeschrieben: Die Stellen stimmen nicht mehr ueberein.
        DropCache();

        // Ein von Hand gesetztes Ende bleibt stehen; nur das offene Ende waechst mit.
        int? follow = _playback.Rescan(change.Previous, fresh);

        RefreshSequenceItem(_current!);

        BuildStrip();
        BuildGaps();
        DrawScrub();

        if (follow is int newest) ShowFrame(newest);
        else UpdateStripSelection();

        Refresh();
    }

    private void OnFollowChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;

        if (_playback.SetFollow(toggle.IsChecked == true) is int newest) ShowFrame(newest);
    }

    /// <summary>
    /// Den Zwischenspeicher freigeben.
    ///
    /// Eine Folge aus tausend Bildern belegt schnell ein halbes Gigabyte. Sie liegen
    /// zu lassen, weil "vielleicht kommt jemand zurueck", waere genau die Art
    /// Grosszuegigkeit, die FrameFlip nicht haben will.
    /// </summary>
    private void DropCache()
    {
        if (_frames.IsPreloading) CancelPreload();
        _frames.Reset(_sequence);
        _lastPreload = null;
        _videos.Reset();
    }
    private void ShowEmptyStage()
    {
        _missing = Array.Empty<int>();

        StageImage.Source = null;
        StageEmpty.Visibility = Visibility.Visible;
        StageEmpty.SetResourceReference(TextBlock.TextProperty, "D_StageStartHint");

        SequenceName.SetResourceReference(TextBlock.TextProperty, "D_StageTitle");
        SequenceFile.Text = string.Empty;
        StageHead.Text = string.Empty;
        StageZoom.Text = string.Empty;
        StageDecode.Text = string.Empty;

        FilmStrip.Children.Clear();
        ScrubCanvas.Children.Clear();
        GapGrid.Children.Clear();
    }

    // ================================================================ Buehne

    /// <summary>Was der Rahmen zeigt, wenn nichts anderes bekannt ist.</summary>
    private const double DefaultAspect = 16.0 / 9.0;

    /// <summary>
    /// Den Rahmen auf das Ausgabeformat des Renders bringen.
    ///
    /// Nicht auf die Groesse des dekodierten Bildes: Das ist auf 1280 Punkte
    /// begrenzt und sagt ueber das Format nichts aus. Und nicht auf ein festes 16:9 -
    /// wer hochkant rendert oder in 2.39:1, sieht sonst seinen Frame in einem
    /// Rahmen, der ihm nicht gehoert.
    ///
    /// Der Rahmen selbst bekommt eine gerundete Groesse mit dem richtigen
    /// Verhaeltnis; wie gross er am Ende erscheint, entscheidet die Viewbox aus dem
    /// Platz, der da ist. Deshalb waechst und schrumpft er mit dem Fenster, statt
    /// auf einer Zahl zu beharren.
    /// </summary>
    private void ShapeStage()
    {
        double aspect = DefaultAspect;

        if (_current is { Width: > 0, Height: > 0 } known)
        {
            aspect = known.Width / (double)known.Height;
        }
        else if (_sequence is { Count: > 0 } sequence
                 && _decoders.TryProbeSize(sequence.Frames[0].Path, out int width, out int height)
                 && width > 0 && height > 0)
        {
            aspect = width / (double)height;

            // Gemessen ist gemessen: Auch die Statuszeile soll das Format nennen,
            // statt einen Gedankenstrich zu zeigen, weil die Bruecke geschwiegen hat.
            if (_current is { } entry)
            {
                entry.Width = width;
                entry.Height = height;
            }
        }

        // Das dritte Schild ueber der Buehne stand bisher leer. Was dort hingehoert,
        // faellt beim Messen ohnehin an: Format und Farbtiefe der Datei - die Angabe,
        // an der man sieht, ob der Render 8 oder 16 Bit geschrieben hat.
        _format = string.Empty;

        if (_sequence is { Count: > 0 } probe
            && _decoders.TryProbeInfo(probe.Frames[0].Path, out ImageInfo info))
        {
            string kind = _sequence.Pattern.Extension.TrimStart('.').ToUpperInvariant();

            _format = info.BitsPerChannel > 0
                ? kind + " \u00b7 " + info.BitsPerChannel + " bit"
                : kind;
        }

        StageDecode.Text = _format;

        // Auf eine handliche Groesse normiert: Ein 8K-Render als 7680 Punkte breiter
        // Rahmen im Layoutbaum bringt nichts, was 1000 nicht auch brachten.
        const double Longest = 1000;

        StageFrame.Width = aspect >= 1 ? Longest : Longest * aspect;
        StageFrame.Height = aspect >= 1 ? Longest / aspect : Longest;
    }


    private void ShowFrame(int number)
    {
        int index = _playback.Seek(number);
        if (_sequence is null || index < 0) return;

        var frame = _sequence.Frames[index];

        SequenceFile.Text = "/ " + frame.FileName;
        StageHead.Text = frame.Number.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture);

        // Die Nummer allein sagt bei Luecken nicht, wo man steht: Frame 0042 kann
        // das dreissigste Bild sein. Daneben steht darum der Platz in der Sequenz.
        StageZoom.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}",
                                       index + 1, _sequence.Count);

        UpdateStripSelection();
        MoveHead();

        _frames.Request(index);
    }

    private void ShowDecodedFrame(BitmapSource image)
    {
        StageImage.Source = image;
        StageEmpty.Visibility = Visibility.Collapsed;
        StageDecode.Text = _format;
    }

    private void DispatchFrame(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }
    // ================================================================ Zeitleiste

    /// <summary>
    /// Der Untergrund der Zeitleiste: Bereich, geschriebener Teil, Luecken, Marken.
    ///
    /// Er aendert sich nur, wenn sich die Sequenz oder die Breite aendert - beim
    /// Abspielen nicht. Vorher entstand er bei jedem Bild neu, also vierundzwanzigmal
    /// je Sekunde: ein Dutzend Formen samt neun Beschriftungen, jede einzeln
    /// vermessen. Das war der teuerste Teil der Wiedergabe, und keiner davon war
    /// noetig.
    /// </summary>
    private void DrawScrub()
    {
        ScrubCanvas.Children.Clear();
        _headMark = null;

        if (_sequence is null || _sequence.Count == 0) return;

        double width = ScrubCanvas.ActualWidth;
        double height = ScrubCanvas.ActualHeight;
        if (width <= 1 || height <= 1) return;

        // Der gewaehlte Bereich als Flaeche.
        var range = new Rectangle
        {
            Width = Math.Max(0, AtScrub(_playback.OutPoint) - AtScrub(_playback.InPoint)),
            Height = height,
            Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xA8, 0x55, 0xF7)),
        };

        Canvas.SetLeft(range, AtScrub(_playback.InPoint));
        ScrubCanvas.Children.Add(range);

        // Unterkante: wie weit der Ordner geschrieben ist.
        var written = new Rectangle
        {
            Width = AtScrub(_sequence.Frames[^1].Number),
            Height = 4,
            Fill = new SolidColorBrush(Color.FromArgb(0xD9, 0xA8, 0x55, 0xF7)),
        };

        Canvas.SetLeft(written, 0);
        Canvas.SetTop(written, height - 4);
        ScrubCanvas.Children.Add(written);

        // Luecken - der einzige Ort, an dem Pink vorkommt.
        foreach (int gap in _missing)
        {
            var mark = new Rectangle
            {
                Width = 3,
                Height = height,
                Fill = (Brush)FindResource("DesktopWarn"),
            };

            Canvas.SetLeft(mark, AtScrub(gap));
            ScrubCanvas.Children.Add(mark);
        }

        // Marken, so viele wie lesbar nebeneinander passen.
        int ticks = Math.Clamp((int)(width / 74), 2, 9);

        for (int i = 0; i <= ticks; i++)
        {
            int number = _sequence.StartNumber + (int)Math.Round(i / (double)ticks * ScrubSpan);

            var label = new TextBlock
            {
                Text = number.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture),
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = 9.5,
                Foreground = (Brush)FindResource("DesktopMuted"),
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double left = AtScrub(number);
            if (i == ticks) left -= label.DesiredSize.Width;
            else if (i > 0) left -= label.DesiredSize.Width / 2;

            Canvas.SetLeft(label, Math.Max(2, Math.Min(width - label.DesiredSize.Width - 2, left)));
            Canvas.SetTop(label, 9);
            ScrubCanvas.Children.Add(label);
        }

        _headMark = new Rectangle
        {
            Width = 3,
            Height = height,
            RadiusX = 2,
            RadiusY = 2,
            Fill = (Brush)FindResource("DesktopInk"),
        };

        ScrubCanvas.Children.Add(_headMark);
        MoveHead();
    }

    /// <summary>Nur der Zeiger wandert. Das ist alles, was beim Abspielen passiert.</summary>
    private void MoveHead()
    {
        if (_headMark is null || _sequence is null) return;

        Canvas.SetLeft(_headMark, Math.Max(0, Math.Min(ScrubCanvas.ActualWidth - 3, AtScrub(_playback.Head))));
    }

    private int ScrubSpan => _sequence is null ? 1 : Math.Max(1, _sequence.EndNumber - _sequence.StartNumber);

    private double AtScrub(int number)
        => _sequence is null ? 0 : (number - _sequence.StartNumber) / (double)ScrubSpan * ScrubCanvas.ActualWidth;

    private void OnScrubResized(object sender, SizeChangedEventArgs e) => DrawScrub();

    private void OnStripResized(object sender, SizeChangedEventArgs e)
    {
        if (_sequence is null) return;
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < StripCellWidth / 2) return;

        BuildStrip();
        UpdateStripSelection();
    }

    private void OnScrubDown(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = true;
        ScrubTrack.CaptureMouse();
        Pause();
        ScrubTo(e.GetPosition(ScrubCanvas).X);
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) ScrubTo(e.GetPosition(ScrubCanvas).X);
    }

    private void OnScrubUp(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = false;
        ScrubTrack.ReleaseMouseCapture();
    }

    private void ScrubTo(double x)
    {
        if (_sequence is null || _sequence.Count == 0) return;

        double width = Math.Max(1, ScrubCanvas.ActualWidth);
        double share = Math.Clamp(x / width, 0, 1);

        int first = _sequence.StartNumber;
        int last = _sequence.EndNumber;

        ShowFrame(first + (int)Math.Round(share * (last - first)));
    }

    // ================================================================ Filmstreifen

    /// <summary>Das zuletzt geoeffnete Menue im Dashboard - fuer die Probe.</summary>
    internal FlipMenu? DashboardMenu { get; private set; }

    /// <summary>Der Pfad des Bildes mit dieser Nummer - oder keiner.</summary>
    private string? FramePath(int number)
    {
        if (_sequence is not { Count: > 0 } sequence) return null;

        int index = sequence.IndexNearestNumber(number);
        return index >= 0 && index < sequence.Count ? sequence.Frames[index].Path : null;
    }

    /// <summary>Das Menue eines Bildes im Streifen.</summary>
    internal void ShowFrameMenu(FrameworkElement target, string path)
    {
        var menu = new FlipMenu(target)
            .Item("◧", Strings.T("D_MenuOpenInAtelier"), () => OpenInAtelier(path))
            .Item("⌕", Strings.T("D_MenuShowInExplorer"), () => ShowInExplorer(path))
            .Item("⧉", Strings.T("D_MenuCopyPath"), () => Clipboard.SetText(path));

        DashboardMenu = menu;
        menu.Open();
    }

    /// <summary>
    /// Das Menue einer Sequenz in der Liste: ihr Ordner im Explorer und als Pfad - und,
    /// wenn sie gerade gezeigt wird, ihr aktuelles Bild im Atelier.
    /// </summary>
    internal void ShowSequenceMenu(FrameworkElement target, DashboardSequenceEntry entry)
    {
        string where = entry.HasOutput ? entry.Folder : entry.BlendPath;
        string? shown = ReferenceEquals(entry, _current) ? FramePath(_playback.Head) : null;

        var menu = new FlipMenu(target)
            .Item("◧", Strings.T("D_MenuOpenInAtelier"), () => OpenInAtelier(shown!), enabled: shown is not null)
            .Item("⌕", Strings.T("D_MenuShowInExplorer"), () => ShowInExplorer(where), enabled: where.Length > 0)
            .Item("⧉", Strings.T("D_MenuCopyPath"), () => Clipboard.SetText(where), enabled: where.Length > 0);

        DashboardMenu = menu;
        menu.Open();
    }

    /// <summary>Ein Bild ins Atelier - der Reiter wechselt, und das Atelier oeffnet es.</summary>
    internal void OpenInAtelier(string path)
    {
        NavAtelier.IsChecked = true;
        _atelierPage?.Open(path);
    }

    /// <summary>Der Explorer, mit der Datei markiert - oder im Ordner.</summary>
    private static void ShowInExplorer(string path)
    {
        string arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Kein Explorer - dann eben nicht; das Menue hat nichts kaputtgemacht.
        }
    }

    /// <summary>
    /// Der Streifen bekommt genau so viele Zellen, wie nebeneinander sichtbar sind.
    /// Eine feste Zahl waere entweder zu kurz fuer ein breites Fenster oder zu lang
    /// fuer ein schmales - im zweiten Fall stuenden die Zellen ausserhalb, und der
    /// Streifen liefe still ins Nichts.
    /// </summary>
    private int StripCells =>
        Math.Max(4, (int)(Math.Max(StripCellWidth, StripScroller.ActualWidth) / StripCellWidth));

    private void BuildStrip()
    {
        FilmStrip.Children.Clear();

        if (_sequence is null || _sequence.Count == 0) return;

        int cells = StripCells;

        for (int i = 0; i < cells; i++)
        {
            var cell = new ToggleButton { Style = (Style)FindResource("DashStripCell") };

            cell.Click += (s, _) =>
            {
                if (s is ToggleButton button && button.Tag is int number)
                {
                    Pause();
                    ShowFrame(number);
                }
            };

            // Rechtsklick auf ein Bild des Streifens: ins Atelier, in den Explorer, der Pfad.
            cell.MouseRightButtonUp += (s, e) =>
            {
                if (s is not ToggleButton { Tag: int number } button || FramePath(number) is not { } path) return;

                e.Handled = true;
                ShowFrameMenu(button, path);
            };

            FilmStrip.Children.Add(cell);
        }
    }

    /// <summary>
    /// Der Streifen laeuft mit dem Kopf mit, statt bei jedem Schritt neu zu
    /// entstehen: Die Zellen bleiben, nur ihre Beschriftung wandert.
    /// </summary>
    private void UpdateStripSelection()
    {
        if (_sequence is null || FilmStrip.Children.Count == 0) return;

        int count = _sequence.Count;
        int cells = FilmStrip.Children.Count;
        int centre = _sequence.IndexNearestNumber(_playback.Head);
        int start = Math.Max(0, Math.Min(Math.Max(0, count - cells), centre - cells / 2));

        for (int i = 0; i < FilmStrip.Children.Count; i++)
        {
            if (FilmStrip.Children[i] is not ToggleButton cell) continue;

            int index = start + i;

            if (index < 0 || index >= count)
            {
                cell.Visibility = Visibility.Collapsed;
                continue;
            }

            var frame = _sequence.Frames[index];

            cell.Visibility = Visibility.Visible;
            cell.Tag = frame.Number;
            cell.Content = frame.Number.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture);
            cell.IsChecked = frame.Number == _playback.Head;
        }
    }

    // ================================================================ Transport

    private void OnTogglePlay(object sender, RoutedEventArgs e)
    {
        // Waehrend geladen wird, bricht derselbe Knopf ab. Sonst muesste man auf
        // etwas warten, das man gar nicht mehr will.
        if (_frames.IsPreloading)
        {
            CancelPreload();
            return;
        }

        if (_playback.IsPlaying) Pause();
        else if (_prebuffer && NeedsPreload()) StartPreload();
        else Play();
    }

    // ================================================================ Vorausladen

    /// <summary>Ob fuer den gewaehlten Bereich noch Bilder fehlen.</summary>
    private bool NeedsPreload() => _frames.NeedsPreload(_playback.InPoint, _playback.OutPoint);

    private async void StartPreload()
    {
        if (_sequence is null || _frames.IsPreloading) return;
        int total = _sequence.Count;
        ShowPlayGlyph(playing: true);
        PreloadBar.Visibility = Visibility.Visible;
        PreloadNote.Text = string.Empty;
        ShowPreloadProgress(new PreloadProgress(0, total, CurrentPace(), 0));
        Note(Strings.T("D_LogPreload", total));

        var result = await _frames.PreloadAsync(DecodeWidth(), PreloadBudget(), StageAspect());
        // Zwischen Rueckgabe und Fortsetzung kann schon eine neue Auswahl gelten.
        if (result is null || !_frames.IsCurrent(result)) return;
        PreloadBar.Visibility = Visibility.Collapsed;
        Note(result.Whole
            ? Strings.T("D_LogPreloadDone", result.Loaded)
            : Strings.T("D_LogPreloadPart", result.Loaded, result.Total));
        ShowFrame(_playback.InPoint);
        Play();
    }
    /// <summary>
    /// Nebenher schon das Video kodieren.
    ///
    /// Erst NACH dem Vorausladen, nicht waehrenddessen: Beides gleichzeitig kaempft
    /// um dieselben Kerne, und das Vorausladen ist das, worauf jemand wartet. Waehrend
    /// die Folge laeuft, hat ffmpeg Zeit - und laeuft mit der niedrigsten Prioritaet,
    /// die noch vorankommt.
    ///
    /// Geschrieben wird in den Temp-Ordner. An seinen Platz kommt das Ergebnis nur,
    /// wenn jemand wirklich exportiert; bis dahin ist es ein Angebot, keine Datei in
    /// fremden Ordnern.
    /// </summary>
    private async Task PrepareVideoAsync(ImageSequence sequence)
    {
        if (_videos.IsPreparing) return;

        string? exe = FfmpegLocator.Locate(_getSettings().FfmpegPath);

        // Frueher stand hier ein blankes return. Der Knopf tat dann nichts und sagte
        // auch nicht warum - fuer jemanden, der FrameFlip gerade geholt hat, ist das
        // ein kaputtes Programm und kein fehlendes Werkzeug.
        if (exe is null)
        {
            Note(Strings.T("S_ReadyNoFfmpegForExport"));
            RefreshReadiness();
            return;
        }

        var frames = sequence.Frames
            .Where(f => f.Number >= _playback.InPoint && f.Number <= _playback.OutPoint)
            .ToList();

        if (frames.Count < 2) return;

        var preset = ExportPreset.All.FirstOrDefault(x => x.Name == _getSettings().ExportPreset)
                     ?? ExportPreset.H264;

        int width = _current?.Width ?? 0;
        int height = _current?.Height ?? 0;

        if (width <= 0 && _decoders.TryProbeSize(frames[0].Path, out int pw, out int ph))
        {
            width = pw;
            height = ph;
        }

        var request = new DashboardVideoRequest(exe, sequence.Pattern.Describe(), _playback.InPoint, _playback.OutPoint,
            frames, _playback.Fps, width, height, preset, Math.Max(1, Environment.ProcessorCount / 4));
        Note(Strings.T("D_LogPrepare"));
        var result = await _videos.PrepareAsync(request);
        if (result is null || !_videos.IsCurrent(result)) return;
        Note(result.Path is { Length: > 0 }
            ? Strings.T(result.Reused ? "D_LogPrepareReady" : "D_LogPrepareDone")
            : Strings.T("D_LogPrepareFailed", result.Error ?? "?"));
    }

    /// <summary>
    /// Wie breit ein Bild gelesen werden soll - in echten Bildschirmpunkten.
    ///
    /// Das ist der Teil, an dem ich zweimal danebenlag. Zuerst wurde die genormte
    /// Rahmenbreite verdoppelt: eine Zahl ohne Bezug zum Bildschirm, die bei
    /// hundertzwanzig Bildern siebenhundert Megabyte belegte. Dann wurde die Breite
    /// der Buehnenflaeche genommen - und die zaehlt in den Einheiten VOR der
    /// Oberflaechenskalierung und vor der Bildschirmdichte. Auf diesem Rechner sind
    /// das zusammen fast der Faktor zwei, und genau um den war das Bild zu grob.
    ///
    /// Hier wird deshalb gerechnet, was die Flaeche am Ende wirklich belegt: einmal
    /// durch alle Transformationen bis zum Fenster, einmal mit der Dichte des
    /// Bildschirms. Ueber die Quellbreite hinaus wird nie gelesen - groesser als die
    /// Datei ist nur aufgeblasen.
    /// </summary>
    private int DecodeWidth(int atLeast = 480)
    {
        double width = 0;

        try
        {
            if (StageArea.ActualWidth > 1 && StageArea.IsVisible)
            {
                var toWindow = StageArea.TransformToAncestor(this);
                var box = toWindow.TransformBounds(new Rect(0, 0, StageArea.ActualWidth, StageArea.ActualHeight));

                double density = PresentationSource.FromVisual(this)?.CompositionTarget?
                                     .TransformToDevice.M11 ?? 1.0;

                width = box.Width * (density > 0 ? density : 1.0);
            }
        }
        catch (InvalidOperationException)
        {
            // Noch nicht im Baum. Dann bleibt es beim Rueckfallwert.
        }

        int pixels = width > 200 ? (int)Math.Ceiling(width) : 1600;

        if (_current is { Width: > 0 } known) pixels = Math.Min(pixels, known.Width);

        return Math.Clamp(pixels, atLeast, 3840);
    }

    /// <summary>
    /// Wieviel Speicher das Vorausladen belegen darf.
    ///
    /// Die feste Zahl aus den Einstellungen war fuer den Ringpuffer des
    /// Vorschaufensters gedacht, der neben einem Render laeuft und sich klein machen
    /// soll. Hier ist die Lage eine andere: Jemand hat auf Abspielen gedrueckt und
    /// wartet. Mit einem Gigabyte passte eine Folge von sechshundert Bildern nur in
    /// zwei Dritteln der Anzeigebreite in den Speicher, bei tausendzweihundert nur
    /// noch in vier Zehnteln - und genau das sah man der Wiedergabe an.
    ///
    /// Gemessen wird deshalb am wirklich freien Speicher. Ein Gigabyte bleibt immer
    /// stehen, vom Rest nimmt FrameFlip die Haelfte, und die Zahl aus den
    /// Einstellungen ist die Untergrenze, nicht die Obergrenze.
    /// </summary>
    private long PreloadBudget()
    {
        const long Gigabyte = 1024L * 1024 * 1024;

        long configured = _getSettings().MemoryBudgetBytes;
        long free = Math.Max(0, LivePage.Load()?.AvailableMb ?? 0) * 1024L * 1024L;

        if (free <= 0) return configured;

        long spare = Math.Max(0, free - Gigabyte);
        long wanted = Math.Max(configured, spare / 2);

        // Ueber den freien Speicher hinaus wird nie geplant, auch wenn jemand in den
        // Einstellungen eine grosse Zahl eingetragen hat: Auslagern waere langsamer
        // als von der Platte zu lesen.
        return Math.Clamp(Math.Min(wanted, spare), 256L * 1024 * 1024, 8 * Gigabyte);
    }

    /// <summary>Das Seitenverhaeltnis des Renders - fuer die Speicherrechnung.</summary>
    private double StageAspect()
    {
        if (_current is { Width: > 0, Height: > 0 } known) return known.Width / (double)known.Height;

        return StageFrame.Height > 0 ? StageFrame.Width / StageFrame.Height : 16.0 / 9.0;
    }

    private void CancelPreload()
    {
        _frames.CancelPreload();

        PreloadBar.Visibility = Visibility.Collapsed;
        ShowPlayGlyph(playing: false);

        Note(Strings.T("D_LogPreloadStop"));
    }

    private PreloadPace CurrentPace()
        => SequencePreloader.PaceFor(LivePage.Load(), _monitor?.Job?.IsRunning == true);

    /// <summary>Kommt aus dem Ladefaden - alles Sichtbare ueber den Dispatcher.</summary>
    private void ShowPreloadProgress(PreloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ShowPreloadProgress(progress)));
            return;
        }

        _lastPreload = progress;

        PaceText.Text = Strings.T(progress.Pace switch
        {
            PreloadPace.Fast => "D_PaceFast",
            PreloadPace.Medium => "D_PaceMedium",
            _ => "D_PaceSlow",
        });

        PreloadCount.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}   ·   {2:0} MB",
                                          progress.Loaded, progress.Total, progress.Bytes / 1048576.0);

        // Musste fuers Budget verkleinert werden, gehoert das hierher: Sonst sieht
        // man ein weiches Bild und haelt es fuer einen Fehler.
        PreloadNote.Text = progress.Reduced
            ? Strings.T("D_PaceNarrow", progress.Width, progress.WantedWidth)
            : _monitor?.Job?.IsRunning == true
                ? Strings.T("D_PaceRendering")
                : progress.Pace == PreloadPace.Slow ? Strings.T("D_PaceBusy") : string.Empty;

        PaintPreloadBar();
    }

    private void OnPreloadBarResized(object sender, SizeChangedEventArgs e) => PaintPreloadBar();

    private void PaintPreloadBar()
    {
        if (PreloadFill?.Parent is not FrameworkElement track) return;

        PreloadFill.Width = Math.Clamp(_lastPreload?.Share ?? 0, 0, 1) * track.ActualWidth;
    }

    private void Play()
    {
        if (!_playback.Play()) return;

        _player.Interval = _playback.Interval;
        _player.Start();

        ShowPlayGlyph(playing: true);

        // Bei jedem Start nachsehen, nicht nur nach einem Ladevorgang: Die Bildrate
        // gehoert zum fertigen Video, und wer sie aendert, macht eine vorbereitete
        // Fassung ungueltig. Liegt sie schon vor, endet der Aufruf sofort.
        if (_prepareVideo && _sequence is { } running) _ = PrepareVideoAsync(running);
    }

    private void Pause()
    {
        if (!_playback.Pause()) return;

        _player.Stop();

        ShowPlayGlyph(playing: false);
    }

    /// <summary>
    /// Dreieck oder zwei Balken.
    ///
    /// Der Knopf zeigt, was der Klick tut, nicht was gerade laeuft - dieselbe Regel
    /// wie beim Maximieren-Zeichen. Der lesbare Name wandert nach
    /// AutomationProperties, denn ein gezeichnetes Zeichen hat keinen Text, den eine
    /// Sprachausgabe vorlesen koennte.
    /// </summary>
    private void ShowPlayGlyph(bool playing)
    {
        const string Triangle = "M2,1 L15,8 L2,15 Z";
        const string Bars = "M2,1 L6,1 L6,15 L2,15 Z M10,1 L14,1 L14,15 L10,15 Z";

        PlayGlyph.Data = Geometry.Parse(playing ? Bars : Triangle);

        string name = Strings.T(playing ? "D_Pause" : "D_Play");

        PlayButton.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, name);
    }

    private void Advance()
    {
        switch (_playback.Advance(loop: _loopChip?.IsChecked == true, out int next))
        {
            case DashboardTick.Show: ShowFrame(next); break;
            case DashboardTick.Stop: Pause(); break;
        }
    }

    private void OnStepBack(object sender, RoutedEventArgs e) => Step(-1);

    private void OnStepForward(object sender, RoutedEventArgs e) => Step(+1);

    private void Step(int delta)
    {
        if (_playback.StepTarget(delta) is not int target) return;

        Pause();
        ShowFrame(target);
    }

    private void OnJumpIn(object sender, RoutedEventArgs e) { Pause(); ShowFrame(_playback.InPoint); }

    private void OnJumpOut(object sender, RoutedEventArgs e) { Pause(); ShowFrame(_playback.OutPoint); }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (TermsHost.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { OnTermsLater(sender, e); e.Handled = true; }
            return;
        }
        if (PairHost.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { OnPairClose(sender, e); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.O)
            {
                NavDashboard.IsChecked = true;
                ShowOpenDialog();
                e.Handled = true;
                return;
            }
            double? scale = e.Key switch
            {
                Key.Add or Key.OemPlus => _layout.Scale + .05,
                Key.Subtract or Key.OemMinus => _layout.Scale - .05,
                Key.D0 or Key.NumPad0 => 1,
                _ => null,
            };
            if (scale is { } requested)
            {
                _layout.Scale = requested;
                _layout.Save();
                e.Handled = true;
            }
            return;
        }
        // Die Werkzeugspalte des Ateliers hoert auf V, W, C, H, I - dieselben
        // Buchstaben wie anderswo, damit niemand sie neu lernen muss. Tastendruecke
        // kommen im Fenster an und nicht auf der Seite, deshalb der Umweg hierher.
        if (_page == "atelier" && Keyboard.Modifiers == ModifierKeys.None
            && !OwnsNavigationKeys(e.OriginalSource as DependencyObject)
            && _atelierPage is { } atelier && atelier.HandleToolKey(e.Key))
        {
            e.Handled = true;

            return;
        }

        if (_page != "dashboard" || Keyboard.Modifiers != ModifierKeys.None
            || OwnsNavigationKeys(e.OriginalSource as DependencyObject)) return;

        switch (e.Key)
        {
            case Key.Space: OnTogglePlay(sender, e); e.Handled = true; break;
            case Key.Left: Step(-1); e.Handled = true; break;
            case Key.Right: Step(+1); e.Handled = true; break;
            case Key.Home: Pause(); ShowFrame(_playback.InPoint); e.Handled = true; break;
            case Key.End: Pause(); ShowFrame(_playback.OutPoint); e.Handled = true; break;
        }
    }

    // ================================================================ Chips

    private void BuildPlayChips()
    {
        _loopChip = Chip("D_Loop", true);
        PlayChips.Children.Add(_loopChip);

        var settings = _getSettings();

        _prebuffer = settings.Prebuffer;

        // Die beiden Schalter sind Voreinstellungen, keine Bedienung waehrend des
        // Abspielens - und sie sind breit. In der Transportleiste schoben sie
        // "ENDE SETZEN" und das Ratenfeld aus der Spalte heraus, wo sie hinter der
        // Metrikflaeche verschwanden. Ihr Platz ist die Kopfzeile ueber der Buehne,
        // die im Entwurf ohnehin die Ansichtsschalter traegt.
        _prepareVideo = settings.PrepareVideo;

        var preload = Chip("D_Prebuffer", _prebuffer, small: true);
        preload.ToolTip = Strings.T("D_PrebufferHint");
        preload.Checked += (_, _) => SetPrebuffer(true);
        preload.Unchecked += (_, _) => SetPrebuffer(false);
        ViewChips.Children.Add(preload);

        var prepare = Chip("D_PrepareVideo", _prepareVideo, small: true);
        prepare.ToolTip = Strings.T("D_PrepareVideoHint");
        prepare.Checked += (_, _) => SetPrepareVideo(true);
        prepare.Unchecked += (_, _) => SetPrepareVideo(false);
        ViewChips.Children.Add(prepare);

        var setIn = Chip("D_SetIn", false);
        setIn.Checked += (_, _) =>
        {
            setIn.IsChecked = false;
            _playback.MarkIn();
            DrawScrub();
            Refresh();
        };
        PlayChips.Children.Add(setIn);

        var setOut = Chip("D_SetOut", false);
        setOut.Checked += (_, _) =>
        {
            setOut.IsChecked = false;
            _playback.MarkOut();
            DrawScrub();
            Refresh();
        };
        PlayChips.Children.Add(setOut);

    }

    // ================================================================ Bildrate

    /// <summary>Die Raten, die in der Liste stehen. Getippt werden darf jede.</summary>
    private static readonly double[] CommonRates = { 8, 12, 15, 23.976, 24, 25, 30, 48, 50, 60 };

    private void BuildRates()
    {
        foreach (double rate in CommonRates) RateBox.Items.Add(Rate(rate));

        _playback.UseRate(_getSettings().Fps);
        RateBox.Text = Rate(_playback.Fps);

        // Getippt wird erst uebernommen, wenn das Feld den Fokus verlaesst oder die
        // Eingabetaste kommt - sonst bekaeme die Wiedergabe bei "2" aus "24" kurz
        // zwei Bilder je Sekunde zu sehen.
        RateBox.LostFocus += (_, _) => TakeRate(RateBox.Text);
        RateBox.KeyUp += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            TakeRate(RateBox.Text);
            e.Handled = true;
        };
    }

    private void OnRatePicked(object sender, SelectionChangedEventArgs e)
    {
        if (RateBox is null || e.AddedItems.Count == 0) return;

        TakeRate(e.AddedItems[0] as string);
    }

    /// <summary>
    /// Eine getippte oder gewaehlte Rate uebernehmen.
    ///
    /// Unlesbares oder Unsinniges setzt das Feld auf den geltenden Wert zurueck,
    /// statt die Wiedergabe anzuhalten: Ein Tippfehler soll nichts kaputtmachen,
    /// er soll nur nichts bewirken.
    /// </summary>
    private void TakeRate(string? text)
    {
        double wanted = _playback.TakeRate(text);

        string shown = Rate(wanted);
        if (RateBox.Text != shown) RateBox.Text = shown;

        if (_player is not null) _player.Interval = _playback.Interval;

        // Waehrend des Aufbaus wird nur der Wert gesetzt, nichts gespeichert: Der
        // Stand kommt ja gerade aus den Einstellungen.
        if (!_ready) return;

        var settings = _getSettings();

        if (Math.Abs(settings.Fps - wanted) > 0.0005)
        {
            settings.Fps = wanted;
            _persist?.Invoke(settings);
        }

        RefreshStatusBar(_monitor?.Job);
    }

    private static string Rate(double value)
        => value.ToString(Math.Abs(value - Math.Round(value)) < 0.001 ? "0" : "0.###",
                          CultureInfo.InvariantCulture);

    private void SetPrepareVideo(bool on)
    {
        _prepareVideo = on;

        if (!_ready) return;

        var settings = _getSettings();

        if (settings.PrepareVideo != on)
        {
            settings.PrepareVideo = on;
            _persist?.Invoke(settings);
        }

        // Wer den Schalter mitten im Abspielen umlegt, will nicht bis zum naechsten
        // Start warten - er hat ihn ja gerade deshalb umgelegt. Vorher lief die
        // Vorbereitung nur beim Druck auf Abspielen an, und das Umlegen tat sichtbar
        // nichts.
        if (on && _sequence is { Count: > 1 } running) _ = PrepareVideoAsync(running);
        else if (!on) _videos.Cancel();
    }

    private void SetPrebuffer(bool on)
    {
        _prebuffer = on;

        if (!_ready) return;

        var settings = _getSettings();

        if (settings.Prebuffer == on) return;

        settings.Prebuffer = on;
        _persist?.Invoke(settings);
    }

    private ToggleButton Chip(string? key, bool on, bool small = false)
    {
        var chip = new ToggleButton
        {
            Style = (Style)FindResource(small ? "DashChipSmall" : "DashChip"),
            IsChecked = on,
            Margin = new Thickness(0, 0, small ? 6 : 8, 0),
        };

        if (key is not null)
        {
            var label = new TextBlock();
            Track.SetAmount(label, 1);
            Track.SetText(label, Strings.T(key));
            chip.Content = label;
        }

        return chip;
    }

    /// <summary>
    /// Die Fernsteuerung zeigt nur, was von hier aus wirklich geht. Ein Knopf
    /// "Render anhalten" stuende gut im Entwurf, aber FrameFlip haelt keinen
    /// Blender-Render an, den es nicht selbst gestartet hat - und ein Knopf, der
    /// nichts tut, ist schlimmer als eine Luecke.
    /// </summary>
    private void BuildRemoteActions()
    {
        RemoteActions.Children.Add(GhostAction("D_OpenFolder", OnOpenSequenceFolder));
        RemoteActions.Children.Add(GhostAction("D_ExportVideo", OnExport));
    }

    private Button GhostAction(string key, RoutedEventHandler handler)
    {
        var label = new TextBlock { FontSize = 9.5, TextAlignment = TextAlignment.Center };
        Track.SetAmount(label, 1);
        Track.SetText(label, Strings.T(key));

        var button = new Button
        {
            Style = (Style)FindResource("DashGhost"),
            Margin = new Thickness(0, 0, 4, 4),
            Content = label,
        };

        button.Click += handler;
        return button;
    }

    // ================================================================ Rechte Spalte

    private void OnPanelChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string key) return;
        if (PanelMetrics is null) return;

        _panel = key;

        PanelMetrics.Visibility = key == "metrics" ? Visibility.Visible : Visibility.Collapsed;
        PanelLog.Visibility = key == "log" ? Visibility.Visible : Visibility.Collapsed;
        PanelGaps.Visibility = key == "gaps" ? Visibility.Visible : Visibility.Collapsed;

        if (key == "log") RefreshLog();
    }

    private void BuildGaps()
    {
        GapGrid.Children.Clear();

        if (_sequence is null || _sequence.Count == 0)
        {
            GapIntro.Text = Strings.T("D_GapsNone");
            GapList.Text = string.Empty;
            return;
        }

        int first = _sequence.StartNumber;
        int span = Math.Max(1, _sequence.SpanLength);

        GapIntro.Text = Strings.T("D_GapsIntro", _sequence.Pattern.Describe(), _missing.Count);

        // Das Raster zeigt hoechstens 96 Kaesten. Bei laengeren Sequenzen steht ein
        // Kasten fuer mehrere Frames - er faerbt pink, sobald einer davon fehlt.
        double perCell = span / (double)GapCells;

        for (int i = 0; i < GapCells; i++)
        {
            int from = first + (int)Math.Floor(i * perCell);
            int to = Math.Max(from, first + (int)Math.Floor((i + 1) * perCell) - 1);

            bool gap = _missing.Any(m => m >= from && m <= to);

            GapGrid.Children.Add(new Border
            {
                Height = 9,
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Background = gap ? (Brush)FindResource("DesktopWarn") : (Brush)FindResource("DesktopAccent"),
            });
        }

        GapList.Text = _missing.Count == 0
            ? Strings.T("D_GapsClean")
            : Strings.T("D_GapsList", string.Join(" ", _missing.Take(24)
                  .Select(m => m.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture))));
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_sequence is null || _sequence.Count == 0) return;

        var range = _sequence.Frames
            .Where(f => f.Number >= _playback.InPoint && f.Number <= _playback.OutPoint)
            .ToList();

        if (range.Count == 0) return;

        int width = _current?.Width ?? 0;
        int height = _current?.Height ?? 0;

        if (width <= 0 && _decoders.TryProbeSize(range[0].Path, out int probedW, out int probedH))
        {
            width = probedW;
            height = probedH;
        }

        var window = new ExportWindow(_sequence, range, _playback.Fps, width, height,
                                      _getSettings(), s => _persist?.Invoke(s),
                                      () => Math.Max(1, Environment.ProcessorCount / 2))
        {
            Owner = this,
        };

        window.ShowDialog();
    }

    // ================================================================ Kopplung

    /// <summary>Den Kopplungscode zeigen - im Dashboard, nicht anderswo.</summary>
    private void OnShowPairing(object sender, RoutedEventArgs e)
    {
        // Ohne Zustimmung erst die Frage, nicht den Code.
        //
        // Ein Kopplungscode, solange nichts nach draussen geht, ist eine huebsche
        // Grafik und sonst nichts - niemand kann ihn einloesen. Wer zustimmt, sieht
        // ihn eine Wimper spaeter.
        if (!_getSettings().TermsOk)
        {
            AskTerms(() => OnShowPairing(sender, e));
            return;
        }

        PairHost.Visibility = Visibility.Visible;
        RefreshPairing();
        RefreshWatch();
        PairScroll.ScrollToTop();
        PairCloseButton.Focus();
    }

    private void OnPairClose(object sender, RoutedEventArgs e)
    {
        PairHost.Visibility = Visibility.Collapsed;
        RestorePageFocus();
    }

    // ================================================================ Zusehen im Netz

    /// <summary>Der Schalter der Zuschauerseite - siehe <see cref="WatchCard.Toggled"/>.</summary>
    private void OnWatchToggled(object sender, RoutedEventArgs e) => _watchCard?.Toggled();

    private void RefreshWatch() => _watchCard?.Refresh();

    private void OnWatchPassKey(object sender, KeyEventArgs e) => _watchCard?.PassKey(e);

    private void OnWatchPassDone(object sender, RoutedEventArgs e) => _watchCard?.PassDone();

    /* ----------------------------------------------------------- Zustimmung

       Vor der ersten Verbindung nach draussen wird gefragt - bei der Kopplung wie
       beim Zusehen im Browser. Nicht als Formsache: Bis hierhin hat FrameFlip
       ausschliesslich auf diesem Rechner gearbeitet, und dass es das nun verlaesst,
       soll niemandem beilaeufig passieren.

       Durchgesetzt wird der Riegel nicht hier, sondern in AppSettings.Normalize.
       Diese Tafel holt die Zustimmung nur ein. */

    private Action? _afterTerms;

    /// <summary>
    /// Was nach der Zustimmung noch einmal versucht werden soll.
    ///
    /// Die Auffrischung von TermsAccepted ist der ganze Witz daran: Die Kopie
    /// entstand VOR der Zustimmung und traegt darin noch die Null. Wer sie
    /// unveraendert uebernimmt, schaltet mit ihr genau das wieder ab, was sie
    /// einschalten wollte - Normalize sieht die fehlende Zustimmung und zieht
    /// RemoteEnabled und WatchEnabled zurueck. Der Benutzer haette zugestimmt und
    /// stuende vor demselben ausgeschalteten Schalter.
    ///
    /// Alles andere bleibt, wie es war: Ein frisch erzeugtes Kopplungsgeheimnis etwa
    /// steckt in dieser Kopie und nirgends sonst.
    /// </summary>
    private Action Wiederholung(AppSettings wunsch)
    {
        var kopie = wunsch.Clone();

        return () =>
        {
            var aktuell = _getSettings();
            kopie.TermsAccepted = aktuell.TermsAccepted;
            kopie.MainLeft = aktuell.MainLeft;
            kopie.MainTop = aktuell.MainTop;
            kopie.MainWidth = aktuell.MainWidth;
            kopie.MainHeight = aktuell.MainHeight;
            kopie.MainMaximized = aktuell.MainMaximized;

            if (_apply(kopie) is not null) return;

            // Der Aufrufer ist laengst zurueckgekehrt - seine Nacharbeit lief damals
            // ins Leere, weil das Uebernehmen fehlschlug. Ohne das hier stuende die
            // Kopplung eingeschaltet da und zeigte trotzdem keinen Code.
            RefreshPairing();
            RefreshLink();
            RefreshWatch();
        };
    }

    /// <summary>
    /// Zeigt die Tafel und fuehrt <paramref name="dann"/> aus, wenn zugestimmt wurde.
    /// Liegt die Zustimmung schon vor, geht es ohne Umweg weiter.
    /// </summary>
    private void AskTerms(Action dann, string? relayHost = null)
    {
        if (_getSettings().TermsOk) { dann(); return; }

        _afterTerms = dann;

        /* Der Wortlaut richtet sich danach, wessen Server eingetragen ist.
         *
         * Meine Rechtstexte gelten fuer meinen Server - die Seite sagt das im
         * Untertitel selbst. Wer einen eigenen eingetragen hat, soll ihnen nicht
         * zustimmen muessen: Was dort gilt, macht er mit dessen Betreiber aus, und
         * bei einem selbst betriebenen Server ist das er selbst.
         *
         * Die Zustimmung bleibt trotzdem noetig. Worum es hier geht, ist der Schritt
         * nach draussen und dass FrameFlip ohne Gewaehr kommt - und das haengt nicht
         * daran, wem der Server gehoert. */
        bool eigener = !string.Equals(relayHost ?? _getSettings().RelayHost, AppSettings.DefaultRelayHost,
                                      StringComparison.OrdinalIgnoreCase);

        TermsBody.Text = Strings.T(eigener ? "D_TermsBodyOwn" : "D_TermsBody");
        TermsAgreeText.Text = Strings.T(eigener ? "D_TermsAgreeOwn" : "D_TermsAgree");
        TermsNote.Text = Strings.T(eigener ? "D_TermsNoteOwn" : "D_TermsOwnRelay");

        // Keine Links auf meine Texte, wenn sie nicht gelten.
        TermsLinks.Visibility = eigener ? Visibility.Collapsed : Visibility.Visible;

        /* Die Ziele der beiden Links entstehen hier, nicht im Markup.
         *
         * Sie zeigen auf die Rechtstexte des VOREINGESTELLTEN Relays, und das ist
         * richtig so - sie gelten fuer ihn und fuer keinen anderen. Falsch waere nur,
         * den Namen ins Markup zu schreiben: dann stuende an einer zweiten Stelle, wo
         * FrameFlip hinzeigt, und ein Umzug des Servers braeuchte eine Suche statt
         * einer Konstanten.
         *
         * Wer einen eigenen Wirt eingetragen hat, sieht die Zeile ohnehin nicht. */
        if (!eigener)
        {
            TermsUseLink.NavigateUri = Recht("nutzungsbedingungen");
            TermsPrivacyLink.NavigateUri = Recht("datenschutz");
        }

        /* Die Kopplungstafel weicht.
         *
         * Sie liegt jetzt zwar unter der Frage und nicht mehr darueber, aber ein
         * Kopplungscode hinter der Frage ist trotzdem falsch: Er laesst sich nicht
         * einloesen, solange nichts nach draussen geht. Wer zustimmt, bekommt ihn
         * zurueck - wer nicht, hat ihn nie gebraucht. */
        _pairWasOpen = PairHost.Visibility == Visibility.Visible;
        PairHost.Visibility = Visibility.Collapsed;

        TermsAgree.IsChecked = false;
        TermsGo.IsEnabled = false;
        TermsHost.Visibility = Visibility.Visible;
        TermsScroll.ScrollToTop();

        // Den Tastaturschein auf die Tafel holen, damit Esc und Tab dort wirken.
        TermsAgree.Focus();
    }

    /// <summary>Stand die Kopplungstafel offen, als die Frage kam?</summary>
    private bool _pairWasOpen;

    /// <summary>
    /// Die Adresse eines Rechtstextes beim voreingestellten Relay.
    ///
    /// Die einzige Stelle im ganzen Programm, die einen bestimmten Wirtsnamen nennt -
    /// und sie nennt ihn nicht selbst, sondern holt ihn aus
    /// <see cref="AppSettings.DefaultRelayHost"/>. Zieht der Server um, aendert sich
    /// eine Konstante und nicht eine unbekannte Zahl von Zeichenketten.
    /// </summary>
    private static Uri Recht(string seite)
        => new($"https://{AppSettings.DefaultRelayHost}/recht/{seite}.html");

    private void OnTermsChecked(object sender, RoutedEventArgs e)
    {
        if (TermsGo is null) return;

        TermsGo.IsEnabled = TermsAgree.IsChecked == true;
    }

    private void OnTermsLater(object sender, RoutedEventArgs e)
    {
        _afterTerms = null;
        _pairWasOpen = false;
        TermsHost.Visibility = Visibility.Collapsed;
        RestorePageFocus();

        Note(Strings.T("D_TermsDeclined"));
    }

    private void OnTermsAccept(object sender, RoutedEventArgs e)
    {
        if (TermsAgree.IsChecked != true) return;

        var settings = _getSettings().Clone();
        settings.TermsAccepted = AppSettings.TermsVersion;

        if (_apply(settings) is { } error)
        {
            Note(error);
            return;
        }

        TermsHost.Visibility = Visibility.Collapsed;
        Note(Strings.T("D_TermsAccepted"));

        // Wer den Kopplungscode sehen wollte, bekommt ihn jetzt - diesmal einen,
        // der sich auch einloesen laesst.
        if (_pairWasOpen) { PairHost.Visibility = Visibility.Visible; PairCloseButton.Focus(); }
        else RestorePageFocus();
        _pairWasOpen = false;

        var dann = _afterTerms;
        _afterTerms = null;

        // Eine Runde spaeter: Der Wirt hat den neuen Stand dann uebernommen, und
        // was jetzt eingeschaltet wird, ueberlebt das naechste Normalize.
        if (dann is not null) Dispatcher.BeginInvoke(dann, DispatcherPriority.Background);
    }

    /// <summary>
    /// Oeffnet einen der Rechtstexte im Browser. Hyperlink.NavigateUri folgt von sich
    /// aus nichts - das ist gut so, sonst liesse sich aus einer Zeichenkette in den
    /// Einstellungen ein Programmstart machen.
    /// </summary>
    private void OnTermsLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;

        string ziel = e.Uri?.ToString() ?? string.Empty;

        // Nur https, und nur weil es hier fest im Markup steht. Ein Pfad oder ein
        // anderes Schema hat in einem ShellExecute nichts verloren.
        if (!ziel.StartsWith("https://", StringComparison.Ordinal)) return;

        try
        {
            Process.Start(new ProcessStartInfo(ziel) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Kein Browser, keine Zuordnung - die Tafel bleibt trotzdem bedienbar.
        }
    }

    /// <summary>Ein Klick neben die Tafel schliesst sie - auf die Tafel selbst nicht.</summary>
    private void OnPairBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PairHost)) return;

        OnPairClose(sender, e);
    }

    /// <summary>
    /// Was auf der Tafel steht, haengt an drei Dingen: ob die Fernsteuerung an ist,
    /// ob ein Schluessel abgelegt wurde und ob die Relay-Adresse brauchbar ist.
    /// Fehlt eines davon, steht statt des Codes der Weg dorthin - als Knopf, nicht
    /// als Aufforderung, woanders nachzusehen.
    /// </summary>
    private void RefreshPairing()
    {
        PairActions.Children.Clear();

        var settings = _getSettings();

        PairingStore.TryUnprotect(settings.PairingSecret, out PairingKey? stored);

        bool ready = settings.RemoteEnabled
                     && stored is not null
                     && PairingInvite.IsUsableHost(settings.RelayHost);

        if (!ready)
        {
            PairCode.Text = null;
            PairCodeFrame.Visibility = Visibility.Collapsed;
            PairRoom.Text = string.Empty;
            PairHint.Text = Strings.T("D_PairOff");

            PairActions.Children.Add(PairAction("D_PairEnable", primary: true, () => EnablePairing()));
            PairActions.Children.Add(PairAction("D_NavSettings", primary: false, () =>
            {
                PairHost.Visibility = Visibility.Collapsed;
                ShowSettingsPage(pairing: true);
            }));

            return;
        }

        PairingInvite invite;

        try { invite = new PairingInvite(stored!, settings.RelayHost); }
        catch (ArgumentException)
        {
            PairCodeFrame.Visibility = Visibility.Collapsed;
            PairHint.Text = Strings.T("S_NoRelayYet");
            return;
        }

        PairCodeFrame.Visibility = Visibility.Visible;
        PairCode.LightModules = _layout.LightQr;
        PairCode.Text = invite.Text;
        PairRoom.Text = Strings.T("S_RoomLabel", invite.Key.RoomId);
        PairHint.Text = Strings.T("S_ScanHint");

        PairActions.Children.Add(PairAction("S_CopyLink", primary: false, () => CopyInvite(invite.Text)));
        PairActions.Children.Add(PairAction("S_NewKey", primary: false, () => EnablePairing(fresh: true)));
    }

    private Button PairAction(string key, bool primary, Action click)
    {
        var label = new TextBlock { FontSize = 9.5, TextAlignment = TextAlignment.Center };
        Track.SetAmount(label, 1);
        Track.SetText(label, Strings.T(key).ToUpperInvariant());

        var button = new Button
        {
            Style = (Style)FindResource(primary ? "DashPrimary" : "DashGhost"),
            Margin = new Thickness(0, 0, 6, 6),
            MinWidth = 128,
            Height = 34,
            Content = label,
        };

        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// Die Fernsteuerung einschalten und dabei einen Schluessel anlegen.
    ///
    /// Ein neuer Schluessel macht jedes zuvor gekoppelte Geraet ungueltig - deshalb
    /// entsteht er nur auf Verlangen, nie beim blossen Ansehen der Tafel.
    /// </summary>
    private void EnablePairing(bool fresh = false)
    {
        var settings = _getSettings().Clone();

        if (!PairingInvite.IsUsableHost(settings.RelayHost)) settings.RelayHost = AppSettings.DefaultRelayHost;

        if (fresh || !PairingStore.TryUnprotect(settings.PairingSecret, out _))
        {
            string secret = PairingStore.Protect(PairingKey.Create());

            if (secret.Length == 0)
            {
                PairHint.Text = Strings.T("D_PairFailed");
                return;
            }

            settings.PairingSecret = secret;
        }

        settings.RemoteEnabled = true;

        if (_apply(settings) is { } error)
        {
            PairHint.Text = error;
            return;
        }

        Note(Strings.T("D_LogPairing"));
        RefreshPairing();
        RefreshLink();
    }

    private void CopyInvite(string text)
    {
        try
        {
            Clipboard.SetText(text);
            PairHint.Text = Strings.T("S_Copied");
        }
        catch (Exception)
        {
            // Die Zwischenablage kann von einem anderen Programm belegt sein.
            PairHint.Text = Strings.T("S_NoClipboard");
        }
    }

    private void OnOpenSequenceFolder(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_current.Folder}\"")
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception)
        {
            // Ohne Explorer geht es eben nicht.
        }
    }

    // ================================================================ Auffrischen

    private void OnFrameWritten(string path)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Note(Strings.T("D_LogSaved", Path.GetFileName(path)));
            if (_panel == "log") RefreshLog();

            // Liegt der Frame im Ordner, den das Dashboard gerade zeigt, gehoert er
            // auf die Buehne. Liegt er woanders, geht er nur ins Protokoll.
            if (_current is { } shown && shown.HasOutput
                && Path.GetDirectoryName(path) is { } folder
                && string.Equals(folder.TrimEnd('\\', '/'), shown.Folder.TrimEnd('\\', '/'),
                                 StringComparison.OrdinalIgnoreCase))
            {
                _live.NoteNewFrames();
            }
        }));
    }

    private void Note(string line)
    {
        _log.Insert(0, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line);

        if (_log.Count > 60) _log.RemoveAt(_log.Count - 1);
    }

    private void Refresh()
    {
        // Haengt am selben Takt wie alles Langsame. Neu gezeichnet wird nur, wenn
        // sich die Zeilen wirklich geaendert haben.
        RefreshReadiness();

        RenderJob? job = _monitor?.Job;
        bool running = job?.IsRunning == true;

        // Ein Render, der gerade angefangen hat, traegt seine .blend-Datei ueber die
        // Bruecke ein - die Liste daneben wuesste sonst bis zum naechsten Start
        // nichts davon. Nur beim Wechsel der Auftragsnummer, nicht bei jedem Takt.
        if (job?.Id is { Length: > 0 } id && id != _lastJobId)
        {
            _lastJobId = id;
            ReloadSequencesKeepingSelection();
        }

        // ---------------------------------------------------------- Titelzeile
        StatusDot.Fill = running ? (Brush)FindResource("DesktopCyan") : (Brush)FindResource("DesktopFaint");

        TitleInfo.Text = job is null
            ? Environment.MachineName.ToUpperInvariant() + " · " + Strings.T("D_Idle")
            : string.Format(CultureInfo.InvariantCulture, "{0} · {1} {2:0000}/{3:0000}",
                            Environment.MachineName.ToUpperInvariant(),
                            running ? Strings.T("D_Rendering") : Strings.T("D_Idle"),
                            job.CurrentFrame, job.LastFrame);

        double progress = job is { IsAnimation: true } ? job.Progress : 0;
        Hairline.Width = progress * Math.Max(0, ActualWidth);

        // ---------------------------------------------------------- Metriken
        BigPercent.Text = (progress * 100).ToString("0", CultureInfo.InvariantCulture);

        RemainValue.Text = job?.Remaining is { } remaining
            ? remaining.ToString(remaining.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture)
            : "—";

        EtaLine.Text = job is null
            ? string.Empty
            : Strings.T("D_Elapsed", job.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture));

        double panelWidth = Math.Max(0, PanelMetrics.ActualWidth);
        double sampleShare = job?.Stats.SampleProgress ?? 0;

        BarDone.Width = panelWidth * progress;
        BarLive.Width = job is null ? 0 : panelWidth * sampleShare / Math.Max(1, job.TotalFrames);
        SampleBar.Width = panelWidth * sampleShare;

        FrameCounter.Text = job is null
            ? "—"
            : Strings.T("D_FrameOf", job.CurrentFrame.ToString("0000", CultureInfo.InvariantCulture),
                        job.LastFrame.ToString("0000", CultureInfo.InvariantCulture));

        SampleCounter.Text = job?.Stats.Sample is { } sample
            ? Strings.T("D_SampleOf", sample, job.Stats.SampleTotal ?? 0)
            : "—";

        AverageLabel.Text = job?.SecondsPerFrame is { } spf
            ? spf.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : "—";

        UpdateGraph(job);
        UpdateTiles(job);

        // Der Schalter steht nur da, wo es etwas zu verfolgen gibt. Der Punkt
        // leuchtet mit, solange der Kopf wirklich auf dem letzten Bild steht -
        // sonst waere "Neueste" eine Behauptung statt einer Anzeige.
        FollowToggle.Visibility = _sequence is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        bool hasFrames = _sequence is { Count: > 0 };
        TransportControls.IsEnabled = ViewChips.IsEnabled = hasFrames;
        StageAnnotations.Visibility = hasFrames ? Visibility.Visible : Visibility.Collapsed;
        NewestDot.Opacity = _sequence is { } shown && _playback.Head == shown.EndNumber ? 1 : 0.35;

        // ---------------------------------------------------------- Protokoll
        PhaseText.Text = job?.Stats.Activity ?? Strings.T("D_NoActivity");

        // ---------------------------------------------------------- Fernsteuerung
        RefreshLink();
        RefreshStatusBar(job);

        RangeInfo.Text = _sequence is null
            ? string.Empty
            : Strings.T("D_InOut",
                        _playback.InPoint.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture),
                        _playback.OutPoint.ToString(_sequence.NumberFormat, CultureInfo.InvariantCulture),

                        // Vorhandene Bilder, nicht die Spanne: Bei einem abgebrochenen
                        // Render liegen zwischen Start und Ende weniger Dateien, als
                        // die Differenz verspricht - und exportiert werden die Dateien.
                        _sequence.Frames.Count(f => f.Number >= _playback.InPoint && f.Number <= _playback.OutPoint));
    }

    private void UpdateGraph(RenderJob? job)
    {
        FrameGraph.Clear();

        double[] durations = job?.FrameDurations ?? Array.Empty<double>();
        if (durations.Length == 0) return;

        double max = durations.Max();
        if (max <= 0) return;

        FrameGraph.LineBrush = (Brush)FindResource("DesktopBlue");

        foreach (double value in durations.TakeLast(28)) FrameGraph.Add(value / max);
    }

    /// <summary>
    /// Die sechs Kacheln entstehen einmal; danach wandern nur noch Zahl und
    /// Balkenbreite. Sie jede Sekunde neu zu bauen waere einfacher zu schreiben,
    /// wuerde aber sechsmal je Takt den Layoutbaum austauschen - und dabei jedes
    /// Mal die Automatisierungsknoten wegwerfen, an denen eine Sprachausgabe haengt.
    /// </summary>
    private void BuildTiles()
    {
        if (_tiles.Count > 0) return;

        AddTile("D_GpuLoad", "%", "DesktopAccent", 100);
        AddTile("D_Cpu", "%", "DesktopBlue", 100);
        AddTile("D_Ram", " GB", "DesktopBlue", 64);
        AddTile("D_Cycles", " M", "DesktopCyan", 8192);
        AddTile("D_Written", "", "DesktopAccent", 1);
        AddTile("D_Slowest", " s", "DesktopWarn", 60);
    }

    private void UpdateTiles(RenderJob? job)
    {
        BuildTiles();

        LoadSnapshot? load = LivePage.Load();

        _tiles[0].Set(load?.GpuPercent);
        _tiles[1].Set(load?.CpuPercent);
        _tiles[2].Set(load?.AvailableMb is { } mb ? mb / 1024.0 : null);
        _tiles[3].Set(job?.Stats.MemoryMb);

        _tiles[4].Full = Math.Max(1, job?.TotalFrames ?? 1);
        _tiles[4].Set(job?.FramesWritten);

        _tiles[5].Set(job?.SlowestFrame);
    }

    private void AddTile(string key, string unit, string colour, double full)
    {
        var stack = new StackPanel();

        var caption = new TextBlock { Style = (Style)FindResource("DashLabel"), FontSize = 9 };
        Track.SetText(caption, Strings.T(key));
        stack.Children.Add(caption);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };

        var value = new TextBlock { Style = (Style)FindResource("DashDisplay"), FontSize = 24, Text = "—" };
        line.Children.Add(value);

        if (unit.Length > 0)
        {
            line.Children.Add(new TextBlock
            {
                Text = unit,
                Margin = new Thickness(4, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Style = (Style)FindResource("DashLabel"),
                FontSize = 10,
            });
        }

        stack.Children.Add(line);

        var fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            Background = (Brush)FindResource(colour),
        };

        var track = new Border
        {
            Height = 3,
            Margin = new Thickness(0, 7, 0, 0),
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            ClipToBounds = true,
            Child = fill,
        };

        stack.Children.Add(track);

        var tile = new MetricTile(caption, value, track, fill, full) { Key = key };

        // Die Breite steht erst nach dem Layout fest; der Balken zieht nach, und
        // zwar auch dann, wenn sich die rechte Spalte spaeter noch bewegt.
        track.SizeChanged += (_, _) => tile.Paint();

        _tiles.Add(tile);

        Tiles.Children.Add(new Border
        {
            Style = (Style)FindResource("DashTile"),
            Margin = new Thickness(0, 0, 4, 4),
            Child = stack,
        });
    }

    /// <summary>Eine Kachel und ihre beiden veraenderlichen Teile.</summary>
    private sealed class MetricTile
    {
        private readonly TextBlock _caption;
        private readonly TextBlock _value;
        private readonly Border _track;
        private readonly Border _fill;

        private double? _current;

        public MetricTile(TextBlock caption, TextBlock value, Border track, Border fill, double full)
        {
            _caption = caption;
            _value = value;
            _track = track;
            _fill = fill;
            Full = full;
        }

        public string Key { get; init; } = string.Empty;

        public double Full { get; set; }

        public void Set(double? value)
        {
            _current = value;

            // Ein nicht gemessener Wert ist ein Gedankenstrich, keine Null - die
            // Regel gilt in der Handy-App genauso.
            _value.Text = value is null
                ? "—"
                : value.Value.ToString(value.Value >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);

            Paint();
        }

        public void Paint() =>
            _fill.Width = _current is null
                ? 0
                : Math.Clamp(_current.Value / Math.Max(1, Full), 0, 1) * _track.ActualWidth;

        public void Relabel() => Track.SetText(_caption, Strings.T(Key));
    }

    private void RefreshLog()
    {
        LogLines.Children.Clear();

        foreach (string line in _log)
        {
            LogLines.Children.Add(new TextBlock
            {
                Text = line,
                Margin = new Thickness(0, 0, 0, 3),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)FindResource("DashMono"),
            });
        }
    }

    private void RefreshLink()
    {
        var state = _remoteState();

        var (colour, word) = state switch
        {
            RelayState.Paired => ("DesktopCyan", Strings.T("S_PhoneConnected")),
            RelayState.Waiting => ("DesktopAccent", Strings.T("S_LinkWaiting")),
            RelayState.Connecting => ("DesktopMuted", Strings.T("S_Connecting")),
            _ => ("DesktopFaint", Strings.T("S_LinkOff")),
        };

        LinkDot.Fill = (Brush)FindResource(colour);
        LinkText.Text = word;
        LinkDetail.Text = state is null ? Strings.T("S_LinkTurnOn") : _getSettings().RelayHost;
    }

    private void RefreshStatusBar(RenderJob? job)
    {
        StatusFormat.Text = job is { Width: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, "{0}×{1}", job.Width, job.Height)
            : _current is { Width: > 0 }
                ? string.Format(CultureInfo.InvariantCulture, "{0}×{1}", _current.Width, _current.Height)
                : "—";

        StatusDecode.Text = _sequence?.Pattern.Extension.TrimStart('.').ToUpperInvariant() ?? string.Empty;
        StatusFps.Text = Rate(_playback.Fps) + " fps";
        StatusMissing.Text = _missing.Count > 0 ? Strings.T("D_NMissing", _missing.Count) : string.Empty;
        StatusHotkey.Text = _getSettings().Hotkey + " · " + Strings.T("D_Overlay");

        StatusRelay.Text = _remoteState() switch
        {
            RelayState.Paired => Strings.T("D_RelayConnected"),
            RelayState.Waiting => Strings.T("D_RelayWaiting"),
            _ => Strings.T("D_RelayOff"),
        };
    }

    private void OnLayoutChanged()
    {
        ApplyScale();
        ApplyDashboardLayout();
        PairCode.LightModules = WatchCode.LightModules = _layout.LightQr;

        // Der Streifen misst in geraetunabhaengigen Punkten; nach einer neuen
        // Skalierung passen andere Zellenzahlen ins selbe Fenster.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_sequence is null) return;

            BuildStrip();
            UpdateStripSelection();
            DrawScrub();
        }), DispatcherPriority.Loaded);
    }

    private void OnLanguageChanged()
    {
        // Die zusammengesetzten Texte haengen nicht an DynamicResource.
        foreach (var tile in _tiles) tile.Relabel();

        BuildGaps();
        Refresh();
    }

    private DateTime _lastSaved = DateTime.MinValue;

    private void Remember()
    {
        if (_persist is null) return;

        var settings = _getSettings();

        if (WindowState == WindowState.Normal)
        {
            settings.MainLeft = Left;
            settings.MainTop = Top;
            settings.MainWidth = Width;
            settings.MainHeight = Height;
        }

        settings.MainMaximized = WindowState == WindowState.Maximized;

        if (DateTime.UtcNow - _lastSaved < TimeSpan.FromSeconds(2)) return;

        _lastSaved = DateTime.UtcNow;
        _persist(settings);
    }
}
