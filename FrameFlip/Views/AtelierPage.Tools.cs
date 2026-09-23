using System.Windows;
using System.Windows.Input;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Das gewaehlte Werkzeug und was es aus der Maus macht.
///
/// Der ganze Sinn dieser Datei ist eine einzige Regel: Es gibt zu jedem Zeitpunkt
/// GENAU EIN Werkzeug, und das Bild tut, was es sagt. Vorher entschied das eine
/// Mischung aus der Auswahl im Ebenenstreifen, einem Schalter im Maskenbereich und
/// der Frage, ob gerade das Original gezeigt wird - drei Zustaende, die sich
/// gegenseitig ueberlagern konnten und von denen keiner zu sehen war.
/// </summary>
public partial class AtelierPage
{
    private AtelierTool _tool = AtelierTool.Move;

    /// <summary>
    /// Sperre gegen den Ringschluss.
    ///
    /// Es gibt zwei Wege in die Auswahl: den Knopf in der Spalte und den
    /// Maskenbereich, der um eine Kryptomatte bittet. Jeder meldet dem anderen, was
    /// er getan hat, und ohne diese Sperre meldeten sie es einander bis zum
    /// Stapelueberlauf.
    /// </summary>
    private bool _switchingTool;

    /// <summary>Das Werkzeug hat gewechselt - hier haengt alles daran.</summary>
    private void OnToolChanged(AtelierTool tool)
    {
        if (_switchingTool) return;

        _switchingTool = true;

        try
        {
            _tool = tool;

            // "Auswaehlen" IST der Auswahlmodus des Maskenbereichs. Wer das
            // Werkzeug wechselt, verlaesst ihn damit auch dort - sonst bliebe im
            // Streifen ein Haken stehen fuer eine Betriebsart, die nicht mehr gilt.
            // Im Knotenmodus gibt es keine Ebene, an der eine Kryptomatte entstehen
            // koennte - dort waehlt der Klick fuer den gewaehlten Maskenknoten.
            _picking = tool == AtelierTool.Select && !InNodes;

            if (!_picking) Layers.StopPicking();

            Display.Cursor = tool switch
            {
                AtelierTool.Select or AtelierTool.Pick or AtelierTool.Crop => Cursors.Cross,
                AtelierTool.Hand => Cursors.Hand,
                // Der Pinsel blendet den Zeiger aus, weil der Ring seine Stelle
                // einnimmt. Das tut aber ShowBrush - erst dort steht fest, ob
                // ueberhaupt gemalt werden kann.
                AtelierTool.Brush => Display.Cursor,
                _ => null,
            };

            if (tool != AtelierTool.Pick) PickText.Visibility = Visibility.Collapsed;

            Properties.Show(tool, InNodes);
        }
        finally
        {
            _switchingTool = false;
        }

        // Ausserhalb der Sperre: Der Rahmen gehoert dem Verschieben-Werkzeug, und
        // ihn zu zeigen oder zu verstecken ist die sichtbarste Folge des Wechsels.
        ShowPlacement();

        // Und die Knoten: Sie liegen nur ueber dem Bild, solange ihr Werkzeug gilt.
        ShowNodeMode();

        if (tool == AtelierTool.Nodes && InNodes) NodeView.Focus();
    }

    /// <summary>
    /// Die Tastenkuerzel der Spalte - V, W, C, H, I.
    ///
    /// Dieselben Buchstaben wie in Photoshop, und das ist kein Zitat, sondern der
    /// Grund: Wer sie in den Fingern hat, soll sie nicht neu lernen muessen. Das
    /// Fenster ruft hier herein, weil Tastendruecke dort ankommen und nicht hier.
    /// </summary>
    public bool HandleToolKey(Key key)
    {
        var wanted = key switch
        {
            Key.V => AtelierTool.Move,
            Key.W => AtelierTool.Select,
            Key.C => AtelierTool.Crop,
            Key.H => AtelierTool.Hand,
            Key.B => AtelierTool.Brush,
            Key.I => AtelierTool.Pick,
            Key.N => AtelierTool.Nodes,
            _ => (AtelierTool?)null,
        };

        if (wanted is not { } tool) return false;

        MouseTools.Select(tool);
        OnToolChanged(tool);

        return true;
    }

    // ------------------------------------------------------------------ die Hand

    private Point _panFrom;
    private double _panX, _panY;
    private bool _panning;

    /// <summary>
    /// Ein Zug am Ausschnitt - mit der Hand, oder mit der mittleren Taste.
    ///
    /// Die mittlere Taste gilt IMMER und nicht nur beim Handwerkzeug. Das ist keine
    /// Ausnahme von der Regel "ein Werkzeug", sondern ihre Ergaenzung: Schieben ist
    /// keine Aenderung am Bild, sondern am Blick darauf, und einen Blick zu
    /// verschieben sollte nie kosten, das Werkzeug zu wechseln und zurueck.
    /// </summary>
    private void OnViewportDown(object sender, MouseButtonEventArgs e)
    {
        bool hand = e.ChangedButton == MouseButton.Left && _tool == AtelierTool.Hand;
        bool middle = e.ChangedButton == MouseButton.Middle;

        if (!hand && !middle) return;

        _panning = true;
        _panFrom = e.GetPosition(ImageScroll);
        _panX = ImageScroll.HorizontalOffset;
        _panY = ImageScroll.VerticalOffset;

        ImageScroll.CaptureMouse();
        Display.Cursor = Cursors.ScrollAll;

        e.Handled = true;
    }

    private void OnViewportMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;

        var now = e.GetPosition(ImageScroll);

        // Umgekehrtes Vorzeichen: Das Bild folgt der Hand, also laeuft der
        // Ausschnitt ihr entgegen.
        ImageScroll.ScrollToHorizontalOffset(_panX - (now.X - _panFrom.X));
        ImageScroll.ScrollToVerticalOffset(_panY - (now.Y - _panFrom.Y));

        e.Handled = true;
    }

    private void OnViewportUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;

        _panning = false;
        ImageScroll.ReleaseMouseCapture();

        Display.Cursor = _tool == AtelierTool.Hand ? Cursors.Hand : Display.Cursor;

        e.Handled = true;
    }

    // --------------------------------------------------------------- die Pipette

    /// <summary>
    /// Liest den Wert an einer Stelle ab.
    ///
    /// Gezeigt werden beide Zahlen: der Wert, wie er gerechnet wird, und der Wert,
    /// wie er auf dem Schirm steht. Nur der erste ist die Antwort auf "wie hell ist
    /// das", und nur der zweite laesst sich mit einer Pipette in einem anderen
    /// Programm vergleichen. Eine davon wegzulassen hiesse, sich auf eine der beiden
    /// Fragen festzulegen, die jemand haben koennte.
    /// </summary>
    private void ReadAt(int x, int y)
    {
        var frame = _frame;
        if (frame is null) return;

        int i = y * frame.Width + x;

        if (i < 0 || i >= frame.R.Length) return;

        float r = frame.R[i], g = frame.G[i], b = frame.B[i];

        int br = (int)MathF.Round(Math.Clamp(Srgb.Encode(r), 0f, 1f) * 255f);
        int bg = (int)MathF.Round(Math.Clamp(Srgb.Encode(g), 0f, 1f) * 255f);
        int bb = (int)MathF.Round(Math.Clamp(Srgb.Encode(b), 0f, 1f) * 255f);

        string label = (string)(TryFindResource("S_PickReadout") ?? "read");

        PickText.Text = $"{label}  {x},{y}   {br}/{bg}/{bb}   {r:0.###} {g:0.###} {b:0.###}";
        PickText.Visibility = Visibility.Visible;

        Properties.Read(x, y, br, bg, bb, r, g, b, DepthAt(i));
    }

    /// <summary>
    /// Die Entfernung an einer Stelle - null, wenn die Datei keine fuehrt.
    ///
    /// Null und nicht 0: "keine Entfernung bekannt" ist etwas anderes als "null
    /// Meter", und der Unterschied entscheidet darueber, ob der Knopf zum
    /// Scharfstellen ueberhaupt etwas anzubieten hat.
    ///
    /// Werte ab <see cref="FloatFrame.NotHit"/> zaehlen nicht mit: Blender schreibt
    /// in den Hintergrund des Tiefenpasses eine sehr grosse Zahl, und die ist keine
    /// Entfernung, sondern "hier steht nichts".
    /// </summary>
    private float? DepthAt(int i)
    {
        if (FramePasses.NameFor(PassNeed.Depth, _passes) is not { } name) return null;
        if (!_sources.TryGetValue(name, out var depth)) return null;
        if (i < 0 || i >= depth.R.Length) return null;

        float value = depth.R[i];

        return float.IsFinite(value) && value > 0f && value < FloatFrame.NotHit ? value : null;
    }

    /// <summary>Die abgelesene Entfernung soll die Scharfstellung werden.</summary>
    private void OnFocusWanted(float distance)
    {
        if (!Tools.SetFocus(distance))
        {
            Properties.Told(Strings.T("S_NoDepthTool"));
            return;
        }

        Properties.Told($"{Strings.T("S_FocusSet")} {distance:0.###}");
    }
}
