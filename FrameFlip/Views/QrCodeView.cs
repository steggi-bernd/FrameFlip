using System.Collections;
using System.Windows;
using System.Windows.Media;
using QRCoder;

// UseWindowsForms zieht System.Drawing implizit ein, und dort heissen diese Typen ebenso.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FrameFlip.Views;

/// <summary>
/// Zeichnet einen QR-Code als Rechtecke, nicht als Bild.
///
/// QRCoder bringt fertige Renderer mit, die alle ueber System.Drawing gehen und ein
/// Pixelbild liefern. Das waere hier zweimal falsch: Ein Bild fester Groesse wird auf
/// einem 150-%-Bildschirm weich, und genau dort liest eine Handykamera schlechter.
/// Aus der Modulmatrix selbst zu zeichnen kostet ein paar Zeilen und ergibt einen
/// Code, der bei jeder Skalierung auf ganze Geraetepixel faellt.
///
/// Die stille Bedingung dabei: ausreichend Rand. Ohne die vier Module ringsherum
/// findet kein Leser den Code, auch wenn er auf dem Bildschirm gut aussieht.
/// </summary>
public sealed class QrCodeView : FrameworkElement
{
    /// <summary>Ruhezone in Modulen. Vier ist das Minimum der Norm.</summary>
    private const int Quiet = 4;

    private static readonly Brush Dark = Brushes.Black;
    private static readonly Brush Light = Brushes.White;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(QrCodeView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnTextChanged));

    private BitArray[]? _modules;

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((QrCodeView)sender).Rebuild((string?)e.NewValue);

    private void Rebuild(string? text)
    {
        _modules = null;

        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                using var generator = new QRCodeGenerator();

                // Fehlerkorrektur M: haelt einen Viertel Verlust aus. Q oder H waeren
                // robuster, machen den Code aber dichter - auf einem Bildschirm, der
                // weder knittert noch verschmutzt, ist das der falsche Tausch.
                using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

                _modules = data.ModuleMatrix.ToArray();
            }
            catch (Exception)
            {
                // Ein Code, der sich nicht erzeugen laesst, ist eine leere Flaeche -
                // kein Absturz im Einstellungsdialog.
                _modules = null;
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;

        // Der weisse Grund gehoert zum Code, nicht zur Gestaltung: Ein dunkles Thema
        // hinter einem QR-Code kehrt den Kontrast um, und viele Leser geben dann auf.
        context.DrawRectangle(Light, null, new Rect(0, 0, side, side));

        if (_modules is not { Length: > 0 }) return;

        int count = _modules.Length + Quiet * 2;

        // Auf ganze Pixel abrunden, sonst liegen die Modulkanten zwischen zwei
        // Geraetepixeln und der Code wird grau statt schwarzweiss.
        double scale = Math.Floor(side / count * DevicePixels()) / DevicePixels();
        if (scale <= 0) return;

        double drawn = scale * count;
        double offset = Math.Floor((side - drawn) / 2);

        for (int y = 0; y < _modules.Length; y++)
        {
            BitArray row = _modules[y];

            // Waagerecht zusammenhaengende Module als ein Rechteck zeichnen. Bei einem
            // Code dieser Groesse spart das rund die Haelfte der Zeichenbefehle und
            // vermeidet Haarrisse zwischen benachbarten Rechtecken.
            int x = 0;

            while (x < row.Length)
            {
                if (!row[x]) { x++; continue; }

                int run = 1;
                while (x + run < row.Length && row[x + run]) run++;

                context.DrawRectangle(Dark, null, new Rect(
                    offset + (x + Quiet) * scale,
                    offset + (y + Quiet) * scale,
                    run * scale,
                    scale));

                x += run;
            }
        }
    }

    private double DevicePixels()
    {
        var source = PresentationSource.FromVisual(this);
        double factor = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        return factor > 0 ? factor : 1.0;
    }
}
