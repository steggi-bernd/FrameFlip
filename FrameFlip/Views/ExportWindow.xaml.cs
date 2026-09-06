using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Export;
using FrameFlip.Imaging;
using FrameFlip.Playback;
using FrameFlip.Sequencing;

namespace FrameFlip.Views;

/// <summary>
/// Exportdialog. Kennt die Sequenz und den Bereich, den der Player gerade zeigt,
/// und stellt daraus einen Auftrag zusammen.
///
/// Das Fenster bleibt waehrend des Exports bedienbar - abgebrochen wird ueber
/// denselben Knopf, der den Export gestartet hat.
/// </summary>
public partial class ExportWindow : Window
{
    private readonly ImageSequence _sequence;
    private readonly IReadOnlyList<SequenceFrame> _inOutFrames;
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _persist;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly Func<int> _threadBudget;

    /// <summary>Korrektur aus der Vorschau. Neutral, wenn dort nichts eingestellt ist.</summary>
    private readonly ImageAdjustments _adjustments;

    private CancellationTokenSource? _cancellation;
    private bool _running;

    private sealed record ScaleOption(string Name, int Width)
    {
        public override string ToString() => Name;
    }

    public ExportWindow(ImageSequence sequence, IReadOnlyList<SequenceFrame> inOutFrames,
                        double playbackFps, int sourceWidth, int sourceHeight,
                        AppSettings settings, Action<AppSettings> persist,
                        Func<int> threadBudget, ImageAdjustments? adjustments = null)
    {
        _adjustments = adjustments ?? ImageAdjustments.Neutral;
        _sequence = sequence;
        _inOutFrames = inOutFrames;
        _settings = settings;
        _persist = persist;
        _sourceWidth = Math.Max(1, sourceWidth);
        _sourceHeight = Math.Max(1, sourceHeight);
        _threadBudget = threadBudget;

        InitializeComponent();

        PresetBox.ItemsSource = ExportPreset.All;
        PresetBox.SelectedItem = ExportPreset.All.FirstOrDefault(p => p.Name == settings.ExportPreset)
                                 ?? ExportPreset.H264;

        FpsBox.ItemsSource = FpsOption.All;
        FpsBox.SelectedItem = FpsOption.Closest(playbackFps);

        ScaleBox.ItemsSource = BuildScaleOptions();
        ScaleBox.SelectedIndex = 0;

        InOutRadio.IsEnabled = inOutFrames.Count > 0 && inOutFrames.Count != sequence.Count;
        if (!InOutRadio.IsEnabled)
            InOutRadio.ToolTip = "Erst mit I und O einen Bereich im Player setzen";

        HoldRadio.IsChecked = settings.ExportHoldLastFrame;
        SkipRadio.IsChecked = !settings.ExportHoldLastFrame;

        SetUpAdjustmentQuestion();
        ResolveFfmpeg();

        // Auch beim ersten Anzeigen setzen: OnPresetChanged laeuft im Konstruktor
        // noch nicht, weil das Fenster dort nicht geladen ist.
        PresetHint.Text = CurrentPreset.Description ?? string.Empty;

        UpdateGapVisibility();
        UpdateOutputPath();
        UpdateSummary();
    }

    // ---------------------------------------------------------------- Bildkorrektur

    /// <summary>
    /// Zeigt die Rueckfrage nur, wenn in der Vorschau ueberhaupt etwas eingestellt
    /// ist. Ohne Korrektur waere die Zeile eine Frage ohne Gegenstand.
    ///
    /// Die Antwort wird gemerkt, aber nicht als endgueltig behandelt: die Kaestchen
    /// bleibt sichtbar und laesst sich fuer diesen Export jederzeit anders setzen.
    /// </summary>
    private void SetUpAdjustmentQuestion()
    {
        if (_adjustments.IsNeutral) return;

        AdjustLabel.Visibility = Visibility.Visible;
        AdjustPanel.Visibility = Visibility.Visible;

        ApplyAdjustBox.IsChecked = _settings.ExportApplyAdjustments ?? false;

        var filter = _adjustments.ToFfmpegFilter();
        AdjustHint.Text = filter is null
            ? $"Eingestellt: {_adjustments.Describe()}. Davon laesst sich nichts in ein Video " +
              "einrechnen – Kanalansichten sind reine Beurteilungswerkzeuge."
            : $"Eingestellt: {_adjustments.Describe()}";

        // Eine reine Kanalansicht kann nicht uebernommen werden.
        ApplyAdjustBox.IsEnabled = filter is not null;
        if (filter is null) ApplyAdjustBox.IsChecked = false;
    }

    private void OnApplyAdjustChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settings.ExportApplyAdjustments = ApplyAdjustBox.IsChecked == true;
        _persist(_settings);

        UpdateSummary();
    }

    private ImageAdjustments? AdjustmentsForExport
        => ApplyAdjustBox.IsChecked == true && !_adjustments.IsNeutral ? _adjustments : null;

    // ---------------------------------------------------------------- ffmpeg

    private void ResolveFfmpeg()
    {
        var found = FfmpegLocator.Locate(_settings.FfmpegPath);
        FfmpegBox.Text = found ?? string.Empty;
        ValidateFfmpeg(found);
    }

    private void ValidateFfmpeg(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            FfmpegHint.Text = FfmpegLocator.InstallHint;
            FfmpegHint.Foreground = (System.Windows.Media.Brush)FindResource("GapBrush");
            StartButton.IsEnabled = false;
            return;
        }

        // Nicht nur auf den Dateinamen verlassen: eine gleichnamige Datei belegt
        // nicht, dass dahinter ein lauffaehiges ffmpeg steckt.
        var version = FfmpegLocator.TryReadVersion(path);

        if (version is null)
        {
            FfmpegHint.Text = Localization.Strings.T("S_FfmpegWrong");
            FfmpegHint.Foreground = (System.Windows.Media.Brush)FindResource("GapBrush");
            StartButton.IsEnabled = false;
            return;
        }

        FfmpegHint.Text = version;
        FfmpegHint.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        StartButton.IsEnabled = true;

        if (_settings.FfmpegPath != path)
        {
            _settings.FfmpegPath = path;
            _persist(_settings);
        }
    }

    private void OnBrowseFfmpeg(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "ffmpeg.exe auswählen",
            Filter = "ffmpeg|ffmpeg.exe|Programme|*.exe|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        FfmpegBox.Text = dialog.FileName;
        ValidateFfmpeg(dialog.FileName);
    }

    // ---------------------------------------------------------------- Eingaben

    private IReadOnlyList<ScaleOption> BuildScaleOptions()
    {
        var options = new List<ScaleOption> { new("Original", 0) };

        // Nur Verkleinerungen anbieten. Hochskalieren erzeugt keine Details, kostet
        // aber Encodierzeit und Speicherplatz.
        foreach (int width in new[] { 3840, 2560, 1920, 1280, 960, 640 })
            if (width < _sourceWidth) options.Add(new ScaleOption($"{width} px breit", width));

        return options;
    }

    private ExportPreset CurrentPreset => PresetBox.SelectedItem as ExportPreset ?? ExportPreset.H264;

    private double CurrentFps => FpsBox.SelectedItem is FpsOption option ? option.Value : 24.0;

    private int CurrentWidth => ScaleBox.SelectedItem is ScaleOption option ? option.Width : 0;

    private GapHandling CurrentGaps => HoldRadio.IsChecked == true ? GapHandling.HoldLast : GapHandling.Skip;

    private IReadOnlyList<SequenceFrame> CurrentFrames
        => InOutRadio.IsChecked == true && _inOutFrames.Count > 0 ? _inOutFrames : _sequence.Frames;

    private void OnPresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        PresetHint.Text = CurrentPreset.Description ?? string.Empty;
        UpdateOutputPath();
        UpdateSummary();
    }

    private void OnRangeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateGapVisibility();
        UpdateOutputPath();
        UpdateSummary();
    }

    private void OnFpsChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateSummary();
    }

    private void OnScaleChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateSummary();
    }

    private void OnGapChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateSummary();
    }

    // ---------------------------------------------------------------- Anzeige

    private void UpdateGapVisibility()
    {
        var frames = CurrentFrames;
        int span = frames.Count > 0 ? frames[^1].Number - frames[0].Number + 1 : 0;
        int missing = span - frames.Count;

        var visible = missing > 0 ? Visibility.Visible : Visibility.Collapsed;
        GapLabel.Visibility = visible;
        GapPanel.Visibility = visible;

        if (missing > 0)
            GapHint.Text = $"{missing} von {span} Frames fehlen im gewählten Bereich.";
    }

    private void UpdateOutputPath()
    {
        // Vorschlag neben der Sequenz, benannt nach dem Muster. Der Ordner ist schon
        // offen und der Name passt zum Material - das ist fast immer das Gewuenschte.
        var stem = _sequence.Pattern.Prefix.TrimEnd('_', '-', '.', ' ');
        if (string.IsNullOrWhiteSpace(stem))
            stem = Path.GetFileName(_sequence.Pattern.Directory);
        if (string.IsNullOrWhiteSpace(stem)) stem = "sequenz";

        OutputBox.Text = Path.Combine(_sequence.Pattern.Directory, stem + CurrentPreset.Extension);
    }

    private void UpdateSummary()
    {
        var request = BuildRequest();

        int width = request.TargetWidth > 0 ? request.TargetWidth : _sourceWidth;
        int height = request.TargetWidth > 0
            ? (int)Math.Round(_sourceHeight * (request.TargetWidth / (double)_sourceWidth))
            : _sourceHeight;

        // Gerade Masse, wie der Filter sie erzwingt - sonst weicht die Anzeige vom
        // Ergebnis ab.
        width -= width % 2;
        height -= height % 2;

        ScaleHint.Text = $"{width} × {height}";

        var duration = request.Duration;
        string correction = request.Adjustments is null ? "" : "  ·  mit Bildkorrektur";

        SummaryText.Text =
            $"{request.OutputFrameCount} Frames bei {CurrentFps:0.###} fps  ·  " +
            $"{duration.TotalSeconds:0.0} s Laufzeit  ·  {width} × {height}{correction}";
    }

    private ExportRequest BuildRequest() => new()
    {
        Frames = CurrentFrames,
        Preset = CurrentPreset,
        OutputPath = OutputBox.Text.Trim(),
        Fps = CurrentFps,
        Gaps = CurrentGaps,
        TargetWidth = CurrentWidth,
        SourceWidth = _sourceWidth,
        SourceHeight = _sourceHeight,
        Threads = _threadBudget(),
        Adjustments = AdjustmentsForExport,
    };

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var preset = CurrentPreset;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Zieldatei",
            FileName = Path.GetFileName(OutputBox.Text),
            InitialDirectory = SafeDirectory(OutputBox.Text),
            Filter = $"{preset.Name}|*{preset.Extension}|Alle Dateien|*.*",
            DefaultExt = preset.Extension,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FileName;
    }

    private static string SafeDirectory(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            return Directory.Exists(directory) ? directory! : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void OnCopyCommand(object sender, RoutedEventArgs e)
    {
        var request = BuildRequest();
        var passes = FfmpegArguments.Build(request, "frames.txt", "palette.png");

        var lines = passes.Select(p => FfmpegArguments.ToCommandLine(
            FfmpegBox.Text.Trim() is { Length: > 0 } exe ? exe : "ffmpeg", p.Arguments));

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine + Environment.NewLine, lines));
            ShowStatus("Befehl kopiert. Die Frameliste wird beim Export erzeugt.", accent: false);
        }
        catch (Exception)
        {
            ShowStatus("Die Zwischenablage ist gerade nicht verfügbar.", accent: true);
        }
    }

    // ---------------------------------------------------------------- Export

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_running) { _cancellation?.Cancel(); return; }

        var executable = FfmpegBox.Text.Trim();
        if (executable.Length == 0 || !File.Exists(executable))
        {
            ShowStatus("Bitte zuerst ffmpeg auswählen.", accent: true);
            return;
        }

        var request = BuildRequest();

        if (request.OutputPath.Length == 0)
        {
            ShowStatus("Bitte eine Zieldatei angeben.", accent: true);
            return;
        }

        if (request.FrameCount == 0)
        {
            ShowStatus("Der gewählte Bereich enthält keine Frames.", accent: true);
            return;
        }

        // Der Zielname ist frei editierbar, aber nicht jeder Codec passt in jeden
        // Behälter. ProRes in MP4 etwa lässt ffmpeg mit "Could not find tag for codec
        // prores" scheitern - eine Meldung, die ohne Vorwissen niemand deutet.
        // Deshalb hier still korrigieren und sagen, was geschehen ist.
        var extension = Path.GetExtension(request.OutputPath);
        if (!CurrentPreset.Accepts(extension))
        {
            var corrected = Path.ChangeExtension(request.OutputPath, CurrentPreset.Extension);
            OutputBox.Text = corrected;
            request = request with { OutputPath = corrected };

            ShowStatus($"{CurrentPreset.Name} passt nicht in {extension} – " +
                       $"Zieldatei auf {CurrentPreset.Extension} geändert.", accent: false);
        }

        try
        {
            var directory = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            ShowStatus("Der Zielordner ist nicht beschreibbar: " + ex.Message, accent: true);
            return;
        }

        _settings.ExportPreset = CurrentPreset.Name;
        _settings.ExportHoldLastFrame = CurrentGaps == GapHandling.HoldLast;
        _persist(_settings);

        BeginRunningState();

        var exporter = new VideoExporter(executable);
        exporter.Progress += OnProgress;

        _cancellation = new CancellationTokenSource();

        // Waehrend eines laufenden Renders soll der Encoder im Hintergrund bleiben.
        // Die Stufe kommt aus demselben Budget, das auch die Threadzahl bestimmt.
        var priority = _threadBudget() <= 1
            ? ProcessPriorityClass.Idle
            : ProcessPriorityClass.BelowNormal;

        ExportResult result;
        try
        {
            result = await exporter.RunAsync(request, priority, _cancellation.Token);
        }
        finally
        {
            exporter.Progress -= OnProgress;
            _cancellation.Dispose();
            _cancellation = null;
            EndRunningState();
        }

        if (result.Cancelled)
        {
            ShowStatus("Abgebrochen. Die unvollständige Datei wurde entfernt.", accent: false);
            return;
        }

        if (!result.Success)
        {
            ShowStatus(result.Error ?? "Der Export ist fehlgeschlagen.", accent: true);
            return;
        }

        long size = 0;
        try { size = new FileInfo(result.OutputPath!).Length; } catch (Exception) { }

        ShowStatus($"Fertig: {Path.GetFileName(result.OutputPath)}" +
                   (size > 0 ? $"  ({size / (1024.0 * 1024):0.0} MB)" : ""), accent: false);

        Progress.Value = 1;
        RevealInExplorer(result.OutputPath!);
    }

    private void OnProgress(ExportProgress progress)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Progress.Value = progress.Fraction;

            var stage = progress.PassCount > 1
                ? $"{progress.Stage} ({progress.PassIndex + 1}/{progress.PassCount})"
                : progress.Stage;

            ShowStatus(progress.Frame > 0
                ? $"{stage}: Frame {progress.Frame} von {progress.TotalFrames}" +
                  (progress.Fps > 0 ? $"  ·  {progress.Fps:0} fps" : "")
                : stage + " …", accent: false);
        }));
    }

    private void BeginRunningState()
    {
        _running = true;
        StartButton.Content = "Abbrechen";
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;

        PresetBox.IsEnabled = false;
        FpsBox.IsEnabled = false;
        ScaleBox.IsEnabled = false;
        OutputBox.IsEnabled = false;
        BrowseOutputButton.IsEnabled = false;
        BrowseFfmpegButton.IsEnabled = false;
        FfmpegBox.IsEnabled = false;
        GapPanel.IsEnabled = false;
        WholeSequenceRadio.IsEnabled = false;
        InOutRadio.IsEnabled = false;
    }

    private void EndRunningState()
    {
        _running = false;
        StartButton.Content = "Exportieren";

        PresetBox.IsEnabled = true;
        FpsBox.IsEnabled = true;
        ScaleBox.IsEnabled = true;
        OutputBox.IsEnabled = true;
        BrowseOutputButton.IsEnabled = true;
        BrowseFfmpegButton.IsEnabled = true;
        FfmpegBox.IsEnabled = true;
        GapPanel.IsEnabled = true;
        WholeSequenceRadio.IsEnabled = true;
        InOutRadio.IsEnabled = _inOutFrames.Count > 0 && _inOutFrames.Count != _sequence.Count;
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Ohne Explorer ist die Datei trotzdem geschrieben.
        }
    }

    private void ShowStatus(string message, bool accent)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(accent ? "GapBrush" : "MutedBrush");
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            // Nicht schliessen, solange ffmpeg laeuft: sonst bliebe der Prozess
            // verwaist und schriebe weiter in eine Datei, die niemand mehr erwartet.
            _cancellation?.Cancel();
            return;
        }

        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            _cancellation?.Cancel();
            ShowStatus("Export wird abgebrochen …", accent: false);
            return;
        }

        base.OnClosing(e);
    }
}
