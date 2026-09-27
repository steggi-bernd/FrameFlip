using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FrameFlip.Configuration;
using FrameFlip.Views;

using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Die Vorschau der Uebersicht (docs/Atelier-Arbeitsablauf.md, Punkt 17): Ein Bild, das den
/// abgerundeten Rahmen ganz fuellt, steht nicht ueber dessen Ecken - in jedem Format und nach
/// jeder Groessenaenderung. Geprueft wird am Pixel: in der Ecke der Grund, in der Mitte das Bild.
/// Ein weisses Probebild, keine Medien.
/// </summary>
public static class RoundedClipInvariants
{
    public static void Run()
    {
        Check.Group("Uebersicht: das Bild bleibt in den runden Ecken seines Rahmens");

        TheHelperFollowsTheSize();
        TheStageKeepsItsCorners();
    }

    /// <summary>Der Baustein allein: quer, hoch, und nach einer Groessenaenderung.</summary>
    private static void TheHelperFollowsTheSize()
    {
        var content = new Grid { Children = { new Rectangle { Fill = Brushes.White } } };
        RoundedClip.SetRadius(content, 9);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Black,
            Child = content,
        };

        foreach (var (width, height) in new[] { (320.0, 180.0), (140.0, 260.0), (400.0, 100.0) })
        {
            frame.Measure(new Size(width, height));
            frame.Arrange(new Rect(0, 0, width, height));
            frame.UpdateLayout();

            var pixels = Pixels(frame, (int)width, (int)height);

            Check.That(!White(pixels, (int)width, 2, 2) && !White(pixels, (int)width, (int)width - 3, (int)height - 3),
                       $"{width:0} x {height:0}: in den Ecken steht nicht das Bild");
            Check.That(White(pixels, (int)width, (int)width / 2, (int)height / 2) && White(pixels, (int)width, 12, (int)height / 2),
                       $"{width:0} x {height:0}: in der Mitte und am Rand zwischen den Ecken steht es");
        }
    }

    /// <summary>Die Buehne im Hauptfenster, mit einem weissen Bild in zwei Formaten.</summary>
    private static void TheStageKeepsItsCorners()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "frameflip-ecken-" + Guid.NewGuid().ToString("N")[..8]);
        string? previous = Environment.GetEnvironmentVariable("FRAMEFLIP_CONFIG");
        var previousContext = SynchronizationContext.Current;

        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", System.IO.Path.Combine(root, "config.json"));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        var settings = new AppSettings { Prebuffer = false };
        var window = new MainWindow(null, () => null, () => { }, _ => { }, () => { }, settings,
                                    _ => { }, _ => null, () => settings, () => null, () => { }, _ => { })
        {
            Width = 1300, Height = 820, Left = -4000, Top = -4000, ShowInTaskbar = false, ShowActivated = false,
        };

        try
        {
            window.Show();

            var stage = (Border)window.FindName("StageFrame");
            var content = (FrameworkElement)window.FindName("StageContent");
            var image = (Image)window.FindName("StageImage");

            foreach (var (width, height) in new[] { (1000.0, 562.0), (562.0, 1000.0) })
            {
                // Im Seitenverhaeltnis des Rahmens - so fuellt das Bild ihn bis in die Ecken, wie ein
                // Frame der Folge. Ein quadratisches Bild stuende mittig und erreichte sie nie.
                int bw = (int)(width / 5), bh = (int)(height / 5);
                var white = BitmapSource.Create(bw, bh, 96, 96, PixelFormats.Bgra32, null,
                                                Enumerable.Repeat((byte)255, bw * bh * 4).ToArray(), bw * 4);
                image.Source = white;
                stage.Width = width;
                stage.Height = height;

                for (int i = 0; i < 4; i++)
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    window.UpdateLayout();
                }

                // Der Rahmen allein, in seiner eigenen Groesse - ueber einen VisualBrush, damit die
                // Viewbox darueber nicht mitzeichnet. Sie verkleinert die Buehne je nach Bildschirm,
                // auf dem kleinen der CI bis auf ein Viertel; die Ecke verschmolz dann mit der
                // Randlinie, und die Probe mass nichts mehr.
                int w = (int)Math.Ceiling(stage.ActualWidth), h = (int)Math.Ceiling(stage.ActualHeight);
                var pixels = Snapshot(stage, w, h);

                // Die Ecke des Inhalts - zwei Punkte hinein, in Punkten des Rahmens.
                Point At(double x, double y) => content.TranslatePoint(new Point(x, y), stage);

                var corner = At(2, 2);
                var middle = At(content.ActualWidth / 2, content.ActualHeight / 2);

                Check.That(content.Clip is RectangleGeometry { RadiusX: > 0 } clip &&
                           Math.Abs(clip.Rect.Width - content.ActualWidth) < 0.5 && Math.Abs(clip.Rect.Height - content.ActualHeight) < 0.5,
                           $"{width:0} x {height:0}: der Zuschnitt hat die Groesse des Inhalts und runde Ecken");
                Check.That(!White(pixels, w, (int)corner.X, (int)corner.Y) && White(pixels, w, (int)middle.X, (int)middle.Y),
                           $"{width:0} x {height:0}: in der Ecke steht nicht das Bild, in der Mitte schon",
                           $"Ecke bei {corner.X:0},{corner.Y:0}");
            }
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            Environment.SetEnvironmentVariable("FRAMEFLIP_CONFIG", previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>Zeichnet ein Element, das selbst die Wurzel ist - ohne Fenster darum.</summary>
    private static byte[] Pixels(FrameworkElement element, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        return pixels;
    }

    /// <summary>
    /// Zeichnet ein Element in einem Fenster in seiner eigenen Groesse - ueber einen VisualBrush,
    /// so dass weder sein Versatz im Fenster noch eine Viewbox darueber mitzeichnet.
    /// </summary>
    private static byte[] Snapshot(FrameworkElement element, int width, int height)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                  null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        return pixels;
    }

    private static bool White(byte[] pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return pixels[i] > 240 && pixels[i + 1] > 240 && pixels[i + 2] > 240;
    }
}
