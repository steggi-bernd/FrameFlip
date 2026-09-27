using System.Windows.Input;
using System.Windows.Threading;
using FrameFlip.Atelier;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Views;

/// <summary>
/// Das Eigenschaftenfeld folgt dem Ziel (docs/Atelier-Arbeitsablauf.md, C5a, Punkt 8).
///
/// Im Knotenmodus zeigte das Farbfeld schon die Einstellungen des gewaehlten Knotens - aber
/// als Reiter neben den Ebenen, und zugeklappt, wenn man es zugeklappt hatte. Wer einen Knoten
/// waehlte, musste erst den Reiter suchen. Jetzt kommt das Feld beim Waehlen eines Knotens mit
/// Einstellungen nach vorn und klappt auf. Ist danach nichts mehr mit Einstellungen gewaehlt,
/// geht es dorthin zurueck, wo es vorher stand - solange man die Anordnung in der Zwischenzeit
/// nicht selbst angefasst hat. Dann gilt, was man selbst eingestellt hat.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Das Feld, das die Einstellungen des Ziels zeigt - im Stapel "Farbe", im Knotenmodus "Eigenschaften".</summary>
    private const string PropertiesPanelId = "colour";

    /// <summary>Wie die Gruppe des Feldes stand, bevor das Ziel sie geoeffnet hat. Null: nichts zurueckzugeben.</summary>
    private (string? Active, bool Collapsed)? _propertiesBefore;

    /// <summary>Ob die Seite gerade selbst die Anordnung aendert - dann ist es kein Griff des Nutzers.</summary>
    private bool _dockFollowing;

    /// <summary>Ob gerade in der Liste der Ebenen gewaehlt wird.</summary>
    private bool _choosingInList;

    /// <summary>Ob das Folgen auf das Loslassen der Maus wartet.</summary>
    private bool _followPending;

    private void SetUpPropertiesFollow()
    {
        _recipe.TargetChanged += FollowTarget;

        Dock.LayoutChanged += _ =>
        {
            if (!_dockFollowing) _propertiesBefore = null;
        };

        // Auf- und Zuklappen baut die ganze Flaeche neu und haengt dabei auch den Graphen aus.
        // Mitten in einem Klick verloere der Editor seinen Griff - ein Knoten liesse sich nicht
        // mehr ziehen. Dann wartet das Folgen, bis die Maus losgelassen ist.
        PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_followPending) return;

            _followPending = false;
            Dispatcher.BeginInvoke(FollowTarget, DispatcherPriority.Input);
        };
    }

    /// <summary>Ob das Ziel im Feld etwas einzustellen hat - ein Knoten mit Karte oder Feldern.</summary>
    private bool TargetHasSettings()
        => InNodes && _recipe.Target is EditingTarget.GraphNode { Node: var node } &&
           (node is LayerGradeNode || NodeTitles.Section(node) is not null || NodeFields.For(node, FieldContext()).Count > 0);

    private void FollowTarget()
    {
        if (!InNodes)
        {
            _propertiesBefore = null;
            return;
        }

        if (Dock.Layout.Find(PropertiesPanelId) is not { } at) return;

        var group = Dock.Layout.Zone(at.Zone)[at.Group];
        bool open = group.Active == PropertiesPanelId && !group.Collapsed;

        if (TargetHasSettings())
        {
            if (open) return;

            // Nicht das Feld verdecken, in dem gerade gewaehlt wird: Kommt die Wahl aus der
            // Liste der Ebenen und liegt die als Reiter in derselben Gruppe, bleibt sie vorn.
            if ((_choosingInList || _recipe.Target is EditingTarget.GraphNode { FromLayerList: true }) &&
                group.Panels.Contains("layers"))
                return;

            if (Waits(group.Collapsed)) return;

            _propertiesBefore ??= (group.Active, group.Collapsed);
            Change(() => Dock.Activate(PropertiesPanelId));
            return;
        }

        // Nichts mehr mit Einstellungen: zurueck, wie es war - wenn die Seite es geoeffnet hat
        // und es noch so steht, wie sie es hinterlassen hat.
        if (_propertiesBefore is not { } before) return;

        if (!open)
        {
            _propertiesBefore = null;
            return;
        }

        if (Waits(before.Collapsed)) return;

        _propertiesBefore = null;

        Change(() =>
        {
            if (before.Active is { } previous && previous != PropertiesPanelId && group.Panels.Contains(previous))
                Dock.Activate(previous);

            // Liegt das Feld vorn und offen, klappt ein Klick auf seinen Reiter die Gruppe ein.
            if (before.Collapsed) Dock.Toggle(group.Active!);
        });
    }

    /// <summary>Ob das Folgen auf das Loslassen warten muss - nur, wenn es die Flaeche neu baut.</summary>
    private bool Waits(bool reshapes)
    {
        if (!reshapes || Mouse.LeftButton != MouseButtonState.Pressed) return false;

        _followPending = true;
        return true;
    }

    private void Change(Action change)
    {
        _dockFollowing = true;

        try { change(); }
        finally { _dockFollowing = false; }
    }
}
