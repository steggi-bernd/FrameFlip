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

    private List<Rendering.BlenderInstall> _blenders = new();

    /// <summary>Waehrend die Liste gefuellt wird, ist jede Auswahl nur ein Nebeneffekt.</summary>
    private bool _filling;

    public PairingWindow(AppSettings settings, Func<AppSettings, string?> apply, Func<RelayState?> state)
    {
        _settings = settings;
        _apply = apply;
        _state = state;

        InitializeComponent();

        PairingStore.TryUnprotect(settings.PairingSecret, out _key);

        LibraryBox.IsChecked = settings.LibraryAccessEnabled;
        RenderBox.IsChecked = settings.HeadlessRenderEnabled;
        PushBox.IsChecked = settings.FilePushEnabled;

        ShowFolder();

        LoadBlenders();

        Refresh();
    }

    private void Refresh()
    {
        StateLine.Text = _state() switch
        {
            RelayState.Paired => Localization.Strings.T("S_PhoneConnected"),
            RelayState.Waiting => Localization.Strings.T("S_PairedNoPhone"),
            RelayState.Connecting => Localization.Strings.T("S_Connecting"),
            RelayState.Off => Localization.Strings.T("S_RemoteOffState"),
            _ => Localization.Strings.T("S_NoDevice"),
        };

        _key ??= PairingKey.Create();

        string host = PairingInvite.IsUsableHost(_settings.RelayHost)
            ? _settings.RelayHost
            : AppSettings.DefaultRelayHost;

        var invite = new PairingInvite(_key, host);

        Code.Text = invite.Text;
        LinkBox.Text = invite.Text;
        RoomLine.Text = Localization.Strings.T("S_RoomLabel", invite.Key.RoomId);

        Hint.Text = Localization.Strings.T("S_ScanHint");

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
            CopyButton.Content = Localization.Strings.T("S_Copied");
        }
        catch (Exception)
        {
            // Die Zwischenablage kann von einem anderen Programm belegt sein.
            CopyButton.Content = Localization.Strings.T("S_CopyFailed");
        }
    }

    /// <summary>Ein neuer Schluessel trennt jedes bisher gekoppelte Geraet.</summary>
    /// <summary>
    /// Den Dateizugriff umlegen - sofort, ohne Knopf zum Sichern.
    ///
    /// Ein Schalter, der erst nach einem zweiten Klick gilt, ist an dieser Stelle
    /// die schlechtere Antwort: Das Fenster hat sonst nichts zu sichern, und wer
    /// hier etwas umlegt, will es jetzt.
    /// </summary>
    private void OnLibraryChanged(object sender, RoutedEventArgs e)
    {
        bool wanted = LibraryBox.IsChecked == true;

        if (wanted == _settings.LibraryAccessEnabled) return;

        var next = _settings.Clone();
        next.LibraryAccessEnabled = wanted;

        if (_apply(next) is not null)
        {
            // Abgelehnt - dann darf der Haken auch nicht so tun, als waere es
            // durchgegangen.
            LibraryBox.IsChecked = _settings.LibraryAccessEnabled;
            return;
        }

        _settings.LibraryAccessEnabled = wanted;
    }

    // ---------------------------------------------------------------- Rendern

    /// <summary>Die gefundenen Fassungen in die Auswahl - im Hintergrund gesucht.</summary>
    private void LoadBlenders()
    {
        var dispatcher = Dispatcher;
        var settings = _settings;

        Task.Run(() =>
        {
            // Die Suche fasst Registrierung und mehrere Ordner an. Das dauert selten
            // lange, aber "selten" ist kein Grund, ein Fenster warten zu lassen.
            var found = Rendering.BlenderFinder.Find(settings.ExtraBlenders);

            dispatcher.InvokeAsync(() => ShowBlenders(found));
        });
    }

    private void ShowBlenders(List<Rendering.BlenderInstall> found)
    {
        _blenders = found;
        _filling = true;

        BlenderBox.Items.Clear();

        foreach (var install in found) BlenderBox.Items.Add(install.Label);

        if (found.Count == 0)
        {
            BlenderBox.Items.Add(Localization.Strings.T("S_NoBlender"));
            BlenderBox.SelectedIndex = 0;
            BlenderBox.IsEnabled = false;
            _filling = false;
            return;
        }

        BlenderBox.IsEnabled = true;

        // Was eingestellt ist, sonst der Vorschlag: Steam, wenn es da ist, sonst die
        // neueste. Damit steht nach dem ersten Oeffnen etwas Brauchbares da, ohne
        // dass jemand etwas auswaehlen muesste.
        int index = found.FindIndex(i => string.Equals(i.Path, _settings.BlenderPath,
                                                       StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            var preferred = Rendering.BlenderFinder.Preferred(found);
            index = preferred is null ? 0 : found.IndexOf(preferred);
        }

        BlenderBox.SelectedIndex = Math.Max(0, index);
        _filling = false;

        Choose(found[BlenderBox.SelectedIndex].Path);
    }

    private void OnBlenderChosen(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_filling) return;

        int index = BlenderBox.SelectedIndex;

        if (index < 0 || index >= _blenders.Count) return;

        Choose(_blenders[index].Path);
    }

    /// <summary>Eine Fassung uebernehmen - sofort, wie alles in diesem Fenster.</summary>
    private void Choose(string path)
    {
        if (string.Equals(_settings.BlenderPath, path, StringComparison.OrdinalIgnoreCase)) return;

        var next = _settings.Clone();
        next.BlenderPath = path;

        if (_apply(next) is not null) return;

        _settings.BlenderPath = path;
    }

    /// <summary>
    /// Eine Fassung von Hand hinzufuegen.
    ///
    /// Fuer alles, was an keiner ueblichen Stelle liegt: ein entpacktes Blender auf
    /// der Datenplatte, ein selbst gebautes. Ohne diesen Weg waere die Auswahl eine
    /// Sackgasse.
    /// </summary>
    private void OnAddBlender(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.Strings.T("S_PickBlender"),
            Filter = Localization.Strings.T("S_Programs") + " (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        var next = _settings.Clone();

        next.ExtraBlenders = new List<string>(_settings.ExtraBlenders);

        if (!next.ExtraBlenders.Any(p => string.Equals(p, dialog.FileName, StringComparison.OrdinalIgnoreCase)))
            next.ExtraBlenders.Add(dialog.FileName);

        next.BlenderPath = dialog.FileName;

        if (_apply(next) is not null) return;

        _settings.ExtraBlenders = next.ExtraBlenders;
        _settings.BlenderPath = next.BlenderPath;

        LoadBlenders();
    }

    private void OnRenderChanged(object sender, RoutedEventArgs e)
    {
        bool wanted = RenderBox.IsChecked == true;

        if (wanted == _settings.HeadlessRenderEnabled) return;

        var next = _settings.Clone();
        next.HeadlessRenderEnabled = wanted;

        // Normalize schaltet ihn wieder aus, wenn kein Blender eingetragen ist -
        // dann darf der Haken auch nicht so tun, als waere es durchgegangen.
        if (_apply(next) is not null || (wanted && _settings.BlenderPath.Length == 0))
        {
            RenderBox.IsChecked = _settings.HeadlessRenderEnabled;
            return;
        }

        _settings.HeadlessRenderEnabled = wanted;
    }

    // ---------------------------------------------------------------- Annehmen

    private void ShowFolder()
        => FolderLine.Text = _settings.FileFolder.Length > 0
            ? _settings.FileFolder
            : Localization.Strings.T("S_NoFolderSet");

    /// <summary>
    /// Ein Schalter fuer beides - Lesen und Ablegen.
    ///
    /// Innen sind es zwei Erlaubnisse, weil sie verschieden weit reichen. Nach
    /// aussen waere die Unterscheidung eine Frage, die niemand stellen wollte: Wer
    /// dem Handy Dateien schicken laesst, will sie dort auch sehen.
    /// </summary>
    private void OnPushChanged(object sender, RoutedEventArgs e)
    {
        bool wanted = PushBox.IsChecked == true;

        if (wanted == _settings.FilePushEnabled) return;

        var next = _settings.Clone();

        next.FileAccessEnabled = wanted;
        next.FilePushEnabled = wanted;

        // Ohne Ordner schaltet Normalize es wieder aus - dann darf der Haken nicht
        // so tun, als waere es durchgegangen.
        if (_apply(next) is not null || (wanted && _settings.FileFolder.Length == 0))
        {
            PushBox.IsChecked = _settings.FilePushEnabled;
            return;
        }

        _settings.FileAccessEnabled = wanted;
        _settings.FilePushEnabled = wanted;
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Localization.Strings.T("S_PickExchange"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var next = _settings.Clone();
        next.FileFolder = dialog.SelectedPath;

        if (_apply(next) is not null) return;

        _settings.FileFolder = next.FileFolder;

        ShowFolder();

        // Mit einem Ordner darf der Haken jetzt halten - vorher wurde er beim
        // Speichern wieder ausgeschaltet.
        if (PushBox.IsChecked == true) OnPushChanged(sender, e);
    }

    private void OnNewKey(object sender, RoutedEventArgs e)
    {
        _key = PairingKey.Create();
        Refresh();
    }
}
