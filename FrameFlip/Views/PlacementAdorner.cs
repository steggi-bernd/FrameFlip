using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FrameFlip.Views;

/// <summary>
/// Der Greifrahmen ueber dem Bild: anfassen und ziehen, an den Ecken groesser und
/// kleiner.
///
/// Er liegt als eigene Flaeche GENAU ueber dem Bildelement - dieselben Masse,
/// dieselbe Umrechnung. Anders herum, etwa als Aufsatz im Fenster, muesste er die
/// Lage des Bildes nachrechnen, und ein Rahmen, der einen Bildpunkt daneben liegt,
/// laesst einen an einer Ecke ziehen, die nicht dort ist, wo sie aussieht.
///
/// Gezeichnet wird zurueckhaltend: eine duenne helle Linie mit dunklem Schatten
/// darunter, damit sie auf hellem wie auf dunklem Bild steht, und vier kleine
/// Quadrate. Ein kraeftiger Rahmen wuerde genau das ueberdecken, was man beurteilen
/// will.
/// </summary>
public sealed class PlacementAdorner : FrameworkElement
{
    private LayerTransform? _transform;
    private int _layerWidth, _layerHeight, _canvasWidth, _canvasHeight;
    private bool _uniform;

    private PlacementDrag _drag;
    private DragHandle _hover;

    public PlacementAdorner()
    {
        IsHitTestVisible = false;

        // Ohne Hintergrund traefe die Maus nur die gezeichneten Linien. Ein
        // durchsichtiger Hintergrund ist die uebliche Loesung und die einzige, bei
        // der sich eine Flaeche anfassen laesst.
        Background = new SolidColorBrush(Colors.Transparent);
    }

    private Brush Background { get; }

    /// <summary>Die Platzierung hat sich geaendert. <c>interim</c> heisst: es wird gerade gezogen.</summary>
    public event Action<LayerTransform, bool>? Changed;

    /// <summary>
    /// Welche Ebene der Rahmen zeigt. Null blendet ihn aus und macht ihn fuer die
    /// Maus durchlaessig - ein unsichtbarer Rahmen, der Klicks schluckt, waere der
    /// unangenehmste Zustand von allen.
    /// </summary>
    public void Track(LayerTransform? transform, int layerWidth, int layerHeight,
                      int canvasWidth, int canvasHeight, bool uniform)
    {
        _transform = transform;
        _layerWidth = layerWidth;
        _layerHeight = layerHeight;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _uniform = uniform;

        IsHitTestVisible = transform is not null;
        if (transform is null) _drag = default;

        InvalidateVisual();
    }

    /// <summary>Beendet ein laufendes Ziehen - etwa, wenn woanders etwas passiert.</summary>
    public void Cancel()
    {
        if (!_drag.IsActive) return;

        _drag = default;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ Zeichnen

    private static readonly Pen Line = Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen Shadow = Frozen(Color.FromArgb(0x90, 0x00, 0x00, 0x00), 3);
    private static readonly Brush Handle = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Brush HandleEdge = Frozen(Color.FromArgb(0xB0, 0x00, 0x00, 0x00));
    private static readonly Brush Hot = Frozen(Color.FromRgb(0x62, 0xB6, 0xFF));

    /// <summary>Die halbe Kantenlaenge eines Griffs in Punkten.</summary>
    private const double GripSize = 4;

    /// <summary>Wie weit ein Griff fasst. Grosszuegiger als er aussieht - vier Punkte trifft niemand.</summary>
    private const double GripReach = 9;

    protected override void OnRender(DrawingContext context)
    {
        // Ein durchsichtiges Rechteck ueber alles: Ohne es faellt die Maus durch.
        context.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_transform is null) return;
        if (!Corners(out var topLeft, out var bottomRight)) return;

        var rect = new Rect(topLeft, bottomRight);

        context.DrawRectangle(null, Shadow, rect);
        context.DrawRectangle(null, Line, rect);

        Grip(context, rect.Left, rect.Top, DragHandle.TopLeft);
        Grip(context, rect.Right, rect.Top, DragHandle.TopRight);
        Grip(context, rect.Left, rect.Bottom, DragHandle.BottomLeft);
        Grip(context, rect.Right, rect.Bottom, DragHandle.BottomRight);
    }

    private void Grip(DrawingContext context, double x, double y, DragHandle which)
    {
        bool lit = _drag.IsActive ? _drag.Handle == which : _hover == which;

        context.DrawRectangle(lit ? Hot : Handle, new Pen(HandleEdge, 1),
                              new Rect(x - GripSize, y - GripSize, GripSize * 2, GripSize * 2));
    }

    /// <summary>Die beiden Ecken der Ebene, in Punkten auf dieser Flaeche.</summary>
    private bool Corners(out Point topLeft, out Point bottomRight)
    {
        topLeft = bottomRight = default;

        if (_transform is null || _canvasWidth <= 0 || _canvasHeight <= 0) return false;

        PlacementDrag.Basis(_layerWidth, _layerHeight, _canvasWidth, _canvasHeight,
                            out float baseWidth, out float baseHeight);

        PlacementDrag.Region(_transform, baseWidth, baseHeight,
                             out float cx, out float cy, out float hw, out float hh);

        // Von Anteilen in Bildpunkte, von dort auf die Flaeche - ueber denselben Weg,
        // den auch der Klick nimmt.
        //
        // Beide Aufrufe einzeln und nicht mit && verkettet: Der Kurzschluss liesse
        // die zweiten beiden Werte ungesetzt, und der Compiler sagt das zu Recht.
        if (!ImageHit.PointAt((cx - hw) * _canvasWidth, (cy - hh) * _canvasHeight,
                              ActualWidth, ActualHeight, _canvasWidth, _canvasHeight,
                              _uniform, out double left, out double top))
        {
            return false;
        }

        if (!ImageHit.PointAt((cx + hw) * _canvasWidth, (cy + hh) * _canvasHeight,
                              ActualWidth, ActualHeight, _canvasWidth, _canvasHeight,
                              _uniform, out double right, out double bottom))
        {
            return false;
        }

        topLeft = new Point(left, top);
        bottomRight = new Point(right, bottom);

        return true;
    }

    // ---------------------------------------------------------------- Bedienung

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_transform is null) return;
        if (!Fraction(e.GetPosition(this), out float x, out float y)) return;

        PlacementDrag.Basis(_layerWidth, _layerHeight, _canvasWidth, _canvasHeight,
                            out float baseWidth, out float baseHeight);

        _drag = PlacementDrag.Begin(_transform, baseWidth, baseHeight, x, y, (float)Reach());

        if (!_drag.IsActive) return;

        e.Handled = true;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_transform is null) return;
        if (!Fraction(e.GetPosition(this), out float x, out float y)) return;

        if (!_drag.IsActive)
        {
            PlacementDrag.Basis(_layerWidth, _layerHeight, _canvasWidth, _canvasHeight,
                                out float baseWidth, out float baseHeight);

            var over = PlacementDrag.HandleAt(_transform, baseWidth, baseHeight, x, y, (float)Reach());

            if (over != _hover)
            {
                _hover = over;
                InvalidateVisual();
            }

            Cursor = over switch
            {
                DragHandle.TopLeft or DragHandle.BottomRight => Cursors.SizeNWSE,
                DragHandle.TopRight or DragHandle.BottomLeft => Cursors.SizeNESW,
                DragHandle.Body => Cursors.SizeAll,
                _ => null,
            };

            return;
        }

        _transform = _drag.To(x, y);

        Changed?.Invoke(_transform, true);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_drag.IsActive) return;

        _drag = default;
        ReleaseMouseCapture();

        e.Handled = true;

        // Beim Loslassen noch einmal, diesmal endgueltig: Das ist das Zeichen, wieder
        // voll zu rechnen und die Einstellung festzuhalten.
        if (_transform is not null) Changed?.Invoke(_transform, false);

        InvalidateVisual();
    }

    /// <summary>
    /// Der Fangbereich in Anteilen - aus Punkten auf dem Schirm umgerechnet.
    ///
    /// In Punkten und nicht in Anteilen gedacht, weil die Maus in Punkten zielt: Auf
    /// einem eingepassten 4K-Bild waere ein fester Anteil ein Fangbereich von
    /// Haaresbreite.
    /// </summary>
    private double Reach()
        => ActualWidth <= 0 ? 0.01 : GripReach / ActualWidth;

    /// <summary>Ein Punkt auf dieser Flaeche als Anteil der Leinwand.</summary>
    private bool Fraction(Point point, out float x, out float y)
    {
        x = y = 0;

        if (_canvasWidth <= 0 || _canvasHeight <= 0) return false;

        double scale = 1.0;

        if (_uniform)
        {
            scale = Math.Min(ActualWidth / _canvasWidth, ActualHeight / _canvasHeight);
            if (scale <= 0) return false;
        }

        double left = (ActualWidth - _canvasWidth * scale) / 2;
        double top = (ActualHeight - _canvasHeight * scale) / 2;

        // NICHT auf das Bild begrenzt: Beim Ziehen darf eine Ebene ueber den Rand
        // hinauslaufen, und die Maus darf dabei aus dem Bild geraten.
        x = (float)((point.X - left) / scale / _canvasWidth);
        y = (float)((point.Y - top) / scale / _canvasHeight);

        return true;
    }

    private static Pen Frozen(Color colour, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(colour), thickness);
        pen.Freeze();

        return pen;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();

        return brush;
    }
}
