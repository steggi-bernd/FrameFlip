using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Die Farbspalte als Reiter mit Kacheln.
///
/// Einundzwanzig aufklappbare Faecher untereinander waren ein Schacht: Wer die
/// Vignette suchte, rollte an siebzehn Dingen vorbei, die er nicht gesucht hatte -
/// und sah dabei von keinem einzigen, ob es gerade etwas tut. Ein zugeklapptes Fach
/// sieht aus wie ein zugeklapptes Fach, ob die Vignette nun steht oder nicht.
///
/// Geprueft werden deshalb genau die drei Zusagen, die Kacheln machen: Ein Reiter
/// zeigt seine und nur seine Werkzeuge. Ausgeschrieben steht immer GENAU EINES. Und
/// eine Kachel, deren Werkzeug etwas tut, sieht anders aus als eine, deren Werkzeug
/// nichts tut - das ist der eigentliche Gewinn, und er laesst sich messen.
/// </summary>
public static class GradingGroupInvariants
{
    public static void Run()
    {
        TilesShowWhatIsThereAndWhatIsOn();
        ALayerLocksWhatCannotRunOnIt();
        GlitchControlsShowWhatTheyMean();
    }

    /// <summary>
    /// Die Glitch-Abschnitte zeigen nur, was bei der gewaehlten Art etwas bedeutet -
    /// und alte Rezepte kommen richtig an.
    ///
    /// Ein Regler, der bei der gewaehlten Art nichts tut, ist schlimmer als keiner:
    /// Man zieht daran, nichts passiert, und sucht den Fehler. Und ein Rezept mit dem
    /// alten Schalter "Spalten" muss als 90 Grad ankommen - sonst stuende der Winkel
    /// auf null, waehrend der unsichtbare Schalter weiter Spalten sortiert.
    /// </summary>
    private static void GlitchControlsShowWhatTheyMean()
    {
        Check.Group("Die Glitch-Regler zeigen, was sie bedeuten");

        var panel = new GradingPanel();

        var window = new Window
        {
            Content = panel,
            Width = 420,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            // --- Ein altes Rezept: Pixel Sorting mit dem Schalter "Spalten" ------
            var old = new GradingStack
            {
                Frame = { new SortTool { Low = 0.2f, High = 0.8f, Vertical = true } },
            };

            panel.Load(ImageAdjustments.Neutral, old);
            panel.UpdateLayout();

            var sort = panel.Stack.Frame.OfType<SortTool>().First();
            var angle = (Slider)panel.FindName("SortAngleSlider");

            Check.That(!sort.Vertical && Math.Abs(sort.Angle - 90f) < 0.01f,
                       "der alte Schalter 'Spalten' kommt als 90 Grad an",
                       $"Vertical={sort.Vertical}, Winkel={sort.Angle}");

            Check.Near(angle.Value, 90, 0.01, "und der Winkelregler zeigt es");

            // --- Kantenschwelle nur bei Kanten ----------------------------------
            var interval = (ComboBox)panel.FindName("SortIntervalBox");
            var edgeRow = (FrameworkElement)panel.FindName("SortEdgeRow");

            Check.That(edgeRow.Visibility != Visibility.Visible,
                       "bei der Schwelle steht keine Kantenschwelle da");

            interval.SelectedIndex = (int)SortInterval.Edges;
            panel.UpdateLayout();

            Check.That(sort.Interval == SortInterval.Edges, "die Wahl kommt im Werkzeug an");
            Check.That(edgeRow.Visibility == Visibility.Visible, "und bei Kanten erscheint sie");

            // --- Verschiebung: Dichte und Wuerfel nur bei den zerrissenen Formen --
            var shape = (ComboBox)panel.FindName("DisplaceShapeBox");
            var density = (FrameworkElement)panel.FindName("DisplaceDensityRow");
            var reseed = (FrameworkElement)panel.FindName("DisplaceReseedButton");
            var wave = (Slider)panel.FindName("DisplaceWaveSlider");

            shape.SelectedIndex = (int)WaveShape.Sine;
            panel.UpdateLayout();

            Check.That(density.Visibility != Visibility.Visible && reseed.Visibility != Visibility.Visible,
                       "beim Sinus gibt es weder Dichte noch Wuerfel");

            // Und wer auf Streifen wechselt, waehrend die Welle auf null steht, saehe
            // nur das ganze Bild verrueckt - deshalb wird sie sichtbar aufgedreht.
            wave.Value = 0;
            shape.SelectedIndex = (int)WaveShape.Slices;
            panel.UpdateLayout();

            var displace = panel.Stack.Data.OfType<DisplaceTool>().First();

            Check.That(displace.Shape == WaveShape.Slices, "die Form kommt im Werkzeug an");
            Check.That(density.Visibility == Visibility.Visible && reseed.Visibility == Visibility.Visible,
                       "bei Streifen erscheinen Dichte und Wuerfel");
            Check.Near(wave.Value, 1, 0.01, "und die Welle wird aufgedreht, sichtbar am Regler");

            int before = displace.Seed;

            ((System.Windows.Controls.Button)reseed).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Check.That(displace.Seed != before, "Neu wuerfeln gibt einen anderen Startwert");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Was auf einer Ebene nicht gerechnet werden kann, darf dort auch nicht
    /// bedienbar sein.
    ///
    /// Eine Ebene bekommt nur die PUNKTWEISEN Werkzeuge - LayerGrade reicht
    /// SceneLinear und Display durch, sonst nichts. Alles andere - oertliche
    /// Werkzeuge, Optik, Renderdaten, Raster - wird an einer Ebene nie gerechnet.
    ///
    /// Ein Abschnitt, der trotzdem bedienbar bleibt, schreibt in den Stapel der Ebene
    /// und verschwindet dort. Im Fenster sieht das aus wie "der Regler tut nichts",
    /// und zwar ohne Meldung und ohne gesperrten Regler - die unangenehmste Art
    /// Fehler, die dieses Programm kennt.
    ///
    /// Genau das ist hier schon ZWEIMAL passiert: beim Rastern und bei der
    /// Verschiebung. Beide Male stand der Abschnitt im Streifen und fehlte in der
    /// Sperrliste. Diese Probe zaehlt deshalb nicht Namen auf, sondern geht die
    /// Reiter ab: Was in einem der Reiter fuer das ganze Bild steht, MUSS gesperrt
    /// sein, sobald eine Ebene gewaehlt ist. Ein neues Werkzeug faellt damit von
    /// selbst auf.
    /// </summary>
    private static void ALayerLocksWhatCannotRunOnIt()
    {
        Check.Group("Eine gewaehlte Ebene sperrt, was auf ihr nicht rechnen kann");

        var panel = new GradingPanel();

        var window = new Window
        {
            Content = panel,
            Width = 420,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            var tabs = (Panel)panel.FindName("Tabs");
            var tiles = (Panel)panel.FindName("Tiles");

            if (tabs is null || tiles is null)
            {
                Check.That(false, "Reiterleiste und Kachelfeld sind da");
                return;
            }

            // Die drei Reiter, deren Werkzeuge dem ganzen Bild gelten - und die
            // beiden, deren Werkzeuge punktweise rechnen und deshalb auch auf einer
            // Ebene gelten.
            var forThePicture = new[] { "S_GroupLight", "S_GroupOptics", "S_GroupFilm" };
            var forBoth = new[] { "S_GroupBasics", "S_GroupTable" };

            static string[] TagsOf(Panel tiles)
                => tiles.Children.OfType<ToggleButton>()
                        .Select(b => (string)b.Tag)
                        .Where(tag => tag is not null)
                        .ToArray();

            void Open(string tab)
            {
                var button = tabs.Children.OfType<ToggleButton>()
                                 .First(b => (string)b.Tag == tab);

                button.IsChecked = true;
                panel.UpdateLayout();
            }

            // Der Unterschied zwischen "ruht" und "kaputt".
            //
            // Ein Werkzeug mit Renderdaten ruht, wenn sein Pass fehlt - das ist
            // richtig. Nur merkt das niemand: Man zieht am Regler, es passiert
            // nichts, und dann sucht man den Fehler im Programm statt in der Datei.
            // Genau dieser Weg hat hier schon einmal eine Runde gekostet.
            var missing = new[] { "MotionMissing", "DepthMissing", "DisplaceMissing" };

            panel.ShowPasses(depth: true, motion: true, normal: true);
            panel.UpdateLayout();

            foreach (string name in missing)
            {
                var note = (FrameworkElement)panel.FindName(name);

                Check.That(note.Visibility != Visibility.Visible,
                           $"mit allen Passen schweigt {name}");
            }

            panel.ShowPasses(depth: false, motion: false, normal: false);
            panel.UpdateLayout();

            foreach (string name in missing)
            {
                var note = (FrameworkElement)panel.FindName(name);

                Check.That(note.Visibility == Visibility.Visible,
                           $"ohne sie sagt {name}, woran es liegt");
            }

            // Und bei der Verschiebung haengt es daran, welcher Pass gewaehlt ist:
            // Wer den Vektorpass waehlt, will nichts ueber die Normale hoeren.
            var displaceNote = (TextBlock)panel.FindName("DisplaceMissing");

            panel.ShowPasses(depth: true, motion: true, normal: false);
            panel.UpdateLayout();

            Check.That(displaceNote.Visibility == Visibility.Visible,
                       "ohne Normalpass meldet sich die Verschiebung");

            panel.ShowPasses(depth: true, motion: false, normal: true);
            panel.UpdateLayout();

            Check.That(displaceNote.Visibility != Visibility.Visible,
                       "mit Normalpass schweigt sie - der fehlende Vektorpass ist nicht ihrer");

            // Und auf "nur Welle" gestellt schweigt sie auch ohne jeden Pass - dort
            // ist "der Pass fehlt" keine Erklaerung mehr, sondern eine Irrefuehrung.
            var fromBox = (ComboBox)panel.FindName("DisplaceFromBox");

            fromBox.SelectedIndex = 2;
            panel.ShowPasses(depth: false, motion: false, normal: false);
            panel.UpdateLayout();

            Check.That(displaceNote.Visibility != Visibility.Visible,
                       "auf 'nur Welle' schweigt sie auch ganz ohne Passe");

            fromBox.SelectedIndex = 0;

            panel.ShowPasses(depth: true, motion: true, normal: true);
            panel.UpdateLayout();

            // Erst ohne Ebene: Alles muss bedienbar sein, sonst misst die zweite
            // Haelfte nur, dass ohnehin nichts geht.
            panel.ShowTarget(null, onLayer: false, locked: false);
            panel.UpdateLayout();

            foreach (string tab in forThePicture.Concat(forBoth))
            {
                Open(tab);

                foreach (string tag in TagsOf(tiles))
                {
                    if (panel.FindName(tag + "Body") is not FrameworkElement body) continue;

                    Check.That(body.IsEnabled,
                               $"ohne Ebene ist {tag} bedienbar");
                }
            }

            // Und jetzt mit.
            panel.ShowTarget("Glanz", onLayer: true, locked: false);
            panel.UpdateLayout();

            int locked = 0, open = 0;

            foreach (string tab in forThePicture)
            {
                Open(tab);

                foreach (string tag in TagsOf(tiles))
                {
                    if (panel.FindName(tag + "Body") is not FrameworkElement body)
                    {
                        Check.That(false, $"zu {tag} gehoert ein Abschnitt");
                        continue;
                    }

                    if (body.IsEnabled) open++; else locked++;

                    Check.That(!body.IsEnabled,
                               $"{tag} ist an einer Ebene gesperrt - es wuerde dort nie gerechnet");
                }
            }

            Console.WriteLine($"         an einer Ebene gesperrt: {locked}, offen geblieben: {open}");

            foreach (string tab in forBoth)
            {
                Open(tab);

                foreach (string tag in TagsOf(tiles))
                {
                    if (panel.FindName(tag + "Body") is not FrameworkElement body) continue;

                    Check.That(body.IsEnabled,
                               $"{tag} bleibt bedienbar - es rechnet punktweise und gilt auch auf einer Ebene");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void TilesShowWhatIsThereAndWhatIsOn()
    {
        Check.Group("Die Farbspalte: Reiter und Kacheln");

        var panel = new GradingPanel();

        var window = new Window
        {
            Content = panel,
            Width = 420,
            Height = 900,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Left = -4000,
            Top = -4000,
        };

        try
        {
            window.Show();
            panel.UpdateLayout();

            var tabs = (Panel)panel.FindName("Tabs");
            var tiles = (Panel)panel.FindName("Tiles");

            Check.That(tabs is not null && tiles is not null, "Reiterleiste und Kachelfeld sind da");
            if (tabs is null || tiles is null) return;

            Check.That(tabs.Children.Count == 5, "fuenf Reiter", $"{tabs.Children.Count}");

            // Der erste Reiter steht offen und zeigt seine Werkzeuge.
            //
            // Geprueft wird, WELCHE dort stehen, nicht wieviele: Die Zahl aendert
            // sich mit jedem neuen Werkzeug, und sie soll es auch. Eine feste Zahl
            // hier bricht die Probe beim naechsten Werkzeug, ohne dass etwas kaputt
            // waere - und das ist die Art Probe, die man irgendwann nur noch
            // nachzieht, statt sie zu lesen.
            Check.That(tiles.Children.Count > 0 &&
                       tiles.Children.OfType<ToggleButton>().Any(b => (string)b.Tag == "Curve"),
                       "die Grundkorrektur zeigt ihre Kacheln",
                       string.Join(", ", tiles.Children.OfType<ToggleButton>()
                                               .Select(b => (string)b.Tag)));

            // Genau ein Abschnitt ist ausgeschrieben.
            int open = 0;

            foreach (string name in new[]
                     {
                         "BasicBody", "CurveBody", "WhiteBalanceBody", "ZonesBody", "BandsBody",
                         "DehazeBody", "BloomBody", "VignetteBody", "DitherBody", "GrainBody",
                     })
            {
                if (panel.FindName(name) is FrameworkElement shown &&
                    shown.Visibility == Visibility.Visible)
                {
                    open++;
                }
            }

            Check.That(open == 1, "und genau ein Abschnitt steht ausgeschrieben", $"{open}");

            // Ein anderer Reiter zeigt andere Kacheln - und den ersten seiner eigenen.
            var optics = tabs.Children.OfType<ToggleButton>()
                             .First(b => (string)b.Tag == "S_GroupOptics");

            optics.IsChecked = true;
            panel.UpdateLayout();

            // Gezaehlt und nicht aufgezaehlt: Die Zahl aendert sich mit jedem neuen
            // Werkzeug, und sie soll es auch - die Aussage ist "der Reiter zeigt
            // ANDERE Kacheln als der erste", nicht "genau diese fuenf". Wer hier eine
            // feste Zahl hinterlegt, bricht die Probe beim naechsten Werkzeug, ohne
            // dass etwas kaputt waere.
            Check.That(tiles.Children.Count > 0 &&
                       tiles.Children.OfType<ToggleButton>().Any(b => (string)b.Tag == "Vignette") &&
                       tiles.Children.OfType<ToggleButton>().All(b => (string)b.Tag != "Curve"),
                       "die Optik zeigt ihre eigenen Kacheln und keine der ersten",
                       string.Join(", ", tiles.Children.OfType<ToggleButton>()
                                               .Select(b => (string)b.Tag)));

            var basic = (FrameworkElement)panel.FindName("BasicBody");

            Check.That(basic.Visibility != Visibility.Visible,
                       "und die Grundkorrektur ist damit nicht mehr ausgeschrieben");

            // Eine Kachel waehlen schreibt ihren Abschnitt aus - und nur ihren.
            var vignette = tiles.Children.OfType<ToggleButton>()
                                .First(b => (string)b.Tag == "Vignette");

            vignette.IsChecked = true;
            panel.UpdateLayout();

            var body = (FrameworkElement)panel.FindName("VignetteBody");
            var motion = (FrameworkElement)panel.FindName("MotionBody");

            Check.That(body.Visibility == Visibility.Visible, "die gewaehlte Kachel schreibt aus");
            Check.That(motion.Visibility != Visibility.Visible, "und die daneben nicht mehr");

            // Und der Gewinn, um den es ging: Eine Kachel, deren Werkzeug etwas tut,
            // sieht anders aus als eine, deren Werkzeug nichts tut.
            static bool Marked(ToggleButton tile)
                => tile.Content is Grid face &&
                   face.Children.OfType<System.Windows.Shapes.Ellipse>()
                       .Any(dot => dot.Visibility == Visibility.Visible);

            bool quiet = Marked(vignette);

            Check.That(!quiet, "eine Kachel, deren Werkzeug nichts tut, traegt keinen Punkt");

            // Und sie ist trotzdem voll lesbar - blass sah sie aus wie gesperrt.
            Check.Near(vignette.Opacity, 1.0, 0.001,
                       "und ist trotzdem voll lesbar, nicht blass wie gesperrt");

            panel.Stack.Optics.OfType<VignetteTool>().First().Amount = 0.5f;

            panel.Load(panel.Adjustments, panel.Stack);
            panel.UpdateLayout();

            var again = ((Panel)panel.FindName("Tiles")).Children.OfType<ToggleButton>()
                        .First(b => (string)b.Tag == "Vignette");

            Check.That(Marked(again),
                       "eine Kachel, deren Werkzeug etwas tut, traegt den Punkt");
            Check.That(again.FontWeight == FontWeights.SemiBold,
                       "und ihr Name steht kraeftiger");
        }
        finally
        {
            window.Close();
        }
    }
}
