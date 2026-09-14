using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrameFlip.Decoding;
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

    private static readonly (GradeOutputFormat Format, string Key)[] Formats =
    {
        (GradeOutputFormat.Png16, "S_FormatPng16"),
        (GradeOutputFormat.Png8, "S_FormatPng8"),
        (GradeOutputFormat.Tiff16, "S_FormatTiff16"),
        (GradeOutputFormat.Jpeg, "S_FormatJpeg"),
    };

    private void SetUpBatch()
    {
        foreach (var (_, key) in Formats) FormatBox.Items.Add(Strings.T(key));
        FormatBox.SelectedIndex = 0;
    }

    private GradeOutputFormat SelectedFormat
        => Formats[Math.Clamp(FormatBox.SelectedIndex, 0, Formats.Length - 1)].Format;

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

        var request = new GradeBatchRequest
        {
            Frames = frames,
            OutputDirectory = _target,
            Format = SelectedFormat,
            Adjustments = Tools.Adjustments,

            // Kopiert: waehrend der Lauf laeuft, darf am Original weitergeregelt
            // werden, ohne dass sich die Ausgabe auf halber Strecke aendert.
            Grading = Tools.Stack.Clone(),

            View = _frame is not null ? ViewFor(_frame) : new StandardViewTransform(),
            MaxWorkers = Workers?.Invoke() ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
        };

        _running = new CancellationTokenSource();
        var token = _running.Token;

        RunButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        BatchProgress.Visibility = Visibility.Visible;
        BatchProgress.Value = 0;
        Tools.IsEnabled = false;

        var progress = new Progress<GradeProgress>(p =>
        {
            BatchProgress.Value = p.Total > 0 ? p.Done / (double)p.Total : 0;
            BatchStatus.Text = $"{p.Done} / {p.Total}";
        });

        try
        {
            var result = await Task.Run(() => GradeBatch.Run(request, progress, token), token);
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
