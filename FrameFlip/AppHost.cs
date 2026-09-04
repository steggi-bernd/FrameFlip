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
    private SettingsWindow? _settingsWindow;
    private SystemLoadMonitor? _loadMonitor;
    private bool _disposed;

    public void Start()
    {
        _settings = SettingsStore.Load();

        CreateTrayIcon();

        _hotkeys.Pressed += Toggle;

        if (!HotKeyDefinition.TryParse(_settings.Hotkey, out var definition))
            definition = HotKeyDefinition.Default;

        if (!_hotkeys.Register(definition))
        {
            Notify($"Der Hotkey {definition} ist belegt. Bitte in den Einstellungen aendern.");
        }

        UpdateTooltip();

        // Startballast (JIT, XAML-Parser, Icon-Erzeugung) wieder abgeben. Die App
        // steht danach nur noch am Hotkey und soll im Leerlauf nichts festhalten.
        Task.Delay(3000).ContinueWith(_ => MemoryTrimmer.TrimNow(), TaskScheduler.Default);
    }

    // ------------------------------------------------------------ Tray

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Vorschau öffnen", null, (_, _) => Toggle());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Einstellungen …", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Exit());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = BuildIcon(out _iconHandle),
            Visible = true,
            Text = "FrameFlip",
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => Toggle();
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

            Notify("Kein Explorer-Fenster gefunden. Ordner mit der Bildsequenz oeffnen und erneut versuchen.");
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

            Notify("Im aktiven Ordner liegt kein unterstuetztes Bild (PNG, JPG, TIFF, BMP, WebP).");
            return;
        }

        var sequence = SequenceScanner.Scan(seed, _decoders);
        if (sequence is null || sequence.Count == 0)
        {
            if (_viewer is not null) { _viewer.BeginClose(); return; }

            Notify("Die Sequenz konnte nicht gelesen werden.");
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
            Notify("Die angegebene Datei ist kein unterstuetztes Bild.");
            return;
        }

        var sequence = SequenceScanner.Scan(path, _decoders);
        if (sequence is null || sequence.Count == 0)
        {
            Notify("Die Sequenz konnte nicht gelesen werden.");
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
            if (_viewer is null) Notify("Das Bild konnte nicht gelesen werden.");
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

        StartLoadMonitor();
        int maxWorkers = _settings.AdaptiveResources && _loadMonitor is not null ? _loadMonitor.MaxDecoderThreads : 1;

        var bounds = WindowBoundsFor(explorerWindow, sourceWidth, sourceHeight);
        var viewer = new ViewerWindow(sequence, start, _settings, Persist, _decoders,
                                      bounds, sourceWidth, sourceHeight, maxWorkers);
        viewer.Closed += (_, _) =>
        {
            _viewer = null;
            StopLoadMonitor();
        };

        _viewer = viewer;
        viewer.Show();
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

    private void StartLoadMonitor()
    {
        StopLoadMonitor();
        if (!_settings.AdaptiveResources) return;

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

        var window = new SettingsWindow(_settings, ApplySettings);
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
            return "Die Tastenkombination ist ungueltig.";

        if (definition != _hotkeys.Current)
        {
            var previous = _hotkeys.Current;
            if (!_hotkeys.Register(definition))
            {
                _hotkeys.Register(previous);
                return "Die Kombination ist bereits von einer anderen Anwendung belegt.";
            }
        }

        _settings = settings;
        SettingsStore.Save(_settings);
        UpdateTooltip();

        // Puffer- und Budgetwerte greifen beim naechsten Oeffnen des Viewers.
        return null;
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
