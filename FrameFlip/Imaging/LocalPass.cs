using FrameFlip.Imaging.Grading;

namespace FrameFlip.Imaging;

/// <summary>
/// Der zweite Durchgang: die Werkzeuge mit oertlicher Wirkung.
///
/// Sie brauchen die Nachbarschaft eines Punktes, und die gibt es nicht, solange ein
/// Punkt nach dem anderen gerechnet und gleich hinausgeschrieben wird. Deshalb
/// laeuft hier ein anderer Weg:
///
///   1. Die ganze Kette bis hinter die Anzeigewerkzeuge, in einen Zwischenpuffer.
///   2. Dieser Puffer weichgezeichnet - einmal, fuer alle Werkzeuge zusammen.
///   3. Beides zusammen durch die oertlichen Werkzeuge, dann hinaus.
///
/// Drei Puffer zu je drei Gleitkommawerten - Werte, Weichzeichnung und das Feld, ueber
/// das der Kastenfilter laeuft: bei 1080p rund fuenfundsiebzig Megabyte, bei 4K rund
/// dreihundert. Das ist der Preis, und er faellt nur an, wenn wirklich ein oertliches
/// Werkzeug eingestellt ist - sonst nimmt der Bildweg den geraden.
///
/// Beim Ziehen an einem Regler wird nur das Gitter gerechnet, das die Anzeige danach
/// liest, und die Weichzeichnung laeuft auf diesem Gitter mit entsprechend kleinerem
/// Radius. Das ist nicht nur schneller, sondern auch richtig: Ein verkleinertes Bild
/// mit kleinerem Radius weichgezeichnet ist dasselbe wie das grosse mit grossem.
/// </summary>
public static class LocalPass
{
    /// <summary>Der Zwischenpuffer eines Durchgangs. Wird wiederverwendet.</summary>
    public sealed class Scratch
    {
        public float[] Values = Array.Empty<float>();
        public float[] Blurred = Array.Empty<float>();
        public float[] Work = Array.Empty<float>();
        public byte[] Alpha = Array.Empty<byte>();

        /// <summary>Sorgt dafuer, dass Platz fuer so viele Punkte da ist.</summary>
        public void Hold(int count)
        {
            if (Values.Length >= count * 3) return;

            Values = new float[count * 3];
            Blurred = new float[count * 3];
            Work = new float[count * 3];
            Alpha = new byte[count];
        }
    }

    /// <summary>
    /// Die Stellen, an denen gerechnet wird - dieselben, die der grobe Durchgang der
    /// Anzeige liest.
    ///
    /// Dieselbe Formel wie im Composer und im Bildweg. Sie muss an allen drei Stellen
    /// dieselbe sein, sonst rechnet einer einen Streifen, den ein anderer nie liest.
    /// </summary>
    public static int[] Grid(int size, int step)
    {
        if (step <= 1)
        {
            var all = new int[size];
            for (int i = 0; i < size; i++) all[i] = i;

            return all;
        }

        int count = (size + step - 1) / step + 1;
        var grid = new int[count];

        for (int i = 0; i < count; i++) grid[i] = Math.Min(i * step, size - 1);

        return grid;
    }

    /// <summary>
    /// Zeichnet den gefuellten Puffer weich und schickt ihn durch die Werkzeuge.
    /// </summary>
    /// <param name="reach">
    /// Der Radius bei voller Aufloesung, bezogen auf 1080p. Umgerechnet wird hier,
    /// weil hier die Bildgroesse bekannt ist: Derselbe Regler soll auf 4K dieselbe
    /// Wirkung haben, und in Bildpunkten waere er dort ein Viertel so gross.
    /// </param>
    public static void Run(Scratch scratch, ILocalTool[] tools, int reach,
                           int gridWidth, int gridHeight, int imageWidth, int step)
    {
        if (tools.Length == 0) return;

        int radius = RadiusFor(reach, imageWidth, step);

        Array.Copy(scratch.Values, scratch.Blurred, gridWidth * gridHeight * 3);
        Blur.Apply(scratch.Blurred, gridWidth, gridHeight, radius, scratch.Work);

        var values = scratch.Values;
        var blurred = scratch.Blurred;

        Parallel.For(0, gridHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            int row = gy * gridWidth * 3;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = row + gx * 3;

                float vr = values[at], vg = values[at + 1], vb = values[at + 2];

                for (int t = 0; t < tools.Length; t++)
                    tools[t].Apply(ref vr, ref vg, ref vb,
                                   blurred[at], blurred[at + 1], blurred[at + 2]);

                values[at] = vr;
                values[at + 1] = vg;
                values[at + 2] = vb;
            }
        });
    }

    /// <summary>
    /// Der Radius in Punkten des Gitters, auf dem wirklich gerechnet wird.
    ///
    /// Zwei Umrechnungen hintereinander: erst von 1080p auf die Bildbreite, dann von
    /// Bildpunkten auf Gitterpunkte. Mindestens eins - ein Radius von null waere
    /// keine Weichzeichnung, und das Werkzeug taete dann gar nichts, ohne dass man
    /// den Grund saehe.
    /// </summary>
    public static int RadiusFor(int reach, int imageWidth, int step)
    {
        float scaled = reach * imageWidth / 1920f;

        return Math.Clamp((int)MathF.Round(scaled / Math.Max(1, step)), 1, 400);
    }
}
