using FrameFlip.Atelier;
using FrameFlip.Configuration;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

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
}
