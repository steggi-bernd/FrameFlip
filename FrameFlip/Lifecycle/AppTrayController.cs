using FrameFlip.Localization;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Besitzt Tray, Menue und Icon samt Sprachwechsel-Abonnement. Der Host liefert
/// nur die vier Anwendungsaktionen und den aktuellen Hotkey. Alle Aufrufe kommen
/// vom UI-Thread; ein unsichtbarer Tray erlaubt Tests ohne Desktop-Meldungen.
/// </summary>
internal sealed class AppTrayController : IDisposable
{
    private readonly WinForms.NotifyIcon _trayIcon = new();
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly Drawing.Icon? _image;
    private readonly WinForms.ToolStripItem _main, _open, _settings, _exit;
    private readonly Action _showMain;
    private bool _disposed;

    internal AppTrayController(Action showMain, Action toggle, Action showSettings, Action exit,
                               bool visible = true)
    {
        _showMain = showMain;
        try
        {
            _main = _menu.Items.Add(string.Empty, null, (_, _) => showMain());
            _open = _menu.Items.Add(string.Empty, null, (_, _) => toggle());
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _settings = _menu.Items.Add(string.Empty, null, (_, _) => showSettings());
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _exit = _menu.Items.Add(string.Empty, null, (_, _) => exit());
            Relabel();

            _image = BuildIcon();
            _trayIcon.Icon = _image;
            _trayIcon.Text = "FrameFlip";
            _trayIcon.ContextMenuStrip = _menu;
            _trayIcon.DoubleClick += OnDoubleClick;
            _trayIcon.Visible = visible;

            // WinForms unterstuetzt kein WPF-DynamicResource.
            Strings.Changed += Relabel;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Relabel()
    {
        if (_disposed) return;
        _main.Text = Strings.T("S_TrayMain");
        _open.Text = Strings.T("S_TrayOpen");
        _settings.Text = Strings.T("S_TraySettings");
        _exit.Text = Strings.T("S_TrayExit");
    }

    // Doppelklick oeffnet das Hauptfenster; die Vorschau haengt an der Explorer-Auswahl.
    private void OnDoubleClick(object? sender, EventArgs e)
    {
        if (!_disposed) _showMain();
    }

    internal void UpdateTooltip(string hotkey)
    {
        if (_disposed) return;
        var text = $"FrameFlip – {hotkey}";
        _trayIcon.Text = text.Length > 62 ? text[..62] : text;
    }

    internal void Notify(string message)
    {
        if (_disposed) return;
        try
        {
            _trayIcon.ShowBalloonTip(4000, "FrameFlip", message, WinForms.ToolTipIcon.Info);
        }
        catch (Exception)
        {
            // Benachrichtigungen koennen systemseitig unterdrueckt sein.
        }
    }

    /// <summary>
    /// Die mitgelieferte ICO enthaelt mehrere, zur Skalierung passende Groessen.
    /// Ein fehlendes Symbol darf den Start nicht abbrechen. Das erzeugte Icon
    /// gehoert diesem Controller; NotifyIcon gibt es nicht selbst frei.
    /// </summary>
    private static Drawing.Icon? BuildIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/FrameFlip;component/Assets/FrameFlip.ico");
            using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is null) return null;

            int wanted = WinForms.SystemInformation.SmallIconSize.Width;
            return new Drawing.Icon(stream, new Drawing.Size(wanted, wanted));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Strings.Changed -= Relabel;
        _trayIcon.DoubleClick -= OnDoubleClick;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip = null;
        _trayIcon.Icon = null;
        _menu.Dispose();
        _trayIcon.Dispose();
        _image?.Dispose();
    }
}
