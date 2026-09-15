namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Setzt die Ebenen zu einem Bild zusammen.
///
/// Das Ergebnis ist wieder ein <see cref="FloatFrame"/> in linearem Licht, und damit
/// geht es unveraendert denselben Weg weiter wie ein einzeln gelesenes Bild:
/// Grundkorrektur, Werkzeuge, Sichtumwandlung. Die Zusammensetzung ist ein Schritt
/// davor und kein zweiter Weg daneben - sonst haette der Stapellauf eine Kette und
/// die Vorschau eine andere, und irgendwann saehe der Export anders aus als das,
/// worauf sich jemand verlassen hat.
///
/// Zusammengesetzt wird VOR der Sichtumwandlung. Das ist die Stelle, an der die
/// Zerlegung entstanden ist: Blender addiert die Passe im linearen Licht zum
/// fertigen Bild. Wer sie hinterher zusammensetzte, addierte bereits durch AgX
/// gegangene Bilder, und die Summe waere nicht das Original, sondern heller.
/// </summary>
public static class LayerComposer
{
    /// <summary>
    /// Baut das Bild aus den Ebenen. Null, wenn keine Ebene etwas beitraegt.
    /// </summary>
    /// <param name="sources">
    /// Die gelesenen Passe, nach dem Namen der Quelle. Eine Ebene, deren Pass fehlt
    /// oder eine andere Groesse hat, wird uebersprungen - eine halb gelesene Datei
    /// soll ein Bild ergeben, das man ansehen kann, und keinen Abbruch.
    /// </param>
    public static FloatFrame? Compose(LayerStack stack, IReadOnlyDictionary<string, FloatFrame> sources)
    {
        var used = new List<(ImageLayer Layer, FloatFrame Frame)>();
        int width = 0, height = 0;

        foreach (var layer in stack.Layers)
        {
            if (!layer.Visible || layer.Opacity <= 0.0005f) continue;
            if (!sources.TryGetValue(layer.Source, out var frame)) continue;

            // Die erste brauchbare Ebene gibt die Groesse vor; alles Abweichende
            // faellt heraus. Zwei Groessen ineinanderzurechnen hiesse skalieren, und
            // das ist eine andere Aufgabe als mischen.
            if (width == 0)
            {
                width = frame.Width;
                height = frame.Height;
            }
            else if (frame.Width != width || frame.Height != height)
            {
                continue;
            }

            used.Add((layer, frame));
        }

        if (used.Count == 0) return null;

        // Eine einzelne unveraenderte Ebene ist das Bild selbst. Sie durchzureichen
        // spart bei 4K rund hundert Megabyte und eine Kopie.
        if (used.Count == 1 && used[0].Layer.IsNeutral && used[0].Layer.LiesOnBlack)
        {
            return used[0].Frame;
        }

        int count = width * height;
        var r = new float[count];
        var g = new float[count];
        var b = new float[count];
        var a = new float[count];

        bool sceneReferred = used[0].Frame.IsSceneReferred;

        // Je Ebene einmal vorbereitet, damit die innere Schleife nur noch multipliziert.
        var plans = new Plan[used.Count];
        for (int i = 0; i < used.Count; i++)
        {
            var layer = used[i].Layer;
            float gain = MathF.Pow(2f, layer.Exposure);

            // Die unterste Ebene kann sich an nichts anschneiden. Eine Schnittmaske
            // ohne Traeger als solche zu behandeln hiesse, sie verschwinden zu
            // lassen - sie wird stattdessen zur gewoehnlichen Ebene.
            bool clipped = layer.Clipped && i > 0;

            plans[i] = new Plan(used[i].Frame, layer.Mode, Math.Clamp(layer.Opacity, 0f, 1f),
                                gain * layer.Tint.R, gain * layer.Tint.G, gain * layer.Tint.B, clipped);
        }

        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            int start = y * width;

            for (int x = 0; x < width; x++)
            {
                int i = start + x;

                // Der Untergrund ist Schwarz und nicht die unterste Ebene: Damit
                // gilt fuer JEDE Ebene dieselbe Regel, auch fuer die unterste. Auf
                // Add ergibt das die Summe der Passe und damit wieder das Bild, das
                // gerendert wurde; auf Normal legt die unterste Ebene sich einfach
                // auf das Schwarz.
                float vr = 0f, vg = 0f, vb = 0f, va = 0f;

                // Die laufende Gruppe: eine Traegerebene und alles, was sich an sie
                // anschneidet. Sie wird erst als Ganzes auf das Ergebnis gemischt,
                // und zwar mit der Mischung des Traegers - genau so, wie eine
                // Schnittmaske in Photoshop wirkt.
                float gr = 0f, gg = 0f, gb = 0f;
                var groupMode = BlendMode.Normal;
                float groupOpacity = 1f;
                bool open = false;

                for (int p = 0; p < plans.Length; p++)
                {
                    ref readonly var plan = ref plans[p];
                    var frame = plan.Frame;

                    float lr = frame.R[i] * plan.ScaleR;
                    float lg = frame.G[i] * plan.ScaleG;
                    float lb = frame.B[i] * plan.ScaleB;

                    if (plan.Clipped && open)
                    {
                        Blending.Mix(plan.Mode, plan.Opacity, gr, gg, gb, lr, lg, lb,
                                     out gr, out gg, out gb);
                    }
                    else
                    {
                        if (open)
                            Blending.Mix(groupMode, groupOpacity, vr, vg, vb, gr, gg, gb,
                                         out vr, out vg, out vb);

                        gr = lr;
                        gg = lg;
                        gb = lb;
                        groupMode = plan.Mode;
                        groupOpacity = plan.Opacity;
                        open = true;
                    }

                    // Die Deckung ist keine Mischung, sondern eine Abdeckung: Wo
                    // irgendeine Ebene deckt, deckt das Ergebnis. Sie durch dieselbe
                    // Formel zu schicken wie die Farbe hiesse, Alpha auf Add zu
                    // summieren - drei Passe ergaeben Deckung 3.
                    float la = (frame.A is null ? 1f : frame.A[i]) * plan.Opacity;
                    if (la > va) va = la;
                }

                if (open)
                    Blending.Mix(groupMode, groupOpacity, vr, vg, vb, gr, gg, gb,
                                 out vr, out vg, out vb);

                r[i] = vr;
                g[i] = vg;
                b[i] = vb;
                a[i] = va;
            }
        });

        return new FloatFrame
        {
            Width = width,
            Height = height,
            R = r,
            G = g,
            B = b,
            A = a,
            Layer = used.Count == 1 ? used[0].Frame.Layer : null,
            IsSceneReferred = sceneReferred,
        };
    }

    /// <summary>Was je Ebene einmal feststeht.</summary>
    private readonly struct Plan
    {
        public Plan(FloatFrame frame, BlendMode mode, float opacity,
                    float sr, float sg, float sb, bool clipped)
        {
            Frame = frame;
            Mode = mode;
            Opacity = opacity;
            ScaleR = sr;
            ScaleG = sg;
            ScaleB = sb;
            Clipped = clipped;
        }

        public readonly FloatFrame Frame;
        public readonly BlendMode Mode;
        public readonly bool Clipped;
        public readonly float Opacity, ScaleR, ScaleG, ScaleB;
    }
}
