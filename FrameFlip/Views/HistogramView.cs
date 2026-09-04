using System.Windows;
using System.Windows.Media;
using FrameFlip.Imaging;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Zeichnet die Helligkeitsverteilung des angezeigten Bildes.
///
/// Bei einer Renderbeurteilung ist das oft aussagekraeftiger als die Regler selbst:
/// ob Lichter ausbrennen oder Schatten zulaufen, sieht man hier sofort, im Bild
/// dagegen erst, wenn es zu spaet ist.
/// </summary>
public sealed class HistogramView : FrameworkElement
{
    private readonly int[] _red = new int[256];
    private readonly int[] _green = new int[256];
    private readonly int[] _blue = new int[256];
    private readonly int[] _luma = new int[256];

    private int _peak = 1;
    private double _clippedLow;
    private double _clippedHigh;
    private bool _hasData;

    /// <summary>
    /// Ab welchem Anteil anliegender Pixel gewarnt wird. Dieselbe Schwelle benutzt
    /// die Textzeile darunter - sonst leuchtet die Markierung, waehrend daneben
    /// "keine anliegenden Ränder" steht.
    /// </summary>
    public const double ClipThreshold = 0.005;

    /// <summary>Farbkanaele statt der reinen Luminanz zeigen.</summary>
    public bool ShowChannels { get; set; } = true;

    public HistogramView()
    {
        Height = 110;
        ClipToBounds = true;
    }

    /// <summary>
    /// Uebernimmt eine Messung. Kopiert die Zaehlungen, damit der Aufrufer sein
    /// Histogramm weiterverwenden kann, ohne dass sich die Zeichnung darunter aendert.
    /// </summary>
    public void Update(Histogram histogram)
    {
        Array.Copy(histogram.Red, _red, 256);
        Array.Copy(histogram.Green, _green, 256);
        Array.Copy(histogram.Blue, _blue, 256);
        Array.Copy(histogram.Luma, _luma, 256);

        _peak = Math.Max(1, histogram.Peak);
        _clippedLow = histogram.ClippedLow;
        _clippedHigh = histogram.ClippedHigh;
        _hasData = true;

        InvalidateVisual();
    }

    public void Clear()
    {
        _hasData = false;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, Height);

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
        context.DrawRectangle(background, null, new Rect(0, 0, w, h));

        // Viertellinien als Bezug - ohne sie ist nicht abzulesen, wo die Mitten liegen.
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
        for (int i = 1; i < 4; i++)
        {
            double x = Math.Round(w * i / 4.0) + 0.5;
            context.DrawLine(grid, new Point(x, 0), new Point(x, h));
        }

        if (!_hasData) return;

        if (ShowChannels)
        {
            // Additiv uebereinander: wo sich alle drei decken, entsteht Weiss - so
            // liest man Farbstiche unmittelbar ab.
            DrawCurve(context, _red, Color.FromArgb(0xB0, 0xE0, 0x50, 0x50), w, h);
            DrawCurve(context, _green, Color.FromArgb(0xB0, 0x50, 0xE0, 0x50), w, h);
            DrawCurve(context, _blue, Color.FromArgb(0xB0, 0x50, 0x80, 0xE0), w, h);
        }
        else
        {
            DrawCurve(context, _luma, Color.FromArgb(0xD0, 0xDD, 0xDD, 0xDD), w, h);
        }

        // Anliegende Raender markieren: links zugelaufene Schatten, rechts
        // ausgebrannte Lichter. Erst ab einem Promille, sonst leuchtet es staendig.
        if (_clippedLow > ClipThreshold)
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0x40, 0x90, 0xF0)),
                                  null, new Rect(0, 0, 3, h));

        if (_clippedHigh > ClipThreshold)
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0xF0, 0x60, 0x40)),
                                  null, new Rect(w - 3, 0, 3, h));
    }

    private void DrawCurve(DrawingContext context, int[] values, Color color, double w, double h)
    {
        var geometry = new StreamGeometry();

        using (var draw = geometry.Open())
        {
            draw.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);

            for (int i = 0; i < 256; i++)
            {
                double x = w * i / 255.0;

                // Wurzelskalierung: linear verschwaende ein einzelner hoher Ausschlag
                // den gesamten Rest der Verteilung im Bodensatz.
                double normalized = Math.Sqrt(Math.Min(1.0, values[i] / (double)_peak));
                double y = h - normalized * (h - 2);

                draw.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
            }

            draw.LineTo(new Point(w, h), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        context.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }
}
