using System.Windows;
using System.Windows.Controls;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging.Grading;
using FrameFlip.Views;

namespace FrameFlip.Tests;

/// <summary>
/// Der Ebenenstreifen - und vor allem die Frage, die beim letzten Panel die
/// wichtigste war: Laesst sich damit ueberhaupt etwas verstellen?
///
/// Ein Streifen, der laedt, richtig aussieht und auf keinen Griff reagiert, besteht
/// jede andere Pruefung. Deshalb wird hier in einem echten Fenster geklickt und
/// gezogen, und jedes Mal geprueft, ob es im Stapel ankommt.
/// </summary>
public static class LayerPanelInvariants
{
    public static void Run()
    {
        InAWindow(panel =>
        {
            StartsWithOneLayer(panel);
            ControlsReachTheLayer(panel);
            AddDuplicateRemove(panel);
            OrderMovesInTheStack(panel);
            ClipTogglesOnTheSelection(panel);
            MaskControlsReachTheLayer(panel);
            ListReadsTopDown(panel);
        });
    }

    /// <summary>
    /// Die Passe einer gewoehnlichen Blender-Datei, ohne dass eine gelesen werden
    /// muss - der Streifen kennt nur Namen.
    /// </summary>
    private static IReadOnlyList<ExrPass> Passes() => ExrPasses.List(new[]
    {
        "ViewLayer.Combined.R", "ViewLayer.Combined.G", "ViewLayer.Combined.B", "ViewLayer.Combined.A",
        "ViewLayer.DiffCol.R", "ViewLayer.DiffCol.G", "ViewLayer.DiffCol.B",
        "ViewLayer.GlossDir.R", "ViewLayer.GlossDir.G", "ViewLayer.GlossDir.B",
        "ViewLayer.Depth.Z",
    });

    private static void InAWindow(Action<LayerPanel> body)
    {
        var panel = new LayerPanel();

        // Ein Fenster drumherum, damit IsLoaded wirklich wahr wird - ohne
        // Fensterwurzel bleibt ein Steuerelement ungeladen, und genau daran haengt
        // der Handler der Regler.
        var window = new Window
        {
            Content = panel,
            Width = 320,
            Height = 700,
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
            body(panel);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Ohne gespeicherten Stapel steht genau eine Ebene da: das Bild.
    ///
    /// Ein leerer Stapel ergaebe ein schwarzes Bild, und das sieht aus wie ein
    /// Fehler statt wie eine Einstellung.
    /// </summary>
    private static void StartsWithOneLayer(LayerPanel panel)
    {
        Check.Group("Der Streifen faengt mit einer Ebene an");

        panel.Load(Passes(), null);

        Check.That(panel.Stack.Layers.Count == 1, "genau eine Ebene", $"{panel.Stack.Layers.Count}");
        Check.That(panel.Stack.Layers[0].Source.Length == 0, "und sie ist das Bild selbst");
        Check.That(panel.Stack.IsPassThrough, "der Stapel rechnet damit gar nicht erst");
        Check.That(panel.HasChoice, "bei vier Passen gibt es etwas zu waehlen");

        var list = (ListBox)panel.FindName("LayerList");
        Check.That(list.Items.Count == 1, "und die Liste zeigt eine Zeile", $"{list.Items.Count}");
        Check.That(list.SelectedItem is not null, "die auch ausgewaehlt ist");

        // Eine Datei mit nur einem Pass hat nichts zu schichten - dann bleibt der
        // ganze Streifen aus dem Weg.
        panel.Load(ExrPasses.List(new[] { "R", "G", "B" }), null);
        Check.That(!panel.HasChoice, "bei einem Pass gibt es nichts zu waehlen");
    }

    /// <summary>
    /// Die eigentliche Frage. Regler bewegen, Auswahlfeld umstellen - kommt es an?
    /// </summary>
    private static void ControlsReachTheLayer(LayerPanel panel)
    {
        Check.Group("Im Streifen laesst sich etwas verstellen");

        panel.Load(Passes(), null);

        int changes = 0;
        panel.Changed += _ => changes++;

        var layer = panel.Stack.Layers[0];

        var opacity = (Slider)panel.FindName("OpacitySlider");
        opacity.Value = 0.4;

        Check.That(changes > 0, "die Deckkraft meldet ihre Aenderung", $"{changes}");
        Check.Near(layer.Opacity, 0.4, 0.001, "und kommt in der Ebene an");

        changes = 0;
        var exposure = (Slider)panel.FindName("LayerExposureSlider");
        exposure.Value = -1.25;

        Check.That(changes > 0, "die Belichtung meldet auch", $"{changes}");
        Check.Near(layer.Exposure, -1.25, 0.001, "und kommt an");

        changes = 0;
        var mode = (ComboBox)panel.FindName("ModeBox");
        int multiply = Array.FindIndex(Blending.All, m => m.Mode == BlendMode.Multiply);
        mode.SelectedIndex = multiply;

        Check.That(changes > 0, "die Mischung meldet", $"{changes}");
        Check.That(panel.Stack.Layers[0].Mode == BlendMode.Multiply, "und steht in der Ebene");

        // Das Auswahlfeld fuehrt jede Mischung, die es gibt - ein Eintrag, der
        // fehlte, waere eine Mischung, die niemand erreicht.
        Check.That(mode.Items.Count == Enum.GetValues<BlendMode>().Length,
                   "jede Mischung steht zur Wahl", $"{mode.Items.Count}");
    }

    /// <summary>Hinzufuegen, verdoppeln, entfernen.</summary>
    private static void AddDuplicateRemove(LayerPanel panel)
    {
        Check.Group("Ebenen anlegen und entfernen");

        panel.Load(Passes(), new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "DiffCol", Mode = BlendMode.Normal },
                new ImageLayer { Source = "ViewLayer.GlossDir", Name = "GlossDir", Mode = BlendMode.Add },
            },
        });

        Check.That(panel.Stack.Layers.Count == 2, "der gespeicherte Stapel kommt an",
                   $"{panel.Stack.Layers.Count}");

        // Verdoppeln: zweimal derselbe Pass mit verschiedener Mischung ist der
        // gewoehnlichste Griff ueberhaupt.
        Click(panel, "DuplicateButton");

        Check.That(panel.Stack.Layers.Count == 3, "verdoppeln legt eine Ebene an",
                   $"{panel.Stack.Layers.Count}");
        Check.That(panel.Stack.Layers[2].Source == panel.Stack.Layers[1].Source,
                   "mit demselben Pass");
        Check.That(panel.Stack.Layers[2].Name != panel.Stack.Layers[1].Name,
                   "aber unterscheidbarem Namen");

        // Die Kopie darf nicht dasselbe Objekt sein - sonst zoege ein Regler beide.
        panel.Stack.Layers[2].Opacity = 0.3f;
        Check.Near(panel.Stack.Layers[1].Opacity, 1.0, 0.001, "die Kopie haengt nicht am Original");

        Click(panel, "RemoveButton");
        Check.That(panel.Stack.Layers.Count == 2, "entfernen nimmt sie wieder weg",
                   $"{panel.Stack.Layers.Count}");

        // Die letzte Ebene bleibt stehen.
        Click(panel, "RemoveButton");
        Click(panel, "RemoveButton");
        Click(panel, "RemoveButton");

        Check.That(panel.Stack.Layers.Count == 1, "die letzte Ebene laesst sich nicht entfernen",
                   $"{panel.Stack.Layers.Count}");

        var remove = (Button)panel.FindName("RemoveButton");
        Check.That(!remove.IsEnabled, "und der Knopf sagt es auch");
    }

    /// <summary>Die Reihenfolge verschieben.</summary>
    private static void OrderMovesInTheStack(LayerPanel panel)
    {
        Check.Group("Die Reihenfolge laesst sich verschieben");

        panel.Load(Passes(), new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "unten" },
                new ImageLayer { Source = "ViewLayer.GlossDir", Name = "oben" },
            },
        });

        // Nach dem Laden ist die oberste Ebene gewaehlt - sie kann nicht hoeher.
        var up = (Button)panel.FindName("UpButton");
        Check.That(!up.IsEnabled, "die oberste Ebene kann nicht hoeher");

        Click(panel, "DownButton");

        Check.That(panel.Stack.Layers[0].Name == "oben", "nach unten schiebt sie an den Anfang");
        Check.That(panel.Stack.Layers[1].Name == "unten", "und die andere darueber");

        var down = (Button)panel.FindName("DownButton");
        Check.That(!down.IsEnabled, "ganz unten geht es nicht weiter");

        Click(panel, "UpButton");
        Check.That(panel.Stack.Layers[1].Name == "oben", "und wieder zurueck");
    }

    /// <summary>
    /// Die Schnittmaske laesst sich umschalten - und ganz unten nicht.
    /// </summary>
    private static void ClipTogglesOnTheSelection(LayerPanel panel)
    {
        Check.Group("Die Schnittmaske laesst sich setzen");

        panel.Load(Passes(), new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.GlossDir", Name = "Licht" },
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "Farbe", Mode = BlendMode.Multiply },
            },
        });

        var clip = (Button)panel.FindName("ClipButton");
        Check.That(clip.IsEnabled, "auf der oberen Ebene geht es");

        Click(panel, "ClipButton");
        Check.That(panel.Stack.Layers[1].Clipped, "und setzt die Schnittmaske");

        Click(panel, "ClipButton");
        Check.That(!panel.Stack.Layers[1].Clipped, "noch einmal nimmt sie wieder zurueck");

        // Nach ganz unten geschoben kann sie sich an nichts mehr anschneiden.
        Click(panel, "DownButton");
        Check.That(!clip.IsEnabled, "auf der untersten Ebene ist der Knopf gesperrt");
    }

    /// <summary>
    /// Die Maskenregler - dieselbe Frage wie bei allen anderen: kommt es an?
    ///
    /// Und eine zweite dazu: Zeigt der Streifen nur die Regler, die zur gewaehlten
    /// Art gehoeren? Ein Schwarzpunkt neben einem Verlauf waere kein Schaden, aber
    /// eine Einladung, an etwas zu drehen, das nichts tut.
    /// </summary>
    private static void MaskControlsReachTheLayer(LayerPanel panel)
    {
        Check.Group("Die Maskenregler erreichen die Ebene");

        panel.Load(Passes(), new LayerStack
        {
            Layers = { new ImageLayer { Source = "ViewLayer.GlossDir", Name = "Glanz" } },
        });

        var layer = panel.Stack.Layers[0];
        Check.That(layer.Mask.Kind == MaskKind.None, "ohne Zutun ist keine Maske gesetzt");

        int changes = 0;
        panel.Changed += _ => changes++;

        var box = (ComboBox)panel.FindName("MaskBox");
        var range = (FrameworkElement)panel.FindName("MaskRangeBody");
        var gradient = (FrameworkElement)panel.FindName("MaskGradientBody");
        var source = (FrameworkElement)panel.FindName("MaskSourceBox");
        var soft = (FrameworkElement)panel.FindName("MaskSoftSlider");

        // Helligkeit: Bereich mit Weichheit, keine Quelle.
        box.SelectedIndex = IndexOf(box, "S_MaskLuminance");

        Check.That(changes > 0, "die Auswahl meldet", $"{changes}");
        Check.That(layer.Mask.Kind == MaskKind.Luminance, "und steht in der Ebene");
        Check.That(range.Visibility == Visibility.Visible, "der Bereich zeigt sich");
        Check.That(soft.Visibility == Visibility.Visible, "samt Weichheit");
        Check.That(source.Visibility != Visibility.Visible, "eine Quelle braucht es nicht");
        Check.That(gradient.Visibility != Visibility.Visible, "und den Verlauf auch nicht");

        changes = 0;
        var low = (Slider)panel.FindName("MaskLowSlider");
        low.Value = 0.6;

        Check.That(changes > 0, "ein Maskenregler meldet", $"{changes}");
        Check.Near(layer.Mask.Low, 0.6, 0.001, "und kommt an");

        // Von darf Bis nicht ueberholen - sonst laesst die Maske nichts mehr durch,
        // und die Ebene sieht aus, als waere sie verschwunden.
        var high = (Slider)panel.FindName("MaskHighSlider");
        high.Value = 0.3;

        Check.That(layer.Mask.Low <= layer.Mask.High, "Von bleibt unter Bis",
                   $"{layer.Mask.Low:0.##} / {layer.Mask.High:0.##}");

        // Pass: Quelle statt Weichheit, und die Quelle wird gleich mitgesetzt.
        box.SelectedIndex = IndexOf(box, "S_MaskPass");

        Check.That(layer.Mask.Kind == MaskKind.Pass, "die Passmaske laesst sich waehlen");
        Check.That(layer.Mask.Source.Length > 0, "und bekommt gleich eine Quelle",
                   layer.Mask.Source);
        Check.That(source.Visibility == Visibility.Visible, "die Quelle zeigt sich");
        Check.That(soft.Visibility != Visibility.Visible,
                   "die Weichheit nicht - dort sind es Schwarz- und Weisspunkt");

        // Verlauf: nur Richtung, Mitte, Breite.
        box.SelectedIndex = IndexOf(box, "S_MaskGradient");

        Check.That(layer.Mask.Kind == MaskKind.Gradient, "der Verlauf ebenso");
        Check.That(gradient.Visibility == Visibility.Visible, "seine Regler zeigen sich");
        Check.That(range.Visibility != Visibility.Visible, "der Bereich verschwindet");

        changes = 0;
        var angle = (Slider)panel.FindName("MaskAngleSlider");
        angle.Value = 215;

        Check.That(changes > 0, "die Richtung meldet", $"{changes}");
        Check.Near(layer.Mask.Angle, 215, 0.5, "und kommt an");

        // Umkehren.
        changes = 0;
        var invert = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("MaskInvertButton");
        invert.IsChecked = true;

        Check.That(changes > 0, "das Umkehren meldet", $"{changes}");
        Check.That(layer.Mask.Invert, "und steht in der Maske");

        // Und zurueck auf keine.
        box.SelectedIndex = IndexOf(box, "S_MaskNone");
        Check.That(layer.Mask.IsNeutral, "ohne Maske ist die Ebene wieder neutral");
    }

    /// <summary>Der Platz eines Eintrags in der Auswahl, ueber seinen uebersetzten Namen.</summary>
    private static int IndexOf(ComboBox box, string key)
    {
        string wanted = FrameFlip.Localization.Strings.T(key);

        for (int i = 0; i < box.Items.Count; i++)
            if (Equals(box.Items[i], wanted)) return i;

        return 0;
    }

    /// <summary>
    /// Die Liste zeigt oben, was im Bild oben liegt - wie in jedem Bildprogramm.
    ///
    /// Der Stapel selbst laeuft andersherum, weil von unten nach oben gerechnet
    /// wird. Die Umkehrung steht nur in der Oberflaeche, und genau deshalb ist sie
    /// hier zu pruefen: Ein Vorzeichenfehler faellt sonst erst dem auf, der sich
    /// wundert, warum "nach oben" nach unten schiebt.
    /// </summary>
    private static void ListReadsTopDown(LayerPanel panel)
    {
        Check.Group("Oben in der Liste ist oben im Bild");

        panel.Load(Passes(), new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "unten" },
                new ImageLayer { Source = "ViewLayer.GlossDir", Name = "oben" },
            },
        });

        var list = (ListBox)panel.FindName("LayerList");
        Check.That(list.Items.Count == 2, "beide Zeilen stehen da", $"{list.Items.Count}");

        var first = (ListBoxItem)list.Items[0];
        Check.That(((ImageLayer)first.Tag).Name == "oben",
                   "die erste Zeile ist die oberste Ebene");

        var last = (ListBoxItem)list.Items[1];
        Check.That(((ImageLayer)last.Tag).Name == "unten", "die letzte die unterste");
    }

    private static void Click(LayerPanel panel, string name)
    {
        var button = (Button)panel.FindName(name);
        if (!button.IsEnabled) return;

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        panel.UpdateLayout();
    }
}
