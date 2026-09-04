using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace FrameFlip;

public partial class App : Application
{
    static App()
    {
        // Laeuft vor dem ersten Fenster. Auf Systemen, auf denen das Manifest greift,
        // ist der Aufruf wirkungslos - schadet dort aber auch nicht.
        FrameFlip.Interop.NativeMethods.EnablePerMonitorDpiAwareness();
    }

    private Mutex? _singleInstance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Mit eigenem Konfigurationspfad darf eine Testinstanz neben der normalen laufen.
        string mutexName = FrameFlip.Configuration.SettingsStore.Override is { Length: > 0 }
            ? @"Local\FrameFlip.SingleInstance.Test"
            : @"Local\FrameFlip.SingleInstance";

        _singleInstance = new Mutex(true, mutexName, out bool isFirst);
        if (!isFirst)
        {
            // Eine zweite Instanz wuerde den Hotkey nicht bekommen und nur Speicher kosten.
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        // Die App laeuft neben einem Render. Sie bekommt, was uebrig ist.
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception)
        {
            // Ohne ausreichende Rechte bleibt es bei Normal.
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = new AppHost();
        _host.Start();

        // Rohcache-Ordner aufraeumen, die eine fruehere Sitzung nicht loeschen konnte -
        // etwa nach einem Absturz. Im Hintergrund, damit der Start nicht wartet.
        Task.Run(() => FrameFlip.Caching.RawFrameCache.CleanOrphans(TimeSpan.FromHours(6)));

        // "--preview <datei>" oeffnet die Vorschau direkt, ohne Umweg ueber den Explorer.
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (!string.Equals(e.Args[i], "--preview", StringComparison.OrdinalIgnoreCase)) continue;

            string path = e.Args[i + 1];
            Dispatcher.BeginInvoke(new Action(() => _host?.OpenFile(path)));
            break;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ein Fehler in der Vorschau darf die Tray-Anwendung nicht mitnehmen.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
