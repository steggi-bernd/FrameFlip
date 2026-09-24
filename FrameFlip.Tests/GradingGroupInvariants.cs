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

            var palette = (Panel)panel.FindName("Palette");

            if (palette is null)
            {
                Check.That(false, "die Palette ist da");
                return;
            }

            // Die drei Kategorien, deren Werkzeuge dem ganzen Bild gelten - und die
            // beiden, deren Werkzeuge punktweise rechnen und deshalb auch auf einer
            // Ebene gelten. Gegangen wird Zeile fuer Zeile durch die Palette, nicht
            // ueber eine Liste von Namen: Ein neues Werkzeug faellt so von selbst auf.
            var forThePicture = new[] { "S_GroupLight", "S_GroupOptics", "S_GroupFilm" };
            var forBoth = new[] { "S_GroupBasics", "S_GroupTable" };

            ToggleButton[] TilesOf(string tab)
                => palette.Children.OfType<Grid>()
                          .Where(row => (string)row.Tag == tab)
                          .SelectMany(row => row.Children.OfType<Panel>())
                          .SelectMany(tiles => tiles.Children.OfType<ToggleButton>())
                          .ToArray();

            Check.That(forThePicture.Concat(forBoth).All(tab => TilesOf(tab).Length > 0),
                       "jede Kategorie hat ihre Zeile in der Palette");

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
                foreach (var tile in TilesOf(tab))
                {
                    string tag = (string)tile.Tag;

                    if (panel.FindName(tag + "Body") is not FrameworkElement body) continue;

                    Check.That(body.IsEnabled && tile.IsEnabled,
                               $"ohne Ebene ist {tag} bedienbar");
                }
            }

            // Und jetzt mit.
            panel.ShowTarget("Glanz", onLayer: true, locked: false);
            panel.UpdateLayout();

            int locked = 0, open = 0;

            foreach (string tab in forThePicture)
            {
                foreach (var tile in TilesOf(tab))
                {
                    string tag = (string)tile.Tag;

                    if (panel.FindName(tag + "Body") is not FrameworkElement body)
                    {
                        Check.That(false, $"zu {tag} gehoert ein Abschnitt");
                        continue;
                    }

                    if (body.IsEnabled) open++; else locked++;

                    Check.That(!body.IsEnabled,
                               $"{tag} ist an einer Ebene gesperrt - es wuerde dort nie gerechnet");

                    // Und das Zeichen dazu: Wer es anklickt, bekaeme sonst eine Karte,
                    // deren Regler an der Ebene nie wirken.
                    Check.That(!tile.IsEnabled,
                               $"{tag}: auch das Zeichen in der Palette ist gesperrt");
                }
            }

            Console.WriteLine($"         an einer Ebene gesperrt: {locked}, offen geblieben: {open}");

            foreach (string tab in forBoth)
            {
                foreach (var tile in TilesOf(tab))
                {
                    string tag = (string)tile.Tag;

                    if (panel.FindName(tag + "Body") is not FrameworkElement body) continue;

                    Check.That(body.IsEnabled && tile.IsEnabled,
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
        Check.Group("Die Farbspalte: Palette und Effektstapel");

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

            var palette = (Panel)panel.FindName("Palette");
            var cards = (Panel)panel.FindName("Cards");

            var tiles = palette.Children.OfType<Grid>()
                               .SelectMany(row => row.Children.OfType<Panel>())
                               .SelectMany(p => p.Children.OfType<ToggleButton>())
                               .ToDictionary(b => (string)b.Tag);

            // --- Die Palette zeigt ALLE Effekte ----------------------------------
            Check.That(tiles.Count >= 20, "die Palette zeigt alle Effekte auf einen Blick",
                       $"{tiles.Count}");

            static Border? CardOf(Panel cards, string prefix)
                => cards.Children.OfType<Border>().FirstOrDefault(b => (string?)b.Tag == prefix);

            bool Shown(string prefix) => CardOf(cards, prefix)?.Visibility == Visibility.Visible;

            // --- Der Stapel zeigt nur, was benutzt wird --------------------------
            //
            // Das ist der ganze Unterschied zu den Kacheln: Man sah zweiundzwanzig,
            // benutzte drei, und darunter stand immer nur EINES ausgeschrieben.
            var visible = cards.Children.OfType<Border>()
                               .Where(b => b.Visibility == Visibility.Visible)
                               .Select(b => (string)b.Tag).ToArray();

            Check.That(visible.SequenceEqual(new[] { "Basic" }),
                       "auf einem neutralen Bild steht nur die Grundkorrektur im Stapel",
                       string.Join(", ", visible));

            // Ein Zeichen anklicken legt den Effekt in den Stapel.
            tiles["Vignette"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            panel.UpdateLayout();

            Check.That(Shown("Vignette"), "ein Klick auf das Zeichen legt die Karte in den Stapel");
            Check.That(tiles["Vignette"].IsChecked == true, "und das Zeichen zeigt, dass sie drin liegt");

            // Mehrere zugleich offen - das war an den Kacheln unmoeglich.
            tiles["Grain"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            panel.UpdateLayout();

            var vignetteBody = (FrameworkElement)panel.FindName("VignetteBody");
            var grainBody = (FrameworkElement)panel.FindName("GrainBody");
            var basicBody = (FrameworkElement)panel.FindName("BasicBody");

            Check.That(vignetteBody.IsVisible && grainBody.IsVisible && basicBody.IsVisible,
                       "drei Effekte stehen gleichzeitig offen");

            // Die Reihenfolge ist die des Rechenwegs, nicht die des Hinzufuegens:
            // Korn wurde nach der Vignette hinzugefuegt, rechnet aber nach ihr.
            var order = cards.Children.OfType<Border>()
                             .Where(b => b.Visibility == Visibility.Visible)
                             .Select(b => (string)b.Tag).ToArray();

            Check.That(Array.IndexOf(order, "Basic") < Array.IndexOf(order, "Vignette") &&
                       Array.IndexOf(order, "Vignette") < Array.IndexOf(order, "Grain"),
                       "und stehen in der Reihenfolge des Rechenwegs", string.Join(", ", order));

            // --- Was etwas tut, traegt den Strich --------------------------------
            var vignette = panel.Stack.Optics.OfType<VignetteTool>().First();

            static bool Marked(ToggleButton tile)
                => tile.Content is Grid face &&
                   face.Children.OfType<Border>().Any(bar => bar.Visibility == Visibility.Visible);

            Check.That(!Marked(tiles["Vignette"]), "solange sie nichts tut, traegt sie keinen Strich");

            vignette.Amount = 0.5f;
            panel.Load(panel.Adjustments, panel.Stack);
            panel.UpdateLayout();

            Check.That(Marked(tiles["Vignette"]), "sobald sie etwas tut, schon");
            Check.That(Shown("Vignette"), "und ihre Karte bleibt nach dem Laden im Stapel");
            Check.That(!Shown("Grain"),
                       "eine hinzugefuegte, aber unberuehrte Karte nicht - sie gehoerte zum alten Ziel");

            // --- Ausschalten: die Einstellung bleibt -----------------------------
            var power = FindPower(CardOf(cards, "Vignette")!);

            Check.That(power is not null, "die Karte hat einen Ein-Aus-Schalter");
            if (power is null) return;

            power.IsChecked = false;
            power.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            panel.UpdateLayout();

            Check.That(panel.Stack.IsBypassed(vignette.Kind), "ausgeschaltet steht sie auf der Liste");
            Check.That(panel.Stack.Prepare().Optics.Length == 0, "und wird nicht gerechnet");
            Check.Near(vignette.Amount, 0.5, 1e-5, "aber ihre Einstellung bleibt stehen");
            Check.That(Shown("Vignette"), "und ihre Karte auch");
            Check.That(!Marked(tiles["Vignette"]), "ohne den Strich - sie tut ja gerade nichts");

            power.IsChecked = true;
            power.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Check.That(!panel.Stack.IsBypassed(vignette.Kind) && panel.Stack.Prepare().Optics.Length == 1,
                       "wieder eingeschaltet rechnet sie wie vorher");

            // --- Zuruecksetzen und Entfernen -------------------------------------
            //
            // Auch fuer Einstellungen, die auf keinem Regler stehen: Die
            // Farbbereiche haben acht Baender, die Regler zeigen immer nur eines.
            var bands = panel.Stack.Tools.OfType<HslTool>().First();

            bands.Bands[5].Luminance = 40f;
            panel.Load(panel.Adjustments, panel.Stack);
            panel.UpdateLayout();

            Check.That(Shown("Bands"), "veraenderte Farbbereiche stehen im Stapel");

            ClickIn(CardOf(cards, "Bands")!, "\u21BA");
            panel.UpdateLayout();

            Check.That(panel.Stack.Tools.OfType<HslTool>().First().IsNeutral,
                       "Zuruecksetzen erreicht auch das Band, das kein Regler gerade zeigt");

            ClickIn(CardOf(cards, "Vignette")!, "\u2715");
            panel.UpdateLayout();

            Check.That(vignette.IsNeutral, "Entfernen setzt zurueck");
            Check.That(!Shown("Vignette"), "und nimmt die Karte weg");
            Check.That(tiles["Vignette"].IsChecked != true, "das Zeichen zeigt es");
        }
        finally
        {
            window.Close();
        }
    }

    private static ToggleButton? FindPower(DependencyObject root)
        => Descendants(root).OfType<ToggleButton>().FirstOrDefault();

    private static void ClickIn(DependencyObject root, string glyph)
    {
        var button = Descendants(root).OfType<Button>().First(b => (string?)b.Content == glyph);

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            yield return child;

            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

}
