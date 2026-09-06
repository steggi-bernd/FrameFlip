using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Remote;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;

namespace FrameFlip.Views;

/// <summary>
/// Der Kopplungscode als eigenes Fenster.
///
/// Er stand bisher nur im Einstellungsdialog, hinter einem Reiter. Das ist der
/// falsche Ort fuer das, was man am haeufigsten braucht: Wer ein zweites Geraet
/// koppeln will, sucht nicht in den Einstellungen, sondern klickt auf die Stelle,
/// an der steht, ob eines verbunden ist.
///
/// Wie im Dialog gilt: Ein gezeigter Code ist sofort gueltig. Ein Code, den jemand
/// schon abfotografiert haben kann, darf nicht vorlaeufig sein.
/// </summary>
public partial class PairingWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<AppSettings, string?> _apply;
    private readonly Func<RelayState?> _state;

    private PairingKey? _key;

    public PairingWindow(AppSettings settings, Func<AppSettings, string?> apply, Func<RelayState?> state)
    {
        _settings = settings;
        _apply = apply;
        _state = state;

        InitializeComponent();

        PairingStore.TryUnprotect(settings.PairingSecret, out _key);

        Refresh();
    }

    private void Refresh()
    {
        StateLine.Text = _state() switch
        {
            RelayState.Paired => "HANDY VERBUNDEN",
            RelayState.Waiting => "GEKOPPELT · HANDY NICHT DA",
            RelayState.Connecting => "VERBINDET …",
            RelayState.Off => "FERNSTEUERUNG AUS",
            _ => "NOCH KEIN GERÄT",
        };

        _key ??= PairingKey.Create();

        string host = PairingInvite.IsUsableHost(_settings.RelayHost)
            ? _settings.RelayHost
            : AppSettings.DefaultRelayHost;

        var invite = new PairingInvite(_key, host);

        Code.Text = invite.Text;
        LinkBox.Text = invite.Text;
        RoomLine.Text = "Raum " + invite.Key.RoomId;

        Hint.Text = "Mit der FrameFlip-App abfotografieren. Der Schlüssel verlässt den "
                    + "Rechner nur über den Bildschirm, nie über das Netz.";

        Commit(invite);
    }

    /// <summary>Wie im Einstellungsdialog: Zeigen heisst ablegen.</summary>
    private void Commit(PairingInvite invite)
    {
        string secret = PairingStore.Protect(invite.Key);
        if (secret.Length == 0) return;

        var next = _settings.Clone();

        next.RelayHost = invite.Relay;
        next.PairingSecret = secret;
        next.RemoteEnabled = true;

        if (_apply(next) is not null) return;

        _settings.RelayHost = next.RelayHost;
        _settings.PairingSecret = next.PairingSecret;
        _settings.RemoteEnabled = true;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LinkBox.Text);
            CopyButton.Content = "Kopiert";
        }
        catch (Exception)
        {
            // Die Zwischenablage kann von einem anderen Programm belegt sein.
            CopyButton.Content = "Ging nicht";
        }
    }

    /// <summary>Ein neuer Schluessel trennt jedes bisher gekoppelte Geraet.</summary>
    private void OnNewKey(object sender, RoutedEventArgs e)
    {
        _key = PairingKey.Create();
        Refresh();
    }
}
