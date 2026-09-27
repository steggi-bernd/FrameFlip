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
            // Eine Pipette des Tonwerts wartet: Der Ton hier wird ihr Punkt (W2b).
            if (PixelAt(e.GetPosition(Display), out int rx, out int ry))
            {
                if (_levelsPick is not null) LevelsPickAt(rx, ry);
                else ReadAt(rx, ry);
            }

            e.Handled = true;

            return;
        }

        if (_tool != AtelierTool.Select) return;
        if (!PixelAt(e.GetPosition(Display), out int x, out int y)) return;

        // Der Chip "Objektmaske" wartet auf ein Objekt: Es kommt in die Maske der Ebene.
        if (_objectPick is not null)
        {
            ObjectMaskAt(x, y);
            e.Handled = true;
            return;
        }

        var mode = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? PickMode.Add
                 : (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? PickMode.Remove
                 : PickMode.Replace;

        if (PickAt(x, y, mode))
        {
            e.Handled = true;
            ShowCryptoView();
        }
    }

    /// <summary>Was ein Klick mit "Auswaehlen" mit dem Objekt macht - nach den Tasten dabei.</summary>
    internal enum PickMode
    {
        /// <summary>Ohne Taste: Das Objekt ersetzt die Auswahl. War es als einziges gewaehlt, geht es heraus.</summary>
        Replace,

        /// <summary>Umschalt: dazu.</summary>
        Add,

        /// <summary>Alt: heraus.</summary>
        Remove,
    }

    /// <summary>
    /// Waehlt das Objekt an einem Bildpunkt (C3b). Gewaehlt wird an der Kryptomatte des
    /// Bearbeitungsziels - im Stapel der Maske der gewaehlten Ebene, im Knotenmodus dem gewaehlten
    /// Maskenknoten. Hat das Ziel keine, entsteht eine vorlaeufige Auswahl an der Objekt-
    /// Kryptomatte, statt dass der Klick still nichts tut; eine Aktion macht daraus eine
    /// Maskenebene. False, wenn es dort nichts zu waehlen gibt.
    /// </summary>
    internal bool PickAt(int x, int y, PickMode mode = PickMode.Replace)
    {
        var target = CryptoTarget();
        var set = target?.Set ?? ObjectSet();

        if (set is null || LevelsOf(set) is not { } levels) return false;

        var frame = levels[0];
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;

        float id = frame.R[y * frame.Width + x];
        bool provisional = target is null || _recipe.Target is Atelier.EditingTarget.CryptoSelection;

        // Null heisst: hier steht nichts. Der Hintergrund traegt keine Kennung, und ihn
        // auszuwaehlen ergaebe eine Maske, die nirgends greift. Eine vorlaeufige Auswahl leert
        // ein einfacher Klick dorthin - eine Maske bleibt, wie sie ist.
        if (id == 0f)
        {
            if (mode != PickMode.Replace || _recipe.Target is not Atelier.EditingTarget.CryptoSelection) return false;

            _recipe.Focus(Atelier.EditingTarget.Picture);
            return true;
        }

        string name = set.NameOf(id) ?? NameOf(id) ?? "";
        var picks = Picked(target?.Mask.Picks ?? new List<CryptoPick>(), name, id, mode);

        if (!provisional && InNodes && SelectedNode is MaskNode node)
        {
            RememberNodes();
            node.Mask.Picks = picks;
            AfterNodeEdit();
        }
        else if (!provisional && !InNodes)
        {
            Layers.SetPicks(picks);
        }
        else
        {
            _recipe.Focus(picks.Count == 0
                ? Atelier.EditingTarget.Picture
                : new Atelier.EditingTarget.CryptoSelection(set.Prefix, picks));
        }

        return true;
    }

    /// <summary>Die Auswahl nach einem Klick auf ein Objekt - siehe <see cref="PickMode"/>.</summary>
    private static List<CryptoPick> Picked(IReadOnlyList<CryptoPick> current, string name, float id, PickMode mode)
    {
        bool had = current.Any(p => p.Id == id);

        return mode switch
        {
            PickMode.Add => had ? current.ToList() : current.Append(new CryptoPick { Name = name, Id = id }).ToList(),
            PickMode.Remove => current.Where(p => p.Id != id).ToList(),
            _ => had && current.Count == 1 ? new List<CryptoPick>() : new List<CryptoPick> { new() { Name = name, Id = id } },
        };
    }

    /// <summary>
    /// Macht aus der vorlaeufigen Auswahl eine Maskenebene: eine Einstellungsebene mit einer
    /// Kryptomatte, die genau diese Objekte waehlt, benannt nach ihnen - dieselbe Art Ebene wie
    /// "Objekt als Maske". Danach ist sie das Ziel. False, wenn keine Auswahl das Ziel ist.
    /// </summary>
    private bool MaterialiseSelection()
    {
        if (_recipe.Target is not Atelier.EditingTarget.CryptoSelection selection) return false;

        var mask = new LayerMask
        {
            Kind = MaskKind.Cryptomatte,
            Source = selection.Set,
            Levels = Cryptomatte.Levels(_passes, selection.Set).ToList(),
            Picks = selection.Picks.Select(p => p.Clone()).ToList(),
        };

        string name = string.Join(", ", selection.Picks.Select(p => p.Name.Length > 0 ? p.Name : "?"));

        if (InNodes) return AddNodeMaskLayer(mask, name) is not null;

        Layers.AddAdjustment();
        if (Layers.Selection is not { } made) return false;

        made.Name = name;
        made.Mask = mask;
        Layers.SetPicks(mask.Picks);

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
