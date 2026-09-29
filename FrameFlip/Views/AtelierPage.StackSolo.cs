using System.Windows;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Isolieren im Stapel (docs/Atelier-Arbeitsablauf.md, C6b). Im Knotenmodus zeigt der Betrachter
/// eine Ebene allein; im Stapel gibt es keinen, deshalb rechnet der Composer sie selbst heraus:
/// bis zu ihr wie sonst, dann das, was sie beitraegt - oder ihre Maske, grau oder als roter
/// Schleier ueber dem ganzen Bild.
///
/// Nur die Anzeige: Das zusammengesetzte Bild bleibt, wie es ist - Pipetten, Messung und Export
/// sehen weiter das fertige Bild, und im Rezept aendert sich nichts. Esc, ein zweiter Alt+Klick,
/// ein anderes Bild oder der Knotenmodus beenden es.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Welche Ebene allein gezeigt wird - und wie. Null: keine.</summary>
    private LayerSolo? _stackSolo;

    /// <summary>Das Bild der isolierten Ebene - nur fuer die Anzeige.</summary>
    private FloatFrame? _soloFrame;

    /// <summary>Was isoliert ist - fuer die Probe.</summary>
    internal LayerSolo? StackSolo => _stackSolo;

    /// <summary>
    /// Eine Ebene allein zeigen - oder ihre Maske. Dasselbe noch einmal beendet es; etwas anderes
    /// wechselt. Falsch im Knotenmodus - dort isoliert der Betrachter.
    /// </summary>
    internal bool IsolateStack(ImageLayer layer, SoloView view)
    {
        if (InNodes) return false;

        var wanted = new LayerSolo(layer, view);

        if (_stackSolo is { } current && ReferenceEquals(current.Layer, layer) && current.View == view)
        {
            EndStackSolo(render: true);
            return true;
        }

        _stackSolo = wanted;

        // Ein neues Bild fuer die Anzeige: Die Maske grau rechnet anders als die Ebene selbst.
        _soloFrame = null;

        Draw(recompose: true);
        ShowViewer();
        return true;
    }

    /// <summary>Beendet das Isolieren - das Bild ist wieder das ganze.</summary>
    private void EndStackSolo(bool render)
    {
        if (_stackSolo is null) return;

        _stackSolo = null;
        _soloFrame = null;

        if (render && _frame is not null) Draw(recompose: false);
        ShowViewer();
    }

    /// <summary>Rechnet die isolierte Ebene - nach dem Zusammensetzen, auf demselben Gitter.</summary>
    private void ComposeSolo()
    {
        if (_stackSolo is not { } solo || InNodes)
        {
            _soloFrame = null;
            return;
        }

        // Steht die Ebene nicht mehr im Bild - geloescht oder ausgeblendet -, endet das Isolieren.
        _soloFrame = LayerComposer.Compose(Layers.Stack, _sources, _soloFrame, _coarse ? CoarseStep : 1, _number, solo);

        if (_soloFrame is null)
        {
            _stackSolo = null;
            Dispatcher.BeginInvoke(ShowViewer);
        }
    }

    /// <summary>Das Schild ueber dem Bild fuer eine isolierte Ebene. Falsch, wenn keine isoliert ist.</summary>
    private bool ShowSoloBadge()
    {
        if (_stackSolo is not { } solo) return false;

        string name = solo.Layer.Name.Length > 0 ? solo.Layer.Name : System.IO.Path.GetFileName(solo.Layer.Source);

        ViewerText.Text = Strings.T(solo.View switch
        {
            SoloView.Mask => "S_IsolatedMask",
            SoloView.Veil => "S_IsolatedVeil",
            _ => "S_IsolatedLayer",
        }, name);

        ViewerBadge.Visibility = Visibility.Visible;
        return true;
    }
}
