using System.Windows;

namespace FrameFlip.Lifecycle;

/// <summary>
/// Genau ein Hauptfenster, ein Kopplungs- und ein Einstellungsdialog. Kennt nur
/// WPF-Lebenszyklen; Einstellungen, Relais und Lastmessung bleiben beim Host.
/// Alle Aufrufe kommen vom UI-Thread.
/// </summary>
internal sealed class AppWindowController
{
    private readonly Func<Window> _createMain;
    private readonly Func<Window> _createSettings;
    private readonly Func<Window> _createPairing;
    private readonly Action _mainChanged;

    internal Window? Main { get; private set; }
    internal Window? Settings { get; private set; }
    internal Window? Pairing { get; private set; }

    internal AppWindowController(Func<Window> createMain, Func<Window> createSettings,
                                 Func<Window> createPairing, Action mainChanged)
    {
        _createMain = createMain;
        _createSettings = createSettings;
        _createPairing = createPairing;
        _mainChanged = mainChanged;
    }

    internal void ShowMain()
    {
        if (Main is not null)
        {
            if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
            Main.Activate();
            return;
        }

        var window = _createMain();
        window.Closed += (_, _) =>
        {
            Main = null;
            _mainChanged();
        };
        Main = window;

        // Die Lastmessung muss das neue Fenster schon vor Show kennen und
        // nach Closed wieder ohne dieses Fenster entscheiden koennen.
        _mainChanged();
        window.Show();
        window.Activate();
    }

    internal void ShowPairing()
    {
        if (Pairing is not null) { Pairing.Activate(); return; }

        var window = _createPairing();
        window.Owner = Main;
        window.WindowStartupLocation = Main is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        window.Closed += (_, _) => Pairing = null;
        Pairing = window;
        window.Show();
    }

    /// <summary>Der Dialog gibt beim Schliessen genau den beim Oeffnen erfassten Viewer frei.</summary>
    internal void ShowSettings(Window? viewer, Action<bool> setModalDialogOpen)
    {
        if (Settings is not null) { Settings.Activate(); return; }

        // Noch vor der Konstruktion schuetzen: Schon der Dialogaufbau kann dem
        // Viewer den Fokus nehmen. Ein unsichtbarer Viewer bleibt ohne Owner.
        if (viewer is not null) setModalDialogOpen(true);
        var window = _createSettings();
        if (viewer is not null && viewer.IsVisible)
        {
            window.Owner = viewer;
            window.Topmost = true;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.Closed += (_, _) =>
        {
            Settings = null;
            if (viewer is not null) setModalDialogOpen(false);
        };
        Settings = window;
        window.Show();
    }

    internal void CloseSettings() => Settings?.Close();
}
