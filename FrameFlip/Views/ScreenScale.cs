using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FrameFlip.Views;

/// <summary>
/// Wie gross der Bildschirm ist, auf dem ein Fenster steht - in Punkten, wie WPF sie
/// zaehlt, also nach der Windows-Skalierung. Ein 4K-Schirm mit 100 % ist 2160 hoch, mit
/// 150 % 1440, derselbe Schirm mit 200 % 1080. Daraus folgt die automatische
/// Bedienskalierung (<see cref="DesktopLayout.AutoFor"/>).
/// </summary>
internal static class ScreenScale
{
    /// <summary>Die wirksame Hoehe des Bildschirms unter dem Fenster - oder die des Hauptbildschirms, solange es keinen Griff hat.</summary>
    public static double EffectiveHeight(Visual window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source || source.Handle == IntPtr.Zero)
            return SystemParameters.PrimaryScreenHeight;

        var screen = System.Windows.Forms.Screen.FromHandle(source.Handle);
        double dpi = VisualTreeHelper.GetDpi(window).DpiScaleY;

        return screen.Bounds.Height / Math.Max(0.5, dpi);
    }
}
