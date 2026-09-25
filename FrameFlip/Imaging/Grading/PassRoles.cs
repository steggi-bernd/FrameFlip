using System.Text.RegularExpressions;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Mit welchem Mischmodus eine NEUE Ebene aus einem Pass oder einer Bilddatei anfaengt.
///
/// Glare, Bloom, Emission: Solche Passe liegen auf Schwarz, und Schwarz heisst dort
/// "hier kommt nichts dazu". Auf Normal gelegt deckten sie das Bild darunter schwarz
/// zu, und man musste jedes Mal den Modus suchen, den der Name schon verraet. Dieselbe
/// Kenntnis steckt in <see cref="PassStack"/>: Licht kommt dazu, Farbe multipliziert.
///
/// NUR BEIM ANLEGEN. Danach ist der Modus ein Wert wie jeder andere: Nichts hier wird
/// beim Laden, Umwandeln oder Verdoppeln erneut gefragt, und was jemand von Hand
/// umgestellt hat, bleibt so. Eine Erkennung, die spaeter noch einmal zuschluege,
/// wuerde genau die Entscheidung ueberschreiben, die ein Mensch getroffen hat.
/// </summary>
public static class PassRoles
{
    /// <summary>Was zum Bild dazukommt - Licht auf Schwarz.</summary>
    private static readonly HashSet<string> Adding = new(StringComparer.OrdinalIgnoreCase)
    {
        "glare", "bloom", "glow", "flare", "flares", "streak", "streaks", "ghost", "ghosts",
        "godray", "godrays", "emit", "emission", "env", "environment", "spec", "specular",
        // Die Lichtpasse von Cycles: DiffDir, GlossInd, TransDir, VolumeInd ...
        "dir", "ind",
    };

    /// <summary>Was das Bild abdunkelt - Faktoren zwischen Schwarz und Weiss.</summary>
    private static readonly HashSet<string> Darkening = new(StringComparer.OrdinalIgnoreCase)
    {
        "ao", "occlusion", "shadow", "shadows",
        // Die Farbpasse von Cycles: DiffCol, GlossCol, TransCol.
        "col",
    };

    /// <summary>
    /// Der Modus, den der Name verraet - oder null, wenn er nichts verraet.
    /// </summary>
    /// <param name="name">Ein Passname wie "ViewLayer.Glare" oder ein Dateipfad wie "fx/glare_0012.png".</param>
    /// <param name="sceneLinear">
    /// True fuer Szenenlicht (EXR): Dort wird addiert. Anzeigewerte (PNG, JPG) werden
    /// negativ multipliziert - die Summe zweier heller Anzeigewerte liefe ueber Weiss
    /// hinaus und wuerde hart abgeschnitten.
    /// </param>
    public static BlendMode? ByName(string name, bool sceneLinear)
    {
        foreach (string word in Words(name))
        {
            if (Adding.Contains(word)) return sceneLinear ? BlendMode.Add : BlendMode.Screen;
            if (Darkening.Contains(word)) return BlendMode.Multiply;
        }

        return null;
    }

    /// <summary>
    /// Die Zweitpruefung am Inhalt, wenn der Name nichts sagt: Ein Bild, das zum groessten
    /// Teil schwarz ist und keine Deckung mitbringt, ist Licht auf Schwarz. Ein Bild MIT
    /// Deckung ist dagegen etwas zum Auflegen, ein Logo, ein Schriftzug - das bleibt Normal.
    /// </summary>
    /// <param name="blackShare">Anteil der Bildpunkte, die praktisch schwarz sind.</param>
    public static BlendMode? ByContent(double blackShare, bool hasAlpha, bool sceneLinear)
    {
        if (hasAlpha || blackShare < 0.6) return null;

        return sceneLinear ? BlendMode.Add : BlendMode.Screen;
    }

    /// <summary>Ob eine Datei Szenenlicht traegt - bei FrameFlip heisst das: eine EXR.</summary>
    public static bool IsSceneLinear(string path)
        => System.IO.Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex Boundary = new("(?<=[a-z])(?=[A-Z])|[^A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Die Woerter eines Namens - Passname oder Dateiname ohne Endung, das letzte zuerst -
    /// zerlegt an Punkten, Trennzeichen, Ziffern und Binnenmajuskeln. "FogGlow"
    /// ergibt "Fog" und "Glow", "glare_0012" ergibt "glare". Ganze Woerter und keine
    /// Teilzeichenketten: "Environment" soll zaehlen, "Collection" nicht als "col".
    /// </summary>
    private static IEnumerable<string> Words(string name)
    {
        string leaf = name;

        // Ein Pfad oder Dateiname: nur der Name, ohne Endung.
        if (leaf.IndexOfAny(new[] { '\\', '/' }) >= 0 ||
            System.IO.Path.GetExtension(leaf).ToLowerInvariant() is ".exr" or ".png" or ".jpg" or ".jpeg"
                                                                  or ".tif" or ".tiff" or ".bmp" or ".webp")
        {
            leaf = System.IO.Path.GetFileNameWithoutExtension(leaf);
        }

        // Von hinten nach vorn: Bei "ViewLayer.Glare" ist der Pass das letzte Glied, und
        // eine Ansichtsebene, die zufaellig "Shadows" heisst, soll ihn nicht ueberstimmen.
        // Nur das letzte Glied zu nehmen ginge bei Dateien schief - bei "Bloom.0012" ist
        // es die Bildnummer.
        return Boundary.Split(leaf).Where(w => w.Length > 0).Reverse();
    }
}
