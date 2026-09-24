using System.IO;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Kryptomatten gegen einen echten Render.
///
/// Hier wird nichts nachgebaut und nichts angenommen: Blender hat drei benannte
/// Objekte gerendert und dazu geschrieben, welcher Name welchen Hash hat. Wenn
/// FrameFlip die Datei richtig liest, muessen die Kennungen IM BILD genau die aus
/// dem Manifest sein - und das ist eine Behauptung, die man nicht aus Versehen
/// erfuellt.
/// </summary>
public static class CryptomatteInvariants
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(),
                                   "frameflip_crypto_" + Guid.NewGuid().ToString("N") + ".exr");

        try
        {
            File.WriteAllBytes(path, CryptoSample.Bytes());

            LowercaseChannels(path);
            TheManifestIsRead(path);
            TheLevelsAreFound(path);
            EveryIdInThePictureHasAName(path);
            CoverageAddsUpToOne(path);
            AnObjectBecomesAMask(path);
            TwoPicksAreAUnion(path);
            InTheComposer(path);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { /* ein liegengebliebener Temp-Rest ist kein Testfehler */ }
        }
    }

    /// <summary>
    /// Der Fund, der beim Bauen fast durchgerutscht waere.
    ///
    /// Blender schreibt die Kryptomattenkanaele klein - ".r", ".g", ".b", ".a" -,
    /// waehrend Combined und alle Lichtpasse derselben Datei gross geschrieben sind.
    /// Wer nur auf Grossbuchstaben prueft, erkennt ausgerechnet die Kryptomatte
    /// nicht als Farbpass und liest ihren ersten Kanal als Graustufe: Alpha statt
    /// Kennung, und die Maske traefe nie etwas.
    /// </summary>
    private static void LowercaseChannels(string path)
    {
        Check.Group("Kleingeschriebene Kanaele werden erkannt");

        var passes = ExrPasses.Of(path);
        var level = ExrPasses.Find(passes, "ViewLayer.CryptoObject00");

        Check.That(level is not null, "die erste Stufe steht in der Passliste");
        if (level is not { } found) return;

        Check.That(!found.Grey, "und gilt als Farbpass, nicht als Graustufe");
        Check.That(found.Red.EndsWith(".r", StringComparison.Ordinal),
                   "der rote Kanal ist der kleingeschriebene", found.Red);
        Check.That(found.Alpha is not null, "und die vierte Lage ist da");
        Check.That(found.Alpha!.EndsWith(".a", StringComparison.Ordinal),
                   "ebenfalls klein", found.Alpha);

        // Zur Gegenprobe: In derselben Datei ist Combined gross geschrieben. Beide
        // Schreibweisen muessen nebeneinander gehen.
        var combined = ExrPasses.Find(passes, "ViewLayer.Combined");
        Check.That(combined is { Grey: false }, "Combined wird weiter gross erkannt");
        Check.That(combined!.Value.Red.EndsWith(".R", StringComparison.Ordinal),
                   "in derselben Datei", combined.Value.Red);
    }

    private static void TheManifestIsRead(string path)
    {
        Check.Group("Das Manifest wird gelesen");

        var sets = Cryptomatte.Of(path);

        Check.That(sets.Count == 2, "zwei Kryptomatten: Objekt und Material", $"{sets.Count}");

        var objects = sets.FirstOrDefault(s => s.ShortName == "CryptoObject");
        Check.That(objects is not null, "die nach Objekt ist dabei");
        if (objects is null) return;

        Check.That(objects.Prefix == "ViewLayer.CryptoObject",
                   "unter dem vollen Namen", objects.Prefix);

        foreach (string wanted in new[] { "Kugel", "Wuerfel", "Kegel", "Boden" })
            Check.That(objects.Names.ContainsKey(wanted), $"{wanted} steht im Manifest");

        var materials = sets.FirstOrDefault(s => s.ShortName == "CryptoMaterial");
        Check.That(materials is not null, "und die nach Material auch");
        Check.That(materials!.Names.ContainsKey("MaterialKugel"),
                   "mit den Materialnamen", string.Join(", ", materials.Names.Keys.Take(4)));

        // Die Kennung ist das umgedeutete Bitmuster des Hashes, nicht seine Zahl.
        // Umgerechnet statt umgedeutet kaeme etwas voellig anderes heraus - und die
        // Maske traefe nie etwas.
        Check.Near(Cryptomatte.IdFromHex("3f800000"), 1.0, 1e-9,
                   "der Hexwert 3f800000 ist die Gleitkommaeins");
    }

    private static void TheLevelsAreFound(string path)
    {
        Check.Group("Die Stufen werden gefunden");

        var passes = ExrPasses.Of(path);
        var levels = Cryptomatte.Levels(passes, "ViewLayer.CryptoObject");

        // Sechs Rangstufen in Blender sind drei Bilder - jedes traegt zwei Paare.
        Check.That(levels.Count == 3, "drei Stufen", $"{levels.Count}");
        Check.That(levels[0].EndsWith("00", StringComparison.Ordinal), "in der Reihenfolge 00");
        Check.That(levels[2].EndsWith("02", StringComparison.Ordinal), "bis 02");

        // Und sie hoeren auf, wo die Datei aufhoert - nicht erst bei der Obergrenze.
        Check.That(Cryptomatte.Levels(passes, "ViewLayer.GibtEsNicht").Count == 0,
                   "ein Satz, den es nicht gibt, hat keine Stufen");

        // Eine Stufe ist als solche erkennbar und gehoert damit nicht in eine
        // Passliste zum Ansehen.
        Check.That(Cryptomatte.IsLevel("CryptoObject00"), "eine Stufe erkennt sich");
        Check.That(!Cryptomatte.IsLevel("GlossDir"), "ein Lichtpass nicht");
    }

    /// <summary>
    /// Die Probe, die alles auf einmal prueft.
    ///
    /// Jede Kennung, die IM BILD steht, muss im Manifest einen Namen haben. Das geht
    /// nur auf, wenn drei Dinge zugleich stimmen: der richtige Kanal gelesen, die
    /// Gleitkommawerte unversehrt durch den Decoder gekommen, und der Hexwert des
    /// Manifests richtig umgedeutet. Faellt eines davon, sind es fremde Zahlen.
    /// </summary>
    private static void EveryIdInThePictureHasAName(string path)
    {
        Check.Group("Jede Kennung im Bild hat einen Namen");

        var set = Cryptomatte.Of(path).First(s => s.ShortName == "CryptoObject");
        var level = FloatFrame.FromExrPass(path, "ViewLayer.CryptoObject00");

        Check.That(level is not null, "die erste Stufe laesst sich lesen");
        if (level is null) return;

        var seen = new HashSet<float>();
        int unknown = 0;

        for (int i = 0; i < level.PixelCount; i++)
        {
            foreach (float id in new[] { level.R[i], level.B[i] })
            {
                if (id == 0f) continue;          // Hintergrund traegt keine Kennung
                if (!seen.Add(id)) continue;

                if (set.NameOf(id) is null) unknown++;
            }
        }

        Check.That(seen.Count >= 3, "mehrere verschiedene Objekte im Bild", $"{seen.Count}");
        Check.That(unknown == 0, "und jede Kennung steht im Manifest",
                   $"{unknown} von {seen.Count} ohne Namen");

        // Umgekehrt: Die Objekte der Szene sind wirklich zu sehen.
        foreach (string wanted in new[] { "Kugel", "Wuerfel", "Kegel" })
        {
            float id = set.Names[wanted];
            Check.That(seen.Contains(id), $"{wanted} kommt im Bild vor");
        }
    }

    /// <summary>
    /// Die Deckungen aller Raenge duerfen zusammen nie mehr als einen Bildpunkt
    /// ergeben - und tun es dort, wo etwas steht, genau.
    ///
    /// Das ist der Pruefstein fuer die Kanalzuordnung. Jede Stufe traegt r/g/b/a als
    /// Kennung, Deckung, Kennung, Deckung; waeren die beiden vertauscht, stuenden
    /// hier Hashwerte in der Summe statt Anteilen, und sie laege bei Millionen statt
    /// bei eins.
    ///
    /// NICHT geprueft wird "ueberall genau eins" - der erste Entwurf tat das und lag
    /// falsch. Wo der Himmel zu sehen ist, gehoert der Bildpunkt zu keinem Objekt,
    /// und die Summe ist null; an der Kante eines Objekts gegen den Himmel liegt sie
    /// dazwischen. Beides ist richtig und hat nichts mit dem Leser zu tun.
    /// </summary>
    private static void CoverageAddsUpToOne(string path)
    {
        Check.Group("Die Deckungen ergeben nie mehr als einen Bildpunkt");

        var levels = new[] { "00", "01", "02" }
            .Select(n => FloatFrame.FromExrPass(path, "ViewLayer.CryptoObject" + n))
            .ToList();

        Check.That(levels.All(l => l is not null), "alle drei Stufen sind lesbar");
        if (levels.Any(l => l is null)) return;

        float most = 0f;
        int whole = 0, empty = 0, edge = 0;

        for (int i = 0; i < levels[0]!.PixelCount; i++)
        {
            float sum = 0f;

            foreach (var level in levels)
            {
                sum += level!.G[i];
                if (level.A is not null) sum += level.A[i];
            }

            most = MathF.Max(most, sum);

            if (sum > 0.99f) whole++;
            else if (sum < 0.01f) empty++;
            else edge++;
        }

        Check.That(most <= 1.01f, "nie mehr als eins", $"groesster Wert {most:0.####}");
        Check.That(whole > 0, "wo ein Objekt steht, genau eins", $"{whole} Bildpunkte");
        Check.That(empty > 0, "wo der Himmel steht, null", $"{empty} Bildpunkte");

        // Die Kante ist der Grund, warum eine Kryptomatte einer Auswahl per Farbe
        // ueberlegen ist - aber sie darf nicht das halbe Bild sein.
        Check.That(edge < whole, "und dazwischen nur die Kanten",
                   $"{edge} Kanten gegen {whole} volle");
    }

    private static void AnObjectBecomesAMask(string path)
    {
        Check.Group("Ein gewaehltes Objekt ergibt eine Maske");

        var set = Cryptomatte.Of(path).First(s => s.ShortName == "CryptoObject");
        var levels = Load(path);

        float kugel = set.Names["Kugel"];
        var ids = new[] { kugel };

        int covered = 0, full = 0, partial = 0;

        for (int i = 0; i < levels[0].PixelCount; i++)
        {
            float coverage = Masking.Coverage(levels, ids, i);

            if (coverage > 0.001f) covered++;
            if (coverage > 0.999f) full++;
            if (coverage > 0.001f && coverage < 0.999f) partial++;
        }

        Check.That(covered > 0, "die Kugel deckt etwas", $"{covered} Bildpunkte");
        Check.That(covered < levels[0].PixelCount, "aber nicht alles",
                   $"{covered} von {levels[0].PixelCount}");
        Check.That(full > 0, "innen deckt sie ganz", $"{full} Bildpunkte");

        // Weiche Kanten sind der Grund, warum eine Kryptomatte einer Auswahl per
        // Farbe ueberlegen ist: Am Rand gehoert ein Bildpunkt teilweise dazu.
        Check.That(partial > 0, "und am Rand nur teilweise", $"{partial} Bildpunkte");

        // Ein Objekt, das es nicht gibt, deckt nichts - und zwar still.
        float nothing = 0f;
        for (int i = 0; i < levels[0].PixelCount; i++)
            nothing += Masking.Coverage(levels, new[] { 12345.678f }, i);

        Check.Near(nothing, 0.0, 1e-5, "eine fremde Kennung deckt nichts");

        // Und ohne Auswahl ebenfalls nichts.
        Check.Near(Masking.Coverage(levels, Array.Empty<float>(), 0), 0.0, 1e-6,
                   "ohne Auswahl auch nicht");
    }

    private static void TwoPicksAreAUnion(string path)
    {
        Check.Group("Zwei gewaehlte Objekte ergeben ihre Vereinigung");

        var set = Cryptomatte.Of(path).First(s => s.ShortName == "CryptoObject");
        var levels = Load(path);

        float kugel = set.Names["Kugel"];
        float kegel = set.Names["Kegel"];

        double alone = 0, other = 0, both = 0;

        for (int i = 0; i < levels[0].PixelCount; i++)
        {
            alone += Masking.Coverage(levels, new[] { kugel }, i);
            other += Masking.Coverage(levels, new[] { kegel }, i);
            both += Masking.Coverage(levels, new[] { kugel, kegel }, i);
        }

        Check.That(alone > 0 && other > 0, "beide decken fuer sich etwas",
                   $"{alone:0.#} / {other:0.#}");

        // Zwei Objekte, die einander nicht verdecken: Die Vereinigung ist die Summe.
        // Ueberlappten sie, waere sie kleiner - deshalb nicht auf Gleichheit, sondern
        // auf "nicht mehr als die Summe" geprueft.
        Check.That(both <= alone + other + 0.01, "zusammen nie mehr als beide einzeln",
                   $"{both:0.#} gegen {alone + other:0.#}");
        Check.That(both > alone && both > other, "und mehr als jedes allein",
                   $"{both:0.#}");
    }

    /// <summary>Und dasselbe durch den Composer, so wie es das Bild auch nimmt.</summary>
    private static void InTheComposer(string path)
    {
        Check.Group("Die Kryptomatte begrenzt eine Ebene");

        var set = Cryptomatte.Of(path).First(s => s.ShortName == "CryptoObject");
        var passes = ExrPasses.Of(path);
        var levelNames = Cryptomatte.Levels(passes, "ViewLayer.CryptoObject");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.Combined", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "ViewLayer.Combined",
                    Mode = BlendMode.Add,
                    Mask = new LayerMask
                    {
                        Kind = MaskKind.Cryptomatte,
                        Source = "ViewLayer.CryptoObject",
                        Levels = levelNames.ToList(),
                        Picks = { new CryptoPick { Name = "Kugel", Id = set.Names["Kugel"] } },
                    },
                },
            },
        };

        // Der Ladeweg muss die Stufen von sich aus mitnehmen.
        var needed = stack.NeededSources();
        Check.That(needed.Count == 4, "eine Quelle und drei Stufen werden gelesen",
                   string.Join(", ", needed.Select(Short)));

        var built = LayeredFrameLoader.Load(path, stack);
        var plain = FloatFrame.FromExrPass(path, "ViewLayer.Combined");

        Check.That(built is not null && plain is not null, "beide Bilder stehen");
        if (built is null || plain is null) return;

        // Wo die Kugel ist, ist das Bild doppelt so hell; sonst unveraendert.
        int doubled = 0, untouched = 0;

        for (int i = 0; i < built.PixelCount; i++)
        {
            float single = plain.R[i] + plain.G[i] + plain.B[i];
            float mixed = built.R[i] + built.G[i] + built.B[i];

            if (single < 0.001f) continue;

            if (MathF.Abs(mixed - single * 2f) < 0.01f * MathF.Max(1f, single)) doubled++;
            else if (MathF.Abs(mixed - single) < 0.01f * MathF.Max(1f, single)) untouched++;
        }

        Check.That(doubled > 0, "auf der Kugel wirkt die obere Ebene", $"{doubled} Bildpunkte");
        Check.That(untouched > doubled, "daneben nicht", $"{untouched} Bildpunkte");

        // Umgekehrt genau andersherum.
        stack.Layers[1].Mask.Invert = true;
        var flipped = LayeredFrameLoader.Load(path, stack)!;

        int flippedDoubled = 0;
        for (int i = 0; i < flipped.PixelCount; i++)
        {
            float single = plain.R[i] + plain.G[i] + plain.B[i];
            if (single < 0.001f) continue;

            float mixed = flipped.R[i] + flipped.G[i] + flipped.B[i];
            if (MathF.Abs(mixed - single * 2f) < 0.01f * MathF.Max(1f, single)) flippedDoubled++;
        }

        Check.That(flippedDoubled > doubled, "umgekehrt wirkt sie ueberall sonst",
                   $"{flippedDoubled} statt {doubled}");

        static string Short(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name[(dot + 1)..] : name;
        }
    }

    private static FloatFrame[] Load(string path)
        => new[] { "00", "01", "02" }
            .Select(n => FloatFrame.FromExrPass(path, "ViewLayer.CryptoObject" + n)!)
            .Where(f => f is not null)
            .ToArray();
}
