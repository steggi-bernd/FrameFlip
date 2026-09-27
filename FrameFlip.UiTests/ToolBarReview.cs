using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

internal static partial class Program
{
    /// <summary>
    /// Die Einstellungsleiste des Pinsels, als Bild zum Ansehen (Entscheidung 9): in Gruppen,
    /// mit gezeichneten Zeichen, breit und schmal - und beim Rechteck nur, was zum Fuellen
    /// gehoert. Nur die Leiste selbst, keine Medien.
    /// </summary>
    private static void TestToolBar()
    {
        foreach (var (area, width, name) in new[]
                 {
                     (PaintArea.None, 1500, "Leiste-Pinsel-breit.png"),
                     (PaintArea.None, 1000, "Leiste-Pinsel-schmal.png"),
                     (PaintArea.Rectangle, 1000, "Leiste-Rechteck.png"),
                 })
        {
            var panel = new PropertiesPanel();
            panel.Show(AtelierTool.Brush);
            panel.ChooseBrush(area, null);

            var host = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)),
                Padding = new Thickness(8),
                Child = panel,
            };

            host.Measure(new Size(width, double.PositiveInfinity));
            int height = (int)Math.Ceiling(host.DesiredSize.Height);

            Render(host, width, height, name);

            // Gruppen brechen nur zwischen sich um: Jeder Regler steht auf derselben Hoehe wie
            // sein Name davor.
            var size = (FrameworkElement)panel.FindName("BrushSizeSlider");
            var hardness = (FrameworkElement)panel.FindName("BrushHardnessSlider");

            if (area == PaintArea.None)
            {
                double y(FrameworkElement e) => e.TranslatePoint(new Point(0, e.ActualHeight / 2), host).Y;
                Check(Math.Abs(y(size) - y(hardness)) < 1, $"{name}: Groesse und Haerte stehen in einer Zeile - die Gruppe bricht nicht");
                if (width >= 1500) Check(height < 100, $"{name}: breit hoechstens zwei Zeilen ({height})");
            }
            else
            {
                Check(!size.IsVisible && height < 60, $"{name}: beim Rechteck nur eine Zeile, ohne Spitze ({height})");
            }
        }
    }
}
