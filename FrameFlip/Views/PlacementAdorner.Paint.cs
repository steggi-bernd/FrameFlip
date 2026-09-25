using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

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

    /// <summary>Der laufende Zug - er setzt die Tupfer, der Adorner liefert nur den Weg.</summary>
    private PaintStroke? _stroke;

    private WriteableBitmap? _wash;
    private bool _washStale = true;

    /// <summary>
    /// Welcher Bereich der Maske seit der letzten Darstellung beruehrt wurde.
    ///
    /// Ein Pinselstrich aendert ein paar hundert Maskenpunkte; den Schleier
    /// vollstaendig neu zu bauen hiesse, fuer jeden Strich eine halbe Million zu
    /// schreiben. Bei dreissig Strichen je Sekunde ist das der Unterschied zwischen
    /// einem Pinsel, der an der Maus klebt, und einem, der hinterherzieht.
    /// </summary>
    private int _dirtyX0, _dirtyY0, _dirtyX1 = -1, _dirtyY1 = -1;

    /// <summary>Der Pinselradius in Bildpunkten der Leinwand.</summary>
    public float BrushRadius { get; set; } = 40f;

    /// <summary>Wie schnell ein Strich auftraegt. 0 bis 1.</summary>
    public float BrushFlow { get; set; } = 0.7f;

    /// <summary>Wie hart die Kante ist. 0 ist ein Verlauf, 1 eine Scheibe.</summary>
    public float BrushHardness { get; set; } = 0.5f;

    /// <summary>Bis wohin ein Strich ueberhaupt auftraegt. 0 bis 1.</summary>
    public float BrushOpacity { get; set; } = 1f;

    /// <summary>Der Abstand zweier Tupfer als Anteil des Radius - siehe <see cref="PaintStroke.Spacing"/>.</summary>
    public float BrushSpacing { get; set; } = PaintStroke.DefaultSpacing;

    /// <summary>Der zuletzt beendete Zug - fuer die Probe.</summary>
    internal PaintStroke? LastStroke { get; private set; }

    /// <summary>Es wurde gemalt. <c>interim</c> heisst: der Strich laeuft noch.</summary>
    public event Action<bool>? Painted;

    /// <summary>Groesse oder Haerte wurden am Bild gezogen - die Regler sollen nachziehen.</summary>
    public event Action? BrushAdjusted;

    /// <summary>Was ein Zug mit gedrueckter Strg-Taste einstellt - oder Strg und das Rad.</summary>
    internal enum BrushKnob { None, Size, Hardness, Spacing }

    private BrushKnob _knob;

    /// <summary>Wo der Zug begann - dort bleibt der Ring stehen, waehrend er sich aendert.</summary>
    private Point _knobAnchor;

    private float _knobStart;

    /// <summary>Wie viel Haerte ein Schirmpunkt nach rechts bringt: 200 Punkte von weich bis hart.</summary>
    private const double HardnessPerPoint = 1.0 / 200;

    /// <summary>Der groesste Radius, den die Regler zulassen - 400 Punkte Durchmesser.</summary>
    private const float MaxRadius = 200f;

    /// <summary>Ob gerade Groesse oder Haerte gezogen wird - fuer die Probe.</summary>
    internal BrushKnob Knob => _knob;

    /// <summary>
    /// Ob gerade Groesse oder Haerte gezogen werden - dann gehoert die Maus dem Ring.
    /// Die Anzeige des Abstands gehoert ihr nicht; sie kommt vom Rad und steht nur da.
    /// </summary>
    internal bool KnobDragged => _knob is BrushKnob.Size or BrushKnob.Hardness;

    /// <summary>
    /// Beginnt das Einstellen am Bild: Strg und linke Taste die Groesse, Strg und rechte
    /// die Haerte. Waagrecht gezogen - nach rechts groesser und haerter. Gemalt wird
    /// dabei nicht, und eine Maske entsteht auch keine.
    /// </summary>
    internal void BeginKnob(Point at, BrushKnob knob)
    {
        _knob = knob;
        _knobAnchor = at;
        _knobStart = knob == BrushKnob.Size ? BrushRadius : BrushHardness;

        CaptureMouse();
        InvalidateVisual();
    }

    /// <summary>
    /// Der Zug geht weiter. Bei der Groesse folgt die Kante des Rings der Maus: Ein
    /// Schirmpunkt nach rechts ist ein Schirmpunkt mehr Radius, gleich wie weit das
    /// Bild gerade vergroessert ist.
    /// </summary>
    internal void MoveKnob(Point at)
    {
        double dx = at.X - _knobAnchor.X;

        if (_knob == BrushKnob.Size)
            BrushRadius = (float)Math.Clamp(_knobStart + dx / Math.Max(ReachOnScreen, 1e-6), 1, MaxRadius);
        else if (_knob == BrushKnob.Hardness)
            BrushHardness = (float)Math.Clamp(_knobStart + dx * HardnessPerPoint, 0, 1);
        else
            return;

        BrushAdjusted?.Invoke();
        InvalidateVisual();
    }

    internal void EndKnob()
    {
        if (_knob == BrushKnob.None) return;

        // Nur Groesse und Haerte fangen die Maus. Der Abstand kommt vom Rad und darf
        // einen laufenden Strich nicht loslassen.
        bool captured = _knob is BrushKnob.Size or BrushKnob.Hardness;

        _knob = BrushKnob.None;
        _knobFade?.Stop();

        if (captured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>Laesst die Anzeige des Abstands nach dem letzten Dreh am Rad wieder verschwinden.</summary>
    private System.Windows.Threading.DispatcherTimer? _knobFade;

    /// <summary>
    /// Strg und das Rad: der Abstand der Tupfer. Bis zur Haelfte des Radius in Schritten
    /// von 5 %, bis zum Radius in 10 %, darueber in 25 % - unten entscheidet ein Schritt
    /// zwischen glatt und perlig, oben nur noch, wie weit die Perlen auseinander liegen.
    /// Der Ring zeigt waehrenddessen, wo die Tupfer eines Striches saessen.
    /// </summary>
    internal void StepSpacing(double notches, Point at)
    {
        int steps = (int)Math.Round(notches);
        if (steps == 0) steps = Math.Sign(notches);

        float spacing = BrushSpacing;

        for (int i = 0; i < Math.Abs(steps); i++)
        {
            bool up = steps > 0;

            // Die Stufe richtet sich nach der Seite, auf die es geht: 50 % hinauf ist ein
            // Schritt von 10, hinunter einer von 5.
            float from = up ? spacing + 1e-4f : spacing - 1e-4f;
            float step = from < 0.5f ? 0.05f : from < 1f ? 0.1f : 0.25f;

            spacing = MathF.Round((spacing + (up ? step : -step)) * 100f) / 100f;
        }

        BrushSpacing = Math.Clamp(spacing, PaintStroke.MinSpacing, PaintStroke.MaxSpacing);

        // Ein laufender Zug an Groesse oder Haerte behaelt seine Anzeige.
        if (_knob is BrushKnob.None or BrushKnob.Spacing)
        {
            _knob = BrushKnob.Spacing;
            _knobAnchor = at;

            if (_knobFade is null)
            {
                _knobFade = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _knobFade.Tick += (_, _) =>
                {
                    _knobFade.Stop();
                    if (_knob == BrushKnob.Spacing) EndKnob();
                };
            }

            _knobFade.Stop();
            _knobFade.Start();
        }

        BrushAdjusted?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Faengt Strg plus Maustaste ab - dann wird eingestellt statt gemalt.</summary>
    private bool KnobPressed(MouseButtonEventArgs e, BrushKnob knob)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return false;

        BeginKnob(e.GetPosition(this), knob);
        e.Handled = true;
        return true;
    }

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
        // Wenn sich nichts geaendert hat, darf sich auch nichts neu aufbauen.
        //
        // Diese Stelle wird bei JEDEM gezeichneten Bild gerufen - die Anfasser
        // ziehen nach dem Zusammensetzen nach, und das geschieht waehrend eines
        // Striches sechzig Mal in der Sekunde. Blind _washStale zu setzen hiess, den
        // ganzen Schleier jedes zweite Bild vollstaendig neu zu schreiben und damit
        // genau die Teilflaeche wieder wegzuwerfen, die den Pinsel schnell macht.
        bool same = ReferenceEquals(_mask, mask) &&
                    _canvasWidth == canvasWidth && _canvasHeight == canvasHeight &&
                    _uniform == uniform;

        _mask = mask;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _uniform = uniform;

        IsHitTestVisible = mask is not null || MaskWanted is not null;

        if (same) return;

        _washStale = true;
        _dirtyX1 = -1;

        InvalidateVisual();
    }

    /// <summary>Die Darstellung muss neu gebaut werden - nach einem Strich von aussen.</summary>
    public void MaskChanged()
    {
        _washStale = true;
        _dirtyX1 = -1;

        InvalidateVisual();
    }

    /// <summary>Merkt sich, welcher Bereich der Maske einen Strich abbekommen hat.</summary>
    private void Touched(PaintBounds bounds)
    {
        if (_mask is null || bounds.IsEmpty) return;

        // Ein Maskenpunkt Rand auf jeder Seite: Die weiche Kante des Tupfers faellt bis
        // auf null, und der Punkt, auf den sie fast null legt, gehoert noch dazu.
        int x0 = (int)MathF.Floor(bounds.X0 / PaintedMask.Coarse) - 1;
        int y0 = (int)MathF.Floor(bounds.Y0 / PaintedMask.Coarse) - 1;
        int x1 = (int)MathF.Ceiling(bounds.X1 / PaintedMask.Coarse) + 1;
        int y1 = (int)MathF.Ceiling(bounds.Y1 / PaintedMask.Coarse) + 1;

        if (_dirtyX1 < 0)
        {
            _dirtyX0 = x0;
            _dirtyY0 = y0;
            _dirtyX1 = x1;
            _dirtyY1 = y1;
        }
        else
        {
            _dirtyX0 = Math.Min(_dirtyX0, x0);
            _dirtyY0 = Math.Min(_dirtyY0, y0);
            _dirtyX1 = Math.Max(_dirtyX1, x1);
            _dirtyY1 = Math.Max(_dirtyY1, y1);
        }

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
        if (_knob != BrushKnob.None)
        {
            RenderKnob(context);
            return;
        }

        if (!IsMouseOver) return;

        var at = Mouse.GetPosition(this);
        double radius = BrushRadius * ReachOnScreen;

        context.DrawEllipse(null, Shadow, at, radius + 1, radius + 1);
        context.DrawEllipse(null, new Pen(Ring, 1), at, radius, radius);
    }

    /// <summary>
    /// Waehrend Groesse oder Haerte gezogen werden: der Ring an der Stelle, an der der
    /// Zug begann, gefuellt mit dem Abfall des Pinsels - innen deckend bis zur harten
    /// Kante, dann auslaufend. Daneben die beiden Zahlen, die gezogene hervorgehoben.
    /// </summary>
    private void RenderKnob(DrawingContext context)
    {
        var at = _knobAnchor;
        double radius = Math.Max(1, BrushRadius * ReachOnScreen);
        double hard = Math.Clamp(BrushHardness, 0f, 0.999f);

        var fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), hard),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        fill.Freeze();

        context.DrawEllipse(fill, null, at, radius, radius);
        context.DrawEllipse(null, Shadow, at, radius + 1, radius + 1);
        context.DrawEllipse(null, new Pen(Ring, 1.5), at, radius, radius);

        // Die harte Kante als eigener, gestrichelter Ring.
        var dashed = new Pen(Ring, 1) { DashStyle = DashStyles.Dash };
        dashed.Freeze();
        context.DrawEllipse(null, dashed, at, radius * hard, radius * hard);

        // Beim Abstand: die Tupfer eines waagrechten Strichs durch den Ring, so dicht,
        // wie er sie setzen wird. Unter drei Schirmpunkten Abstand verschwimmen sie
        // ohnehin zu einem Band - dann reicht der Ring.
        if (_knob == BrushKnob.Spacing)
        {
            double step = BrushRadius * BrushSpacing * ReachOnScreen;

            if (step >= 3)
            {
                int reach = Math.Min(14, (int)(radius * 3 / step));

                for (int k = -reach; k <= reach; k++)
                {
                    if (k == 0) continue;
                    context.DrawEllipse(null, Ghost, new Point(at.X + k * step, at.Y), radius, radius);
                }
            }
        }

        var lines = new[]
        {
            KnobLabel(Strings.T("S_BrushSize") + " " + (BrushRadius * 2).ToString("0", CultureInfo.CurrentCulture),
                      _knob == BrushKnob.Size),
            KnobLabel(Strings.T("S_BrushHardness") + " " + BrushHardness.ToString("0.00", CultureInfo.CurrentCulture),
                      _knob == BrushKnob.Hardness),
            KnobLabel(Strings.T("S_BrushSpacing") + " " + (BrushSpacing * 100).ToString("0", CultureInfo.CurrentCulture) + " %",
                      _knob == BrushKnob.Spacing),
        };

        double wide = lines.Max(l => l.Width) + 16;
        double high = lines.Sum(l => l.Height) + 4 * (lines.Length - 1) + 8;

        // Rechts vom Ring, ausser er ragt dort aus dem Bild - dann links. Beim Abstand
        // stehen rechts und links die Tupfer, dann steht die Anzeige darueber.
        double x = at.X + radius + 12;
        double y = at.Y - high / 2 + 4;

        if (_knob == BrushKnob.Spacing)
        {
            x = at.X - wide / 2;
            y = at.Y - radius - high - 8 + 4;
        }
        else if (x + wide > ActualWidth)
        {
            x = at.X - radius - 12 - wide;
        }

        context.DrawRoundedRectangle(KnobBack, null, new Rect(x, y - 4, wide, high), 4, 4);

        foreach (var line in lines)
        {
            context.DrawText(line, new Point(x + 8, y));
            y += line.Height + 4;
        }
    }

    private static readonly Pen Ghost = FrozenPen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 1);

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static readonly Brush KnobBack = Frozen(Color.FromArgb(0xD8, 0x18, 0x18, 0x1E));
    private static readonly Brush KnobActive = Frozen(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly Brush KnobQuiet = Frozen(Color.FromArgb(0xFF, 0xA8, 0xA8, 0xB4));

    private FormattedText KnobLabel(string text, bool active) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, active ? FontWeights.SemiBold : FontWeights.Normal,
                     FontStretches.Normal),
        12, active ? KnobActive : KnobQuiet, VisualTreeHelper.GetDpi(this).PixelsPerDip);

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

        bool fresh = _wash is null || _wash.PixelWidth != w || _wash.PixelHeight != h;

        if (fresh) _wash = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

        // Nach einem Strich reicht der beruehrte Ausschnitt. Nur wenn die Maske ganz
        // neu ist - oder von aussen gewechselt hat - muss alles geschrieben werden.
        int x0 = fresh || _dirtyX1 < 0 ? 0 : Math.Max(0, _dirtyX0);
        int y0 = fresh || _dirtyX1 < 0 ? 0 : Math.Max(0, _dirtyY0);
        int x1 = fresh || _dirtyX1 < 0 ? w - 1 : Math.Min(w - 1, _dirtyX1);
        int y1 = fresh || _dirtyX1 < 0 ? h - 1 : Math.Min(h - 1, _dirtyY1);

        _dirtyX1 = -1;

        if (x1 < x0 || y1 < y0) return;

        int across = x1 - x0 + 1;
        int down = y1 - y0 + 1;

        var cover = _mask.Cover();
        var pixels = new byte[across * down * 4];

        for (int y = 0; y < down; y++)
        {
            int from = (y0 + y) * w + x0;
            int to = y * across * 4;

            for (int x = 0; x < across; x++)
            {
                // Ein warmer Ton, halb durchsichtig: Er muss auf hellem wie auf
                // dunklem Bild zu sehen sein und darf nicht fuer Bildinhalt gehalten
                // werden.
                pixels[to + x * 4] = 0x40;
                pixels[to + x * 4 + 1] = 0x30;
                pixels[to + x * 4 + 2] = 0xFF;
                pixels[to + x * 4 + 3] = (byte)(cover[from + x] * 0.55f);
            }
        }

        _wash!.WritePixels(new Int32Rect(x0, y0, across, down), pixels, across * 4, 0);
    }

    // ---------------------------------------------------------------- Bedienung

    private void PaintDown(MouseButtonEventArgs e, float x, float y, bool erase = false)
    {
        // Jetzt erst wird eine Maske gebraucht - und, wenn noetig, angelegt.
        _mask ??= MaskWanted?.Invoke();

        if (_mask is null) return;

        _washStale = true;

        _painting = true;

        // Die rechte Taste nimmt weg, Alt ebenso - siehe OnMouseRightButtonDown.
        _erasing = erase || (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        _stroke = new PaintStroke
        {
            Radius = BrushRadius,
            Hardness = BrushHardness,
            Flow = BrushFlow,
            Opacity = BrushOpacity,
            Spacing = BrushSpacing,
            Erase = _erasing,
        };

        Touched(_stroke.Begin(_mask, x, y));
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
        // Groesse und Haerte werden gerade gezogen - dann wird nicht gemalt. Die Anzeige
        // des Abstands haelt einen Strich dagegen nicht auf.
        if (_knob is BrushKnob.Size or BrushKnob.Hardness) return;

        if (!_painting || _mask is null || _stroke is null)
        {
            // Der Kreis folgt dem Zeiger, auch wenn nicht gemalt wird.
            InvalidateVisual();
            return;
        }

        // Die Tupfer zwischen zwei Mausmeldungen setzt der Zug, im Abstand des Pinsels.
        // Liegt die Meldung naeher als ein Abstand, entsteht kein Tupfer - dann gibt
        // es auch nichts neu zu rechnen, nur der Ring folgt.
        var touched = _stroke.To(_mask, x, y);

        if (touched.IsEmpty)
        {
            InvalidateVisual();
            return;
        }

        Touched(touched);
        Painted?.Invoke(true);
    }

    private void PaintUp(MouseButtonEventArgs e)
    {
        if (!_painting) return;

        _painting = false;
        ReleaseMouseCapture();

        LastStroke = _stroke;
        _stroke = null;

        // Beim Loslassen einmal endgueltig: Das ist das Zeichen, voll zu rechnen und
        // den Anstrich festzuhalten.
        _mask?.Keep();

        Painted?.Invoke(false);

        e.Handled = true;
        InvalidateVisual();
    }
}
