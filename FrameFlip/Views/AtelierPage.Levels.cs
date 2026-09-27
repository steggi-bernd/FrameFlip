using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Views;

/// <summary>
/// Auto-Tonwert und die Pipetten des Tonwerts (W2b im Werkzeugplan). Beides braucht das Bild
/// so, wie es beim Tonwert ANKOMMT: Gemessen am fertigen Bild, in dem er schon wirkt, liefe
/// ein zweites Auto weiter, und eine Pipette traefe einen Ton, den es vor ihm nicht gab.
///
/// Im Stapel heisst das: derselbe Weg wie die Messung, aber nur mit den Anzeigewerkzeugen
/// VOR dem Tonwert. Im Knotenmodus: der Graph bis zum Kabel, das in den Tonwert fuehrt - wie
/// ein Betrachter, nur ohne die Anzeige anzufassen.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Pipette, die auf einen Klick ins Bild wartet: welcher Tonwert, welcher Punkt.</summary>
    private (LevelsTool Tool, LevelsPickKind Kind)? _levelsPick;

    /// <summary>Das Werkzeug der Maus vor der Pipette - danach geht es dorthin zurueck.</summary>
    private AtelierTool _beforeLevelsPick = AtelierTool.Move;

    private void SetUpLevels()
    {
        Tools.LevelsAutoWanted += (tool, kind) => AutoLevels(tool, kind);
        Tools.LevelsPickWanted += StartLevelsPick;
    }

    /// <summary>Auto am Tonwert - aus dem, was bei ihm ankommt.</summary>
    internal bool AutoLevels(LevelsTool tool, LevelsAutoKind kind)
    {
        if (LevelsInput(tool, step: 4) is not { } sample) return false;

        LevelsAuto.Apply(tool, sample.Rgb, kind);
        Tools.LevelsChangedOutside();

        return true;
    }

    /// <summary>Eine Pipette wurde gewaehlt - oder abgewaehlt (null).</summary>
    private void StartLevelsPick(LevelsTool tool, LevelsPickKind? kind)
    {
        if (kind is not { } chosen)
        {
            EndLevelsPick();
            return;
        }

        if (_levelsPick is null) _beforeLevelsPick = _tool == AtelierTool.Pick ? AtelierTool.Move : _tool;

        _levelsPick = (tool, chosen);

        MouseTools.Select(AtelierTool.Pick);
        OnToolChanged(AtelierTool.Pick);
    }

    /// <summary>Die Pipette ist fertig oder wurde verlassen.</summary>
    private void EndLevelsPick()
    {
        if (_levelsPick is null) return;

        _levelsPick = null;
        Tools.EndLevelsPick();
    }

    /// <summary>
    /// Ein Klick ins Bild mit einer Pipette des Tonwerts: Der Ton, der dort beim Tonwert ankommt,
    /// wird Schwarz, Grau oder Weiss. Danach geht die Maus zu ihrem vorigen Werkzeug zurueck.
    /// </summary>
    internal bool LevelsPickAt(int x, int y)
    {
        if (_levelsPick is not { } pick) return false;
        if (LevelsInputAt(pick.Tool, x, y) is not var (r, g, b)) return false;

        LevelsAuto.Pick(pick.Tool, pick.Kind, r, g, b);
        Tools.LevelsChangedOutside();

        LeaveLevelsPick();

        return true;
    }

    /// <summary>Beendet eine wartende Pipette, und die Maus geht zu ihrem vorigen Werkzeug zurueck.</summary>
    private void LeaveLevelsPick()
    {
        if (_levelsPick is null) return;

        var back = _beforeLevelsPick;
        EndLevelsPick();

        MouseTools.Select(back);
        OnToolChanged(back);
    }

    /// <summary>Ob eine Pipette des Tonwerts wartet - fuer die Probe.</summary>
    internal bool LevelsPicking => _levelsPick is not null;

    // ------------------------------------------------------------------ was ankommt

    /// <summary>
    /// Was beim Tonwert ankommt, als Stichprobe jedes <paramref name="step"/>-ten Punktes. Null,
    /// wenn es nichts zu messen gibt.
    /// </summary>
    internal (float[] Rgb, int Width, int Height)? LevelsInput(LevelsTool tool, int step)
    {
        if (InNodes) return NodeInput(tool, step) is { } node ? (node.Rgb, node.Width, node.Height) : null;

        var frame = _frame;
        if (frame is null) return null;

        return FloatFrameProcessor.Sample(frame, _finalAdjustments, ViewFor(frame), BeforeLevels(tool), step, _number);
    }

    /// <summary>Der Ton, der an einem Bildpunkt beim Tonwert ankommt.</summary>
    private (float R, float G, float B)? LevelsInputAt(LevelsTool tool, int x, int y)
    {
        if (InNodes)
        {
            if (NodeInput(tool, step: 2) is not { } node) return null;

            int column = Nearest(node.Columns, x), row = Nearest(node.Rows, y);
            int i = (row * node.Width + column) * 3;

            return (node.Rgb[i], node.Rgb[i + 1], node.Rgb[i + 2]);
        }

        var frame = _frame;
        if (frame is null) return null;

        return FloatFrameProcessor.SampleAt(frame, _finalAdjustments, ViewFor(frame), BeforeLevels(tool), x, y, _number);
    }

    /// <summary>
    /// Die fertigen Werkzeuge ohne den Tonwert und alles, was im Stapel nach ihm kommt. Steht er
    /// nicht im Stapel des ganzen Bildes - etwa bei einer Ebene -, bleibt es beim fertigen Bild.
    /// </summary>
    private PreparedGrading BeforeLevels(LevelsTool tool)
    {
        var tools = Tools.Stack.Tools;
        int at = tools.FindIndex(t => ReferenceEquals(t, tool));
        if (at < 0) return _finalGrading;

        var after = new HashSet<IGradingTool>(tools.Skip(at), ReferenceEqualityComparer.Instance);
        var g = _finalGrading;

        return new PreparedGrading(g.SceneLinear, g.Display.Where(t => !after.Contains(t)).ToArray(), g.Local,
                                   g.LocalLight, g.Optics, g.Geometry, g.Data, g.Frame);
    }

    /// <summary>
    /// Im Knotenmodus: der Graph bis zum Kabel, das in den Knoten des Tonwerts fuehrt - mit
    /// eigenem Vorrat und ohne Zwischenspeicher, damit die laufende Anzeige nichts merkt.
    /// </summary>
    private (float[] Rgb, int Width, int Height, int[] Columns, int[] Rows)? NodeInput(LevelsTool tool, int step)
    {
        if (_graph is null || _base is null) return null;

        Node? owner = _graph.Nodes.OfType<PointToolNode>().FirstOrDefault(n => ReferenceEquals(n.Tool, tool))
                      ?? (Node?)_graph.Nodes.OfType<LayerGradeNode>().FirstOrDefault(n => n.Tools?.Tools.Contains(tool) == true);

        if (owner is null || NodeEdits.Through(owner).Input is not { } input || _graph.Into(owner.Id, input) is not { } link)
            return null;

        var inputs = new GraphInputs
        {
            Sources = _sources,
            Data = NodeData(),
            View = ViewFor(_base),
            Step = step,
            Number = _number,
            Pool = new GridPool(),
            Viewer = (link.From, link.Output),
        };

        var (image, context) = GraphEvaluator.Evaluate(_graph, inputs, sixteen: false);
        if (image is null || context is null) return null;

        return (image.Rgb, context.GridWidth, context.GridHeight, context.Columns, context.Rows);
    }

    private static int Nearest(int[] positions, int value)
    {
        int best = 0;

        for (int i = 1; i < positions.Length; i++)
            if (Math.Abs(positions[i] - value) < Math.Abs(positions[best] - value)) best = i;

        return best;
    }
}
