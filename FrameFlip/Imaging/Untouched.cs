using FrameFlip.Imaging.Grading;
using FrameFlip.Imaging.Nodes;

namespace FrameFlip.Imaging;

/// <summary>
/// Das Original zum Vergleichen: dieselben Ebenen, eingemischt wie sonst - Mischart,
/// Deckkraft, Masken, Schnittmasken, Gruppen, Lage, Deckungsschleier -, aber ohne das, was
/// man selbst an Farbe und Licht gedreht hat. Es entfallen die Korrekturen an den Ebenen und
/// in den Einstellungsebenen, Belichtung und Toenung je Ebene und alles am Gesamtbild.
///
/// Vorher zeigte "Original" das Bild der Datei ohne die Ebenen. Wer sein Bild aus Passen
/// zusammensetzt, sah dann etwas anderes als sein Bild ohne Korrektur - mit anderer Farbe,
/// weil die Mischung fehlte. Die Mischung ist aber kein Eingriff in die Farbe, sondern der
/// Aufbau des Bildes; sie bleibt.
///
/// Eine Einstellungsebene wird neutral und nicht ausgeblendet: So bleibt jede Schnittmaske
/// an ihrem Traeger, und eine neutrale Korrektur auf Normal laesst das Bild, wie es ist.
/// </summary>
public static class Untouched
{
    /// <summary>Eine Kopie des Stapels ohne eigene Korrekturen. Der Stapel selbst bleibt.</summary>
    public static LayerStack Of(LayerStack stack)
    {
        var plain = stack.Clone();

        foreach (var layer in plain.All()) Strip(layer);

        return plain;
    }

    private static void Strip(ImageLayer layer)
    {
        layer.Adjustments = layer.Content == LayerContent.Adjustment ? ImageAdjustments.Neutral : null;
        layer.Tools = null;
        layer.Exposure = 0f;
        layer.Tint = new ColourTriplet(1, 1, 1);
    }

    /// <summary>
    /// Eine Kopie des Graphen, in der alles stumm ist, was die Farbe aendert - an den Ebenen
    /// und am Gesamtbild. Mischen, Platzieren, Masken, Begrenzen und die Sichtumwandlung
    /// bleiben. Ein stummer Knoten reicht sein Bild unveraendert durch.
    /// </summary>
    public static NodeGraph Of(NodeGraph graph)
    {
        var plain = graph.Clone();

        foreach (var node in plain.Nodes)
        {
            switch (node)
            {
                case LayerGradeNode or ExposureTintNode or LightNode or ToneNode or PointToolNode or LocalNode
                    or OpticsNode or GeometryNode or DataNode or FramePassNode:
                    node.Muted = true;
                    break;

                case OverlayNode overlay:
                    overlay.Exposure = 0f;
                    overlay.Tint = new ColourTriplet(1, 1, 1);
                    break;
            }
        }

        return plain;
    }
}
