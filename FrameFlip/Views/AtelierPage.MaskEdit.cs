using System.Windows;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die ganze Maske auf einmal bearbeiten (docs/Atelier-Werkzeugplan.md, W1): umkehren,
/// fuellen, leeren, weiche Kante, ausweiten, schrumpfen. Jede Bearbeitung ist ein Strich
/// (<see cref="PaintStroke.Edit"/>) - der Maskenverlauf fuehrt sie wie jeden Zug, und
/// Strg+Z nimmt sie zurueck wie jede andere Aenderung am Graphen.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Weiten, die das Menue anbietet, in Bildpunkten.</summary>
    internal static readonly float[] MaskEditAmounts = { 2f, 5f, 10f, 25f };

    /// <summary>
    /// Wie weit sich das Verfeinern nach einer Kante umsieht, in Bildpunkten. Weiter als beim
    /// Ausweiten: Eine grob gemalte Maske liegt oft ein gutes Stueck neben der Kante.
    /// </summary>
    internal static readonly float[] MaskRefineAmounts = { 4f, 8f, 16f, 32f };

    /// <summary>Das zuletzt geoeffnete Menue der Maskenbearbeitung - fuer die Probe.</summary>
    internal FlipMenu? MaskEditMenu { get; private set; }

    /// <summary>Das Menue: die drei sofortigen Schritte, dann die drei mit einer Weite.</summary>
    private void ShowMaskEditMenu(MaskNode mask, FrameworkElement anchor)
    {
        bool painted = mask.Mask.PaintFor(_number) is not null;

        var menu = new FlipMenu(anchor)
            .Item("◑", Strings.T("S_MaskEditInvert"), () => EditMask(mask, PaintEdit.Invert))
            .Item("■", Strings.T("S_MaskEditFill"), () => EditMask(mask, PaintEdit.Fill))
            .Item("□", Strings.T("S_MaskEditClear"), () => EditMask(mask, PaintEdit.Clear), enabled: painted)
            .Separator()
            .Item("≈", Strings.T("S_MaskEditFeather"), () => ShowMaskAmountMenu(mask, PaintEdit.Feather, anchor), enabled: painted)
            .Item("⊕", Strings.T("S_MaskEditGrow"), () => ShowMaskAmountMenu(mask, PaintEdit.Grow, anchor), enabled: painted)
            .Item("⊖", Strings.T("S_MaskEditShrink"), () => ShowMaskAmountMenu(mask, PaintEdit.Shrink, anchor), enabled: painted)
            .Separator()
            .Item("⌁", Strings.T("S_MaskEditRefine"), () => ShowMaskAmountMenu(mask, PaintEdit.Refine, anchor), enabled: painted);

        MaskEditMenu = menu;
        menu.Open();
    }

    /// <summary>Die Weite fuer weiche Kante, ausweiten oder schrumpfen - ein zweites Menue.</summary>
    private void ShowMaskAmountMenu(MaskNode mask, PaintEdit edit, FrameworkElement anchor)
    {
        var menu = new FlipMenu(anchor);

        foreach (float amount in edit == PaintEdit.Refine ? MaskRefineAmounts : MaskEditAmounts)
            menu.Item("", $"{amount:0} px", () => EditMask(mask, edit, amount));

        MaskEditMenu = menu;
        menu.Open();
    }

    /// <summary>
    /// Bearbeitet die gemalte Maske dieses Knotens fuer das offene Bild. False, wenn es
    /// nichts zu bearbeiten gab oder sich nichts aendern wuerde - dann entsteht auch kein
    /// Schritt im Verlauf.
    /// </summary>
    internal bool EditMask(MaskNode mask, PaintEdit edit, float amount = 0f)
    {
        if (_graph is null || _frame is null || mask.Mask.Kind != MaskKind.Painted) return false;

        // Fuellen und Umkehren brauchen keinen Anstrich - sie legen ihn an. Die anderen
        // haetten an einer leeren Maske nichts zu tun.
        var paint = edit is PaintEdit.Fill or PaintEdit.Invert
            ? mask.Mask.PaintOn(_number, _frame.Width, _frame.Height)
            : mask.Mask.PaintFor(_number);

        if (paint is null) return false;

        // Der Verlauf kennt den Stand davor - vor der ersten Aenderung.
        WatchMask(mask, paint);

        var stroke = edit == PaintEdit.Refine
            ? RefineStroke(paint, amount)
            : new PaintStroke { Edit = edit, Amount = amount, Version = PaintStroke.CurrentVersion };

        if (stroke is null) return false;

        // Erst zur Probe: Aendert sich nichts, gibt es auch keinen Schritt zum Zuruecknehmen.
        // An der Deckung, wie sie gerade ist - Clone kopierte nur den gepackten Stand, und der
        // hinkt nach einem Strich hinterher, bis er festgehalten ist.
        var trial = PaintedMask.FromCover(paint.Width, paint.Height, paint.Cover());
        stroke.Replay(trial);
        if (trial.Cover().AsSpan().SequenceEqual(paint.Cover())) return false;

        RememberNodes();

        stroke.Replay(paint);
        paint.Keep();

        _maskHistories.Record(mask.Mask, _number, paint, stroke);

        Placement.MaskChanged();
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return true;
    }

    /// <summary>
    /// Kanten verfeinern: am Bild gerechnet und als fertiges Ergebnis in den Strich gepackt.
    /// Gefuehrt wird vom gelesenen Bild selbst, nicht vom fertig gerechneten - die Kanten, an
    /// die eine Maske gehoert, sind die des Renders, nicht die einer Korrektur darauf.
    /// </summary>
    private PaintStroke? RefineStroke(PaintedMask paint, float amount)
    {
        if ((_base ?? _frame) is not { } image) return null;

        var guide = MaskRefine.Guide(image, paint.Width, paint.Height, PaintedMask.Coarse);
        int radius = Math.Max(1, (int)MathF.Round(amount / PaintedMask.Coarse));
        var refined = MaskRefine.Refine(paint.Cover(), guide, paint.Width, paint.Height, radius);

        return new PaintStroke
        {
            Edit = PaintEdit.Refine,
            Amount = amount,
            Result = PaintedMask.Pack(refined),
            Version = PaintStroke.CurrentVersion,
        };
    }
}
