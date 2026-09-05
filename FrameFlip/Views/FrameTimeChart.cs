using System.Globalization;
using System.Windows;
using System.Windows.Media;
// UseWindowsForms zieht System.Drawing mit ein; dort heissen diese Typen genauso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Wie lange jeder Frame gebraucht hat, ueber die Animation hinweg.
///
/// Der Zweck ist nicht Schoenheit, sondern eine Frage zu beantworten: WELCHE
/// Stellen waren teuer? Ein Mittelwert verschweigt das - er sagt "zwei Minuten je
/// Frame", waehrend in Wahrheit dreissig Frames zwanzig Sekunden brauchten und
/// zehn davon zehn Minuten. Genau die zehn will man finden.
///
/// Deshalb Balken statt Kurve: Jeder Balken ist ein Frame, und der teuerste faellt
/// sofort ins Auge. Der Durchschnitt liegt als Linie darueber, damit "teuer"
/// einen Bezug hat.
/// </summary>
public sealed class FrameTimeChart : FrameworkElement
{
    private double[] _values = Array.Empty<double>();
    private double _max;
    private double _average;

    public Brush BarBrush { get; set; } = Brushes.MediumPurple;

    /// <summary>Der teuerste Balken wird hervorgehoben - er ist die eigentliche Auskunft.</summary>
    public Brush PeakBrush { get; set; } = Brushes.White;

    public Brush AverageBrush { get; set; } =
        new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    public Brush LabelBrush { get; set; } = Brushes.Gray;

    public bool HasData => _values.Length > 0;

    public void SetData(double[] seconds)
    {
        _values = seconds ?? Array.Empty<double>();
        _max = _values.Length > 0 ? _values.Max() : 0;
        _average = _values.Length > 0 ? _values.Average() : 0;

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 58);

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 1 || h <= 1) return;

        // Unten bleibt Platz fuer die Beschriftung der Achse.
        double plot = Math.Max(1, h - 14);

        var floor = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        context.DrawRectangle(floor, null, new Rect(0, plot, w, 1));

        if (_values.Length == 0 || _max <= 0)
        {
            DrawText(context, "noch keine fertigen Frames", 0, plot + 2);
            return;
        }

        // Ein Balken je Frame, mindestens ein Pixel breit. Bei sehr vielen Frames
        // wird die Luecke aufgegeben, bevor der Balken unsichtbar wird.
        double slot = w / _values.Length;
        double bar = Math.Max(1, slot >= 3 ? slot - 1 : slot);

        int peak = 0;
        for (int i = 1; i < _values.Length; i++)
            if (_values[i] > _values[peak]) peak = i;

        for (int i = 0; i < _values.Length; i++)
        {
            double height = Math.Max(1, _values[i] / _max * (plot - 2));
            double x = i * slot;

            context.DrawRectangle(i == peak ? PeakBrush : BarBrush, null,
                                  new Rect(x, plot - height, bar, height));
        }

        // Der Durchschnitt als Bezugslinie.
        if (_average > 0 && _values.Length > 1)
        {
            double y = plot - _average / _max * (plot - 2);
            context.DrawRectangle(AverageBrush, null, new Rect(0, y, w, 1));
        }

        DrawText(context, Describe(_values[peak]) + " langsamster", 0, plot + 2);

        var label = Describe(_average) + " Ø";
        DrawText(context, label, w, plot + 2, rightAligned: true);
    }

    private static string Describe(double seconds)
        => seconds >= 90
            ? (seconds / 60).ToString("0.0", CultureInfo.CurrentCulture) + " min"
            : seconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";

    private void DrawText(DrawingContext context, string text, double x, double y,
                          bool rightAligned = false)
    {
        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                         FontWeights.Normal, FontStretches.Normal),
            10, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        context.DrawText(formatted, new Point(rightAligned ? x - formatted.Width : x, y));
    }
}
