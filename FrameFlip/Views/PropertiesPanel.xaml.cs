using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging.Grading;
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
    public PropertiesPanel()
    {
        InitializeComponent();

        SizeChanged += (_, _) => ArrangeFor(ActualWidth);

        BrushShapeBox.ItemsSource = new[]
        {
            new IconChoice("round", Strings.T("S_BrushRound")),
            new IconChoice("square", Strings.T("S_BrushSquare")),
        };

        ShowBrushGroups();
    }

    /// <summary>
    /// Schmal - in einer Seitenzone - steht der Name des Werkzeugs ueber den Gruppen statt
    /// links daneben, wo er eine ganze Spalte fraesse.
    /// </summary>
    private void ArrangeFor(double width)
    {
        bool narrow = width > 0 && width < 520;

        DockPanel.SetDock(ToolNamePanel, narrow ? Dock.Top : Dock.Left);
        ToolNamePanel.Margin = narrow ? new Thickness(0, 8, 0, 2) : new Thickness(0, 0, 16, 0);
    }

    /// <summary>Das Werkzeug der Zeile, das der Pinsel gerade ist - Art oder Stempel.</summary>
    public string BrushToolKey => BrushArea switch
    {
        PaintArea.Rectangle => "rectangle",
        PaintArea.Ellipse => "ellipse",
        PaintArea.Lasso => "lasso",
        _ => BrushShape == BrushShape.Stamp ? "stamp" : "brush",
    };

    /// <summary>Jemand moechte die gelesene Entfernung als Scharfstellung.</summary>
    public event Action<float>? FocusWanted;

    /// <summary>Eine Pinseleinstellung hat sich geaendert.</summary>
    public event Action? BrushChanged;

    /// <summary>Der Verlauf der bemalten Maske soll aufgehen - am Knopf, der ihn will.</summary>
    public event Action<FrameworkElement>? BrushHistoryWanted;

    /// <summary>Die bemalte Maske soll bearbeitet werden - fuellen, umkehren, ausweiten ...</summary>
    public event Action<FrameworkElement>? BrushEditWanted;

    /// <summary>
    /// Ob die Knoepfe fuer Verlauf und Bearbeitung der Maske zu sehen sind - nur, wenn der
    /// Pinsel auf einer gemalten Maske im Graphen liegt.
    /// </summary>
    public void ShowBrushHistory(bool shown)
    {
        BrushHistoryButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        BrushEditButton.Visibility = BrushHistoryButton.Visibility;
    }

    /// <summary>Der Knopf fuer die Bearbeitung der Maske - fuer die Probe.</summary>
    internal Button EditButton => BrushEditButton;

    private void OnBrushEdit(object sender, RoutedEventArgs e) => BrushEditWanted?.Invoke(BrushEditButton);

    /// <summary>Der Knopf fuer den Maskenverlauf - fuer die Probe.</summary>
    internal Button HistoryButton => BrushHistoryButton;

    private void OnBrushHistory(object sender, RoutedEventArgs e) => BrushHistoryWanted?.Invoke(BrushHistoryButton);

    /// <summary>Der Pinselradius in Bildpunkten.</summary>
    public float BrushRadius => (float)BrushSizeSlider.Value / 2f;

    /// <summary>Wie hart die Kante ist. 0 ist ein Verlauf, 1 eine Scheibe.</summary>
    public float BrushHardness => (float)BrushHardnessSlider.Value;

    /// <summary>Wie schnell ein Strich auftraegt.</summary>
    public float BrushFlow => (float)BrushFlowSlider.Value;

    /// <summary>Bis wohin ein Strich ueberhaupt auftraegt.</summary>
    public float BrushOpacity => (float)BrushOpacitySlider.Value;

    /// <summary>Der Abstand der Tupfer als Anteil des Radius.</summary>
    public float BrushSpacing => (float)BrushSpacingSlider.Value;

    /// <summary>Eckig, Stempel oder rund.</summary>
    public BrushShape BrushShape
        => BrushStampToggle.IsChecked == true ? BrushShape.Stamp
         : BrushSquareToggle.IsChecked == true ? BrushShape.Square
         : BrushShape.Round;

    /// <summary>Die geladene Stempelspitze - null, solange keine gewaehlt ist.</summary>
    public StampTip? BrushStamp { get; private set; }

    /// <summary>Zufaellige Drehung je Stempeltupfer, 0 bis 1.</summary>
    public float BrushJitter => (float)BrushJitterSlider.Value;

    /// <summary>Streuung der Stempeltupfer, 0 bis 1.</summary>
    public float BrushScatter => (float)BrushScatterSlider.Value;

    /// <summary>Eine Spitze fuer den Stempel - aus der Datei oder fuer die Probe. Schaltet den Stempel ein.</summary>
    internal void UseStamp(StampTip tip, string? name = null)
    {
        BrushStamp = tip;
        BrushTipButton.ToolTip = name is null ? Strings.T("S_BrushTipHint") : $"{name} – {tip.Width} × {tip.Height}";
        BrushStampToggle.IsChecked = true;

        if (IsLoaded) BrushChanged?.Invoke();
    }

    private void OnBrushTipClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("S_BrushTipLoad"),
            Filter = "Bilder|*.png;*.tif;*.tiff;*.bmp;*.jpg;*.jpeg;*.webp|Alle Dateien|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        if (StampTip.Load(dialog.FileName) is { } tip) UseStamp(tip, System.IO.Path.GetFileName(dialog.FileName));
        else Told(Strings.T("S_BrushTipUnreadable"));
    }

    /// <summary>Der Winkel der Spitze in Grad.</summary>
    public float BrushAngle => (float)BrushAngleSlider.Value;

    /// <summary>Breite zu Hoehe der Spitze.</summary>
    public float BrushAspect => (float)BrushAspectSlider.Value;

    /// <summary>Der Winkel folgt dem Strich.</summary>
    public bool BrushFollow => BrushFollowToggle.IsChecked == true;

    /// <summary>Wie weit die eckige Spitze zum Karo gezogen ist, 0 bis 1.</summary>
    public float BrushSquish => (float)BrushSquishSlider.Value;

    /// <summary>Malt der Pinsel Zuege oder zieht er Flaechen auf.</summary>
    public PaintArea BrushArea
        => BrushModeRectangle.IsChecked == true ? PaintArea.Rectangle
         : BrushModeEllipse.IsChecked == true ? PaintArea.Ellipse
         : BrushModeLasso.IsChecked == true ? PaintArea.Lasso
         : PaintArea.None;

    private bool _choosingMode;

    /// <summary>
    /// Der Pinsel aus der Werkzeugleiste: Zug oder Flaeche, und beim Stempel die Spitze.
    /// Ohne geladene Spitze fragt der Stempel nach einer.
    /// </summary>
    public void ChooseBrush(PaintArea area, BrushShape? shape)
    {
        var mode = area switch
        {
            PaintArea.Rectangle => BrushModeRectangle,
            PaintArea.Ellipse => BrushModeEllipse,
            PaintArea.Lasso => BrushModeLasso,
            _ => BrushModeStroke,
        };

        mode.IsChecked = true;

        if (shape == BrushShape.Stamp)
        {
            BrushStampToggle.IsChecked = true;
            if (BrushStamp is null) Told(Strings.T("S_ToolStampNeedsTip"));
        }
        else if (area == PaintArea.None)
        {
            // "Pinsel" ist der Pinsel ohne Stempel - eckig oder rund, wie eingestellt.
            BrushStampToggle.IsChecked = false;
        }

        ShowBrushGroups();
    }

    /// <summary>Genau eine Art ist an: Die gewaehlte schaltet die anderen ab und laesst sich selbst nicht abschalten.</summary>
    private void OnBrushMode(object sender, RoutedEventArgs e)
    {
        // Der erste Knopf steht im XAML auf an - sein Checked kommt beim Laden, bevor die
        // anderen Knoepfe da sind. Dann gibt es noch nichts abzuschalten.
        if (_choosingMode || BrushModeLasso is null) return;

        _choosingMode = true;

        foreach (var mode in new[] { BrushModeStroke, BrushModeRectangle, BrushModeEllipse, BrushModeLasso })
            mode.IsChecked = ReferenceEquals(mode, sender);

        _choosingMode = false;

        ShowBrushGroups();
        if (IsLoaded) BrushChanged?.Invoke();
    }

    /// <summary>
    /// Zeigt nur, was zum gewaehlten Werkzeug gehoert (Phase U3). Rechteck, Ellipse und Lasso
    /// fuellen - Spitze, Staerke, Abstand, Form und Druck haben dort nichts zu sagen. Das Karo
    /// gibt es nur eckig, Spitze, Zufall und Streuung nur beim Stempel, und dann nicht die
    /// Wahl rund oder eckig. Die Toleranz nur mit der Kante.
    /// </summary>
    private void ShowBrushGroups()
    {
        if (BrushModeLasso is null || BrushEdgeRow is null) return;

        bool stroke = BrushArea == PaintArea.None;

        static void Show(UIElement element, bool shown) => element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        Show(BrushTipRow, stroke);
        Show(BrushFlowRow, stroke);
        Show(BrushSpacingRow, stroke);
        Show(BrushShapeRow, stroke);
        Show(BrushPressureRow, stroke);
        Show(BrushStampRow, BrushShape == BrushShape.Stamp);
        Show(BrushTipShapes, BrushShape != BrushShape.Stamp);
        Show(BrushSquishRow, BrushShape == BrushShape.Square);
        Show(BrushEdgeRow, BrushEdge);

        // Rund ist, was nicht eckig ist - der Schalter und das Auswahlfeld zeigen es nur an.
        BrushRoundToggle.IsChecked = BrushSquareToggle.IsChecked != true;

        _showingShape = true;
        BrushShapeBox.SelectedIndex = BrushSquareToggle.IsChecked == true ? 1 : 0;
        _showingShape = false;
    }

    private bool _showingShape;

    /// <summary>Rund oder eckig aus dem Auswahlfeld - es stellt nur die Schalter, die den Stand halten.</summary>
    private void OnBrushShapeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_showingShape || BrushRoundToggle is null || BrushSquareToggle is null) return;

        if (BrushShapeBox.SelectedIndex == 1) BrushSquareToggle.IsChecked = true;
        else BrushRoundToggle.IsChecked = true;
    }

    /// <summary>Der Strich bleibt auf der Flaeche, auf der er ansetzt - aus Tiefe und Normale.</summary>
    public bool BrushEdge => BrushEdgeToggle.IsChecked == true;

    /// <summary>Wie weit die Flaeche vom Ansatz abweichen darf, 0 bis 1.</summary>
    public float BrushEdgeTolerance => (float)BrushEdgeSlider.Value;

    /// <summary>Worauf der Druck eines Stifts wirkt - in der Reihenfolge der Auswahl: aus, Groesse, Staerke, beides.</summary>
    public BrushPressure BrushPressureTo => BrushPressureBox.SelectedIndex switch
    {
        0 => BrushPressure.None,
        2 => BrushPressure.Flow,
        3 => BrushPressure.Size | BrushPressure.Flow,
        _ => BrushPressure.Size,
    };

    private void OnBrushPressureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        BrushChanged?.Invoke();
    }

    /// <summary>Der Strich bleibt auf dem Objekt, auf dem er beginnt.</summary>
    public bool BrushObject => BrushObjectToggle.IsChecked == true;

    private void OnBrushToggle(object sender, RoutedEventArgs e)
    {
        // Rund oder eckig: Rund schaltet eckig ab und laesst sich selbst nicht abschalten -
        // es ist das, was bleibt, wenn eckig aus ist (ShowBrushGroups).
        if (ReferenceEquals(sender, BrushRoundToggle) && BrushRoundToggle.IsChecked == true) BrushSquareToggle.IsChecked = false;

        // Eckig oder Stempel - eine Spitze hat nur eine Form.
        if (ReferenceEquals(sender, BrushSquareToggle) && BrushSquareToggle.IsChecked == true) BrushStampToggle.IsChecked = false;
        if (ReferenceEquals(sender, BrushStampToggle) && BrushStampToggle.IsChecked == true) BrushSquareToggle.IsChecked = false;

        // Objekt oder Flaeche - beides zugleich hiesse zwei Begrenzungen, und der Strich
        // traegt nur eine.
        if (ReferenceEquals(sender, BrushObjectToggle) && BrushObject) BrushEdgeToggle.IsChecked = false;
        if (ReferenceEquals(sender, BrushEdgeToggle) && BrushEdge) BrushObjectToggle.IsChecked = false;

        ShowBrushGroups();

        if (!IsLoaded) return;

        BrushChanged?.Invoke();
    }

    /// <summary>Die Spitze von aussen - wenn am Bild mit Umschalt und Rad gedreht wurde.</summary>
    public void SetBrushAngle(float angle)
        => BrushAngleSlider.Value = Math.Clamp(angle, BrushAngleSlider.Minimum, BrushAngleSlider.Maximum);

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
    /// Setzt Groesse, Haerte und Abstand von aussen - wenn sie am Bild mit Strg gezogen
    /// oder gedreht wurden. Die Regler ziehen mit, als haette man an ihnen gedreht.
    /// </summary>
    public void SetBrush(float radius, float hardness, float spacing)
    {
        BrushSizeSlider.Value = Math.Clamp(radius * 2, BrushSizeSlider.Minimum, BrushSizeSlider.Maximum);
        BrushHardnessSlider.Value = Math.Clamp(hardness, BrushHardnessSlider.Minimum, BrushHardnessSlider.Maximum);
        BrushSpacingSlider.Value = Math.Clamp(spacing, BrushSpacingSlider.Minimum, BrushSpacingSlider.Maximum);
    }

    private void ShowBrushValues()
    {
        BrushSizeValue.Text = $"{BrushSizeSlider.Value:0}";
        BrushHardnessValue.Text = $"{BrushHardnessSlider.Value:0.00}";
        BrushFlowValue.Text = $"{BrushFlowSlider.Value:0.00}";
        BrushOpacityValue.Text = $"{BrushOpacitySlider.Value:0.00}";
        BrushSpacingValue.Text = $"{BrushSpacingSlider.Value * 100:0} %";
        BrushAngleValue.Text = $"{BrushAngleSlider.Value:0}°";
        BrushAspectValue.Text = $"1:{BrushAspectSlider.Value:0.##}";
        BrushSquishValue.Text = $"{BrushSquishSlider.Value * 100:0} %";
        BrushEdgeValue.Text = $"{BrushEdgeSlider.Value * 100:0} %";
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

        // Der Satz steht nicht mehr in der Leiste, sondern im Hinweis am Namen des Werkzeugs -
        // die Leiste war mit ihm ueberladen (Entscheidung 9, Feinschliff).
        ToolName.ToolTip = ToolHint.Text;

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
