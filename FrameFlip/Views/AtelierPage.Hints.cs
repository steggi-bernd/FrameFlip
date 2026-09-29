using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FrameFlip.Views;

/// <summary>
/// Die Tastenhilfe (docs/Atelier-Tastenhilfe.md): Die Seite sagt, was Maus und Tasten gerade tun,
/// das Hauptfenster zeigt es in seiner Statuszeile.
///
/// Abgelesen alle 200 ms statt an einem Dutzend Ereignissen: Die Hilfe haengt am Werkzeug, an der
/// Flaeche des Pinsels, an gehaltenen Zusatztasten, daran, wo die Maus steht, und an Pipetten, die
/// auf einen Klick warten. Jedes davon hat seinen eigenen Weg, sich zu aendern; ein Blick in
/// regelmaessigem Abstand ist billiger und verpasst keinen. Gemeldet wird nur, wenn sich etwas
/// aendert, und nur, solange die Seite zu sehen ist.
/// </summary>
public partial class AtelierPage
{
    private DispatcherTimer? _hintTimer;
    private HintContext? _hintContext;

    /// <summary>Die Hinweise haben sich geaendert.</summary>
    public event Action<IReadOnlyList<KeyHint>>? HintsChanged;

    /// <summary>Was die Hilfe gerade zeigt - fuer die Probe und fuer ein Fenster, das spaet dazukommt.</summary>
    public IReadOnlyList<KeyHint> CurrentHints { get; private set; } = Array.Empty<KeyHint>();

    private void SetUpHints()
    {
        _hintTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _hintTimer.Tick += (_, _) => RefreshHints();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _hintTimer.Start();
            else _hintTimer.Stop();

            RefreshHints();
        };
    }

    /// <summary>Die Lage, wie die Hilfe sie sieht.</summary>
    internal HintContext HintContextNow() => new()
    {
        Tool = _tool,
        Area = Placement.BrushArea,
        HasImage = _frame is not null,
        InNodes = InNodes,
        OverLayers = Layers.IsVisible && Layers.IsMouseOver,
        OverGraph = NodeView.IsVisible && NodeView.IsMouseOver,
        Held = Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift),
        ColourWaiting = LevelsPicking || ColourPicking,
        ObjectWaiting = ObjectPicking,
        Escapable = _viewer is not null || _stackSolo is not null,
        CurvesShown = Tools.CurvesShown,
        ColourMask = !InNodes && Layers.Selection?.Mask.Kind == Imaging.Grading.MaskKind.Colour,
    };

    /// <summary>Liest die Lage neu und meldet, wenn sich die Hinweise geaendert haben.</summary>
    internal void RefreshHints()
    {
        var context = HintContextNow();
        if (context == _hintContext) return;

        _hintContext = context;
        CurrentHints = AtelierHints.For(context);
        HintsChanged?.Invoke(CurrentHints);
    }
}
