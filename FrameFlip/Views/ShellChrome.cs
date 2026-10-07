using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameFlip.Views;

/// <summary>
/// Die eigene Titelleiste - ohne die Verhaltensweisen zu verlieren, die Windows
/// besser kann als jeder Nachbau.
///
/// Die frueher hier getroffene Entscheidung (siehe DarkTitleBar) lautete: lieber
/// eine dunkel gefaerbte echte Leiste als eine nachgebaute, weil Ziehen,
/// Maximieren, Andocken und die Fangpunkte an den Bildschirmraendern sonst
/// selbst gebaut werden muessten. Das galt fuer ein Fenster ohne Rahmen.
///
/// WindowChrome ist der dritte Weg: Das Fenster behaelt seinen echten Rahmen und
/// damit saemtliche Verhaltensweisen - Ziehen, Doppelklick zum Maximieren,
/// Aero Snap, Win+Pfeil, das Systemmenue auf Rechtsklick, das Andocken am
/// Bildschirmrand. Uebernommen wird ausschliesslich das Zeichnen der Leiste.
/// Genau ein Punkt bleibt selbst zu erledigen, und den erledigt diese Klasse:
/// die Groesse im maximierten Zustand.
/// </summary>
public static class ShellChrome
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT Reserved;
        public POINT MaxSize;
        public POINT MaxPosition;
        public POINT MinTrackSize;
        public POINT MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    /// <summary>
    /// Wie weit ein maximiertes Fenster ueber den Arbeitsbereich seines Bildschirms hinausragt -
    /// in geraeteunabhaengigen Punkten, je Seite. Null, wenn es nicht maximiert ist oder
    /// genau passt.
    ///
    /// Gemessen und nicht angenommen: Windows legt ein maximiertes Fenster mit eigener Leiste
    /// um die Breite seines Anfassrahmens ueber den Rand hinaus, und ob die Korrektur in
    /// WM_GETMINMAXINFO greift, haengt von der Anordnung der Bildschirme ab. Auf einem
    /// zweiten Bildschirm links vom ersten klebte der Inhalt oben und unten am Rand - die
    /// Leisten waren um diesen Streifen abgeschnitten.
    /// </summary>
    public static Thickness Overhang(Window window)
    {
        try
        {
            if (window.WindowState != WindowState.Maximized) return new Thickness(0);

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return new Thickness(0);

            IntPtr monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return new Thickness(0);

            double scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            if (scale <= 0) scale = 1;

            return new Thickness(Math.Max(0, info.Work.Left - rect.Left) / scale,
                                 Math.Max(0, info.Work.Top - rect.Top) / scale,
                                 Math.Max(0, rect.Right - info.Work.Right) / scale,
                                 Math.Max(0, rect.Bottom - info.Work.Bottom) / scale);
        }
        catch (Exception)
        {
            return new Thickness(0);
        }
    }

    /// <summary>
    /// An ein Fenster haengen, sobald es ein Handle hat (SourceInitialized).
    /// </summary>
    public static void Attach(Window window)
    {
        try
        {
            var source = (HwndSource?)PresentationSource.FromVisual(window);
            source?.AddHook(Hook);
        }
        catch (Exception)
        {
            // Ohne den Haken ist das Fenster im maximierten Zustand ein paar Pixel
            // zu gross - unschoen, aber kein Grund, den Start abzubrechen.
        }
    }

    private static IntPtr Hook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_GETMINMAXINFO) return IntPtr.Zero;

        try
        {
            IntPtr monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

            var minMax = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            // Ohne diese Korrektur macht Windows ein maximiertes Fenster mit eigener
            // Leiste um die Breite des Anfassrahmens zu gross - rechts und unten
            // verschwindet dann ein Streifen des Inhalts hinter dem Bildschirmrand,
            // und die Taskleiste wird ueberdeckt.
            minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            minMax.MaxSize.X = info.Work.Right - info.Work.Left;
            minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;

            Marshal.StructureToPtr(minMax, lParam, true);
            handled = true;
        }
        catch (Exception)
        {
            // Bei einem Fehler bleibt es beim Standardverhalten von Windows.
        }

        return IntPtr.Zero;
    }
}
