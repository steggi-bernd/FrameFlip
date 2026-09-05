using System.Windows;
using System.Windows.Media;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen diese Typen genauso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Verlaufskurve einer Messgroesse ueber die letzte Minute.
///
/// Eine einzelne Zahl sagt nicht, ob sie gerade steigt oder faellt - genau das will
/// man aber wissen, wenn man auf die Auslastung sieht. Deshalb Kurve statt Ziffer,
/// mit der aktuellen Zahl daneben.
///
/// Die Werte liegen normiert zwischen 0 und 1 in einem Ringpuffer. Gezeichnet wird
/// ein Streckenzug mit weicher Verlaufsfuellung darunter und einem Punkt am aktuellen
/// Ende, damit auf einen Blick klar ist, wo "jetzt" ist.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private double[] _values = new double[60];
    private int _count;
    private int _next;

    /// <summary>Wie viele Messpunkte die Kurve fasst.</summary>
    public int Capacity
    {
        get => _values.Length;
        set
        {
            int capacity = Math.Clamp(value, 4, 600);
            if (capacity == _values.Length) return;

            _values = new double[capacity];
            _count = 0;
            _next = 0;
            InvalidateVisual();
        }
    }

    public Brush LineBrush { get; set; } = Brushes.White;

    /// <summary>Farbe der Flaeche unter der Kurve. Wird nach unten ausgeblendet.</summary>
    public Color FillColor { get; set; } = Color.FromRgb(0xA4, 0x7B, 0xF0);

    public double LineThickness { get; set; } = 1.6;

    /// <summary>Der zuletzt eingetragene Wert, oder 0.</summary>
    public double Latest => _count == 0 ? 0 : _values[(_next - 1 + _values.Length) % _values.Length];

    public bool HasData => _count > 0;

    public void Add(double normalized)
    {
        if (double.IsNaN(normalized)) return;

        _values[_next] = Math.Clamp(normalized, 0, 1);
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length) _count++;

        InvalidateVisual();
    }

    public void Clear()
    {
        _count = 0;
        _next = 0;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 34);

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        // Grundlinie, damit die Flaeche auch bei leerer Kurve einen Boden hat.
        var baseline = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        context.DrawRectangle(baseline, null, new Rect(0, h - 1, w, 1));

        if (_count < 2) return;

        // Die Kurve waechst von rechts nach links: Der juengste Wert steht am rechten
        // Rand und wandert nach links aus dem Bild - so herum liest man einen Verlauf.
        double step = w / (_values.Length - 1);
        double left = w - (_count - 1) * step;

        var figure = new PathFigure { StartPoint = new Point(left, Y(Value(0), h)) };

        for (int i = 1; i < _count; i++)
            figure.Segments.Add(new LineSegment(new Point(left + i * step, Y(Value(i), h)), true));

        var line = new PathGeometry();
        line.Figures.Add(figure);
        line.Freeze();

        // Dieselbe Kontur, unten geschlossen, als Flaeche.
        var area = figure.Clone();
        area.Segments.Add(new LineSegment(new Point(left + (_count - 1) * step, h), false));
        area.Segments.Add(new LineSegment(new Point(left, h), false));
        area.IsClosed = true;

        var filled = new PathGeometry();
        filled.Figures.Add(area);
        filled.Freeze();

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x66, FillColor.R, FillColor.G, FillColor.B), 0),
                new GradientStop(Color.FromArgb(0x00, FillColor.R, FillColor.G, FillColor.B), 1),
            },
        };
        gradient.Freeze();

        context.DrawGeometry(gradient, null, filled);

        var pen = new Pen(LineBrush, LineThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();

        context.DrawGeometry(null, pen, line);

        // Der Punkt am Ende markiert den aktuellen Wert.
        var head = new Point(left + (_count - 1) * step, Y(Value(_count - 1), h));
        context.DrawEllipse(LineBrush, null, head, 2.4, 2.4);
    }

    /// <summary>Wert i in Einfuegereihenfolge, aeltester zuerst.</summary>
    private double Value(int i)
        => _values[(_next - _count + i + _values.Length * 2) % _values.Length];

    /// <summary>
    /// Oben und unten je zwei Pixel frei lassen: Sonst wird eine Kurve bei 100 %
    /// von der eigenen Linienstaerke halb abgeschnitten.
    /// </summary>
    private static double Y(double value, double height)
        => 2 + (1 - value) * Math.Max(1, height - 4);
}
