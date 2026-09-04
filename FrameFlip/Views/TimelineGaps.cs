using System.Windows;
using System.Windows.Media;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen diese Typen genauso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Zeichnet fehlende Frames als Markierung hinter dem Scrubber.
///
/// Die Zeitleiste spannt den NUMMERNBEREICH auf, nicht die Listenposition. Nur so
/// ist eine Luecke ueberhaupt darstellbar: fehlende Frames stehen ja gerade nicht in
/// der Liste, und ueber den Index waeren 250 gerenderte von 500 Frames von einer
/// vollstaendigen Sequenz nicht zu unterscheiden.
///
/// Eigenes Element statt eines Slider-Templates, damit die Geometrie im Fenster
/// sichtbar bleibt: Streifen und Slider liegen in derselben Zelle mit derselben
/// Randbreite und decken sich dadurch exakt.
/// </summary>
public sealed class TimelineGaps : FrameworkElement
{
    private int _startNumber;
    private int _endNumber;
    private IReadOnlyList<int> _missing = Array.Empty<int>();

    /// <summary>
    /// Waagerechter Rand, der frei bleibt. Entspricht der halben Thumb-Breite des
    /// Sliders - ohne ihn laegen die Markierungen um sechs Pixel daneben.
    /// </summary>
    public double EdgePadding { get; set; } = 6.0;

    public Brush GapBrush { get; set; } = Brushes.IndianRed;

    public double StripeHeight { get; set; } = 4.0;

    public void SetSequence(int startNumber, int endNumber, IReadOnlyList<int> missing)
    {
        _startNumber = startNumber;
        _endNumber = endNumber;
        _missing = missing;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, StripeHeight);

    protected override void OnRender(DrawingContext context)
    {
        if (_missing.Count == 0) return;

        int span = _endNumber - _startNumber + 1;
        if (span <= 1) return;

        double usable = ActualWidth - 2 * EdgePadding;
        if (usable <= 0) return;

        double y = (ActualHeight - StripeHeight) / 2;
        double perFrame = usable / span;

        // Zusammenhaengende Luecken als einen Balken zeichnen, sonst entstehen bei
        // langen Ausfaellen hunderte Rechtecke nebeneinander.
        for (int i = 0; i < _missing.Count;)
        {
            int j = i;
            while (j + 1 < _missing.Count && _missing[j + 1] == _missing[j] + 1) j++;

            double left = EdgePadding + (_missing[i] - _startNumber) * perFrame;
            double width = (_missing[j] - _missing[i] + 1) * perFrame;

            // Eine einzelne fehlende Nummer waere sonst schmaler als ein Pixel und
            // damit unsichtbar - genau die will man aber sehen.
            if (width < 2.0) { left -= (2.0 - width) / 2; width = 2.0; }

            context.DrawRectangle(GapBrush, null, new Rect(left, y, width, StripeHeight));
            i = j + 1;
        }
    }
}
