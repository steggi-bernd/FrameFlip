using System.Windows.Input;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Ein Hinweis der Tastenhilfe: was man drueckt (<see cref="Keys"/>, Bausteine wie "Alt", "Klick",
/// oder ein Buchstabe) und was dann geschieht. <see cref="Needs"/> ist die Zusatztaste, die er
/// braucht - haelt man sie, bleiben nur die Hinweise, die mit ihr gehen.
/// </summary>
public sealed record KeyHint(string[] Keys, string ActionKey, ModifierKeys Needs = ModifierKeys.None)
{
    /// <summary>Die Tasten, wie sie in der Statuszeile stehen - "Alt+Ziehen".</summary>
    public string KeysText => string.Join("+", Keys.Select(AtelierHints.KeyName));

    public string Action => Strings.T(ActionKey);
}

/// <summary>Worauf die Tastenhilfe schaut.</summary>
public sealed record HintContext
{
    public AtelierTool Tool { get; init; } = AtelierTool.Move;

    public PaintArea Area { get; init; }

    public bool HasImage { get; init; }

    public bool InNodes { get; init; }

    /// <summary>Die Maus steht ueber der Ebenenliste des Stapels.</summary>
    public bool OverLayers { get; init; }

    /// <summary>Die Maus steht ueber dem Graphen.</summary>
    public bool OverGraph { get; init; }

    /// <summary>Welche Zusatztasten gerade gehalten werden.</summary>
    public ModifierKeys Held { get; init; }

    /// <summary>Eine Pipette wartet auf einen Klick ins Bild (Tonwert, Farbrad, Toenung, Weissabgleich).</summary>
    public bool ColourWaiting { get; init; }

    /// <summary>Die Objektmaske wartet auf einen Klick ins Bild.</summary>
    public bool ObjectWaiting { get; init; }

    /// <summary>Etwas laesst sich mit Esc beenden - Isolieren, Betrachter.</summary>
    public bool Escapable { get; init; }

    /// <summary>Die Karte der Kurven ist zu sehen - dann setzt Strg+Klick einen Punkt.</summary>
    public bool CurvesShown { get; init; }

    /// <summary>Die gewaehlte Ebene hat eine Farbbereichsmaske - dann erweitert Umschalt+Klick der Pipette sie.</summary>
    public bool ColourMask { get; init; }
}

/// <summary>
/// Die Tastenhilfe in der Statuszeile: je nach Werkzeug und Lage, was Maus und Tasten gerade tun.
///
/// Eine Tabelle und nicht verstreute Texte, damit sie mit dem Code zusammenbleibt - die Probe
/// prueft jede Taste, die hier steht, gegen die Seite. Was hier steht, ist aus dem Code
/// abgelesen (docs/Atelier-Tastenhilfe.md), nicht ausgedacht: Ein Hinweis, der luegt, ist
/// schlimmer als keiner.
/// </summary>
public static class AtelierHints
{
    /// <summary>Die Werkzeugtasten, wie HandleToolKey sie kennt.</summary>
    public static readonly (Key Key, AtelierTool Tool)[] ToolKeys =
    {
        (Key.V, AtelierTool.Move), (Key.W, AtelierTool.Select), (Key.C, AtelierTool.Crop), (Key.H, AtelierTool.Hand),
        (Key.B, AtelierTool.Brush), (Key.I, AtelierTool.Pick), (Key.N, AtelierTool.Nodes),
    };

    /// <summary>Ein Baustein, uebersetzt - "Ctrl" wird "Strg", ein Buchstabe bleibt.</summary>
    public static string KeyName(string token) => token switch
    {
        "Ctrl" => Strings.T("S_KeyCtrl"),
        "Shift" => Strings.T("S_KeyShift"),
        "Alt" => Strings.T("S_KeyAlt"),
        "Click" => Strings.T("S_KeyClick"),
        "DoubleClick" => Strings.T("S_KeyDoubleClick"),
        "Drag" => Strings.T("S_KeyDrag"),
        "RightDrag" => Strings.T("S_KeyRightDrag"),
        "Wheel" => Strings.T("S_KeyWheel"),
        "Middle" => Strings.T("S_KeyMiddle"),
        "Grip" => Strings.T("S_KeyGrip"),
        "TurnGrip" => Strings.T("S_KeyTurnGrip"),
        "Esc" => "Esc",
        "Del" => Strings.T("S_KeyDelete"),
        _ => token,
    };

    /// <summary>Die Hinweise fuer eine Lage - hoechstens so viele, wie in eine Zeile passen.</summary>
    public static IReadOnlyList<KeyHint> For(HintContext c)
    {
        var all = Collect(c).ToList();

        // Wer eine Zusatztaste haelt, sucht, was mit ihr geht - wie in Blender.
        if (c.Held != ModifierKeys.None)
        {
            var with = all.Where(h => h.Needs != ModifierKeys.None && (h.Needs & c.Held) == c.Held).ToList();
            if (with.Count > 0) return with.Take(8).ToList();
        }

        return all.Where(h => h.Needs == ModifierKeys.None || h.Keys.Length <= 2).Take(9).ToList();
    }

    private static IEnumerable<KeyHint> Collect(HintContext c)
    {
        if (!c.HasImage)
        {
            yield return new KeyHint(new[] { "Ctrl", "K" }, "S_HintSearch", ModifierKeys.Control);
            yield break;
        }

        // Wer wartet, braucht nur zwei Dinge: wohin klicken und wie heraus.
        if (c.ColourWaiting || c.ObjectWaiting)
        {
            yield return new KeyHint(new[] { "Click" }, c.ObjectWaiting ? "S_HintTakeObject" : "S_HintTakeColour");
            yield return new KeyHint(new[] { "Esc" }, "S_HintCancel");
            yield break;
        }

        if (c.OverLayers && !c.InNodes)
        {
            yield return new KeyHint(new[] { "Click" }, "S_HintLayerChoose");
            yield return new KeyHint(new[] { "Drag" }, "S_HintLayerDrag");
            yield return new KeyHint(new[] { "Alt", "Click", "●" }, "S_HintLayerSolo", ModifierKeys.Alt);
            yield return new KeyHint(new[] { "Alt", "Click", "◐" }, "S_HintMaskSolo", ModifierKeys.Alt);
            yield return new KeyHint(new[] { "Alt", "Shift", "Click", "◐" }, "S_HintMaskVeil", ModifierKeys.Alt | ModifierKeys.Shift);
            yield return new KeyHint(new[] { "Alt", "↑↓" }, "S_HintLayerMove", ModifierKeys.Alt);
            yield return new KeyHint(new[] { "Ctrl", "Click" }, "S_HintLayerSeveral", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Shift", "Click" }, "S_HintLayerRange", ModifierKeys.Shift);
            yield return new KeyHint(new[] { "Ctrl", "J" }, "S_HintLayerDuplicate", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Ctrl", "G" }, "S_HintLayerIndent", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Ctrl", "Shift", "G" }, "S_HintLayerOutdent", ModifierKeys.Control | ModifierKeys.Shift);
            yield return new KeyHint(new[] { "Del" }, "S_HintLayerDelete");
            yield break;
        }

        if (c.InNodes && c.OverGraph)
        {
            yield return new KeyHint(new[] { "DoubleClick" }, "S_HintNodeQuick");
            yield return new KeyHint(new[] { "Drag" }, "S_HintNodeDrag");
            yield return new KeyHint(new[] { "Del" }, "S_HintNodeDelete");
            yield return new KeyHint(new[] { "Ctrl", "D" }, "S_HintNodeDuplicate", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Ctrl", "Z" }, "S_HintUndo", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Ctrl", "Y" }, "S_HintRedo", ModifierKeys.Control);
            yield return new KeyHint(new[] { "Ctrl", "K" }, "S_HintSearch", ModifierKeys.Control);
            yield break;
        }

        foreach (var hint in ToolHints(c)) yield return hint;

        if (c.CurvesShown) yield return new KeyHint(new[] { "Ctrl", "Click" }, "S_HintCurvePoint", ModifierKeys.Control);
        if (c.Escapable) yield return new KeyHint(new[] { "Esc" }, "S_HintBack");

        yield return new KeyHint(new[] { "O" }, "S_HintOriginal");
        yield return new KeyHint(new[] { "J" }, "S_HintViewAid");
        yield return new KeyHint(new[] { string.Join(" ", ToolKeys.Select(t => t.Key.ToString())) }, "S_HintToolKeys");
        yield return new KeyHint(new[] { "Ctrl", "K" }, "S_HintSearch", ModifierKeys.Control);
    }

    private static IEnumerable<KeyHint> ToolHints(HintContext c)
    {
        switch (c.Tool)
        {
            case AtelierTool.Move:
                yield return new KeyHint(new[] { "Drag" }, "S_HintMoveLayer");
                yield return new KeyHint(new[] { "Grip" }, "S_HintResize");
                yield return new KeyHint(new[] { "TurnGrip" }, "S_HintTurn");
                yield return new KeyHint(new[] { "Wheel" }, "S_HintZoom");
                yield return new KeyHint(new[] { "Middle", "Drag" }, "S_HintPan");
                break;

            case AtelierTool.Select:
                yield return new KeyHint(new[] { "Click" }, "S_HintSelectPick");
                yield return new KeyHint(new[] { "Shift", "Click" }, "S_HintSelectAdd", ModifierKeys.Shift);
                yield return new KeyHint(new[] { "Alt", "Click" }, "S_HintSelectRemove", ModifierKeys.Alt);
                break;

            case AtelierTool.Crop:
                yield return new KeyHint(new[] { "Grip" }, "S_HintCrop");
                yield return new KeyHint(new[] { "Middle", "Drag" }, "S_HintPan");
                break;

            case AtelierTool.Hand:
                yield return new KeyHint(new[] { "Drag" }, "S_HintPan");
                break;

            case AtelierTool.Brush when c.Area == PaintArea.None:
                yield return new KeyHint(new[] { "Drag" }, "S_HintPaint");
                yield return new KeyHint(new[] { "RightDrag" }, "S_HintErase");
                yield return new KeyHint(new[] { "Alt", "Drag" }, "S_HintErase", ModifierKeys.Alt);
                yield return new KeyHint(new[] { "Shift", "Click" }, "S_HintLine", ModifierKeys.Shift);
                yield return new KeyHint(new[] { "Shift", "Wheel" }, "S_HintTipTurn", ModifierKeys.Shift);
                yield return new KeyHint(new[] { "Ctrl", "Wheel" }, "S_HintSpacing", ModifierKeys.Control);
                yield return new KeyHint(new[] { "Wheel" }, "S_HintZoom");
                break;

            case AtelierTool.Brush when c.Area == PaintArea.Lasso:
                yield return new KeyHint(new[] { "Drag" }, "S_HintLasso");
                yield return new KeyHint(new[] { "Alt", "Drag" }, "S_HintAreaRemove", ModifierKeys.Alt);
                yield return new KeyHint(new[] { "Wheel" }, "S_HintZoom");
                break;

            case AtelierTool.Brush:
                yield return new KeyHint(new[] { "Drag" }, "S_HintArea");
                yield return new KeyHint(new[] { "Shift", "Drag" }, "S_HintAreaEven", ModifierKeys.Shift);
                yield return new KeyHint(new[] { "Alt", "Drag" }, "S_HintAreaRemove", ModifierKeys.Alt);
                yield return new KeyHint(new[] { "Wheel" }, "S_HintZoom");
                break;

            case AtelierTool.Pick:
                yield return new KeyHint(new[] { "Click" }, "S_HintRead");
                if (c.ColourMask) yield return new KeyHint(new[] { "Shift", "Click" }, "S_HintWiden", ModifierKeys.Shift);
                break;

            case AtelierTool.Nodes:
                yield return new KeyHint(new[] { "Click" }, "S_HintNodeChoose");
                yield return new KeyHint(new[] { "DoubleClick" }, "S_HintNodeQuick");
                break;
        }
    }
}
