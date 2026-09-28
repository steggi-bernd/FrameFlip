using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Der Bereichsregler (docs/Atelier-Arbeitsablauf.md, C7): ein Fenster auf einer Skala, mit vier
/// Griffen - je zwei als Paar.
///
/// Der innere Griff zieht sein Paar mit, die Kante bleibt, wie sie war. Der aeussere zieht
/// allein und macht die Kante weicher oder haerter; mit Alt zieht auch der innere allein. So
/// wird aus einer harten Grenze ein weicher Bereich, ohne dass es dafuer einen zweiten Regler
/// braucht. Umschalt zieht fein, ein Doppelklick setzt zurueck.
///
/// Wie das Farbrad ein zeichnendes Element ohne eigene XAML. Die Rechnung steht in
/// <see cref="RangeWindows"/> und wird dort geprueft.
/// </summary>
public sealed class RangeSlider : FrameworkElement
{
    private const double TrackTop = 3, TrackHeight = 12, HandleSize = 7, Side = 6;

    private RangeWindow _window = new(0f, 1f, 0.1f, 0.1f);
    private RangeScale _scale = RangeScale.Unit;

    private RangeHandle? _dragging;
    private bool _alone;
    private double _startX;
    private RangeWindow _startWindow;

    public RangeSlider()
    {
        Height = 32;
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    /// <summary>Das Fenster, das der Regler zeigt. Setzen meldet nichts - nur ein Zug meldet.</summary>
    public RangeWindow Window
    {
        get => _window;
        set
        {
            _window = value;
            InvalidateVisual();
        }
    }

    /// <summary>Die Skala: eine Strecke oder ein Kreis. Der Kreis bekommt die Farben des Farbtons.</summary>
    public RangeScale Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            InvalidateVisual();
        }
    }

    /// <summary>Ein Zug hat das Fenster geaendert - mit <c>true</c>, solange er laeuft.</summary>
    public event Action<RangeWindow, bool>? Changed;

    /// <summary>Doppelklick: zurueck in die Grundstellung. Was das heisst, weiss der, dem das Fenster gehoert.</summary>
    public event Action? ResetWanted;

    // ---------------------------------------------------------------- Zeichnen

    private double Track => Math.Max(1, ActualWidth - 2 * Side);

    private double XOf(float value) => Side + (value - _scale.Min) / _scale.Span * Track;

    private float ValueAt(double x) => _scale.Min + (float)((x - Side) / Track) * _scale.Span;

    protected override Size MeasureOverride(Size available)
        => new(double.IsInfinity(available.Width) ? 160 : available.Width, 32);

    protected override void OnRender(DrawingContext dc)
    {
        var track = new Rect(Side, TrackTop, Track, TrackHeight);

        dc.DrawRoundedRectangle(Scale.Circular ? HueBrush : LightBrush, null, track, 3, 3);

        // Was nicht durchgelassen wird, liegt im Schatten - so weit, wie die Kante es sagt.
        var shade = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x16));
        shade.Freeze();

        int columns = Math.Max(1, (int)Track);
        var outline = new StreamGeometry();

        using (var geometry = outline.Open())
        {
            for (int i = 0; i <= columns; i++)
            {
                double x = Side + i;
                float cover = RangeWindows.Cover(_window, ValueAt(x), _scale);

                if (cover < 1f)
                    dc.DrawRectangle(shade, null, new Rect(x, TrackTop, 1, TrackHeight * (1 - cover)));

                var at = new Point(x, TrackTop + TrackHeight * (1 - cover));

                if (i == 0) geometry.BeginFigure(at, false, false);
                else geometry.LineTo(at, true, false);
            }
        }

        outline.Freeze();
        dc.DrawGeometry(null, new Pen(Accent, 1.2), outline);

        // Die Griffe unter der Skala: innen gefuellt, aussen nur umrandet.
        foreach (var handle in new[] { RangeHandle.OuterLow, RangeHandle.OuterHigh, RangeHandle.Low, RangeHandle.High })
        {
            bool inner = handle is RangeHandle.Low or RangeHandle.High;
            double x = XOf(RangeWindows.Shown(_window, handle, _scale));
            double top = TrackTop + TrackHeight + 2;

            var tip = new StreamGeometry();
            using (var g = tip.Open())
            {
                g.BeginFigure(new Point(x, top), true, true);
                g.LineTo(new Point(x - HandleSize / 2 - (inner ? 1 : 0), top + HandleSize + (inner ? 2 : 0)), true, false);
                g.LineTo(new Point(x + HandleSize / 2 + (inner ? 1 : 0), top + HandleSize + (inner ? 2 : 0)), true, false);
            }

            tip.Freeze();

            bool active = _dragging == handle;
            dc.DrawGeometry(inner ? (active ? Accent : Foreground) : null,
                            new Pen(active ? Accent : Foreground, 1.2), tip);
        }
    }

    private Brush Foreground => TryFindResource("ForegroundBrush") as Brush ?? Brushes.White;

    private Brush Accent => TryFindResource("AccentBrush") as Brush ?? Brushes.MediumPurple;

    private static readonly Brush LightBrush = Frozen(new LinearGradientBrush(Colors.Black, Colors.White, 0));

    private static readonly Brush HueBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0xFF, 0x00, 0x00), 0),
        new(Color.FromRgb(0xFF, 0xFF, 0x00), 1 / 6.0),
        new(Color.FromRgb(0x00, 0xFF, 0x00), 2 / 6.0),
        new(Color.FromRgb(0x00, 0xFF, 0xFF), 3 / 6.0),
        new(Color.FromRgb(0x00, 0x00, 0xFF), 4 / 6.0),
        new(Color.FromRgb(0xFF, 0x00, 0xFF), 5 / 6.0),
        new(Color.FromRgb(0xFF, 0x00, 0x00), 1),
    }, 0));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ---------------------------------------------------------------- Bedienung

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;

        if (e.ClickCount == 2)
        {
            ResetWanted?.Invoke();
            return;
        }

        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        double x = e.GetPosition(this).X;

        if (!Grab(x, alt)) return;

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging is null || !IsMouseCaptured) return;

        bool fine = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        MoveTo(e.GetPosition(this).X, fine);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragging is null) return;

        ReleaseMouseCapture();
        Release();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_dragging is not null) Release();
    }

    /// <summary>
    /// Greift den Griff an einer Stelle - getrennt von der Maus, damit die Probe denselben Weg
    /// gehen kann. Falsch, wenn dort keiner liegt.
    /// </summary>
    internal bool Grab(double x, bool alt)
    {
        float reach = (float)(HandleSize / Track) * _scale.Span;

        _dragging = RangeWindows.HandleAt(_window, ValueAt(x), reach, alt, _scale);
        if (_dragging is null) return false;

        _alone = alt;
        _startX = x;
        _startWindow = _window;

        InvalidateVisual();
        return true;
    }

    /// <summary>Zieht den gegriffenen Griff an eine Stelle; <paramref name="fine"/> ist ein Zehntel so schnell.</summary>
    internal void MoveTo(double x, bool fine = false)
    {
        if (_dragging is not { } handle) return;

        float delta = (float)((x - _startX) / Track) * _scale.Span * (fine ? 0.1f : 1f);

        _window = RangeWindows.Drag(_startWindow, handle, delta, _alone, _scale);
        InvalidateVisual();

        Changed?.Invoke(_window, true);
    }

    /// <summary>Laesst los - das Fenster gilt.</summary>
    internal void Release()
    {
        if (_dragging is null) return;

        _dragging = null;
        InvalidateVisual();

        Changed?.Invoke(_window, false);
    }

    /// <summary>Wo ein Griff auf dem Schirm liegt - fuer die Probe.</summary>
    internal double ScreenOf(RangeHandle handle) => XOf(RangeWindows.Shown(_window, handle, _scale));
}
