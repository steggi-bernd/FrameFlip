using System.Windows;
using System.Windows.Controls;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Die Eigenschaften des gewaehlten Werkzeugs - der oberste Abschnitt der rechten
/// Spalte.
///
/// Sie zeigt, was das Werkzeug tut, und die Werte, die NUR zu ihm gehoeren. Was zur
/// EBENE gehoert - Mischung, Deckkraft, Maske, Platzierung - bleibt beim
/// Ebenenstreifen: Es gilt weiter, wenn man das Werkzeug wechselt, und waere hier eine
/// Zahl, die verschwindet, sobald man etwas anderes anfasst.
///
/// Der Fall, der diesen Abschnitt rechtfertigt, ist die Scharfstellung. Die
/// nachtraegliche Tiefenschaerfe verlangt eine Entfernung in Szeneneinheiten; ohne
/// Pipette ist die einzige Art, sie zu beantworten, ein Regler und Probieren - und
/// probiert wird an einer Zahl, die man nirgends ablesen kann.
/// </summary>
public partial class PropertiesPanel : UserControl
{
    public PropertiesPanel() => InitializeComponent();

    /// <summary>Jemand moechte die gelesene Entfernung als Scharfstellung.</summary>
    public event Action<float>? FocusWanted;

    private float? _depth;

    /// <summary>Welches Werkzeug gilt - danach richtet sich, was hier steht.</summary>
    public void Show(AtelierTool tool)
    {
        ToolName.Text = Strings.T(tool switch
        {
            AtelierTool.Select => "S_ToolSelect",
            AtelierTool.Crop => "S_ToolCrop",
            AtelierTool.Hand => "S_ToolHand",
            AtelierTool.Pick => "S_ToolPick",
            AtelierTool.Brush => "S_ToolBrush",
            _ => "S_ToolMove",
        });

        ToolHint.Text = Strings.T(tool switch
        {
            AtelierTool.Select => "S_ToolSelectShort",
            AtelierTool.Crop => "S_ToolCropShort",
            AtelierTool.Hand => "S_ToolHandShort",
            AtelierTool.Pick => "S_ToolPickShort",
            AtelierTool.Brush => "S_ToolBrushShort",
            _ => "S_ToolMoveShort",
        });

        PickBody.Visibility = tool == AtelierTool.Pick ? Visibility.Visible : Visibility.Collapsed;

        if (tool != AtelierTool.Pick) FocusNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Traegt ein, was die Pipette gelesen hat.
    ///
    /// <paramref name="depth"/> ist null, wenn die Datei keinen Tiefenpass fuehrt -
    /// dann faellt die Zeile weg, statt eine Null zu zeigen. Eine Null waere eine
    /// Entfernung, und "keine Entfernung bekannt" ist etwas anderes als "null Meter".
    /// </summary>
    public void Read(int x, int y, int r, int g, int b, float lr, float lg, float lb, float? depth)
    {
        PickWhere.Text = $"{x}, {y}";
        PickByte.Text = $"{r} / {g} / {b}";
        PickLight.Text = $"{lr:0.###}  {lg:0.###}  {lb:0.###}";

        _depth = depth;

        DepthRow.Visibility = depth is null ? Visibility.Collapsed : Visibility.Visible;
        FocusButton.IsEnabled = depth is > 0f;

        if (depth is { } metres) PickDepth.Text = $"{metres:0.###}";

        FocusNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>Sagt, was aus dem Uebernehmen geworden ist.</summary>
    public void Told(string text)
    {
        FocusNote.Text = text;
        FocusNote.Visibility = Visibility.Visible;
    }

    private void OnUseAsFocus(object sender, RoutedEventArgs e)
    {
        if (_depth is { } metres && metres > 0f) FocusWanted?.Invoke(metres);
    }
}
