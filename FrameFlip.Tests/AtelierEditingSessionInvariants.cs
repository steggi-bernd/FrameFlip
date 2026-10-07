using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Tests;

/// <summary>
/// Die Bearbeitungssitzung des Ateliers ohne Fenster: Sie schreibt in dieselben Felder der
/// Einstellungen wie bisher, meldet jede Aenderung und weiss, ob seit dem letzten
/// Festhalten etwas dazukam.
/// </summary>
public static class AtelierEditingSessionInvariants
{
    public static void Run()
    {
        TheTargetLivesBesideTheRecipe();

        Check.Group("Bearbeitungssitzung: Rezept, Meldung, ungespeichert");

        var settings = new AppSettings();
        var session = new AtelierEditingSession(new SettingsRecipeStore(settings));

        int changes = 0;
        session.Changed += () => changes++;

        var grading = new GradingStack();
        var layers = new LayerStack();
        var adjustments = new ImageAdjustments { Exposure = 0.4 };

        session.Adjustments = adjustments;
        session.Grading = grading;
        session.Layers = layers;
        session.Nodes = "{\"graph\":1}";

        Check.That(ReferenceEquals(settings.Adjustments, adjustments) && ReferenceEquals(settings.Grading, grading) &&
                   ReferenceEquals(settings.Layers, layers) && settings.AtelierNodes == "{\"graph\":1}",
                   "geschrieben wird in dieselben Felder der Einstellungen wie bisher");

        Check.That(ReferenceEquals(session.Grading, grading) && session.Nodes == settings.AtelierNodes,
                   "und gelesen aus ihnen");

        // Was in den Einstellungen steht, gilt auch - etwa ein Rezept, das beim Start geladen wurde.
        settings.AtelierNodes = null;
        Check.That(session.Nodes is null, "die Sitzung haelt keine zweite Kopie neben den Einstellungen");

        Check.That(changes == 4 && session.Dirty && session.Revision == 4,
                   "jede Aenderung wird gemeldet und gezaehlt", $"{changes} Meldungen, Stand {session.Revision}");

        // Festhalten: nur der Stand, der festgehalten wurde.
        long saving = session.Revision;
        session.Layers = layers;
        session.MarkKept(saving);

        Check.That(session.Dirty, "kam waehrend des Speicherns etwas dazu, bleibt es ungespeichert");

        session.MarkKept(session.Revision);
        Check.That(!session.Dirty, "der letzte Stand festgehalten: nichts mehr ungespeichert");

        session.Grading = new GradingStack();
        Check.That(session.Dirty, "die naechste Aenderung macht es wieder ungespeichert");
    }

    /// <summary>
    /// Das Bearbeitungsziel (docs/Atelier-Arbeitsablauf.md, Phase B): beginnt beim Bild, meldet
    /// jeden Wechsel einmal, ist kein Teil des Rezepts und faellt mit einem neuen Rezept aufs
    /// Bild zurueck.
    /// </summary>
    private static void TheTargetLivesBesideTheRecipe()
    {
        Check.Group("Bearbeitungssitzung: das Ziel neben dem Rezept");

        var session = new AtelierEditingSession(new SettingsRecipeStore(new AppSettings()));
        int moved = 0, changed = 0;
        session.TargetChanged += () => moved++;
        session.Changed += () => changed++;

        Check.That(ReferenceEquals(session.Target, EditingTarget.Picture), "eine neue Sitzung arbeitet am Bild");

        var layer = new ImageLayer { Content = LayerContent.Adjustment };
        session.Focus(new EditingTarget.StackLayer(layer, Tools: true));
        session.Focus(new EditingTarget.StackLayer(layer, Tools: true));

        Check.That(session.Target is EditingTarget.StackLayer { Tools: true } chosen && ReferenceEquals(chosen.Layer, layer) && moved == 1,
                   "eine Ebene gewaehlt: das Ziel wechselt, dasselbe Ziel noch einmal meldet nichts");
        Check.That(changed == 0 && !session.Dirty, "das Ziel ist kein Teil des Rezepts - es macht nichts ungespeichert");

        // Knoten stehen als Objekt im Ziel: Derselbe Name in einem anderen Graphen ist ein anderer Knoten.
        var first = new LightNode { Id = "n1" };
        var twin = new LightNode { Id = "n1" };
        session.Focus(new EditingTarget.GraphNode(first, FromLayerList: false));
        session.Focus(new EditingTarget.GraphNode(twin, FromLayerList: false));

        Check.That(session.Target is EditingTarget.GraphNode { Node: var node } && ReferenceEquals(node, twin) && moved == 3,
                   "ein Knoten gleichen Namens aus einem anderen Graphen ist ein neues Ziel");

        session.Switch(new SettingsRecipeStore(new AppSettings()));
        Check.That(ReferenceEquals(session.Target, EditingTarget.Picture) && moved == 4,
                   "ein neues Rezept: das Ziel faellt aufs Bild zurueck und meldet es");
    }
}
