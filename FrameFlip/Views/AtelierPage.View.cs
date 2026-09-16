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

    /// <summary>1 heisst eingepasst; sonst der Massstab in Bildpunkten je Punkt.</summary>
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

        Display.Stretch = System.Windows.Media.Stretch.None;
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
