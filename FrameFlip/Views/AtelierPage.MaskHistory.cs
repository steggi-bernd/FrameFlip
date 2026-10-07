using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Atelier;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

namespace FrameFlip.Views;

/// <summary>
/// Der Verlauf der gemalten Masken im Knotenmodus (docs/Projekte-und-Masken.md, Punkt 10):
/// jeder Strich wird beim Loslassen aufgezeichnet, ein neuer Stand entsteht, sobald sich ein
/// Zehntel der Flaeche geaendert hat, und "Maskenverlauf ..." im Menue einer Maske stellt
/// einen frueheren Stand wieder her - als ein Schritt, den Strg+Z zuruecknimmt.
///
/// Was ein Stand ist und wie er gespeichert wird, steht in <see cref="MaskHistory"/>, wo er
/// liegt in <see cref="MaskHistoryKeeper"/>. Hier steht nur, wann die Seite fragt.
/// </summary>
public partial class AtelierPage
{
    private MaskHistoryKeeper _maskHistories = null!;

    /// <summary>Das zuletzt geoeffnete Fenster des Maskenverlaufs - fuer die Probe.</summary>
    internal MaskHistoryPopup? MaskHistoryMenu { get; private set; }

    /// <summary>
    /// Der Pinsel liegt auf einer Maske: Ihr Verlauf beginnt jetzt zu beobachten - vor dem
    /// ersten Strich, damit er den Stand davor kennt.
    /// </summary>
    private void WatchMask(MaskNode node, PaintedMask paint) => _maskHistories.Watch(node.Mask, _number, paint);

    /// <summary>Ein Strich ist zu Ende: in den Verlauf der Maske, auf die er ging.</summary>
    private void RecordMaskStroke()
    {
        if (_frame is null || PaintTarget() is not { } target || target.Mask.PaintFor(_number) is not { } paint) return;

        _maskHistories.Record(target.Mask, _number, paint, Placement.LastStroke);
    }

    /// <summary>Die Staende einer Maske fuer das Fenster, der neueste zuerst - oder null ohne Anstrich.</summary>
    internal IReadOnlyList<MaskHistoryRow>? MaskHistoryRows(MaskNode node, out float pending)
    {
        pending = 0;

        if (_frame is null || node.Mask.PaintFor(_number) is not { } paint) return null;

        var history = _maskHistories.Watch(node.Mask, _number, paint);
        var current = paint.Cover();
        var rows = new List<MaskHistoryRow>();

        for (int i = history.States.Count - 1; i >= 0; i--)
        {
            var state = history.States[i];
            var cover = history.CoverOf(i);

            rows.Add(new MaskHistoryRow(state.Number, state.SavedUtc, state.Changed, Thumb(cover, history.Width, history.Height),
                                        cover.AsSpan().SequenceEqual(current), i == 0));
        }

        pending = history.Pending;
        return rows;
    }

    /// <summary>"Maskenverlauf ...": das Fenster mit den Staenden, an der Stelle, von der das Menue kam.</summary>
    private void ShowMaskHistory(MaskNode node, FrameworkElement anchor)
    {
        if (MaskHistoryRows(node, out float pending) is not { } rows) return;

        var popup = new MaskHistoryPopup(anchor, Strings.T("S_MaskHistoryTitle", NodeTitles.MaskName(node)), rows, pending,
                                         number => RestoreMaskState(node, number));

        MaskHistoryMenu = popup;
        popup.Open();
    }

    /// <summary>
    /// Stellt einen Stand wieder her. Was seit dem letzten Stand dazukam, wird vorher selbst
    /// ein Stand - so geht beim Zurueckgehen nichts verloren. Ein Schritt im Verlauf der
    /// Seite: Strg+Z nimmt es zurueck. False, wenn es den Stand nicht gibt oder die Maske
    /// schon so aussieht.
    /// </summary>
    internal bool RestoreMaskState(MaskNode node, int number)
    {
        if (_graph is null || _frame is null || node.Mask.PaintFor(_number) is not { } paint) return false;

        var history = _maskHistories.Watch(node.Mask, _number, paint);

        if (history.Checkpoint(paint)) _maskHistories.Save(node.Mask, _number, history);

        int index = history.IndexOf(number);
        if (index < 0) return false;

        var cover = history.CoverOf(index);
        if (cover.AsSpan().SequenceEqual(paint.Cover())) return false;

        RememberNodes();

        if (!paint.Replace(cover)) return false;

        history.Restored(paint);

        Placement.MaskChanged();
        NodeView.InvalidateVisual();
        AfterNodeEdit();

        return true;
    }

    /// <summary>Ein kleines Bild einer Deckung: hoechstens 96 Punkte breit, jeder der Schnitt seines Feldes.</summary>
    private static ImageSource Thumb(byte[] cover, int width, int height)
    {
        int w = Math.Max(1, Math.Min(width, 96));
        int h = Math.Max(1, height * w / Math.Max(1, width));
        var pixels = new byte[w * h];

        for (int ty = 0; ty < h; ty++)
        {
            int y0 = ty * height / h, y1 = Math.Max(y0 + 1, (ty + 1) * height / h);

            for (int tx = 0; tx < w; tx++)
            {
                int x0 = tx * width / w, x1 = Math.Max(x0 + 1, (tx + 1) * width / w);
                int sum = 0, count = 0;

                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++, count++)
                        sum += cover[y * width + x];

                pixels[ty * w + tx] = (byte)(sum / Math.Max(1, count));
            }
        }

        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w);
        image.Freeze();

        return image;
    }
}
