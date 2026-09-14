using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Das Feld, in dem eine Gradationskurve gezogen wird.
///
/// Wie <see cref="HistogramView"/> ein FrameworkElement mit eigener Zeichnung statt
/// eines zusammengesetzten Steuerelements: Es gibt nichts zu klicken ausser der
/// Kurve selbst, und die Punkte entstehen und verschwinden waehrend der Bedienung.
///
/// Die Rechnung dahinter steht in <see cref="CurveEditing"/> und wird dort geprueft -
/// hier bleibt das Zeichnen und das Verteilen der Mauszeiger.
/// </summary>
public sealed class CurveEditor : FrameworkElement
{
    private ToneCurve _curve = new();
    private int _dragging = -1;
    private int _hover = -1;

    public CurveEditor()
    {
        Height = 180;
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
    }

    /// <summary>Wird gerufen, sobald sich an der Kurve etwas geaendert hat.</summary>
    public event Action? Changed;

    /// <summary>Die bearbeitete Kurve. Wird nicht kopiert - Aenderungen wirken sofort.</summary>
    public ToneCurve Curve
    {
        get => _curve;
        set
        {
            _curve = value;
            _dragging = -1;
            _hover = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Die Farbe der Kurve - fuer die Kanalansichten Rot, Gruen und Blau.</summary>
    public Color LineColour { get; set; } = Color.FromRgb(0xE8, 0xE8, 0xE8);

    /// <summary>Die Verteilung als Hintergrund, damit man sieht, worauf man zieht.</summary>
    public int[]? Background { get; set; }

    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FrameBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private static readonly Brush FieldBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0x00, 0x00));
    private static readonly Brush HistogramBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

    static CurveEditor()
    {
        GridBrush.Freeze();
        FrameBrush.Freeze();
        FieldBrush.Freeze();
        HistogramBrush.Freeze();
    }

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        context.DrawRectangle(FieldBrush, null, new Rect(0, 0, w, h));

        DrawHistogram(context, w, h);

        // Gitter in Vierteln, dazu die Diagonale als Bezug: an ihr sieht man
        // sofort, wo die Kurve anhebt und wo sie absenkt.
        var gridPen = new Pen(GridBrush, 1);
        for (int i = 1; i < 4; i++)
        {
            double x = w * i / 4.0;
            double y = h * i / 4.0;
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        context.DrawLine(new Pen(GridBrush, 1), new Point(0, h), new Point(w, 0));
        context.DrawRectangle(null, new Pen(FrameBrush, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        DrawCurve(context, w, h);
        DrawPoints(context, w, h);
    }

    private void DrawHistogram(DrawingContext context, double w, double h)
    {
        var data = Background;
        if (data is null || data.Length == 0) return;

        int peak = 1;
        for (int i = 1; i < data.Length - 1; i++)
            if (data[i] > peak) peak = data[i];

        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);

            for (int i = 0; i < data.Length; i++)
            {
                // Wurzelskalierung wie im Histogramm darunter: eine einzelne hohe
                // Spitze wuerde den Rest sonst in den Boden druecken.
                double value = Math.Sqrt(Math.Min(1.0, data[i] / (double)peak));
                draw.LineTo(new Point(w * i / (data.Length - 1.0), h - value * h * 0.85), true, false);
            }

            draw.LineTo(new Point(w, h), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(HistogramBrush, null, geometry);
    }

    private void DrawCurve(DrawingContext context, double w, double h)
    {
        _curve.Prepare();

        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(0, h - _curve.Evaluate(0) * h), isFilled: false, isClosed: false);

            // Je Bildschirmspalte ein Punkt - feiner waere nicht zu sehen, groeber
            // zeigte die Kurve als Streckenzug.
            int steps = Math.Max(2, (int)w);
            for (int i = 1; i <= steps; i++)
            {
                float x = i / (float)steps;
                draw.LineTo(new Point(x * w, h - _curve.Evaluate(x) * h), true, false);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, new Pen(new SolidColorBrush(LineColour), 1.6), geometry);
    }

    private void DrawPoints(DrawingContext context, double w, double h)
    {
        var line = new SolidColorBrush(LineColour);

        for (int i = 0; i < _curve.Points.Count; i++)
        {
            var (x, y) = CurveEditing.ToCanvas(_curve.Points[i], w, h);
            bool active = i == _dragging || i == _hover;

            context.DrawEllipse(active ? line : Brushes.Transparent,
                                new Pen(line, active ? 2.0 : 1.4),
                                new Point(x, y), active ? 5.0 : 3.5, active ? 5.0 : 3.5);
        }
    }

    // ------------------------------------------------------------------- Maus

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var position = e.GetPosition(this);
        int found = CurveEditing.FindPoint(_curve.Points, position.X, position.Y, ActualWidth, ActualHeight);

        if (found < 0)
        {
            // Auf freier Flaeche entsteht ein neuer Punkt - und er wird gleich
            // gegriffen, damit man ihn in derselben Bewegung setzen kann.
            var wanted = CurveEditing.ToCurve(position.X, position.Y, ActualWidth, ActualHeight);
            found = CurveEditing.Insert(_curve.Points, wanted);
            if (found < 0) return;

            Notify();
        }

        _dragging = found;
        CaptureMouse();
        Focus();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);

        if (_dragging < 0)
        {
            int over = CurveEditing.FindPoint(_curve.Points, position.X, position.Y, ActualWidth, ActualHeight);
            if (over != _hover)
            {
                _hover = over;
                Cursor = over >= 0 ? Cursors.SizeAll : Cursors.Cross;
                InvalidateVisual();
            }

            return;
        }

        var wanted = CurveEditing.ToCurve(position.X, position.Y, ActualWidth, ActualHeight);
        var held = CurveEditing.Constrain(_curve.Points, _dragging, wanted);

        if (held != _curve.Points[_dragging])
        {
            _curve.Points[_dragging] = held;
            Notify();
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragging < 0) return;

        _dragging = -1;
        ReleaseMouseCapture();
        InvalidateVisual();

        // Beim Loslassen noch einmal melden: die Anzeige hat waehrend des Ziehens
        // grob gerechnet und soll jetzt das volle Bild zeigen.
        Released?.Invoke();
        e.Handled = true;
    }

    /// <summary>Wird beim Loslassen gerufen - das Zeichen, wieder voll zu rechnen.</summary>
    public event Action? Released;

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        var position = e.GetPosition(this);
        int found = CurveEditing.FindPoint(_curve.Points, position.X, position.Y, ActualWidth, ActualHeight);

        if (found >= 0 && CurveEditing.Remove(_curve.Points, found))
        {
            _hover = -1;
            Notify();
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Setzt die Kurve auf die Gerade zurueck.</summary>
    public void Reset()
    {
        _curve.Points.Clear();
        _curve.Points.Add(new CurvePoint(0, 0));
        _curve.Points.Add(new CurvePoint(1, 1));

        _dragging = -1;
        _hover = -1;
        Notify();
        InvalidateVisual();
    }

    private void Notify()
    {
        _curve.Prepare();
        Changed?.Invoke();
    }
}
