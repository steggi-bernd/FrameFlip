using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameFlip.Imaging.Grading;
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Ebenen mit der Maus umsortieren und mit der Tastatur bedienen.
///
/// Beides fehlte, und beides fehlte auf dieselbe Art: Die Knoepfe darunter konnten
/// alles, was hier gebraucht wird - nur der Weg dorthin war in jedem Fall ein Klick
/// auf einen Knopf am Rand. Eine Liste von Ebenen, in der man nichts ziehen kann,
/// fuehlt sich kaputt an, auch wenn jeder einzelne Knopf funktioniert.
///
/// Die Liste laeuft RUECKWAERTS zum Stapel: oben in der Liste ist oben im Bild, und
/// das ist der hoechste Platz im Stapel. Wer eine Zeile ueber eine andere zieht,
/// meint also einen hoeheren Index. Diese Umkehrung ist schon einmal falsch herum
/// eingebaut worden, als es um das Einruecken in Gruppen ging; deshalb steht sie
/// hier an genau einer Stelle und mit ihrem Namen.
/// </summary>
public partial class LayerPanel
{
    /// <summary>
    /// Die Kennung des eigenen Zugformats.
    ///
    /// Eine Zeichenkette und nicht die Ebene selbst: Was in ein Zugobjekt gelegt
    /// wird, kann Windows ueber Prozessgrenzen schicken wollen und versucht es dann
    /// zu verpacken. Die Ebene bleibt deshalb in einem Feld, und im Zugobjekt steht
    /// nur, dass es sich um eine handelt.
    /// </summary>
    private const string RowFormat = "FrameFlip.LayerRow";

    private ImageLayer? _pressed;
    private ImageLayer? _dragging;
    private Point _pressAt;

    // ------------------------------------------------------------------ Tastatur

    /// <summary>
    /// Die Tasten, die eine Ebenenliste haben muss.
    ///
    /// Sie greifen nur, wenn die Liste den Eingabefokus hat - sonst loeschte die
    /// Rueckschritttaste eine Ebene, waehrend jemand einen Namen tippt.
    /// </summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Die vorhandenen Knopfhandler tun genau das Richtige und pruefen selbst, ob
        // es geht - etwa, dass die unterste Ebene stehen bleibt. Sie hier noch
        // einmal auszuschreiben hiesse, zwei Fassungen derselben Regel zu haben.
        var empty = new RoutedEventArgs();

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                OnRemoveClicked(this, empty);
                break;

            case Key.J when control:
                OnDuplicateClicked(this, empty);
                break;

            // Einruecken und ausruecken statt Gruppieren: Eine Gruppe anlegen ist
            // ein eigener Knopf, und ihn auf dieselbe Taste zu legen hiesse, zwei
            // verschiedene Dinge unter einem Griff zu fuehren.
            case Key.G when control && (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                OnOutdentClicked(this, empty);
                break;

            case Key.G when control:
                OnIndentClicked(this, empty);
                break;

            case Key.Up when (Keyboard.Modifiers & ModifierKeys.Alt) != 0:
                OnUpClicked(this, empty);
                break;

            case Key.Down when (Keyboard.Modifiers & ModifierKeys.Alt) != 0:
                OnDownClicked(this, empty);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- Ziehen

    private void OnRowPressed(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(LayerList);
        _pressed = LayerAt(e.OriginalSource as DependencyObject);
    }

    /// <summary>
    /// Ein Zug faengt erst nach einer Mindeststrecke an.
    ///
    /// Ohne sie waere jeder Klick, bei dem die Hand einen Bildpunkt wackelt, ein
    /// Verschieben - und man verschoebe staendig Ebenen, waehrend man sie nur
    /// auswaehlen wollte.
    /// </summary>
    private void OnRowDragging(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(LayerList);

        if (Math.Abs(now.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragging = _pressed;
        _pressed = null;

        try
        {
            DragDrop.DoDragDrop(LayerList, new DataObject(RowFormat, RowFormat),
                                DragDropEffects.Move);
        }
        finally
        {
            _dragging = null;
            HideDropLine();
        }
    }

    /// <summary>
    /// Zeigt, wo die Zeile landen wuerde.
    ///
    /// Ein Zug ohne Strich ist ein Raten: Zwischen "ueber diese Zeile" und "unter
    /// diese Zeile" liegt ein halber Zeilenabstand, und ohne Anzeige trifft man ihn
    /// nur zufaellig.
    /// </summary>
    private bool ShowDropLine(DragEventArgs e)
    {
        var over = RowAt(e.OriginalSource as DependencyObject);

        if (over is null || _dragging is null || ReferenceEquals(over.Tag, _dragging))
        {
            HideDropLine();
            return false;
        }

        var place = e.GetPosition(LayerList);
        var top = over.TranslatePoint(new Point(0, 0), LayerList);

        bool above = place.Y < top.Y + over.ActualHeight / 2;

        DropLine.Width = LayerList.ActualWidth;
        Canvas.SetLeft(DropLine, 0);
        Canvas.SetTop(DropLine, above ? top.Y : top.Y + over.ActualHeight - 2);

        DropLine.Visibility = Visibility.Visible;
        return true;
    }

    private void HideDropLine() => DropLine.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Legt die gezogene Zeile an ihren neuen Platz.
    ///
    /// Die Umkehrung steht hier: Ueber einer Zeile heisst HINTER ihr im Stapel. Und
    /// eine Gruppe darf nicht in sich selbst wandern - der Stapel waere danach ein
    /// Ring, und das Zusammensetzen liefe nicht mehr zu Ende.
    /// </summary>
    private void DropRow(DragEventArgs e)
    {
        var over = RowAt(e.OriginalSource as DependencyObject);

        if (_dragging is null || over?.Tag is not ImageLayer target ||
            ReferenceEquals(target, _dragging) || Holds(_dragging, target))
        {
            HideDropLine();
            return;
        }

        var from = Owner(_dragging);
        var to = Owner(target);

        if (from is null || to is null)
        {
            HideDropLine();
            return;
        }

        var place = e.GetPosition(LayerList);
        var top = over.TranslatePoint(new Point(0, 0), LayerList);

        if (!Reorder(_dragging, target, place.Y < top.Y + over.ActualHeight / 2))
        {
            HideDropLine();
            return;
        }

        _selected = _dragging;

        HideDropLine();
        Rebuild();
        Editing?.Invoke(EditedLayer);
        Raise(interim: false);
    }

    /// <summary>
    /// Legt eine Ebene vor oder hinter eine andere - die Rechnung ohne die Maus.
    ///
    /// Getrennt vom Zug, weil hier die Umkehrung steht und sie sich nur so pruefen
    /// laesst: Ein Zugereignis laesst sich nicht bauen, eine Reihenfolge schon.
    /// UEBER einer Zeile heisst HINTER ihr im Stapel, weil die Liste rueckwaerts
    /// laeuft - oben in der Liste ist oben im Bild und damit der hoechste Platz.
    /// </summary>
    /// <returns>False, wenn der Zug nichts bewirkt haette.</returns>
    public bool Reorder(ImageLayer moved, ImageLayer target, bool above)
    {
        if (ReferenceEquals(moved, target) || Holds(moved, target)) return false;

        var from = Owner(moved);
        var to = Owner(target);

        if (from is null || to is null) return false;

        from.Remove(moved);

        int at = to.IndexOf(target);
        if (above) at++;

        to.Insert(Math.Clamp(at, 0, to.Count), moved);

        return true;
    }

    /// <summary>Ob die eine Ebene die andere enthaelt - direkt oder tiefer.</summary>
    private static bool Holds(ImageLayer group, ImageLayer layer)
    {
        if (group.Content != LayerContent.Group) return false;

        foreach (var child in group.Children)
        {
            if (ReferenceEquals(child, layer)) return true;
            if (Holds(child, layer)) return true;
        }

        return false;
    }

    /// <summary>Die Zeile unter einem angeklickten Element.</summary>
    private static ListBoxItem? RowAt(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);

        return source as ListBoxItem;
    }

    private static ImageLayer? LayerAt(DependencyObject? source)
        => RowAt(source)?.Tag as ImageLayer;
}
