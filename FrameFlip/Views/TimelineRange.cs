using System.Windows;
using System.Windows.Media;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen diese Typen genauso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Zeigt den In/Out-Bereich auf der Zeitleiste.
///
/// Liegt bewusst UEBER dem Scrubber statt darunter wie die Lueckenmarkierung: Der
/// gefuellte Teil der Spur ist deckend, eine Markierung darunter waere links vom
/// Griff unsichtbar - also genau dort, wo der In-Punkt meistens steht.
///
/// Gezeigt wird beides, was man wissen will: wo die Grenzen liegen (zwei helle
/// Striche) und was dadurch wegfaellt (der abgedunkelte Rest). Nur Striche waeren
/// mehrdeutig - man saehe nicht, welche Seite gemeint ist.
///
/// Die Zeitleiste spannt wie die Lueckenmarkierung den NUMMERNbereich auf, nicht die
/// Listenposition. Deshalb kommen auch hier Framenummern herein, keine Indizes.
/// </summary>
public sealed class TimelineRange : FrameworkElement
{
    private int _startNumber;
    private int _endNumber;
    private int _firstNumber = -1;
    private int _lastNumber = -1;

    /// <summary>
    /// Waagerechter Rand, der frei bleibt. Entspricht der halben Thumb-Breite des
    /// Sliders - ohne ihn laegen die Striche neben den Grenzen, an denen sie stehen.
    /// </summary>
    public double EdgePadding { get; set; } = 6.5;

    /// <summary>Hoehe der Spur, die abgedunkelt wird.</summary>
    public double TrackHeight { get; set; } = 4.0;

    /// <summary>Hoehe der Grenzstriche. Ragt ueber die Spur hinaus, damit sie auffallen.</summary>
    public double MarkerHeight { get; set; } = 12.0;

    public double MarkerWidth { get; set; } = 2.0;

    /// <summary>Deckt ab, was ausserhalb des Bereichs liegt.</summary>
    public Brush VeilBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x11, 0x14));

    public Brush MarkerBrush { get; set; } = Brushes.White;

    public bool HasRange => _firstNumber >= 0 && _lastNumber >= 0;

    public void SetSequence(int startNumber, int endNumber)
    {
        _startNumber = startNumber;
        _endNumber = endNumber;
        InvalidateVisual();
    }

    /// <param name="firstNumber">Framenummer des In-Punkts, oder -1 fuer keinen Bereich.</param>
    public void SetRange(int firstNumber, int lastNumber)
    {
        if (firstNumber == _firstNumber && lastNumber == _lastNumber) return;

        _firstNumber = firstNumber;
        _lastNumber = lastNumber;
        InvalidateVisual();
    }

    public void Clear() => SetRange(-1, -1);

    protected override Size MeasureOverride(Size availableSize) => new(0, MarkerHeight);

    protected override void OnRender(DrawingContext context)
    {
        if (!HasRange) return;

        int span = _endNumber - _startNumber + 1;
        if (span <= 1) return;

        double usable = ActualWidth - 2 * EdgePadding;
        if (usable <= 0) return;

        double perFrame = usable / span;

        // Der Out-Punkt schliesst seinen eigenen Frame ein, deshalb eine Breite
        // weiter rechts - sonst faellt das letzte Bild optisch aus dem Bereich.
        double left = EdgePadding + (_firstNumber - _startNumber) * perFrame;
        double right = EdgePadding + (_lastNumber - _startNumber + 1) * perFrame;

        left = Math.Clamp(left, EdgePadding, EdgePadding + usable);
        right = Math.Clamp(right, left, EdgePadding + usable);

        double trackTop = (ActualHeight - TrackHeight) / 2;
        double markerTop = (ActualHeight - MarkerHeight) / 2;

        // Was wegfaellt, wird abgedunkelt - nur auf der Spur, nicht auf voller Hoehe:
        // ein Balken ueber die ganze Leiste wirkte wie ein zweites Bedienelement.
        if (left > EdgePadding)
            context.DrawRoundedRectangle(VeilBrush, null,
                new Rect(EdgePadding, trackTop, left - EdgePadding, TrackHeight), 2, 2);

        double tailWidth = EdgePadding + usable - right;
        if (tailWidth > 0)
            context.DrawRoundedRectangle(VeilBrush, null,
                new Rect(right, trackTop, tailWidth, TrackHeight), 2, 2);

        // Die Grenzen selbst. Nach innen versetzt, damit beide Striche auch bei einem
        // Bereich aus einem einzigen Frame noch nebeneinander stehen statt ineinander.
        DrawMarker(context, left, markerTop);
        DrawMarker(context, right - MarkerWidth, markerTop);
    }

    private void DrawMarker(DrawingContext context, double x, double top)
    {
        context.DrawRoundedRectangle(MarkerBrush, null,
            new Rect(x, top, MarkerWidth, MarkerHeight), 1, 1);
    }
}
