using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;
using FrameFlip.Localization;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace FrameFlip.Views;

/// <summary>
/// Die Pipette (docs/Atelier-Arbeitsablauf.md, C4). Vorher las sie die Quelle, nur beim Klick,
/// und konnte ausser der Schaerfeebene nichts mit der Farbe anfangen.
///
/// Jetzt: eine Lupe am Zeiger mit Farbe, HEX, RGB, HSV und den linearen Werten, gelesen aus dem
/// angezeigten Ergebnis - oder, umschaltbar, aus der Quelle. Und ein Klick gibt die Farbe dem
/// Ziel: An einem Farbbereich setzt er den Farbton, mit Umschalt erweitert er die Breite. Die
/// Lupe sagt vorher, wofuer die Pipette gerade liest.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Die Seitenlaenge der Lupe in Bildpunkten - ungerade, damit es eine Mitte gibt.</summary>
    private const int LoupeSize = 11;

    private readonly Rectangle[] _loupeCells = new Rectangle[LoupeSize * LoupeSize];

    private void SetUpPipette()
    {
        for (int i = 0; i < _loupeCells.Length; i++)
        {
            _loupeCells[i] = new Rectangle();

            // Die Mitte umrandet: Das ist der Punkt, den ein Klick nimmt.
            if (i == _loupeCells.Length / 2)
            {
                _loupeCells[i].StrokeThickness = 1;
                _loupeCells[i].SetResourceReference(Shape.StrokeProperty, "ForegroundBrush");
            }

            LoupeGrid.Children.Add(_loupeCells[i]);
        }
    }

    /// <summary>Die Farbe an einem Bildpunkt, wie die Pipette sie liest: angezeigt oder aus der Quelle, als Bytes.</summary>
    private (byte R, byte G, byte B)? ShownAt(int x, int y)
    {
        var frame = _frame;
        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return null;

        // Liegt eine Sichthilfe ueber dem Bild, liest die Pipette das Bild darunter (W2c).
        if (!Properties.PickSource && PlainUnderAid is { } plain && _surface is { } shown &&
            x < shown.PixelWidth && y < shown.PixelHeight)
        {
            int at = y * shown.BackBufferStride + x * 4;
            return (plain[at + 2], plain[at + 1], plain[at]);
        }

        if (!Properties.PickSource && _surface is { } surface && x < surface.PixelWidth && y < surface.PixelHeight)
        {
            var pixel = new byte[4];
            surface.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return (pixel[2], pixel[1], pixel[0]);
        }

        int i = y * frame.Width + x;

        static byte Encode(float value) => (byte)MathF.Round(Math.Clamp(Srgb.Encode(value), 0f, 1f) * 255f);

        return (Encode(frame.R[i]), Encode(frame.G[i]), Encode(frame.B[i]));
    }

    /// <summary>Die linearen Werte der Quelle an einem Bildpunkt.</summary>
    private (float R, float G, float B)? LinearAt(int x, int y)
    {
        var frame = _frame;
        if (frame is null || x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return null;

        int i = y * frame.Width + x;
        return (frame.R[i], frame.G[i], frame.B[i]);
    }

    /// <summary>
    /// Der Farbbereich, dem die Pipette ihre Farbe gibt - die Maske des Ziels, wenn sie einer ist,
    /// und wie sie heisst. Null: Die Pipette liest nur ab.
    /// </summary>
    private (LayerMask Mask, string Name)? ColourTarget()
    {
        if (InNodes)
            return SelectedNode is MaskNode { Mask.Kind: MaskKind.Colour } node ? (node.Mask, NodeTitles.MaskName(node)) : null;

        return Layers.Selection is { Mask.Kind: MaskKind.Colour } layer ? (layer.Mask, layer.Name) : null;
    }

    /// <summary>
    /// Die Lupe am Zeiger: elf mal elf Punkte um den Bildpunkt, seine Farbe und Werte, und wofuer
    /// die Pipette liest. Getrennt von der Maus, damit die Probe denselben Weg gehen kann.
    /// </summary>
    internal bool PipetteAt(int x, int y, Point at)
    {
        if (_tool != AtelierTool.Pick || ShownAt(x, y) is not var (r, g, b) || LinearAt(x, y) is not var (lr, lg, lb))
        {
            HidePipette();
            return false;
        }

        int half = LoupeSize / 2;

        for (int dy = -half; dy <= half; dy++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                var cell = _loupeCells[(dy + half) * LoupeSize + dx + half];
                cell.Fill = ShownAt(x + dx, y + dy) is var (cr, cg, cb)
                    ? new SolidColorBrush(Color.FromRgb(cr, cg, cb))
                    : Brushes.Transparent;
            }
        }

        var (hue, saturation, value) = ColourReadout.Hsv(r, g, b);

        LoupeSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        LoupeHex.Text = ColourReadout.Hex(r, g, b);
        LoupeValues.Text = $"RGB {r} {g} {b}\nHSV {hue}° {saturation}% {value}%\nlin {lr:0.###} {lg:0.###} {lb:0.###}";
        LoupeFor.Text = ColourPickPurpose()
                        ?? (ColourTarget() is var (_, name) ? Strings.T("S_PickForColourRange", name) : Strings.T("S_PickReadOnly"));

        PickLoupe.Visibility = Visibility.Visible;

        PickLoupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = Math.Min(at.X + 18, Math.Max(0, CryptoHoverLayer.ActualWidth - PickLoupe.DesiredSize.Width - 4));
        double top = Math.Min(at.Y + 18, Math.Max(0, CryptoHoverLayer.ActualHeight - PickLoupe.DesiredSize.Height - 4));
        Canvas.SetLeft(PickLoupe, left);
        Canvas.SetTop(PickLoupe, top);

        return true;
    }

    /// <summary>Was die Lupe gerade zeigt - fuer die Probe.</summary>
    internal string? LoupeText => PickLoupe.Visibility == Visibility.Visible ? LoupeHex.Text + " | " + LoupeFor.Text : null;

    private void HidePipette() => PickLoupe.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Ein Klick mit der Pipette: die Werte in die Werkzeugeinstellungen - und an einem Farbbereich
    /// der Farbton in die Maske. Mit Umschalt wird der Bereich so weit, dass die Farbe dazugehoert.
    /// </summary>
    private void ReadAt(int x, int y)
    {
        if (ShownAt(x, y) is not var (r, g, b) || LinearAt(x, y) is not var (lr, lg, lb)) return;

        string label = (string)(TryFindResource("S_PickReadout") ?? "read");

        PickText.Text = $"{label}  {x},{y}   {r}/{g}/{b}   {lr:0.###} {lg:0.###} {lb:0.###}";
        PickText.Visibility = Visibility.Visible;

        Properties.Read(x, y, r, g, b, lr, lg, lb, DepthAt(y * _frame!.Width + x));
        RememberColour(x, y);

        PickColourRange(lr, lg, lb, widen: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    /// <summary>
    /// Gibt einem Farbbereich im Ziel den Farbton einer Farbe - aus den linearen Werten der Quelle,
    /// denn die Maske misst den Farbton dort. Grau hat keinen; dann bleibt alles, wie es ist.
    /// </summary>
    internal bool PickColourRange(float r, float g, float b, bool widen)
    {
        if (ColourTarget() is not var (mask, _) || ColourReadout.Hue(r, g, b) is not { } hue) return false;

        float newHue = widen ? mask.Hue : hue;
        float spread = widen ? ColourReadout.Widen(mask.Hue, mask.Spread, hue) : mask.Spread;

        if (InNodes)
        {
            RememberNodes();
            mask.Hue = newHue;
            mask.Spread = spread;
            AfterNodeEdit();
            return true;
        }

        return Layers.SetColourRange(newHue, spread);
    }
}
