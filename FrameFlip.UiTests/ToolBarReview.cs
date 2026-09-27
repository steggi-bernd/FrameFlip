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
        // Die Hinweise im Stil der Oberflaeche: dunkel, gerundet, und ein langer Satz bricht um.
        var tip = new ToolTip { Content = string.Join(" ", Enumerable.Repeat("Ein langer Hinweis, der umbrechen muss.", 12)) };
        tip.Style = (Style)Application.Current.FindResource(typeof(ToolTip));
        tip.ApplyTemplate();

        // Ein Hinweis darf in keinem anderen Element stecken - gemessen und gezeichnet wird er allein.
        tip.Measure(new Size(1000, double.PositiveInfinity));
        Render(tip, (int)Math.Ceiling(tip.DesiredSize.Width), (int)Math.Ceiling(tip.DesiredSize.Height), "Hinweis.png");

        var frame = System.Windows.Media.VisualTreeHelper.GetChildrenCount(tip) > 0
            ? System.Windows.Media.VisualTreeHelper.GetChild(tip, 0) as Border
            : null;
        Check(frame is { CornerRadius.TopLeft: 6 } && tip.ActualWidth <= 341 && tip.ActualHeight > 40,
              $"Hinweis: im Stil der Oberflaeche, hoechstens 340 breit, der lange Satz bricht um ({tip.ActualWidth:0} x {tip.ActualHeight:0})");

        // Die Werkzeugleiste: Kategorien nur als Name, die gewaehlte gerahmt; die Werkzeuge in
        // ihrem eigenen Balken, rechts der Umschalter fuer Folge und Bild.
        var band = new ToolBand { Width = 1200 };
        band.ShowFrameSwitch(single: true, inSequence: false);
        var bandHost = new Border { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x13, 0x1A)), Padding = new Thickness(8), Child = band };
        bandHost.Measure(new Size(1216, double.PositiveInfinity));
        Render(bandHost, 1216, (int)Math.Ceiling(bandHost.DesiredSize.Height), "Werkzeugleiste.png");
        Check(((FrameworkElement)band.FindName("FrameSwitch")).Visibility == Visibility.Visible && !((UIElement)band.FindName("FrameAll")).IsEnabled, "Werkzeugleiste: der Umschalter steht auch beim Einzelbild da");

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
