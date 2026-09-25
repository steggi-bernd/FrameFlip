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
public sealed partial class PlacementAdorner : FrameworkElement
{
    private LayerTransform? _transform;
    private int _layerWidth, _layerHeight, _canvasWidth, _canvasHeight;
    private bool _uniform;

    private PlacementDrag _drag;
    private DragHandle _hover;

    /// <summary>
    /// Was der Rahmen gerade ist: der Griff zum Verschieben oder der zum Zuschneiden.
    ///
    /// Beides ist derselbe Rahmen an derselben Stelle mit derselben Umrechnung - nur
    /// schreiben die Griffe auf andere Werte. Ein zweites Element daneben haette die
    /// Umrechnung ein zweites Mal gebraucht, und zwei Umrechnungen laufen frueher
    /// oder spaeter um einen Bildpunkt auseinander.
    /// </summary>
    public AdornerMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;

            _mode = value;

            Cancel();
            InvalidateVisual();
        }
    }

    private AdornerMode _mode = AdornerMode.Place;

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

        if (_mode == AdornerMode.Paint)
        {
            RenderPaint(context);
            return;
        }

        if (_transform is null || _canvasWidth <= 0) return;

        if (_mode == AdornerMode.Crop)
        {
            RenderCrop(context);
            return;
        }

        var box = Box();
        box.Corners(out float x0, out float y0, out float x1, out float y1,
                    out float x2, out float y2, out float x3, out float y3);

        var a = Screen(x0, y0);
        var b = Screen(x1, y1);
        var c = Screen(x2, y2);
        var d = Screen(x3, y3);

        // Als Linienzug und nicht als Rechteck: Gedreht ist es keines mehr.
        var shape = new StreamGeometry();

        using (var draw = shape.Open())
        {
            draw.BeginFigure(a, false, true);
            draw.PolyLineTo(new[] { b, c, d }, true, false);
        }

        shape.Freeze();

        context.DrawGeometry(null, Shadow, shape);
        context.DrawGeometry(null, Line, shape);

        // Der Drehgriff sitzt ueber der oberen Kante und dreht mit.
        box.RotateGrip(PlacementDrag.RotateDistance, out float rx, out float ry);

        var grip = Screen(rx, ry);
        var top = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        context.DrawLine(Shadow, top, grip);
        context.DrawLine(Line, top, grip);

        context.DrawEllipse(_drag.IsActive
                                ? _drag.Handle == DragHandle.Rotate ? Hot : Handle
                                : _hover == DragHandle.Rotate ? Hot : Handle,
                            new Pen(HandleEdge, 1), grip, GripSize + 1, GripSize + 1);

        Grip(context, a, DragHandle.TopLeft);
        Grip(context, b, DragHandle.TopRight);
        Grip(context, c, DragHandle.BottomRight);
        Grip(context, d, DragHandle.BottomLeft);
    }

    private void Grip(DrawingContext context, Point at, DragHandle which)
    {
        bool lit = _drag.IsActive ? _drag.Handle == which : _hover == which;

        context.DrawRectangle(lit ? Hot : Handle, new Pen(HandleEdge, 1),
                              new Rect(at.X - GripSize, at.Y - GripSize, GripSize * 2, GripSize * 2));
    }

    /// <summary>Die Lage der Ebene, in Bildpunkten der Leinwand.</summary>
    private PlacementDrag.Frame Box()
        => PlacementDrag.Region(_transform!, _layerWidth, _layerHeight, _canvasWidth, _canvasHeight);

    /// <summary>Von einem Bildpunkt der Leinwand auf einen Punkt dieser Flaeche.</summary>
    private Point Screen(float imageX, float imageY)
    {
        ImageHit.PointAt(imageX, imageY, ActualWidth, ActualHeight,
                         _canvasWidth, _canvasHeight, _uniform, out double x, out double y);

        return new Point(x, y);
    }

    // ---------------------------------------------------------------- Bedienung

    /// <summary>
    /// Rechts nimmt weg - und zwar OHNE Alt.
    ///
    /// Alt war die naheliegende Wahl und die falsche: Windows haelt Alt fuer den
    /// Anfang eines Menuebefehls und schickt waehrenddessen einen eigenen Strom von
    /// Meldungen durch das Fenster. Das Radieren hing daran sichtbar hinterher,
    /// waehrend das Auftragen daneben fluessig lief - derselbe Rechenweg, dieselbe
    /// Maske, nur eine gedrueckte Taste Unterschied.
    ///
    /// Die rechte Taste hat das Problem nicht, ist in jedem Zeichenprogramm ohnehin
    /// die zweite Farbe und kostet keinen Finger an der Tastatur. Alt tut es
    /// weiterhin - wer es gewohnt ist, soll es behalten duerfen.
    /// </summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_mode != AdornerMode.Paint)
        {
            base.OnMouseRightButtonDown(e);
            return;
        }

        if (KnobPressed(e, BrushKnob.Hardness)) return;

        if (Canvas(e.GetPosition(this), out float px, out float py)) PaintDown(e, px, py, erase: true);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_mode == AdornerMode.Paint)
        {
            if (Knob != BrushKnob.None)
            {
                EndKnob();
                e.Handled = true;
                return;
            }

            PaintUp(e);
            return;
        }

        base.OnMouseRightButtonUp(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_mode == AdornerMode.Paint)
        {
            if (KnobPressed(e, BrushKnob.Size)) return;

            if (Canvas(e.GetPosition(this), out float px, out float py)) PaintDown(e, px, py);
            return;
        }

        if (_transform is null) return;
        if (!Canvas(e.GetPosition(this), out float x, out float y)) return;

        if (_mode == AdornerMode.Crop)
        {
            CropDown(e, x, y);
            return;
        }

        // Doppelklick auf die Flaeche setzt die Platzierung zurueck: mittig, volle
        // Groesse, ungedreht.
        //
        // Es gibt den Knopf dafuer schon, aber er steht im Ebenenstreifen neben der
        // Ueberschrift "Platzierung", und dieser Abschnitt ist zugeklappt. Wer eine
        // Ebene verschoben hat und sie zurueckhaben will, sucht dort, wo er sie
        // verschoben hat - im Bild. Derselbe Griff, an der Stelle, an der die Frage
        // aufkommt.
        if (e.ClickCount == 2 &&
            PlacementDrag.HandleAt(Box(), x, y, (float)Reach()) != DragHandle.None)
        {
            Cancel();

            _transform = new LayerTransform();
            Changed?.Invoke(_transform, false);

            e.Handled = true;
            InvalidateVisual();

            return;
        }

        _drag = PlacementDrag.Begin(_transform, Box(), x, y, (float)Reach());

        if (!_drag.IsActive) return;

        e.Handled = true;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mode == AdornerMode.Paint)
        {
            if (Knob != BrushKnob.None)
            {
                MoveKnob(e.GetPosition(this));
                return;
            }

            if (Canvas(e.GetPosition(this), out float px, out float py)) PaintMove(px, py);
            return;
        }

        if (_transform is null) return;
        if (!Canvas(e.GetPosition(this), out float x, out float y)) return;

        if (_mode == AdornerMode.Crop)
        {
            CropMove(x, y);
            return;
        }

        if (!_drag.IsActive)
        {
            var over = PlacementDrag.HandleAt(Box(), x, y, (float)Reach());

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
                DragHandle.Rotate => Cursors.Hand,
                _ => null,
            };

            return;
        }

        _transform = _drag.To(x, y, _canvasWidth, _canvasHeight);

        Changed?.Invoke(_transform, true);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mode == AdornerMode.Paint)
        {
            if (Knob != BrushKnob.None)
            {
                EndKnob();
                e.Handled = true;
                return;
            }

            PaintUp(e);
            return;
        }

        if (_mode == AdornerMode.Crop)
        {
            CropUp(e);
            return;
        }

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
    /// Der Fangbereich in Bildpunkten der Leinwand - aus Punkten auf dem Schirm
    /// umgerechnet.
    ///
    /// In Schirmpunkten gedacht, weil die Maus darin zielt: Auf einem eingepassten
    /// 4K-Bild waere ein fester Abstand in Bildpunkten ein Fangbereich von
    /// Haaresbreite.
    /// </summary>
    private double Reach()
    {
        double scale = ImageHit.Scale(ActualWidth, ActualHeight,
                                      _canvasWidth, _canvasHeight, _uniform);

        return scale <= 0 ? GripReach : GripReach / scale;
    }

    /// <summary>Ein Punkt dieser Flaeche in Bildpunkten der Leinwand.</summary>
    private bool Canvas(Point point, out float x, out float y)
    {
        x = y = 0;

        // Ueber denselben Helfer wie das Zeichnen - siehe ImageHit.Scale. Hier stand
        // frueher eine eigene Rechnung mit derselben Formel, und zwei Fassungen
        // derselben Abbildung laufen frueher oder spaeter auseinander.
        //
        // NICHT auf das Bild begrenzt: Beim Ziehen darf eine Ebene ueber den Rand
        // hinauslaufen, und die Maus darf dabei aus dem Bild geraten.
        if (!ImageHit.Exact(point.X, point.Y, ActualWidth, ActualHeight,
                            _canvasWidth, _canvasHeight, _uniform,
                            out double ex, out double ey))
        {
            return false;
        }

        x = (float)ex;
        y = (float)ey;

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
