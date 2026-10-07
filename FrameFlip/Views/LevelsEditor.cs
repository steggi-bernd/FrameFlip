using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Die Tonwertkorrektur am Histogramm: oben die Verteilung, darunter die drei Anfasser des
/// Eingangs (Schwarz, Grau, Weiss), ein Verlauf und die beiden des Ausgangs.
///
/// Wie <see cref="CurveEditor"/> eine eigene Zeichnung statt zusammengesetzter Regler: Die
/// Anfasser stehen genau unter dem Ton, den sie setzen, und man sieht beim Ziehen, wo das
/// Bild anfaengt und aufhoert. Was links vom Schwarzpunkt und rechts vom Weisspunkt liegt,
/// ist abgedunkelt - dort wird abgeschnitten.
/// </summary>
public sealed class LevelsEditor : FrameworkElement
{
    private LevelsChannel _channel = new();

    /// <summary>Welcher Anfasser gezogen wird - oder keiner.</summary>
    private Handle _dragging = Handle.None;

    public LevelsEditor()
    {
        Height = 150;
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    /// <summary>Die Anfasser: drei fuer den Eingang, zwei fuer den Ausgang.</summary>
    public enum Handle { None, InBlack, Gray, InWhite, OutBlack, OutWhite }

    /// <summary>Waehrend gezogen wird - eine grobe Vorschau reicht.</summary>
    public event Action? Changed;

    /// <summary>Losgelassen - jetzt darf der Rest nachziehen.</summary>
    public event Action? Released;

    /// <summary>Der bearbeitete Kanal. Wird nicht kopiert - Aenderungen wirken sofort.</summary>
    public LevelsChannel Channel
    {
        get => _channel;
        set
        {
            _channel = value;
            _dragging = Handle.None;
            InvalidateVisual();
        }
    }

    /// <summary>Die Verteilung als Hintergrund - die gemeinsame oder die eines Kanals.</summary>
    public int[]? Background { get; set; }

    /// <summary>Die Farbe der Verteilung - fuer die Kanalansichten Rot, Gruen und Blau.</summary>
    public Color HistogramColour { get; set; } = Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF);

    // Die Hoehen der Streifen, von oben nach unten.
    private const double InputStrip = 14, Ramp = 8, OutputStrip = 14, Gaps = 4;

    private double HistogramHeight => Math.Max(20, ActualHeight - InputStrip - Ramp - OutputStrip - Gaps * 2);

    private double Inset => 7;

    private double Usable => Math.Max(1, ActualWidth - 2 * Inset);

    private double X(float value) => Inset + Math.Clamp(value, 0f, 1f) * Usable;

    private float ValueAt(double x) => (float)Math.Clamp((x - Inset) / Usable, 0, 1);

    private static readonly Brush FieldBrush = Frozen(Color.FromArgb(0x28, 0x00, 0x00, 0x00));
    private static readonly Brush FrameBrush = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ClipBrush = Frozen(Color.FromArgb(0x70, 0x00, 0x00, 0x00));
    private static readonly Brush GuideBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth, h = HistogramHeight;
        if (w <= 2 * Inset + 1 || h <= 1) return;

        // Die Verteilung.
        var field = new Rect(Inset, 0, Usable, h);
        context.DrawRectangle(FieldBrush, null, field);
        DrawHistogram(context, field);

        // Was abgeschnitten wird, abgedunkelt - und Linien an den drei Eingangspunkten.
        double black = X(_channel.InBlack), white = X(_channel.InWhite);
        context.DrawRectangle(ClipBrush, null, new Rect(Inset, 0, Math.Max(0, black - Inset), h));
        context.DrawRectangle(ClipBrush, null, new Rect(white, 0, Math.Max(0, Inset + Usable - white), h));

        var guide = new Pen(GuideBrush, 1);
        context.DrawLine(guide, new Point(black, 0), new Point(black, h));
        context.DrawLine(guide, new Point(white, 0), new Point(white, h));
        context.DrawRectangle(null, new Pen(FrameBrush, 1), new Rect(Inset + 0.5, 0.5, Usable - 1, h - 1));

        // Eingang: Schwarz, Grau, Weiss.
        double inTop = h + Gaps;
        Triangle(context, black, inTop, Colors.Black, _dragging == Handle.InBlack);
        Triangle(context, GrayX(), inTop, Color.FromRgb(0x80, 0x80, 0x80), _dragging == Handle.Gray);
        Triangle(context, white, inTop, Colors.White, _dragging == Handle.InWhite);

        // Der Verlauf zwischen den Streifen, wie der Ausgang ihn abbildet.
        double rampTop = inTop + InputStrip;
        var ramp = new LinearGradientBrush(Colors.Black, Colors.White, 0);
        ramp.Freeze();
        context.DrawRectangle(ramp, new Pen(FrameBrush, 1), new Rect(Inset, rampTop, Usable, Ramp));

        // Ausgang: Schwarz und Weiss.
        double outTop = rampTop + Ramp + Gaps;
        Triangle(context, X(_channel.OutBlack), outTop, Colors.Black, _dragging == Handle.OutBlack);
        Triangle(context, X(_channel.OutWhite), outTop, Colors.White, _dragging == Handle.OutWhite);
    }

    private double GrayX()
    {
        double black = X(_channel.InBlack), white = X(_channel.InWhite);
        return black + (white - black) * LevelsChannel.GrayPosition(_channel.Gamma);
    }

    private void DrawHistogram(DrawingContext context, Rect field)
    {
        var data = Background;
        if (data is null || data.Length < 2) return;

        int peak = 1;
        for (int i = 1; i < data.Length - 1; i++)
            if (data[i] > peak) peak = data[i];

        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(field.Left, field.Bottom), isFilled: true, isClosed: true);

            for (int i = 0; i < data.Length; i++)
            {
                // Wurzelskalierung wie im Histogramm und unter der Kurve.
                double value = Math.Sqrt(Math.Min(1.0, data[i] / (double)peak));
                draw.LineTo(new Point(field.Left + field.Width * i / (data.Length - 1.0),
                                      field.Bottom - value * field.Height * 0.9), true, false);
            }

            draw.LineTo(new Point(field.Right, field.Bottom), true, false);
        }

        geometry.Freeze();

        var fill = new SolidColorBrush(HistogramColour);
        fill.Freeze();
        context.DrawGeometry(fill, null, geometry);
    }

    private static void Triangle(DrawingContext context, double x, double top, Color colour, bool active)
    {
        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(x, top), isFilled: true, isClosed: true);
            draw.LineTo(new Point(x + 6, top + InputStrip - 2), true, false);
            draw.LineTo(new Point(x - 6, top + InputStrip - 2), true, false);
        }

        geometry.Freeze();

        var fill = new SolidColorBrush(colour);
        fill.Freeze();

        var edge = new Pen(active ? new SolidColorBrush(Color.FromRgb(0xA4, 0x7B, 0xF0)) : FrameBrush, active ? 1.6 : 1);
        context.DrawGeometry(fill, edge, geometry);
    }

    // ------------------------------------------------------------------ Maus

    /// <summary>Welcher Anfasser an einer Stelle liegt - der naechste in seinem Streifen, sonst keiner.</summary>
    public Handle HandleAt(Point point)
    {
        double h = HistogramHeight;
        double inTop = h + Gaps, rampTop = inTop + InputStrip, outTop = rampTop + Ramp + Gaps;

        (Handle Handle, double X)[] candidates;

        // Die Eingangsanfasser auch aus dem Histogramm heraus - man zielt dort auf den Ton.
        if (point.Y < rampTop)
        {
            candidates = new[]
            {
                (Handle.InBlack, X(_channel.InBlack)),
                (Handle.Gray, GrayX()),
                (Handle.InWhite, X(_channel.InWhite)),
            };
        }
        else if (point.Y >= rampTop + Ramp - 2 && point.Y <= outTop + OutputStrip + 4)
        {
            candidates = new[] { (Handle.OutBlack, X(_channel.OutBlack)), (Handle.OutWhite, X(_channel.OutWhite)) };
        }
        else
        {
            return Handle.None;
        }

        var best = candidates.OrderBy(c => Math.Abs(c.X - point.X)).First();
        return Math.Abs(best.X - point.X) <= 10 ? best.Handle : Handle.None;
    }

    /// <summary>Einen Anfasser an eine Stelle ziehen - dieselbe Rechnung wie mit der Maus, fuer die Probe.</summary>
    public void Drag(Handle handle, double x)
    {
        float value = ValueAt(x);
        const float gap = LevelsChannel.MinimumSpan;

        switch (handle)
        {
            case Handle.InBlack:
                _channel.InBlack = Math.Min(value, _channel.InWhite - gap);
                break;

            case Handle.InWhite:
                _channel.InWhite = Math.Max(value, _channel.InBlack + gap);
                break;

            case Handle.Gray:
            {
                // Der Grauregler bleibt zwischen Schwarz und Weiss; seine Lage dort ist das Gamma.
                double black = X(_channel.InBlack), white = X(_channel.InWhite);
                double position = white - black < 1 ? 0.5 : (x - black) / (white - black);
                _channel.Gamma = LevelsChannel.GammaAt((float)position);
                break;
            }

            case Handle.OutBlack:
                _channel.OutBlack = value;
                break;

            case Handle.OutWhite:
                _channel.OutWhite = value;
                break;

            default:
                return;
        }

        _channel.Prepare();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Doppelklick auf einen Anfasser setzt ihn zurueck.
        if (e.ClickCount == 2)
        {
            if (Reset(HandleAt(e.GetPosition(this)))) e.Handled = true;
            return;
        }

        _dragging = HandleAt(e.GetPosition(this));
        if (_dragging == Handle.None) return;

        CaptureMouse();
        Focus();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging == Handle.None) return;

        Drag(_dragging, e.GetPosition(this).X);
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragging == Handle.None) return;

        _dragging = Handle.None;
        ReleaseMouseCapture();
        InvalidateVisual();
        Released?.Invoke();
    }

    /// <summary>Setzt einen Anfasser auf seine Grundstellung. False, wenn keiner getroffen war.</summary>
    public bool Reset(Handle handle)
    {
        switch (handle)
        {
            case Handle.InBlack: _channel.InBlack = 0; break;
            case Handle.InWhite: _channel.InWhite = 1; break;
            case Handle.Gray: _channel.Gamma = 1; break;
            case Handle.OutBlack: _channel.OutBlack = 0; break;
            case Handle.OutWhite: _channel.OutWhite = 1; break;
            default: return false;
        }

        _channel.Prepare();
        InvalidateVisual();
        Released?.Invoke();
        return true;
    }
}
