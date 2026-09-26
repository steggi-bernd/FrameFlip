using System.IO;
using System.Windows;
using FrameFlip.Atelier;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Ganze Folge oder nur dieses Bild (docs/Atelier-Werkzeugplan.md, Entscheidung 7). "Nur dieses
/// Bild" loest das Bild aus der Folge heraus: ein eigener Ordner im Quellordner mit einer Kopie
/// des Originals und einem eigenen Projekt, das mit dem Stand der Folge beginnt - siehe
/// <see cref="FrameDetach"/>. Zurueck geht es in die Folge, auf das Bild, aus dem es kam.
/// </summary>
public partial class AtelierPage
{
    /// <summary>
    /// Der Umschalter am Ende der Werkzeugzeile: bei einer Folge auf "ganze Folge", bei einem
    /// herausgeloesten Einzelbild auf "nur dieses Bild", bei einem gewoehnlichen Bild gar nicht.
    /// </summary>
    private void ShowFrameMode()
    {
        bool detached = _projects.Origin is { Length: > 0 };
        bool sequence = _sequence is { Count: > 1 };

        ToolBand.ShowFrameSwitch(detached || sequence, detached);
    }

    /// <summary>
    /// Umschalten: das offene Bild der Folge herausloesen oder aus dem Einzelbild zurueck in die
    /// Folge. Das Ergebnis sagt, ob umgeschaltet wurde; das andere Bild oeffnet sich danach.
    /// </summary>
    internal Task<bool> UseFrameMode(bool single)
    {
        if (_path is null) return Task.FromResult(false);

        if (!single)
        {
            if (_projects.Origin is not { Length: > 0 } origin) return Task.FromResult(false);

            if (!File.Exists(origin))
            {
                Properties.Told(Strings.T("S_FrameOriginMissing"));
                ShowFrameMode();
                return Task.FromResult(false);
            }

            Open(origin);
            return Task.FromResult(true);
        }

        if (_projects.Origin is not null || _sequence is not { Count: > 1 }) return Task.FromResult(false);

        // Der Stand der Folge, mit allem, was eben erst gezogen wurde.
        if (InNodes) KeepNodes();
        if (_projects.Snapshot() is not { } snapshot) return Task.FromResult(false);

        string frame = _path;
        var store = _projects.Store;
        var done = new TaskCompletionSource<bool>();

        BusyBadge.Visibility = Visibility.Visible;

        // Kopiert wird im Hintergrund - eine EXR mit allen Paessen sind Gigabytes. Zurueck auf
        // den Oberflaechenfaden ausdruecklich ueber den Dispatcher.
        Task.Run(() => FrameDetach.Detach(frame, snapshot, store)).ContinueWith(copied => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (copied.Exception?.GetBaseException() is { } failure)
            {
                BusyBadge.Visibility = Visibility.Collapsed;
                Properties.Told(Strings.T("S_FrameDetachFailed", failure.Message));
                ShowFrameMode();
                done.SetResult(false);
                return;
            }

            Open(copied.Result);
            done.SetResult(true);
        })), TaskScheduler.Default);

        return done.Task;
    }
}
