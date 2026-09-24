using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using PixelFormats = System.Windows.Media.PixelFormats;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
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

        context.DrawImage(Disc, new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2));

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
    /// Die Scheibe, einmal gerechnet und danach nur noch gezeichnet.
    ///
    /// Erste Fassung war ein Kranz aus sechzig Tortenstuecken mit je einem radialen
    /// Verlauf - und sah fleckig aus statt wie ein Farbkreis. Der Grund: Ein
    /// RadialGradientBrush bezieht sich auf die Huellbox der Form, die er fuellt,
    /// und die Huellbox eines Tortenstuecks ist nicht der Kreis. Jedes Stueck bekam
    /// damit seinen eigenen kleinen Verlauf aus seiner eigenen Mitte.
    ///
    /// Ein Bild hat das Problem nicht: Jeder Bildpunkt bekommt die Farbe, die an
    /// seiner Stelle wirklich entsteht - dieselbe Rechnung, die auch das Bild
    /// verschiebt. Damit zeigt das Rad nicht irgendeinen Farbkreis, sondern seinen
    /// eigenen.
    /// </summary>
    private static readonly System.Windows.Media.Imaging.BitmapSource Disc = BuildDisc(160);

    private static System.Windows.Media.Imaging.BitmapSource BuildDisc(int size)
    {
        int stride = size * 4;
        var pixels = new byte[stride * size];
        double half = size / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Von der Bildmitte aus, und Y auf dem Schirm nach unten.
                double dx = (x + 0.5 - half) / half;
                double dy = (half - y - 0.5) / half;
                double radius = Math.Sqrt(dx * dx + dy * dy);

                int at = y * stride + x * 4;
                if (radius > 1.0)
                {
                    // Ausserhalb bleibt es durchsichtig; die Kante wird ueber ein
                    // schmales Band weich, sonst franst der Rand aus.
                    continue;
                }

                var (r, g, b) = ColourWheelMath.Offset(new WheelPoint((float)dx, (float)dy));

                // Auf mittleres Grau gelegt: So sieht man an jeder Stelle die Farbe,
                // die dort tatsaechlich dazukommt.
                byte red = Component(r);
                byte green = Component(g);
                byte blue = Component(b);

                // Weiche Aussenkante ueber die letzten drei Prozent des Radius.
                double edge = Math.Clamp((1.0 - radius) / 0.03, 0, 1);
                byte alpha = (byte)(edge * 190);

                // Vormultipliziert, weil Pbgra32 das erwartet.
                pixels[at] = (byte)(blue * alpha / 255);
                pixels[at + 1] = (byte)(green * alpha / 255);
                pixels[at + 2] = (byte)(red * alpha / 255);
                pixels[at + 3] = alpha;
            }
        }

        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);

        source.Freeze();
        return source;

        static byte Component(float value) => (byte)Math.Clamp((value * 0.5 + 0.5) * 255, 0, 255);
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
