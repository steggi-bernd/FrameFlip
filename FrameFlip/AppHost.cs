using System.Diagnostics;
using System.IO;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
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

    /// <summary>Nimmt Meldungen des Blender-Addons entgegen. Null, wenn abgeschaltet.</summary>
    private Bridge.RenderMonitor? _renderMonitor;

    /// <summary>Reicht den Renderfortschritt ans Handy weiter. Null, solange nicht gekoppelt.</summary>
    private Remote.RemoteLink? _remote;
    private readonly AppWindowController _windows;
    private SystemLoadMonitor? _loadMonitor;
    private bool _disposed;

    public AppHost() : this(null, null, null) { }

    /// <summary>Fenstertests brauchen weder persoenliche Einstellungen noch eine Kopplung anzulegen.</summary>
    internal AppHost(Func<Window>? createMain, Func<Window>? createSettings, Func<Window>? createPairing)
    {
        _viewerOpening = new ViewerOpenController(new ViewerOpenSources(_decoders), () => _viewer, OpenNewViewer, Notify);
        _windows = new AppWindowController(
            () =>
            {
                LivePage.Load = () => _loadMonitor?.LastSnapshot;
                return createMain?.Invoke() ?? new MainWindow(_renderMonitor, () => _remote?.State,
                    ShowSettings, OpenFile, ShowPairing, _settings, settings => SettingsStore.Save(settings));
            },
            createSettings ?? (() => new SettingsWindow(_settings, ApplySettings, () => _remote?.State)),
            createPairing ?? (() => new PairingWindow(_settings, ApplySettings, () => _remote?.State)),
            EnsureLoadMonitor);
    }

    internal AppHost(ViewerOpenSources sources, Func<IViewerOpenTarget?> current,
                     Action<ViewerOpenRequest> create, Action<string> notify) : this(null, null, null)
    {
        _viewerOpening = new ViewerOpenController(sources, current, create, notify);
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
            _renderMonitor.Changed += () =>
                _loadMonitor?.SetRenderMode(_renderMonitor?.HasRunningJob == true);
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
        var (sequence, seed, start, explorerWindow, sourceWidth, sourceHeight) = request;

        EnsureLoadMonitor();
        int maxWorkers = _settings.AdaptiveResources && _loadMonitor is not null ? _loadMonitor.MaxDecoderThreads : 1;

        var bounds = WindowBoundsFor(explorerWindow, sourceWidth, sourceHeight);
        var viewer = new ViewerWindow(sequence, start, _settings, Persist, _decoders,
                                      bounds, sourceWidth, sourceHeight, maxWorkers);

        // Der Monitor gehoert dem Tray, nicht dem Fenster: Ein Render kann laufen,
        // waehrend gar keine Vorschau offen ist, und soll dann trotzdem mitgezaehlt
        // werden. Das Fenster haengt sich nur an.
        if (_renderMonitor is not null) viewer.AttachRenderMonitor(_renderMonitor);

        // Der Viewer soll die Einstellungen oeffnen koennen, ohne den Dialog selbst
        // zu bauen - Pruefen und Sichern gehoeren hierher.
        viewer.SettingsRequested = ShowSettings;
        viewer.Closed += (_, _) =>
        {
            _viewer = null;
            EnsureLoadMonitor();
        };

        _viewer = viewer;
        viewer.Show();

        Remember(sequence, seed, sourceWidth, sourceHeight);
    }

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

    // ------------------------------------------------------------ Lasterkennung

    /// <summary>
    /// Die Lastmessung laufen lassen, solange jemand sie braucht.
    ///
    /// Sie haengt nicht mehr allein am Vorschaufenster. Das war richtig, solange sie
    /// nur die Decoder-Threads regelte - jetzt speist sie auch die Werte, die ans
    /// Handy gehen, und die sollen gerade dann kommen, wenn keine Vorschau offen
    /// ist. Ohne diese Unterscheidung blieb der Live-Bildschirm leer, sobald man das
    /// Fenster schloss.
    /// </summary>
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

    private void EnsureLoadMonitor()
    {
        bool wanted = _viewer is not null || _remote is not null || _windows.Main is not null;

        if (wanted) StartLoadMonitor();
        else StopLoadMonitor();
    }

    private void StartLoadMonitor()
    {
        // Schon am Laufen - ein Neustart wuerde nur die Messreihe abschneiden.
        if (_loadMonitor is not null) return;

        StopLoadMonitor();

        // Die Messung selbst ist billig und wird auch fuer die Fernsteuerung
        // gebraucht; geregelt wird nur, wenn es eingeschaltet ist.
        if (!_settings.AdaptiveResources && _remote is null) return;

        var monitor = new SystemLoadMonitor(_settings.MaxDecoderThreads,
                                            TimeSpan.FromSeconds(_settings.LoadIntervalSeconds));
        monitor.Updated += OnLoadUpdated;
        _loadMonitor = monitor;
        monitor.Start();
    }

    private void StopLoadMonitor()
    {
        var monitor = _loadMonitor;
        _loadMonitor = null;

        if (monitor is not null)
        {
            monitor.Updated -= OnLoadUpdated;
            monitor.Dispose();
        }

        // Ohne offene Vorschau wieder zurueckhaltend werden.
        ApplyProcessPriority(ProcessPriorityClass.BelowNormal);
    }

    private void OnLoadUpdated(LoadSnapshot snapshot, ResourceProfile profile)
    {
        var viewer = _viewer;
        if (viewer is null) return;

        viewer.Dispatcher.BeginInvoke(new Action(() =>
        {
            var current = _viewer;
            if (current is null) return;
            ApplyProcessPriority(profile.ProcessPriority);
            current.ApplyLoad(snapshot, profile);
        }));
    }

    private static void ApplyProcessPriority(ProcessPriorityClass priority)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.PriorityClass != priority) process.PriorityClass = priority;
        }
        catch (Exception)
        {
            // Ohne ausreichende Rechte bleibt es bei der aktuellen Stufe.
        }
    }

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

        bool remoteChanged = settings.RemoteEnabled != _settings.RemoteEnabled
                             || settings.RelayHost != _settings.RelayHost
                             || settings.PairingSecret != _settings.PairingSecret;

        _settings = settings;
        Localization.Strings.Apply(Localization.Strings.Parse(_settings.Language));
        SettingsStore.Save(_settings);
        UpdateTooltip();

        // Nur bei echter Aenderung neu aufbauen. Sonst risse jedes Speichern im
        // Einstellungsdialog eine stehende Verbindung ab.
        if (remoteChanged) StartRemote();

        // Puffer- und Budgetwerte greifen beim naechsten Oeffnen des Viewers.
        return null;
    }

    /// <summary>
    /// Baut die Leitung zum Handy auf - oder raeumt sie weg, wenn eine der
    /// Voraussetzungen fehlt.
    ///
    /// Voraussetzungen sind drei: eingeschaltet, eine brauchbare Relais-Adresse, und
    /// ein Schluessel, der sich auf diesem Konto entschluesseln laesst. Fehlt eine,
    /// passiert nichts - kein Verbindungsversuch, kein Hinweis, keine Last. Wer die
    /// Fernsteuerung nicht benutzt, soll von ihr auch nichts merken.
    /// </summary>
    private void StartRemote()
    {
        var previous = _remote;
        _remote = null;

        // Im Hintergrund abraeumen: Das Schliessen wartet auf die Leseschleife, und
        // darauf soll niemand im Einstellungsdialog warten.
        if (previous is not null) _ = previous.DisposeAsync().AsTask();

        if (!_settings.RemoteEnabled || _renderMonitor is null) { EnsureLoadMonitor(); return; }
        if (!Remote.PairingStore.TryUnprotect(_settings.PairingSecret, out var key)) { EnsureLoadMonitor(); return; }

        try
        {
            var invite = new Remote.PairingInvite(key!, _settings.RelayHost);

            // Die Einstellungen als Funktion, nicht als Kopie: Der Dateizugriff wird
            // im Dialog umgeschaltet, und die Leitung soll das sofort merken statt
            // erst beim naechsten Verbindungsaufbau.
            _remote = new Remote.RemoteLink(invite, _renderMonitor, () => _loadMonitor?.LastSnapshot,
                                            () => _settings);
            _remote.Start();

            // Erst jetzt, denn sie haengt daran, ob die Fernsteuerung steht.
            EnsureLoadMonitor();
        }
        catch (ArgumentException)
        {
            // Unbrauchbare Adresse. Der Dialog weist sie ab; kommt sie aus einer von
            // Hand bearbeiteten config.json, bleibt die Fernsteuerung eben aus.
            _remote = null;
        }
    }

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

        StopLoadMonitor();

        if (_remote is not null)
        {
            _ = _remote.DisposeAsync().AsTask();
            _remote = null;
        }

        _renderMonitor?.Dispose();
        _renderMonitor = null;

        _hotkeys.Pressed -= Toggle;
        _hotkeys.Dispose();

        _tray?.Dispose();
        _tray = null;
    }
}
