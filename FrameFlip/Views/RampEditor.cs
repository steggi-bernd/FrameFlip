using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Der Verlauf eines Farbverlauf-Knotens zum Anfassen - wie die Color Ramp in Blender.
///
/// Oben der Verlauf, darunter die Stopps als kleine Marken. Ein Klick in den Verlauf
/// setzt einen Stopp mit der Farbe, die dort schon steht; eine Marke waehlt man durch
/// Anklicken und verschiebt sie durch Ziehen. Farbe und Lage des gewaehlten Stopps
/// stehen in Reglern darunter.
///
/// Der Verlauf wird aus dem Knoten selbst abgetastet (<see cref="ColorRampNode.ColourAt"/>)
/// und nicht nachgebaut - so zeigt er auch "weich" und "stufig" so, wie gerechnet wird.
/// </summary>
public sealed class RampEditor : StackPanel
{
    private readonly ColorRampNode _node;
    private readonly Action<bool> _changed;
    private readonly Func<string, object> _resource;

    private readonly Border _bar;
    private readonly Canvas _marks;
    private readonly StackPanel _details;

    private RampStop _selected;
    private RampStop? _dragging;
    private bool _filling;

    /// <param name="changed">Etwas hat sich geaendert - true, solange noch gezogen wird.</param>
    /// <param name="resource">Holt Stile und Farben aus dem Farbstreifen.</param>
    public RampEditor(ColorRampNode node, Action<bool> changed, Func<string, object> resource)
    {
        _node = node;
        _changed = changed;
        _resource = resource;
        _selected = node.Stops.OrderBy(s => s.Position).First();

        _bar = new Border
        {
            Height = 22,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46)),
            Cursor = Cursors.Cross,
            ToolTip = Strings.T("S_NodeRampHint"),
        };

        _bar.MouseLeftButtonDown += OnBarPressed;

        _marks = new Canvas { Height = 14, Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent };
        _marks.SizeChanged += (_, _) => ShowMarks();
        _marks.MouseMove += OnMarkDragged;
        _marks.MouseLeftButtonUp += OnMarkReleased;

        _details = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        Children.Add(_bar);
        Children.Add(_marks);
        Children.Add(_details);

        Refresh();
    }

    /// <summary>Welcher Stopp gerade bearbeitet wird - fuer die Probe.</summary>
    internal RampStop Selected => _selected;

    /// <summary>Setzt einen Stopp an eine Stelle des Verlaufs - derselbe Weg wie ein Klick.</summary>
    internal void AddAt(double fraction)
    {
        _selected = _node.Insert((float)Math.Clamp(fraction, 0, 1));
        Refresh();
        _changed(false);
    }

    private void OnBarPressed(object sender, MouseButtonEventArgs e)
    {
        if (_bar.ActualWidth <= 0) return;

        AddAt(e.GetPosition(_bar).X / _bar.ActualWidth);
        e.Handled = true;
    }

    // ------------------------------------------------------------ Marken

    private void ShowMarks()
    {
        _marks.Children.Clear();

        double width = _marks.ActualWidth;
        if (width <= 0) return;

        foreach (var stop in _node.Stops)
        {
            bool chosen = ReferenceEquals(stop, _selected);

            var mark = new Path
            {
                Data = Geometry.Parse("M 5,0 L 10,6 L 10,13 L 0,13 L 0,6 Z"),
                Fill = new SolidColorBrush(ToColor(stop.Colour)),
                Stroke = chosen ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)),
                StrokeThickness = chosen ? 2 : 1,
                Cursor = Cursors.SizeWE,
                Tag = stop,
            };

            Canvas.SetLeft(mark, stop.Position * width - 5);
            Canvas.SetTop(mark, 0);

            mark.MouseLeftButtonDown += (_, e) =>
            {
                _selected = stop;
                _dragging = stop;
                _marks.CaptureMouse();
                Refresh();
                e.Handled = true;
            };

            _marks.Children.Add(mark);
        }
    }

    private void OnMarkDragged(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed || _marks.ActualWidth <= 0) return;

        _dragging.Position = (float)Math.Clamp(e.GetPosition(_marks).X / _marks.ActualWidth, 0, 1);

        ShowBar();
        ShowMarks();
        ShowPosition();

        _changed(true);
    }

    private void OnMarkReleased(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null) return;

        _dragging = null;
        _marks.ReleaseMouseCapture();
        _changed(false);
    }

    // ------------------------------------------------------------ Darstellung

    private void Refresh()
    {
        ShowBar();
        ShowMarks();
        ShowDetails();
    }

    private void ShowBar()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };

        for (int i = 0; i <= 64; i++)
        {
            float t = i / 64f;
            brush.GradientStops.Add(new GradientStop(ToColor(_node.ColourAt(t)), t));
        }

        _bar.Background = brush;
    }

    private Slider? _position;
    private TextBlock? _positionValue;

    private void ShowPosition()
    {
        if (_position is null || _positionValue is null) return;

        _filling = true;
        _position.Value = _selected.Position;
        _filling = false;

        _positionValue.Text = _selected.Position.ToString("0.00", CultureInfo.CurrentCulture);
    }

    /// <summary>Die Regler des gewaehlten Stopps: Lage, Rot, Gruen, Blau - und Entfernen.</summary>
    private void ShowDetails()
    {
        _details.Children.Clear();

        (_position, _positionValue) = AddSlider("S_NodeRampPosition", () => _selected.Position, v => _selected.Position = v);

        AddSlider("S_NodeRed", () => _selected.Colour.R, v => _selected.Colour.R = v);
        AddSlider("S_NodeGreen", () => _selected.Colour.G, v => _selected.Colour.G = v);
        AddSlider("S_NodeBlue", () => _selected.Colour.B, v => _selected.Colour.B = v);

        var remove = new Button
        {
            Style = (Style)_resource("OverlayButton"),
            Content = Strings.T("S_NodeRampRemove"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = _node.Stops.Count > 2,
        };

        remove.Click += (_, _) => Remove();
        _details.Children.Add(remove);

        ShowPosition();
    }

    /// <summary>Nimmt den gewaehlten Stopp heraus - zwei bleiben immer, ein Verlauf braucht zwei Enden.</summary>
    internal void Remove()
    {
        if (_node.Stops.Count <= 2) return;

        _node.Stops.Remove(_selected);
        _selected = _node.Stops.OrderBy(s => Math.Abs(s.Position - _selected.Position)).First();

        Refresh();
        _changed(false);
    }

    private (Slider, TextBlock) AddSlider(string key, Func<float> get, Action<float> set)
    {
        var head = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        head.Children.Add(new TextBlock { Text = Strings.T(key), Style = (Style)_resource("PanelLabel") });

        var value = new TextBlock { Style = (Style)_resource("PanelValue"), Text = get().ToString("0.00", CultureInfo.CurrentCulture) };
        head.Children.Add(value);

        var slider = new Slider
        {
            Style = (Style)_resource("PanelSlider"),
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.005,
            LargeChange = 0.05,
        };

        _filling = true;
        slider.Value = get();
        _filling = false;

        slider.ValueChanged += (_, e) =>
        {
            if (_filling) return;

            set((float)e.NewValue);
            value.Text = e.NewValue.ToString("0.00", CultureInfo.CurrentCulture);

            ShowBar();
            ShowMarks();

            // Wie an jedem Regler: waehrend des Zuges grob, das Loslassen holt der
            // Zeitgeber der Seite nach.
            _changed(true);
        };

        _details.Children.Add(head);
        _details.Children.Add(slider);

        return (slider, value);
    }

    /// <summary>Die Farbe eines Stopps, wie sie auf dem Schirm steht - sie ist schon in Anzeigewerten.</summary>
    private static Color ToColor(ColourTriplet colour)
        => Color.FromRgb(Byte(colour.R), Byte(colour.G), Byte(colour.B));

    private static byte Byte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
}
