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
    private Web.WatchService? _watch;

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
                    ShowSettings, OpenFile, ShowPairing, _settings, settings => SettingsStore.Save(settings),
                    ApplySettings, () => _settings, () => _watch, RenewWatchLink, SetWatchCode);
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
        StartWatch();

        // Startballast (JIT, XAML-Parser, Icon-Erzeugung) wieder abgeben. Die App
        // steht danach nur noch am Hotkey und soll im Leerlauf nichts festhalten.
        Task.Delay(3000).ContinueWith(_ => MemoryTrimmer.TrimNow(), TaskScheduler.Default);
    }

    // ------------------------------------------------------------ Zusehen im Netz

    /// <summary>
    /// Die Seite zum Zusehen starten, falls eingeschaltet.
    ///
    /// Sie bekommt drei Lesezugriffe und sonst nichts: den Renderzustand, die
    /// Systemlast und den Pfad des zuletzt geschriebenen Bildes. Der Pfad bleibt im
    /// Programm - nach draussen geht nur das fertig verkleinerte JPEG.
    /// </summary>
    private void StartWatch()
    {
        if (!_settings.WatchEnabled) return;

        // Ohne brauchbaren Relay-Namen gaebe es nur eine Adresse, die niemanden
        // erreicht. Das faellt spaeter auf und ist dann schwer zu deuten.
        if (!Remote.PairingInvite.IsUsableHost(_settings.RelayHost))
        {
            Notify(Localization.Strings.T("S_WatchNoRelay"));
            return;
        }

        var key = WatchKeyForSettings();

        var newest = new Web.NewestFrame(() => _renderMonitor?.Job,
                                         Decoding.FrameDecoderRegistry.CreateDefault());

        _watch = new Web.WatchService(key, _settings.RelayHost, _renderMonitor,
                                      () => _load.LastSnapshot, newest);

        _watch.Start();
    }

    /// <summary>
    /// Holt das gespeicherte Zuschauer-Geheimnis - oder legt beim ersten Mal eines an.
    ///
    /// Angelegt wird nur, wenn keines da ist. Ein neues bei jedem Start waere bequem
    /// zu schreiben und in der Sache falsch: Der Link soll gelten, bis jemand ihn
    /// ausdruecklich erneuert, sonst ist ein Lesezeichen auf dem Handy nach jedem
    /// Neustart wertlos.
    /// </summary>
    private Remote.WatchKey WatchKeyForSettings()
    {
        if (Remote.WatchStore.TryUnprotect(_settings.WatchSecret, out var stored) && stored is not null)
            return stored;

        var fresh = Remote.WatchKey.Create(null);

        _settings.WatchSecret = Remote.WatchStore.Protect(fresh);
        SettingsStore.Save(_settings);

        return fresh;
    }

    /// <summary>
    /// Erzeugt einen neuen Link und macht damit jeden alten unwirksam.
    ///
    /// Der Widerruf ist vollstaendig, weil er an der Wurzel ansetzt: Mit einem neuen
    /// Geheimnis liegen auch alle Raeume woanders. Wer den alten Link oeffnet, landet
    /// in Raeumen, in denen schlicht niemand sitzt. Das eingestellte Kennwort bleibt -
    /// es zu verwerfen, waere eine zweite Ueberraschung fuer einen Handgriff, der nur
    /// eine bewirken soll.
    /// </summary>
    public void RenewWatchLink()
    {
        string? code = Remote.WatchStore.TryUnprotect(_settings.WatchSecret, out var old) && old is not null
            ? old.Code
            : null;

        _settings.WatchSecret = Remote.WatchStore.Protect(Remote.WatchKey.Create(code));
        SettingsStore.Save(_settings);

        ApplyWatch();
    }

    /// <summary>Setzt das Kennwort fuer die hinteren Plaetze. Leer heisst: nur die freien.</summary>
    public void SetWatchCode(string? code)
    {
        if (!Remote.WatchKey.IsUsableCode(code)) return;

        var key = WatchKeyForSettings().WithCode(string.IsNullOrWhiteSpace(code) ? null : code.Trim());

        _settings.WatchSecret = Remote.WatchStore.Protect(key);
        SettingsStore.Save(_settings);

        ApplyWatch();
    }

    /// <summary>Der Dienst hinter der Zuschauerseite - oder null, wenn er nicht laeuft.</summary>
    public Web.WatchService? Watch => _watch;

    /// <summary>
    /// Ein- und ausschalten, ohne das Programm neu zu starten.
    ///
    /// Beim Einschalten entsteht ein neues Zeichen in der Adresse. Wer die alte noch
    /// offen hat, sieht ab dann nichts mehr - und das ist der Sinn eines Schalters.
    /// </summary>
    public void ApplyWatch()
    {
        var closing = _watch;
        _watch = null;

        // Nicht abwarten: Das Schliessen einer Verbindung kann an einem haengenden
        // Socket Sekunden dauern, und die Oberflaeche steht sonst so lange.
        if (closing is not null) _ = closing.DisposeAsync().AsTask();

        StartWatch();
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
    private void ShowPairing()
    {
        if (_windows.Main is MainWindow main) { main.ShowSettingsPage(true); main.Activate(); }
        else _windows.ShowPairing();
    }

    private void ShowSettings()
    {
        if (_windows.Main is MainWindow main && _viewer is null)
        { main.ShowSettingsPage(); main.Activate(); return; }
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
        /* Was hinauswollte, bevor Normalize es abgeschaltet hat.
         *
         * Der Riegel selbst sitzt in Normalize und ist damit dicht. Eine Oberflaeche,
         * die einen Schalter umlegt und dann feststellt, dass er von selbst wieder
         * zurueckspringt, laesst den Benutzer aber im Dunkeln. Also wird VOR dem
         * Normalisieren nachgesehen, was gewollt war, und daraus eine Meldung. */
        bool wollteHinaus = settings.RemoteEnabled || settings.WatchEnabled;

        settings.Normalize();

        if (wollteHinaus && !settings.TermsOk) return Localization.Strings.T("S_TermsMissing");

        if (!HotKeyDefinition.TryParse(settings.Hotkey, out var definition))
            return Localization.Strings.T("S_HotkeyInvalid");

        // Nur anfassen, wenn die Kombination selbst geaendert wurde.
        //
        // Verglichen wurde hier frueher mit der GERADE REGISTRIERTEN - und die kann
        // von der eingestellten abweichen, naemlich dann, wenn das Registrieren beim
        // Start fehlgeschlagen ist, weil ein anderes Programm die Kombination haelt.
        // Dann schlug jeder Versuch fehl, IRGENDEINE Einstellung zu speichern, mit
        // der Meldung, die Kombination sei belegt. Ein Schalter fuer eine Seite im
        // Netz hat mit der Tastenkombination aber nichts zu tun, und ein Fehlschlag
        // an einer Stelle darf nicht alles andere blockieren.
        //
        // Wer die Kombination wirklich aendert, bekommt die Meldung weiterhin - dort
        // gehoert sie hin.
        if (!string.Equals(settings.Hotkey, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            var previous = _hotkeys.Current;

            if (!_hotkeys.Register(definition))
            {
                _hotkeys.Register(previous);
                return Localization.Strings.T("S_HotkeyBusy");
            }
        }
        else if (definition != _hotkeys.Current)
        {
            // Unveraendert, aber noch nicht aktiv: Ein stiller zweiter Versuch. Klappt
            // er nicht, bleibt es dabei - gemeldet wurde es beim Start.
            _hotkeys.Register(definition);
        }

        // Eine Kopie des bisherigen Standes, bevor der neue uebernommen wird.
        //
        // Ohne das Kopieren geht der Vergleich weiter unten schief, sobald ein
        // Aufrufer das Objekt aendert, das er von getSettings() bekommen hat - dann
        // ist "vorher" dieselbe Instanz wie "nachher", und keine Aenderung faellt
        // mehr auf. Genau daran startete die Seite im Netz nicht: Der Schalter stand
        // auf an, gespeichert war es auch, nur der Server erfuhr nie davon.
        var previousSettings = _settings.Clone();

        _settings = settings;
        Localization.Strings.Apply(Localization.Strings.Parse(_settings.Language));
        SettingsStore.Save(_settings);
        UpdateTooltip();

        // Nur bei echter Aenderung neu aufbauen. Sonst risse jedes Speichern im
        // Einstellungsdialog eine stehende Verbindung ab.
        _remote.SettingsChanged(previousSettings);

        // Dasselbe fuer die Seite im Netz: Nur wenn sich Schalter oder Port geaendert
        // haben. Ein Neuaufbau bei jedem Speichern wuerde das Zeichen in der Adresse
        // erneuern, und die offene Seite auf dem Handy waere ohne Grund tot.
        if (previousSettings.WatchEnabled != _settings.WatchEnabled
            || previousSettings.WatchSecret != _settings.WatchSecret
            || previousSettings.RelayHost != _settings.RelayHost)
        {
            ApplyWatch();
        }

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

        // Beim Beenden warten wir kurz: Die Verbindungen sollen sauber enden,
        // damit im Raum kein Platz als belegt zurueckbleibt, bis der Leuchtturm die
        // Leiche selbst bemerkt.
        var watch = _watch;
        _watch = null;

        if (watch is not null)
        {
            try { watch.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* beim Beenden ist ein haengender Socket kein Anlass */ }
        }

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
