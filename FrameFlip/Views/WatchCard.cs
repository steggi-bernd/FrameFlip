using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Remote;

namespace FrameFlip.Views;

/// <summary>
/// Die Karte der Zuschauerseite: Schalter, Code, Adresse, Stand, Kennwort und Handgriffe.
///
/// Die Oberflaeche liefert die Teile (<see cref="Parts"/>), der Wirt Einstellungen, Dienst
/// und die Wege nach draussen (<see cref="Host"/>). So zeigt die Kopplungstafel des
/// Dashboards dieselbe Karte mit derselben Logik wie jede andere Stelle, die sie braucht.
/// </summary>
internal sealed class WatchCard
{
    /// <summary>Die Teile auf dem Schirm.</summary>
    internal sealed record Parts(
        ToggleButton Toggle,
        TextBlock ToggleText,
        FrameworkElement CodeFrame,
        QrCodeView Code,
        TextBlock Address,
        TextBlock Hint,
        FrameworkElement PassRow,
        TextBox Pass,
        TextBlock PassHint,
        Panel Actions);

    /// <summary>Was der Wirt beitraegt.</summary>
    /// <param name="Settings">Der Bestand - das Objekt, das der Wirt haelt.</param>
    /// <param name="Apply">Uebernimmt Einstellungen. Rueckgabe: Fehlertext oder null.</param>
    /// <param name="Watch">Der laufende Dienst - oder null, wenn die Seite nicht laeuft.</param>
    /// <param name="Ready">Ob Aenderungen am Schalter schon Auftraege sind - vorher fuellt nur der Aufbau die Felder.</param>
    /// <param name="AskTerms">Holt die Zustimmung ein und fuehrt dann das Uebergebene aus.</param>
    /// <param name="Note">Eine Zeile ins Protokoll.</param>
    /// <param name="ShowError">Ein Fehler beim Uebernehmen - dort, wo die Karte Hinweise zeigt.</param>
    /// <param name="MakeAction">Baut einen Handgriff: Textschluessel, ob er der wichtigste ist, und was er tut.</param>
    /// <param name="Copy">Legt einen Link in die Zwischenablage.</param>
    internal sealed record Host(
        Func<AppSettings> Settings,
        Func<AppSettings, string?> Apply,
        Func<Web.WatchService?> Watch,
        Action? Renew,
        Action<string?>? SetCode,
        Func<bool> LightQr,
        Func<bool> Ready,
        Action<Action> AskTerms,
        Action<string> Note,
        Action<string> ShowError,
        Func<string, bool, Action, Button> MakeAction,
        Action<string> Copy);

    private readonly Parts _parts;
    private readonly Host _host;

    /// <summary>Woran die Anzeige haengt - abgemeldet, sobald der Dienst wechselt.</summary>
    private Web.WatchService? _watched;

    public WatchCard(Parts parts, Host host)
    {
        _parts = parts;
        _host = host;
    }

    private Dispatcher Dispatcher => _parts.Toggle.Dispatcher;

    /// <summary>
    /// Der Schalter fuer die Seite im eigenen Netz.
    ///
    /// Einschalten oeffnet einen Port und legt ein neues Zeichen fuer die Adresse an;
    /// Ausschalten macht beides wieder zu. Dass die alte Adresse danach nicht mehr
    /// gilt, ist kein Nebeneffekt, sondern der Zweck.
    /// </summary>
    public void Toggled()
    {
        var toggle = _parts.Toggle;

        if (!_host.Ready()) return;
        // Refresh bildet nur den Bestand ab; das ist kein neuer Auftrag.
        if ((toggle.IsChecked == true) == _host.Settings().WatchEnabled) return;

        // Einschalten heisst: eine Verbindung nach draussen. Vorher wird gefragt.
        // Beim Ausschalten nicht - wer zumacht, braucht keine Zustimmung.
        if (toggle.IsChecked == true && !_host.Settings().TermsOk)
        {
            toggle.IsChecked = false;
            _host.AskTerms(() => { toggle.IsChecked = true; });
            return;
        }

        // Eine KOPIE aendern, nicht den Bestand: Settings() liefert dasselbe Objekt, das
        // der Wirt haelt. Wer es an Ort und Stelle umschreibt, nimmt ihm die Moeglichkeit,
        // die Aenderung zu bemerken - er vergleicht dann den neuen Stand mit sich selbst.
        var settings = _host.Settings().Clone();

        settings.WatchEnabled = toggle.IsChecked == true;

        if (_host.Apply(settings) is { } error)
        {
            _host.ShowError(error);
            return;
        }

        _host.Note(Strings.T(settings.WatchEnabled ? "D_LogWatchOn" : "D_LogWatchOff"));

        // Erst nachdem der Host den Server auf- oder abgebaut hat, steht die Adresse
        // fest. Deshalb eine Runde spaeter nachsehen.
        Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
    }

    /// <summary>Bildet den Bestand ab: an oder aus, Code, Adresse, Stand, Kennwort, Handgriffe.</summary>
    public void Refresh()
    {
        var p = _parts;

        var settings = _host.Settings();
        bool on = settings.WatchEnabled;

        p.Toggle.IsChecked = on;
        Track.SetAmount(p.ToggleText, 1);
        Track.SetText(p.ToggleText, Strings.T(on ? "D_On" : "D_Off"));

        p.Actions.Children.Clear();

        var watch = _host.Watch();
        Follow(watch);

        if (!on || watch is null)
        {
            p.CodeFrame.Visibility = Visibility.Collapsed;
            p.PassRow.Visibility = Visibility.Collapsed;
            p.Code.Text = null;
            p.Address.Text = string.Empty;

            // Zwei verschiedene Faelle mit zwei verschiedenen Saetzen: schlicht aus -
            // oder an, aber ohne Relay-Adresse, mit der sich etwas anfangen liesse.
            p.Hint.Text = Strings.T(on ? "D_WatchNoRelay" : "D_WatchOff");

            return;
        }

        string link = watch.Link;

        p.CodeFrame.Visibility = Visibility.Visible;
        p.Code.LightModules = _host.LightQr();
        p.Code.Text = link;
        p.Address.Text = link;

        p.Hint.Text = Standing(watch);

        p.PassRow.Visibility = Visibility.Visible;
        p.PassHint.Text = Strings.T("D_WatchPassHint");

        // Nur nachtragen, wenn gerade niemand darin schreibt - sonst spraenge der
        // Text unter den Fingern zurueck, sobald ein Zuschauer kommt oder geht.
        if (!p.Pass.IsKeyboardFocusWithin) p.Pass.Text = CurrentCode() ?? string.Empty;

        p.Actions.Children.Add(_host.MakeAction("S_CopyLink", false, () => _host.Copy(link)));
        p.Actions.Children.Add(_host.MakeAction("D_WatchRenew", false, Renew));
    }

    /// <summary>
    /// Wie es gerade um die Zuschauer steht - in Worten, nicht in Zahlenkolonnen.
    ///
    /// Dass hier ueberhaupt etwas Verlaessliches stehen kann, liegt am Entwurf: Es
    /// wird nur gesendet, solange jemand zusieht. Zuschauer und Datenverkehr sind
    /// dasselbe, und die Zeile ist deshalb keine Vermutung.
    /// </summary>
    internal static string Standing(Web.WatchService watch)
    {
        int watchers = watch.Watchers;

        string who = watchers switch
        {
            0 => Strings.T("D_WatchNobody"),
            1 => Strings.T("D_WatchOneViewer"),
            _ => Strings.T("D_WatchViewers", watchers)
        };

        // MaxSeats, nicht OpenSeats: Offen ist immer nur einer mehr als besetzt, weil
        // Raeume auf dem Leuchtturm knapp sind. Dem Benutzer davon zu erzaehlen waere
        // verwirrend - fuer ihn zaehlt, wieviele ueberhaupt zusehen koennen.
        string seats = Strings.T("D_WatchSeats", Math.Max(0, watch.MaxSeats - watchers), watch.MaxSeats);

        string standing = Strings.T("D_WatchOn") + Environment.NewLine + Environment.NewLine + who + " " + seats;

        if (watch.LockedUntilUtc is { } until && until > DateTime.UtcNow)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes));
            standing += Environment.NewLine + Environment.NewLine + Strings.T("D_WatchLocked", minutes);
        }

        return standing;
    }

    /// <summary>
    /// Meldet sich beim laufenden Dienst an, damit die Anzeige mitbekommt, wenn
    /// jemand kommt oder geht - und beim alten ab, damit ein ausgetauschter Dienst
    /// nicht in ein Fenster meldet, das ihn gar nicht mehr zeigt.
    /// </summary>
    private void Follow(Web.WatchService? watch)
    {
        if (ReferenceEquals(_watched, watch)) return;

        if (_watched is not null) _watched.Changed -= OnChanged;

        _watched = watch;

        if (_watched is not null) _watched.Changed += OnChanged;
    }

    private void OnChanged()
        => Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);

    private string? CurrentCode()
        => WatchStore.TryUnprotect(_host.Settings().WatchSecret, out var key) ? key?.Code : null;

    private void Renew()
    {
        _host.Renew?.Invoke();
        _host.Note(Strings.T("D_WatchRenewed"));

        Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
    }

    /// <summary>Enter im Kennwortfeld.</summary>
    public void PassKey(KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        ApplyPass();

        // Den Tastaturschein abgeben, sonst bliebe der Text stehen und es sieht aus,
        // als waere nichts geschehen.
        Keyboard.ClearFocus();
    }

    /// <summary>Das Kennwortfeld wurde verlassen.</summary>
    public void PassDone() => ApplyPass();

    /// <summary>
    /// Uebernimmt das Kennwort - aber nur, wenn es sich wirklich geaendert hat.
    ///
    /// Jedes Verlassen des Feldes als Aenderung zu werten, hiesse die Verbindungen
    /// jedesmal neu aufzubauen, auch wenn niemand etwas getippt hat. Zuschauer flogen
    /// dann heraus, weil jemand durchs Fenster geklickt hat.
    /// </summary>
    private void ApplyPass()
    {
        if (_host.SetCode is null) return;

        var pass = _parts.Pass;

        string typed = pass.Text?.Trim() ?? string.Empty;
        string? current = CurrentCode();

        if (string.Equals(typed, current ?? string.Empty, StringComparison.Ordinal)) return;

        if (typed.Length > 0 && typed.Length < WatchKey.MinCodeLength)
        {
            _host.Note(Strings.T("D_WatchPassShort"));
            pass.Text = current ?? string.Empty;
            return;
        }

        _host.SetCode(typed.Length == 0 ? null : typed);

        _host.Note(Strings.T(typed.Length == 0 ? "D_WatchPassCleared" : "D_WatchPassSet"));

        Dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
    }
}
