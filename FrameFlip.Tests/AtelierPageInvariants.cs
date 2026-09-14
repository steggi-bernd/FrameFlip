using System.IO;
using System.Windows;
using FrameFlip.Configuration;
using FrameFlip.Decoding;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace FrameFlip.Tests;

/// <summary>
/// Die Atelierseite und ihr Werkzeugbereich - wirklich angelegt, gemessen und
/// gezeichnet.
///
/// Dass die XAML laedt, faengt einen fehlenden Stil und einen falschen TargetType.
/// Dass die Regler sich fuellen lassen, faengt den Fehler, der vorher schon einmal
/// da war: ein Handler, der beim Einlesen zu frueh laeuft und auf Elemente greift,
/// die es noch nicht gibt.
/// </summary>
public static class AtelierPageInvariants
{
    public static void Run()
    {
        Panel();
        Page();
    }

    private static void Panel()
    {
        Check.Group("Werkzeugbereich");

        GradingPanel panel;

        try
        {
            panel = new GradingPanel();
        }
        catch (Exception ex)
        {
            Check.That(false, "laesst sich anlegen", $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        Check.That(true, "laesst sich anlegen");

        // Messen und anordnen fuehrt das Laden zu Ende - erst danach stehen die
        // Werte in den Reglern.
        panel.Measure(new Size(300, 900));
        panel.Arrange(new Rect(0, 0, 300, 900));
        panel.UpdateLayout();

        Check.That(panel.Adjustments.IsNeutral, "startet ohne Korrektur");
        Check.That(panel.Stack.IsNeutral, "und ohne wirksame Werkzeuge");
        Check.That(panel.Prepared.IsEmpty || panel.Prepared.SceneLinear.Length == 0,
                   "der vorbereitete Stapel ist leer");

        // Gespeicherte Einstellungen uebernehmen.
        var stack = new GradingStack();
        stack.Tools.Add(new WhiteBalanceTool { Kelvin = 4000 });
        stack.Tools.Add(new CurvesTool
        {
            Master = new ToneCurve(new[] { new CurvePoint(0, 0), new CurvePoint(0.5f, 0.8f), new CurvePoint(1, 1) }),
        });

        try
        {
            panel.Load(new ImageAdjustments { Exposure = -1.5 }, stack);
        }
        catch (Exception ex)
        {
            Check.That(false, "uebernimmt gespeicherte Einstellungen", $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        Check.Near(panel.Adjustments.Exposure, -1.5, 0.001, "die Belichtung kommt an");
        Check.That(!panel.Stack.IsNeutral, "und die Werkzeuge wirken");

        // Jedes Werkzeug muss genau einmal vorkommen - auch wenn der gespeicherte
        // Stapel nur zwei davon kannte.
        foreach (var kind in new[]
                 {
                     typeof(CurvesTool), typeof(WhiteBalanceTool), typeof(LiftGammaGainTool),
                     typeof(HslTool), typeof(VibranceTool), typeof(LutTool),
                 })
        {
            int count = panel.Stack.Tools.Count(t => t.GetType() == kind);
            Check.That(count == 1, $"{kind.Name} kommt genau einmal vor", $"{count}");
        }

        var prepared = panel.Prepare();
        Check.That(prepared.SceneLinear.Length == 1, "der Weissabgleich steht auf der linearen Seite");
        Check.That(prepared.Display.Length == 1, "die Kurve auf der Anzeigeseite");

        // Ein zweites Laden darf nicht doppeln.
        panel.Load(null, panel.Stack);
        Check.That(panel.Stack.Tools.Count == 6, "nochmal laden doppelt nichts",
                   $"{panel.Stack.Tools.Count}");

        // Abschalten und wieder an.
        panel.ToolsEnabled = false;
        Check.That(!panel.ToolsEnabled, "die Werkzeuge lassen sich abschalten");
        panel.ToolsEnabled = true;
        Check.That(panel.ToolsEnabled, "und wieder an");

        // Eine Messung durchreichen - das zeichnet Histogramm und Kurvenhintergrund.
        var histogram = new Histogram();
        var frame = new FloatFrame
        {
            Width = 4, Height = 4,
            R = Enumerable.Repeat(0.5f, 16).ToArray(),
            G = Enumerable.Repeat(0.5f, 16).ToArray(),
            B = Enumerable.Repeat(2.0f, 16).ToArray(),
        };

        FloatFrameProcessor.Measure(frame, panel.Adjustments, new StandardViewTransform(),
                                    panel.Prepared, histogram);

        try
        {
            panel.ShowHistogram(histogram);
            Check.That(true, "eine Messung laesst sich anzeigen");
        }
        catch (Exception ex)
        {
            Check.That(false, "eine Messung laesst sich anzeigen", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Page()
    {
        Check.Group("Atelierseite");

        string folder = Path.Combine(Path.GetTempPath(), "frameflip-atelier-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var settings = new AppSettings();
            var saved = new List<AppSettings>();

            AtelierPage page;

            try
            {
                page = new AtelierPage(FrameDecoderRegistry.CreateDefault(() => null), settings, saved.Add);
            }
            catch (Exception ex)
            {
                Check.That(false, "laesst sich anlegen", $"{ex.GetType().Name}: {ex.Message}");
                return;
            }

            Check.That(true, "laesst sich anlegen");

            page.Measure(new Size(1200, 800));
            page.Arrange(new Rect(0, 0, 1200, 800));
            page.UpdateLayout();

            Check.That(true, "und anordnen");

            // Eine Datei, die keine ist: die Seite darf das melden und nicht werfen.
            string broken = Path.Combine(folder, "kaputt.exr");
            File.WriteAllBytes(broken, new byte[] { 1, 2, 3, 4 });

            try
            {
                page.Open(broken);
                Check.That(true, "eine unlesbare Datei wirft nicht");
            }
            catch (Exception ex)
            {
                Check.That(false, "eine unlesbare Datei wirft nicht", $"{ex.GetType().Name}: {ex.Message}");
            }

            // Und eine, die es gar nicht gibt.
            try
            {
                page.Open(Path.Combine(folder, "gibtsnicht.png"));
                Check.That(true, "eine fehlende Datei ebenso");
            }
            catch (Exception ex)
            {
                Check.That(false, "eine fehlende Datei ebenso", $"{ex.GetType().Name}: {ex.Message}");
            }

            // Die Arbeiterzahl kommt von aussen und darf fehlen - ohne Zuweisung
            // nimmt der Lauf die Haelfte der Kerne.
            var before = AtelierPage.Workers;

            try
            {
                AtelierPage.Workers = null;
                Check.That(true, "ohne Lastregelung laeuft es trotzdem");

                AtelierPage.Workers = () => 3;
                Check.That(AtelierPage.Workers() == 3, "und laesst sich setzen");
            }
            finally
            {
                AtelierPage.Workers = before;
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }
}
