using System.Reflection;
using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Interop;
using FrameFlip.Lifecycle;
using FrameFlip.Sequencing;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>Charakterisiert die Fensterregeln zuerst im AppHost, ohne Start/Tray/Relais oder Benutzerdaten.</summary>
public static class AppHostWindowInvariants
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Check.Group("AppHost - Hauptfenster, Kopplung und Einstellungen");
        var created = new List<Window>();
        int mains = 0, pairings = 0, settings = 0;
        using var host = new AppHost(
            () => { mains++; return NewWindow(created); },
            () => { settings++; return NewWindow(created); },
            () => { pairings++; return NewWindow(created); });
        Set(host, "_settings", new AppSettings { AdaptiveResources = false });
        var previousLoad = LivePage.Load;
        ViewerWindow? viewer = null;
        try
        {
            Check.That(mains + pairings + settings == 0, "Host-Konstruktion legt noch kein Anwendungsfenster an");
            Call(host, "ShowPairing");
            var standalone = Field<Window>(host, "_pairingWindow");
            Check.That(standalone.Owner is null && standalone.WindowStartupLocation == WindowStartupLocation.CenterScreen,
                       "Kopplung ohne Hauptfenster ist ein eigenstaendiger Dialog");
            Call(host, "ShowPairing");
            Check.That(pairings == 1 && ReferenceEquals(standalone, Field<Window>(host, "_pairingWindow")),
                       "erneutes Koppeln verwendet dasselbe Fenster");
            standalone.Close();
            Check.That(Field<Window?>(host, "_pairingWindow") is null, "Schliessen gibt den Kopplungsdialog frei");

            host.ShowMain();
            var main = Field<Window>(host, "_mainWindow");
            Check.That(main.IsVisible && mains == 1, "Hauptfenster wird bei Bedarf erzeugt und gezeigt");
            main.WindowState = WindowState.Minimized;
            host.ShowMain();
            Check.That(mains == 1 && main.WindowState == WindowState.Normal,
                       "erneutes Oeffnen stellt dasselbe minimierte Hauptfenster wieder her");
            Check.That(Field<object?>(host, "_loadMonitor") is null, "isolierte Tests starten ohne adaptive Ressourcen keinen Lastmonitor");
            Call(host, "ShowPairing");
            var owned = Field<Window>(host, "_pairingWindow");
            Check.That(pairings == 2 && ReferenceEquals(owned.Owner, main)
                       && owned.WindowStartupLocation == WindowStartupLocation.CenterOwner,
                       "neuer Kopplungsdialog gehoert zum offenen Hauptfenster");
            main.Close();
            Check.That(Field<Window?>(host, "_mainWindow") is null && Field<Window?>(host, "_pairingWindow") is null,
                       "Hauptfenster schliesst seinen Kopplungsdialog und gibt beide frei");
            host.ShowMain();
            Check.That(mains == 2 && !ReferenceEquals(main, Field<Window>(host, "_mainWindow")),
                       "nach dem Schliessen entsteht ein neues Hauptfenster");

            Call(host, "ShowSettings");
            var dialog = Field<Window>(host, "_settingsWindow");
            Check.That(dialog.Owner is null && !dialog.Topmost, "Einstellungen ohne Viewer bleiben ein eigenstaendiges Fenster");
            Call(host, "ShowSettings");
            Check.That(settings == 1 && ReferenceEquals(dialog, Field<Window>(host, "_settingsWindow")),
                       "erneute Einstellungen erzeugen keinen zweiten Dialog");
            dialog.Close();
            Check.That(Field<Window?>(host, "_settingsWindow") is null, "geschlossene Einstellungen werden freigegeben");

            viewer = CreateViewer();
            Set(host, "_viewer", viewer);
            Call(host, "ShowSettings");
            dialog = Field<Window>(host, "_settingsWindow");
            Check.That(settings == 2 && viewer.ModalDialogOpen, "offene Einstellungen schuetzen den zugehoerigen Viewer vor Fokusverlust");
            Check.That(dialog.Owner is null, "ein noch unsichtbarer Viewer wird nicht zum Dialog-Owner");
            // Der Close-Callback muss den beim Oeffnen erfassten Viewer treffen,
            // auch wenn der Host inzwischen eine andere Auswahl besitzt.
            Set(host, "_viewer", null);
            dialog.Close();
            Check.That(!viewer.ModalDialogOpen, "Dialogende gibt den urspruenglichen Viewer wieder frei");
        }
        finally
        {
            foreach (var window in created.Where(w => w.IsVisible).Reverse()) window.Close();
            viewer?.Close();
            Set(host, "_viewer", null);
            LivePage.Load = previousLoad;
        }
    }

    internal static Window NewWindow(List<Window> created)
    {
        // Echte WPF-Owner-/Closed-Ereignisse, aber keine sichtbare Testoberflaeche.
        var window = new Window
        {
            Width = 1, Height = 1, Opacity = 0, ShowActivated = false,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        created.Add(window);
        return window;
    }

    private static ViewerWindow CreateViewer()
    {
        var sequence = new ImageSequence(new SequencePattern(".", "frame_", 4, "", ".png"),
            new[] { new SequenceFrame(1, "missing-frame.png", "frame_0001.png") });
        return new ViewerWindow(sequence, 0, new AppSettings(), _ => { }, FrameDecoderRegistry.CreateDefault(),
            new PixelRect(0, 0, 800, 600), 800, 600, 1);
    }

    private static T Field<T>(AppHost host, string name)
    {
        var windows = (AppWindowController)typeof(AppHost).GetField("_windows", Hidden)!.GetValue(host)!;
        object? value = name switch
        {
            "_mainWindow" => windows.Main,
            "_settingsWindow" => windows.Settings,
            "_pairingWindow" => windows.Pairing,
            _ => typeof(AppHost).GetField(name, Hidden)!.GetValue(host),
        };
        return (T)value!;
    }
    private static void Set(AppHost host, string name, object? value) => typeof(AppHost).GetField(name, Hidden)!.SetValue(host, value);
    private static void Call(AppHost host, string name) => typeof(AppHost).GetMethod(name, Hidden)!.Invoke(host, null);
}
