using System.Windows.Input;
using System.Windows.Media;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

// WinForms ist mit im Haus, und dort gibt es Point noch einmal. Der Alias sagt,
// welcher gemeint ist, statt es dem naechsten Leser zu ueberlassen.
using Point = System.Windows.Point;

namespace FrameFlip.Views;

/// <summary>
/// Kryptomatten auf der Atelierseite: das Auswaehlen durch Klicken ins Bild.
///
/// Der Ebenenstreifen kann das nicht selbst - er hat kein Bild. Er meldet nur, dass
/// jemand waehlen moechte; hier wird der Klick in einen Bildpunkt umgerechnet, die
/// Kennung aus der untersten Stufe gelesen und der Name dazu im Manifest gesucht.
///
/// Das ist der ganze Trick an der Kryptomatte, und er ist der Grund, warum sie eine
/// Sequenz ueberdauert: Gewaehlt wird kein Bereich, sondern ein OBJEKT. In Bild 300
/// steht dieselbe Kennung an einer anderen Stelle, und die Maske sitzt dort, wo das
/// Objekt inzwischen ist.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Was die Datei an Kryptomatten fuehrt - meist Objekt und Material.</summary>
    private IReadOnlyList<CryptomatteSet> _cryptomattes = Array.Empty<CryptomatteSet>();

    /// <summary>True, solange ein Klick ins Bild eine Auswahl bedeutet.</summary>
    private bool _picking;

    /// <summary>
    /// Der Maskenbereich bittet um eine Auswahl - oder gibt sie zurueck.
    ///
    /// Er bekommt keinen eigenen Zustand mehr, sondern schaltet das WERKZEUG. Damit
    /// gibt es nur noch eine Stelle, an der steht, was ein Klick ins Bild bedeutet,
    /// und sie ist in der Spalte zu sehen.
    /// </summary>
    private void OnPickModeChanged(bool on)
    {
        var tool = on ? AtelierTool.Select : AtelierTool.Move;

        MouseTools.Select(tool);
        OnToolChanged(tool);
    }

    private void OnImageClicked(object sender, MouseButtonEventArgs e)
    {
        // Die Pipette liest nur ab und aendert nichts - deshalb steht sie vor allem
        // anderen und braucht keine Ebene, keine Maske und keinen Stapel.
        if (_tool == AtelierTool.Pick)
        {
            if (PixelAt(e.GetPosition(Display), out int rx, out int ry)) ReadAt(rx, ry);

            e.Handled = true;

            return;
        }

        if (_tool != AtelierTool.Select) return;
        if (!PixelAt(e.GetPosition(Display), out int x, out int y)) return;

        if (PickAt(x, y)) e.Handled = true;
    }

    /// <summary>
    /// Nimmt das Objekt an einem Bildpunkt in die Kryptomatte auf, an der gerade gewaehlt
    /// wird - oder wieder heraus. Im Stapel ist das die Maske der gewaehlten Ebene, im
    /// Knotenmodus der gewaehlte Maskenknoten. False, wenn es dort nichts zu waehlen gibt.
    /// </summary>
    internal bool PickAt(int x, int y)
    {
        var node = InNodes ? NodeView.Selected as MaskNode : null;

        string? level = InNodes
            ? node?.Mask is { Kind: MaskKind.Cryptomatte, Levels.Count: > 0 } mask ? mask.Levels[0] : null
            : Layers.PickLevel;

        if (level is null || !_sources.TryGetValue(level, out var frame)) return false;
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;

        float id = frame.R[y * frame.Width + x];

        // Null heisst: hier steht nichts. Der Hintergrund traegt keine Kennung, und
        // ihn auszuwaehlen ergaebe eine Maske, die nirgends greift.
        if (id == 0f) return false;

        string name = NameOf(id) ?? "";

        if (node is null)
        {
            Layers.AddPick(name, id);
            return true;
        }

        RememberNodes();
        node.Mask.TogglePick(name, id);
        AfterNodeEdit();

        return true;
    }

    /// <summary>Der Name zu einer Kennung, aus dem Manifest der Datei.</summary>
    private string? NameOf(float id)
    {
        foreach (var set in _cryptomattes)
            if (set.NameOf(id) is { } name) return name;

        return null;
    }

    /// <summary>
    /// Rechnet einen Punkt auf dem Bildelement in einen Bildpunkt um.
    ///
    /// Die Rechnung selbst steht in <see cref="ImageHit"/> - sie hat zwei Faelle, die
    /// beide stimmen muessen, und sie laesst sich dort pruefen, ohne ein Fenster
    /// aufzumachen.
    /// </summary>
    private bool PixelAt(Point point, out int x, out int y)
    {
        x = y = 0;

        var frame = _frame;
        if (frame is null) return false;

        return ImageHit.PixelAt(point.X, point.Y,
                                Display.ActualWidth, Display.ActualHeight,
                                frame.Width, frame.Height,
                                Display.Stretch == Stretch.Uniform,
                                out x, out y);
    }
}
