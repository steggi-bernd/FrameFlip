using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Localization;

using Color = System.Windows.Media.Color;

namespace FrameFlip.Views;

/// <summary>
/// Sichthilfen und der Kurvenpunkt aus dem Bild (docs/Atelier-Werkzeugplan.md, W2c).
///
/// Clipping und Falschfarben liegen ueber dem fertigen Anzeigebild - nur auf dem Schirm, nie im
/// Export, und die Pipette liest weiter das Bild darunter. Strg+Klick ins Bild setzt einen Punkt
/// auf die Kurve beim Ton dieser Stelle, so wie er bei den Kurven ankommt.
/// </summary>
public partial class AtelierPage
{
    private ViewAid _viewAid = ViewAid.None;

    /// <summary>Das Anzeigebild ohne Sichthilfe - fuer die Pipette, solange eine darueber liegt.</summary>
    private byte[]? _plainPixels;

    /// <summary>Die Grenzen des Abschneidens: darunter blau, darueber rot. Knapp innerhalb von 0 und 255.</summary>
    private float _clipLow = 0.005f, _clipHigh = 0.995f;

    /// <summary>Was gerade ueber dem Bild liegt - fuer die Probe.</summary>
    internal ViewAid ViewAidShown => _viewAid;

    private void SetUpViewAids()
    {
        ClipRange.Scale = RangeScale.Window;
        ClipRange.Window = new RangeWindow(_clipLow, _clipHigh, 0f, 0f);
        ClipRange.Changed += (window, _) => SetClipping(window.Low, window.High);
        ClipRange.ResetWanted += () => SetClipping(0.005f, 0.995f);

        // Die Legende der Falschfarben: nur die farbigen Zonen, die grauen erklaeren sich selbst.
        foreach (var zone in ViewAids.Zones.Where(z => !z.Grey))
        {
            FalseColourRow.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(6, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(zone.R, zone.G, zone.B)),
            });

            var label = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.TextProperty, zone.Key);
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            FalseColourRow.Children.Add(label);
        }

        ShowViewAid();
    }

    private void OnViewAidClicked(object sender, RoutedEventArgs e) => NextViewAid();

    /// <summary>Die naechste Sichthilfe: Normal, Clipping, Falschfarben, und wieder Normal.</summary>
    internal void NextViewAid() => SetViewAid(_viewAid switch
    {
        ViewAid.None => ViewAid.Clipping,
        ViewAid.Clipping => ViewAid.FalseColour,
        _ => ViewAid.None,
    });

    internal void SetViewAid(ViewAid aid)
    {
        _viewAid = aid;

        if (aid == ViewAid.None) _plainPixels = null;

        ShowViewAid();
        if (_frame is not null) Render();
    }

    /// <summary>Die Grenzen des Abschneidens - vom Bereichsregler in der Statuszeile.</summary>
    internal void SetClipping(float low, float high)
    {
        _clipLow = Math.Clamp(low, 0f, 1f);
        _clipHigh = Math.Clamp(high, _clipLow, 1f);

        ClipRange.Window = new RangeWindow(_clipLow, _clipHigh, 0f, 0f);
        ShowViewAid();

        if (_frame is not null && _viewAid == ViewAid.Clipping) Render();
    }

    private void ShowViewAid()
    {
        ViewAidText.SetResourceReference(TextBlock.TextProperty, _viewAid switch
        {
            ViewAid.Clipping => "S_ViewAidClipping",
            ViewAid.FalseColour => "S_ViewAidFalseColour",
            _ => "S_ViewAidNone",
        });

        ClipRow.Visibility = _viewAid == ViewAid.Clipping ? Visibility.Visible : Visibility.Collapsed;
        FalseColourRow.Visibility = _viewAid == ViewAid.FalseColour ? Visibility.Visible : Visibility.Collapsed;
        ClipValue.Text = $"{MathF.Round(_clipLow * 255f):0} – {MathF.Round(_clipHigh * 255f):0}";
    }

    /// <summary>
    /// Legt die Sichthilfe ueber das gerade gezeichnete Bild - vorher wird es fuer die Pipette
    /// beiseitegelegt. Beim Vergleich mit dem Original liegt nichts darueber.
    /// </summary>
    private void ApplyViewAid()
    {
        if (_viewAid == ViewAid.None || _showingOriginal || _surface is not { } surface) return;

        int length = surface.BackBufferStride * surface.PixelHeight;
        if (_plainPixels is null || _plainPixels.Length != length) _plainPixels = new byte[length];

        Marshal.Copy(surface.BackBuffer, _plainPixels, 0, length);

        ViewAids.Apply(surface.BackBuffer, surface.PixelWidth, surface.PixelHeight, surface.BackBufferStride,
                       _viewAid, _clipLow, _clipHigh);
    }

    /// <summary>
    /// Strg+Klick ins Bild: ein Punkt auf der gezeigten Kurve beim Ton dieser Stelle - so, wie er
    /// bei den Kurven ankommt, dieselbe Frage wie bei den Pipetten des Tonwerts. Falsch, wenn die
    /// Karte der Kurven nicht zu sehen ist.
    /// </summary>
    internal bool CurvePointAt(int x, int y)
    {
        if (!Tools.CurvesShown || LevelsInputAt(Tools.Curves, x, y) is not var (r, g, b)) return false;

        return Tools.AddCurvePoint(r, g, b);
    }
}
