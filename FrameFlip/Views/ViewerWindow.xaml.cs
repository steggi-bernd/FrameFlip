using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameFlip.Caching;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Imaging;
using FrameFlip.Interop;
using FrameFlip.Playback;
using FrameFlip.Sequencing;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen diese Typen genauso.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

public partial class ViewerWindow : Window
{
    private const int FallbackLookback = 8;
    private const double ZoomStep = 1.2;      // wie QuickLook: spuerbare, gleichmaessige Schritte
    private const int RedecodeDelayMs = 150;  // Zoom-Ruhe vor dem Nachschaerfen
    private const double RedecodeThreshold = 0.12;   // erst ab 12 % Groessenunterschied neu laden
    private const int FadeMs = 120;                  // Ein- und Ausblenden des Fensters

    /// <summary>Mindestvorlauf in Sekunden, unabhaengig von der eingestellten Framezahl.</summary>
    private const double PrefetchSeconds = 2.0;

    // Nicht readonly: beim Wechsel auf eine andere Sequenz wird der Inhalt getauscht,
    // statt ein zweites Fenster zu oeffnen.
    private ImageSequence _sequence;
    private string _numberFormat;
    private int _sourceWidth;
    private int _sourceHeight;

    private readonly FrameDecoderRegistry _decoders;
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _persist;
    private readonly PixelRect _bounds;
    private readonly int _maxWorkers;
    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _bufferTimer;
    private readonly DispatcherTimer _redecodeTimer;
    private readonly DispatcherTimer _statusTimer;

    private FrameCache? _cache;
    private WriteableBitmap? _surface;
    private ResourceProfile _profile = ResourceProfile.Conservative;

    private int _index;
    private int _shownIndex = -1;
    private int _direction = 1;
    private int _pendingIndex = -1;
    private bool _loop;
    private volatile bool _playing;
    private volatile bool _closing;
    private bool _buffering;
    private bool _resumeAfterBuffering;
    private long _bufferingSince;

    private bool _initializing = true;
    private bool _suppressScrubber;
    private bool _scrubbing;
    private bool _resumeAfterScrub;
    private bool _barVisible = true;

    /// <summary>In- und Out-Punkt als Listenposition, -1 wenn nicht gesetzt.</summary>
    private int _inPoint = -1;
    private int _outPoint = -1;

    private bool _closeAnimating;
    private ExportWindow? _exportWindow;

    /// <summary>
    /// Haelt die Vorschau offen, solange ein eigener Dialog den Fokus hat. Ohne das
    /// verschwindet das Fenster in dem Moment, in dem der Dialog aufgeht - und mit
    /// ihm der Zusammenhang, auf den sich der Dialog bezieht.
    /// </summary>
    public bool ModalDialogOpen { get; set; }

    /// <summary>Farbtiefe je Kanal aus dem Dateikopf, 0 wenn unbekannt.</summary>
    private int _bitsPerChannel;

    private readonly System.Diagnostics.Stopwatch _rateWindow = new();
    private int _presentedInWindow;

    /// <summary>Misst den echten Bildschirmtakt aus den Zeichenschritten.</summary>
    private readonly RefreshEstimator _refresh = new();
    private readonly System.Diagnostics.Stopwatch _refreshWatch = System.Diagnostics.Stopwatch.StartNew();

    private double _dpiX = 1.0;
    private double _dpiY = 1.0;

    /// <summary>Zoom und Versatz. Haelt die einzige Wahrheit ueber die Darstellung.</summary>
    private readonly ZoomController _view = new();

    private bool _panning;
    private Point _dragOrigin;

    /// <summary>Zweite Cachestufe auf der Platte, oder null.</summary>
    private RawFrameCache? _rawCache;

    /// <summary>Gesetzt, wenn waehrend der Wiedergabe gezoomt wurde. Wird beim Pausieren eingeloest.</summary>
    private bool _redecodeDeferred;
    private bool _deferRedecode = true;

    public ViewerWindow(ImageSequence sequence, int startIndex, AppSettings settings,
                        Action<AppSettings> persist, FrameDecoderRegistry decoders,
                        PixelRect bounds, int sourceWidth, int sourceHeight, int maxWorkers)
    {
        _sequence = sequence;
        _decoders = decoders;
        _settings = settings;
        _persist = persist;
        _sourceWidth = Math.Max(1, sourceWidth);
        _sourceHeight = Math.Max(1, sourceHeight);
        _maxWorkers = Math.Max(1, maxWorkers);
        _index = Math.Clamp(startIndex, 0, Math.Max(0, sequence.Count - 1));
        _loop = settings.Loop;
        _numberFormat = sequence.NumberFormat;

        InitializeComponent();

        _bounds = bounds;

        _view.SetNativeSize(_sourceWidth, _sourceHeight);
        _view.Changed += OnViewChanged;

        _clock.Fps = settings.Fps;
        _clock.LockToDisplay = settings.LockToDisplay;

        FpsBox.ItemsSource = FpsOption.All;
        FpsBox.SelectedItem = FpsOption.Closest(settings.Fps);
        LoopButton.IsChecked = _loop;

        // Die Zeitleiste laeuft ueber Framenummern, nicht ueber Listenpositionen -
        // sonst waeren Luecken nicht darstellbar.
        Scrubber.Minimum = sequence.StartNumber;
        Scrubber.Maximum = Math.Max(sequence.StartNumber, sequence.EndNumber);
        Scrubber.Value = sequence.Frames[_index].Number;
        Scrubber.ValueChanged += OnScrubberValueChanged;
        Scrubber.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnScrubStarted));
        Scrubber.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnScrubCompleted));

        GapStrip.GapBrush = (Brush)FindResource("GapBrush");
        ShowGaps();

        _draftStep = Math.Clamp(settings.DraftStep, 0, DraftScales.Length - 1);
        UpdateDraftButton();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _hideTimer.Tick += OnHideTick;

        _bufferTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _bufferTimer.Tick += OnBufferTick;

        _redecodeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(RedecodeDelayMs) };
        _redecodeTimer.Tick += OnRedecodeTick;

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += OnStatusTick;

        // Erst nach den Timern: das Panel kann beim Aufklappen die Bedienleiste
        // einblenden, und die haengt am Ausblendtimer.
        InitialisePanel();

        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += OnMouseMoved;
        MouseWheel += OnMouseWheel;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;

        Header.MouseLeftButtonDown += OnHeaderDrag;
        Viewport.MouseLeftButtonDown += OnViewportMouseDown;
        Viewport.MouseLeftButtonUp += OnViewportMouseUp;
        Viewport.MouseMove += OnViewportMouseMove;
        Viewport.MouseRightButtonUp += OnViewportRightClick;
        Viewport.MouseDown += OnViewportOtherButtonDown;
        Viewport.MouseUp += OnViewportOtherButtonUp;

        _initializing = false;
    }

    // ------------------------------------------------------------ Lebenszyklus

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        NativeMethods.SizeAndCenter(handle, _bounds.Width, _bounds.Height);

        // Abgerundete Ecken und dunkler Rahmen wie bei den Systemfenstern.
        NativeMethods.ApplyDarkWindowFrame(handle);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        _dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        _view.SetDpi(_dpiX);

        // Erzwingt den Layoutdurchlauf, bevor die Viewportgroesse gelesen wird. Ohne
        // das ist ActualWidth beim ersten Loaded noch 0, der Einpassmassstab bliebe
        // unbekannt und der erste Puffer wuerde in voller Aufloesung dekodiert -
        // teuer in Zeit und Speicher, und beim Abspielen wird das nicht mehr
        // korrigiert, weil das Nachschaerfen dann wartet.
        UpdateLayout();
        SyncViewport();

        CreateCache(RequiredDecodeWidth(), RequiredDecodeHeight());

        Volatile.Write(ref _pendingIndex, _index);
        PresentFrame(_index);

        FileNameText.Text = _sequence.Frames[_index].FileName;
        UpdateCounter(_index);

        ProbeBitDepth();
        UpdateMetadata(_index);

        // Erst puffern, dann abspielen. Sonst laeuft die Uhr los, waehrend der
        // Decoder noch fuellt - genau das erzeugt das Ruckeln beim Start.
        if (_sequence.Count > 1) EnterBuffering(resume: true, reason: "Start");

        Activate();
        Focus();
        ShowBar();

        // Einblenden erst, wenn der erste Frame steht - sonst blendet ein leeres
        // Fenster auf und das Bild springt danach hinein.
        BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(FadeMs)));
    }

    /// <summary>
    /// Farbtiefe einmalig aus dem Dateikopf lesen. Innerhalb einer Rendersequenz ist
    /// sie konstant, ein Zugriff je Frame waere also nur Last ohne Erkenntnis.
    /// </summary>
    private void ProbeBitDepth()
    {
        _bitsPerChannel = 0;
        if (_sequence.Count == 0) return;

        if (_decoders.TryProbeInfo(_sequence.Frames[_index].Path, out var info))
            _bitsPerChannel = info.BitsPerChannel;
    }

    /// <summary>
    /// Klick ausserhalb schliesst - das QuickLook-Verhalten.
    ///
    /// Die Ausnahme fuer eigene Dialoge ist keine Feinheit: ohne sie verschwindet das
    /// Fenster genau in dem Moment, in dem der Export- oder Einstellungsdialog den
    /// Fokus uebernimmt, und mit ihm der Kontext, auf den sich der Dialog bezieht.
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (_closing || ModalDialogOpen) return;
        if (!_settings.CloseOnFocusLoss) return;

        BeginClose();
    }

    /// <summary>Blendet aus und schliesst danach. Ohne laufende Animation sofort.</summary>
    public void BeginClose()
    {
        if (_closing || _closeAnimating) return;

        _closeAnimating = true;

        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(FadeMs));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// True, wenn bereits genau diese Sequenz gezeigt wird. Verglichen wird das
    /// Muster, nicht der Bestand: waehrend eines laufenden Renders kommen staendig
    /// Frames dazu, und dieselbe Sequenz waere sonst gleich eine andere.
    /// </summary>
    public bool ShowsSameSequence(ImageSequence other)
        => string.Equals(_sequence.Pattern.Directory, other.Pattern.Directory,
                         StringComparison.OrdinalIgnoreCase)
        && string.Equals(_sequence.Pattern.Prefix, other.Pattern.Prefix,
                         StringComparison.OrdinalIgnoreCase)
        && string.Equals(_sequence.Pattern.Suffix, other.Pattern.Suffix,
                         StringComparison.OrdinalIgnoreCase)
        && string.Equals(_sequence.Pattern.Extension, other.Pattern.Extension,
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tauscht den Inhalt, statt ein zweites Fenster zu oeffnen. Der Zoomzustand
    /// wird bewusst verworfen: die neue Sequenz kann eine ganz andere Aufloesung
    /// haben, und ein uebernommener Massstab waere dort willkuerlich.
    /// </summary>
    public bool TryLoadSequence(ImageSequence sequence, int startIndex,
                                int sourceWidth, int sourceHeight)
    {
        if (_closing) return false;

        Pause();

        _sequence = sequence;
        _numberFormat = sequence.NumberFormat;
        _sourceWidth = Math.Max(1, sourceWidth);
        _sourceHeight = Math.Max(1, sourceHeight);
        _index = Math.Clamp(startIndex, 0, Math.Max(0, sequence.Count - 1));
        _shownIndex = -1;
        _direction = 1;
        _inPoint = -1;
        _outPoint = -1;

        _view.SetNativeSize(_sourceWidth, _sourceHeight);

        _suppressScrubber = true;
        Scrubber.Minimum = sequence.StartNumber;
        Scrubber.Maximum = Math.Max(sequence.StartNumber, sequence.EndNumber);
        Scrubber.Value = sequence.Frames[_index].Number;
        _suppressScrubber = false;

        ShowGaps();
        UpdateRangeText();
        ProbeBitDepth();

        // Die Bitmap hat noch die alte Groesse; sie wird beim naechsten Blit neu
        // angelegt. Der Regler bekommt den neuen Inhalt erst dann - bis dahin
        // wuerde er mit der alten Inhaltsgroesse rechnen.
        _surface = null;
        Display.Source = null;

        _view.SetContent(new Size(_sourceWidth, _sourceHeight), preserveZoom: false);
        _view.FitToViewport();

        CreateCache(RequiredDecodeWidth(), RequiredDecodeHeight());

        Volatile.Write(ref _pendingIndex, _index);
        PresentFrame(_index);

        FileNameText.Text = sequence.Frames[_index].FileName;
        UpdateCounter(_index);
        UpdateMetadata(_index);

        Activate();
        ShowBar();

        if (sequence.Count > 1) EnterBuffering(resume: true, reason: "neue Sequenz");
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;

        if (_playing)
        {
            CompositionTarget.Rendering -= OnRendering;
            _playing = false;
        }

        _clock.Stop();
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTick;
        _bufferTimer.Stop();
        _bufferTimer.Tick -= OnBufferTick;
        _redecodeTimer.Stop();
        _redecodeTimer.Tick -= OnRedecodeTick;

        var cache = _cache;
        _cache = null;

        var raw = _rawCache;
        _rawCache = null;

        Display.Source = null;
        _surface = null;

        // Der Rohcache ist sitzungsgebunden: er gehoert zu genau dieser
        // Dekodiergroesse und waere beim naechsten Oeffnen womoeglich unpassend.
        if (raw is not null) Task.Run(raw.Dispose);

        if (cache is not null)
        {
            cache.FrameReady -= OnFrameReady;
            cache.BeginShutdown();

            // Nicht auf dem UI-Thread joinen: ein laufender Decode kann unter Renderlast dauern.
            Task.Run(() =>
            {
                cache.Dispose();
                MemoryTrimmer.TrimNow();
            });
        }
        else
        {
            Task.Run(MemoryTrimmer.TrimNow);
        }

        base.OnClosed(e);
    }

    // ------------------------------------------------------------ Lastprofil

    /// <summary>Wird vom AppHost aufgerufen, sobald eine neue Messung vorliegt.</summary>
    public void ApplyLoad(LoadSnapshot snapshot, ResourceProfile profile)
    {
        _profile = profile;
        _cache?.ApplyProfile(profile);

        string gpu = snapshot.GpuPercent is null ? "" : $" · GPU {snapshot.GpuPercent.Value:0} %";
        LoadText.Text = $"CPU {snapshot.CpuPercent:0} %{gpu} · {profile.DecoderThreads} Thr";
        LoadText.ToolTip = snapshot.Describe();
    }

    // ------------------------------------------------------------ Cache

    private void CreateCache(int decodeWidth, int decodeHeight)
    {
        // Erst den alten Ring leeren, dann den neuen aufbauen - sonst liegen beide
        // gleichzeitig im Speicher und das Budget waere kurzzeitig doppelt belegt.
        var previous = _cache;
        _cache = null;

        if (previous is not null)
        {
            previous.FrameReady -= OnFrameReady;
            previous.BeginShutdown();
            previous.ReleaseBuffers();
            Task.Run(previous.Dispose);
        }

        // Der Rohcache haengt an der Dekodiergroesse: dieselbe Sequenz in halber
        // Groesse ergibt andere Pixel, und ein Treffer darauf waere ein falsches Bild.
        ReplaceRawCache(decodeWidth, decodeHeight);

        var cache = new FrameCache(_sequence, _decoders, decodeWidth, decodeHeight,
                                   _settings.MemoryBudgetBytes, PrefetchAhead(), PrefetchBehind(),
                                   _loop, _maxWorkers, _profile, _rawCache);
        cache.FrameReady += OnFrameReady;
        cache.SetPosition(_index, _direction, _loop, urgent: true);
        _cache = cache;
    }

    /// <summary>
    /// Legt den Rohcache fuer die aktuelle Dekodiergroesse an und wirft den alten weg.
    /// </summary>
    private void ReplaceRawCache(int decodeWidth, int decodeHeight)
    {
        var previous = _rawCache;
        _rawCache = null;

        // Das Loeschen kann bei vielen Dateien dauern - nicht auf dem UI-Thread.
        if (previous is not null) Task.Run(previous.Dispose);

        if (!_settings.RawCacheEnabled) return;

        try
        {
            var key = string.Join('|', _sequence.Pattern.Directory, _sequence.Pattern.Prefix,
                                  _sequence.Pattern.Suffix, _sequence.Pattern.Extension,
                                  decodeWidth, decodeHeight);

            _rawCache = new RawFrameCache(key, (long)_settings.RawCacheMaxGb * 1024 * 1024 * 1024);
        }
        catch (Exception)
        {
            // Kein Schreibrecht im temporaeren Ordner: dann eben ohne zweite Stufe.
            _rawCache = null;
        }
    }

    /// <summary>
    /// Vorlauf in Frames, abgeleitet aus der Bildrate.
    ///
    /// Der Wert aus den Einstellungen ist eine Frameanzahl, und die bedeutet je nach
    /// Bildrate etwas voellig anderes: 60 Frames sind bei 24 fps zweieinhalb
    /// Sekunden Reserve, bei 60 fps nur eine. Genau dort faellt der Ring beim
    /// kleinsten Stocken trocken - und weil die Kapazitaet aus Vorlauf plus Ruecklauf
    /// folgt, half auch ein groesseres Speicherbudget nicht: der Ring blieb bei
    /// 76 Frames stehen, obwohl 240 hineingepasst haetten.
    ///
    /// Deshalb gilt der eingestellte Wert als Untergrenze, und darueber hinaus
    /// mindestens zwei Sekunden Material.
    /// </summary>
    private int PrefetchAhead()
    {
        int fromSeconds = (int)Math.Ceiling(_clock.Fps * PrefetchSeconds);
        return Math.Clamp(Math.Max(_settings.PrefetchAhead, fromSeconds), 1, 2000);
    }

    /// <summary>Ruecklauf im selben Verhaeltnis wie eingestellt.</summary>
    private int PrefetchBehind()
    {
        double ratio = _settings.PrefetchAhead > 0
            ? _settings.PrefetchBehind / (double)_settings.PrefetchAhead
            : 0.25;

        return Math.Clamp((int)Math.Round(PrefetchAhead() * ratio), 0, 2000);
    }

    /// <summary>
    /// Zweite Stufe des Nachschaerfens: nach kurzer Zoom-Ruhe in der passenden
    /// Aufloesung neu dekodieren. Die erste Stufe - die sofortige Skalierung ueber
    /// die Matrix - ist zu diesem Zeitpunkt laengst sichtbar.
    ///
    /// Bewusst nur im pausierten Zustand: waehrend der Wiedergabe wuerde ein
    /// Neuaufbau des Ringpuffers ein Nachpuffern erzwingen, und der Gewinn an
    /// Schaerfe faellt bei laufenden Bildern ohnehin nicht auf.
    /// </summary>
    private void OnRedecodeTick(object? sender, EventArgs e)
    {
        _redecodeTimer.Stop();
        if (_closing || _cache is null) return;

        if (_deferRedecode && (_playing || _buffering))
        {
            _redecodeDeferred = true;   // wird beim Pausieren eingeloest
            return;
        }

        _redecodeDeferred = false;

        int wanted = RequiredDecodeWidth();
        int current = _cache.DecodeWidth;
        if (current <= 0) return;

        double delta = Math.Abs(wanted - current) / (double)current;
        if (delta < RedecodeThreshold) return;

        bool wasPlaying = _playing;
        CreateCache(wanted, RequiredDecodeHeight());

        Volatile.Write(ref _pendingIndex, _index);
        if (wasPlaying || _buffering)
            EnterBuffering(resume: wasPlaying || _resumeAfterBuffering, reason: "neue Auflösung");
    }

    /// <param name="deferWhilePlaying">
    /// True fuer Zoom: das Nachschaerfen wartet, bis pausiert wird, damit die
    /// Wiedergabe nicht fuer einen Neuaufbau des Ringpuffers aussetzt.
    /// False fuer eine geaenderte Fenstergroesse: dort ist die alte Aufloesung
    /// dauerhaft falsch, und Warten wuerde ein unscharfes Bild stehen lassen.
    /// </param>
    private void ScheduleRedecode(bool deferWhilePlaying = true)
    {
        _deferRedecode = deferWhilePlaying;
        _redecodeTimer.Stop();
        _redecodeTimer.Start();
    }

    private int RequiredDecodeWidth()
        => Math.Clamp((int)Math.Ceiling(_sourceWidth * _view.Zoom * _draftScale), 16, _sourceWidth);

    private int RequiredDecodeHeight()
        => Math.Clamp((int)Math.Ceiling(_sourceHeight * _view.Zoom * _draftScale), 16, _sourceHeight);

    // ------------------------------------------------------------ Pufferstufe

    /// <summary>
    /// Waehlbare Dekodiergroesse. Der Name "Entwurf" waere irrefuehrend: gemessen
    /// spart das Verkleinern beim Dekodieren kaum Zeit (1080p: 35,7 ms bei voller,
    /// 31,1 ms bei viertel Groesse), weil WIC das PNG ohnehin vollstaendig
    /// entpacken muss, bevor es skalieren kann.
    ///
    /// Der Gewinn liegt im SPEICHER und damit im Vorlauf: bei 512 MB Budget passen
    /// 64 Frames in voller Aufloesung in den Ring, 258 bei halber, 1035 bei viertel.
    /// Aus 2,7 Sekunden Reserve werden 43. Deshalb ist es eine Pufferstufe.
    /// </summary>
    private static readonly double[] DraftScales = { 1.0, 0.5, 0.25 };

    private int _draftStep;

    private double _draftScale => DraftScales[_draftStep];

    private void SetDraftStep(int step)
    {
        step = Math.Clamp(step, 0, DraftScales.Length - 1);
        if (step == _draftStep) return;

        _draftStep = step;
        _settings.DraftStep = step;
        _persist(_settings);

        UpdateDraftButton();
        OnViewChanged();                       // Glaettung folgt der Stufe

        // Sofort neu aufbauen, nicht erst nach Zoom-Ruhe: die Stufe ist eine
        // ausdrueckliche Ansage des Benutzers, keine Nebenwirkung einer Geste.
        ScheduleRedecode(deferWhilePlaying: false);
        ShowBar();
    }

    private void CycleDraftStep() => SetDraftStep((_draftStep + 1) % DraftScales.Length);

    private void UpdateDraftButton()
    {
        DraftText.Text = $"{_draftScale * 100:0} %";

        DraftButton.ToolTip = _draftStep == 0
            ? "Dekodiergröße: voll. Klick verkleinert sie und verlängert damit den Puffer (Tasten 1/2/3)"
            : $"Dekodiergröße {_draftScale * 100:0} % – gröberes Bild, dafür {1 / (_draftScale * _draftScale):0}× mehr Vorlauf";

        DraftText.Foreground = (Brush)FindResource(_draftStep == 0 ? "MutedBrush" : "AccentBrush");
    }

    // ------------------------------------------------------------ Praesentation

    /// <summary>Wird auf einem Decoder-Thread ausgeloest - relevant, solange auf einen Frame gewartet wird.</summary>
    private void OnFrameReady(int index)
    {
        if (_closing || _playing) return;
        if (Volatile.Read(ref _pendingIndex) != index) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_closing) return;
            if (Volatile.Read(ref _pendingIndex) != index) return;
            if (PresentFrame(index)) Volatile.Write(ref _pendingIndex, -1);
        }));
    }

    private bool PresentFrame(int index)
    {
        var cache = _cache;
        if (cache is null || index < 0 || index >= _sequence.Count) return false;
        if (!cache.TryPresent(index, Blit)) return false;

        _shownIndex = index;
        _presentedInWindow++;
        FileNameText.Text = _sequence.Frames[index].FileName;
        UpdateCounter(index);
        UpdateScrubber(index);

        // Waehrend der Wiedergabe entfaellt der Dateisystemzugriff fuer die Groesse.
        if (!_playing) UpdateMetadata(index);
        return true;
    }

    /// <summary>
    /// Laeuft auf dem UI-Thread, aber ausserhalb des Cache-Locks. Die WriteableBitmap
    /// wird wiederverwendet und nur bei geaenderter Framegroesse neu angelegt.
    ///
    /// Die Bitmap hat IMMER exakt die Dimension des Frames, der hineingeschrieben
    /// wird. Es wird nie eine kleinere Region in eine groessere Bitmap geschrieben -
    /// das ist die Invariante, die uninitialisierte, also schwarze Bereiche
    /// ausschliesst.
    /// </summary>
    private void Blit(FrameBuffer buffer)
    {
        // Der Puffer kommt aus einem Pool, der nach Kapazitaet vergibt: das Array darf
        // laenger sein als die Nutzlast, der Stride niemals breiter als der Frame.
        Debug.Assert(buffer.Stride == buffer.Width * 4,
            "Stride muss dicht gepackt sein - die Poolgroesse darf ihn nicht beeinflussen.");
        Debug.Assert(buffer.Pixels.Length >= buffer.Stride * buffer.Height,
            "Puffer zu klein fuer die angegebene Framedimension.");

        if (_surface is null || _surface.PixelWidth != buffer.Width || _surface.PixelHeight != buffer.Height)
        {
            // 96 dpi, nicht 96 * Bildschirmskalierung: dadurch entspricht die
            // Layoutgroesse des Bildes in DIP exakt seiner Pixelzahl. Die
            // Bildschirmskalierung geht nur an einer einzigen Stelle ein, naemlich
            // im ZoomController.
            _surface = new WriteableBitmap(buffer.Width, buffer.Height, 96, 96,
                                           PixelFormats.Bgra32, null);
            Display.Source = _surface;

            // Setzt den Massstab so um, dass das Bild optisch an derselben Stelle in
            // derselben Groesse steht, obwohl der Puffer jetzt mehr Pixel hat.
            _view.SetContent(new Size(buffer.Width, buffer.Height), preserveZoom: true);
        }

        Debug.Assert(_surface.PixelWidth == buffer.Width && _surface.PixelHeight == buffer.Height,
            "Die Bitmap muss vor jedem Schreiben exakt die Framedimension haben.");

        // Direkt in den Rueckpuffer statt ueber WritePixels: die Anzeigekorrektur
        // laesst sich so im selben Durchgang anwenden, ohne einen Zwischenpuffer in
        // Framegroesse. Ohne Korrektur ist es ein reiner Speicherkopiervorgang.
        _surface.Lock();
        try
        {
            FrameProcessor.Apply(buffer.Pixels, buffer.Width, buffer.Height, buffer.Stride,
                                 _surface.BackBuffer, _surface.BackBufferStride, _adjustments);

            _surface.AddDirtyRect(new Int32Rect(0, 0, buffer.Width, buffer.Height));
        }
        finally
        {
            _surface.Unlock();
        }

        // Das Histogramm hier messen, solange der Puffer gueltig ist: gleich nach der
        // Rueckkehr gibt der Ringpuffer ihn frei, und der Pool vergibt ihn an den
        // naechsten Frame weiter. Eine Referenz aufzuheben waere ein Fehler.
        //
        // Gedrosselt, weil die Messung sonst bei jedem Bild anfiele, obwohl das Auge
        // vier Aktualisierungen je Sekunde ohnehin nicht unterscheidet.
        if (_panelOpen && _histogramDue.ElapsedMilliseconds >= 250)
        {
            _histogramDue.Restart();
            FrameProcessor.Measure(buffer.Pixels, buffer.Width, buffer.Height, buffer.Stride,
                                   _histogram, HistogramStep, _adjustments);
            HistogramView.Update(_histogram);
        }

        // Fuer den A/B-Vergleich wird beim Merken ausdruecklich kopiert - siehe MarkA().
        if (_captureRequested)
        {
            _captureRequested = false;
            CaptureReference(buffer);
        }
    }

    private void UpdateCounter(int index)
    {
        var frame = _sequence.Frames[index];
        CounterText.Text = frame.Number.ToString(_numberFormat) + " / " + _sequence.EndNumber.ToString(_numberFormat);
    }

    private void UpdateScrubber(int index)
    {
        if (_scrubbing) return;
        _suppressScrubber = true;
        Scrubber.Value = _sequence.Frames[index].Number;
        _suppressScrubber = false;
    }

    // ------------------------------------------------------------ Luecken

    /// <summary>
    /// Markiert die fehlenden Frames in der Zeitleiste und blendet die Hinweiszeile
    /// ein. Bei einem abgebrochenen Render ist das die wichtigste Information im
    /// ganzen Fenster.
    /// </summary>
    private void ShowGaps()
    {
        var missing = _sequence.MissingNumbers();

        GapStrip.SetSequence(_sequence.StartNumber, _sequence.EndNumber, missing);

        if (missing.Count == 0)
        {
            GapBar.Visibility = Visibility.Collapsed;
            return;
        }

        int ranges = SequenceMath.CountRanges(missing);
        string list = SequenceMath.FormatForDisplay(missing);

        GapText.Text = ranges == 1
            ? $"1 Luecke: {list}   ({missing.Count} von {_sequence.SpanLength} Frames fehlen)"
            : $"{ranges} Luecken: {list}   ({missing.Count} von {_sequence.SpanLength} Frames fehlen)";

        GapText.ToolTip = GapText.Text;
        GapBar.Visibility = Visibility.Visible;
    }

    private void OnCopyBlenderClicked(object sender, RoutedEventArgs e)
    {
        var missing = _sequence.MissingNumbers();
        if (missing.Count == 0) return;

        var command = BlenderCommand.BuildRepairCommand(_sequence, missing);

        try
        {
            // Die Zwischenablage ist ein systemweiter, exklusiv gesperrter Dienst;
            // ein anderer Prozess kann sie gerade halten. Kein Grund fuer einen Absturz.
            Clipboard.SetText(command);
            ShowStatus("Befehl kopiert");
        }
        catch (Exception)
        {
            ShowStatus("Zwischenablage nicht verfuegbar");
        }

        _statusTimer.Stop();
        _statusTimer.Start();
        ShowBar();
    }

    // ------------------------------------------------------------ Zoom und Verschieben

    /// <summary>
    /// Uebertraegt die Matrix des Reglers auf das Bild. Das ist der EINZIGE Ort, an
    /// dem der Zoom sichtbar wird - es gibt keinen Codepfad, der dabei eine
    /// Layoutgroesse, eine Puffergroesse oder eine Bitmapgroesse anfasst.
    /// </summary>
    private void OnViewChanged()
    {
        ViewTransform.Matrix = _view.Matrix;

        double zoom = _view.Zoom;
        ZoomText.Text = $"{zoom * 100:0} %";
        Viewport.Cursor = _view.CanPan ? Cursors.SizeAll : Cursors.Arrow;

        // Ueber 100 % sollen die Pixel scharf stehen bleiben - beim Beurteilen von
        // Renderdetails ist Glaetten genau das Falsche. Darunter wird geglaettet.
        //
        // In einer verkleinerten Pufferstufe waere NearestNeighbor unbrauchbar: dort
        // wird immer hochskaliert, und harte Kloetzchen verdecken genau das, was man
        // beurteilen will. Deshalb dort bilinear, das ist ausserdem billiger.
        RenderOptions.SetBitmapScalingMode(Display,
            _draftStep > 0 ? BitmapScalingMode.LowQuality
            : zoom > 1.0 ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality);
    }

    /// <summary>Uebernimmt die aktuelle Groesse des Anzeigebereichs in den Regler.</summary>
    private void SyncViewport()
    {
        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        _view.SetViewport(new Size(Viewport.ActualWidth, Viewport.ActualHeight));
    }

    /// <summary>
    /// Zeigerposition im Anzeigebereich, direkt von Win32 statt aus dem
    /// WPF-Eingabepfad: auf skalierten Systemen liegen die beiden auseinander,
    /// und der Zoom wuerde dann nicht dort ansetzen, wo der Zeiger wirklich steht.
    /// </summary>
    private Point CursorInViewport(Point fallback)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return fallback;
        if (!NativeMethods.TryGetCursorInClient(handle, out double clientX, out double clientY)) return fallback;

        // Der Anzeigebereich beginnt unterhalb der Kopfleiste.
        var offset = Viewport.TranslatePoint(new Point(0, 0), this);
        return new Point(clientX / _dpiX - offset.X, clientY / _dpiY - offset.Y);
    }

    /// <summary>Zoomt um n Rasten; der Bildpunkt unter dem Zeiger bleibt ortsfest.</summary>
    private void ZoomAt(Point anchor, int notches)
    {
        _view.ZoomBy(anchor, Math.Pow(ZoomStep, notches));
        ScheduleRedecode();
        ShowBar();
    }

    private Point ViewportCenter() => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    private void ZoomToFit()
    {
        _view.FitToViewport();
        ScheduleRedecode();
    }

    /// <summary>1:1 - ein Bildpixel je Geraetepixel, zum Beurteilen von Renderdetails.</summary>
    private void ZoomToActualPixels()
    {
        _view.ActualSize(ViewportCenter());
        ScheduleRedecode();
    }

    /// <summary>Doppelklick wechselt zwischen Einpassung und 100 %, wie in QuickLook.</summary>
    private void ToggleFitAndActual(Point anchor)
    {
        _view.ToggleFitAndActual(anchor);
        ScheduleRedecode();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncViewport();
        ScheduleRedecode(deferWhilePlaying: false);
    }

    // ------------------------------------------------------------ Wiedergabe

    private void OnRendering(object? sender, EventArgs e)
    {
        var cache = _cache;
        if (!_clock.IsRunning || cache is null) return;

        // Erst messen, dann schalten: RawTarget liest im gekoppelten Betrieb den
        // Zaehler, den Tick gerade fortschreibt.
        _refresh.Sample(_refreshWatch.Elapsed.TotalMilliseconds);
        _clock.ObserveDisplay(_refresh.NominalHz, _refresh.EffectiveHz);
        _clock.Tick();

        var (first, last) = ActiveRange();
        int target = SequenceMath.ResolveInRange(_clock.RawTarget, first, last, _loop, out bool pastEnd);
        if (pastEnd)
        {
            PresentFrame(target);
            _index = target;
            Pause();
            return;
        }

        UpdateMeasuredRate();

        if (target < 0 || target == _shownIndex) return;

        _index = target;
        cache.SetPosition(target, _direction, _loop, urgent: false);

        if (PresentFrame(target)) return;

        // Fehlt der Zielframe, aber es liegt noch etwas voraus: Frame verwerfen und
        // weiterlaufen. Ist der Ring leergelaufen, wird angehalten und nachgeladen.
        int fallback = cache.BestAvailableBefore(target, _shownIndex, FallbackLookback);
        if (fallback >= 0)
        {
            PresentFrame(fallback);
            return;
        }

        if (cache.ReadyAhead() != 0) return;

        // Der Ring ist leergelaufen: mehr Threads freigeben, statt bis zur naechsten
        // Lastmessung zu warten. Die kommt erst in bis zu zehn Sekunden, und so lange
        // stockt es sonst weiter.
        int workers = cache.RaiseWorkerFloor();
        EnterBuffering(resume: true, reason: $"Ring leer, jetzt {workers} Threads");
    }

    /// <summary>
    /// Zeigt, wie viele Bilder tatsaechlich auf dem Schirm landen. Die Zeitachse ist
    /// per Konstruktion exakt; interessant ist, ob die Anzeige mitkommt.
    /// </summary>
    private void UpdateMeasuredRate()
    {
        if (!_rateWindow.IsRunning) { _rateWindow.Restart(); _presentedInWindow = 0; return; }
        if (_rateWindow.ElapsedMilliseconds < 1000) return;

        double rate = _presentedInWindow * 1000.0 / _rateWindow.ElapsedMilliseconds;

        RateText.Text = $"{rate:0.0} fps";
        RateText.Foreground = (Brush)FindResource(rate < _clock.Fps * 0.9 ? "AccentBrush" : "MutedBrush");
        RateText.ToolTip = _clock.IsLockedToDisplay
            ? $"An den Bildschirm gekoppelt ({_clock.DisplayHz:0.#} Hz)"
            : _clock.DisplayHz > 0
                ? $"Zeitbasiert – Bildschirm {_clock.DisplayHz:0.#} Hz"
                : null;

        UpdateBufferReadout();
        CheckDecoderKeepsUp(rate);

        _presentedInWindow = 0;
        _rateWindow.Restart();
    }

    /// <summary>
    /// Zeigt, wie weit der Ringpuffer vorausreicht.
    ///
    /// Ohne diese Angabe ist Nachpuffern ein Raetsel: die Wiedergabe stockt, und
    /// nichts erklaert warum. Mit ihr sieht man, dass der Vorrat zur Neige geht -
    /// und dass eine kleinere Pufferstufe genau das behebt.
    /// </summary>
    private void UpdateBufferReadout()
    {
        var cache = _cache;
        if (cache is null || _clock.Fps <= 0) { BufferText.Text = string.Empty; return; }

        var stats = cache.GetStats();

        var (rangeFirst, rangeLast) = ActiveRange();
        int rangeLength = rangeLast - rangeFirst + 1;

        // Liegt der ganze Bereich im Ring, gibt es kein Nachpuffern mehr - eine
        // Sekundenangabe waere hier nur Zahlenrauschen.
        if (stats.CachedFrames >= rangeLength)
        {
            BufferText.Text = "Puffer komplett";
            BufferText.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }

        // ReadyAhead zaehlt bei aktivem Loop ueber das Sequenzende hinaus weiter und
        // trifft dieselben Frames erneut. Fuer die Pufferlogik ist das gleichgueltig,
        // fuer eine Zeitangabe nicht: mehr als die Sequenz selbst kann nicht
        // vorausliegen.
        int ahead = Math.Min(stats.AheadReady, Math.Max(0, rangeLength - 1));
        double seconds = ahead / _clock.Fps;

        BufferText.Text = $"Puffer {seconds:0.0} s";

        // Unter einer halben Sekunde Vorrat ist das naechste Stocken absehbar.
        BufferText.Foreground = (Brush)FindResource(seconds < 0.5 ? "GapBrush"
                                                  : seconds < 1.5 ? "AccentBrush" : "MutedBrush");
    }

    /// <summary>Frames, die vor dem Start bereitliegen muessen.</summary>
    private int WarmupTarget()
    {
        int configured = _settings.WarmupFrames;
        int frames = configured > 0 ? configured : (int)Math.Ceiling(_clock.Fps * 1.5);

        // Nie mehr verlangen, als der Ring ueberhaupt bereitstellen kann. Sonst
        // wartet das Puffern auf eine Zahl, die nie erreicht wird, und endet erst
        // im Notausstieg nach acht Sekunden.
        // Auf den aktiven Bereich bezogen: Bei einem Ausschnitt von zehn Frames
        // wartet das Puffern sonst auf neunzig, die es dort gar nicht gibt.
        var (first, last) = ActiveRange();
        int reachable = Math.Min(PrefetchAhead(), Math.Max(1, last - first));
        int fitsInRing = _cache?.GetStats().Capacity - 1 ?? reachable;

        return Math.Clamp(frames, 2, Math.Max(2, Math.Min(reachable, fitsInRing)));
    }

    /// <param name="reason">
    /// Woher der Anstoss kam. Steht im Hinweis, damit sichtbar wird, ob der Ring
    /// leergelaufen ist oder ob ihn etwas anderes verworfen hat - ein blosses
    /// "Puffern …" laesst beides gleich aussehen.
    /// </param>
    private void EnterBuffering(bool resume, string? reason = null)
    {
        if (_closing || _sequence.Count <= 1) return;

        if (_playing)
        {
            CompositionTarget.Rendering -= OnRendering;
            _playing = false;
            _clock.Stop();
            if (_shownIndex >= 0) _index = _shownIndex;
        }

        _buffering = true;
        _resumeAfterBuffering = resume;
        _bufferingSince = Environment.TickCount64;

        PlayButton.Content = "▶";
        ShowStatus(reason is null ? "Puffern …" : $"Puffern … ({reason})");
        _bufferTimer.Start();
    }

    private void OnBufferTick(object? sender, EventArgs e)
    {
        var cache = _cache;
        if (_closing || cache is null) { _bufferTimer.Stop(); return; }

        // Solange noch kein Bild steht, zuerst das aktuelle zeigen.
        if (_shownIndex < 0 && PresentFrame(_index)) Volatile.Write(ref _pendingIndex, -1);

        int ready = cache.ReadyAhead();
        int target = WarmupTarget();

        var (rangeFirst, rangeLast) = ActiveRange();
        bool wholeSequence = cache.GetStats().CachedFrames >= rangeLast - rangeFirst + 1;

        // Notausstieg, damit eine langsame Platte die Wiedergabe nicht endlos blockiert.
        bool timedOut = Environment.TickCount64 - _bufferingSince > 8000;

        if (ready < target && !wholeSequence && !timedOut) return;

        _bufferTimer.Stop();
        _buffering = false;
        HideStatus();

        if (_resumeAfterBuffering)
        {
            _resumeAfterBuffering = false;
            Play();
            return;
        }

        // Gepuffert, aber nicht weitergespielt: jetzt sind die Dateidaten wieder dran.
        RefreshMetadata();
    }

    private void Play()
    {
        if (_playing || _sequence.Count <= 1 || _closing) return;

        _buffering = false;
        _bufferTimer.Stop();
        HideStatus();

        _playing = true;
        _direction = 1;
        Volatile.Write(ref _pendingIndex, -1);
        _refresh.Reset();
        _clock.Start(_index);
        _cache?.SetPosition(_index, 1, _loop, urgent: false);

        CompositionTarget.Rendering += OnRendering;
        PlayButton.Content = "❙❙";
    }

    private void Pause()
    {
        _resumeAfterBuffering = false;

        if (_buffering)
        {
            _buffering = false;
            _bufferTimer.Stop();
            HideStatus();
        }

        if (!_playing)
        {
            PlayButton.Content = "▶";
            return;
        }

        _playing = false;
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();

        if (_shownIndex >= 0) _index = _shownIndex;

        PlayButton.Content = "▶";
        _rateWindow.Reset();
        RateText.Text = string.Empty;

        // Im Stillstand ist die Dateigroesse wieder erreichbar - waehrend der
        // Wiedergabe wird sie ausgelassen, weil sie einen Dateizugriff je Frame kostet.
        RefreshMetadata();

        // Ohne laufende Wiedergabe gibt es keinen Grund mehr, Threads zu belegen.
        _cache?.ResetWorkerFloor();

        // Waehrend der Wiedergabe wurde gezoomt: jetzt ist der richtige Zeitpunkt,
        // in der passenden Aufloesung nachzuladen.
        if (_redecodeDeferred) ScheduleRedecode();
    }

    private void RefreshMetadata() => UpdateMetadata(_shownIndex >= 0 ? _shownIndex : _index);

    /// <summary>
    /// Sagt es, wenn die eingestellte Bildrate nicht zu halten ist.
    ///
    /// Ohne diesen Hinweis sieht es aus wie ein Fehler im Programm: die Wiedergabe
    /// laeuft zu langsam, waehrend CPU und GPU im Taskmanager unbelastet wirken. Der
    /// Grund ist meist, dass zu wenige Decoder-Threads erlaubt sind - ein Thread
    /// entpackt ein 1080p-PNG mit 8,5 MB in rund 46 ms und schafft damit knapp
    /// 18 Bilder je Sekunde, gleichgueltig wie viele Kerne daneben leerlaufen.
    /// </summary>
    private void CheckDecoderKeepsUp(double rate)
    {
        // Nur bei anhaltendem Rueckstand und leerem Vorrat: ein einzelner Aussetzer
        // ist normal und soll nicht kommentiert werden.
        bool behind = rate < _clock.Fps * 0.85;
        bool starved = (_cache?.ReadyAhead() ?? 0) < _clock.Fps * 0.25;

        if (!behind || !starved) { _slowRounds = 0; return; }

        if (++_slowRounds < 3) return;
        _slowRounds = 0;

        int threads = _profile.DecoderThreads;
        ShowStatus($"Decoder kommt nicht mit ({threads} {(threads == 1 ? "Thread" : "Threads")}). " +
                   (_draftStep == 0
                       ? "Taste 2 verkleinert die Dekodiergröße, mehr Threads in den Einstellungen."
                       : "Mehr Decoder-Threads in den Einstellungen."));

        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private int _slowRounds;

    private void TogglePlay()
    {
        if (_playing || _buffering) Pause();
        else EnterBuffering(resume: true, reason: "Wiedergabe startet");
        ShowBar();
    }

    /// <summary>
    /// Einzelbildnavigation. Pro Tastendruck genau eine O(1)-Operation, damit ein
    /// gehaltener Pfeil keine Ereignisschlange aufbaut: spaetere Anschlaege
    /// ueberschreiben nur das Ziel, der Decoder arbeitet immer am aktuellsten.
    /// </summary>
    private void Step(int delta)
    {
        if (_playing || _buffering) Pause();

        var (first, last) = ActiveRange();
        int next = SequenceMath.OffsetInRange(_index, delta, first, last, _loop);
        if (next < 0) return;   // Bereichsende ohne Loop: stehenbleiben

        _index = next;
        _direction = delta >= 0 ? 1 : -1;

        // Richtungswechsel dreht auch die Vorausladerichtung des Ringpuffers um.
        _cache?.SetPosition(_index, _direction, _loop, urgent: true);

        Volatile.Write(ref _pendingIndex, _index);
        if (PresentFrame(_index)) Volatile.Write(ref _pendingIndex, -1);

        UpdateScrubber(_index);
        ShowBar();
    }

    private void SeekTo(int index)
    {
        if (index < 0 || index >= _sequence.Count || index == _index) return;

        _direction = index >= _index ? 1 : -1;
        _index = index;
        _cache?.SetPosition(index, _direction, _loop, urgent: true);
        if (_playing) _clock.Seek(index);

        Volatile.Write(ref _pendingIndex, index);
        if (PresentFrame(index)) Volatile.Write(ref _pendingIndex, -1);
    }

    // ------------------------------------------------------------ Eingaben

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                BeginClose();
                return;

            case Key.Space:
                e.Handled = true;
                TogglePlay();
                return;

            case Key.Right:
                e.Handled = true;
                Step(1);
                return;

            case Key.Left:
                e.Handled = true;
                Step(-1);
                return;

            case Key.Home:
                e.Handled = true;
                Pause();
                SeekTo(0);
                ShowBar();
                return;

            case Key.End:
                e.Handled = true;
                Pause();
                SeekTo(_sequence.Count - 1);
                ShowBar();
                return;

            case Key.L:
                e.Handled = true;
                LoopButton.IsChecked = !(LoopButton.IsChecked == true);
                ShowBar();
                return;

            case Key.D:
                e.Handled = true;
                OnMetaClicked(this, new RoutedEventArgs());
                return;

            case Key.E:
                e.Handled = true;
                ShowExportDialog();
                return;

            case Key.Tab:
                e.Handled = true;
                TogglePanel();
                return;

            case Key.C:
                e.Handled = true;
                ToggleCompare();
                return;

            case Key.A:
                e.Handled = true;
                MarkReference();
                return;

            case Key.D1:
            case Key.NumPad1:
                e.Handled = true;
                SetDraftStep(0);
                return;

            case Key.D2:
            case Key.NumPad2:
                e.Handled = true;
                SetDraftStep(1);
                return;

            case Key.D3:
            case Key.NumPad3:
                e.Handled = true;
                SetDraftStep(2);
                return;

            case Key.I:
                e.Handled = true;
                SetInPoint();
                return;

            case Key.O:
                e.Handled = true;
                SetOutPoint();
                return;

            case Key.Delete:
            case Key.Back:
                e.Handled = true;
                ClearInOut();
                return;

            case Key.D0:
            case Key.NumPad0:
                if (control)
                {
                    e.Handled = true;
                    ZoomToFit();
                }
                return;

            case Key.Add:
            case Key.OemPlus:
                if (control)
                {
                    e.Handled = true;
                    ZoomAt(ViewportCenter(), 1);
                }
                return;

            case Key.Subtract:
            case Key.OemMinus:
                if (control)
                {
                    e.Handled = true;
                    ZoomAt(ViewportCenter(), -1);
                }
                return;
        }
    }

    /// <summary>
    /// Im pausierten Zustand zoomt das Rad, mit Strg wird gescrubbt. Waehrend der
    /// Wiedergabe ist es umgekehrt: dort ist Weiterblaettern die naheliegende Geste,
    /// und ein versehentlicher Zoom mitten im Abspielen stoert mehr, als er nuetzt.
    /// </summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool running = _playing || _buffering;
        bool scrub = running ? !control : control;

        if (scrub) Step(e.Delta > 0 ? 1 : -1);
        else ZoomAt(CursorInViewport(e.GetPosition(Viewport)), e.Delta > 0 ? 1 : -1);
    }

    /// <summary>Mittlere Maustaste verschiebt - dieselbe Geste wie in Blender.</summary>
    private void OnViewportOtherButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_view.CanPan) return;

        e.Handled = true;
        _dragOrigin = e.GetPosition(Viewport);
        _panning = true;
        Viewport.CaptureMouse();
    }

    private void OnViewportOtherButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !Viewport.IsMouseCaptured) return;

        e.Handled = true;
        Viewport.ReleaseMouseCapture();
        _panning = false;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        try { DragMove(); }
        catch (InvalidOperationException) { /* Maustaste war schon los */ }
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ControlBar.IsMouseOver) return;

        if (e.ClickCount == 2)
        {
            ToggleFitAndActual(e.GetPosition(Viewport));
            return;
        }

        _dragOrigin = e.GetPosition(Viewport);
        _panning = false;
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!Viewport.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed) return;

        var position = e.GetPosition(Viewport);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;

        if (!_panning && Math.Abs(dx) + Math.Abs(dy) < 4) return;

        // Schrittweise verschieben statt vom Startpunkt aus: der Regler begrenzt den
        // Versatz, ein absolutes Delta wuerde nach dem Anschlag weiterlaufen und das
        // Bild spaeter ruckartig nachziehen.
        _panning = true;
        _dragOrigin = position;
        _view.Pan(new Vector(dx, dy));
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!Viewport.IsMouseCaptured) return;
        Viewport.ReleaseMouseCapture();

        // Klick ohne Bewegung heisst Wiedergabe umschalten, Ziehen heisst verschieben.
        if (!_panning && !ControlBar.IsMouseOver) TogglePlay();
        _panning = false;
    }

    private void OnViewportRightClick(object sender, MouseButtonEventArgs e) => ZoomToFit();

    private void OnMouseMoved(object sender, MouseEventArgs e) => ShowBar();

    private void OnPlayClicked(object sender, RoutedEventArgs e) => TogglePlay();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => BeginClose();

    private void OnScrubStarted(object sender, DragStartedEventArgs e)
    {
        _scrubbing = true;
        _resumeAfterScrub = _playing || _buffering;
        Pause();
    }

    private void OnScrubCompleted(object sender, DragCompletedEventArgs e)
    {
        _scrubbing = false;

        // Auf die Nummer des tatsaechlich gezeigten Frames einrasten; nach einem
        // Halt in einer Luecke stuende der Regler sonst neben dem Bild.
        UpdateScrubber(_index);

        if (_resumeAfterScrub)
        {
            _resumeAfterScrub = false;
            EnterBuffering(resume: true, reason: "Sprung");
        }
        ShowBar();
    }

    private void OnScrubberValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressScrubber || _initializing) return;

        // Die Leiste liefert eine Framenummer. Zeigt sie in eine Luecke, wird der
        // naechstgelegene vorhandene Frame gezeigt - der Regler bleibt dabei stehen,
        // wo der Zeiger ist, und rastet erst beim Loslassen ein.
        int index = _sequence.IndexNearestNumber((int)Math.Round(e.NewValue));
        if (index >= 0) SeekTo(index);
    }

    private void OnLoopChanged(object sender, RoutedEventArgs e)
    {
        _loop = LoopButton.IsChecked == true;
        _cache?.SetPosition(_index, _direction, _loop, urgent: false);

        if (_initializing) return;
        _settings.Loop = _loop;
        _persist(_settings);
    }

    private void OnFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FpsBox.SelectedItem is not FpsOption option) return;

        // Rebasing steckt im Setter: die Wiedergabe springt beim Umschalten nicht.
        _clock.Fps = option.Value;

        // Der Vorlauf ist in Frames angegeben und bedeutet bei jeder Bildrate etwas
        // anderes - er muss mitgezogen werden, sonst schrumpft die Reserve beim
        // Hochschalten auf die Haelfte.
        _cache?.SetWindow(PrefetchAhead(), PrefetchBehind());

        if (_initializing) return;
        _settings.Fps = option.Value;
        _persist(_settings);
        ShowBar();
    }

    // ------------------------------------------------------------ Overlays

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusBadge.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        _statusTimer.Stop();
        StatusBadge.Visibility = Visibility.Collapsed;
    }

    private void OnStatusTick(object? sender, EventArgs e)
    {
        // Nur kurzlebige Rueckmeldungen wieder wegnehmen. Der Pufferhinweis wird
        // vom Puffervorgang selbst gesteuert und darf hier nicht verschwinden.
        _statusTimer.Stop();
        if (!_buffering) HideStatus();
    }

    // ------------------------------------------------------------ In- und Out-Punkt

    /// <summary>
    /// Der aktive Bereich als Listenpositionen. Ohne gesetzte Punkte ist das die
    /// ganze Sequenz - dadurch braucht die Wiedergabe keine Fallunterscheidung.
    /// </summary>
    private (int First, int Last) ActiveRange()
    {
        int first = _inPoint >= 0 ? _inPoint : 0;
        int last = _outPoint >= 0 ? _outPoint : _sequence.Count - 1;

        // Vertauschte Punkte sind kein Fehler des Benutzers, sondern eine
        // Reihenfolge, die sich beim Setzen ergibt. Still korrigieren.
        return first <= last ? (first, last) : (last, first);
    }

    private bool HasRange => _inPoint >= 0 || _outPoint >= 0;

    private void SetInPoint()
    {
        _inPoint = _index;
        if (_outPoint >= 0 && _outPoint < _inPoint) _outPoint = -1;
        AfterRangeChanged();
    }

    private void SetOutPoint()
    {
        _outPoint = _index;
        if (_inPoint >= 0 && _inPoint > _outPoint) _inPoint = -1;
        AfterRangeChanged();
    }

    private void ClearInOut()
    {
        if (!HasRange) return;
        _inPoint = -1;
        _outPoint = -1;
        AfterRangeChanged();
    }

    private void AfterRangeChanged()
    {
        UpdateRangeText();

        // Steht die Wiedergabe ausserhalb des neuen Bereichs, an den Anfang springen -
        // sonst laeuft sie bis zum Ende weiter und der Bereich waere wirkungslos.
        var (first, last) = ActiveRange();
        if (_index < first || _index > last) SeekTo(first);

        // Der Ring muss den Bereich kennen. Ohne das lud er beim Loop-Sprung die
        // Frames hinter dem Out-Punkt, die nie gezeigt werden, und hielt den
        // In-Punkt nicht - jede Runde endete im Nachpuffern.
        _cache?.SetRange(first, last);
        _cache?.SetPosition(_index, _direction, _loop, urgent: false);
        ShowBar();
    }

    private void UpdateRangeText()
    {
        if (!HasRange)
        {
            RangeText.Visibility = Visibility.Collapsed;
            return;
        }

        var (first, last) = ActiveRange();
        int count = last - first + 1;

        RangeText.Text = $"[{_sequence.Frames[first].Number.ToString(_numberFormat)}" +
                         $"–{_sequence.Frames[last].Number.ToString(_numberFormat)}] {count}";
        RangeText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ Bilddaten

    /// <summary>
    /// Aufloesung, Farbtiefe und Dateigroesse des aktuellen Frames.
    ///
    /// Die Dateigroesse kommt vom Dateisystem, kostet also einen Zugriff je Frame.
    /// Deshalb nur im pausierten Zustand und nur, wenn die Anzeige ueberhaupt
    /// sichtbar ist - waehrend der Wiedergabe waeren das 24 Zugriffe je Sekunde.
    /// </summary>
    private void UpdateMetadata(int index)
    {
        if (!_settings.ShowMetadata)
        {
            MetaText.Text = "Daten";
            MetaButton.ToolTip = "Bilddaten einblenden (D)";
            return;
        }

        var text = $"{_sourceWidth}×{_sourceHeight}";

        if (_bitsPerChannel > 0) text += $" · {_bitsPerChannel} bit";

        if (!_playing && !_buffering && index >= 0 && index < _sequence.Count)
        {
            try
            {
                var info = new FileInfo(_sequence.Frames[index].Path);
                if (info.Exists) text += " · " + FormatBytes(info.Length);
            }
            catch (Exception)
            {
                // Datei gerade ersetzt oder gesperrt - die Groesse ist dann eben nicht dabei.
            }
        }

        MetaText.Text = text;
        MetaButton.ToolTip = "Bilddaten ausblenden (D)";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    private void OnMetaClicked(object sender, RoutedEventArgs e)
    {
        _settings.ShowMetadata = !_settings.ShowMetadata;
        _persist(_settings);
        UpdateMetadata(_shownIndex >= 0 ? _shownIndex : _index);
        ShowBar();
    }

    private void OnDraftClicked(object sender, RoutedEventArgs e) => CycleDraftStep();

    private void OnPanelToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetPanelOpen(PanelButton.IsChecked == true, resizeWindow: true);
    }

    private void OnZoomClicked(object sender, RoutedEventArgs e)
    {
        ToggleFitAndActual(ViewportCenter());
        ShowBar();
    }

    // ------------------------------------------------------------ Export

    /// <summary>
    /// Oeffnet den Exportdialog. Die Wiedergabe wird angehalten: waehrend eines
    /// Exports laufen sonst Decoder und Encoder gleichzeitig um dieselben Kerne.
    /// </summary>
    private void OnExportClicked(object sender, RoutedEventArgs e) => ShowExportDialog();

    private void ShowExportDialog()
    {
        if (_exportWindow is not null)
        {
            _exportWindow.Activate();
            return;
        }

        Pause();

        var (first, last) = ActiveRange();
        var inOut = HasRange
            ? _sequence.Frames.Skip(first).Take(last - first + 1).ToArray()
            : Array.Empty<SequenceFrame>();

        var window = new ExportWindow(_sequence, inOut, _clock.Fps, _sourceWidth, _sourceHeight,
                                      _settings, _persist, () => _profile.DecoderThreads,
                                      _adjustments)
        {
            Owner = this,
        };

        // Ohne diese Sperre schliesst sich die Vorschau in dem Moment, in dem der
        // Dialog den Fokus uebernimmt - samt der Sequenz, die exportiert werden soll.
        ModalDialogOpen = true;

        window.Closed += (_, _) =>
        {
            _exportWindow = null;
            ModalDialogOpen = false;
            Activate();
        };

        _exportWindow = window;
        window.Show();
    }

    private void ShowBar()
    {
        _hideTimer.Stop();

        if (!_barVisible)
        {
            _barVisible = true;
            ControlBar.IsHitTestVisible = true;
            ControlBar.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120)));
        }

        _hideTimer.Start();
    }

    private void OnHideTick(object? sender, EventArgs e)
    {
        // Nicht ausblenden, solange die Leiste benutzt wird.
        if (_scrubbing || ControlBar.IsMouseOver || FpsBox.IsDropDownOpen) return;

        _hideTimer.Stop();
        _barVisible = false;
        ControlBar.IsHitTestVisible = false;
        ControlBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300)));
    }
}
