using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FrameFlip.Imaging.Grading;
using Color = System.Windows.Media.Color;

namespace FrameFlip.Views;

/// <summary>
/// Das Kurvenfeld im Panel: welcher Kanal gezeigt wird, was beim Ziehen passiert,
/// und wann wieder voll gerechnet wird.
/// </summary>
public sealed partial class ViewerWindow
{
    private readonly GradingStack _grading = new();
    private CurvesTool _curves = new();

    /// <summary>
    /// Der vorbereitete Stapel. Wird bei jeder Aenderung neu gebaut und dann von
    /// mehreren Threads gelesen - deshalb ersetzt statt veraendert.
    /// </summary>
    private PreparedGrading _preparedGrading = PreparedGrading.None;

    /// <summary>
    /// Waehrend ein Regler gezogen wird, rechnet die Anzeige grob. Beim Loslassen
    /// laeuft dieser Zeitgeber ab und das volle Bild kommt zurueck.
    ///
    /// Ueber einen Zeitgeber und nicht direkt beim Loslassen, weil auch das Ziehen
    /// selbst Pausen hat: wer einen Punkt sucht, haelt zwischendurch inne, und dann
    /// soll das genaue Bild kommen, ohne dass er die Maus loslassen muesste.
    /// </summary>
    private DispatcherTimer? _coarseTimer;

    private bool _coarse;

    /// <summary>
    /// Wie grob waehrend des Ziehens gerechnet wird. Vier bedeutet ein Sechzehntel
    /// der Arbeit - gemessen faellt 1080p damit von 40 auf 4,5 ms.
    /// </summary>
    private const int CoarseStep = 4;

    private static readonly Color[] CurveColours =
    {
        Color.FromRgb(0xE8, 0xE8, 0xE8),      // gemeinsam
        Color.FromRgb(0xE0, 0x6C, 0x6C),      // Rot
        Color.FromRgb(0x6C, 0xD0, 0x86),      // Gruen
        Color.FromRgb(0x6C, 0x9C, 0xE0),      // Blau
    };

    private void SetUpCurves()
    {
        // Ein gespeicherter Stapel wird uebernommen, sonst einer angelegt. Die
        // Kurven sind darin das erste Werkzeug.
        var saved = _settings.Grading;
        if (saved is not null)
        {
            _grading.Tools.Clear();
            _grading.Tools.AddRange(saved.Tools);
        }

        _curves = _grading.Tools.OfType<CurvesTool>().FirstOrDefault() ?? new CurvesTool();
        if (!_grading.Tools.Contains(_curves)) _grading.Tools.Add(_curves);

        CurveField.Curve = _curves.Master;
        CurveField.LineColour = CurveColours[0];
        CurveField.Changed += OnCurveChanged;
        CurveField.Released += OnCurveReleased;

        _preparedGrading = _grading.Prepare();

        _coarseTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };

        _coarseTimer.Tick += (_, _) =>
        {
            _coarseTimer!.Stop();
            if (!_coarse) return;

            _coarse = false;
            RedrawCurrentFrame();
            UpdateHistogramFromCurrentFrame();
        };
    }

    /// <summary>Welcher Kanal im Feld liegt: 0 gemeinsam, 1 bis 3 die Farben.</summary>
    private void OnCurveChannelPicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton picked || picked.Tag is not string tag) return;
        if (!int.TryParse(tag, out int channel)) return;

        // Die vier Schalter sind eine Gruppe, aber ToggleButton kennt keine - das
        // Abwaehlen der uebrigen muss von Hand kommen.
        foreach (var button in new[] { CurveMasterButton, CurveRedButton, CurveGreenButton, CurveBlueButton })
        {
            if (!ReferenceEquals(button, picked)) button.IsChecked = false;
        }

        CurveField.Curve = channel switch
        {
            1 => _curves.Red,
            2 => _curves.Green,
            3 => _curves.Blue,
            _ => _curves.Master,
        };

        CurveField.LineColour = CurveColours[Math.Clamp(channel, 0, 3)];
        UpdateCurveBackground();
        CurveField.InvalidateVisual();
    }

    /// <summary>
    /// Die Kurve hat sich geaendert - das passiert waehrend des Ziehens vielfach je
    /// Sekunde. Deshalb wird grob gerechnet, bis eine Weile Ruhe war.
    /// </summary>
    private void OnCurveChanged()
    {
        _preparedGrading = _grading.Prepare();
        _settings.Grading = _grading;

        _coarse = true;
        _coarseTimer?.Stop();
        _coarseTimer?.Start();

        UpdateCurveBadge();
        RedrawCurrentFrame();
    }

    private void OnCurveReleased()
    {
        // Losgelassen: gleich das volle Bild, ohne die Ruhezeit abzuwarten.
        _coarseTimer?.Stop();

        if (!_coarse) return;

        _coarse = false;
        RedrawCurrentFrame();
        UpdateHistogramFromCurrentFrame();
    }

    private void OnResetCurveClicked(object sender, RoutedEventArgs e)
    {
        CurveField.Reset();
        ShowBar();
    }

    /// <summary>
    /// Die Verteilung als Hintergrund des Feldes - man soll sehen, worauf man zieht.
    /// Beim gemeinsamen Kanal die Luminanz, sonst der jeweilige Farbkanal.
    /// </summary>
    private void UpdateCurveBackground()
    {
        if (CurveField is null) return;

        CurveField.Background = ReferenceEquals(CurveField.Curve, _curves.Red) ? _histogram.Red
                              : ReferenceEquals(CurveField.Curve, _curves.Green) ? _histogram.Green
                              : ReferenceEquals(CurveField.Curve, _curves.Blue) ? _histogram.Blue
                              : _histogram.Luma;
    }

    /// <summary>Zeigt an, dass ausser den Reglern auch Werkzeuge wirken.</summary>
    private void UpdateCurveBadge()
    {
        if (CurveBadge is null) return;

        CurveBadge.Visibility = _grading.IsNeutral ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Wie grob gerade gerechnet wird: 1 heisst voll.</summary>
    private int CurrentStep => _coarse ? CoarseStep : 1;
}
