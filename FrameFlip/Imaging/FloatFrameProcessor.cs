using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging;

/// <summary>
/// Die Anzeigekorrektur auf linearem Szenenlicht.
///
/// Der Unterschied zum <see cref="FrameProcessor"/> ist nicht die Genauigkeit,
/// sondern die Reihenfolge - und die ist hier nicht Geschmack, sondern folgt dem,
/// was die einzelnen Regler physikalisch bedeuten:
///
/// **Belichtung wirkt VOR der Sichtumwandlung.** Sie ist eine Lichtmenge, also eine
/// Multiplikation in linearem Licht. Genau dort sitzt sie auch in Blender. Wer sie
/// hinter AgX anwendet, hellt ein bereits fertiges Bild auf und holt damit nichts
/// zurueck - die Lichter sind dann schon auf Weiss gelaufen. Davor angewandt zieht
/// derselbe Regler die Zeichnung aus der Ueberstrahlung heraus. Das ist der ganze
/// Grund, warum EXR gelesen wird.
///
/// **Schwarzpunkt, Gamma und Kontrast wirken DANACH.** Es sind Anzeigegroessen; sie
/// biegen eine Kurve, die von 0 bis 1 laeuft. Vor der Sichtumwandlung angewandt
/// haetten sie keinen definierten Bezugspunkt, weil es dort kein Weiss gibt.
///
/// Die Saettigung steht dazwischen, auf der linearen Seite: die Rec.-709-Gewichte
/// sind fuer lineares Licht bestimmt.
/// </summary>
public static class FloatFrameProcessor
{
    // Rec.-709-Luminanz, hier als Gleitkomma - dieselben Gewichte wie im
    // Ganzzahlpfad, nur ohne die Festkomma-Rundung.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Rechnet den Frame mit Korrektur und Sichtumwandlung nach Bgra32.
    ///
    /// Geschrieben wird direkt in den Rueckpuffer der WriteableBitmap, wie im
    /// Ganzzahlpfad auch. Der Quellframe bleibt unangetastet: er wird beim naechsten
    /// Reglerzug erneut gebraucht, und ihn jedes Mal neu von der Platte zu lesen
    /// waere der Unterschied zwischen fluessig und zaeh.
    /// </summary>
    public static void Apply(FloatFrame frame, ImageAdjustments adjustments,
                             IViewTransform view, IntPtr destination, int destinationStride)
        => Apply(frame, adjustments, view, PreparedGrading.None, destination, destinationStride);

    /// <inheritdoc cref="Apply(FloatFrame, ImageAdjustments, IViewTransform, IntPtr, int)"/>
    /// <param name="grading">
    /// Die Werkzeuge, bereits vorbereitet und nach Seite getrennt. Sie wirken NACH
    /// der Grundkorrektur: erst Belichtung und Saettigung, dann die linearen
    /// Werkzeuge, dann die Sichtumwandlung, dann Tonwertkurve und die uebrigen.
    /// Die Grundkorrektur ist der schnelle Griff beim Beurteilen und bleibt deshalb
    /// vorn - wer sie umgehen will, laesst sie neutral.
    /// </param>
    /// <param name="step">
    /// Nur jeder n-te Bildpunkt wird gerechnet; die uebrigen bekommen seinen Wert.
    /// Bei 2 bleibt ein Viertel der Arbeit, bei 4 ein Sechzehntel.
    ///
    /// Gebraucht wird das beim Ziehen eines Reglers: ein 4K-Bild mit AgX kostet
    /// rund 367 ms je Aktualisierung, und dreimal je Sekunde neu zu zeichnen ist
    /// keine Bedienung. Dass die Vorschau dabei grob wird, faellt in der Bewegung
    /// nicht auf - beim Loslassen steht wieder das volle Bild.
    ///
    /// Die Bitmap behaelt ihre Groesse. Sie zu verkleinern waere der naheliegende
    /// Weg und der falsche: Zoom und Bildlage haengen daran, und beide duerfen
    /// waehrend eines Reglerzugs nicht springen.
    /// </param>
    public static unsafe void Apply(FloatFrame frame, ImageAdjustments adjustments,
                                    IViewTransform view, PreparedGrading grading,
                                    IntPtr destination, int destinationStride, int step = 1,
                                    OverlayPlan[]? overlays = null)
    {
        overlays ??= Overlays.None;

        // Ein oertliches Werkzeug braucht die Nachbarschaft und damit einen zweiten
        // Durchgang. Ohne eines geht es den geraden Weg - und das ist der Normalfall.
        if (grading.HasLocal)
        {
            ApplyLocal(frame, adjustments, view, grading, destination, destinationStride,
                       step, overlays);
            return;
        }

        var linearTools = grading.SceneLinear ?? Array.Empty<IGradingTool>();
        var displayTools = grading.Display ?? Array.Empty<IGradingTool>();

        step = Math.Clamp(step, 1, 16);

        byte* target = (byte*)destination.ToPointer();

        float gain = (float)Math.Pow(2.0, adjustments.Exposure);
        float saturation = (float)adjustments.Saturation;
        var channel = adjustments.Channel;

        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        bool needsTone = !Same(black, 0f) || !Same(white, 1f) ||
                         !Same(inverseGamma, 1f) || !Same(contrast, 1f);

        int width = frame.Width;
        var r = frame.R;
        var g = frame.G;
        var b = frame.B;
        var a = frame.A;

        var plan = new ShadePlan(gain, saturation, channel, black, span, inverseGamma, contrast,
                                 needsTone, view, linearTools, displayTools);

        int height = frame.Height;

        if (step == 1)
        {
            Parallel.For(0, height, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            y =>
            {
                byte* row = target + (long)y * destinationStride;

                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    float vr = r[i], vg = g[i], vb = b[i];
                    float alpha = a is null ? 1f : a[i];

                    Shade(in plan, ref vr, ref vg, ref vb, alpha);

                    // Ganz zum Schluss, auf den fertigen Anzeigewerten: Ein
                    // Wasserzeichen soll in jedem Bild gleich aussehen.
                    if (overlays.Length > 0) Overlays.Apply(overlays, x, y, ref vr, ref vg, ref vb);

                    byte* pixel = row + x * 4;
                    pixel[0] = ToByte(vb);
                    pixel[1] = ToByte(vg);
                    pixel[2] = ToByte(vr);
                    pixel[3] = ToByte(Math.Clamp(alpha, 0f, 1f));
                }
            });

            return;
        }

        // --- Der grobe Weg: rechnen auf einem Gitter, dazwischen interpolieren ---
        //
        // Erste Fassung fuellte jeden Block mit dem einen gerechneten Wert. Das ist
        // schnell und sieht aus wie ein Defekt: harte Quadrate, die man fuer einen
        // Fehler haelt statt fuer eine Zwischenstufe. Zwischen den Gitterpunkten zu
        // interpolieren kostet fast nichts mehr - es ist Speicherzugriff, keine
        // Rechnung - und ergibt eine Unschaerfe, die sich als "wird noch gerechnet"
        // liest.

        int gridWidth = (width + step - 1) / step + 1;
        int gridHeight = (height + step - 1) / step + 1;

        // Das Gitter selbst: bei 4K und Schrittweite vier sind das rund 2 MB, also
        // ein Fuenfzigstel des Bildes.
        var grid = new byte[gridWidth * gridHeight * 4];

        fixed (byte* gridBase = grid)
        {
            byte* gridPtr = gridBase;

            Parallel.For(0, gridHeight, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            gy =>
            {
                // Die letzte Gitterzeile liegt auf dem Rand, nicht darueber hinaus.
                int y = Math.Min(gy * step, height - 1);
                byte* row = gridPtr + (long)gy * gridWidth * 4;

                for (int gx = 0; gx < gridWidth; gx++)
                {
                    int x = Math.Min(gx * step, width - 1);
                    int i = y * width + x;

                    float vr = r[i], vg = g[i], vb = b[i];
                    float alpha = a is null ? 1f : a[i];

                    Shade(in plan, ref vr, ref vg, ref vb, alpha);

                    if (overlays.Length > 0) Overlays.Apply(overlays, x, y, ref vr, ref vg, ref vb);

                    byte* cell = row + gx * 4;
                    cell[0] = ToByte(vb);
                    cell[1] = ToByte(vg);
                    cell[2] = ToByte(vr);
                    cell[3] = ToByte(Math.Clamp(alpha, 0f, 1f));
                }
            });

            Expand(gridPtr, gridWidth, gridHeight, target, destinationStride, width, height, step);
        }
    }

    /// <summary>
    /// Der Teil des oertlichen Wegs, den beide Ausgaenge teilen: erst die
    /// Lichtwerkzeuge, dann die Sichtumwandlung, dann die uebrigen.
    ///
    /// An einer Stelle und nicht zweimal abgeschrieben. Die Vorschau und der
    /// Sechzehn-Bit-Ausgang muessen hier dasselbe rechnen - liefen sie auseinander,
    /// saehe der Export anders aus als das, was beim Einstellen auf dem Schirm stand,
    /// und das faende man erst am fertigen Film.
    /// </summary>
    /// <param name="split">
    /// True, wenn der Puffer noch vor der Sichtumwandlung steht und sie hier
    /// nachgeholt werden muss.
    /// </param>
    private static void RunLocal(ShadePlan plan, LocalPass.Scratch scratch, PreparedGrading grading,
                                 int gridWidth, int gridHeight, int imageWidth, int step, bool split)
    {
        if (split)
        {
            LocalPass.Run(scratch, grading.LocalLight, gridWidth, gridHeight, imageWidth, step);

            var values = scratch.Values;
            var alpha = scratch.Alpha;

            Parallel.For(0, gridHeight, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            gy =>
            {
                int row = gy * gridWidth;

                for (int gx = 0; gx < gridWidth; gx++)
                {
                    int at = (row + gx) * 3;

                    float vr = values[at], vg = values[at + 1], vb = values[at + 2];

                    // Die Deckung liegt als Byte daneben. Fuer die Kanalansicht ist
                    // das genau genug - sie zeigt ohnehin Bytes.
                    ShadeDisplay(in plan, ref vr, ref vg, ref vb, alpha[row + gx] / 255f);

                    values[at] = vr;
                    values[at + 1] = vg;
                    values[at + 2] = vb;
                }
            });
        }

        if (grading.Local.Length > 0)
            LocalPass.Run(scratch, grading.Local, gridWidth, gridHeight, imageWidth, step);
    }

    /// <summary>
    /// Blaest ein Bytegitter auf die volle Bildgroesse auf - bilinear zwischen den
    /// Gitterpunkten.
    ///
    /// Steht an einer Stelle, weil zwei Wege sie brauchen: der gerade und der mit
    /// oertlichen Werkzeugen. Zweimal abgeschrieben liefe sie beim naechsten Griff
    /// auseinander, und der Unterschied waere genau die Art Streifen, den man fuer
    /// ein Artefakt der Vorschau haelt.
    ///
    /// Die Gewichte stehen als Festkomma in einer Tabelle mit step Eintraegen. Die
    /// erste Fassung rechnete je Bildpunkt eine Division fuer den Gitterplatz und
    /// vier Vergleiche fuer die Raender - und war damit genauso teuer wie der volle
    /// Durchgang, also fuer nichts. Ueber die Bloecke zu laufen statt ueber die
    /// Bildpunkte macht beides ueberfluessig.
    /// </summary>
    private static unsafe void Expand(byte* gridPtr, int gridWidth, int gridHeight,
                                      byte* target, int destinationStride,
                                      int width, int height, int step)
    {
        var weights = new int[step];
        for (int i = 0; i < step; i++) weights[i] = i * 256 / step;

        fixed (int* weightBase = weights)
        {
            int* weight = weightBase;

            Parallel.For(0, gridHeight - 1, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            gy =>
            {
                int y0 = gy * step;
                int rows = Math.Min(step, height - y0);
                if (rows <= 0) return;

                byte* upperRow = gridPtr + (long)gy * gridWidth * 4;
                byte* lowerRow = gridPtr + (long)(gy + 1) * gridWidth * 4;

                for (int gx = 0; gx < gridWidth - 1; gx++)
                {
                    int x0 = gx * step;
                    int columns = Math.Min(step, width - x0);
                    if (columns <= 0) break;

                    // Die vier Ecken des Blocks - einmal je Block gelesen, nicht
                    // einmal je Bildpunkt.
                    byte* c00 = upperRow + gx * 4;
                    byte* c10 = c00 + 4;
                    byte* c01 = lowerRow + gx * 4;
                    byte* c11 = c01 + 4;

                    for (int dy = 0; dy < rows; dy++)
                    {
                        int wy = weight[dy];
                        byte* pixel = target + (long)(y0 + dy) * destinationStride + x0 * 4;

                        // Die beiden waagerechten Kanten des Blocks, auf dieser
                        // Zeile schon zusammengezogen.
                        int l0 = c00[0] + ((c01[0] - c00[0]) * wy >> 8);
                        int l1 = c00[1] + ((c01[1] - c00[1]) * wy >> 8);
                        int l2 = c00[2] + ((c01[2] - c00[2]) * wy >> 8);
                        int l3 = c00[3] + ((c01[3] - c00[3]) * wy >> 8);

                        int r0 = c10[0] + ((c11[0] - c10[0]) * wy >> 8);
                        int r1 = c10[1] + ((c11[1] - c10[1]) * wy >> 8);
                        int r2 = c10[2] + ((c11[2] - c10[2]) * wy >> 8);
                        int r3 = c10[3] + ((c11[3] - c10[3]) * wy >> 8);

                        for (int dx = 0; dx < columns; dx++)
                        {
                            int wx = weight[dx];

                            pixel[0] = (byte)(l0 + ((r0 - l0) * wx >> 8));
                            pixel[1] = (byte)(l1 + ((r1 - l1) * wx >> 8));
                            pixel[2] = (byte)(l2 + ((r2 - l2) * wx >> 8));
                            pixel[3] = (byte)(l3 + ((r3 - l3) * wx >> 8));

                            pixel += 4;
                        }
                    }
                }
            });
        }
    }

    /// <summary>
    /// Der Puffer des oertlichen Wegs - einer je Faden.
    ///
    /// Je Faden und nicht je Programm, weil der Stapellauf mehrere Bilder zugleich
    /// rechnet; ein geteilter Puffer waere ein Wettlauf. Und einmal statt je Bild,
    /// weil er gross ist: bei 4K rund dreihundert Megabyte, und dreihundertmal neu
    /// angelegt waere das der teuerste Teil des ganzen Laufs.
    ///
    /// Der Preis steht damit fest: So viele Puffer, wie der Lauf Faeden hat. Bei vier
    /// Arbeitern und 4K ist das gut ein Gigabyte - der Grund, warum dieser Weg nur
    /// genommen wird, wenn wirklich ein oertliches Werkzeug eingestellt ist.
    /// </summary>
    [ThreadStatic]
    private static LocalPass.Scratch? _scratch;

    /// <summary>
    /// Der Weg mit oertlichen Werkzeugen: erst alles in einen Puffer, dann
    /// weichzeichnen, dann hinausschreiben.
    ///
    /// Gerechnet wird immer auf einem Gitter - bei voller Aufloesung ist es das ganze
    /// Bild, beim Reglerzug jeder n-te Punkt. Ein Weg statt zwei: Die Verdopplung
    /// waere hier besonders teuer, weil die Fehler in der zweiten Fassung erst
    /// auffielen, wenn jemand waehrend eines Zugs genau hinsieht.
    /// </summary>
    private static unsafe void ApplyLocal(FloatFrame frame, ImageAdjustments adjustments,
                                          IViewTransform view, PreparedGrading grading,
                                          IntPtr destination, int destinationStride,
                                          int step, OverlayPlan[] overlays)
    {
        var plan = BuildPlan(adjustments, view, grading);

        int width = frame.Width;
        int height = frame.Height;

        int[] columns = LocalPass.Grid(width, step);
        int[] rows = LocalPass.Grid(height, step);

        var scratch = _scratch ??= new LocalPass.Scratch();
        scratch.Hold(columns.Length * rows.Length);

        var values = scratch.Values;
        var alpha = scratch.Alpha;

        var r = frame.R;
        var g = frame.G;
        var b = frame.B;
        var a = frame.A;

        int gridWidth = columns.Length;

        // Mit Lichtwerkzeugen endet der erste Durchgang VOR der Sichtumwandlung; sie
        // kommt dann im zweiten, nachdem Glanz und Halation ihr Licht verteilt haben.
        // Ohne sie bleibt alles in einem Zug - ein zusaetzlicher Lauf ueber den
        // Puffer kostet bei 4K rund dreihundert Megabyte hin und zurueck.
        bool split = grading.LocalLight.Length > 0;

        // --- erster Durchgang: die Kette bis hinter die Anzeigewerkzeuge ---
        Parallel.For(0, rows.Length, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            int y = rows[gy];
            int line = y * width;
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int i = line + columns[gx];

                float vr = r[i], vg = g[i], vb = b[i];
                float va = a is null ? 1f : a[i];

                if (split) ShadeLinear(in plan, ref vr, ref vg, ref vb);
                else Shade(in plan, ref vr, ref vg, ref vb, va);

                int at = (row + gx) * 3;
                values[at] = vr;
                values[at + 1] = vg;
                values[at + 2] = vb;

                alpha[row + gx] = ToByte(Math.Clamp(va, 0f, 1f));
            }
        });

        // --- weichzeichnen und durch die Werkzeuge ---
        RunLocal(plan, scratch, grading, gridWidth, rows.Length, width, step, split);

        // --- zweiter Durchgang: hinausschreiben ---
        byte* target = (byte*)destination.ToPointer();

        if (step == 1)
        {
            Parallel.For(0, rows.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            gy =>
            {
                byte* line = target + (long)gy * destinationStride;
                int row = gy * gridWidth;

                for (int gx = 0; gx < gridWidth; gx++)
                {
                    int at = (row + gx) * 3;

                    float vr = values[at], vg = values[at + 1], vb = values[at + 2];

                    // Das Wasserzeichen zuletzt - auch hier. Es soll von einem
                    // oertlichen Werkzeug so wenig beruehrt werden wie von einer
                    // Kurve.
                    if (overlays.Length > 0)
                        Overlays.Apply(overlays, columns[gx], rows[gy], ref vr, ref vg, ref vb);

                    byte* pixel = line + gx * 4;
                    pixel[0] = ToByte(vb);
                    pixel[1] = ToByte(vg);
                    pixel[2] = ToByte(vr);
                    pixel[3] = alpha[row + gx];
                }
            });

            return;
        }

        // Grob: erst in ein Bytegitter, dann dazwischen interpolieren - genau wie im
        // geraden Weg, und mit demselben Gitter.
        var grid = new byte[gridWidth * rows.Length * 4];

        fixed (byte* gridBase = grid)
        {
            byte* gridPtr = gridBase;

            Parallel.For(0, rows.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            gy =>
            {
                byte* line = gridPtr + (long)gy * gridWidth * 4;
                int row = gy * gridWidth;

                for (int gx = 0; gx < gridWidth; gx++)
                {
                    int at = (row + gx) * 3;

                    float vr = values[at], vg = values[at + 1], vb = values[at + 2];

                    if (overlays.Length > 0)
                        Overlays.Apply(overlays, columns[gx], rows[gy], ref vr, ref vg, ref vb);

                    byte* cell = line + gx * 4;
                    cell[0] = ToByte(vb);
                    cell[1] = ToByte(vg);
                    cell[2] = ToByte(vr);
                    cell[3] = alpha[row + gx];
                }
            });

            Expand(gridPtr, gridWidth, rows.Length, target, destinationStride, width, height, step);
        }
    }

    /// <summary>
    /// Alles, was einmal je Bild feststeht. Als Struktur, damit die innere Schleife
    /// nicht zwoelf Einzelwerte durchreichen muss.
    /// </summary>
    private readonly struct ShadePlan
    {
        public ShadePlan(float gain, float saturation, ChannelView channel,
                         float black, float span, float inverseGamma, float contrast, bool needsTone,
                         IViewTransform view, IGradingTool[] linear, IGradingTool[] display)
        {
            Gain = gain;
            Saturation = saturation;
            Channel = channel;
            Black = black;
            Span = span;
            InverseGamma = inverseGamma;
            Contrast = contrast;
            NeedsTone = needsTone;
            View = view;
            Linear = linear;
            Display = display;
        }

        public readonly float Gain, Saturation, Black, Span, InverseGamma, Contrast;
        public readonly bool NeedsTone;
        public readonly ChannelView Channel;
        public readonly IViewTransform View;
        public readonly IGradingTool[] Linear, Display;
    }

    /// <summary>
    /// Die ganze Kette fuer einen Bildpunkt - von linearem Szenenlicht zu
    /// Anzeigewerten zwischen 0 und 1.
    ///
    /// Steht an einer Stelle, weil sie an zwei gebraucht wird: fuer die Anzeige mit
    /// acht Bit und fuer den Export mit sechzehn. Zweimal abgeschrieben liefe sie
    /// beim naechsten Werkzeug auseinander, und dann saehe das Ergebnis anders aus
    /// als die Vorschau, auf die jemand sich verlassen hat.
    /// </summary>
    private static void Shade(in ShadePlan plan, ref float vr, ref float vg, ref float vb, float alpha)
    {
        ShadeLinear(in plan, ref vr, ref vg, ref vb);
        ShadeDisplay(in plan, ref vr, ref vg, ref vb, alpha);
    }

    /// <summary>
    /// Die Kette bis VOR die Sichtumwandlung. Heraus kommt lineares Licht, unbegrenzt.
    ///
    /// Getrennt, weil zwischen die beiden Haelften etwas passt: die Lichtwerkzeuge.
    /// Glanz und Halation brauchen die Ueberhellen, und hinter der Umwandlung gibt es
    /// die nicht mehr. Wer beides in einem Zug rechnet, kann dort nichts einschieben.
    /// </summary>
    private static void ShadeLinear(in ShadePlan plan, ref float vr, ref float vg, ref float vb)
    {
        // --- lineare Seite ---

        if (plan.Gain != 1f)
        {
            vr *= plan.Gain;
            vg *= plan.Gain;
            vb *= plan.Gain;
        }

        if (!Same(plan.Saturation, 1f))
        {
            float luma = LumaR * vr + LumaG * vg + LumaB * vb;
            vr = luma + (vr - luma) * plan.Saturation;
            vg = luma + (vg - luma) * plan.Saturation;
            vb = luma + (vb - luma) * plan.Saturation;

            // Uebersaettigung kann unter null druecken; negatives Licht gibt es
            // nicht, und die Sichtumwandlung koennte damit nichts anfangen.
            if (vr < 0) vr = 0;
            if (vg < 0) vg = 0;
            if (vb < 0) vb = 0;
        }

        var linear = plan.Linear;
        for (int t = 0; t < linear.Length; t++) linear[t].Apply(ref vr, ref vg, ref vb);
    }

    /// <summary>Die Sichtumwandlung und alles dahinter.</summary>
    private static void ShadeDisplay(in ShadePlan plan, ref float vr, ref float vg, ref float vb,
                                     float alpha)
    {
        // --- Sichtumwandlung: ab hier sind es Anzeigewerte von 0 bis 1 ---

        plan.View.Apply(ref vr, ref vg, ref vb);

        // --- Anzeigeseite ---

        if (plan.NeedsTone)
        {
            vr = Tone(vr, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
            vg = Tone(vg, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
            vb = Tone(vb, plan.Black, plan.Span, plan.InverseGamma, plan.Contrast);
        }

        var display = plan.Display;
        for (int t = 0; t < display.Length; t++) display[t].Apply(ref vr, ref vg, ref vb);

        if (plan.Channel == ChannelView.All) return;

        switch (plan.Channel)
        {
            case ChannelView.Red: vg = vb = vr; break;
            case ChannelView.Green: vr = vb = vg; break;
            case ChannelView.Blue: vr = vg = vb; break;
            case ChannelView.Alpha: vr = vg = vb = Math.Clamp(alpha, 0f, 1f); break;
            case ChannelView.Luminance:
                vr = vg = vb = LumaR * vr + LumaG * vg + LumaB * vb;
                break;
        }
    }

    /// <summary>
    /// Wie <see cref="Apply(FloatFrame, ImageAdjustments, IViewTransform, PreparedGrading, IntPtr, int, int)"/>,
    /// aber nach Rgba64 - sechzehn Bit je Kanal, Reihenfolge R, G, B, A.
    ///
    /// Fuer den Export. Acht Bit wuerden dort genau das wegwerfen, was die Korrektur
    /// eben gewonnen hat: Ein Verlauf, den die Kurve gestreckt hat, zeigt auf acht
    /// Bit Stufen, wo vorher keine waren - und eine Sequenz, die danach noch durch
    /// eine Farbkorrektur soll, hat davon nichts mehr.
    /// </summary>
    public static unsafe void ApplyRgba64(FloatFrame frame, ImageAdjustments adjustments,
                                          IViewTransform view, PreparedGrading grading,
                                          IntPtr destination, int destinationStride,
                                          OverlayPlan[]? overlays = null)
    {
        overlays ??= Overlays.None;
        var plan = BuildPlan(adjustments, view, grading);
        ushort* target = (ushort*)destination.ToPointer();

        int width = frame.Width;
        int height = frame.Height;

        var r = frame.R;
        var g = frame.G;
        var b = frame.B;
        var a = frame.A;

        // Die oertlichen Werkzeuge brauchen auch hier ihren Puffer. Ohne sie bleibt
        // der Weg, der er war - ein Durchgang, kein Zwischenspeicher.
        LocalPass.Scratch? scratch = null;

        if (grading.HasLocal)
        {
            bool split = grading.LocalLight.Length > 0;

            scratch = new LocalPass.Scratch();
            scratch.Hold(width * height);

            var values = scratch.Values;
            var opacity = scratch.Alpha;

            Parallel.For(0, height, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            },
            y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    float vr = r[i], vg = g[i], vb = b[i];
                    float va = a is null ? 1f : a[i];

                    if (split) ShadeLinear(in plan, ref vr, ref vg, ref vb);
                    else Shade(in plan, ref vr, ref vg, ref vb, va);

                    int at = i * 3;
                    values[at] = vr;
                    values[at + 1] = vg;
                    values[at + 2] = vb;

                    opacity[i] = ToByte(Math.Clamp(va, 0f, 1f));
                }
            });

            RunLocal(plan, scratch, grading, width, height, width, step: 1, split);
        }

        var shaded = scratch?.Values;

        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            ushort* row = (ushort*)((byte*)target + (long)y * destinationStride);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float alpha = a is null ? 1f : a[i];

                float vr, vg, vb;

                if (shaded is not null)
                {
                    int at = i * 3;
                    vr = shaded[at];
                    vg = shaded[at + 1];
                    vb = shaded[at + 2];
                }
                else
                {
                    vr = r[i];
                    vg = g[i];
                    vb = b[i];

                    Shade(in plan, ref vr, ref vg, ref vb, alpha);
                }

                if (overlays.Length > 0) Overlays.Apply(overlays, x, y, ref vr, ref vg, ref vb);

                ushort* pixel = row + x * 4;
                pixel[0] = ToUShort(vr);
                pixel[1] = ToUShort(vg);
                pixel[2] = ToUShort(vb);
                pixel[3] = ToUShort(Math.Clamp(alpha, 0f, 1f));
            }
        });
    }

    private static ShadePlan BuildPlan(ImageAdjustments adjustments, IViewTransform view, PreparedGrading grading)
    {
        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        bool needsTone = !Same(black, 0f) || !Same(white, 1f) ||
                         !Same(inverseGamma, 1f) || !Same(contrast, 1f);

        return new ShadePlan((float)Math.Pow(2.0, adjustments.Exposure), (float)adjustments.Saturation,
                             adjustments.Channel, black, span, inverseGamma, contrast, needsTone, view,
                             grading.SceneLinear ?? Array.Empty<IGradingTool>(),
                             grading.Display ?? Array.Empty<IGradingTool>());
    }

    private static ushort ToUShort(float value)
        => (ushort)Math.Clamp(MathF.Round(value * 65535f), 0f, 65535f);

    /// <summary>
    /// Die Tonwertkurve auf der Anzeigeseite. Dieselben vier Schritte wie im
    /// Ganzzahlpfad, nur ohne die Tabelle mit 256 Eintraegen: die Werte sind hier
    /// nicht mehr abzaehlbar.
    /// </summary>
    private static float Tone(float v, float black, float span, float inverseGamma, float contrast)
    {
        v = (v - black) / span;
        v = Math.Clamp(v, 0f, 1f);
        if (!Same(inverseGamma, 1f)) v = MathF.Pow(v, inverseGamma);
        if (!Same(contrast, 1f)) v = (v - 0.5f) * contrast + 0.5f;

        return v;
    }

    /// <summary>
    /// Misst die Verteilung. Gemessen wird auf dem korrigierten Bild, damit im
    /// Histogramm steht, was zu sehen ist - dieselbe Regel wie im Ganzzahlpfad.
    ///
    /// Dazu kommt eine Angabe, die es nur hier geben kann: wieviele Pixel in der
    /// DATEI oberhalb von Weiss liegen. Das ist etwas anderes als Ueberstrahlung in
    /// der Anzeige - es ist die Reserve, die der Belichtungsregler noch heben kann.
    /// </summary>
    public static void Measure(FloatFrame frame, ImageAdjustments adjustments, IViewTransform view,
                               Histogram histogram, int step = 1)
        => Measure(frame, adjustments, view, PreparedGrading.None, histogram, step);

    /// <inheritdoc cref="Measure(FloatFrame, ImageAdjustments, IViewTransform, Histogram, int)"/>
    public static void Measure(FloatFrame frame, ImageAdjustments adjustments, IViewTransform view,
                               PreparedGrading grading, Histogram histogram, int step = 1)
    {
        var linearTools = grading.SceneLinear ?? Array.Empty<IGradingTool>();
        var displayTools = grading.Display ?? Array.Empty<IGradingTool>();

        histogram.Clear();
        step = Math.Max(1, step);

        float gain = (float)Math.Pow(2.0, adjustments.Exposure);
        float saturation = (float)adjustments.Saturation;

        float black = (float)adjustments.BlackPoint;
        float white = (float)adjustments.WhitePoint;
        float span = white - black;
        if (MathF.Abs(span) < 1e-6f) span = 1e-6f;

        float inverseGamma = 1f / MathF.Max(0.0001f, (float)adjustments.Gamma);
        float contrast = (float)adjustments.Contrast;

        long sampled = 0;
        long aboveWhite = 0;

        for (int y = 0; y < frame.Height; y += step)
        {
            for (int x = 0; x < frame.Width; x += step)
            {
                int i = y * frame.Width + x;
                float vr = frame.R[i], vg = frame.G[i], vb = frame.B[i];

                // Vor jeder Korrektur: liegt hier Reserve oberhalb von Weiss?
                if (vr > 1f || vg > 1f || vb > 1f) aboveWhite++;

                vr *= gain;
                vg *= gain;
                vb *= gain;

                if (!Same(saturation, 1f))
                {
                    float luma = LumaR * vr + LumaG * vg + LumaB * vb;
                    vr = MathF.Max(0f, luma + (vr - luma) * saturation);
                    vg = MathF.Max(0f, luma + (vg - luma) * saturation);
                    vb = MathF.Max(0f, luma + (vb - luma) * saturation);
                }

                for (int t = 0; t < linearTools.Length; t++)
                    linearTools[t].Apply(ref vr, ref vg, ref vb);

                view.Apply(ref vr, ref vg, ref vb);

                vr = Tone(vr, black, span, inverseGamma, contrast);
                vg = Tone(vg, black, span, inverseGamma, contrast);
                vb = Tone(vb, black, span, inverseGamma, contrast);

                for (int t = 0; t < displayTools.Length; t++)
                    displayTools[t].Apply(ref vr, ref vg, ref vb);

                int br = ToByte(vr), bg = ToByte(vg), bb = ToByte(vb);

                histogram.Red[br]++;
                histogram.Green[bg]++;
                histogram.Blue[bb]++;
                histogram.Luma[(int)Math.Clamp(MathF.Round(LumaR * br + LumaG * bg + LumaB * bb), 0f, 255f)]++;
                sampled++;
            }
        }

        histogram.Finish(sampled);
        histogram.AboveWhite = sampled > 0 ? aboveWhite / (double)sampled : 0;
    }

    private static bool Same(float value, float reference) => MathF.Abs(value - reference) < 0.001f;

    private static byte ToByte(float value)
        => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
