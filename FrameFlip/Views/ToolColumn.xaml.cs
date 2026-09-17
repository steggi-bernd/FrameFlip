using System.Windows;
using System.Windows.Controls;

namespace FrameFlip.Views;

/// <summary>
/// Die Werkzeugspalte: fuenf Knoepfe, von denen genau einer gilt.
///
/// Sie weiss nichts davon, was die Werkzeuge tun - sie sagt nur, welches gewaehlt
/// ist. Das ist Absicht: Die Spalte gehoert der Oberflaeche, die Wirkung gehoert der
/// Atelierseite, und wer beides in ein Bedienelement legt, kann das eine nicht mehr
/// ohne das andere pruefen.
/// </summary>
public partial class ToolColumn : UserControl
{
    public ToolColumn() => InitializeComponent();

    /// <summary>Das gewaehlte Werkzeug hat gewechselt.</summary>
    public event Action<AtelierTool>? ToolChanged;

    /// <summary>Was gerade gilt.</summary>
    public AtelierTool Tool { get; private set; } = AtelierTool.Move;

    /// <summary>
    /// Waehlt von aussen - ohne die Meldung wieder auszuloesen.
    ///
    /// Gebraucht, weil es zwei Wege zu demselben Zustand gibt: der Knopf hier und der
    /// Maskenbereich, der um eine Kryptomatte bittet. Ohne die Sperre riefen sie sich
    /// gegenseitig auf, bis der Stapel voll ist.
    /// </summary>
    public void Select(AtelierTool tool)
    {
        if (Tool == tool) return;

        _quiet = true;

        try
        {
            ButtonFor(tool).IsChecked = true;
            Tool = tool;
        }
        finally
        {
            _quiet = false;
        }
    }

    private bool _quiet;

    private RadioButton ButtonFor(AtelierTool tool) => tool switch
    {
        AtelierTool.Select => SelectTool,
        AtelierTool.Crop => CropTool,
        AtelierTool.Hand => HandTool,
        AtelierTool.Brush => BrushTool,
        AtelierTool.Pick => PickTool,
        _ => MoveTool,
    };

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_quiet || sender is not RadioButton button) return;
        if (!Enum.TryParse((string)button.Tag, out AtelierTool tool)) return;

        Tool = tool;
        ToolChanged?.Invoke(tool);
    }
}
