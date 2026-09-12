using System.Windows.Controls;
using FrameFlip.Configuration;
using FrameFlip.Remote;
namespace FrameFlip.Views;
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly Func<AppSettings> _current;
    private readonly Func<AppSettings, string?> _apply;
    private readonly Func<RelayState?> _state;
    private readonly DesktopLayout _layout;
    private SettingsEditor _editor = null!;
    public SettingsPage(Func<AppSettings> current, Func<AppSettings, string?> apply, Func<RelayState?> state, DesktopLayout layout)
    { _current = current; _apply = apply; _state = state; _layout = layout; InitializeComponent(); Rebuild(); }
    private void Rebuild()
    {
        _editor?.Dispose();
        _editor = new SettingsEditor(_current(), _apply, _state, _current, _layout);
        _editor.Cancelled += Rebuild; EditorHost.Content = _editor;
    }
    public void SelectRemote() => _editor.SelectRemote();
    public void SelectAppearance() => _editor.SelectAppearance();
    public void Dispose() => _editor.Dispose();
}