using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Views;

internal static partial class Program
{
    /// <summary>
    /// Das Atelier ohne Bild, als Bild zum Ansehen (Entscheidung 10): die Werkzeugeinstellungen
    /// oben als Leiste mit ihrem Griff - und an die rechte Seite gezogen, wo die Gruppen
    /// untereinander stehen. Keine Medien, nur die leere Flaeche.
    /// </summary>
    private static void TestAtelierDock()
    {
        var page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), new AppSettings(), _ => { });
        var window = new Window
        {
            Content = page,
            Width = 1400,
            Height = 860,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x13, 0x1A)),
        };

        try
        {
            window.Show();

            var tool = (PropertiesPanel)page.FindName("Properties");
            var dock = (DockHost)page.FindName("Dock");

            tool.Show(AtelierTool.Brush);
            page.UpdateLayout();
            Render(page, 1400, 860, "Atelier-Werkzeug-oben.png");

            Check(dock.Layout.Find("tool") is { Zone: DockZone.Top }, "Atelier: die Werkzeugeinstellungen stehen oben");

            dock.MovePanel("tool", DockZone.Right, 0, asTab: false);
            page.UpdateLayout();
            Render(page, 1400, 860, "Atelier-Werkzeug-rechts.png");

            Check(dock.Layout.Find("tool") is { Zone: DockZone.Right }, "Atelier: an die rechte Seite gezogen");

            dock.ResetLayout();
        }
        finally
        {
            window.Close();
        }
    }
}
