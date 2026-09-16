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

            // Der Pass, aus dem die Maske liest. Fehlt er, faellt die Maske weg -
            // eine Ebene ganz verschwinden zu lassen, weil ihre Maske nicht gelesen
            // werden konnte, waere die falsche Antwort auf eine fehlende Datei.
            FloatFrame? maskFrame = null;
            FloatFrame[]? maskLevels = null;
            float[]? maskIds = null;
            var maskKind = layer.Mask.Kind;

            if (maskKind == MaskKind.Pass)
            {
                if (sources.TryGetValue(layer.Mask.Source, out var found) &&
                    found.Width == width && found.Height == height)
                {
                    maskFrame = found;
                }
                else
                {
                    maskKind = MaskKind.None;
                }
            }
            else if (maskKind == MaskKind.Cryptomatte)
            {
                var levels = new List<FloatFrame>();

                foreach (string level in layer.Mask.Levels)
                {
                    if (sources.TryGetValue(level, out var found) &&
                        found.Width == width && found.Height == height)
                    {
                        levels.Add(found);
                    }
                }

                maskLevels = levels.ToArray();
                maskIds = layer.Mask.Picks.Select(p => p.Id).ToArray();

                // Ohne Stufen oder ohne Auswahl gibt es nichts zu maskieren. Die
                // Maske fallen zu lassen ist hier die richtige Antwort: Eine leere
                // Auswahl liesse die Ebene ganz verschwinden, und das saehe aus wie
                // ein Fehler statt wie "es ist noch nichts ausgewaehlt".
                if (maskLevels.Length == 0 || maskIds.Length == 0) maskKind = MaskKind.None;
            }

            plans[i] = new Plan(used[i].Frame, layer.Mode, Math.Clamp(layer.Opacity, 0f, 1f),
                                gain * layer.Tint.R, gain * layer.Tint.G, gain * layer.Tint.B, clipped,
                                layer.Mask, maskKind, maskFrame, maskLevels, maskIds);
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

                    bool inGroup = plan.Clipped && open;

                    // Die Maske greift an genau einer Stelle an: Sie macht die
                    // Deckkraft oertlich. Damit gilt fuer jede Mischung und jede
                    // Schnittmaske dieselbe Regel, und es gibt keinen Fall, in dem
                    // eine Maske etwas anderes bedeutet als sonst.
                    float opacity = plan.Mask == MaskKind.None
                        ? plan.Opacity
                        : plan.Opacity * Factor(in plan, x, y, width, height, i,
                                                lr, lg, lb,
                                                inGroup ? gr : vr,
                                                inGroup ? gg : vg,
                                                inGroup ? gb : vb);

                    if (inGroup)
                    {
                        Blending.Mix(plan.Mode, opacity, gr, gg, gb, lr, lg, lb,
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

                        // Die Gruppe fuehrt die Deckkraft ihres Traegers mit, und
                        // damit auch dessen Maske: Erst wenn die Gruppe geschlossen
                        // wird, mischt sie sich auf das Ergebnis, und bis dahin muss
                        // der oertliche Wert erhalten bleiben.
                        groupOpacity = opacity;
                        open = true;
                    }

                    // Die Deckung ist keine Mischung, sondern eine Abdeckung: Wo
                    // irgendeine Ebene deckt, deckt das Ergebnis. Sie durch dieselbe
                    // Formel zu schicken wie die Farbe hiesse, Alpha auf Add zu
                    // summieren - drei Passe ergaeben Deckung 3.
                    float la = (frame.A is null ? 1f : frame.A[i]) * opacity;
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

    // Rec.-709-Luminanz, dieselben Gewichte wie im uebrigen Bildweg.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Der Maskenwert eines Bildpunkts, zwischen 0 und 1.
    /// </summary>
    /// <param name="lr">Die Ebene selbst, bereits mit Belichtung und Farbe.</param>
    /// <param name="ur">Was an dieser Stelle schon darunter liegt.</param>
    private static float Factor(in Plan plan, int x, int y, int width, int height, int i,
                                float lr, float lg, float lb,
                                float ur, float ug, float ub)
    {
        float value;

        switch (plan.Mask)
        {
            case MaskKind.Luminance:
                value = Masking.Perceptual(LumaR * lr + LumaG * lg + LumaB * lb);
                break;

            case MaskKind.Underlying:
                value = Masking.Perceptual(LumaR * ur + LumaG * ug + LumaB * ub);
                break;

            case MaskKind.Pass:
                // Ein anderer Weg als bei der Helligkeit, und das mit Absicht.
                //
                // Nebel, Verschattung und Indexmasken sind bereits Masken: Ihr Wert
                // IST der Anteil. Er wird durchgereicht und bekommt nur einen
                // Schwarz- und einen Weisspunkt, wie jede Maske, die man anzieht.
                // Durch das Bereichsfenster der Helligkeitsmaske geschickt taete er
                // in Grundstellung nichts - jeder Wert zwischen 0 und 1 liegt im
                // Fenster 0 bis 1.
                //
                // Ueber die Luminanz und nicht ueber Rot allein: Bei einem
                // Graustufenpass sind beide identisch, bei einem farbigen waere Rot
                // eine willkuerliche Wahl.
                var m = plan.MaskFrame!;
                float raw = LumaR * m.R[i] + LumaG * m.G[i] + LumaB * m.B[i];

                return Fit(Masking.Levels(raw, plan.MaskLow, plan.MaskHigh), plan.MaskInvert);

            case MaskKind.Cryptomatte:
                // Dieselbe Behandlung wie beim Pass: Die Deckung IST der Anteil, und
                // Schwarz- und Weisspunkt ziehen ihn an - damit laesst sich eine
                // weiche Kante wegnehmen oder stehenlassen.
                float coverage = Masking.Coverage(plan.MaskLevels!, plan.MaskIds!, i);

                return Fit(Masking.Levels(coverage, plan.MaskLow, plan.MaskHigh), plan.MaskInvert);

            case MaskKind.Gradient:
                return Fit(Masking.Gradient(x, y, width, height,
                                            plan.GradientCos, plan.GradientSin,
                                            plan.GradientFrom, plan.GradientTo), plan.MaskInvert);

            default:
                return 1f;
        }

        return Fit(Masking.Band(value, plan.MaskLow, plan.MaskHigh, plan.MaskSoftness),
                   plan.MaskInvert);
    }

    private static float Fit(float factor, bool invert) => invert ? 1f - factor : factor;

    /// <summary>Was je Ebene einmal feststeht.</summary>
    private readonly struct Plan
    {
        public Plan(FloatFrame frame, BlendMode mode, float opacity,
                    float sr, float sg, float sb, bool clipped,
                    LayerMask mask, MaskKind kind, FloatFrame? maskFrame,
                    FloatFrame[]? maskLevels = null, float[]? maskIds = null)
        {
            Frame = frame;
            Mode = mode;
            Opacity = opacity;
            ScaleR = sr;
            ScaleG = sg;
            ScaleB = sb;
            Clipped = clipped;

            Mask = kind;
            MaskFrame = maskFrame;
            MaskLevels = maskLevels;
            MaskIds = maskIds;
            MaskInvert = mask.Invert;
            MaskLow = mask.Low;
            MaskHigh = mask.High;
            MaskSoftness = mask.Softness;

            // Winkel und Breite einmal je Bild in das umrechnen, was die innere
            // Schleife braucht - bei 4K waeren es sonst 25 Millionen Sinusse.
            float radians = mask.Angle * MathF.PI / 180f;
            GradientCos = MathF.Cos(radians);
            GradientSin = MathF.Sin(radians);

            float half = MathF.Max(0f, mask.Width) / 2f;
            GradientFrom = mask.Centre - half;
            GradientTo = mask.Centre + half;
        }

        public readonly FloatFrame Frame;
        public readonly BlendMode Mode;
        public readonly bool Clipped;
        public readonly float Opacity, ScaleR, ScaleG, ScaleB;

        public readonly MaskKind Mask;
        public readonly FloatFrame? MaskFrame;
        public readonly FloatFrame[]? MaskLevels;
        public readonly float[]? MaskIds;
        public readonly bool MaskInvert;
        public readonly float MaskLow, MaskHigh, MaskSoftness;
        public readonly float GradientCos, GradientSin, GradientFrom, GradientTo;
    }
}
