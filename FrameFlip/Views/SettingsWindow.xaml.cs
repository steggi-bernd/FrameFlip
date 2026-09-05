using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FrameFlip.Configuration;
using FrameFlip.Localization;
using FrameFlip.Interop;
using FrameFlip.Playback;

namespace FrameFlip.Views;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, string?> _apply;

    /// <summary>Ausgangszustand. Felder ohne Dialogfeld werden daraus uebernommen.</summary>
    private readonly AppSettings _current;

    private HotKeyDefinition _hotkey;

    /// <param name="apply">Uebernimmt die Einstellungen. Rueckgabe: Fehlertext oder null.</param>
    public SettingsWindow(AppSettings current, Func<AppSettings, string?> apply)
    {
        _apply = apply;
        _current = current;

        InitializeComponent();

        _hotkey = HotKeyDefinition.TryParse(current.Hotkey, out var parsed) ? parsed : HotKeyDefinition.Default;
        HotkeyBox.Text = _hotkey.ToString();
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) => ShowStatus("Kombination druecken …");

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
        ThreadsHint.Text = $"auf diesem Rechner hoechstens {FrameFlip.Diagnostics.SystemLoadMonitor.ThreadCeiling} von {Environment.ProcessorCount} Kernen";
    }

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
            ShowStatus("Mindestens ein Modifikator (Strg, Alt, Shift, Win) ist noetig.");
            return;
        }

        _hotkey = new HotKeyDefinition(modifiers, key);
        HotkeyBox.Text = _hotkey.ToString();
        ShowStatus(null);
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadInt(BudgetBox.Text, 64, 8192, out int budget))
        {
            ShowStatus("RAM-Budget muss zwischen 64 und 8192 MB liegen.");
            return;
        }

        if (!TryReadInt(AheadBox.Text, 1, 2000, out int ahead))
        {
            ShowStatus("Puffer voraus muss zwischen 1 und 2000 Frames liegen.");
            return;
        }

        if (!TryReadInt(BehindBox.Text, 0, 2000, out int behind))
        {
            ShowStatus("Puffer zurueck muss zwischen 0 und 2000 Frames liegen.");
            return;
        }

        if (!TryReadInt(IntervalBox.Text, 2, 300, out int interval))
        {
            ShowStatus("Der Messtakt muss zwischen 2 und 300 Sekunden liegen.");
            return;
        }

        if (!TryReadInt(ThreadsBox.Text, 1, 16, out int threads))
        {
            ShowStatus("Decoder-Threads muessen zwischen 1 und 16 liegen.");
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
            ShowStatus("Der Ordner konnte nicht geoeffnet werden.");
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
