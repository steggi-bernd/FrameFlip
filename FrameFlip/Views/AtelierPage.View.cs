using System.Windows;
using System.Windows.Controls;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Zwei Dinge, die zum Beurteilen gehoeren und beim Bauen leicht vergessen werden:
/// das Original daneben und ein Blick aus der Naehe.
/// </summary>
public partial class AtelierPage
{
    /// <summary>True, solange das Original gezeigt wird.</summary>
    private bool _showingOriginal;

    /// <summary>0 heisst eingepasst; sonst der Massstab in Punkten je Bildpunkt.</summary>
    private double _zoom;

    /// <summary>
    /// Zeigt das Bild ohne jede Korrektur, solange die Maustaste haelt.
    ///
    /// Gedrueckt halten statt umschalten: Der Vergleich ist ein Blick und kein
    /// Zustand. Wer umschaltet, vergisst zurueckzuschalten und beurteilt dann
    /// minutenlang das falsche Bild - und merkt es, wenn ueberhaupt, an einer
    /// Einstellung, die sich nicht mehr erklaeren laesst.
    /// </summary>
    private void OnCompareDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_frame is null || _showingOriginal) return;

        _showingOriginal = true;
        Render();
    }

    private void OnCompareUp(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_showingOriginal) return;

        _showingOriginal = false;
        Render();
    }

    /// <summary>
    /// Zwischen Einpassen und 100 % umschalten.
    ///
    /// Zweistufig wie im Vorschaufenster, und mit derselben Beschriftung - wer den
    /// Knopf dort kennt, soll hier nicht raten muessen, was eine dritte Stufe
    /// bedeutet.
    ///
    /// 100 % ist dabei die eigentliche Stufe: Rauschen, Stufen in einem Verlauf und
    /// die Wirkung einer steilen Kurve lassen sich nur dort beurteilen, wo ein
    /// Bildpunkt auch ein Bildpunkt ist. Eingepasst sieht alles glatt aus, was die
    /// Skalierung glattgerechnet hat.
    /// </summary>
    private void OnZoomClicked(object sender, RoutedEventArgs e)
    {
        _zoom = _zoom == 0 ? 1.0 : 0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        var frame = _frame;

        if (frame is null || _zoom == 0)
        {
            Display.Stretch = System.Windows.Media.Stretch.Uniform;
            Display.Width = double.NaN;
            Display.Height = double.NaN;
            ZoomText.Text = Localization.Strings.T("S_ZoomFit");

            // Eingepasst gibt es nichts zu schieben; die Balken verschwinden.
            ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            ShowPlacement();
            return;
        }

        // Eingepasst gestreckt und nicht "None": Bei "None" zeichnet das Bildelement die
        // Bitmap in ihrer eigenen Groesse, egal wie gross das Element ist - das ging nur,
        // solange es neben dem Einpassen allein 100 % gab. Das Element ist genau so gross
        // wie das Bild im Massstab, also gibt es keine Raender, und die Umrechnung in
        // ImageHit liefert den Massstab aus der Elementgroesse.
        Display.Stretch = System.Windows.Media.Stretch.Uniform;
        Display.Width = frame.Width * _zoom;
        Display.Height = frame.Height * _zoom;
        ZoomText.Text = $"{_zoom * 100:0} %";

        ImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        ImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        // Bei 100 % und darueber soll ein Bildpunkt scharf stehen; die weiche
        // Skalierung wuerde genau das verwischen, was man sehen will.
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(
            Display, _zoom >= 1.0
                ? System.Windows.Media.BitmapScalingMode.NearestNeighbor
                : System.Windows.Media.BitmapScalingMode.HighQuality);

        // Der Greifrahmen rechnet in Punkten auf dem Element - beim Massstabwechsel
        // stimmt seine Umrechnung nicht mehr.
        ShowPlacement();
    }

    private void SetUpView() => ImageScroll.PreviewMouseWheel += OnViewportWheel;

    /// <summary>
    /// Das Mausrad zoomt - beim Verschieben und beim Pinsel, sonst nicht.
    ///
    /// Nur dort, weil es sonst anderem gehoert: ueber dem Graphen zoomt es den Graphen,
    /// und beim Zuschneiden, Waehlen und bei der Pipette rollt es wie bisher den
    /// Ausschnitt. Beim Pinsel stellt Strg + Rad den Abstand der Tupfer.
    /// </summary>
    private void OnViewportWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_frame is null || _tool is not (AtelierTool.Move or AtelierTool.Brush)) return;

        e.Handled = true;

        if (_tool == AtelierTool.Brush &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            StepBrushSpacing(e.Delta / 120.0);
            return;
        }

        WheelZoom(e.GetPosition(Display), e.GetPosition(ImageScroll), e.Delta / 120.0);
    }

    /// <summary>
    /// Zoomt um den Punkt unter dem Zeiger: Der Bildpunkt, auf den er zeigt, steht nach
    /// dem Schritt wieder unter ihm.
    ///
    /// Beim Herauszoomen rastet es an der Einpassung ein und zeigt das Bild wieder
    /// eingepasst und mittig - dieselbe Ansicht wie nach dem Oeffnen, nicht ein Bild,
    /// das zufaellig fast so gross ist und irgendwo liegt.
    /// </summary>
    /// <param name="onDisplay">Der Zeiger auf dem Bildelement.</param>
    /// <param name="inView">Derselbe Zeiger im sichtbaren Ausschnitt.</param>
    internal void WheelZoom(System.Windows.Point onDisplay, System.Windows.Point inView, double notches)
    {
        var frame = _frame;
        if (frame is null) return;

        double fit = FitScale();
        if (fit <= 0) return;

        double current = _zoom == 0 ? fit : _zoom;
        double next = ZoomSteps.Next(current, notches, fit);

        if (next <= fit * (1 + 1e-9))
        {
            if (_zoom == 0) return;

            _zoom = 0;
            ApplyZoom();
            return;
        }

        if (Math.Abs(next - current) < 1e-9) return;

        // Der Bildpunkt unter dem Zeiger, ungerundet - gerundet wanderte das Bild bei
        // starkem Zoom um bis zu einen Bildpunkt je Schritt.
        ImageHit.Exact(onDisplay.X, onDisplay.Y, Display.ActualWidth, Display.ActualHeight,
                       frame.Width, frame.Height, Display.Stretch == System.Windows.Media.Stretch.Uniform,
                       out double u, out double v);

        _zoom = next;
        ApplyZoom();

        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(u * next - inView.X);
        ImageScroll.ScrollToVerticalOffset(v * next - inView.Y);
    }

    /// <summary>Der Massstab des eingepassten Bildes - so wie das Bildelement es beim Einpassen zeigt.</summary>
    private double FitScale()
    {
        var frame = _frame;
        if (frame is null || frame.Width <= 0 || frame.Height <= 0) return 0;

        return Math.Min(ImageScroll.ActualWidth / frame.Width, ImageScroll.ActualHeight / frame.Height);
    }

    /// <summary>
    /// Was gerechnet werden soll: beim Vergleich nichts, sonst das Eingestellte.
    ///
    /// Das Original heisst hier wirklich ohne alles - auch ohne die Grundregler.
    /// Ein Vergleich, der die Haelfte der Korrektur stehenlaesst, beantwortet keine
    /// Frage, die jemand gestellt haette.
    /// </summary>
    private (ImageAdjustments Adjustments, PreparedGrading Grading) Current()
        => _showingOriginal
            ? (ImageAdjustments.Neutral, PreparedGrading.None)
            : (_finalAdjustments, _finalGrading);

    /// <summary>
    /// Welches Bild gezeichnet wird: das zusammengesetzte, oder beim Vergleich das
    /// der Datei.
    ///
    /// Auch die Schichtung faellt beim Vergleich weg. Sie ist ein Eingriff wie jeder
    /// andere - ein "Original", das eine selbstgebaute Mischung aus acht Passen
    /// zeigt, waere keines.
    /// </summary>
    private FloatFrame? Shown() => _showingOriginal ? _base ?? _frame : _frame;
}
