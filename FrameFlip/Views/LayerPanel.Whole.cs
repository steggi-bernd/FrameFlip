using System.Windows;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace FrameFlip.Views;

/// <summary>
/// Versuch (docs/Atelier-UX-Versuch.md): die Zeile "Gesamtbild" ueber der Liste. Gewaehlt heisst
/// sie: keine Ebene - die Regler gelten dem ganzen Bild, wie heute, wenn nichts markiert ist.
/// Nur war "nichts markiert" bisher kaum herzustellen.
/// </summary>
public partial class LayerPanel
{
    /// <summary>Ob gerade das Gesamtbild gewaehlt ist - fuer die Probe.</summary>
    internal bool WholeChosen => _selected is null;

    private void OnWholeClicked(object sender, RoutedEventArgs e) => SelectWhole();

    /// <summary>Keine Ebene gewaehlt: das Gesamtbild ist das Ziel.</summary>
    public void SelectWhole()
    {
        _selected = null;
        _filling = true;

        try
        {
            LayerList.SelectedItems.Clear();
        }
        finally
        {
            _filling = false;
        }

        OnLayer = false;
        PushToControls();
        UpdateButtons();
        ShowWhole();
        Editing?.Invoke(EditedLayer);
    }

    /// <summary>Die Zeile "Gesamtbild" hervorgehoben, solange sie gilt.</summary>
    private void ShowWhole()
    {
        bool whole = _selected is null;

        WholeRow.Background = whole
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x2F, 0x58))
            : (Brush)FindResource("ControlBrush");
        WholeRow.BorderBrush = whole ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("ControlBorderBrush");
    }

    /// <summary>Der Maskenabschnitt offen - in den Einstellungen soll man ihn nicht suchen muessen.</summary>
    internal void OpenMaskSection()
    {
        MaskBody.Visibility = Visibility.Visible;
        MaskFoldButton.Content = "−";
    }
}
