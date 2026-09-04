using System.Windows.Interop;

namespace FrameFlip.Interop;

/// <summary>
/// Globaler Hotkey ueber RegisterHotKey. Haengt an einem unsichtbaren Top-Level-Fenster,
/// damit WM_HOTKEY zuverlaessig zugestellt wird.
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0xB1FF;

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public event Action? Pressed;

    public HotKeyService()
    {
        var parameters = new HwndSourceParameters("FrameFlip.HotKeyWindow")
        {
            WindowStyle = 0,               // kein WS_VISIBLE -> Fenster bleibt unsichtbar
            ExtendedWindowStyle = 0x0080,  // WS_EX_TOOLWINDOW
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = 1
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public HotKeyDefinition Current { get; private set; }

    /// <summary>Registriert neu. Gibt false zurueck, wenn die Kombination bereits belegt ist.</summary>
    public bool Register(HotKeyDefinition definition)
    {
        Unregister();
        if (!definition.IsValid) return false;

        if (NativeMethods.RegisterHotKey(_source.Handle, HotKeyId, definition.NativeModifiers, definition.VirtualKey))
        {
            _registered = true;
            Current = definition;
            return true;
        }

        return false;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotKeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
