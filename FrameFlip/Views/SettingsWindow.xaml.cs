using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Remote;
namespace FrameFlip.Views;
public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings current, Func<AppSettings, string?> apply, Func<RelayState?>? remoteState = null)
    {
        InitializeComponent();
        var editor = new SettingsEditor(current, apply, remoteState);
        EditorHost.Content = editor; editor.Saved += Close; editor.Cancelled += Close;
        Closed += (_, _) => editor.Dispose();
    }
}