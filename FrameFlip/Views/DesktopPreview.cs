using System.IO;
using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Diagnostics;
using FrameFlip.Interop;
using FrameFlip.Localization;
using FrameFlip.Sequencing;
namespace FrameFlip.Views;
/// <summary>Isolierter UI-Einstieg ohne Hotkey, Bridge-Port, Relay oder Tray der Arbeitsinstanz.</summary>
internal static class DesktopPreview
{
    public static void Start()
    {
        if (string.IsNullOrWhiteSpace(SettingsStore.Override))
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", Path.Combine(AppContext.BaseDirectory, "ui-data", "config.json"));
        var current = SettingsStore.Load();
        if (!File.Exists(SettingsStore.FilePath)) { current.MainWidth = 1240; current.MainHeight = 820; }
        Strings.Apply(Strings.Parse(current.Language));
        var decoders = FrameDecoderRegistry.CreateDefault();
        ViewerWindow? viewer = null;
        MainWindow? main = null;
        var load = new SystemLoadMonitor(2, TimeSpan.FromSeconds(2));
        LivePage.Load = () => load.LastSnapshot;
        AtelierPage.Workers = () => Math.Clamp(load.MaxDecoderThreads, 1, 16);
        load.Start();
        async void Open(string path)
        {
            try
            {
                var sequence = await Task.Run(() => SequenceScanner.Scan(path, decoders));
                if (main?.IsLoaded != true || sequence is null || sequence.Count == 0) return;
                if (!decoders.TryProbeSize(path, out int width, out int height)) return;
                int index = sequence.Frames.ToList().FindIndex(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
                var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(main).Handle).WorkingArea;
                viewer?.Close();
                viewer = new ViewerWindow(sequence, Math.Max(0, index), current, SettingsStore.Save, decoders,
                    new PixelRect(screen.X, screen.Y, screen.Width, screen.Height), width, height, 2);
                viewer.SettingsRequested = () => { main.ShowSettingsPage(); main.Activate(); };
                viewer.Show();
            }
            catch (Exception error) { MessageBox.Show(main, error.Message, "FrameFlip", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        string? Apply(AppSettings next)
        {
            next.Normalize(); current = next; SettingsStore.Save(next); Strings.Apply(Strings.Parse(next.Language)); return null;
        }
        main = new MainWindow(null, () => null, () => { }, Open, () => { }, current,
            SettingsStore.Save, Apply, () => current);
        main.Title = "FrameFlip · Desktop UI";
        main.PreviewBadge.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "D_Preview");
        main.FooterHint.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "D_PreviewHint");
        main.Closed += (_, _) => { viewer?.Close(); load.Dispose(); LivePage.Load = () => null; Application.Current.Shutdown(); };
        Application.Current.MainWindow = main;
        main.Show();
    }
}