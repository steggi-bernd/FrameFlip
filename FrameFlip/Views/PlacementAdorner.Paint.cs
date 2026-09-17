using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging.Grading;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace FrameFlip.Views;

/// <summary>
/// Der Pinsel: eine Maske von Hand auftragen.
///
/// Er liegt in demselben Element wie der Greif- und der Schnittrahmen, aus demselben
/// Grund: Die Umrechnung zwischen Schirm, Leinwand und Bildpunkt steht hier einmal.
/// Ein zweites Element daneben haette sie ein zweites Mal gebraucht, und zwei
/// Umrechnungen laufen frueher oder spaeter um einen Bildpunkt auseinander - dann
/// malt man neben der Stelle, auf die man gezeigt hat.
///
/// GEMALT WIRD SICHTBAR. Ohne eine Darstellung der Maske sieht man nur ihre WIRKUNG,
/// und die ist bei einer schwachen Ebene kaum zu erkennen - man malte ins Blinde und
/// merkte erst an der Deckkraft, wo man gewesen ist. Die Maske liegt deshalb als
/// farbiger Schleier ueber dem Bild, solange der Pinsel gewaehlt ist.
/// </summary>
public sealed partial class PlacementAdorner
{
    private PaintedMask? _mask;
    private bool _painting;
    private bool _erasing;
    private Point _lastStroke;

    private WriteableBitmap? _wash;
    private bool _washStale = true;

    /// <summary>Der Pinselradius in Bildpunkten der Leinwand.</summary>
    public float BrushRadius { get; set; } = 40f;

    /// <summary>Wie schnell ein Strich auftraegt. 0 bis 1.</summary>
    public float BrushFlow { get; set; } = 0.7f;

    /// <summary>Wie hart die Kante ist. 0 ist ein Verlauf, 1 eine Scheibe.</summary>
    public float BrushHardness { get; set; } = 0.5f;

    /// <summary>Bis wohin ein Strich ueberhaupt auftraegt. 0 bis 1.</summary>
    public float BrushOpacity { get; set; } = 1f;

    /// <summary>Es wurde gemalt. <c>interim</c> heisst: der Strich laeuft noch.</summary>
    public event Action<bool>? Painted;

    /// <summary>
    /// Woher eine Maske kommt, wenn noch keine da ist - beim ERSTEN Strich gefragt.
    ///
    /// Erst beim Strich und nicht beim Waehlen des Werkzeugs: Wer den Pinsel nur
    /// anfasst, um zu sehen, was er tut, soll keine Ebene erzeugt haben. Eine Ebene,
    /// die entsteht, weil jemand ein Werkzeug angeklickt hat, ist eine Ebene, die man
    /// hinterher wegraeumt.
    /// </summary>
    public Func<PaintedMask?>? MaskWanted { get; set; }

    /// <summary>
    /// Zeigt eine Maske zum Bemalen - oder null, um den Pinsel abzuschalten.
    ///
    /// Die Leinwandmasse kommen mit, weil der Pinsel in BILDpunkten rechnet und die
    /// Maus in Schirmpunkten zielt.
    /// </summary>
    public void Paint(PaintedMask? mask, int canvasWidth, int canvasHeight, bool uniform)
    {
        _mask = mask;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _uniform = uniform;
        _washStale = true;

        // Faengt auch OHNE Maske, solange jemand eine liefern kann: Sonst gaebe es
        // keinen ersten Strich, mit dem sie entstehen koennte - und der Ring am
        // Zeiger waere auch nicht zu sehen.
        IsHitTestVisible = mask is not null || MaskWanted is not null;

        InvalidateVisual();
    }

    /// <summary>Die Darstellung muss neu gebaut werden - nach einem Strich von aussen.</summary>
    public void MaskChanged()
    {
        _washStale = true;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ Zeichnen

    private static readonly Brush Ring = Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));

    private void RenderPaint(DrawingContext context)
    {
        if (_canvasWidth <= 0) return;

        Wash();

        if (_mask is not null && _wash is not null)
        {
            // Ueber die ganze Bildflaeche gezogen: Die Maske ist groeber als das Bild,
            // und das Strecken uebernimmt dieselbe Umrechnung wie die Anzeige.
            var a = Screen(0, 0);
            var b = Screen(_canvasWidth, _canvasHeight);

            context.DrawImage(_wash, new Rect(a, b));
        }

        // Der Pinselkreis am Zeiger - sonst weiss niemand, wie gross er ist, und der
        // Zeiger selbst ist ausgeblendet. Er haengt NICHT an der Maske: Vor dem
        // ersten Strich gibt es noch keine, und genau dann braucht man ihn am meisten.
        if (!IsMouseOver) return;

        var at = Mouse.GetPosition(this);
        double radius = BrushRadius * ReachOnScreen;

        context.DrawEllipse(null, Shadow, at, radius + 1, radius + 1);
        context.DrawEllipse(null, new Pen(Ring, 1), at, radius, radius);
    }

    /// <summary>
    /// Baut den Schleier - einmal je Aenderung und nicht je Bildwiederholung.
    ///
    /// Als Bitmap in der Aufloesung der MASKE und nicht des Bildes: Das sind bei 4K
    /// ein Sechzehntel der Punkte, und gestreckt sieht man den Unterschied nicht, weil
    /// eine gemalte Kante ohnehin weich ist.
    /// </summary>
    private void Wash()
    {
        if (!_washStale || _mask is null) return;


        _washStale = false;

        int w = Math.Max(1, _mask.Width);
        int h = Math.Max(1, _mask.Height);

        if (_wash is null || _wash.PixelWidth != w || _wash.PixelHeight != h)
            _wash = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

        var cover = _mask.Cover();
        var pixels = new byte[w * h * 4];

        for (int i = 0; i < cover.Length && i < w * h; i++)
        {
            // Ein warmer Ton, halb durchsichtig: Er muss auf hellem wie auf dunklem
            // Bild zu sehen sein und darf nicht fuer Bildinhalt gehalten werden.
            byte strength = (byte)(cover[i] * 0.55f);

            pixels[i * 4] = 0x40;
            pixels[i * 4 + 1] = 0x30;
            pixels[i * 4 + 2] = 0xFF;
            pixels[i * 4 + 3] = strength;
        }

        _wash.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
    }

    // ---------------------------------------------------------------- Bedienung

    private void PaintDown(MouseButtonEventArgs e, float x, float y)
    {
        // Jetzt erst wird eine Maske gebraucht - und, wenn noetig, angelegt.
        _mask ??= MaskWanted?.Invoke();

        if (_mask is null) return;

        _washStale = true;

        _painting = true;

        // Alt nimmt weg statt aufzutragen - derselbe Griff wie in jedem Programm mit
        // Pinsel, und einer, den man nicht lernen muss.
        _erasing = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        _lastStroke = new Point(x, y);

        _mask.Stroke(x, y, BrushRadius, _erasing ? 0f : 1f, BrushFlow,
                     BrushHardness, BrushOpacity);

        MaskChanged();
        Painted?.Invoke(true);

        e.Handled = true;
        CaptureMouse();
    }

    /// <summary>Der Ring muss dem Zeiger folgen, auch wenn nicht gemalt wird.</summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        if (_mode == AdornerMode.Paint) InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_mode == AdornerMode.Paint) InvalidateVisual();
    }

    private void PaintMove(float x, float y)
    {
        if (!_painting || _mask is null)
        {
            // Der Kreis folgt dem Zeiger, auch wenn nicht gemalt wird.
            InvalidateVisual();
            return;
        }

        // Zwischen zwei Mausmeldungen liegen bei schneller Bewegung viele Bildpunkte.
        // Ohne Zwischenschritte ergaebe ein Strich eine Perlenkette statt einer Linie.
        double dx = x - _lastStroke.X;
        double dy = y - _lastStroke.Y;
        double away = Math.Sqrt(dx * dx + dy * dy);

        int steps = Math.Max(1, (int)(away / Math.Max(1f, BrushRadius * 0.25f)));

        for (int s = 1; s <= steps; s++)
        {
            float t = (float)s / steps;

            _mask.Stroke((float)(_lastStroke.X + dx * t), (float)(_lastStroke.Y + dy * t),
                         BrushRadius, _erasing ? 0f : 1f, BrushFlow,
                         BrushHardness, BrushOpacity);
        }

        _lastStroke = new Point(x, y);

        MaskChanged();
        Painted?.Invoke(true);
    }

    private void PaintUp(MouseButtonEventArgs e)
    {
        if (!_painting) return;

        _painting = false;
        ReleaseMouseCapture();

        // Beim Loslassen einmal endgueltig: Das ist das Zeichen, voll zu rechnen und
        // den Anstrich festzuhalten.
        _mask?.Keep();

        Painted?.Invoke(false);

        e.Handled = true;
        InvalidateVisual();
    }
}
