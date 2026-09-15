using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Ein Farbrad fuer eine der drei Zonen.
///
/// Wie das Kurvenfeld ein zeichnendes Element ohne eigene XAML - es gibt nichts zu
/// klicken ausser dem Griff selbst. Die Rechnung steht in
/// <see cref="ColourWheelMath"/> und wird dort geprueft.
///
/// Warum ueberhaupt ein Rad und nicht drei Regler: "Schatten waermer" ist EINE
/// Bewegung. Mit drei Reglern sind es drei, von denen zwei in die
/// Gegenrichtung muessen, und das Ergebnis ist nach dem dritten Griff selten das,
/// was man wollte.
/// </summary>
public sealed class ColourWheel : FrameworkElement
{
    private WheelPoint _value;
    private bool _dragging;
    private bool _hover;

    public ColourWheel()
    {
        Width = 84;
        Height = 84;
        Cursor = Cursors.Cross;
    }

    /// <summary>Wird gerufen, waehrend gezogen wird.</summary>
    public event Action? Changed;

    /// <summary>Wird beim Loslassen gerufen - das Zeichen, wieder voll zu rechnen.</summary>
    public event Action? Released;

    public WheelPoint Value
    {
        get => _value;
        set
        {
            _value = Clamp(value);
            InvalidateVisual();
        }
    }

    private static readonly Brush RingBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CrossBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
    private static readonly Brush ShadowBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00));

    static ColourWheel()
    {
        RingBrush.Freeze();
        CrossBrush.Freeze();
        HandleBrush.Freeze();
        ShadowBrush.Freeze();
    }

    protected override void OnRender(DrawingContext context)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;

        double radius = size / 2 - 2;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);

        DrawWheel(context, centre, radius);

        // Fadenkreuz: ohne es ist die Mitte - also "nichts tun" - nicht zu treffen.
        context.DrawLine(new Pen(CrossBrush, 1),
                         new Point(centre.X - radius, centre.Y), new Point(centre.X + radius, centre.Y));
        context.DrawLine(new Pen(CrossBrush, 1),
                         new Point(centre.X, centre.Y - radius), new Point(centre.X, centre.Y + radius));

        context.DrawEllipse(null, new Pen(RingBrush, 1), centre, radius, radius);

        // Der Griff. Y ist auf dem Schirm nach unten und im Rad nach oben.
        var handle = new Point(centre.X + _value.X * radius, centre.Y - _value.Y * radius);
        double grip = _dragging || _hover ? 6.0 : 4.5;

        context.DrawEllipse(null, new Pen(ShadowBrush, 3), handle, grip, grip);
        context.DrawEllipse(HandleBrush, null, handle, grip - 1.5, grip - 1.5);
    }

    /// <summary>
    /// Die Scheibe: Farbton umlaufend, zur Mitte hin ausgewaschen.
    ///
    /// Gezeichnet als Kranz aus Segmenten mit je einem radialen Verlauf. Ein echter
    /// winkelabhaengiger Verlauf fehlt WPF, und ein Bild je Rad waere fuer drei
    /// Raeder dreimal derselbe Aufwand - bei dieser Groesse sind sechzig Segmente
    /// nicht von einem Verlauf zu unterscheiden.
    /// </summary>
    private static void DrawWheel(DrawingContext context, Point centre, double radius)
    {
        const int segments = 60;
        double step = 2 * Math.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            double from = i * step;
            double to = from + step * 1.05;      // leichte Ueberlappung gegen Haarrisse

            var geometry = new StreamGeometry();
            using (var draw = geometry.Open())
            {
                draw.BeginFigure(centre, isFilled: true, isClosed: true);
                draw.LineTo(new Point(centre.X + Math.Cos(from) * radius,
                                      centre.Y - Math.Sin(from) * radius), false, false);
                draw.ArcTo(new Point(centre.X + Math.Cos(to) * radius, centre.Y - Math.Sin(to) * radius),
                           new Size(radius, radius), 0, false, SweepDirection.Counterclockwise, false, false);
            }

            geometry.Freeze();

            var hue = HueAt(from + step / 2);
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };

            // Innen fast neutral, aussen die volle Farbe - so sieht man, dass zur
            // Mitte hin weniger passiert.
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0x80, 0x80, 0x80), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, hue.R, hue.G, hue.B), 1.0));
            brush.Freeze();

            context.DrawGeometry(brush, null, geometry);
        }
    }

    /// <summary>
    /// Welche Farbe an diesem Winkel steht - dieselbe Zuordnung, die die Rechnung
    /// benutzt: Rot rechts, Gruen und Blau um je 120 Grad versetzt.
    /// </summary>
    private static Color HueAt(double angle)
    {
        var (r, g, b) = ColourWheelMath.Offset(new WheelPoint((float)Math.Cos(angle), (float)Math.Sin(angle)));

        // Von -1..1 auf einen darstellbaren Bereich heben.
        return Color.FromRgb(Component(r), Component(g), Component(b));

        static byte Component(float value) => (byte)Math.Clamp((value + 0.55) * 255 / 1.1, 0, 255);
    }

    // ------------------------------------------------------------------- Maus

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Doppelklick setzt zurueck - wie bei den Reglern daneben. Ueber ClickCount
        // und nicht ueber OnMouseDoubleClick: das gibt es erst auf Control, und ein
        // Control waere hier eine Vorlage mehr, ohne dass es etwas zu gestalten gaebe.
        if (e.ClickCount == 2)
        {
            Value = default;
            Changed?.Invoke();
            Released?.Invoke();
            e.Handled = true;
            return;
        }

        _dragging = true;
        CaptureMouse();
        Track(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging)
        {
            bool over = Near(e.GetPosition(this));
            if (over != _hover)
            {
                _hover = over;
                InvalidateVisual();
            }

            return;
        }

        Track(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
        Released?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hover)
        {
            _hover = false;
            InvalidateVisual();
        }
    }

    private void Track(Point position)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double radius = size / 2 - 2;
        if (radius <= 0) return;

        var next = Clamp(new WheelPoint((float)((position.X - ActualWidth / 2) / radius),
                                        (float)((ActualHeight / 2 - position.Y) / radius)));

        if (next == _value) return;

        _value = next;
        InvalidateVisual();
        Changed?.Invoke();
    }

    private bool Near(Point position)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double radius = size / 2 - 2;
        if (radius <= 0) return false;

        double dx = position.X - (ActualWidth / 2 + _value.X * radius);
        double dy = position.Y - (ActualHeight / 2 - _value.Y * radius);

        return dx * dx + dy * dy <= 100;
    }

    /// <summary>Der Griff bleibt im Kreis - ausserhalb gaebe es keine Farbe mehr.</summary>
    private static WheelPoint Clamp(WheelPoint point)
    {
        float length = MathF.Sqrt(point.X * point.X + point.Y * point.Y);
        if (length <= 1f || length <= 0f) return point;

        return new WheelPoint(point.X / length, point.Y / length);
    }
}
