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
    private CancellationTokenSource? _ffmpegValidation;
    private bool _closing;
    private bool _running;

    /// <summary>Laeuft waehrend des Exports - Grundlage fuer Restzeit und Durchsatz.</summary>
    private readonly System.Diagnostics.Stopwatch _runClock = new();

    /// <summary>
    /// Eine im Hintergrund fertig kodierte Fassung, die zu den aktuellen Einstellungen
    /// passt - oder null. Wird bei jeder Aenderung neu gesucht, denn jede Aenderung
    /// kann sie ungueltig machen.
    /// </summary>
    private string? _prepared;

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

        // Die Wiedergaberate darf frei eingestellt werden; die Liste hier kennt nur
        // die ueblichen Werte. Wer bei 37,5 zugesehen hat und dann exportiert, soll
        // nicht stillschweigend 30 bekommen - also kommt sein Wert in die Liste.
        var rates = FpsOption.All.ToList();

        if (playbackFps > 0 && !rates.Any(o => Math.Abs(o.Value - playbackFps) < 0.001))
        {
            rates.Add(new FpsOption(playbackFps,
                playbackFps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

            rates.Sort((a, b) => a.Value.CompareTo(b.Value));
        }

        QualityBox.ItemsSource = ExportQuality.All;
        QualityBox.SelectedItem = ExportQuality.All.FirstOrDefault(q => q.Name == settings.ExportQuality)
                                  ?? ExportQuality.High;

        SpeedBox.ItemsSource = ExportSpeed.All;
        SpeedBox.SelectedItem = ExportSpeed.All.FirstOrDefault(s => s.Name == settings.ExportSpeed)
                                ?? ExportSpeed.Balanced;

        QualityHint.Text = CurrentQuality.Hint;
        SpeedHint.Text = CurrentSpeed.Hint;

        FpsBox.ItemsSource = rates;
        FpsBox.SelectedItem = rates.FirstOrDefault(o => Math.Abs(o.Value - playbackFps) < 0.001)
                              ?? FpsOption.Closest(playbackFps);

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
        BeginFfmpegValidation(found);
    }

    private void BeginFfmpegValidation(string? path)
    {
        _ffmpegValidation?.Cancel();

        if (string.IsNullOrWhiteSpace(path))
        {
            _ffmpegValidation = null;
            FfmpegHint.Text = FfmpegLocator.InstallHint;
            FfmpegHint.Foreground = (System.Windows.Media.Brush)FindResource("GapBrush");
            StartButton.IsEnabled = false;
            return;
        }

        var validation = new CancellationTokenSource();
        _ffmpegValidation = validation;
        StartButton.IsEnabled = false;
        FfmpegHint.Text = "ffmpeg wird geprüft …";
        FfmpegHint.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");

        _ = ValidateFfmpegAsync(path, validation);
    }

    private async Task ValidateFfmpegAsync(string path, CancellationTokenSource validation)
    {
        string? version;

        try
        {
            // Nicht nur auf den Dateinamen verlassen: eine gleichnamige Datei belegt
            // nicht, dass dahinter ein lauffaehiges ffmpeg steckt. Die Pruefung
            // findet bewusst neben dem UI-Thread statt und hat eine feste Frist.
            version = await FfmpegLocator.TryReadVersionAsync(path, cancellation: validation.Token);
        }
        catch (Exception)
        {
            // Ein fremdes Programm darf den Dispatcher nie mit einer Ausnahme
            // erreichen. Der Locator faengt bereits ab; dies sichert den Dialog.
            version = null;
        }

        if (!ReferenceEquals(_ffmpegValidation, validation))
        {
            validation.Dispose();
            return;
        }

        _ffmpegValidation = null;

        bool cancelled = validation.IsCancellationRequested;
        validation.Dispose();

        if (_closing || cancelled) return;

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
        BeginFfmpegValidation(dialog.FileName);
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

        ShowEstimate(request);
        LookForPrepared(request);
    }

    /// <summary>
    /// Was vermutlich herauskommt: Groesse und Dauer.
    ///
    /// Beides mit "ca." und beides aus den aktuellen Einstellungen - wer die Qualitaet
    /// eine Stufe hoeher stellt, soll die Folge sofort sehen und nicht erst nach dem
    /// Export. Die Dauer stuetzt sich auf das, was diese Maschine beim letzten Mal
    /// geschafft hat; beim ersten Export steht dort ein vorsichtiger Anfangswert.
    /// </summary>
    private void ShowEstimate(ExportRequest request)
    {
        if (EstimateText is null) return;

        var parts = new List<string>();

        double calibration = _settings.ExportSizeFactor > 0 ? _settings.ExportSizeFactor : 1.0;

        if (ExportEstimate.Bytes(request, CurrentQuality, CurrentSpeed, calibration) is { } bytes && bytes > 0)
            parts.Add("ca. " + ExportEstimate.Size(bytes));

        double throughput = _settings.ExportThroughput > 0
            ? _settings.ExportThroughput
            : ExportEstimate.DefaultThroughput;

        if (ExportEstimate.Duration(request, CurrentSpeed, throughput) is { } span)
            parts.Add("ca. " + ExportEstimate.Clock(span) + " Export");

        if (_prepared is { Length: > 0 }) parts.Clear();

        EstimateText.Text = parts.Count > 0 ? string.Join("  ·  ", parts) : string.Empty;
        EstimateText.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Nachsehen, ob das Dashboard diesen Fall schon kodiert hat.
    ///
    /// Streng verglichen wird, und das ist Absicht: Eine Fassung, die nur fast passt,
    /// waere schlimmer als gar keine - sie kaeme heraus, ohne dass jemand merkt, dass
    /// er etwas anderes eingestellt hatte. Passt etwas nicht, wird ganz normal
    /// kodiert, und der Benutzer verliert nichts ausser der Abkuerzung.
    /// </summary>
    private void LookForPrepared(ExportRequest request)
    {
        _prepared = null;

        // Eine eingerechnete Bildkorrektur entsteht erst hier im Dialog - dafuer kann
        // es nichts Vorbereitetes geben.
        if (request.Adjustments is not null || _inOutFrames.Count < 2)
        {
            PreparedHint.Visibility = Visibility.Collapsed;
            return;
        }

        var print = new VideoFingerprint(
            _sequence.Pattern.Describe(),
            request.Frames.Count > 0 ? request.Frames[0].Number : 0,
            request.Frames.Count > 0 ? request.Frames[^1].Number : 0,
            request.Frames.Count,
            request.Fps,
            _sourceWidth,
            _sourceHeight,
            request.TargetWidth,
            request.Preset.Name,
            request.Gaps.ToString(),
            PreparedVideo.NewestTicks(request.Frames.Select(f => f.Path)));

        if (PreparedVideo.TryFind(print, request.Preset.Extension, out string found))
        {
            _prepared = found;
            PreparedHint.Visibility = Visibility.Visible;
        }
        else
        {
            PreparedHint.Visibility = Visibility.Collapsed;
        }
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
        Crf = ExportEstimate.Crf(CurrentPreset, CurrentQuality),
        Speed = CurrentSpeed.Value,
    };

    private ExportQuality CurrentQuality
        => QualityBox?.SelectedItem as ExportQuality ?? ExportQuality.High;

    private ExportSpeed CurrentSpeed
        => SpeedBox?.SelectedItem as ExportSpeed ?? ExportSpeed.Balanced;

    private void OnQualityChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (QualityHint is null) return;

        QualityHint.Text = CurrentQuality.Hint;
        UpdateSummary();
    }

    private void OnSpeedChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SpeedHint is null) return;

        SpeedHint.Text = CurrentSpeed.Hint;
        UpdateSummary();
    }

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
        _settings.ExportQuality = CurrentQuality.Name;
        _settings.ExportSpeed = CurrentSpeed.Name;
        _persist(_settings);

        // Die fertige Fassung an ihren Platz bringen. Kopiert wird, nicht verschoben:
        // Wer zweimal exportiert, soll nicht beim zweiten Mal warten muessen.
        if (_prepared is { Length: > 0 } ready && File.Exists(ready))
        {
            try
            {
                File.Copy(ready, request.OutputPath, overwrite: true);

                long copied = 0;
                try { copied = new FileInfo(request.OutputPath).Length; } catch (Exception) { }

                ShowStatus($"Übernommen: {Path.GetFileName(request.OutputPath)}" +
                           (copied > 0 ? $"  ({copied / (1024.0 * 1024):0.0} MB)" : "") +
                           "  ·  war bereits vorbereitet", accent: false);

                ProgressPanel.Visibility = Visibility.Visible;
                Progress.Value = 1;
                ProgressLeft.Text = "übernommen";
                ProgressRight.Text = "100 %";

                RevealInExplorer(request.OutputPath);
                return;
            }
            catch (Exception copyFailed)
            {
                // Gescheitert heisst hier nur: dann eben kodieren.
                ShowStatus("Die vorbereitete Fassung liess sich nicht kopieren (" +
                           copyFailed.Message + ") – es wird neu kodiert.", accent: true);
            }
        }

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
                   (size > 0 ? $"  ({size / (1024.0 * 1024):0.0} MB)" : "") +
                   $"  ·  {ExportEstimate.Clock(_runClock.Elapsed)}", accent: false);

        Progress.Value = 1;
        ProgressRight.Text = "100 %";

        RememberThroughput(request);
        RememberSize(request, size);
        RevealInExplorer(result.OutputPath!);
    }

    /// <summary>
    /// Behalten, was die Maschine geschafft hat.
    ///
    /// Damit wird die Dauerschaetzung mit jedem Export besser. Gemittelt wird mit dem
    /// bisherigen Wert, damit ein einzelner Lauf unter ungewoehnlicher Last - etwa
    /// waehrend eines Renders - die Schaetzung nicht dauerhaft verzieht.
    /// </summary>
    private void RememberThroughput(ExportRequest request)
    {
        double seconds = _runClock.Elapsed.TotalSeconds;
        if (seconds < 1) return;

        double megapixels = (double)ExportEstimate.OutputWidth(request)
                            * ExportEstimate.OutputHeight(request)
                            * request.OutputFrameCount / 1_000_000.0;

        double passes = request.Preset.TwoPassPalette ? 1.5 : 1.0;
        double measured = megapixels / seconds * passes / CurrentSpeed.Factor;

        if (!(measured > 0) || double.IsInfinity(measured)) return;

        _settings.ExportThroughput = _settings.ExportThroughput > 0
            ? _settings.ExportThroughput * 0.6 + measured * 0.4
            : measured;

        _persist(_settings);
    }

    /// <summary>
    /// Behalten, wie weit die Schaetzung danebenlag.
    ///
    /// Gemittelt wie beim Durchsatz, damit ein einzelner ungewoehnlicher Export - ein
    /// Standbild, eine Szene voller Rauch - die Schaetzung nicht dauerhaft verzieht.
    /// </summary>
    private void RememberSize(ExportRequest request, long actual)
    {
        if (actual <= 0) return;

        // Ohne Kalibrierung rechnen, sonst misst man die eigene Korrektur mit.
        if (ExportEstimate.Bytes(request, CurrentQuality, CurrentSpeed) is not { } raw || raw <= 0) return;

        double factor = actual / (double)raw;

        if (!(factor > ExportEstimate.MinFactor) || factor > ExportEstimate.MaxFactor) return;

        _settings.ExportSizeFactor = _settings.ExportSizeFactor > 0
            ? _settings.ExportSizeFactor * 0.6 + factor * 0.4
            : factor;

        _persist(_settings);
    }

    private void OnProgress(ExportProgress progress)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Progress.Value = progress.Fraction;

            var stage = progress.PassCount > 1
                ? $"{progress.Stage} ({progress.PassIndex + 1}/{progress.PassCount})"
                : progress.Stage;

            ProgressLeft.Text = progress.Frame > 0
                ? $"{stage}  ·  Frame {progress.Frame} von {progress.TotalFrames}" +
                  (progress.Fps > 0 ? $"  ·  {progress.Fps:0} fps" : "")
                : stage + " …";

            // Die Restzeit kommt aus dem, was wirklich passiert ist - nicht aus der
            // Schaetzung von vorhin. Erst ab fuenf Prozent, sonst rechnet sie aus
            // einem Anlaufwert eine Zahl, die gleich darauf wieder falsch ist.
            double done = progress.Fraction;
            var elapsed = _runClock.Elapsed;

            string right = $"{done * 100:0} %";

            if (done > 0.05 && elapsed.TotalSeconds > 1.5)
            {
                var left = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - done) / done);

                right += $"  ·  noch {ExportEstimate.Clock(left)}";
            }

            ProgressRight.Text = right;
        }));
    }

    private void BeginRunningState()
    {
        _running = true;
        StartButton.Content = "Abbrechen";

        ProgressPanel.Visibility = Visibility.Visible;
        Progress.Value = 0;
        ProgressLeft.Text = "wird vorbereitet …";
        ProgressRight.Text = "0 %";

        _runClock.Restart();

        QualityBox.IsEnabled = false;
        SpeedBox.IsEnabled = false;
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

        _runClock.Stop();

        QualityBox.IsEnabled = true;
        SpeedBox.IsEnabled = true;
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

        _closing = true;
        _ffmpegValidation?.Cancel();
        base.OnClosing(e);
    }
}
