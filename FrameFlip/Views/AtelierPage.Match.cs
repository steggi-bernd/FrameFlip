using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Farbe angleichen und Deflicker (docs/Atelier-Werkzeugplan.md, W2f): was die Seite dafuer misst.
/// Das Vorbild und dieses Bild dort, wo das Werkzeug steht; die Folge im Hintergrund, Bild fuer Bild.
/// </summary>
public partial class AtelierPage
{
    private CancellationTokenSource? _deflickerScan;

    /// <summary>Die laufende oder letzte Messung der Folge - fuer die Probe.</summary>
    internal Task? DeflickerScan { get; private set; }

    private void SetUpMatch()
    {
        Tools.MatchReferenceWanted += (tool, file) =>
        {
            if (file) PickMatchReference(tool);
            else MatchReference(tool);
        };

        Tools.MatchWanted += tool => MatchNow(tool);

        Tools.DeflickerMeasureWanted += tool =>
        {
            if (tool is null) CancelDeflicker();
            else MeasureDeflicker(tool);
        };
    }

    /// <summary>Merkt sich, wie das gezeigte Bild beim Werkzeug aussieht - als Vorbild.</summary>
    internal bool MatchReference(MatchTool tool)
    {
        if (ToolInput(tool, step: 4) is not { } sample) return false;

        Tools.MatchReferenceSet(tool, ColourStats.Measure(sample.Rgb), _path is null ? "?" : Path.GetFileName(_path));
        return true;
    }

    /// <summary>Ein anderes Bild als Vorbild - so, wie es ohne Korrektur aussieht.</summary>
    internal bool MatchReferenceFrom(MatchTool tool, string path)
    {
        var frame = LayeredFrameLoader.Plain(path);
        if (frame is null) return false;

        var sample = FloatFrameProcessor.Sample(frame, ImageAdjustments.Neutral, ViewFor(frame), PreparedGrading.None, step: 4);
        Tools.MatchReferenceSet(tool, ColourStats.Measure(sample.Rgb), Path.GetFileName(path));

        return true;
    }

    private void PickMatchReference(MatchTool tool)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_MatchReferenceFile"),
            Filter = Strings.T("S_MatchReferenceFilter"),
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true) MatchReferenceFrom(tool, dialog.FileName);
    }

    /// <summary>Misst dieses Bild beim Werkzeug und gleicht es ans Vorbild an.</summary>
    internal bool MatchNow(MatchTool tool)
    {
        if (tool.Reference is null || ToolInput(tool, step: 4) is not { } sample) return false;

        Tools.MatchSourceSet(tool, ColourStats.Measure(sample.Rgb));
        return true;
    }

    /// <summary>
    /// Misst die Helligkeit jedes Bildes der Folge - im Hintergrund, auf der Haelfte der Kerne, damit
    /// die Anzeige weiter antwortet. Das Ergebnis geht an das Werkzeug, das gefragt hat.
    /// </summary>
    internal void MeasureDeflicker(DeflickerTool tool)
    {
        if (_deflickerScan is not null) return;

        var paths = _sequence is { Count: > 1 } sequence
            ? sequence.Frames.Select(f => f.Path).ToArray()
            : _path is { } single ? new[] { single } : Array.Empty<string>();

        if (paths.Length == 0) return;

        var cancel = new CancellationTokenSource();
        var dispatcher = Dispatcher;
        int done = 0;

        _deflickerScan = cancel;
        Tools.DeflickerProgress(0, paths.Length);

        DeflickerScan = Task.Run(() =>
        {
            var levels = new ConcurrentDictionary<int, float>();
            var options = new ParallelOptions
            {
                CancellationToken = cancel.Token,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            };

            Parallel.ForEach(paths, options, path =>
            {
                if (LayeredFrameLoader.Plain(path) is { } frame)
                    levels[SequenceLink.NumberOf(path) ?? 0] = DeflickerTool.Level(frame);

                int now = Interlocked.Increment(ref done);
                dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_deflickerScan, cancel)) Tools.DeflickerProgress(now, paths.Length);
                });
            });

            return new Dictionary<int, float>(levels);
        }, cancel.Token).ContinueWith(task => dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_deflickerScan, cancel)) return;

            _deflickerScan = null;
            Tools.DeflickerProgress(0, 0);

            if (task.IsCompletedSuccessfully) Tools.DeflickerMeasured(tool, task.Result);
        }), TaskScheduler.Default);
    }

    private void CancelDeflicker()
    {
        if (_deflickerScan is not { } cancel) return;

        _deflickerScan = null;
        cancel.Cancel();
        Tools.DeflickerProgress(0, 0);
    }
}
