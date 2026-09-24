using System.IO;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Die Probe gegen einen echten Render.
///
/// Alles andere in dieser Reihe prueft, ob FrameFlip mit sich selbst
/// uebereinstimmt. Hier wird gegen eine fremde Rechnung geprueft: Blender hat ein
/// Bild in zehn Lichtpasse und drei Farbpasse zerlegt, und der Stapel, den
/// FrameFlip daraus baut, muss wieder genau dieses Bild ergeben.
///
/// Das ist der Test, der eine naheliegende und falsche Annahme aufdeckt. "Alle
/// Passe addieren" klingt richtig und ist es nicht - Cycles zerlegt in Licht MAL
/// Farbe. Waeren die Farbpasse Summanden, laege das Ergebnis hier um ein Vielfaches
/// daneben; multiplizierte man sie ueber den ganzen Stapel statt nur ueber ihr
/// eigenes Licht, waere es zu dunkel. Beides sieht fuer sich genommen nach einem
/// Bild aus.
/// </summary>
public static class PassRebuildInvariants
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(),
                                   "frameflip_passes_" + Guid.NewGuid().ToString("N") + ".exr");

        try
        {
            File.WriteAllBytes(path, ExrPassSample.Bytes());

            var passes = ReadsThePasses(path);
            ReadsASinglePass(path, passes);
            BuildsTheDecomposition(passes);
            RebuildsTheRender(path, passes);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { /* ein liegengebliebener Temp-Rest ist kein Testfehler */ }
        }
    }

    private static IReadOnlyList<ExrPass> ReadsThePasses(string path)
    {
        Check.Group("Passe eines echten Renders");

        var passes = ExrPasses.Of(path);

        Check.That(passes.Count == 14, "vierzehn Passe stehen in der Datei", $"{passes.Count}");

        foreach (string wanted in new[] { "Combined", "DiffDir", "DiffInd", "DiffCol",
                                          "GlossDir", "GlossInd", "GlossCol",
                                          "TransDir", "TransInd", "TransCol",
                                          "VolumeDir", "VolumeInd", "Emit", "Env" })
        {
            Check.That(passes.Any(p => p.ShortName == wanted), $"{wanted} ist dabei");
        }

        // Die Ansichtsebene heisst "ViewLayer" - der Kurzname muss sie abstreifen,
        // sonst steht in einer 300 Punkte breiten Liste vierzehnmal dasselbe davor.
        var combined = passes.First(p => p.ShortName == "Combined");
        Check.That(combined.Name == "ViewLayer.Combined", "der volle Name bleibt erhalten",
                   combined.Name);
        Check.That(combined.Alpha is not null, "Combined fuehrt eine Deckung");

        return passes;
    }

    /// <summary>
    /// Einen einzelnen Pass lesen - und zwar wirklich nur dessen Kanaele.
    ///
    /// Bei einer Datei mit vierzehn Passen ist das der Unterschied zwischen drei
    /// Kanaelen und dreiundvierzig. Geprueft wird ueber den Inhalt: Der Farbpass des
    /// Bodens muss gruenlich sein, der Emissionspass fast ueberall schwarz.
    /// </summary>
    private static void ReadsASinglePass(string path, IReadOnlyList<ExrPass> passes)
    {
        Check.Group("Einen einzelnen Pass lesen");

        var diffuseColour = FloatFrame.FromExrPass(path, "ViewLayer.DiffCol");

        Check.That(diffuseColour is not null, "der Farbpass laesst sich lesen");
        if (diffuseColour is null) return;

        Check.That(diffuseColour.Width == ExrPassSample.Width &&
                   diffuseColour.Height == ExrPassSample.Height,
                   "in der Groesse der Datei", $"{diffuseColour.Width}x{diffuseColour.Height}");

        Check.That(diffuseColour.Layer == "ViewLayer.DiffCol", "und weiss, woher er kommt");

        // Der Boden ist gruen eingestellt. Ueber das ganze Bild gemittelt muss
        // deshalb Gruen vor Rot liegen - waeren die Kanaele vertauscht, stuende es
        // andersherum.
        float red = diffuseColour.R.Average();
        float green = diffuseColour.G.Average();

        Check.That(green > red, "der gruene Boden faerbt den Farbpass gruen",
                   $"R {red:0.###}, G {green:0.###}");

        // Ein Pass, den es nicht gibt, ergibt nichts - und keinen Abbruch.
        Check.That(FloatFrame.FromExrPass(path, "ViewLayer.GibtEsNicht") is null,
                   "ein Pass, den es nicht gibt, ergibt null");

        // Ueber den Kurznamen ebenfalls, damit ein Rezept eine Umbenennung in
        // Blender uebersteht.
        Check.That(FloatFrame.FromExrPass(path, "AndereEbene.GlossCol") is not null,
                   "und ueber den Kurznamen findet er sich trotzdem");
    }

    /// <summary>Der Aufbau des Stapels - noch ohne zu rechnen.</summary>
    private static void BuildsTheDecomposition(IReadOnlyList<ExrPass> passes)
    {
        Check.Group("Der Stapel bildet die Zerlegung ab");

        var stack = PassStack.Rebuild(passes);

        // Zehn Lichtpasse, und sechs davon bekommen ihre Farbe angeschnitten:
        // Diff, Gloss und Trans je zweimal, Volume gar nicht.
        Check.That(stack.Layers.Count == 16, "sechzehn Ebenen", $"{stack.Layers.Count}");

        Check.That(stack.Layers.Count(l => l.Clipped) == 6,
                   "sechs davon sind angeschnittene Farbpasse",
                   $"{stack.Layers.Count(l => l.Clipped)}");

        Check.That(stack.Layers.All(l => !l.Name.EndsWith("Col") || l.Clipped),
                   "kein Farbpass steht fuer sich");

        Check.That(stack.Layers.All(l => !l.Clipped || l.Mode == BlendMode.Multiply),
                   "angeschnitten wird multipliziert");

        Check.That(stack.Layers.All(l => l.Name != "Combined"),
                   "das fertige Bild gehoert nicht in seine eigene Summe");

        // Volume hat keinen Farbpass - dass kein Partner gefunden wird, ist kein
        // Fehler, sondern der Normalfall fuer diese beiden.
        int volumeAt = stack.Layers.FindIndex(l => l.Name == "VolumeDir");
        Check.That(volumeAt >= 0, "VolumeDir ist im Stapel");

        if (volumeAt >= 0 && volumeAt + 1 < stack.Layers.Count)
            Check.That(!stack.Layers[volumeAt + 1].Clipped,
                       "und bekommt nichts angeschnitten, weil es dazu nichts gibt");

        Check.That(stack.Layers[0].Mode == BlendMode.Normal, "die unterste traegt");
        Check.That(stack.Layers.Skip(1).All(l => l.Mode != BlendMode.Normal),
                   "alle uebrigen kommen dazu oder multiplizieren");
    }

    /// <summary>
    /// Die eigentliche Probe: zusammensetzen und mit Blenders eigenem Ergebnis
    /// vergleichen.
    /// </summary>
    private static void RebuildsTheRender(string path, IReadOnlyList<ExrPass> passes)
    {
        Check.Group("Der Stapel ergibt wieder den Render");

        var stack = PassStack.Rebuild(passes);

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);
        foreach (string source in stack.Layers.Select(l => l.Source).Distinct(StringComparer.Ordinal))
        {
            var frame = FloatFrame.FromExrPass(path, source);
            if (frame is not null) sources[source] = frame;
        }

        Check.That(sources.Count == 13, "dreizehn verschiedene Passe werden gelesen",
                   $"{sources.Count}");

        var built = LayerComposer.Compose(stack, sources);
        var rendered = FloatFrame.FromExrPass(path, "ViewLayer.Combined");

        Check.That(built is not null && rendered is not null, "beide Bilder stehen");
        if (built is null || rendered is null) return;

        // Gemessen wird relativ, mit einem Boden: Bei einem Wert von 0,0005 sagt ein
        // relativer Fehler nichts mehr, und die Speicherung in sechzehn Bit traegt
        // ohnehin nur drei bis vier Stellen.
        float worst = 0f;
        int at = -1;

        for (int i = 0; i < built.PixelCount; i++)
        {
            foreach (var (a, b) in new[]
                     {
                         (built.R[i], rendered.R[i]),
                         (built.G[i], rendered.G[i]),
                         (built.B[i], rendered.B[i]),
                     })
            {
                float off = MathF.Abs(a - b) / MathF.Max(0.01f, MathF.Abs(b));
                if (off > worst)
                {
                    worst = off;
                    at = i;
                }
            }
        }

        // Die Schranke steht bei einem halben Prozent, gemessen wurden 0,09 %.
        // Das ist nicht die Genauigkeit der Rechnung, sondern die der Speicherung:
        // Sechzehn Bit je Kanal tragen drei bis vier Stellen, und das Ergebnis ist
        // eine Summe aus zehn davon. Waere ein Summand vergessen oder ein Faktor
        // falsch angesetzt, laege die Abweichung bei Prozenten statt Promille.
        Check.That(worst < 0.005f, "das Ergebnis entspricht dem Render",
                   $"groesste Abweichung {worst * 100:0.##} % bei Punkt {at}");

        // Und die Gegenprobe, die zeigt, dass der Test etwas misst: Werden die
        // Farbpasse als Summanden behandelt statt als Faktoren - die naheliegende
        // und falsche Lesart -, muss das Ergebnis deutlich danebenliegen.
        var naive = new LayerStack
        {
            Layers = stack.Layers.Select(l =>
            {
                var copy = l.Clone();
                copy.Clipped = false;
                copy.Mode = copy.Mode == BlendMode.Normal ? BlendMode.Normal : BlendMode.Add;
                return copy;
            }).ToList(),
        };

        var wrong = LayerComposer.Compose(naive, sources);
        Check.That(wrong is not null, "auch die falsche Lesart ergibt ein Bild");

        if (wrong is not null)
        {
            double sumBuilt = 0, sumWrong = 0;
            for (int i = 0; i < built.PixelCount; i++)
            {
                sumBuilt += built.R[i] + built.G[i] + built.B[i];
                sumWrong += wrong.R[i] + wrong.G[i] + wrong.B[i];
            }

            Check.That(sumWrong > sumBuilt * 1.2, "sie liegt deutlich daneben",
                       $"{sumWrong:0.#} gegen {sumBuilt:0.#}");
        }
    }
}
