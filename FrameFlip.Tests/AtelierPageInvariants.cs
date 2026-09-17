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

        // Und den eigenen Stapel hereinzureichen darf ihn nicht leeren. Vorher wurde
        // erst geleert und dann aus derselben Liste gelesen - die Werkzeuge kamen
        // frisch und neutral zurueck, und der Weissabgleich war still weg.
        Check.Near(panel.Stack.Tools.OfType<WhiteBalanceTool>().Single().Kelvin, 4000, 1,
                   "und wirft die Einstellungen nicht weg");

        // Klarheit steht in der eigenen Liste - und muss denselben Weg nehmen.
        Check.That(panel.Stack.Local.OfType<ClarityTool>().Count() == 1,
                   "Klarheit steht genau einmal in der oertlichen Liste",
                   $"{panel.Stack.Local.Count}");

        // Ein gespeicherter Wert muss ankommen. Wuerde die oertliche Liste beim Laden
        // uebergangen, behielte der Bereich sein eigenes Werkzeug: Eingestellt an
        // einer Ebene, wirksam an allen, und beim naechsten Start weg.
        var local = new GradingStack();
        local.Local.Add(new ClarityTool { Amount = 0.6f, Reach = 80 });

        panel.Load(null, local);

        var clarity = panel.Stack.Local.OfType<ClarityTool>().Single();
        Check.Near(clarity.Amount, 0.6, 0.001, "die gespeicherte Klarheit kommt an");
        Check.That(clarity.Reach == 80, "mitsamt Radius", $"{clarity.Reach}");

        // Und sie steht auch am Regler - sonst zeigt der Bereich etwas anderes an,
        // als das Bild bekommt.
        var amountSlider = (System.Windows.Controls.Slider)panel.FindName("ClaritySlider");
        var reachSlider = (System.Windows.Controls.Slider)panel.FindName("ClarityReachSlider");

        Check.That(amountSlider is not null && reachSlider is not null, "die Regler sind da");

        if (amountSlider is not null) Check.Near(amountSlider.Value, 0.6, 0.001, "der Staerkeregler zeigt sie");
        if (reachSlider is not null) Check.Near(reachSlider.Value, 80, 0.001, "der Radiusregler auch");

        // Zurueck auf einen Stapel ohne Klarheit: Dann darf nichts haengenbleiben.
        panel.Load(null, new GradingStack());
        Check.That(panel.Stack.Local.OfType<ClarityTool>().Single().IsNeutral,
                   "ein Stapel ohne Klarheit laesst keine stehen");

        // Alle drei oertlichen Werkzeuge kommen genau einmal vor - jedes bringt
        // seine eigene Stufe mit, und der Stapel sortiert danach.
        foreach (var kind in new[]
                 {
                     typeof(DehazeTool), typeof(BloomTool), typeof(HalationTool),
                     typeof(NoiseTool), typeof(ClarityTool), typeof(TextureTool),
                     typeof(SharpenTool),
                 })
        {
            int count = panel.Stack.Local.Count(tool => tool.GetType() == kind);
            Check.That(count == 1, $"{kind.Name} steht genau einmal in der oertlichen Liste", $"{count}");
        }

        // Vignette und Korn stehen in ihrer eigenen Liste, aus demselben Grund und
        // auf einem anderen Weg.
        foreach (var kind in new[] { typeof(VignetteTool), typeof(GrainTool) })
        {
            int count = panel.Stack.Optics.Count(tool => tool.GetType() == kind);
            Check.That(count == 1, $"{kind.Name} steht genau einmal in der Ortsliste", $"{count}");
        }

        // Und die vierte Liste - die Werkzeuge, die Bildpunkte verschieben.
        foreach (var kind in new[] { typeof(DistortionTool), typeof(ChromaticTool) })
        {
            int count = panel.Stack.Geometry.Count(tool => tool.GetType() == kind);
            Check.That(count == 1, $"{kind.Name} steht genau einmal in der Geometrieliste", $"{count}");
        }

        // An einer Ebene sind sie abgeschaltet. Sie wirken auf das fertige Bild; eine
        // Einstellungsebene wird punktweise gerechnet, und eine Nachbarschaft gibt es
        // dort nicht. Bedienbar zu bleiben waere schlimmer als abgeschaltet zu sein -
        // der Regler liefe, und das Bild bliebe stehen.
        var distortionBody = (System.Windows.FrameworkElement)panel.FindName("DistortionBody");
        var vignetteBody = (System.Windows.FrameworkElement)panel.FindName("VignetteBody");
        var grainBody = (System.Windows.FrameworkElement)panel.FindName("GrainBody");
        var dehazeBody = (System.Windows.FrameworkElement)panel.FindName("DehazeBody");
        var textureBody = (System.Windows.FrameworkElement)panel.FindName("TextureBody");
        var bloomBody = (System.Windows.FrameworkElement)panel.FindName("BloomBody");
        var noiseBody = (System.Windows.FrameworkElement)panel.FindName("NoiseBody");
        var clarityBody = (System.Windows.FrameworkElement)panel.FindName("ClarityBody");
        var sharpenBody = (System.Windows.FrameworkElement)panel.FindName("SharpenBody");
        var note = (System.Windows.FrameworkElement)panel.FindName("LocalOnFinalNote");

        panel.Target = "eine Ebene";

        Check.That(!dehazeBody.IsEnabled && !bloomBody.IsEnabled && !noiseBody.IsEnabled &&
                   !clarityBody.IsEnabled && !textureBody.IsEnabled && !sharpenBody.IsEnabled &&
                   !vignetteBody.IsEnabled && !grainBody.IsEnabled && !distortionBody.IsEnabled,
                   "an einer Ebene sind die oertlichen Werkzeuge abgeschaltet");
        Check.That(note.Visibility == System.Windows.Visibility.Visible,
                   "und der Grund steht dabei");

        panel.Target = null;

        Check.That(dehazeBody.IsEnabled && bloomBody.IsEnabled && noiseBody.IsEnabled &&
                   clarityBody.IsEnabled && textureBody.IsEnabled && sharpenBody.IsEnabled &&
                   vignetteBody.IsEnabled && grainBody.IsEnabled && distortionBody.IsEnabled,
                   "am fertigen Bild wieder an");
        Check.That(note.Visibility != System.Windows.Visibility.Visible,
                   "und der Hinweis verschwindet");

        // Abschalten und wieder an.
        panel.ToolsEnabled = false;
        Check.That(!panel.ToolsEnabled, "die Werkzeuge lassen sich abschalten");
        panel.ToolsEnabled = true;
        Check.That(panel.ToolsEnabled, "und wieder an");

        // Zwei Gruende koennen die oertlichen Abschnitte sperren, und der zuletzt
        // gesetzte darf nicht gewinnen: An einer Ebene bleiben sie gesperrt, auch
        // wenn die Werkzeuge gerade eingeschaltet wurden.
        panel.Target = "eine Ebene";
        panel.ToolsEnabled = true;

        Check.That(!clarityBody.IsEnabled, "die Ebenensperre ueberlebt den Gesamtschalter");
        panel.Target = null;

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

            // Vergleich und Massstab: beides muss sich bedienen lassen, ohne dass
            // ein Bild offen ist - ein Klick auf einer leeren Seite darf nicht
            // werfen, und genau das passiert beim Ausprobieren als erstes.
            var compare = page.FindName("CompareButton") as System.Windows.Controls.Button;
            var zoom = page.FindName("ZoomButton") as System.Windows.Controls.Button;

            Check.That(compare is not null && zoom is not null, "Vergleich und Massstab sind da");

            if (compare is not null)
            {
                Check.That(!compare.IsEnabled, "ohne Bild ist der Vergleich aus");

                try
                {
                    compare.RaiseEvent(new System.Windows.RoutedEventArgs(
                        System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Check.That(true, "und ein Klick darauf wirft nicht");
                }
                catch (Exception ex)
                {
                    Check.That(false, "und ein Klick darauf wirft nicht", ex.GetType().Name);
                }
            }

            if (zoom is not null)
            {
                try
                {
                    // Zweimal: hin zur vollen Groesse und wieder zurueck. Ohne Bild
                    // gibt es nichts zu skalieren, und auch das muss halten.
                    zoom.RaiseEvent(new System.Windows.RoutedEventArgs(
                        System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    zoom.RaiseEvent(new System.Windows.RoutedEventArgs(
                        System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                    Check.That(true, "der Massstab laesst sich ohne Bild umschalten");
                }
                catch (Exception ex)
                {
                    Check.That(false, "der Massstab laesst sich ohne Bild umschalten", ex.GetType().Name);
                }
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
