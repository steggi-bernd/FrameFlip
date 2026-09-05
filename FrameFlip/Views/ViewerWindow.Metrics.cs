using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Threading;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;

namespace FrameFlip.Views;

/// <summary>
/// Der Metrik-Fluegel: was gerade gerendert wird und was der Rechner dabei tut.
///
/// Er erscheint von selbst, sobald ein Render gemeldet wird, und verschwindet nicht
/// sofort wieder, wenn er endet - man will ja sehen, WAS fertig geworden ist.
///
/// Er sitzt links in einer eigenen Spalte, die Korrektur rechts in ihrer. Beide
/// koennen offen sein, und jede rechnet nur mit ihrer eigenen Breite an der
/// Fenstergroesse. Solange sie sich eine Zelle teilten, mussten sie einander
/// verdraengen - und beim Hin- und Herschalten rechneten beide an derselben Breite,
/// bis die Fenstergroesse nicht mehr stimmte.
/// </summary>
public partial class ViewerWindow
{
    /// <summary>Takt der Anzeige. Schneller braucht es nicht - langsamer wirkt tot.</summary>
    private static readonly TimeSpan MetricsTick = TimeSpan.FromSeconds(1);

    private RenderMonitor? _monitor;
    private DispatcherTimer? _metricsTimer;

    /// <summary>Letzte Messung des Lastwaechters, von ApplyLoad hinterlegt.</summary>
    private LoadSnapshot? _lastLoad;

    private bool _metricsOpen;

    /// <summary>Welcher Frame gerade als Vorschau geladen ist - verhindert Doppelarbeit.</summary>
    private string? _previewPath;

    /// <summary>
    /// Der Auftrag, fuer den sich die Spalte bereits selbst geoeffnet hat. Jeder
    /// Auftrag meldet sich genau einmal - danach entscheidet der Nutzer.
    /// </summary>
    private string? _announcedJob;

    /// <summary>Oeffnet die Einstellungen. Vom Tray gesetzt, der sie ohnehin verwaltet.</summary>
    public Action? SettingsRequested { get; set; }


    public void AttachRenderMonitor(RenderMonitor monitor)
    {
        _monitor = monitor;

        // Die Ereignisse kommen von einem Hintergrundthread; alles, was Oberflaeche
        // anfasst, muss auf den UI-Thread.
        monitor.Changed += OnMonitorChangedOffThread;

        _metricsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = MetricsTick };
        _metricsTimer.Tick += (_, _) => RefreshMetrics();
        _metricsTimer.Start();

        RefreshMetrics();
    }

    private void OnMonitorChangedOffThread()
    {
        if (_closing) return;

        try { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshMetrics)); }
        catch (Exception) { /* Fenster schliesst gerade */ }
    }

    // ---------------------------------------------------------------- Ein und aus

    /// <summary>Klappt die Metrikspalte links auf. Die Korrektur bleibt, wo sie ist.</summary>
    private void ShowMetrics()
    {
        if (_metricsOpen) return;

        MetricsPanel.Visibility = Visibility.Visible;
        _metricsOpen = true;

        // Eigene Spalte, eigene Rechnung: Das Fenster waechst um genau diese Breite
        // nach links. Die Korrekturspalte rechts bleibt davon unberuehrt - beide
        // koennen gleichzeitig offen sein.
        ResizeForPanel(true, MetricsPanel.Width + 1, toTheLeft: true);

        MetricsButton.IsChecked = true;

        SyncViewport();
        ScheduleRedecode(deferWhilePlaying: false);
    }

    private void HideMetrics()
    {
        if (!_metricsOpen) return;

        MetricsPanel.Visibility = Visibility.Collapsed;
        _metricsOpen = false;

        ResizeForPanel(false, MetricsPanel.Width + 1, toTheLeft: true);

        MetricsButton.IsChecked = false;

        SyncViewport();
        ScheduleRedecode(deferWhilePlaying: false);
    }

    /// <summary>Vom Nutzer umgeschaltet - dann bleibt der Fluegel auch ohne Render zu.</summary>
    private void ToggleMetrics()
    {
        if (_metricsOpen) { HideMetrics(); return; }

        ShowMetrics();
    }

    /// <summary>
    /// Oeffnet die Einstellungen. Der Viewer baut den Dialog nicht selbst: Ihn
    /// verwaltet der Tray, der auch weiss, wie geaenderte Werte zu pruefen und zu
    /// sichern sind. Hier wird nur gefragt.
    /// </summary>
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsRequested is null)
        {
            ShowStatus(Strings.T("S_SettingsViaTray"));
            return;
        }

        // Dass die Vorschau waehrenddessen nicht wegschliesst, regelt der Tray ueber
        // ModalDialogOpen - hier noch einmal daran zu drehen waere ein zweiter
        // Schalter fuer dieselbe Sache.
        SettingsRequested.Invoke();
    }

    private void OnMetricsToggled(object sender, RoutedEventArgs e)
    {
        // Der Knopf spiegelt den Zustand, deshalb nur handeln, wenn er wirklich
        // abweicht - sonst schaukelt er sich mit ShowMetrics gegenseitig hoch.
        bool wanted = MetricsButton.IsChecked == true;
        if (wanted == _metricsOpen) return;

        ToggleMetrics();
    }

    // ---------------------------------------------------------------- Anzeige

    private void RefreshMetrics()
    {
        if (_closing) return;

        var job = _monitor?.Job;

        // Ein neu gemeldeter Render klappt die Spalte EINMAL auf und danach nie
        // wieder von selbst.
        //
        // Ohne das Merken der Kennung tat er es bei jedem Takt: Wer die Spalte
        // waehrend eines Renders zumachte, sah sie eine Sekunde spaeter wieder
        // aufspringen und konnte sie ueberhaupt nicht loswerden.
        if (job is not null && job.IsRunning && !_metricsOpen &&
            !string.Equals(job.Id, _announcedJob, StringComparison.Ordinal))
        {
            _announcedJob = job.Id;
            ShowMetrics();
        }

        if (!_metricsOpen) return;

        SampleSystemLoad();

        // Der Leerzustand steht IM Panel, nicht als Meldung ueber dem Bild: Eine
        // Meldung dort bleibt stehen und widerspricht spaeter den Zahlen daneben.
        bool empty = job is null;

        MetricsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        MetricsEmpty.Text = _monitor?.IsListening == true
            ? Strings.T("S_NoRender")
            : Strings.T("S_BridgeSilent");

        JobStateBadge.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (job is null) return;

        UpdateJobState(job);
        UpdateOverall(job);
        UpdateCurrentFrame(job);
        UpdateSpeed(job);
        UpdateTimes(job);
        UpdateJobDescription(job);
        UpdatePreview(job);
    }

    // ---------------------------------------------------------------- Tempoverlauf

    private void UpdateSpeed(RenderJob job)
    {
        var durations = job.FrameDurations;

        SpeedCard.Visibility = durations.Length >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (durations.Length < 2) return;

        SpeedChart.SetData(durations);

        // Die Spanne beantwortet, ob der Mittelwert ueberhaupt etwas taugt: Liegen
        // schnellster und langsamster Frame weit auseinander, sagt er wenig.
        if (job.FastestFrame is double fastest && job.SlowestFrame is double slowest)
            SpeedSpread.Text = $"{Seconds(fastest)} – {Seconds(slowest)}";
    }

    // ---------------------------------------------------------------- Vorschau

    /// <summary>
    /// Zeigt den zuletzt geschriebenen Frame klein an.
    ///
    /// Bewusst klein dekodiert: Die Datei kann 70 MB haben, und fuer 260 Pixel
    /// Breite muss sie nicht in voller Groesse in den Speicher. Geladen wird auf
    /// einem Hintergrundthread - waehrend eines Renders ist der UI-Thread das
    /// Letzte, was auf eine Platte warten sollte.
    /// </summary>
    private void UpdatePreview(RenderJob job)
    {
        var path = job.LatestFrameFile;

        if (string.IsNullOrEmpty(path))
        {
            PreviewCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (string.Equals(path, _previewPath, StringComparison.OrdinalIgnoreCase)) return;

        _previewPath = path;
        PreviewCaption.Text = $"Frame {job.CurrentFrame}";

        _ = LoadPreviewAsync(path);
    }

    private async Task LoadPreviewAsync(string path)
    {
        var image = await Task.Run(() => DecodeThumbnail(path));

        if (_closing || image is null) return;

        // Zwischenzeitlich kam ein neuerer Frame: Der alte waere jetzt falsch.
        if (!string.Equals(path, _previewPath, StringComparison.OrdinalIgnoreCase)) return;

        LatestPreview.Source = image;
        PreviewCard.Visibility = Visibility.Visible;
    }

    private static BitmapSource? DecodeThumbnail(string path)
    {
        try
        {
            // Der Render schreibt womoeglich noch. FileShare erlaubt das Lesen
            // trotzdem; ein halb geschriebenes Bild faengt der catch ab.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 272;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception)
        {
            // Noch nicht fertig geschrieben, gesperrt, geloescht - beim naechsten
            // Frame gibt es ohnehin ein neues Bild.
            return null;
        }
    }

    private void UpdateJobState(RenderJob job)
    {
        (string label, string key) = job.State switch
        {
            JobState.Preparing => (Strings.T("S_JobPreparing"), "AccentBrush"),
            JobState.Rendering => (Strings.T("S_JobRunning"), "AccentBrush"),
            JobState.Finished => (Strings.T("S_JobFinished"), "MutedBrush"),
            JobState.Cancelled => (Strings.T("S_JobCancelled"), "GapBrush"),
            JobState.Failed => (Strings.T("S_JobFailed"), "GapBrush"),
            _ => (Strings.T("S_JobIdle"), "MutedBrush"),
        };

        JobStateText.Text = label;
        JobStateText.Foreground = (Brush)FindResource(key);
        JobStateBadge.Visibility = Visibility.Visible;
    }

    private void UpdateOverall(RenderJob job)
    {
        double progress = job.Progress;

        OverallPercent.Text = (progress * 100).ToString("0");
        SetFill(OverallFill, progress);

        FrameCounter.Text = Strings.T("S_FrameOf", job.FramesWritten, job.TotalFrames);

        RemainingText.Text = job.Remaining is TimeSpan left
            ? Strings.T("S_RemainingShort", Duration(left))
            : string.Empty;
    }

    private void UpdateCurrentFrame(RenderJob job)
    {
        var stats = job.Stats;

        if (stats.Sample is int sample)
        {
            SampleValue.Text = sample.ToString();
            SampleTotal.Text = stats.SampleTotal is int total ? $"/ {total}" : string.Empty;
        }
        else
        {
            SampleValue.Text = "–";
            SampleTotal.Text = string.Empty;
        }

        SetFill(SampleFill, stats.SampleProgress ?? 0);

        // Blenders eigene Restzeit gilt fuer DIESEN Frame - im Unterschied zu der
        // Angabe oben, die den ganzen Auftrag meint. Beide nebeneinander sind kein
        // Widerspruch, sondern zwei verschiedene Fragen.
        FrameRemainingText.Text = stats.FrameRemaining is TimeSpan frameLeft
            ? Strings.T("S_RemainingShort", Duration(frameLeft))
            : string.Empty;

        MemoryText.Text = stats.MemoryMb is long memory
            ? $"{memory / 1024.0:0.0} GB"
            : string.Empty;

        // Ohne Sample-Zaehler bleibt die Taetigkeit die einzige Auskunft darueber, ob
        // ueberhaupt etwas passiert - "Updating Volume · Building octree" erklaert
        // eine Minute Stillstand, die sonst wie ein Absturz aussieht.
        ActivityText.Text = stats.Activity ?? string.Empty;
    }

    private void UpdateTimes(RenderJob job)
    {
        if (job.SecondsPerFrame is double perFrame)
        {
            bool minutes = perFrame >= 90;

            PerFrameValue.Text = minutes ? (perFrame / 60).ToString("0.0") : perFrame.ToString("0.0");
            PerFrameUnit.Text = minutes ? "min" : "s";
        }
        else
        {
            PerFrameValue.Text = "–";
            PerFrameUnit.Text = string.Empty;
        }

        ElapsedValue.Text = Duration(job.Elapsed);
    }

    private void UpdateJobDescription(RenderJob job)
    {
        JobFileText.Text = string.IsNullOrEmpty(job.BlendFile)
            ? string.Empty
            : Path.GetFileName(job.BlendFile);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(job.Scene)) parts.Add(job.Scene);
        if (!string.IsNullOrEmpty(job.Engine)) parts.Add(job.Engine);
        if (job.Width > 0 && job.Height > 0) parts.Add($"{job.Width}×{job.Height}");

        JobDetailText.Text = string.Join("  ·  ", parts);
    }

    // ---------------------------------------------------------------- Auslastung

    /// <summary>
    /// Uebernimmt die Messung, die der Lastwaechter ohnehin macht. Ein zweiter
    /// Messpfad waere Aufwand fuer dieselbe Zahl - und ausgerechnet waehrend eines
    /// Renders ist jede eingesparte Messung willkommen.
    /// </summary>
    private void SampleSystemLoad()
    {
        var snapshot = _lastLoad;
        if (snapshot is null) return;

        CpuLine.Add(snapshot.CpuPercent / 100.0);
        CpuValue.Text = $"{snapshot.CpuPercent:0} %";

        if (snapshot.AvailableMb > 0 && snapshot.TotalMb > 0)
        {
            double used = 1 - snapshot.AvailableMb / (double)snapshot.TotalMb;

            RamLine.Add(used);

            // Belegt statt frei: Die Kurve zeigt Auslastung, und Zahl und Kurve
            // sollen dasselbe meinen. Der freie Rest steht daneben.
            RamValue.Text = Strings.T("S_MemoryFree",
                                      $"{used * 100:0}",
                                      $"{snapshot.AvailableMb / 1024.0:0.0}");
        }

        if (snapshot.GpuPercent is double gpu)
        {
            GpuLine.Add(gpu / 100.0);
            GpuValue.Text = $"{gpu:0} %";
        }
        else
        {
            GpuValue.Text = Strings.T("S_NotMeasurable");
        }
    }

    // ---------------------------------------------------------------- Hilfen

    /// <summary>
    /// Breite des gefuellten Teils. Ueber die tatsaechliche Breite der Spur gerechnet,
    /// nicht ueber einen Prozentwert im Layout: Nur so stimmt der Balken auch, wenn
    /// das Fenster gerade eine andere Groesse hat.
    /// </summary>
    private static void SetFill(FrameworkElement fill, double fraction)
    {
        if (fill.Parent is not FrameworkElement track) return;

        double available = track.ActualWidth;
        if (available <= 0) return;

        fill.Width = Math.Clamp(fraction, 0, 1) * available;
    }

    /// <summary>Sekunden lesbar - ab anderthalb Minuten in Minuten.</summary>
    private static string Seconds(double seconds)
        => seconds >= 90 ? $"{seconds / 60:0.0} min" : $"{seconds:0.0} s";

    private static string Duration(TimeSpan span)
        => span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";

    private void DisposeMetrics()
    {
        _metricsTimer?.Stop();
        _metricsTimer = null;

        if (_monitor is not null) _monitor.Changed -= OnMonitorChangedOffThread;
        _monitor = null;
    }
}
