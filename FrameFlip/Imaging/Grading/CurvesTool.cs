using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Gradationskurven: eine gemeinsame und eine je Kanal.
///
/// Das wichtigste einzelne Werkzeug der Farbkorrektur und bisher das einzige, das
/// ganz fehlte. Was Belichtung, Gamma und Kontrast zusammen an einer Kurve biegen
/// koennen, ist ein Ausschnitt dessen, was sich hier von Hand einstellen laesst -
/// etwa Schatten anheben, ohne die Lichter mitzunehmen, was mit Gamma nicht geht.
///
/// Die Reihenfolge ist die uebliche: erst die gemeinsame Kurve auf alle drei
/// Kanaele, dann die einzelnen. So bleibt ein Farbstich, der ueber eine Kanalkurve
/// gesetzt wurde, von einer spaeteren Aenderung an der gemeinsamen unberuehrt.
/// </summary>
public sealed class CurvesTool : IGradingTool
{
    public const string KindName = "curves";

    public string Kind => KindName;

    /// <summary>
    /// Nach der Sichtumwandlung. Eine Gradationskurve laeuft von Schwarz nach Weiss
    /// und braucht dafuer ein Weiss - im linearen Szenenlicht gibt es keines.
    /// </summary>
    public GradingStage Stage => GradingStage.Display;

    public ToneCurve Master { get; set; } = new();

    public ToneCurve Red { get; set; } = new();

    public ToneCurve Green { get; set; } = new();

    public ToneCurve Blue { get; set; } = new();

    [JsonIgnore]
    public bool IsNeutral => Master.IsIdentity && Red.IsIdentity && Green.IsIdentity && Blue.IsIdentity;

    public void Prepare()
    {
        Master.Prepare();
        Red.Prepare();
        Green.Prepare();
        Blue.Prepare();
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        r = Master.Evaluate(r);
        g = Master.Evaluate(g);
        b = Master.Evaluate(b);

        r = Red.Evaluate(r);
        g = Green.Evaluate(g);
        b = Blue.Evaluate(b);
    }

    public CurvesTool Clone() => new()
    {
        Master = Master.Clone(),
        Red = Red.Clone(),
        Green = Green.Clone(),
        Blue = Blue.Clone(),
    };
}
