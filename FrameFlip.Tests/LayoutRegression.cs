using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Der eigentliche Regressionstest zum Zoom-Fehler.
///
/// Ursache war nicht die Zoom-Mathematik, sondern der Container: ein Grid arrangiert
/// sein Kind in der Zellgroesse und setzt, sobald das Kind groesser ist, einen
/// Layout-Clip. Dieser Clip greift in den Koordinaten des Kindes, also VOR der
/// RenderTransform - das anschliessend verschobene Bild war rechts und unten um
/// genau den Betrag des Versatzes abgeschnitten, und der Versatz waechst mit dem
/// Zoom. Ein Canvas misst seine Kinder unbegrenzt und arrangiert sie in voller
/// Groesse; es entsteht kein Layout-Clip.
///
/// Der Test rendert wirklich und zaehlt Pixel. Eine Zusicherung auf Matrixwerte
/// haette den Fehler nicht gefunden - die Matrix war die ganze Zeit richtig.
/// </summary>
public static class LayoutRegression
{
    private const double ViewportW = 731.4;
    private const double ViewportH = 396.0;

    public static void Run()
    {
        Check.Group("Regression - verschobener Inhalt fuellt die Anzeige vollstaendig");

        foreach (double zoom in new[] { 1.0, 1.5, 2.5, 4.0, 8.0 })
        {
            double scale = 0.5714 * zoom;
            double contentW = 1920 * scale, contentH = 1080 * scale;

            // Zentriert bei groesserem Inhalt heisst: negativer Versatz in beide
            // Richtungen. Das ist der Zustand, in dem der Fehler sichtbar wurde.
            double offsetX = (ViewportW - contentW) / 2;
            double offsetY = (ViewportH - contentH) / 2;

            var (right, bottom) = RenderAndMeasure(scale, offsetX, offsetY);

            Check.That(right <= 1,
                $"kein Rand rechts bei {zoom * 100:0} % Zoom",
                $"{right:0} DIP leer, Versatz {offsetX:0}");
            Check.That(bottom <= 1,
                $"kein Rand unten bei {zoom * 100:0} % Zoom",
                $"{bottom:0} DIP leer, Versatz {offsetY:0}");
        }
    }

    /// <summary>
    /// Baut den Anzeigebaum aus ViewerWindow.xaml nach, rendert ihn und misst, wie
    /// viele DIP am rechten und unteren Rand nicht vom Bild bedeckt sind.
    /// </summary>
    private static (double right, double bottom) RenderAndMeasure(double scale, double offsetX, double offsetY)
    {
        const int bmpW = 1920, bmpH = 1080;

        var bitmap = new WriteableBitmap(bmpW, bmpH, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[bmpW * 4 * bmpH];
        Array.Fill(pixels, (byte)0xFF);
        bitmap.WritePixels(new Int32Rect(0, 0, bmpW, bmpH), pixels, bmpW * 4, 0);

        var display = new Image
        {
            Source = bitmap,
            Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = new MatrixTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY)),
        };

        var stage = new Canvas { ClipToBounds = true };
        stage.Children.Add(display);

        var viewport = new Grid { ClipToBounds = true, Background = Brushes.Black };
        viewport.Children.Add(stage);

        // Wie im Fenster: der Anzeigebereich liegt in einer *-Zeile unter der Kopfleiste.
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(viewport);

        root.Measure(new Size(ViewportW, ViewportH));
        root.Arrange(new Rect(0, 0, ViewportW, ViewportH));
        root.UpdateLayout();

        int w = (int)Math.Round(ViewportW), h = (int)Math.Round(ViewportH);
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);

        var rendered = new byte[w * 4 * h];
        target.CopyPixels(rendered, w * 4, 0);

        bool Covered(int x, int y) => rendered[(y * w + x) * 4 + 1] > 100;

        int right = w - 1;
        while (right >= 0 && !Covered(right, h / 2)) right--;

        int bottom = h - 1;
        while (bottom >= 0 && !Covered(w / 2, bottom)) bottom--;

        return (w - 1 - right, h - 1 - bottom);
    }
}
