namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Die Werkzeuge einer Korrektur, in ihrer Reihenfolge.
///
/// Noch ist es eine einzelne Ebene ohne Masken - der Stapel ist trotzdem schon die
/// richtige Form, weil sich Ebenen spaeter aus ihm bilden lassen, ein einzelner
/// Satz Regler aber nicht.
///
/// Was der Stapel NICHT tut: die Grundkorrektur ersetzen. Belichtung, Schwarzpunkt,
/// Gamma und Kontrast bleiben in <see cref="ImageAdjustments"/> und wirken vor ihm.
/// Sie sind der schnelle Griff beim Beurteilen, den es auch ohne EXR und ohne
/// Werkzeuge geben soll; der Stapel ist das, was jemand bewusst dazunimmt.
/// </summary>
public sealed class GradingStack
{
    public List<IGradingTool> Tools { get; set; } = new();

    /// <summary>True, wenn kein Werkzeug etwas zu tun hat.</summary>
    public bool IsNeutral => Tools.Count == 0 || Tools.All(t => t.IsNeutral);

    /// <summary>
    /// Bereitet alle Werkzeuge vor und sammelt die ein, die tatsaechlich etwas tun.
    ///
    /// Zweimal getrennt nach Seite, weil der Prozessor sie an zwei verschiedenen
    /// Stellen braucht - vor und nach der Sichtumwandlung. Die Aufteilung hier zu
    /// machen statt in der inneren Schleife spart je Bildpunkt eine Verzweigung, und
    /// bei 4K sind das 25 Millionen.
    /// </summary>
    public PreparedGrading Prepare()
    {
        var linear = new List<IGradingTool>();
        var display = new List<IGradingTool>();

        foreach (var tool in Tools)
        {
            if (tool.IsNeutral) continue;

            tool.Prepare();
            (tool.Stage == GradingStage.SceneLinear ? linear : display).Add(tool);
        }

        return new PreparedGrading(linear.ToArray(), display.ToArray());
    }

    public GradingStack Clone() => new()
    {
        Tools = Tools.Select(t => t is CurvesTool curves ? curves.Clone() : t).ToList(),
    };
}

/// <summary>
/// Der vorbereitete Stapel, aufgeteilt nach der Seite der Sichtumwandlung.
///
/// Felder statt Listen: die Schleife laeuft ueber sie je Bildpunkt, und ein Feld
/// laesst sich ohne Umweg ueber eine Schnittstelle durchlaufen.
/// </summary>
public readonly struct PreparedGrading
{
    public PreparedGrading(IGradingTool[] sceneLinear, IGradingTool[] display)
    {
        SceneLinear = sceneLinear;
        Display = display;
    }

    public IGradingTool[] SceneLinear { get; }

    public IGradingTool[] Display { get; }

    public bool IsEmpty => SceneLinear.Length == 0 && Display.Length == 0;

    public static readonly PreparedGrading None =
        new(Array.Empty<IGradingTool>(), Array.Empty<IGradingTool>());
}
