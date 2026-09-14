using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameFlip.Decoding;
using FrameFlip.Export;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;
using FrameFlip.Sequencing;

namespace FrameFlip.Views;

/// <summary>
/// Der Stapellauf auf der Atelierseite: ein Bild einstellen, das Rezept auf die
/// ganze Sequenz anwenden, ausgeben.
///
/// Erst damit schliesst sich der Weg. Ohne ihn waere das Atelier ein
/// Einzelbildwerkzeug, und eine Sequenz von dreihundert Bildern von Hand
/// durchzugehen will niemand.
/// </summary>
public partial class AtelierPage
{
    /// <summary>
    /// Wie viele Bilder gleichzeitig gerechnet werden duerfen.
    ///
    /// Wird vom Programm gesetzt und kommt aus derselben Lastregelung wie die
    /// Decoder-Threads: Laeuft nebenher ein Render, soll der Export langsamer
    /// werden statt um die Kerne zu kaempfen. Ohne Zuweisung bleibt es bei der
    /// Haelfte dessen, was die Maschine hat.
    /// </summary>
    public static Func<int>? Workers { get; set; }

    private ImageSequence? _sequence;
    private string? _target;
    private CancellationTokenSource? _running;

    /// <summary>
    /// Was sich ausgeben laesst. Bildfolgen und Videos stehen in derselben Auswahl,
    /// weil es aus Sicht des Anwenders dieselbe Frage ist - nur die Antwort landet
    /// einmal in vielen Dateien und einmal in einer.
    /// </summary>
    private static readonly (GradeOutputFormat? Image, ExportPreset? Video, string Key)[] Formats =
    {
        (GradeOutputFormat.Png16, null, "S_FormatPng16"),
        (GradeOutputFormat.Png8, null, "S_FormatPng8"),
        (GradeOutputFormat.Tiff16, null, "S_FormatTiff16"),
        (GradeOutputFormat.Jpeg, null, "S_FormatJpeg"),
        (null, ExportPreset.H264, "S_FormatH264"),
        (null, ExportPreset.H265, "S_FormatH265"),
        (null, ExportPreset.ProRes, "S_FormatProRes"),
    };

    private void SetUpBatch()
    {
        foreach (var (_, _, key) in Formats) FormatBox.Items.Add(Strings.T(key));
        FormatBox.SelectedIndex = 0;
    }

    private (GradeOutputFormat? Image, ExportPreset? Video, string Key) Selected
        => Formats[Math.Clamp(FormatBox.SelectedIndex, 0, Formats.Length - 1)];

    /// <summary>
    /// Sucht die Sequenz um das geoeffnete Bild - derselbe Weg, den auch die
    /// Vorschau nimmt. Ein Einzelbild ohne Nummer ergibt eine Sequenz aus einem.
    /// </summary>
    private void FindSequence(string path)
    {
        _sequence = null;

        try
        {
            _sequence = SequenceScanner.Scan(path, _decoders);
        }
        catch (Exception)
        {
            // Ein unlesbarer Ordner heisst: keine Sequenz, nicht: kein Bild.
        }

        UpdateBatchBar();
    }

    private void UpdateBatchBar()
    {
        if (_frame is null || _sequence is null || _sequence.Count == 0)
        {
            ExportBar.Visibility = Visibility.Collapsed;
            return;
        }

        ExportBar.Visibility = Visibility.Visible;

        SequenceText.Text = _sequence.Count == 1
            ? Strings.T("S_SingleImage")
            : Strings.T("S_FrameCount", _sequence.Count.ToString());

        // Ohne Ziel kein Lauf: den Ordner zu erraten waere die Art Bequemlichkeit,
        // die irgendwann dreihundert Dateien an einer ueberraschenden Stelle ablegt.
        RunButton.IsEnabled = _target is not null && _running is null;
        TargetText.Text = _target ?? Strings.T("S_NoTarget");
    }

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e) => UpdateBatchBar();

    private void OnChooseTargetClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.T("S_ChooseTarget"),
        };

        if (_target is not null && Directory.Exists(_target)) dialog.InitialDirectory = _target;
        else if (_path is not null)
        {
            try { dialog.InitialDirectory = Path.GetDirectoryName(_path); }
            catch (Exception) { /* ein ungueltiger Pfad ist kein Grund, den Dialog nicht zu oeffnen */ }
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        _target = dialog.FolderName;
        UpdateBatchBar();
    }

    private async void OnRunClicked(object sender, RoutedEventArgs e)
    {
        if (_sequence is null || _target is null || _running is not null) return;

        var frames = _sequence.Frames.Select(f => f.Path).ToList();
        var chosen = Selected;

        // Fuer ein Video braucht es ffmpeg. Das erst beim Klick zu bemerken ist
        // besser, als den Knopf stumm zu lassen - so steht wenigstens da, warum.
        string? ffmpeg = null;
        if (chosen.Video is not null)
        {
            ffmpeg = FfmpegLocator.Locate(_settings.FfmpegPath);
            if (ffmpeg is null)
            {
                BatchStatus.Text = Strings.T("S_NoFfmpeg");
                return;
            }
        }

        _running = new CancellationTokenSource();
        var token = _running.Token;

        RunButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = true;
        BatchProgress.Visibility = Visibility.Visible;
        BatchProgress.Value = 0;
        Tools.IsEnabled = false;

        int seen = 0;
        var progress = new Progress<GradeProgress>(p =>
        {
            // Beim Video zaehlt der Durchlauf selbst mit, weil die Bilder der Reihe
            // nach in den Strom gehen und nicht einzeln fertig werden.
            int done = p.Done > 0 ? p.Done : ++seen;
            BatchProgress.Value = p.Total > 0 ? done / (double)p.Total : 0;
            BatchStatus.Text = $"{done} / {p.Total}";
        });

        try
        {
            var result = chosen.Video is not null
                ? await RunVideo(frames, chosen.Video, ffmpeg!, progress, token)
                : await RunImages(frames, chosen.Image!.Value, progress, token);

            Report(result);
        }
        catch (OperationCanceledException)
        {
            BatchStatus.Text = Strings.T("S_BatchStopped");
        }
        catch (Exception ex)
        {
            BatchStatus.Text = ex.Message;
        }
        finally
        {
            _running.Dispose();
            _running = null;

            StopButton.Visibility = Visibility.Collapsed;
            BatchProgress.Visibility = Visibility.Collapsed;
            Tools.IsEnabled = true;
            UpdateBatchBar();
        }
    }

    private Task<GradeBatchResult> RunImages(IReadOnlyList<string> frames, GradeOutputFormat format,
                                             IProgress<GradeProgress> progress, CancellationToken token)
    {
        var request = new GradeBatchRequest
        {
            Frames = frames,
            OutputDirectory = _target!,
            Format = format,
            Adjustments = Tools.Adjustments,

            // Kopiert: waehrend der Lauf laeuft, darf am Original weitergeregelt
            // werden, ohne dass sich die Ausgabe auf halber Strecke aendert.
            Grading = Tools.Stack.Clone(),

            View = _frame is not null ? ViewFor(_frame) : new StandardViewTransform(),
            MaxWorkers = Workers?.Invoke() ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
        };

        return Task.Run(() => GradeBatch.Run(request, progress, token), token);
    }

    private Task<GradeBatchResult> RunVideo(IReadOnlyList<string> frames, ExportPreset preset,
                                            string ffmpeg, IProgress<GradeProgress> progress,
                                            CancellationToken token)
    {
        var request = new GradeVideoRequest
        {
            Frames = frames,
            OutputPath = VideoTarget(preset),
            Preset = preset,
            Fps = _settings.Fps > 0 ? _settings.Fps : 24,
            Adjustments = Tools.Adjustments,
            Grading = Tools.Stack.Clone(),
            View = _frame is not null ? ViewFor(_frame) : new StandardViewTransform(),

            // Dieselbe Zurueckhaltung wie beim Rechnen: der Encoder darf einen
            // laufenden Render nicht verdraengen.
            EncoderThreads = Workers?.Invoke() ?? 0,
        };

        return GradeVideo.RunAsync(ffmpeg, request, progress, token);
    }

    /// <summary>
    /// Der Name der Videodatei: der Praefix der Sequenz ohne die Nummer, im
    /// gewaehlten Ordner. Damit heisst das Video wie die Bilder, aus denen es kommt.
    /// </summary>
    private string VideoTarget(ExportPreset preset)
    {
        string name = _sequence?.Pattern.Prefix.TrimEnd('_', '-', '.', ' ') ?? "";
        if (name.Length == 0) name = "atelier";

        return Path.Combine(_target!, name + preset.Extension);
    }

    private void Report(GradeBatchResult result)
    {
        string time = result.Elapsed.TotalSeconds < 90
            ? $"{result.Elapsed.TotalSeconds:0.#} s"
            : $"{result.Elapsed.TotalMinutes:0.#} min";

        if (result.Cancelled)
        {
            BatchStatus.Text = Strings.T("S_BatchStopped") + $" — {result.Written}";
            return;
        }

        if (result.Failures.Count == 0)
        {
            BatchStatus.Text = Strings.T("S_BatchDone", result.Written.ToString()) + $", {time}";
            return;
        }

        // Fehlschlaege werden nicht in einer Zahl versteckt: Wer wissen will, welches
        // Bild fehlt, soll es hier lesen koennen und nicht im Zielordner abzaehlen.
        BatchStatus.Text = Strings.T("S_BatchPartial",
                                     result.Written.ToString(), result.Failures.Count.ToString());

        MessageBox.Show(Window.GetWindow(this),
                        string.Join("\n", result.Failures.Take(20)) +
                        (result.Failures.Count > 20 ? $"\n… ({result.Failures.Count - 20})" : ""),
                        Strings.T("S_BatchProblems"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _running?.Cancel();
        StopButton.IsEnabled = false;
    }
}
