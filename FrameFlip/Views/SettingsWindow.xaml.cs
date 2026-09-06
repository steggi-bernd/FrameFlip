using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Interop;
using FrameFlip.Playback;
using FrameFlip.Remote;

namespace FrameFlip.Views;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, string?> _apply;

    /// <summary>Ausgangszustand. Felder ohne Dialogfeld werden daraus uebernommen.</summary>
    private readonly AppSettings _current;

    private HotKeyDefinition _hotkey;

    /// <summary>
    /// Der Kopplungsschluessel, solange der Dialog offen ist.
    ///
    /// Er entsteht erst, wenn eine brauchbare Relais-Adresse dasteht - ohne Relais
    /// gaebe es nichts zu koppeln, und ein Geheimnis auf Vorrat zu erzeugen und in
    /// die Konfiguration zu schreiben waere Unfug. Gespeichert wird er beim Sichern,
    /// nicht beim Erzeugen: Wer den Dialog abbricht, hat nichts angefasst.
    /// </summary>
    private PairingKey? _pairing;

    /// <param name="apply">Uebernimmt die Einstellungen. Rueckgabe: Fehlertext oder null.</param>
    public SettingsWindow(AppSettings current, Func<AppSettings, string?> apply)
    {
        _apply = apply;
        _current = current;

        InitializeComponent();

        _hotkey = HotKeyDefinition.TryParse(current.Hotkey, out var parsed) ? parsed : HotKeyDefinition.Default;
        HotkeyBox.Text = _hotkey.ToString();
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) => ShowStatus(Strings.T("S_HotkeyPrompt"));

        // Die Sprachen stehen in ihrer EIGENEN Sprache da - "Deutsch" und "English",
        // nicht uebersetzt. Wer die Oberflaeche gerade nicht lesen kann, findet den
        // eigenen Sprachnamen trotzdem.
        LanguageBox.ItemsSource = LanguageOption.All;
        LanguageBox.SelectedItem = LanguageOption.For(Strings.Parse(current.Language));

        FpsBox.ItemsSource = FpsOption.All;
        FpsBox.SelectedItem = FpsOption.Closest(current.Fps);

        LoopBox.IsChecked = current.Loop;
        LockToDisplayBox.IsChecked = current.LockToDisplay;
        CloseOnFocusLossBox.IsChecked = current.CloseOnFocusLoss;
        BudgetBox.Text = current.MemoryBudgetMb.ToString(CultureInfo.InvariantCulture);
        AheadBox.Text = current.PrefetchAhead.ToString(CultureInfo.InvariantCulture);
        BehindBox.Text = current.PrefetchBehind.ToString(CultureInfo.InvariantCulture);

        AdaptiveBox.IsChecked = current.AdaptiveResources;
        IntervalBox.Text = current.LoadIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        ThreadsBox.Text = current.MaxDecoderThreads.ToString(CultureInfo.InvariantCulture);

        UpdateThreadHint();
        Strings.Changed += UpdateThreadHint;
        Closed += (_, _) => Strings.Changed -= UpdateThreadHint;

        PairingStore.TryUnprotect(current.PairingSecret, out _pairing);

        RelayBox.Text = current.RelayHost;
        RemoteBox.IsChecked = current.RemoteEnabled;

        UpdatePairing();
    }

    /// <summary>
    /// Der Hinweis unter den Decoder-Threads traegt Zahlen und kann deshalb nicht
    /// als fertiger Text im Woerterbuch stehen. Beim Sprachwechsel im laufenden
    /// Dialog muss er neu gesetzt werden - DynamicResource kann das hier nicht.
    /// </summary>
    private void UpdateThreadHint()
        => ThreadsHint.Text = Strings.T("S_ThreadCeiling",
                                       FrameFlip.Diagnostics.SystemLoadMonitor.ThreadCeiling,
                                       Environment.ProcessorCount);

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Bei Alt-Kombinationen liefert WPF die eigentliche Taste in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            HotkeyBox.Text = _hotkey.ToString();
            ShowStatus(null);
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            ShowStatus(Strings.T("S_HotkeyNeedsModifier"));
            return;
        }

        _hotkey = new HotKeyDefinition(modifiers, key);
        HotkeyBox.Text = _hotkey.ToString();
        ShowStatus(null);
    }

    private void OnRelayChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;

        UpdatePairing();
    }

    /// <summary>
    /// Zeigt den QR-Code, sobald eine brauchbare Adresse dasteht - und nur dann.
    ///
    /// Ein Code, der beim Tippen mitwaechst, waere unruhig und im Zweifel falsch
    /// abfotografiert. Deshalb erscheint er erst, wenn die Adresse als Ganzes
    /// aufgeht.
    /// </summary>
    private void UpdatePairing()
    {
        string host = RelayBox.Text.Trim();

        if (host.Length == 0 || !TryInvite(host, out PairingInvite? invite))
        {
            PairingCode.Text = null;
            RoomText.Text = string.Empty;
            CopyLinkButton.IsEnabled = false;
            NewKeyButton.IsEnabled = false;
            PairingHint.Text = Strings.T("S_NoRelayYet");
            return;
        }

        PairingCode.Text = invite!.Text;
        RoomText.Text = Strings.T("S_RoomLabel", invite.Key.RoomId);
        CopyLinkButton.IsEnabled = true;
        NewKeyButton.IsEnabled = true;
        PairingHint.Text = Strings.T("S_ScanHint");
    }

    private bool TryInvite(string host, out PairingInvite? invite)
    {
        invite = null;

        // Erst hier entsteht ein Schluessel, und nur einmal je Dialog.
        _pairing ??= PairingKey.Create();

        try
        {
            invite = new PairingInvite(_pairing, host);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void OnCopyLinkClicked(object sender, RoutedEventArgs e)
    {
        if (PairingCode.Text is not { Length: > 0 } link) return;

        try
        {
            Clipboard.SetText(link);
            ShowStatus(Strings.T("S_Copied"));
        }
        catch (Exception)
        {
            // Die Zwischenablage kann von einem anderen Programm belegt sein.
            ShowStatus(Strings.T("S_NoClipboard"));
        }
    }

    private void OnNewKeyClicked(object sender, RoutedEventArgs e)
    {
        _pairing = PairingKey.Create();
        UpdatePairing();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadInt(BudgetBox.Text, 64, 8192, out int budget))
        {
            ShowStatus(Strings.T("S_RangeMemory", 64, 8192));
            return;
        }

        if (!TryReadInt(AheadBox.Text, 1, 2000, out int ahead))
        {
            ShowStatus(Strings.T("S_RangeAhead", 1, 2000));
            return;
        }

        if (!TryReadInt(BehindBox.Text, 0, 2000, out int behind))
        {
            ShowStatus(Strings.T("S_RangeBehind", 0, 2000));
            return;
        }

        if (!TryReadInt(IntervalBox.Text, 2, 300, out int interval))
        {
            ShowStatus(Strings.T("S_RangeInterval", 2, 300));
            return;
        }

        if (!TryReadInt(ThreadsBox.Text, 1, 16, out int threads))
        {
            ShowStatus(Strings.T("S_RangeThreads", 1, 16));
            return;
        }

        string relay = RelayBox.Text.Trim();

        if (relay.Length > 0 && !TryInvite(relay, out _))
        {
            ShowStatus(Strings.T("S_RelayHostBad"));
            return;
        }

        // Vom Bestand ausgehen und nur die Dialogfelder ueberschreiben. Ein frisch
        // erzeugtes Objekt wuerde jede Einstellung, die dieser Dialog nicht kennt,
        // stillschweigend auf den Standard zuruecksetzen - etwa den Zustand der
        // Bilddatenanzeige, die im Vorschaufenster umgeschaltet wird.
        var settings = _current.Clone();

        settings.Hotkey = _hotkey.ToString();

        settings.Language = LanguageBox.SelectedItem is LanguageOption language
            ? Strings.ToCode(language.Value)
            : Strings.ToCode(Strings.Current);

        settings.Fps = FpsBox.SelectedItem is FpsOption option ? option.Value : 24.0;
        settings.Loop = LoopBox.IsChecked == true;
        settings.LockToDisplay = LockToDisplayBox.IsChecked == true;
        settings.CloseOnFocusLoss = CloseOnFocusLossBox.IsChecked == true;
        settings.MemoryBudgetMb = budget;
        settings.PrefetchAhead = ahead;
        settings.PrefetchBehind = behind;
        settings.AdaptiveResources = AdaptiveBox.IsChecked == true;
        settings.LoadIntervalSeconds = interval;
        settings.MaxDecoderThreads = threads;

        settings.RelayHost = relay;
        settings.RemoteEnabled = RemoteBox.IsChecked == true;

        // Ohne Relais wird der Schluessel nicht abgelegt. Er waere ein Geheimnis auf
        // der Platte, das nichts sichert.
        settings.PairingSecret = relay.Length > 0 && _pairing is not null
            ? PairingStore.Protect(_pairing)
            : string.Empty;

        string? error = _apply(settings);
        if (error is not null)
        {
            ShowStatus(error);
            return;
        }

        Close();
    }

    /// <summary>
    /// Wechselt die Sprache sofort, nicht erst beim Speichern.
    ///
    /// Eine Sprachauswahl, deren Wirkung man erst nach dem Uebernehmen sieht, ist
    /// eine Zumutung: Man waehlt blind und muss zurueck, wenn es die falsche war.
    /// Verworfen wird sie beim Abbrechen wieder.
    /// </summary>
    private void OnLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (LanguageBox.SelectedItem is not LanguageOption option) return;

        Strings.Apply(option.Value);

        // Die zusammengesetzten Texte haengen nicht an DynamicResource.
        UpdatePairing();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        // Die Sprache wurde beim Auswaehlen sofort umgestellt - Abbrechen muss sie
        // also auch zuruecknehmen, sonst bliebe von einem verworfenen Dialog etwas
        // haengen.
        Strings.Apply(Strings.Parse(_current.Language));
        Close();
    }

    private void OnRevealClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);

            var arguments = File.Exists(SettingsStore.FilePath)
                ? $"/select,\"{SettingsStore.FilePath}\""
                : $"\"{SettingsStore.DirectoryPath}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ShowStatus(Strings.T("S_FolderFailed"));
        }
    }

    private static bool TryReadInt(string text, int min, int max, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
           && value >= min && value <= max;

    private void ShowStatus(string? message)
    {
        StatusText.Text = message ?? string.Empty;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
