using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameFlip.Bridge;
using FrameFlip.Diagnostics;
using Brush = System.Windows.Media.Brush;

namespace FrameFlip.Views;

/// <summary>
/// Der Metrik-Fluegel: was gerade gerendert wird und was der Rechner dabei tut.
///
/// Er erscheint von selbst, sobald ein Render gemeldet wird, und verschwindet nicht
/// sofort wieder, wenn er endet - man will ja sehen, WAS fertig geworden ist. Er
/// schliesst sich mit dem Korrekturpanel gegenseitig aus: zwei Panels nebeneinander
/// fressen genau die Bildflaeche auf, die man beim Rendern betrachten will.
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
    private bool _panelWasOpen;

    /// <summary>Bezugsgroesse fuer die Speicherkurve, beim ersten Messwert bestimmt.</summary>
    private long _memoryTotalMb;

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

    /// <summary>
    /// Zeigt den Fluegel. Das Korrekturpanel weicht dabei und kommt zurueck, sobald
    /// der Fluegel wieder schliesst - wer die Regler offen hatte, will sie behalten.
    /// </summary>
    private void ShowMetrics()
    {
        if (_metricsOpen) return;

        _panelWasOpen = SidePanel.Visibility == Visibility.Visible;
        if (_panelWasOpen) SetPanelOpen(false, resizeWindow: false);

        MetricsPanel.Visibility = Visibility.Visible;
        _metricsOpen = true;

        SyncViewport();
        ScheduleRedecode(deferWhilePlaying: false);
    }

    private void HideMetrics()
    {
        if (!_metricsOpen) return;

        MetricsPanel.Visibility = Visibility.Collapsed;
        _metricsOpen = false;

        if (_panelWasOpen) SetPanelOpen(true, resizeWindow: false);
        _panelWasOpen = false;

        SyncViewport();
        ScheduleRedecode(deferWhilePlaying: false);
    }

    /// <summary>Vom Nutzer umgeschaltet - dann bleibt der Fluegel auch ohne Render zu.</summary>
    private void ToggleMetrics()
    {
        if (_metricsOpen) HideMetrics();
        else if (_monitor?.Job is not null) ShowMetrics();
        else ShowStatus("Kein Render gemeldet.");
    }

    // ---------------------------------------------------------------- Anzeige

    private void RefreshMetrics()
    {
        if (_closing) return;

        var job = _monitor?.Job;

        // Ein neu gemeldeter Render klappt den Fluegel selbst auf. Ein beendeter
        // laesst ihn stehen, bis der Nutzer ihn zumacht.
        if (job is not null && job.IsRunning && !_metricsOpen) ShowMetrics();
        if (!_metricsOpen) return;

        SampleSystemLoad();

        if (job is null) return;

        UpdateJobState(job);
        UpdateOverall(job);
        UpdateCurrentFrame(job);
        UpdateTimes(job);
        UpdateJobDescription(job);
    }

    private void UpdateJobState(RenderJob job)
    {
        (string label, string key) = job.State switch
        {
            JobState.Preparing => ("wird vorbereitet", "AccentBrush"),
            JobState.Rendering => ("läuft", "AccentBrush"),
            JobState.Finished => ("fertig", "MutedBrush"),
            JobState.Cancelled => ("abgebrochen", "GapBrush"),
            JobState.Failed => ("fehlgeschlagen", "GapBrush"),
            _ => ("bereit", "MutedBrush"),
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

        FrameCounter.Text = $"Frame {job.FramesWritten} / {job.TotalFrames}";

        RemainingText.Text = job.Remaining is TimeSpan left
            ? $"noch {Duration(left)}"
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

        // Ohne Sample-Zaehler bleibt die Taetigkeit die einzige Auskunft darueber, ob
        // ueberhaupt etwas passiert - "Loading render kernels" erklaert eine Minute
        // Stillstand, die sonst wie ein Absturz aussieht.
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

        if (snapshot.AvailableMb > 0)
        {
            // Der Gesamtspeicher steht in keiner Momentaufnahme. Die groesste je
            // gesehene freie Menge ist eine brauchbare Untergrenze dafuer - und mehr
            // braucht eine Kurve nicht, die ohnehin nur den Verlauf zeigen soll.
            _memoryTotalMb = Math.Max(_memoryTotalMb, snapshot.AvailableMb);

            double used = 1 - snapshot.AvailableMb / (double)Math.Max(1, _memoryTotalMb);

            RamLine.Add(used);
            RamValue.Text = $"{snapshot.AvailableMb / 1024.0:0.0} GB frei";
        }

        if (snapshot.GpuPercent is double gpu)
        {
            GpuLine.Add(gpu / 100.0);
            GpuValue.Text = $"{gpu:0} %";
        }
        else
        {
            GpuValue.Text = "nicht messbar";
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
