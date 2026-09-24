using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Das Kurvenfeld wirklich zeichnen lassen.
///
/// Die uebrigen Fensterpruefungen legen das Fenster an und lesen damit die XAML -
/// das faengt einen fehlenden Stil oder einen falschen TargetType. Was es NICHT
/// faengt, ist OnRender: Der Code laeuft erst, wenn tatsaechlich gezeichnet wird,
/// also beim Oeffnen des Panels. Ein Fehler dort - eine Division durch null bei
/// einem Feld ohne Groesse, ein leeres Feld im Hintergrund - faellt sonst erst dem
/// Anwender auf.
/// </summary>
public static class CurveRenderInvariants
{
    public static void Run()
    {
        Check.Group("Kurvenfeld zeichnen");

        Draw("Grundstellung", new ToneCurve(), background: null, 240, 180);

        Draw("mit Stuetzpunkten", new ToneCurve(new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.25f, 0.18f),
            new CurvePoint(0.5f, 0.5f), new CurvePoint(0.75f, 0.82f), new CurvePoint(1, 1),
        }), background: null, 240, 180);

        // Mit Verteilung im Hintergrund - der Pfad, der die Wurzelskalierung nimmt.
        var histogram = new int[256];
        for (int i = 0; i < 256; i++) histogram[i] = i * i;
        Draw("mit Verteilung", new ToneCurve(), histogram, 240, 180);

        // Eine leere Verteilung darf nicht durch null teilen.
        Draw("mit leerer Verteilung", new ToneCurve(), new int[256], 240, 180);

        // Ein Feld ohne Groesse kommt beim ersten Aufbau vor, bevor das Layout steht.
        Draw("ohne Groesse", new ToneCurve(), null, 0, 0);
        Draw("ein Pixel breit", new ToneCurve(), null, 1, 1);

        // Sehr schmal und sehr hoch - die Verhaeltnisse, bei denen eine
        // Umrechnung leicht ueberlaeuft.
        Draw("sehr schmal", new ToneCurve(), null, 3, 400);

        // Eine Kurve mit Punkten dicht beieinander, wie sie beim Ziehen entsteht.
        Draw("mit engen Punkten", new ToneCurve(new[]
        {
            new CurvePoint(0, 0), new CurvePoint(0.50f, 0.3f),
            new CurvePoint(0.51f, 0.9f), new CurvePoint(1, 1),
        }), null, 240, 180);
    }

    private static void Draw(string what, ToneCurve curve, int[]? background, int width, int height)
    {
        try
        {
            var editor = new CurveEditor
            {
                Curve = curve,
                Background = background,
                Width = width,
                Height = height,
            };

            // Messen und anordnen, sonst bleibt ActualWidth null und OnRender kehrt
            // sofort zurueck - der Test praefte dann nichts.
            editor.Measure(new Size(width, height));
            editor.Arrange(new Rect(0, 0, width, height));
            editor.UpdateLayout();

            if (width >= 1 && height >= 1)
            {
                var target = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height),
                                                    96, 96, PixelFormats.Pbgra32);
                target.Render(editor);

                Check.That(target.PixelWidth == width, $"{what}: gezeichnet", $"{target.PixelWidth}px");
            }
            else
            {
                // Ohne Groesse laesst sich keine Bitmap anlegen; hier zaehlt nur,
                // dass das Anordnen selbst nichts wirft.
                Check.That(true, $"{what}: ueberstanden");
            }
        }
        catch (Exception ex)
        {
            Check.That(false, $"{what}: gezeichnet", $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
