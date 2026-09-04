using System.Runtime.InteropServices;

namespace FrameFlip.Interop;

internal static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    /// <summary>Verhindert, dass eine gehaltene Hotkey-Kombination wiederholt ausloest.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out WindowRect rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }

    /// <summary>
    /// Setzt die Fenstergroesse und zentriert es anschliessend auf seinem Monitor.
    ///
    /// Bewusst ueber relative Korrekturen statt ueber eine Umrechnung: auf gemischt
    /// skalierten Systemen liegen Fenstergroesse, Monitorabfrage und WPF-Koordinaten
    /// in unterschiedlichen Raeumen. Ein Regelkreis aus Messen und Nachziehen kommt
    /// ohne Annahmen ueber den Skalierungsfaktor aus.
    /// </summary>
    public static void SizeAndCenter(IntPtr window, int width, int height)
    {
        try
        {
            SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOMOVE);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!GetWindowRect(window, out var rect)) return;

                IntPtr monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

                int currentWidth = rect.Right - rect.Left;
                int currentHeight = rect.Bottom - rect.Top;

                int targetX = info.Work.Left + (info.Work.Right - info.Work.Left - currentWidth) / 2;
                int targetY = info.Work.Top + (info.Work.Bottom - info.Work.Top - currentHeight) / 2;

                if (Math.Abs(rect.Left - targetX) <= 2 && Math.Abs(rect.Top - targetY) <= 2) return;

                SetWindowPos(window, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOZORDER | SWP_NOSIZE);
            }
        }
        catch (Exception)
        {
            // Bleibt das Fenster, wo Windows es hingestellt hat.
        }
    }

    [DllImport("kernel32.dll")]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ---------------------------------------------------------------- DPI

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    /// <summary>
    /// Muss vor dem ersten Fenster laufen. Das Manifest allein reicht im
    /// Single-File-Build nicht - ohne diesen Aufruf virtualisiert Windows saemtliche
    /// Koordinaten, GetMonitorInfo liefert skalierte Werte und die Fensterlage
    /// stimmt auf hochaufloesenden Monitoren nicht.
    /// </summary>
    public static void EnablePerMonitorDpiAwareness()
    {
        // Reihenfolge von neu nach alt; scheitert der Aufruf, ist die Awareness
        // bereits gesetzt (etwa durch das Manifest) und alles ist gut.
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
        try { if (SetProcessDpiAwareness(2) == 0) return; } catch (Exception) { }
        try { SetProcessDPIAware(); } catch (Exception) { }
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr window, ref POINT point);

    /// <summary>
    /// Zeigerposition in Clientkoordinaten des Fensters, direkt von Win32.
    /// Auf skalierten Systemen weicht der WPF-Eingabepfad davon ab.
    /// </summary>
    public static bool TryGetCursorInClient(IntPtr window, out double x, out double y)
    {
        x = 0; y = 0;
        try
        {
            if (!GetCursorPos(out POINT point)) return false;
            if (!ScreenToClient(window, ref point)) return false;
            x = point.X;
            y = point.Y;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>
    /// Arbeitsflaeche und Skalierung des Monitors, auf dem das Fenster liegt (oder,
    /// ohne Fenster, der Mauszeiger). Bewusst ueber GetMonitorInfo statt ueber
    /// System.Windows.Forms.Screen: dessen Werte sind in diesem Prozess bereits
    /// skaliert, was die Umrechnung zweimal anwenden wuerde.
    /// </summary>
    public static (PixelRect Work, double Scale) GetWorkArea(IntPtr window)
    {
        var fallback = (new PixelRect(0, 0, 1920, 1080), 1.0);

        try
        {
            IntPtr monitor;
            if (window != IntPtr.Zero)
            {
                monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
            }
            else
            {
                GetCursorPos(out POINT cursor);
                monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
            }

            if (monitor == IntPtr.Zero) return fallback;

            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return fallback;

            double scale = 1.0;
            if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;

            var work = new PixelRect(
                info.Work.Left,
                info.Work.Top,
                info.Work.Right - info.Work.Left,
                info.Work.Bottom - info.Work.Top);

            return (work, scale);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    // ---------------------------------------------------------------- Fensterrahmen

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;

    /// <summary>
    /// Abgerundete Ecken, dunkler Rahmen und dunkle Titelfarbe. Ohne das zeichnet DWM
    /// oben einen hellen Streifen, weil der Rahmen der Systemfarbe folgt.
    /// Attribute, die es auf der jeweiligen Windows-Version nicht gibt, werden ignoriert.
    /// </summary>
    public static void ApplyDarkWindowFrame(IntPtr window)
    {
        Set(window, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(window, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

        // COLORREF: 0x00BBGGRR, passend zum Fensterhintergrund #171717
        Set(window, DWMWA_BORDER_COLOR, 0x00171717);
        Set(window, DWMWA_CAPTION_COLOR, 0x00202020);
    }

    private static void Set(IntPtr window, int attribute, int value)
    {
        try { DwmSetWindowAttribute(window, attribute, ref value, sizeof(int)); }
        catch (Exception) { /* aeltere Windows-Version */ }
    }

    // ---------------------------------------------------------------- Systemlast

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    // ---------------------------------------------------------------- PDH (GPU-Zaehler)

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    public struct PdhCounterValueItem
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
                                                           out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);
}
