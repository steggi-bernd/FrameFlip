using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FrameFlip.Configuration;
using FrameFlip.Export;
using FrameFlip.Localization;
using FrameFlip.Remote;
using FrameFlip.Setup;

namespace FrameFlip.Views;

/// <summary>
/// Die sichtbare Seite der Bereitschaftsanzeige.
///
/// Entschieden wird nichts hier - das tut <see cref="Readiness"/>, ohne WPF und
/// darum vollstaendig pruefbar. Diese Datei sammelt nur die Werte ein und zeichnet
/// die Zeilen.
/// </summary>
public partial class MainWindow
{
    private bool _installing;

    /// <summary>
    /// Ob ffmpeg gefunden wurde - gemerkt, nicht jede Sekunde neu gesucht.
    ///
    /// <see cref="FfmpegLocator.Locate"/> geht den PATH und die Ablageorte der
    /// Paketverwaltungen durch, also Dutzende Dateisystemzugriffe. Im Sekundentakt
    /// waere das eine Dauerlast fuer eine Antwort, die sich fast nie aendert.
    ///
    /// Verworfen wird der Wert genau dort, wo er sich aendern KANN: nach einer
    /// Installation und beim Uebernehmen von Einstellungen.
    /// </summary>
    private bool? _ffmpegFound;

    /// <summary>Woran sich erkennen laesst, ob sich die Anzeige ueberhaupt geaendert hat.</summary>
    private string _readySignature = "?";

    /// <summary>Die gemerkte Antwort vergessen - die naechste Runde sucht neu.</summary>
    private void ForgetFfmpeg() => _ffmpegFound = null;

    /// <summary>Die Werte, aus denen sich die Anzeige ergibt - einmal eingesammelt.</summary>
    private ReadinessInput ReadNow()
    {
        var settings = _getSettings();

        /* Der gespeicherte Zeitpunkt ODER der laufende Kontakt.
         *
         * Beides zusammen, weil beides einzeln luegt: Der gespeicherte Wert kennt den
         * Handschlag von vor einer Minute noch nicht, und der laufende Kontakt weiss
         * nichts von der Einrichtung vor drei Wochen. */
        bool seen = settings.BridgeLastSeen is not null || _monitor?.LastBridgeContact is not null;

        var relay = _remoteState();

        return new ReadinessInput(
            FfmpegFound: _ffmpegFound ??= FfmpegLocator.Locate(settings.FfmpegPath) is not null,
            WingetAvailable: FfmpegLocator.WingetAvailable,
            BridgeEnabled: settings.BridgeEnabled,
            BridgeListening: _monitor?.IsListening ?? false,
            BridgeSeenEver: seen,
            RelayWanted: settings.RemoteEnabled || settings.WatchEnabled,

            /* Warten zaehlt als verbunden.
             *
             * "Waiting" heisst: im Raum, die Gegenstelle noch nicht da. Das ist der
             * Normalzustand, solange das Handy in der Tasche steckt - und kein Fehler,
             * den man melden muesste. Zu melden ist nur, wenn gar nichts steht. */
            RelayConnected: relay is RelayState.Waiting or RelayState.Paired);
    }

    /// <summary>
    /// Die Leiste neu zeichnen. Ist nichts zu melden, verschwindet sie.
    ///
    /// Nebenbei wird der Zeitpunkt des Handschlags in die Konfiguration uebernommen.
    /// Das gehoert hierher und nicht in den Bruecken-Code: Dort faellt der Kontakt an,
    /// aber erst hier ist bekannt, dass er ueber den Programmlauf hinaus gelten soll.
    /// </summary>
    private void RefreshReadiness()
    {
        if (ReadyBar is null || ReadyRows is null) return;

        MerkeBrueckenkontakt();

        var rows = Readiness.Check(ReadNow());

        /* Nur zeichnen, wenn sich etwas geaendert hat.
         *
         * Der Takt schlaegt jede Sekunde. Die Zeilen jedes Mal zu verwerfen und neu
         * aufzubauen hiesse, im Sekundentakt Elemente wegzuwerfen - und einen Knopf,
         * den gerade jemand anklickt, unter dem Zeiger auszutauschen. */
        string jetzt = string.Join("|", rows.Select(r => $"{r.Topic}:{r.TextKey}:{r.Action}"));
        if (jetzt == _readySignature) return;

        _readySignature = jetzt;

        ReadyRows.Items.Clear();

        foreach (var row in rows) ReadyRows.Items.Add(Zeile(row));

        ReadyBar.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Den Handschlag festhalten, sobald er das erste Mal auftaucht.</summary>
    private void MerkeBrueckenkontakt()
    {
        if (_monitor?.LastBridgeContact is not { } kontakt) return;

        var settings = _getSettings();

        // Nur beim ersten Mal schreiben. Bei jedem Zeichnen zu speichern hiesse, die
        // Konfiguration im Sekundentakt anzufassen, und der Wert wird ohnehin nur als
        // "war schon mal da" gelesen.
        if (settings.BridgeLastSeen is not null) return;

        var kopie = settings.Clone();
        kopie.BridgeLastSeen = kontakt;
        _apply(kopie);
    }

    /// <summary>Eine Zeile: Text links, die eine Handlung rechts.</summary>
    private UIElement Zeile(ReadyItem row)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            LineHeight = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("DesktopMuted"),
        };
        text.SetResourceReference(TextBlock.TextProperty, row.TextKey);

        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (row.Action != ReadyAction.None)
        {
            var button = new Button
            {
                Style = (Style)FindResource("DashGhostSmall"),
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = Strings.T(row.Action switch
                {
                    ReadyAction.InstallFfmpeg => "S_ReadyInstall",
                    ReadyAction.GetBridge => "S_ReadyGetBridge",
                    _ => "S_ReadyOpenSettings",
                }),
            };

            var was = row.Action;
            button.Click += (_, _) => Tue(was, button);

            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }

        return grid;
    }

    private async void Tue(ReadyAction action, Button button)
    {
        switch (action)
        {
            case ReadyAction.OpenSettings:
                ShowSettingsPage();
                break;

            case ReadyAction.GetBridge:
                Oeffne("https://github.com/steggi-bernd/FrameFlipBridge/releases");
                break;

            case ReadyAction.InstallFfmpeg:
                await HoleFfmpeg(button);
                break;
        }
    }

    /// <summary>
    /// ffmpeg von Windows holen lassen und danach neu urteilen.
    ///
    /// Der Knopf bleibt waehrenddessen gesperrt, und die Fortschrittszeilen von winget
    /// landen in der Statusleiste. Beides aus demselben Grund: Ein Vorgang, der bis zu
    /// einer Minute dauert und nichts von sich zeigt, sieht aus wie ein Absturz.
    /// </summary>
    private async Task HoleFfmpeg(Button button)
    {
        if (_installing) return;

        _installing = true;
        button.IsEnabled = false;
        button.Content = Strings.T("S_ReadyInstalling");

        var melden = new Progress<string>(line => Note(line));

        string? pfad = await FfmpegLocator.InstallAsync(melden);

        _installing = false;
        ForgetFfmpeg();

        Note(Strings.T(pfad is null ? "S_ReadyInstallFailed" : "S_ReadyInstalled"));

        // Neu zeichnen statt den Knopf zurueckzusetzen: Bei Erfolg ist die Zeile weg,
        // bei Fehlschlag steht sie wieder da - beides ergibt sich aus dem Zustand.
        RefreshReadiness();
    }

    /// <summary>
    /// Eine Adresse im Browser oeffnen - nur https, und nur aus dem Programm heraus.
    ///
    /// Dieselbe Schranke wie bei den Rechtstexten: Was hier hineingereicht wird, steht
    /// fest im Quelltext. Ein Pfad oder ein anderes Schema haette in einem
    /// ShellExecute nichts verloren.
    /// </summary>
    private static void Oeffne(string ziel)
    {
        if (!ziel.StartsWith("https://", StringComparison.Ordinal)) return;

        try { Process.Start(new ProcessStartInfo(ziel) { UseShellExecute = true }); }
        catch (Exception) { /* kein Browser, keine Zuordnung - nicht der Rede wert */ }
    }
}
