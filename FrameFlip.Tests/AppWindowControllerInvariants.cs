using System.ComponentModel;
using System.Windows;
using FrameFlip.Lifecycle;

namespace FrameFlip.Tests;

public static class AppWindowControllerInvariants
{
    public static void Run()
    {
        MainLifetime();
        DialogOwnership();
    }

    private static void MainLifetime()
    {
        Check.Group("App-Fenster - Lebenszyklus und Lastmonitor-Signal");
        var created = new List<Window>();
        var changes = new List<(bool Exists, bool Visible)>();
        AppWindowController? windows = null;
        windows = new AppWindowController(() => AppHostWindowInvariants.NewWindow(created),
            () => AppHostWindowInvariants.NewWindow(created), () => AppHostWindowInvariants.NewWindow(created),
            () => changes.Add((windows!.Main is not null, windows.Main?.IsVisible == true)));
        try
        {
            windows.CloseSettings();
            Check.That(created.Count == 0 && changes.Count == 0, "leerer Controller und Schliessen ohne Dialog erzeugen keine Fenster");
            windows.ShowMain();
            var main = windows.Main!;
            Check.That(changes.SequenceEqual(new[] { (true, false) }), "Host erfaehrt vor Show vom neuen Hauptfenster");
            windows.ShowMain();
            Check.That(created.Count == 1 && changes.Count == 1, "Wiederverwenden des Hauptfensters startet keine neue Lastmessung");
            CancelEventHandler cancel = (_, e) => e.Cancel = true;
            main.Closing += cancel;
            main.Close();
            Check.That(ReferenceEquals(windows.Main, main) && changes.Count == 1,
                       "abgebrochenes Schliessen behaelt Fenster und Monitorbedarf");
            main.Closing -= cancel;

            windows.ShowPairing();
            windows.Pairing!.Close();
            windows.ShowSettings(null, _ => throw new InvalidOperationException("kein Viewer vorhanden"));
            windows.CloseSettings();
            Check.That(changes.Count == 1, "Dialoge aendern den Bedarf des Hauptfensters nicht");
            main.Close();
            Check.That(changes.SequenceEqual(new[] { (true, false), (false, false) }),
                       "Host erfaehrt erst nach Freigabe der Referenz vom geschlossenen Hauptfenster");
            windows.ShowMain();
            Check.That(changes.Count == 3 && !ReferenceEquals(windows.Main, main),
                       "erneutes Hauptfenster beginnt einen neuen Lebenszyklus");
        }
        finally { foreach (var window in created.Where(w => w.IsVisible).Reverse()) window.Close(); }
    }

    private static void DialogOwnership()
    {
        Check.Group("App-Fenster - Einstellungsdialog und Viewer-Schutz");
        var created = new List<Window>();
        var modal = new List<bool>();
        var otherModal = new List<bool>();
        bool protectedDuringCreation = false;
        var windows = new AppWindowController(() => AppHostWindowInvariants.NewWindow(created),
            () =>
            {
                protectedDuringCreation = modal.LastOrDefault();
                return AppHostWindowInvariants.NewWindow(created);
            }, () => AppHostWindowInvariants.NewWindow(created), () => { });
        var viewer = AppHostWindowInvariants.NewWindow(created);
        var otherViewer = AppHostWindowInvariants.NewWindow(created);
        try
        {
            viewer.Show();
            windows.ShowSettings(viewer, modal.Add);
            var dialog = windows.Settings!;
            Check.That(protectedDuringCreation && modal.SequenceEqual(new[] { true }), "Viewer-Schutz steht schon vor der Dialogkonstruktion");
            Check.That(ReferenceEquals(dialog.Owner, viewer) && dialog.Topmost
                       && dialog.WindowStartupLocation == WindowStartupLocation.CenterOwner,
                       "sichtbarer Viewer besitzt einen darueber zentrierten Einstellungsdialog");
            windows.ShowSettings(otherViewer, otherModal.Add);
            Check.That(ReferenceEquals(windows.Settings, dialog) && otherModal.Count == 0 && modal.Count == 1,
                       "erneutes Oeffnen behaelt den urspruenglichen Viewer und dessen Schutz");

            CancelEventHandler cancel = (_, e) => e.Cancel = true;
            dialog.Closing += cancel;
            windows.CloseSettings();
            Check.That(ReferenceEquals(windows.Settings, dialog) && modal.Count == 1,
                       "abgebrochenes Dialogende laesst den Viewer weiter geschuetzt");
            dialog.Closing -= cancel;
            windows.CloseSettings();
            Check.That(windows.Settings is null && modal.SequenceEqual(new[] { true, false }) && otherModal.Count == 0,
                       "Dialogende gibt genau den urspruenglichen Viewer frei");
            windows.CloseSettings();
            Check.That(modal.Count == 2, "wiederholtes Schliessen sendet keine zweite Freigabe");

            windows.ShowSettings(viewer, modal.Add);
            Check.That(!ReferenceEquals(windows.Settings, dialog), "nach Dialogende wird ein neuer Dialog erzeugt");
            viewer.Close();
            Check.That(windows.Settings is null && modal.SequenceEqual(new[] { true, false, true, false }),
                       "Schliessen des Owners raeumt den Dialog und seinen Viewer-Schutz auf");
        }
        finally
        {
            foreach (var window in created.Where(w => w.IsVisible).Reverse()) window.Close();
            otherViewer.Close();
        }
    }
}
