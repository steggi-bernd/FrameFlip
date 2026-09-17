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

    /// <summary>
    /// Die Werkzeuge mit oertlicher Wirkung - Rauschminderung, Klarheit, Schaerfe.
    ///
    /// Eine eigene Liste und nicht dieselbe, weil sie einen anderen Weg nehmen: Sie
    /// brauchen einen Zwischenpuffer und einen zweiten Durchgang. Sie in dieselbe
    /// Kette zu haengen hiesse, jedes Bild durch diesen Puffer zu zwaengen, auch
    /// wenn gar keines von ihnen benutzt wird.
    ///
    /// Die Reihenfolge in dieser Liste bedeutet nichts - jedes Werkzeug bringt seine
    /// Stufe mit, und <see cref="Prepare"/> sortiert danach.
    /// </summary>
    public List<ILocalTool> Local { get; set; } = new();

    /// <summary>True, wenn kein Werkzeug etwas zu tun hat.</summary>
    public bool IsNeutral
        => (Tools.Count == 0 || Tools.All(t => t.IsNeutral)) &&
           (Local.Count == 0 || Local.All(t => t.IsNeutral));

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
        var local = new List<ILocalTool>();

        foreach (var tool in Tools)
        {
            if (tool.IsNeutral) continue;

            tool.Prepare();
            (tool.Stage == GradingStage.SceneLinear ? linear : display).Add(tool);
        }

        // Sortiert, nicht in der Reihenfolge der Liste: Rauschminderung vor
        // Klarheit vor Schaerfe. Wer in welcher Reihenfolge an den Reglern war, darf
        // das Ergebnis nicht bestimmen - und die Reihenfolge, die hier steht, ist
        // die einzige, die in beide Richtungen Sinn ergibt.
        foreach (var tool in Local.OrderBy(t => t.Stage))
        {
            if (tool.IsNeutral) continue;

            tool.Prepare();
            local.Add(tool);
        }

        return new PreparedGrading(linear.ToArray(), display.ToArray(), local.ToArray());
    }

    /// <summary>
    /// Eine Kopie, deren Werkzeuge nicht mehr dieselben Objekte sind.
    ///
    /// Gebraucht, sobald ein Rezept festgehalten wird, waehrend am Original
    /// weitergeregelt wird - beim Vergleich zweier Einstellungen und spaeter beim
    /// Stapellauf, der nicht mitbekommen darf, dass jemand am Regler zieht.
    /// </summary>
    public GradingStack Clone() => new()
    {
        Tools = Tools.Select(Copy).ToList(),
        Local = Local.Select(CopyLocal).ToList(),
    };

    private static ILocalTool CopyLocal(ILocalTool tool) => tool switch
    {
        ClarityTool clarity => new ClarityTool { Amount = clarity.Amount, Reach = clarity.Reach },

        SharpenTool sharpen => new SharpenTool
        {
            Amount = sharpen.Amount, Reach = sharpen.Reach, Threshold = sharpen.Threshold,
        },

        NoiseTool noise => new NoiseTool
        {
            Luminance = noise.Luminance, Colour = noise.Colour,
            Threshold = noise.Threshold, Reach = noise.Reach,
        },

        // Wie oben: Ein Werkzeug, das hier fehlt, wuerde geteilt statt kopiert.
        _ => throw new NotSupportedException($"Kein Kopierweg fuer {tool.GetType().Name}."),
    };

    private static IGradingTool Copy(IGradingTool tool) => tool switch
    {
        CurvesTool curves => curves.Clone(),
        LiftGammaGainTool lgg => lgg.Clone(),
        WhiteBalanceTool wb => new WhiteBalanceTool { Kelvin = wb.Kelvin, Tint = wb.Tint },
        VibranceTool vibrance => new VibranceTool { Amount = vibrance.Amount },
        HslTool hsl => hsl.Clone(),
        LutTool lut => lut.Clone(),

        // Ein Werkzeug, das hier fehlt, wuerde geteilt statt kopiert - und der
        // Fehler faellt erst auf, wenn eine festgehaltene Einstellung sich
        // mitbewegt. Lieber sofort laut.
        _ => throw new NotSupportedException($"Kein Kopierweg fuer {tool.GetType().Name}."),
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
    public PreparedGrading(IGradingTool[] sceneLinear, IGradingTool[] display, ILocalTool[] local)
    {
        SceneLinear = sceneLinear;
        Display = display;
        Local = local;
    }

    public IGradingTool[] SceneLinear { get; }

    public IGradingTool[] Display { get; }

    /// <summary>
    /// Die oertlichen Werkzeuge, in ihrer festen Reihenfolge. Leer heisst: kein
    /// Zwischenpuffer, kein zweiter Durchgang - und das ist der Normalfall.
    ///
    /// Den Radius bringt jedes selbst mit. Frueher stand hier der groesste fuer alle;
    /// das war richtig, solange nur die Klarheit hier stand, und wurde falsch mit der
    /// Schaerfe - zwei Werkzeuge, deren Radien um das Zwanzigfache auseinanderliegen,
    /// koennen sich keine Weichzeichnung teilen.
    /// </summary>
    public ILocalTool[] Local { get; } = Array.Empty<ILocalTool>();

    public bool IsEmpty => SceneLinear.Length == 0 && Display.Length == 0 && Local.Length == 0;

    public static readonly PreparedGrading None =
        new(Array.Empty<IGradingTool>(), Array.Empty<IGradingTool>(), Array.Empty<ILocalTool>());
}
