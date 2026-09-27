using System.Windows;
using System.Windows.Media;

namespace FrameFlip.Views;

/// <summary>
/// Schneidet ein Element auf abgerundete Ecken zu - und zieht den Zuschnitt bei jeder
/// Groessenaenderung nach.
///
/// <c>ClipToBounds</c> schneidet in WPF rechteckig: Ein Rahmen mit runden Ecken haelt seinen
/// Inhalt damit nicht in den Ecken, ein Bild, das ihn ganz fuellt, steht dort ueber. Gesetzt
/// wird der Radius des Rahmens innen, also sein Eckradius weniger der Randbreite. Weil der
/// Zuschnitt in den Koordinaten des Elements liegt, gilt er fuer jedes Format und skaliert mit
/// einer Viewbox oder einem Zoom darueber einfach mit.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached("Radius", typeof(double), typeof(RoundedClip),
                                            new PropertyMetadata(0.0, OnRadiusChanged));

    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;

        element.SizeChanged -= OnSizeChanged;
        element.SizeChanged += OnSizeChanged;

        Apply(element);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        double radius = GetRadius(element);

        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Clip = null;
            return;
        }

        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();

        element.Clip = clip;
    }
}
