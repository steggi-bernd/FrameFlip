using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Imaging.Grading;

using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace FrameFlip.Views;

/// <summary>Was der Greifrahmen gerade bedient.</summary>
public enum AdornerMode
{
    /// <summary>Verschieben, drehen, skalieren.</summary>
    Place,

    /// <summary>Zuschneiden: an den Kanten der Ebene ziehen.</summary>
    Crop,
}

/// <summary>
/// Zuschneiden durch Ziehen.
///
/// Die Zahlen dafuer gab es schon - vier Regler im Ebenenstreifen. Regler sind fuer
/// einen Schnitt aber das falsche Werkzeug: Man schneidet nach dem, was man SIEHT,
/// und dafuer muss der Blick am Bild bleiben. Wer zwischen Regler und Bild hin und
/// her sieht, trifft die Kante beim dritten Versuch.
///
/// Gerechnet wird in der eigenen Ebene der Ebene: Die vier Ecken des vollen Rahmens
/// spannen ein Koordinatensystem auf, und jeder Punkt darin ist eine Linearkombination
/// davon. Damit stimmt der Schnitt auch bei gedrehter und kleingezogener Ebene, ohne
/// dass Drehung und Massstab hier noch einmal vorkommen.
/// </summary>
public sealed partial class PlacementAdorner
{
    /// <summary>Welche Kante oder Ecke gerade gezogen wird.</summary>
    private CropGrip _cropDrag = CropGrip.None;

    private CropGrip _cropHover = CropGrip.None;

    internal enum CropGrip
    {
        None,
        Left,
        Top,
        Right,
        Bottom,
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
    }

    // ------------------------------------------------------------------ Zeichnen

    private void RenderCrop(DrawingContext context)
    {
        var box = Box();

        box.Corners(out float x0, out float y0, out float x1, out float y1,
                    out float x2, out float y2, out float x3, out float y3);

        var tl = new Point(x0, y0);
        var e1 = new Vector(x1 - x0, y1 - y0);
        var e2 = new Vector(x3 - x0, y3 - y0);

        var place = _transform!;

        float left = place.CropLeft, top = place.CropTop;
        float right = 1f - place.CropRight, bottom = 1f - place.CropBottom;

        // Der volle Rahmen bleibt zu sehen, nur gedaempft: Er sagt, WOVON
        // abgeschnitten wird. Ohne ihn sieht ein Schnitt aus wie eine kleine Ebene,
        // und man weiss nicht mehr, wieviel noch da ist.
        Outline(context, Faint, tl, e1, e2, 0f, 0f, 1f, 1f);
        Outline(context, Shadow, tl, e1, e2, left, top, right, bottom);
        Outline(context, Line, tl, e1, e2, left, top, right, bottom);

        foreach (var (grip, u, v) in Grips(left, top, right, bottom))
        {
            var at = At(tl, e1, e2, u, v);
            bool lit = _cropDrag != CropGrip.None ? _cropDrag == grip : _cropHover == grip;

            context.DrawRectangle(lit ? Hot : Handle, new Pen(HandleEdge, 1),
                                  new Rect(at.X - GripSize, at.Y - GripSize,
                                           GripSize * 2, GripSize * 2));
        }
    }

    private static readonly Pen Faint = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 1);

    /// <summary>Die acht Griffe: vier Ecken und vier Kantenmitten.</summary>
    private static IEnumerable<(CropGrip Grip, float U, float V)> Grips(
        float left, float top, float right, float bottom)
    {
        float midX = (left + right) / 2f;
        float midY = (top + bottom) / 2f;

        yield return (CropGrip.TopLeft, left, top);
        yield return (CropGrip.TopRight, right, top);
        yield return (CropGrip.BottomRight, right, bottom);
        yield return (CropGrip.BottomLeft, left, bottom);
        yield return (CropGrip.Left, left, midY);
        yield return (CropGrip.Right, right, midY);
        yield return (CropGrip.Top, midX, top);
        yield return (CropGrip.Bottom, midX, bottom);
    }

    private void Outline(DrawingContext context, Pen pen, Point tl, Vector e1, Vector e2,
                         float left, float top, float right, float bottom)
    {
        var shape = new StreamGeometry();

        using (var draw = shape.Open())
        {
            draw.BeginFigure(At(tl, e1, e2, left, top), false, true);
            draw.PolyLineTo(new[]
            {
                At(tl, e1, e2, right, top),
                At(tl, e1, e2, right, bottom),
                At(tl, e1, e2, left, bottom),
            }, true, false);
        }

        shape.Freeze();
        context.DrawGeometry(null, pen, shape);
    }

    /// <summary>Ein Punkt der Ebene, in Anteilen ihrer Kanten - auf der Flaeche.</summary>
    private Point At(Point tl, Vector e1, Vector e2, float u, float v)
        => Screen((float)(tl.X + e1.X * u + e2.X * v),
                  (float)(tl.Y + e1.Y * u + e2.Y * v));

    // ---------------------------------------------------------------- Bedienung

    /// <summary>
    /// Rechnet einen Punkt der Leinwand in Anteile der Ebene um.
    ///
    /// Die Umkehrung der Zeichnung: P = Ecke + u*Kante1 + v*Kante2. Zwei Unbekannte,
    /// zwei Gleichungen, und das Kreuzprodukt loest sie in einer Zeile. Weil die
    /// Abbildung affin ist, gilt sie auch ausserhalb des Rahmens - und das ist
    /// wichtig: Wer eine Kante nach aussen zieht, zieht ueber den Rand hinaus.
    /// </summary>
    private bool Local(float x, float y, out float u, out float v)
    {
        u = v = 0f;

        var box = Box();

        box.Corners(out float x0, out float y0, out float x1, out float y1,
                    out float _, out float _, out float x3, out float y3);

        double e1x = x1 - x0, e1y = y1 - y0;
        double e2x = x3 - x0, e2y = y3 - y0;

        double area = e1x * e2y - e1y * e2x;
        if (Math.Abs(area) < 1e-6) return false;

        double dx = x - x0, dy = y - y0;

        u = (float)((dx * e2y - dy * e2x) / area);
        v = (float)((e1x * dy - e1y * dx) / area);

        return true;
    }

    private CropGrip CropAt(float x, float y)
    {
        var place = _transform!;

        float left = place.CropLeft, top = place.CropTop;
        float right = 1f - place.CropRight, bottom = 1f - place.CropBottom;

        var box = Box();

        box.Corners(out float x0, out float y0, out float x1, out float y1,
                    out float _, out float _, out float x3, out float y3);

        var tl = new Point(x0, y0);
        var e1 = new Vector(x1 - x0, y1 - y0);
        var e2 = new Vector(x3 - x0, y3 - y0);

        // Gesucht wird auf der FLAECHE und nicht in Anteilen: Ein Fangbereich in
        // Anteilen waere bei einer kleingezogenen Ebene winzig und bei einer grossen
        // riesig, und die Maus zielt in Schirmpunkten.
        var here = Screen(x, y);

        double best = Reach() * ReachOnScreen;
        var found = CropGrip.None;

        foreach (var (grip, u, v) in Grips(left, top, right, bottom))
        {
            var at = At(tl, e1, e2, u, v);
            double distance = (at - here).Length;

            if (distance > best) continue;

            best = distance;
            found = grip;
        }

        return found;
    }

    /// <summary>
    /// Wieviel Schirmpunkte ein Punkt der Leinwand ist - fuer den Fangbereich.
    ///
    /// <see cref="Reach"/> rechnet andersherum, von Schirmpunkten in Bildpunkte. Hier
    /// wird beides gebraucht, weil die Griffe auf der Flaeche gesucht werden.
    /// </summary>
    private double ReachOnScreen => _canvasWidth <= 0 ? 1 : ActualWidth / _canvasWidth;

    private void CropDown(MouseButtonEventArgs e, float x, float y)
    {
        // Doppelklick nimmt den Schnitt zurueck - derselbe Griff wie beim
        // Verschieben, an derselben Stelle.
        if (e.ClickCount == 2)
        {
            var reset = _transform!.Clone();

            reset.CropLeft = reset.CropTop = reset.CropRight = reset.CropBottom = 0f;

            _transform = reset;
            _cropDrag = CropGrip.None;

            Changed?.Invoke(reset, false);

            e.Handled = true;
            InvalidateVisual();

            return;
        }

        _cropDrag = CropAt(x, y);

        if (_cropDrag == CropGrip.None) return;

        e.Handled = true;
        CaptureMouse();
        InvalidateVisual();
    }

    private void CropMove(float x, float y)
    {
        if (_cropDrag == CropGrip.None)
        {
            var over = CropAt(x, y);

            if (over != _cropHover)
            {
                _cropHover = over;
                InvalidateVisual();
            }

            Cursor = over switch
            {
                CropGrip.Left or CropGrip.Right => Cursors.SizeWE,
                CropGrip.Top or CropGrip.Bottom => Cursors.SizeNS,
                CropGrip.TopLeft or CropGrip.BottomRight => Cursors.SizeNWSE,
                CropGrip.TopRight or CropGrip.BottomLeft => Cursors.SizeNESW,
                _ => null,
            };

            return;
        }

        if (!Local(x, y, out float u, out float v)) return;

        _transform = WithCrop(_transform!, _cropDrag, u, v);

        Changed?.Invoke(_transform, true);
        InvalidateVisual();
    }

    private void CropUp(MouseButtonEventArgs e)
    {
        if (_cropDrag == CropGrip.None) return;

        _cropDrag = CropGrip.None;
        ReleaseMouseCapture();

        e.Handled = true;

        if (_transform is not null) Changed?.Invoke(_transform, false);

        InvalidateVisual();
    }

    /// <summary>
    /// Setzt die gezogene Kante - und laesst immer einen Rest stehen.
    ///
    /// Ein Schnitt, der die Ebene auf null zusammenzieht, waere eine Ebene, die es
    /// noch gibt, die aber nichts mehr zeigt und deren Griffe alle aufeinanderliegen.
    /// Aus der kaeme man nur noch mit dem Doppelklick heraus, und den muesste man
    /// erst kennen.
    /// </summary>
    internal static LayerTransform WithCrop(LayerTransform place, CropGrip grip, float u, float v)
    {
        const float Least = 0.02f;

        var next = place.Clone();

        bool left = grip is CropGrip.Left or CropGrip.TopLeft or CropGrip.BottomLeft;
        bool right = grip is CropGrip.Right or CropGrip.TopRight or CropGrip.BottomRight;
        bool top = grip is CropGrip.Top or CropGrip.TopLeft or CropGrip.TopRight;
        bool bottom = grip is CropGrip.Bottom or CropGrip.BottomLeft or CropGrip.BottomRight;

        if (left) next.CropLeft = Math.Clamp(u, 0f, 1f - next.CropRight - Least);
        if (right) next.CropRight = Math.Clamp(1f - u, 0f, 1f - next.CropLeft - Least);
        if (top) next.CropTop = Math.Clamp(v, 0f, 1f - next.CropBottom - Least);
        if (bottom) next.CropBottom = Math.Clamp(1f - v, 0f, 1f - next.CropTop - Least);

        return next;
    }
}
