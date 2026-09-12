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

        // Jedes Fenster bekommt die dunkle Titelleiste, ohne dass es jemand einzeln
        // eintragen muss - vergessen wuerde man genau das eine, das man selten oeffnet.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                                          new RoutedEventHandler((sender, _) =>
                                          {
                                              if (sender is not Window window) return;

                                              Views.DarkTitleBar.Apply(window);
                                              Views.WindowIcon.Apply(window);
                                          }));

        // Der Fanghaken gilt fuer beide Wege. Ein Fehler in einer Ansicht soll das
        // Fenster nicht mitnehmen - vorher endete die Vorschau-Kopie bei jedem
        // Fehler wortlos, und aus Sicht des Benutzers war das Programm einfach weg.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Der Normalfall ist das ganze Programm: Ablagebereich, Tastenkombination,
        // Bruecke. Die isolierte Oberflaechen-Vorschau - ohne Hintergrunddienste und
        // ohne Zugriff auf die echte Konfiguration - bleibt als Schalter erhalten,
        // weil sie beim Entwickeln der Ansichten weiterhin gebraucht wird.
        //
        // Waehrend des Umbaus war es umgekehrt: Die Kopie startete isoliert, damit
        // sie neben der laufenden Fassung nichts anfasst. Als Hauptfassung waere das
        // genau falsch herum.
        if (e.Args.Contains("--ui-preview", StringComparer.OrdinalIgnoreCase))
        {
            Views.DesktopPreview.Start();
            return;
        }

        // Mit eigenem Konfigurationspfad darf eine Testinstanz neben der normalen laufen.
        string mutexName = FrameFlip.Configuration.SettingsStore.Override is { Length: > 0 }
            ? @"Local\FrameFlip.SingleInstance.Test"
            : @"Local\FrameFlip.SingleInstance";

        _singleInstance = new Mutex(true, mutexName, out bool isFirst);

        if (!isFirst)
        {
            // Eine zweite Instanz wuerde den Hotkey nicht bekommen und nur Speicher
            // kosten. Sie beendet sich also - klopft vorher aber an: Wer die exe ein
            // zweites Mal startet, will das Fenster sehen. Ohne dieses Zeichen sah
            // ein Doppelklick aus wie ein defektes Programm, weil schlicht nichts
            // passierte.
            try
            {
                using var knock = EventWaitHandle.OpenExisting(mutexName + ".Show");
                knock.Set();
            }
            catch (Exception)
            {
                // Die erste Instanz ist aelter als dieses Zeichen oder gerade am
                // Beenden. Dann bleibt es beim stillen Ende.
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        WaitForKnock(mutexName + ".Show");

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

        _host = new AppHost();
        _host.Start();

        // Rohcache-Ordner aufraeumen, die eine fruehere Sitzung nicht loeschen konnte -
        // etwa nach einem Absturz. Im Hintergrund, damit der Start nicht wartet.
        Task.Run(() => FrameFlip.Caching.RawFrameCache.CleanOrphans(TimeSpan.FromHours(6)));

        // Dasselbe fuer vorbereitete Videos, die niemand exportiert hat. Sie sind
        // gross, und ihr einziger Zweck war eine Abkuerzung, die nicht genommen wurde.
        Task.Run(FrameFlip.Playback.PreparedVideo.CleanOld);

        // "--show" oeffnet das Fenster gleich beim Start.
        //
        // FrameFlip lebt im Ablagebereich: Es startet still und wartet auf die
        // Tastenkombination. Das ist richtig fuer den taeglichen Gebrauch und
        // unpraktisch in jedem anderen Fall - beim Vergleichen zweier Staende, beim
        // Vorfuehren, nach einer frischen Installation. Wer es einmal sehen will,
        // soll es nicht erst im Ablagebereich suchen muessen.
        if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(new Action(() => _host?.ShowMain()));
        }

        // "--preview <datei>" oeffnet die Vorschau direkt, ohne Umweg ueber den Explorer.
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (!string.Equals(e.Args[i], "--preview", StringComparison.OrdinalIgnoreCase)) continue;

            string path = e.Args[i + 1];
            Dispatcher.BeginInvoke(new Action(() => _host?.OpenFile(path)));
            break;
        }
    }

    /// <summary>
    /// Auf das Zeichen einer zweiten Instanz warten und das Fenster zeigen.
    ///
    /// Ein benanntes Ereignis statt eines Sockets oder einer Pipe: Es gibt genau
    /// ein Signal zu uebertragen - "zeig dich" -, und dafuer waere alles andere zu
    /// viel Maschinerie. Der Thread ist ein Hintergrundthread; er haelt das
    /// Programm beim Beenden nicht auf.
    /// </summary>
    private void WaitForKnock(string name)
    {
        EventWaitHandle handle;

        try
        {
            handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
        catch (Exception)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                }
                catch (Exception)
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() => _host?.ShowMain()));
            }
        })
        {
            IsBackground = true,
            Name = "FrameFlipKnock",
        };

        thread.Start();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Ein Fehler in einer Ansicht darf die Tray-Anwendung nicht mitnehmen.
        // Verschluckt wird er trotzdem nicht: Mit gesetztem FRAMEFLIP_CONFIG steht
        // er im Protokoll daneben, sonst waere ein stiller Fehler schlimmer als ein
        // lauter.
        Configuration.SettingsStore.Trace("Unbehandelt: " + e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
