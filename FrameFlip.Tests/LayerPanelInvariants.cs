using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            GroupsNestInTheList(panel);
            ListReadsTopDown(panel);
            DraggingReordersTheStack(panel);
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

    /// <summary>
    /// Ziehen in der Liste - die Rechnung dahinter, nicht das Zugereignis.
    ///
    /// Hier steht die Umkehrung, an der schon einmal etwas falsch herum gebaut
    /// wurde: Die Liste laeuft rueckwaerts zum Stapel. Wer eine Zeile UEBER eine
    /// andere zieht, meint im Stapel den HOEHEREN Platz. Ein Test, der nur prueft,
    /// dass sich ueberhaupt etwas bewegt, faende genau diesen Fehler nicht.
    /// </summary>
    private static void DraggingReordersTheStack(LayerPanel panel)
    {
        Check.Group("Ziehen sortiert um - in der Richtung der Liste");

        var stack = panel.Stack;

        stack.Layers.Clear();

        var unten = new ImageLayer { Name = "unten" };
        var mitte = new ImageLayer { Name = "mitte" };
        var oben = new ImageLayer { Name = "oben" };

        stack.Layers.Add(unten);
        stack.Layers.Add(mitte);
        stack.Layers.Add(oben);

        // In der Liste steht "oben" zuoberst. Die unterste Zeile darueber zu ziehen
        // heisst: ganz nach oben, also an das Ende des Stapels.
        Check.That(panel.Reorder(unten, oben, above: true), "der Zug bewirkt etwas");

        Check.That(ReferenceEquals(stack.Layers[^1], unten),
                   "ueber die oberste Zeile gezogen landet sie auf dem hoechsten Platz",
                   string.Join(", ", stack.Layers.Select(l => l.Name)));

        // Und darunter gezogen landet sie eine Stelle tiefer.
        Check.That(panel.Reorder(unten, mitte, above: false), "und andersherum auch");
        Check.That(ReferenceEquals(stack.Layers[0], unten),
                   "unter die unterste Zeile gezogen landet sie zuunterst",
                   string.Join(", ", stack.Layers.Select(l => l.Name)));

        // Eine Gruppe darf nicht in sich selbst - der Stapel waere danach ein Ring.
        var gruppe = new ImageLayer { Name = "Gruppe", Content = LayerContent.Group };
        var kind = new ImageLayer { Name = "Kind" };

        gruppe.Children.Add(kind);
        stack.Layers.Add(gruppe);

        Check.That(!panel.Reorder(gruppe, kind, above: true),
                   "eine Gruppe wandert nicht in sich selbst");
        Check.That(ReferenceEquals(gruppe.Children[0], kind), "und ihr Kind bleibt, wo es war");

        Check.That(!panel.Reorder(kind, kind, above: true), "und nichts landet auf sich selbst");
    }

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

        // Ein SCHALTER und kein Knopf: Die Schnittmaske ist eine Eigenschaft der
        // Ebene, und wer sie nicht ablesen kann, sieht bei jedem Wechsel der Auswahl
        // im Bild nach.
        var clip = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ClipButton");

        Check.That(clip.IsEnabled, "auf der oberen Ebene geht es");
        Check.That(clip.IsChecked != true, "und steht zunaechst offen");

        clip.IsChecked = true;
        Check.That(panel.Stack.Layers[1].Clipped, "und setzt die Schnittmaske");

        clip.IsChecked = false;
        Check.That(!panel.Stack.Layers[1].Clipped, "umgelegt nimmt sie wieder zurueck");

        // Und er zeigt, was an der Ebene steht - nicht, was zuletzt geklickt wurde.
        panel.Stack.Layers[1].Clipped = true;
        Select(panel, panel.Stack.Layers[0]);
        Select(panel, panel.Stack.Layers[1]);

        Check.That(clip.IsChecked == true,
                   "und liest den Stand aus der Ebene, nicht aus sich selbst");

        panel.Stack.Layers[1].Clipped = false;
        Select(panel, panel.Stack.Layers[0]);
        Select(panel, panel.Stack.Layers[1]);

        // Nach ganz unten geschoben kann sie sich an nichts mehr anschneiden.
        Click(panel, "DownButton");
        Check.That(!clip.IsEnabled, "auf der untersten Ebene ist er gesperrt");
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

        // Worauf die Maske wirkt.
        //
        // Eine NEU gewaehlte Maske auf einer Bildebene begrenzt die Korrektur, nicht
        // die Sichtbarkeit. Das ist die Falle, um die es ging: Wer sich einen Fleck
        // auf sein Bild malt, will dort etwas aendern - nicht alles ausser dem Fleck
        // verlieren.
        var scopeRow = (FrameworkElement)panel.FindName("MaskScopeRow");
        var asColour = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ScopeColourButton");
        var asShow = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ScopeShowButton");

        Check.That(scopeRow.Visibility == Visibility.Visible,
                   "an einer Bildebene steht die Wahl bereit");

        Check.That(layer.Mask.Scope == MaskScope.Colour,
                   "und eine neu gewaehlte Maske begrenzt die Farbe",
                   layer.Mask.Scope.ToString());

        Check.That(asColour.IsChecked == true, "der Knopf zeigt es auch");

        changes = 0;
        asShow.IsChecked = true;

        Check.That(changes > 0, "das Umschalten meldet", $"{changes}");
        Check.That(layer.Mask.Scope == MaskScope.Visibility,
                   "und die andere Lesart laesst sich waehlen - ein Glanz braucht sie");

        // Der Abbruch am Regler haengt am STIL und nicht an einzelnen Reglern. Wer
        // ihn dort loest, verliert ihn ueberall auf einmal - und zwar lautlos.
        Check.That(FrameFlip.Views.SliderGuard.GetCancelOnRightClick(low),
                   "jeder Regler des Streifens laesst sich mit rechts abbrechen");

        // Und die Vorlage muss sich wirklich aufbauen lassen.
        //
        // Ein Auslöser, der auf eine Bewegung mit falschem Namen zeigt, ist im XAML
        // kein Fehler: Er faellt erst auf, wenn die Vorlage entsteht - also beim
        // ersten Regler auf dem Bildschirm und nicht im Compiler und nicht in einer
        // Probe, die nur Werte setzt.
        low.ApplyTemplate();

        Check.That(low.Template.FindName("PART_Track", low) is not null,
                   "und die Reglervorlage baut sich auf, samt Griff und Bewegungen");

        // Tiefen, Mitten, Lichter: drei Knoepfe, die die Regler darueber stellen.
        //
        // Sie sind der eigentliche Grund, warum es sie gibt - wer "nur die Schatten"
        // will, weiss nicht, dass das "von 0 bis 0,35 mit weicher Kante" heisst. Sie
        // duerfen deshalb nicht nur die Ebene setzen, sondern muessen auch die Regler
        // nachziehen: Ein Knopf, nach dem die Anzeige etwas anderes behauptet als das
        // Bild zeigt, ist schlimmer als keiner.
        box.SelectedIndex = IndexOf(box, "S_MaskLuminance");

        var zones = (FrameworkElement)panel.FindName("MaskZoneRow");

        Check.That(zones.Visibility == Visibility.Visible,
                   "an einer Helligkeit stehen Tiefen, Mitten und Lichter bereit");

        changes = 0;
        Press(panel, zones, "lights");

        Check.That(changes > 0, "ein Bereichsknopf meldet", $"{changes}");
        Check.Near(layer.Mask.Low, 0.65, 0.001, "Lichter fangen oben an");
        Check.Near(layer.Mask.High, 1.0, 0.001, "und reichen bis ganz hinauf");
        Check.Near(low.Value, 0.65, 0.001, "und der Regler zeigt es auch");

        Press(panel, zones, "shadows");

        Check.Near(layer.Mask.High, 0.35, 0.001, "Tiefen hoeren unten auf");
        Check.That(layer.Mask.Softness > 0.01f, "und haben eine weiche Kante",
                   $"{layer.Mask.Softness:0.00}");

        // Farbbereich: Farbton und Weite statt eines Helligkeitsfensters.
        var colour = (FrameworkElement)panel.FindName("MaskColourBody");

        box.SelectedIndex = IndexOf(box, "S_MaskColour");

        Check.That(layer.Mask.Kind == MaskKind.Colour, "der Farbbereich laesst sich waehlen");
        Check.That(colour.Visibility == Visibility.Visible, "seine Regler zeigen sich");
        Check.That(zones.Visibility != Visibility.Visible,
                   "Tiefen und Mitten nicht - an einer Farbe heissen sie nichts");

        // Die Weichheit bleibt: Beim Farbbereich zaehlt sie in Grad um den Farbton.
        Check.That(soft.Visibility == Visibility.Visible,
                   "die Weichheit bleibt - hier zaehlt sie in Grad");

        changes = 0;
        var hue = (Slider)panel.FindName("MaskHueSlider");
        hue.Value = 240;

        Check.That(changes > 0, "der Farbton meldet", $"{changes}");
        Check.Near(layer.Mask.Hue, 240, 0.5, "und kommt an");

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

    /// <summary>
    /// Gruppen in der Liste: einruecken, ausruecken, und was die Knoepfe dabei
    /// zulassen.
    ///
    /// Die Liste ist flach, der Stapel ist ein Baum. Alles, was auf eine Ebene
    /// zeigt - verschieben, entfernen, verdoppeln -, muss deshalb den richtigen Ast
    /// treffen. Ein Verschieben, das versehentlich auf der obersten Liste arbeitet,
    /// zieht eine Ebene aus ihrer Gruppe heraus, ohne dass jemand es verlangt hat.
    /// </summary>
    private static void GroupsNestInTheList(LayerPanel panel)
    {
        Check.Group("Gruppen schachteln sich in der Liste");

        panel.Load(Passes(), new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "Diffus" },
                new ImageLayer { Source = "ViewLayer.GlossDir", Name = "Glanz", Mode = BlendMode.Add },
            },
        });

        var list = (ListBox)panel.FindName("LayerList");

        // Eine Gruppe nimmt die gewaehlte Ebene gleich mit hinein.
        panel.AddGroup();

        Check.That(panel.Stack.Layers.Count == 2, "oben stehen noch zwei Zeilen",
                   $"{panel.Stack.Layers.Count}");

        var group = panel.Stack.Layers[1];
        Check.That(group.Content == LayerContent.Group, "die obere ist die Gruppe");
        Check.That(group.Children.Count == 1 && group.Children[0].Name == "Glanz",
                   "und der Glanz steckt darin");

        // Die Liste zeigt drei Zeilen: Gruppe, ihr Kind, die uebrige Ebene.
        Check.That(list.Items.Count == 3, "die Liste zeigt alle drei", $"{list.Items.Count}");

        var first = (ListBoxItem)list.Items[0];
        Check.That(ReferenceEquals(first.Tag, group), "die Gruppe steht ueber ihrem Kind");

        var second = (ListBoxItem)list.Items[1];
        Check.That(ReferenceEquals(second.Tag, group.Children[0]), "danach ihr Kind");

        // Die diffuse Ebene einruecken - die Gruppe steht in der Liste ueber ihr.
        var diffuse = panel.Stack.Layers[0];
        Select(panel, diffuse);

        var indent = (Button)panel.FindName("IndentButton");
        Check.That(indent.IsEnabled, "einruecken geht, wenn eine Gruppe darueber steht");

        Click(panel, "IndentButton");

        Check.That(group.Children.Count == 2, "jetzt stecken beide in der Gruppe",
                   $"{group.Children.Count}");
        Check.That(panel.Stack.Layers.Count == 1, "und oben steht nur noch die Gruppe",
                   $"{panel.Stack.Layers.Count}");

        // Verschieben bleibt im eigenen Zweig.
        Select(panel, group.Children[0]);
        var up = (Button)panel.FindName("UpButton");
        Check.That(up.IsEnabled, "in der Gruppe laesst sich verschieben");

        Click(panel, "UpButton");
        Check.That(group.Children.Count == 2, "die Ebene bleibt in der Gruppe",
                   $"{group.Children.Count}");
        Check.That(panel.Stack.Layers.Count == 1, "und rutscht nicht nach draussen");

        // Ausruecken setzt sie ueber die Gruppe.
        Select(panel, group.Children[1]);
        var outdent = (Button)panel.FindName("OutdentButton");
        Check.That(outdent.IsEnabled, "ausruecken geht aus einer Gruppe heraus");

        Click(panel, "OutdentButton");

        Check.That(panel.Stack.Layers.Count == 2, "sie steht wieder ausserhalb",
                   $"{panel.Stack.Layers.Count}");
        Check.That(group.Children.Count == 1, "und fehlt in der Gruppe",
                   $"{group.Children.Count}");

        // Direkt unter die Gruppe - der Umkehrschritt zum Einruecken.
        Check.That(ReferenceEquals(panel.Stack.Layers[1], group),
                   "die Gruppe liegt weiter oben");

        // Ausserhalb einer Gruppe geht kein Ausruecken mehr.
        Select(panel, panel.Stack.Layers[0]);
        Check.That(!outdent.IsEnabled, "ausserhalb einer Gruppe ist der Knopf gesperrt");

        // Eine Gruppe laesst sich nicht anschneiden - sie ist keine Ebene, an die
        // sich etwas anschneidet.
        Select(panel, group);
        var clip = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ClipButton");
        Check.That(!clip.IsEnabled, "eine Gruppe nimmt keine Schnittmaske");
    }

    /// <summary>Waehlt eine Ebene so aus, wie ein Klick in die Liste es taete.</summary>
    private static void Select(LayerPanel panel, ImageLayer layer)
    {
        var list = (ListBox)panel.FindName("LayerList");

        foreach (ListBoxItem item in list.Items)
        {
            if (!ReferenceEquals(item.Tag, layer)) continue;

            item.IsSelected = true;
            panel.UpdateLayout();
            return;
        }
    }

    /// <summary>Der Platz eines Eintrags in der Auswahl, ueber seinen uebersetzten Namen.</summary>
    /// <summary>Drueckt den Knopf mit dieser Kennung - so, wie ein Klick es taete.</summary>
    private static void Press(LayerPanel panel, FrameworkElement row, string tag)
    {
        var button = ((Panel)row).Children.OfType<Button>()
                                 .First(b => (string)b.Tag == tag);

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

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
