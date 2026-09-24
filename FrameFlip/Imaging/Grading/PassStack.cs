using FrameFlip.Decoding.Exr;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Baut aus den Passen einer Datei den Stapel, der wieder das gerenderte Bild ergibt.
///
/// Die naheliegende Annahme - "alle Passe addieren" - ist falsch, und zwar auf eine
/// Art, die man dem Ergebnis nicht ansieht. Cycles zerlegt nicht in Summanden,
/// sondern in Licht und Farbe:
///
///     Combined = (DiffDir + DiffInd) * DiffCol
///              + (GlossDir + GlossInd) * GlossCol
///              + (TransDir + TransInd) * TransCol
///              + (VolumeDir + VolumeInd)
///              + Emit + Env
///
/// Die Farbpasse sind Faktoren. Wer sie mitaddierte, bekaeme ein zu helles Bild;
/// wer sie ueber den ganzen Stapel multiplizierte, ein zu dunkles. Beides sieht
/// plausibel aus - und genau deshalb steht die Regel hier aufgeschrieben, statt
/// jedem selbst ueberlassen zu bleiben.
///
/// Die Beschraenkung eines Farbpasses auf sein Licht ist dieselbe Sache, die in
/// Photoshop Schnittmaske heisst: <see cref="ImageLayer.Clipped"/>.
/// </summary>
public static class PassStack
{
    /// <summary>
    /// Der Stapel, der die Zerlegung wieder zusammensetzt. Leer, wenn die Datei
    /// keine Lichtpasse fuehrt - dann gibt es nichts zu rekonstruieren.
    /// </summary>
    public static LayerStack Rebuild(IReadOnlyList<ExrPass> passes)
    {
        var stack = new LayerStack();
        var colours = passes.Where(p => !p.Grey).ToList();

        foreach (var pass in colours)
        {
            string leaf = pass.ShortName;
            if (!IsLight(leaf)) continue;

            stack.Layers.Add(new ImageLayer
            {
                Source = pass.Name,
                Name = leaf,

                // Die unterste traegt; alles Weitere kommt dazu. Auf Schwarz sind
                // Normal und Addieren dasselbe, aber wer die Liste liest, soll die
                // unterste als Grundlage erkennen.
                Mode = stack.Layers.Count == 0 ? BlendMode.Normal : BlendMode.Add,
            });

            // Das Licht bekommt seine Farbe - und nur sein Licht, nicht den Rest
            // des Stapels.
            string? partner = ColourFor(leaf);
            if (partner is null) continue;

            var colour = colours.FirstOrDefault(
                p => p.ShortName.Equals(partner, StringComparison.OrdinalIgnoreCase));

            if (colour.Red is null) continue;

            stack.Layers.Add(new ImageLayer
            {
                Source = colour.Name,
                Name = colour.ShortName,
                Mode = BlendMode.Multiply,
                Clipped = true,
            });
        }

        return stack;
    }

    /// <summary>
    /// Ein Pass, der Licht traegt und damit in die Summe gehoert.
    ///
    /// "Dir" und "Ind" sind direktes und indirektes Licht; Emission und Umgebung
    /// kommen ohne Farbpass und stehen fuer sich. Alles Uebrige - Farbe, Tiefe,
    /// Verschattung, Kryptomatten - ist entweder Faktor oder gar kein Licht und
    /// gehoert nicht in die Summe.
    /// </summary>
    private static bool IsLight(string leaf)
        => leaf.EndsWith("Dir", StringComparison.OrdinalIgnoreCase) ||
           leaf.EndsWith("Ind", StringComparison.OrdinalIgnoreCase) ||
           leaf.Equals("Emit", StringComparison.OrdinalIgnoreCase) ||
           leaf.Equals("Env", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Der Farbpass zu einem Lichtpass, oder null. "GlossDir" gehoert zu
    /// "GlossCol"; "VolumeDir" hat keinen, und das ist kein Fehler.
    /// </summary>
    private static string? ColourFor(string leaf)
        => leaf.Length > 3 &&
           (leaf.EndsWith("Dir", StringComparison.OrdinalIgnoreCase) ||
            leaf.EndsWith("Ind", StringComparison.OrdinalIgnoreCase))
            ? leaf[..^3] + "Col"
            : null;
}
