using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;

using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Zeigt ein Messbild (W2d) - Waveform, RGB-Parade oder Vektorskop - mit seinem Raster: bei der
/// Waveform die Viertel der Helligkeit, beim Vektorskop der Kreis, die Farbziele bei 75 % und die
/// Linie der Hauttoene. Das Bild selbst rechnet <see cref="Scopes"/>.
/// </summary>
public sealed class ScopeView : FrameworkElement
{
    private ScopeImage? _image;
    private WriteableBitmap? _bitmap;

    public ScopeView()
    {
        Height = 110;
        ClipToBounds = true;
    }

    /// <summary>Ob schon ein Messbild da ist - fuer die Probe.</summary>
    internal ScopeImage? Shown => _image;

    public void Show(ScopeImage image)
    {
        _image = image;

        if (_bitmap is null || _bitmap.PixelWidth != image.Width || _bitmap.PixelHeight != image.Height)
            _bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);

        _bitmap.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);

        Height = image.Kind == ScopeKind.Vectorscope ? 150 : 110;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, Height);

    protected override void OnRender(DrawingContext context)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), null, new Rect(0, 0, w, h));

        if (_image is not { } image || _bitmap is null) return;

        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1);
        line.Freeze();

        if (image.Kind == ScopeKind.Vectorscope)
        {
            // Quadratisch, in der Mitte.
            double side = Math.Min(w, h);
            var area = new Rect((w - side) / 2, (h - side) / 2, side, side);

            context.DrawImage(_bitmap, area);

            var centre = new Point(area.X + side / 2, area.Y + side / 2);
            context.DrawEllipse(null, line, centre, side / 2 - 0.5, side / 2 - 0.5);
            context.DrawLine(line, new Point(area.X, centre.Y), new Point(area.Right, centre.Y));
            context.DrawLine(line, new Point(centre.X, area.Y), new Point(centre.X, area.Bottom));

            // Die Farbziele bei 75 %, wie auf einem Vektorskop: R, Gb (Gelb), G, C, B, M.
            foreach (var (r, g, b) in new (byte, byte, byte)[] { (191, 0, 0), (191, 191, 0), (0, 191, 0), (0, 191, 191), (0, 0, 191), (191, 0, 191) })
            {
                var at = Place(Scopes.Chroma(r, g, b), area);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), 1.2), new Rect(at.X - 3, at.Y - 3, 6, 6));
            }

            // Die Linie der Hauttoene: von der Mitte durch einen typischen Hautton bis zum Rand.
            var skin = Scopes.Chroma(225, 172, 145);
            float length = MathF.Sqrt(skin.Cb * skin.Cb + skin.Cr * skin.Cr);
            var edge = Place((skin.Cb / length * 0.5f, skin.Cr / length * 0.5f), area);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC0, 0xA0)), 1), centre, edge);
            return;
        }

        context.DrawImage(_bitmap, new Rect(0, 0, w, h));

        // Die Viertel der Helligkeit: 0, 25, 50, 75, 100 %.
        for (int i = 0; i <= 4; i++)
        {
            double y = Math.Round(h - 1 - (h - 1) * i / 4.0) + 0.5;
            context.DrawLine(line, new Point(0, y), new Point(w, y));
        }

        // Die Parade in drei Teilen.
        if (image.Kind == ScopeKind.Parade)
        {
            for (int i = 1; i < 3; i++)
            {
                double x = Math.Round(w * i / 3.0) + 0.5;
                context.DrawLine(line, new Point(x, 0), new Point(x, h));
            }
        }
    }

    private static Point Place((float Cb, float Cr) chroma, Rect area)
        => new(area.X + (chroma.Cb + 0.5) * area.Width, area.Y + (0.5 - chroma.Cr) * area.Height);
}
