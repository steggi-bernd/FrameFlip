using System.Diagnostics;
using System.IO;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Interop;
using FrameFlip.Sequencing;
using FrameFlip.Views;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

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
    private WinForms.NotifyIcon? _trayIcon;
    private IntPtr _iconHandle;
    private ViewerWindow? _viewer;

    /// <summary>Nimmt Meldungen des Blender-Addons entgegen. Null, wenn abgeschaltet.</summary>
    private Bridge.RenderMonitor? _renderMonitor;

    /// <summary>Reicht den Renderfortschritt ans Handy weiter. Null, solange nicht gekoppelt.</summary>
    private Remote.RemoteLink? _remote;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;
    private SystemLoadMonitor? _loadMonitor;
    private bool _disposed;

    public void Start()
    {
        _settings = SettingsStore.Load();

        // Vor allem anderen: Die Oberflaeche soll gleich in der richtigen Sprache
        // erscheinen, nicht erst nach dem ersten Fensterwechsel.
        Localization.Strings.Apply(Localization.Strings.Parse(_settings.Language));

        CreateTrayIcon();

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

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();

        var main = menu.Items.Add(string.Empty, null, (_, _) => ShowMain());
        var open = menu.Items.Add(string.Empty, null, (_, _) => Toggle());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var settings = menu.Items.Add(string.Empty, null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var exit = menu.Items.Add(string.Empty, null, (_, _) => Exit());

        // Das Menue entsteht einmal beim Start und wuerde einen Sprachwechsel sonst
        // nicht mitbekommen - es haengt an WinForms und kann kein DynamicResource.
        void Relabel()
        {
            main.Text = Localization.Strings.T("S_TrayMain");
            open.Text = Localization.Strings.T("S_TrayOpen");
            settings.Text = Localization.Strings.T("S_TraySettings");
            exit.Text = Localization.Strings.T("S_TrayExit");
        }

        Relabel();
        Localization.Strings.Changed += Relabel;

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = BuildIcon(out _iconHandle),
            Visible = true,
            Text = "FrameFlip",
            ContextMenuStrip = menu
        };

        // Doppelklick oeffnet das Hauptfenster, nicht die Vorschau: Die haengt am
        // Hotkey und an dem, was im Explorer markiert ist - ein Doppelklick auf ein
        // Tray-Symbol weiss davon nichts.
        _trayIcon.DoubleClick += (_, _) => ShowMain();
    }

    /// <summary>Zur Laufzeit gezeichnet - so bleibt das Projekt ohne Binaerassets.</summary>
    private static Drawing.Icon BuildIcon(out IntPtr handle)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var body = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 28, 28, 28));
            g.FillRectangle(body, 2, 5, 28, 22);

            using var perforation = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 150, 150, 150));
            for (int y = 8; y <= 22; y += 7)
            {
                g.FillRectangle(perforation, 4, y, 3, 3);
                g.FillRectangle(perforation, 25, y, 3, 3);
            }

            using var play = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 224, 138, 60));
            g.FillPolygon(play, new[]
            {
                new Drawing.Point(13, 10),
                new Drawing.Point(23, 16),
                new Drawing.Point(13, 22)
            });
        }

        handle = bitmap.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null) return;

        var text = $"FrameFlip – {_hotkeys.Current}";
        _trayIcon.Text = text.Length > 62 ? text[..62] : text;   // NotifyIcon.Text ist begrenzt
    }

    private void Notify(string message)
    {
        try
        {
            _trayIcon?.ShowBalloonTip(4000, "FrameFlip", message, WinForms.ToolTipIcon.Info);
        }
        catch (Exception)
        {
            // Benachrichtigungen koennen systemseitig unterdrueckt sein.
        }
    }

    // ------------------------------------------------------------ Viewer

    /// <summary>
    /// Der Hotkey wirkt als Umschalter - mit einer Ausnahme: zeigt der Explorer
    /// inzwischen auf eine ANDERE Sequenz, wird der Inhalt getauscht statt
    /// geschlossen. Andernfalls muesste man zweimal druecken, nur um von einem
    /// Renderordner zum naechsten zu kommen.
    /// </summary>
    private void Toggle() => OpenViewer();

    private void OpenViewer()
    {
        var target = ExplorerSelectionProvider.Resolve();
        if (target is null || !target.HasAnything)
        {
            // Kein Explorer im Vordergrund: dann ist der Hotkey schlicht ein
            // Schliessbefehl fuer die offene Vorschau.
            if (_viewer is not null) { _viewer.BeginClose(); return; }

            Notify(Localization.Strings.T("S_NoExplorer"));
            return;
        }

        string? seed = target.FilePath;
        if (seed is null || !_decoders.IsSupported(Path.GetExtension(seed)))
        {
            // Nichts oder etwas Unlesbares selektiert: erstes darstellbare Bild im Ordner.
            seed = target.FolderPath is not null
                ? SequenceScanner.FindFirstImage(target.FolderPath, _decoders)
                : null;
        }

        if (seed is null)
        {
            if (_viewer is not null) { _viewer.BeginClose(); return; }

            Notify(Localization.Strings.T("S_NoImageInFolder"));
            return;
        }

        var sequence = SequenceScanner.Scan(seed, _decoders);
        if (sequence is null || sequence.Count == 0)
        {
            if (_viewer is not null) { _viewer.BeginClose(); return; }

            Notify(Localization.Strings.T("S_SequenceUnreadable"));
            return;
        }

        ShowSequence(sequence, seed, target.WindowHandle, allowToggleClose: true);
    }

    /// <summary>
    /// Oeffnet die Vorschau fuer eine bestimmte Datei, ohne den Explorer zu befragen.
    /// Wird von der Befehlszeile benutzt: FrameFlip.exe --preview "C:\pfad\render_0001.png"
    /// </summary>
    public void OpenFile(string path)
    {
        if (!File.Exists(path) || !_decoders.IsSupported(Path.GetExtension(path)))
        {
            Notify(Localization.Strings.T("S_FileUnsupported"));
            return;
        }

        var sequence = SequenceScanner.Scan(path, _decoders);
        if (sequence is null || sequence.Count == 0)
        {
            Notify(Localization.Strings.T("S_SequenceUnreadable"));
            return;
        }

        // Vom Kommandozeilenaufruf aus wird nie geschlossen: wer eine Datei uebergibt,
        // will sie sehen, auch wenn sie zufaellig schon offen ist.
        ShowSequence(sequence, path, IntPtr.Zero, allowToggleClose: false);
    }

    private void ShowSequence(ImageSequence sequence, string seed, IntPtr explorerWindow,
                              bool allowToggleClose)
    {
        int start = Math.Max(0, sequence.IndexOfPath(seed));

        // Fenster in Mediengroesse: dafuer wird nur der Header der Datei gelesen.
        if (!_decoders.TryProbeSize(sequence.Frames[start].Path, out int sourceWidth, out int sourceHeight))
        {
            if (_viewer is null) Notify(Localization.Strings.T("S_ImageUnreadable"));
            return;
        }

        // Ein offenes Fenster bekommt den neuen Inhalt, statt dass ein zweites
        // aufgeht. Zeigt der Explorer auf dieselbe Sequenz, wirkt der Hotkey wie
        // erwartet als Umschalter und schliesst.
        if (_viewer is { } open)
        {
            if (allowToggleClose && open.ShowsSameSequence(sequence)) open.BeginClose();
            else if (open.ShowsSameSequence(sequence)) open.Activate();
            else open.TryLoadSequence(sequence, start, sourceWidth, sourceHeight);
            return;
        }

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
        bool wanted = _viewer is not null || _remote is not null || _mainWindow is not null;

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
    public void ShowMain()
    {
        if (_mainWindow is not null)
        {
            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
                _mainWindow.WindowState = System.Windows.WindowState.Normal;

            _mainWindow.Activate();
            return;
        }

        LivePage.Load = () => _loadMonitor?.LastSnapshot;

        var window = new MainWindow(_renderMonitor, () => _remote?.State, ShowSettings, OpenFile);
        window.Closed += (_, _) =>
        {
            _mainWindow = null;

            // Die Lastmessung lief womoeglich nur fuer dieses Fenster.
            EnsureLoadMonitor();
        };

        _mainWindow = window;

        // Ohne offene Vorschau laeuft die Messung sonst nicht, und die Kacheln
        // blieben leer - ausgerechnet auf der Seite, die sie zeigt.
        EnsureLoadMonitor();

        window.Show();
        window.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Die Vorschau darf nicht wegschliessen, waehrend der Dialog den Fokus hat.
        var viewer = _viewer;
        if (viewer is not null) viewer.ModalDialogOpen = true;

        var window = new SettingsWindow(_settings, ApplySettings, () => _remote?.State);

        // Die Vorschau liegt ueber allem. Ohne dasselbe fuer den Dialog erschiene er
        // dahinter - man klickt auf "Einstellungen" und es passiert scheinbar nichts.
        if (viewer is not null && viewer.IsVisible)
        {
            window.Owner = viewer;
            window.Topmost = true;
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }

        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (viewer is not null) viewer.ModalDialogOpen = false;
        };
        _settingsWindow = window;
        window.Show();
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

            _remote = new Remote.RemoteLink(invite, _renderMonitor, () => _loadMonitor?.LastSnapshot);
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
        _settingsWindow?.Close();
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

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }
}
