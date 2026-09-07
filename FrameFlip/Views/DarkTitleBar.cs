using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameFlip.Views;

/// <summary>
/// Die Titelleiste dunkel einfaerben.
///
/// WPF zeichnet den Fensterrahmen nicht selbst - den macht Windows, und der ist
/// hell, solange man nichts sagt. Ueber einer fast schwarzen Oberflaeche sitzt dann
/// ein weisser Balken, und das ist der einzige helle Fleck im Bild.
///
/// Der andere Weg waere ein Fenster ohne Rahmen mit selbstgebauter Leiste. Den geht
/// FrameFlip absichtlich nicht: Dann muesste es Ziehen, Maximieren, Andocken und die
/// Fangpunkte an den Bildschirmraendern selbst nachbauen, und all das funktioniert
/// nirgends so gut wie beim Original. Ein dunkler echter Rahmen ist die bessere
/// Loesung als ein nachgebauter.
/// </summary>
public static class DarkTitleBar
{
    /// <summary>Seit Windows 10 Version 2004. Davor lag dasselbe auf 19.</summary>
    private const int UseImmersiveDarkMode = 20;

    private const int UseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// Auf ein Fenster anwenden, sobald es ein Handle hat.
    ///
    /// Faellt still aus, wenn Windows das nicht kennt - unter Windows 8 gibt es
    /// keine dunkle Titelleiste, und ein Programm, das deshalb nicht startet, waere
    /// die schlechtere Antwort.
    /// </summary>
    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int on = 1;

            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
        }
        catch (Exception)
        {
        }
    }
}
