using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>Eine Pinseleinstellung hat sich geaendert.</summary>
    public event Action? BrushChanged;

    /// <summary>Der Pinselradius in Bildpunkten.</summary>
    public float BrushRadius => (float)BrushSizeSlider.Value / 2f;

    /// <summary>Wie hart die Kante ist. 0 ist ein Verlauf, 1 eine Scheibe.</summary>
    public float BrushHardness => (float)BrushHardnessSlider.Value;

    /// <summary>Wie schnell ein Strich auftraegt.</summary>
    public float BrushFlow => (float)BrushFlowSlider.Value;

    /// <summary>Bis wohin ein Strich ueberhaupt auftraegt.</summary>
    public float BrushOpacity => (float)BrushOpacitySlider.Value;

    private void OnBrushChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;

        ShowBrushValues();
        BrushChanged?.Invoke();
    }

    /// <summary>Doppelklick stellt einen Regler zurueck - wie ueberall sonst auch.</summary>
    private void OnBrushReset(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not string tag) return;
        if (!double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out double back))
            return;

        slider.Value = back;
        e.Handled = true;
    }

    /// <summary>
    /// Setzt Groesse und Haerte von aussen - wenn sie am Bild mit Strg gezogen wurden.
    /// Die Regler ziehen mit, als haette man an ihnen gedreht.
    /// </summary>
    public void SetBrush(float radius, float hardness)
    {
        BrushSizeSlider.Value = Math.Clamp(radius * 2, BrushSizeSlider.Minimum, BrushSizeSlider.Maximum);
        BrushHardnessSlider.Value = Math.Clamp(hardness, BrushHardnessSlider.Minimum, BrushHardnessSlider.Maximum);
    }

    private void ShowBrushValues()
    {
        BrushSizeValue.Text = $"{BrushSizeSlider.Value:0}";
        BrushHardnessValue.Text = $"{BrushHardnessSlider.Value:0.00}";
        BrushFlowValue.Text = $"{BrushFlowSlider.Value:0.00}";
        BrushOpacityValue.Text = $"{BrushOpacitySlider.Value:0.00}";
    }

    private float? _depth;

    /// <summary>Welches Werkzeug gilt - danach richtet sich, was hier steht.</summary>
    /// <param name="nodes">Im Knotenmodus waehlt "Auswaehlen" fuer einen Knoten und legt keine Ebene an.</param>
    public void Show(AtelierTool tool, bool nodes = false)
    {
        ToolName.Text = Strings.T(tool switch
        {
            AtelierTool.Select => "S_ToolSelect",
            AtelierTool.Crop => "S_ToolCrop",
            AtelierTool.Hand => "S_ToolHand",
            AtelierTool.Pick => "S_ToolPick",
            AtelierTool.Brush => "S_ToolBrush",
            AtelierTool.Nodes => "S_ToolNodes",
            _ => "S_ToolMove",
        });

        ToolHint.Text = Strings.T(tool switch
        {
            AtelierTool.Select => nodes ? "S_ToolSelectNodesShort" : "S_ToolSelectShort",
            AtelierTool.Crop => "S_ToolCropShort",
            AtelierTool.Hand => "S_ToolHandShort",
            AtelierTool.Pick => "S_ToolPickShort",
            AtelierTool.Brush => "S_ToolBrushShort",
            AtelierTool.Nodes => "S_ToolNodesShort",
            _ => "S_ToolMoveShort",
        });

        // Die Leiste kuerzt den Satz, wenn der Platz nicht reicht - ganz steht er im
        // Hinweis. Ein Satz, der mitten im Wort aufhoert und nirgends vollstaendig zu
        // lesen ist, waere schlimmer als keiner.
        ToolHint.ToolTip = ToolHint.Text;

        PickBody.Visibility = tool == AtelierTool.Pick ? Visibility.Visible : Visibility.Collapsed;

        BrushBody.Visibility = tool == AtelierTool.Brush ? Visibility.Visible : Visibility.Collapsed;

        if (tool == AtelierTool.Brush) ShowBrushValues();

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
