using System.Text.Json;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Tests;

/// <summary>
/// Der Ebenenstapel: was die Datei hergibt, wie daraus ein Bild wird, und was davon
/// eine Sitzung ueberdauert.
/// </summary>
public static class LayerInvariants
{
    public static void Run()
    {
        PassesFromNames();
        FindsByShortName();
        SumRebuildsTheImage();
        OrderAndVisibility();
        ToneAndOpacity();
        ClippingStaysWithItsLayer();
        MissingAndMismatched();
        PassThroughIsFree();
        Persistence();
    }

    // ----------------------------------------------------------------- die Datei

    /// <summary>
    /// Die Kanalnamen einer Blender-Datei in Passe zerlegen.
    ///
    /// Wie diese Namen gebildet werden, haengt an der Blender-Fassung und am Namen
    /// der Ansichtsebene, den der Anwender frei vergibt. Geprueft wird deshalb die
    /// Regel - am letzten Punkt trennen - und nicht eine Liste erwarteter Namen.
    /// </summary>
    private static void PassesFromNames()
    {
        Check.Group("Passe aus Kanalnamen");

        var names = new[]
        {
            "ViewLayer.Combined.R", "ViewLayer.Combined.G", "ViewLayer.Combined.B", "ViewLayer.Combined.A",
            "ViewLayer.DiffCol.R", "ViewLayer.DiffCol.G", "ViewLayer.DiffCol.B",
            "ViewLayer.Depth.Z",
            "ViewLayer.Normal.X", "ViewLayer.Normal.Y", "ViewLayer.Normal.Z",
            "ViewLayer.CryptoObject00.R", "ViewLayer.CryptoObject00.G",
            "ViewLayer.CryptoObject00.B", "ViewLayer.CryptoObject00.A",
        };

        var passes = ExrPasses.List(names);

        Check.That(passes.Count == 5, "fuenf Passe", $"{passes.Count}");

        var combined = passes.FirstOrDefault(p => p.ShortName == "Combined");
        Check.That(combined.Red == "ViewLayer.Combined.R", "Combined findet seine Kanaele");
        Check.That(combined.Alpha == "ViewLayer.Combined.A", "samt Deckung");
        Check.That(!combined.Grey, "und gilt als Farbe");

        var diffuse = passes.FirstOrDefault(p => p.ShortName == "DiffCol");
        Check.That(diffuse.Alpha is null, "ein Pass ohne Deckung hat keine");

        // Eine Richtung ist keine Farbe, laesst sich aber als eine ansehen - das ist
        // die uebliche Darstellung und beantwortet, was drinsteht.
        var normal = passes.FirstOrDefault(p => p.ShortName == "Normal");
        Check.That(normal.Red == "ViewLayer.Normal.X" && normal.Blue == "ViewLayer.Normal.Z",
                   "X, Y, Z werden zu R, G, B");

        // Die Tiefe hat einen Kanal. Dreimal derselbe ergibt ein Graustufenbild.
        var depth = passes.FirstOrDefault(p => p.ShortName == "Depth");
        Check.That(depth.Grey, "die Tiefe gilt als Graustufe");
        Check.That(depth.Red == depth.Green && depth.Green == depth.Blue,
                   "und nennt dreimal denselben Kanal");

        // Eine schmucklose Datei ohne Ebenennamen.
        var plain = ExrPasses.List(new[] { "R", "G", "B", "A" });
        Check.That(plain.Count == 1, "eine schmucklose Datei hat einen Pass", $"{plain.Count}");
        Check.That(plain[0].Name.Length == 0, "ohne Gruppennamen");
        Check.That(plain[0].ShortName == "RGB", "und heisst in der Liste RGB");
    }

    /// <summary>
    /// Ein Rezept muss eine Umbenennung in Blender ueberstehen.
    ///
    /// Die Ansichtsebene heisst so, wie jemand sie genannt hat. Wer den Stapel auf
    /// einer Sequenz eingerichtet hat, deren Ebene "ViewLayer" hiess, soll ihn auch
    /// auf einer anwenden koennen, in der sie "Hauptebene" heisst - sonst waere jede
    /// Umbenennung ein verlorenes Rezept.
    /// </summary>
    private static void FindsByShortName()
    {
        Check.Group("Passe ueber den Kurznamen wiederfinden");

        var passes = ExrPasses.List(new[]
        {
            "Hauptebene.GlossDir.R", "Hauptebene.GlossDir.G", "Hauptebene.GlossDir.B",
        });

        Check.That(ExrPasses.Find(passes, "Hauptebene.GlossDir") is not null,
                   "der volle Name findet");
        Check.That(ExrPasses.Find(passes, "ViewLayer.GlossDir") is not null,
                   "und der aus einer anders benannten Ebene auch");
        Check.That(ExrPasses.Find(passes, "ViewLayer.DiffCol") is null,
                   "ein Pass, den es nicht gibt, wird nicht erfunden");
    }

    // -------------------------------------------------------- das Zusammensetzen

    /// <summary>
    /// Addieren addiert - und zwar in linearem Licht, ohne unterwegs zu beschneiden.
    ///
    /// Das ist der Unterschied zwischen einem Werkzeug, das Renderpasse versteht,
    /// und einem, das Bilder uebereinanderlegt: Ein Glanzpass mit Wert 12 muss mit
    /// Wert 12 in die Summe gehen und nicht mit 1.
    ///
    /// Ob diese Summe wirklich wieder den Render ergibt, steht in
    /// <see cref="PassRebuildInvariants"/> - dort gegen eine echte Datei. Hier geht
    /// es nur um die Rechnung selbst.
    /// </summary>
    private static void SumRebuildsTheImage()
    {
        Check.Group("Addieren addiert, ohne zu beschneiden");

        var diffuse = Frame(4, 4, 0.30f, 0.20f, 0.10f);
        var gloss = Frame(4, 4, 12.0f, 9.5f, 6.0f);
        var emission = Frame(4, 4, 0.02f, 0.04f, 0.07f);

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["DiffCol"] = diffuse,
            ["GlossDir"] = gloss,
            ["Emit"] = emission,
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "DiffCol", Mode = BlendMode.Normal },
                new ImageLayer { Source = "GlossDir", Mode = BlendMode.Add },
                new ImageLayer { Source = "Emit", Mode = BlendMode.Add },
            },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt ein Bild heraus");
        if (built is null) return;

        Check.Near(built.R[0], 0.30 + 12.0 + 0.02, 1e-4, "Rot ist die Summe");
        Check.Near(built.G[0], 0.20 + 9.5 + 0.04, 1e-4, "Gruen auch");
        Check.Near(built.B[0], 0.10 + 6.0 + 0.07, 1e-4, "und Blau");

        Check.That(built.R[0] > 1f, "die Zeichnung ueber Weiss bleibt erhalten", $"{built.R[0]:0.##}");
        Check.That(built.IsSceneReferred, "und das Ergebnis ist weiter Szenenlicht");
    }

    /// <summary>
    /// Reihenfolge und Sichtbarkeit - die beiden Griffe, die man am haeufigsten tut.
    /// </summary>
    private static void OrderAndVisibility()
    {
        Check.Group("Reihenfolge und Sichtbarkeit");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["a"] = Frame(2, 2, 0.4f, 0.4f, 0.4f),
            ["b"] = Frame(2, 2, 0.5f, 0.5f, 0.5f),
        };

        var forward = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "a", Mode = BlendMode.Normal },
                new ImageLayer { Source = "b", Mode = BlendMode.Add },
            },
        };

        var reversed = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "b", Mode = BlendMode.Normal },
                new ImageLayer { Source = "a", Mode = BlendMode.Add },
            },
        };

        // Auf Addieren ist die Reihenfolge gleichgueltig - das ist der Grund, warum
        // ein Passestapel sich nicht verstellen laesst, solange niemand die Mischung
        // aendert.
        Check.Near(LayerComposer.Compose(forward, sources)!.R[0],
                   LayerComposer.Compose(reversed, sources)!.R[0], 1e-5,
                   "auf Addieren ist die Reihenfolge gleichgueltig");

        // Auf Normal dagegen entscheidet, wer oben liegt: die obere ersetzt.
        var overlaid = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "a", Mode = BlendMode.Normal },
                new ImageLayer { Source = "b", Mode = BlendMode.Normal },
            },
        };

        Check.Near(LayerComposer.Compose(overlaid, sources)!.R[0], 0.5, 1e-5,
                   "auf Normal gilt die obere Ebene");

        // Eine unsichtbare Ebene traegt nichts bei - und zwar so, als waere sie
        // nicht da, nicht als waere sie schwarz.
        var hidden = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "a", Mode = BlendMode.Normal },
                new ImageLayer { Source = "b", Mode = BlendMode.Add, Visible = false },
            },
        };

        Check.Near(LayerComposer.Compose(hidden, sources)!.R[0], 0.4, 1e-5,
                   "eine unsichtbare Ebene traegt nichts bei");

        Check.That(!hidden.NeededSources().Contains("b"),
                   "und ihr Pass wird gar nicht erst gelesen");
    }

    /// <summary>Belichtung, Farbe und Deckkraft je Ebene.</summary>
    private static void ToneAndOpacity()
    {
        Check.Group("Belichtung, Farbe und Deckkraft je Ebene");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["gloss"] = Frame(2, 2, 1f, 1f, 1f),
        };

        var doubled = new LayerStack
        {
            Layers = { new ImageLayer { Source = "gloss", Mode = BlendMode.Normal, Exposure = 1f } },
        };

        Check.Near(LayerComposer.Compose(doubled, sources)!.R[0], 2.0, 1e-4,
                   "eine Blendenstufe verdoppelt");

        var tinted = new LayerStack
        {
            Layers =
            {
                new ImageLayer
                {
                    Source = "gloss",
                    Mode = BlendMode.Normal,
                    Tint = new ColourTriplet(1.5f, 1f, 0.5f),
                },
            },
        };

        var warm = LayerComposer.Compose(tinted, sources)!;
        Check.Near(warm.R[0], 1.5, 1e-4, "die Farbe faerbt Rot ein");
        Check.Near(warm.B[0], 0.5, 1e-4, "und nimmt Blau zurueck");

        // Deckkraft null heisst: gar nicht erst mitrechnen. Sonst waere eine
        // ausgeblendete Ebene bei Difference oder Darken immer noch zu sehen.
        var faded = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "gloss", Mode = BlendMode.Normal },
                new ImageLayer { Source = "gloss", Mode = BlendMode.Darken, Opacity = 0f },
            },
        };

        Check.Near(LayerComposer.Compose(faded, sources)!.R[0], 1.0, 1e-4,
                   "Deckkraft null wirkt gar nicht");
    }

    /// <summary>
    /// Die Schnittmaske: wirkt nur auf die Ebene darunter, nicht auf alles darunter.
    ///
    /// Ohne sie geht die Zerlegung eines Renders nicht auf. Cycles liefert Licht und
    /// Farbe getrennt, und der Farbpass ist ein Faktor SEINES Lichts - nicht des
    /// ganzen Stapels. Der Unterschied ist hier mit Absicht gross gewaehlt, damit er
    /// sich nicht als Rundung lesen laesst.
    /// </summary>
    private static void ClippingStaysWithItsLayer()
    {
        Check.Group("Eine Schnittmaske bleibt bei ihrer Ebene");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["grund"] = Frame(2, 2, 1f, 1f, 1f),
            ["licht"] = Frame(2, 2, 2f, 2f, 2f),
            ["farbe"] = Frame(2, 2, 0.5f, 0.5f, 0.5f),
        };

        ImageLayer[] Build(bool clipped) => new[]
        {
            new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
            new ImageLayer { Source = "licht", Mode = BlendMode.Add },
            new ImageLayer { Source = "farbe", Mode = BlendMode.Multiply, Clipped = clipped },
        };

        // Ohne Schnittmaske: (1 + 2) * 0,5 = 1,5 - der Faktor erwischt auch die
        // Grundebene, die ihn nichts angeht.
        var loose = new LayerStack { Layers = Build(false).ToList() };
        Check.Near(LayerComposer.Compose(loose, sources)!.R[0], 1.5, 1e-4,
                   "ohne Schnittmaske multipliziert der Faktor alles darunter");

        // Mit: 1 + (2 * 0,5) = 2 - der Faktor bleibt bei seinem Licht.
        var clipped = new LayerStack { Layers = Build(true).ToList() };
        Check.Near(LayerComposer.Compose(clipped, sources)!.R[0], 2.0, 1e-4,
                   "mit Schnittmaske nur die eine Ebene");

        // Die unterste Ebene kann sich an nichts anschneiden. Sie deshalb ganz
        // wegzulassen waere die schlechtere Antwort - sie wird zur gewoehnlichen.
        var atTheBottom = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal, Clipped = true },
                new ImageLayer { Source = "licht", Mode = BlendMode.Add },
            },
        };

        Check.Near(LayerComposer.Compose(atTheBottom, sources)!.R[0], 3.0, 1e-4,
                   "ganz unten wirkt eine Schnittmaske wie keine");

        // Mehrere angeschnittene Ebenen gehoeren alle zu derselben Traegerebene.
        var twice = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "grund", Mode = BlendMode.Normal },
                new ImageLayer { Source = "licht", Mode = BlendMode.Add },
                new ImageLayer { Source = "farbe", Mode = BlendMode.Multiply, Clipped = true },
                new ImageLayer { Source = "farbe", Mode = BlendMode.Multiply, Clipped = true },
            },
        };

        Check.Near(LayerComposer.Compose(twice, sources)!.R[0], 1.5, 1e-4,
                   "zwei Schnittmasken wirken beide auf denselben Traeger");
    }

    /// <summary>
    /// Was passiert, wenn das Rezept nicht zur Datei passt.
    ///
    /// Ein fehlender Pass ist der Normalfall, nicht die Ausnahme: Rezepte wandern
    /// zwischen Dateien. Ein Abbruch waere die falsche Antwort - man soll das Bild
    /// sehen und den Fehler dort suchen, wo er liegt.
    /// </summary>
    private static void MissingAndMismatched()
    {
        Check.Group("Fehlende und unpassende Passe");

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal)
        {
            ["da"] = Frame(4, 4, 0.5f, 0.5f, 0.5f),
            ["klein"] = Frame(2, 2, 9f, 9f, 9f),
        };

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "da", Mode = BlendMode.Normal },
                new ImageLayer { Source = "fehlt", Mode = BlendMode.Add },
                new ImageLayer { Source = "klein", Mode = BlendMode.Add },
            },
        };

        var built = LayerComposer.Compose(stack, sources);
        Check.That(built is not null, "es kommt trotzdem ein Bild heraus");
        if (built is null) return;

        Check.That(built.Width == 4 && built.Height == 4, "in der Groesse der ersten brauchbaren Ebene");

        // Der fehlende Pass bleibt draussen - es gibt ihn nicht.
        //
        // Die Ebene in anderer GROESSE dagegen bleibt drin und wird eingepasst. Das
        // war frueher andersherum: Abweichende Groessen fielen heraus, weil Skalieren
        // eine andere Aufgabe war als Mischen. Seit es die Platzierung gibt, ist es
        // keine andere Aufgabe mehr, sondern ein Logo - und ein Logo wegzulassen,
        // weil es kleiner ist als das Bild, waere die falsche Antwort.
        Check.Near(built.R[0], 9.5, 1e-4, "eine Ebene anderer Groesse wird eingepasst");

        // Ohne sie bleibt genau die Grundebene stehen - der fehlende Pass traegt
        // wirklich nichts bei.
        stack.Layers.RemoveAt(2);
        Check.Near(LayerComposer.Compose(stack, sources)!.R[0], 0.5, 1e-4,
                   "der fehlende Pass traegt nichts bei");

        // Gar nichts Lesbares: dann gibt es auch kein Bild, und der Aufrufer weicht
        // auf die Datei selbst aus.
        var nothing = new LayerStack
        {
            Layers = { new ImageLayer { Source = "gibt es nicht" } },
        };

        Check.That(LayerComposer.Compose(nothing, sources) is null,
                   "ohne lesbare Ebene kommt nichts heraus");
    }

    /// <summary>
    /// Eine einzelne unveraenderte Ebene darf nichts kosten.
    ///
    /// Bei 4K sind das rund hundert Megabyte und eine volle Kopie je Reglerzug. Der
    /// Weg ohne Zusammensetzung ist ausserdem der genauere: Er reicht den Frame
    /// durch, wie er gelesen wurde.
    /// </summary>
    private static void PassThroughIsFree()
    {
        Check.Group("Eine Ebene ohne Einstellung kostet nichts");

        var image = Frame(2, 2, 0.3f, 0.3f, 0.3f);
        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal) { [""] = image };

        var single = new LayerStack
        {
            Layers = { new ImageLayer { Source = "", Mode = BlendMode.Normal } },
        };

        Check.That(single.IsPassThrough, "der Stapel meldet sich als durchgereicht");
        Check.That(ReferenceEquals(LayerComposer.Compose(single, sources), image),
                   "und es ist derselbe Frame, nicht eine Kopie");

        // Sobald etwas eingestellt ist, wird gerechnet.
        single.Layers[0].Exposure = 0.5f;
        Check.That(!single.IsPassThrough, "mit Belichtung nicht mehr");
        Check.That(!ReferenceEquals(LayerComposer.Compose(single, sources), image),
                   "und es kommt ein neuer Frame heraus");

        // Zwei Ebenen sind nie ein Durchreichen, auch wenn beide nichts tun.
        var two = new LayerStack
        {
            Layers = { new ImageLayer(), new ImageLayer() },
        };

        Check.That(!two.IsPassThrough, "zwei Ebenen werden gerechnet");
    }

    /// <summary>
    /// Der Stapel wird gespeichert. Was sich nicht lesen laesst, ist verloren - und
    /// zwar stillschweigend, was die unangenehmere Art ist.
    /// </summary>
    private static void Persistence()
    {
        Check.Group("Der Stapel ueberlebt das Speichern");

        var stack = new LayerStack
        {
            Layers =
            {
                new ImageLayer { Source = "ViewLayer.DiffCol", Name = "Diffus", Mode = BlendMode.Normal },
                new ImageLayer
                {
                    Source = "ViewLayer.GlossDir",
                    Name = "Glanz",
                    Mode = BlendMode.Screen,
                    Opacity = 0.62f,
                    Exposure = -1.5f,
                    Visible = false,
                    Clipped = true,
                    Tint = new ColourTriplet(1.2f, 1f, 0.8f),
                },
            },
        };

        string json = JsonSerializer.Serialize(stack);
        var read = JsonSerializer.Deserialize<LayerStack>(json);

        Check.That(read is not null, "es laesst sich wieder lesen");
        if (read is null) return;

        Check.That(read.Layers.Count == 2, "beide Ebenen sind da", $"{read.Layers.Count}");
        Check.That(read.Layers[1].Mode == BlendMode.Screen, "die Mischung bleibt");
        Check.Near(read.Layers[1].Opacity, 0.62, 1e-5, "die Deckkraft auch");
        Check.Near(read.Layers[1].Exposure, -1.5, 1e-5, "und die Belichtung");
        Check.That(!read.Layers[1].Visible, "eine ausgeblendete Ebene bleibt ausgeblendet");
        Check.That(read.Layers[1].Clipped, "und eine Schnittmaske bleibt eine");
        Check.Near(read.Layers[1].Tint.R, 1.2, 1e-5, "und die Farbe steht noch");

        // Kopieren muss tief sein: Waehrend ein Stapellauf laeuft, darf am Original
        // weitergeregelt werden, ohne dass sich die Ausgabe auf halber Strecke
        // aendert.
        var copy = stack.Clone();
        stack.Layers[0].Opacity = 0.1f;
        stack.Layers[1].Tint.R = 9f;

        Check.Near(copy.Layers[0].Opacity, 1.0, 1e-5, "eine Kopie bewegt sich nicht mit");
        Check.Near(copy.Layers[1].Tint.R, 1.2, 1e-5, "auch nicht in der Farbe");
    }

    // ------------------------------------------------------------------- Handwerk

    private static FloatFrame Frame(int width, int height, float r, float g, float b)
    {
        int count = width * height;

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = Enumerable.Repeat(r, count).ToArray(),
            G = Enumerable.Repeat(g, count).ToArray(),
            B = Enumerable.Repeat(b, count).ToArray(),
            A = Enumerable.Repeat(1f, count).ToArray(),
        };
    }
}
