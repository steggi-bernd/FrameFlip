using System.Windows;
using System.Windows.Controls;

// System.Drawing kommt ueber die globalen Usings des Projekts herein und bringt
// ein zweites Size mit. Hier ist immer das von WPF gemeint.
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace FrameFlip.Views;

/// <summary>
/// Kacheln in Zeilen, die die Breite ausfuellen.
///
/// Ein WrapPanel legt Kinder fester Breite von links nebeneinander und bricht um,
/// wenn das naechste nicht mehr passt. Was dann rechts uebrigbleibt, bleibt leer -
/// bei jeder Fensterbreite, und je breiter das Fenster, desto auffaelliger. Der
/// Projektbereich sah dadurch aus, als haette man ihn in eine zu grosse Flaeche
/// gelegt.
///
/// Hier wird stattdessen gerechnet: Wie viele Kacheln der eingestellten Groesse
/// passen nebeneinander? Diese Zahl ist die Spaltenzahl, und der Rest wird auf die
/// Spalten verteilt. Die eingestellte Kachelgroesse ist damit ein Zielwert und keine
/// feste Zahl - was sie fuer ein Raster ohnehin sein muss, wenn kein Loch bleiben
/// soll.
///
/// Die Kinder duerfen deshalb KEINE eigene Breite setzen: Eine gesetzte Breite
/// gewinnt gegen jede Zuteilung, und die Rechnung hier waere umsonst.
/// </summary>
public class FillWrap : Panel
{
    /// <summary>Die angestrebte Breite einer Kachel. Die wirkliche wird daraus errechnet.</summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(FillWrap),
        new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>Der Abstand zwischen zwei Kacheln, waagerecht wie senkrecht.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(FillWrap),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Wie viele Spalten in die Breite passen, und wie breit sie dann sind.</summary>
    private (int Columns, double Width) Raster(double available)
    {
        double ziel = Math.Max(1, ItemWidth);
        double gap = Math.Max(0, Gap);

        // Ohne brauchbare Breite - etwa im ersten Messdurchgang - eine Spalte
        // annehmen. Der naechste Durchgang hat die echte Breite und rechnet neu.
        if (double.IsInfinity(available) || available <= 0) return (1, ziel);

        // +gap auf beiden Seiten, weil zwischen n Kacheln nur n-1 Luecken liegen.
        int spalten = Math.Max(1, (int)Math.Floor((available + gap) / (ziel + gap)));

        return (spalten, Math.Max(1, (available - (spalten - 1) * gap) / spalten));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var (spalten, breite) = Raster(constraint.Width);

        double hoehe = 0, zeile = 0;
        int inZeile = 0;

        foreach (UIElement kind in InternalChildren)
        {
            if (kind.Visibility == Visibility.Collapsed) continue;

            kind.Measure(new Size(breite, double.PositiveInfinity));

            zeile = Math.Max(zeile, kind.DesiredSize.Height);

            if (++inZeile < spalten) continue;

            hoehe += zeile + Gap;
            zeile = 0;
            inZeile = 0;
        }

        if (inZeile > 0) hoehe += zeile + Gap;

        double gesamt = double.IsInfinity(constraint.Width) ? spalten * breite : constraint.Width;

        return new Size(gesamt, Math.Max(0, hoehe - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (spalten, breite) = Raster(finalSize.Width);

        double y = 0, zeile = 0;
        int inZeile = 0;

        foreach (UIElement kind in InternalChildren)
        {
            if (kind.Visibility == Visibility.Collapsed) continue;

            double x = inZeile * (breite + Gap);

            kind.Arrange(new Rect(x, y, breite, kind.DesiredSize.Height));

            zeile = Math.Max(zeile, kind.DesiredSize.Height);

            if (++inZeile < spalten) continue;

            y += zeile + Gap;
            zeile = 0;
            inZeile = 0;
        }

        return finalSize;
    }
}
