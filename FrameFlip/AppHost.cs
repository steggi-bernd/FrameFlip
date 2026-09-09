using System.IO;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Sequencing;
using FrameFlip.Views;
using Window = System.Windows.Window;

namespace FrameFlip;

/// <summary>
/// Die eigentliche Anwendung: lebt im Tray, haelt den globalen Hotkey und oeffnet
/// bei Bedarf genau ein Viewer-Fenster. Alles Schwergewichtige entsteht erst beim
/// Oeffnen und verschwindet beim Schliessen wieder.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly FrameDecoderRegistry _decoders = FrameDecoderRegistry.CreateDefault();
    private readonly HotKeyService _hotkeys = new();

    private AppSettings _settings = new();
    private AppTrayController? _tray;
    private ViewerWindow? _viewer;
    private readonly ViewerOpenController _viewerOpening;
    private readonly Func<ViewerOpenRequest, int, ViewerWindow> _createViewer;

    /// <summary>Nimmt Meldungen des Blender-Addons entgegen. Null, wenn abgeschaltet.</summary>
    private Bridge.RenderMonitor? _renderMonitor;

    /// <summary>Besitzt die optionale Verbindung zum Handy.</summary>
    private readonly AppRemoteController _remote;
    private readonly AppWindowController _windows;
    private readonly AppLoadController _load;
    private bool _disposed;

    public AppHost() : this(null, null, null) { }

    /// <summary>Fenstertests brauchen weder persoenliche Einstellungen noch eine Kopplung anzulegen.</summary>
    internal AppHost(Func<Window>? createMain, Func<Window>? createSettings, Func<Window>? createPairing)
    {
        _createViewer = CreateViewer;
        _load = new AppLoadController(() => _settings,
            AppLoadSources.Default(() => _viewer is { } viewer ? new AppViewerLoadTarget(viewer) : null));
        // Die Verbindung liest Einstellungen und Lastwerte weiter live aus dem Host.
        _remote = new AppRemoteController(() => _settings, new AppRemoteSources(
            () => _renderMonitor is not null,
            invite => new AppRemoteLink(new Remote.RemoteLink(invite, _renderMonitor!,
                () => _load.LastSnapshot, () => _settings))), EnsureLoadMonitor);
        _viewerOpening = new ViewerOpenController(new ViewerOpenSources(_decoders), () => _viewer, OpenNewViewer, Notify);
        _windows = new AppWindowController(
            () =>
            {
                LivePage.Load = () => _load.LastSnapshot;
                return createMain?.Invoke() ?? new MainWindow(_renderMonitor, () => _remote.State,
                    ShowSettings, OpenFile, ShowPairing, _settings, settings => SettingsStore.Save(settings));
            },
            createSettings ?? (() => new SettingsWindow(_settings, ApplySettings, () => _remote.State)),
            createPairing ?? (() => new PairingWindow(_settings, ApplySettings, () => _remote.State)),
            EnsureLoadMonitor);
    }

    internal AppHost(ViewerOpenSources sources, Func<IViewerOpenTarget?> current,
                     Action<ViewerOpenRequest> create, Action<string> notify) : this(null, null, null)
    {
        _viewerOpening = new ViewerOpenController(sources, current, create, notify);
    }

    internal AppHost(AppRemoteSources sources, Action remoteChanged) : this(null, null, null)
    {
        _remote = new AppRemoteController(() => _settings, sources, remoteChanged);
    }

    internal AppHost(AppLoadSources sources, Func<ViewerOpenRequest, int, ViewerWindow>? createViewer = null) : this(null, null, null)
    {
        _load = new AppLoadController(() => _settings, sources);
        _createViewer = createViewer ?? CreateViewer;
    }

    public void Start()
    {
        _settings = SettingsStore.Load();

        // Vor allem anderen: Die Oberflaeche soll gleich in der richtigen Sprache
        // erscheinen, nicht erst nach dem ersten Fensterwechsel.
        Localization.Strings.Apply(Localization.Strings.Parse(_settings.Language));

        _tray = new AppTrayController(ShowMain, Toggle, ShowSettings, Exit);

        _hotkeys.Pressed += Toggle;

        if (!HotKeyDefinition.TryParse(_settings.Hotkey, out var definition))
            definition = HotKeyDefinition.Default;

        if (!_hotkeys.Register(definition))
        {
            Notify(Localization.Strings.T("S_HotkeyTaken", definition));
        }

        UpdateTooltip();

        // Die Bruecke zum Blender-Addon. Schlaegt sie fehl - Port belegt, kein Addon
        // installiert -, bleibt es dabei: FrameFlip ist zuerst eine Vorschau.
        if (_settings.BridgeEnabled)
        {
            _renderMonitor = new Bridge.RenderMonitor(_settings.BridgePort);

            // Waehrend eines Renders wird dichter gemessen: Der normale Takt reicht
            // fuer die Lastregelung, aber nicht fuer eine Verlaufskurve.
            _renderMonitor.Changed += OnRenderChanged;
            OnRenderChanged();
        }

        StartRemote();

        // Startballast (JIT, XAML-Parser, Icon-Erzeugung) wieder abgeben. Die App
        // steht danach nur noch am Hotkey und soll im Leerlauf nichts festhalten.
        Task.Delay(3000).ContinueWith(_ => MemoryTrimmer.TrimNow(), TaskScheduler.Default);
    }

    // ------------------------------------------------------------ Tray

    private void UpdateTooltip() => _tray?.UpdateTooltip(_hotkeys.Current.ToString());

    private void Notify(string message) => _tray?.Notify(message);

    // ------------------------------------------------------------ Viewer

    /// <summary>
    /// Der Hotkey wirkt als Umschalter - mit einer Ausnahme: zeigt der Explorer
    /// inzwischen auf eine ANDERE Sequenz, wird der Inhalt getauscht statt
    /// geschlossen. Andernfalls muesste man zweimal druecken, nur um von einem
    /// Renderordner zum naechsten zu kommen.
    /// </summary>
    private void Toggle() => _viewerOpening.Toggle();

    /// <summary>
    /// Oeffnet die Vorschau fuer eine bestimmte Datei, ohne den Explorer zu befragen.
    /// Wird von der Befehlszeile benutzt: FrameFlip.exe --preview "C:\pfad\render_0001.png"
    /// </summary>
    public void OpenFile(string path) => _viewerOpening.OpenFile(path);

    private void OpenNewViewer(ViewerOpenRequest request)
    {
        int maxWorkers = PrepareViewerLoad();
        ViewerWindow? viewer = null;
        try
        {
            viewer = _createViewer(request, maxWorkers);
            // Der Render-Monitor gehoert dem Host; das Fenster haengt sich nur an.
            if (_renderMonitor is not null) viewer.AttachRenderMonitor(_renderMonitor);
            viewer.SettingsRequested = ShowSettings;
            viewer.Closed += (_, _) =>
            {
                if (ReferenceEquals(_viewer, viewer)) _viewer = null;
                EnsureLoadMonitor();
            };

            _viewer = viewer;
            viewer.Show();

            Remember(request.Sequence, request.Seed, request.SourceWidth, request.SourceHeight);
        }
        catch
        {
            if (ReferenceEquals(_viewer, viewer)) _viewer = null;
            viewer?.Close();
            throw;
        }
        finally
        {
            // Die Reservierung vor der Konstruktion darf bei einem Fehler keinen
            // Lastmonitor ohne Verbraucher zuruecklassen.
            EnsureLoadMonitor();
        }
    }

    private ViewerWindow CreateViewer(ViewerOpenRequest request, int maxWorkers)
        => new(request.Sequence, request.StartIndex, _settings, Persist, _decoders,
            WindowBoundsFor(request.ExplorerWindow, request.SourceWidth, request.SourceHeight),
            request.SourceWidth, request.SourceHeight, maxWorkers);

    /// <summary>
    /// Fenstergroesse und -position auf dem Monitor des ausloesenden Explorer-Fensters.
    /// Die Rechnung selbst liegt in <see cref="WindowPlacement"/>, damit sie ohne
    /// echten Bildschirm pruefbar bleibt.
    /// </summary>
    private static PixelRect WindowBoundsFor(IntPtr explorerWindow, int sourceWidth, int sourceHeight)
    {
        var (work, scale) = NativeMethods.GetWorkArea(explorerWindow);
        return WindowPlacement.Compute(work, scale, sourceWidth, sourceHeight);
    }

    /// <summary>
    /// Die Sequenz in die Liste der zuletzt geoeffneten aufnehmen.
    ///
    /// Im Hintergrund, weil es die Platte anfasst und der Aufrufer gerade ein
    /// Fenster oeffnet - eine Liste fuer spaeter darf das Jetzt nicht aufhalten.
    /// </summary>
    private static void Remember(ImageSequence sequence, string seed, int width, int height)
    {
        if (sequence.Count == 0) return;

        var entry = new Configuration.RecentSequence
        {
            Folder = sequence.Pattern.Directory,
            Seed = seed,
            First = sequence.StartNumber,
            Last = sequence.EndNumber,
            Count = sequence.Count,
            Width = width,
            Height = height,
            Kind = sequence.Pattern.Extension.TrimStart(Path.DirectorySeparatorChar, '.').ToUpperInvariant(),
        };

        Task.Run(() => Configuration.RecentSequences.Remember(entry));
    }

    // ------------------------------------------------------------ Lasterkennung

    private void EnsureLoadMonitor()
        => _load.Ensure(_viewer is not null, _windows.Main is not null, _remote.HasConnection);

    private int PrepareViewerLoad()
    {
        // Der neue Viewer muss schon vor seiner Konstruktion als Verbraucher zaehlen.
        _load.Ensure(true, _windows.Main is not null, _remote.HasConnection);
        return _load.ViewerDecoderThreads;
    }

    private void OnRenderChanged() => _load.SetRenderMode(_renderMonitor?.HasRunningJob == true);

    private void Persist(AppSettings settings) => SettingsStore.Save(settings);

    // ------------------------------------------------------------ Einstellungen

    /// <summary>
    /// Das Hauptfenster zeigen - oder nach vorn holen, wenn es schon offen ist.
    ///
    /// Genau eines: Zwei Fenster mit demselben Inhalt waeren zwei Stellen, an denen
    /// derselbe Render steht, und die zweite wuerde niemand schliessen.
    /// </summary>
    public void ShowMain() => _windows.ShowMain();

    /// <summary>Der Kopplungscode als eigenes Fenster, ueber dem Hauptfenster.</summary>
    private void ShowPairing() => _windows.ShowPairing();

    private void ShowSettings()
    {
        // Der Callback gehoert zu diesem Viewer, auch wenn inzwischen ein
        // anderer geoeffnet wurde. Der Controller besitzt keine Viewer-Sitzung.
        var viewer = _viewer;
        _windows.ShowSettings(viewer, open =>
        {
            if (viewer is not null) viewer.ModalDialogOpen = open;
        });
    }

    /// <summary>Rueckgabe: Fehlertext fuer den Dialog, oder null bei Erfolg.</summary>
    private string? ApplySettings(AppSettings settings)
    {
        settings.Normalize();

        if (!HotKeyDefinition.TryParse(settings.Hotkey, out var definition))
            return Localization.Strings.T("S_HotkeyInvalid");

        if (definition != _hotkeys.Current)
        {
            var previous = _hotkeys.Current;
            if (!_hotkeys.Register(definition))
            {
                _hotkeys.Register(previous);
                return Localization.Strings.T("S_HotkeyBusy");
            }
        }

        var previousSettings = _settings;
        _settings = settings;
        Localization.Strings.Apply(Localization.Strings.Parse(_settings.Language));
        SettingsStore.Save(_settings);
        UpdateTooltip();

        // Nur bei echter Aenderung neu aufbauen. Sonst risse jedes Speichern im
        // Einstellungsdialog eine stehende Verbindung ab.
        _remote.SettingsChanged(previousSettings);

        // Puffer- und Budgetwerte greifen beim naechsten Oeffnen des Viewers.
        return null;
    }

    private void StartRemote() => _remote.Restart();

    private void Exit()
    {
        _viewer?.Close();
        _windows.CloseSettings();
        System.Windows.Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _load.Dispose();

        _remote.Dispose();

        if (_renderMonitor is not null)
        {
            _renderMonitor.Changed -= OnRenderChanged;
            _renderMonitor.Dispose();
        }
        _renderMonitor = null;

        _hotkeys.Pressed -= Toggle;
        _hotkeys.Dispose();

        _tray?.Dispose();
        _tray = null;
    }
}
