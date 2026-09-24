using System.Text.Json.Serialization;
using FrameFlip.Decoding.Exr;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Was ein Werkzeug aus der Datei braucht, ausser dem Bild selbst.
///
/// Eine Art und kein Name, weil der Name nicht feststeht: Blender schreibt den
/// Tiefenpass als "ViewLayer.Depth", aeltere Fassungen als "Z", und wer mit Nebel
/// arbeitet, hat statt dessen "Mist". Das Werkzeug sagt, WAS es braucht; welcher
/// Pass das in dieser Datei ist, entscheidet sich beim Lesen.
/// </summary>
public enum PassNeed
{
    /// <summary>Entfernung je Bildpunkt - Depth, Z oder ersatzweise Mist.</summary>
    Depth,

    /// <summary>Bewegung je Bildpunkt - der Vektorpass mit vier Kanaelen.</summary>
    Motion,

    /// <summary>Wohin eine Flaeche zeigt - der Normalpass mit drei Kanaelen.</summary>
    Normal,

    /// <summary>
    /// Gar keine. Fuer ein Werkzeug, das auch ohne Renderdaten etwas zu tun hat.
    ///
    /// Es steht hier und nicht als eigene Werkzeugart, weil sonst dasselbe Werkzeug
    /// zweimal existieren muesste - einmal mit Pass und einmal ohne, mit denselben
    /// Reglern und derselben Rechnung. Wer die Verschiebung von der Geometrie auf
    /// "nur Welle" umstellt, will kein anderes Werkzeug, sondern dasselbe ohne
    /// Datei.
    /// </summary>
    None,
}

/// <summary>
/// Die Reihenfolge der Werkzeuge mit Renderdaten.
///
/// Bewegung vor Schaerfe: Die Verschluszeit sammelt ueber die Zeit, das Objektiv
/// zeichnet, was in diesem Augenblick ankommt. Beides zugleich ist das Richtige und
/// hinterher nicht mehr zu trennen; von den beiden moeglichen Reihenfolgen ist diese
/// die, die bei einem Gegenstand in der Schaerfeebene stimmt.
/// </summary>
public enum DataStage
{
    Motion = 0,
    Focus = 1,
}

/// <summary>
/// Ein Werkzeug, das ausser dem Bild noch Renderdaten braucht.
///
/// Die fuenfte Art, und die einzige, die es ohne EXR gar nicht geben kann. Alles
/// andere im Atelier liesse sich auf einem JPEG rechnen - schlechter, aber es liefe.
/// Diese hier haben nichts zu rechnen, wenn die Datei den Pass nicht fuehrt, und
/// genau das ist der Grund, warum es das Atelier gibt.
///
/// Sie rechnen auf dem ganzen Puffer und nicht je Bildpunkt: Wer die Entfernung
/// kennt, will damit meistens die NACHBARSCHAFT eines Punktes anders behandeln, und
/// das geht nur mit dem Bild im Zugriff.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(DepthFieldTool), DepthFieldTool.KindName)]
[JsonDerivedType(typeof(MotionBlurTool), MotionBlurTool.KindName)]
[JsonDerivedType(typeof(DisplaceTool), DisplaceTool.KindName)]
public interface IDataTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    /// <summary>Welche Renderdaten gebraucht werden.</summary>
    PassNeed Needs { get; }

    /// <summary>
    /// True, wenn das Werkzeug auch OHNE seinen Pass etwas zu tun hat.
    ///
    /// Die Grundstellung ist false, und das ist die richtige: Eine Tiefenschaerfe
    /// ohne Tiefe muesste sich die Entfernung ausdenken, und ein Regler, der etwas
    /// erfindet, ist schlimmer als einer, der ruht.
    ///
    /// Die Verschiebung ist der Fall, fuer den es die Ausnahme gibt: Ihre Welle
    /// braucht nichts als das Bild. Nur die RICHTUNG kommt aus dem Pass, und wer auf
    /// "nur Welle" stellt, hat sie schon anders beantwortet.
    /// </summary>
    bool Optional => false;

    /// <summary>Wann das Werkzeug an der Reihe ist - die Liste entscheidet nicht.</summary>
    DataStage Stage { get; }

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    bool IsNeutral { get; }

    void Prepare();

    /// <summary>
    /// Rechnet auf dem ganzen Puffer.
    /// </summary>
    /// <param name="data">
    /// Der Pass, in voller Bildgroesse. Null nur bei <see cref="Optional"/>.
    /// </param>
    /// <param name="columns">Zu welcher Bildspalte eine Gitterspalte gehoert.</param>
    /// <param name="rows">Dasselbe fuer die Zeilen.</param>
    /// <param name="number">
    /// Die Nummer des Bildes in der Sequenz. Gebraucht von allem, was sich ueber die
    /// Zeit bewegen soll - eine Welle, die ueber alle Bilder gleich steht, sieht aus
    /// wie ein Aufkleber auf der Linse.
    /// </param>
    void Run(LocalPass.Scratch scratch, FloatFrame? data, int[] columns, int[] rows,
             int imageWidth, int step, int number = 0);
}

/// <summary>
/// Nachtraegliche Tiefenschaerfe.
///
/// Das Werkzeug, das am deutlichsten zeigt, wozu die EXR da ist: Es braucht die
/// Entfernung je Bildpunkt, und die steht in keinem fertigen Bild. Wer sie hat, kann
/// die Schaerfentiefe nach dem Rendern aendern - und zwar in Sekunden statt in einer
/// weiteren Nacht.
///
/// GERECHNET WIRD MIT DEM KEHRWERT der Entfernung. Unschaerfe waechst nicht mit dem
/// Abstand, sondern mit dem Unterschied der Kehrwerte: Zwischen zwei und vier Metern
/// liegt optisch dasselbe wie zwischen vier Metern und unendlich. Wer linear rechnet,
/// bekommt einen Vordergrund, der gar nicht unscharf wird, und einen Hintergrund, der
/// ab einer Grenze gleichmaessig matschig ist.
///
/// UND ES IST EINE NAEHERUNG. Eine echte Tiefenschaerfe entsteht beim Sammeln des
/// Lichts, und dabei verdeckt Scharfes vor Unscharfem einander teilweise. Hinterher
/// laesst sich das nicht wiederherstellen: Was hinter einer scharfen Kante liegt,
/// steht nicht in der Datei. Sichtbar wird das an genau einer Stelle - ein scharfer
/// Gegenstand vor unscharfem Hintergrund behaelt einen zu scharfen Rand, weil der
/// unscharfe Hintergrund nicht ueber ihn kriechen kann. Wer das nicht will, rendert
/// die Schaerfentiefe. Fuer alles andere - Probieren, Nachjustieren, ein zweiter
/// Blick - ist das hier der schnellere Weg.
///
/// Gerechnet wird ueber drei Stufen: scharf, halb, ganz. Dazwischen wird gemischt.
/// Ein Sammeln ueber eine Scheibe mit veraenderlichem Radius waere das Richtige und
/// kostete bei Radius zwanzig zwoelfhundert Abtastungen je Bildpunkt; drei Stufen
/// kosten zwei Weichzeichnungen fuer das ganze Bild.
/// </summary>
public sealed class DepthFieldTool : IDataTool
{
    public const string KindName = "depth-field";

    /// <summary>Der groesste Zerstreuungskreis in Bildpunkten, bezogen auf 1080p.</summary>
    private const float FullBlur = 48f;

    public string Kind => KindName;

    public PassNeed Needs => PassNeed.Depth;

    public DataStage Stage => DataStage.Focus;

    /// <summary>0 bis 1. Wie weit die Blende geoeffnet ist.</summary>
    public float Aperture { get; set; }

    /// <summary>
    /// Die Entfernung, die scharf bleibt - in den Einheiten der Datei.
    ///
    /// Blender schreibt Meter, wenn die Szene in Metern gebaut ist. Eine Zahl ohne
    /// Einheit waere hier ehrlicher und im Gebrauch schlechter: Wer weiss, wie weit
    /// seine Kamera von seinem Gegenstand steht, kann sie eintragen.
    /// </summary>
    public float Focus { get; set; } = 5f;

    [JsonIgnore]
    public bool IsNeutral => Aperture < 0.005f;

    private float _reach;
    private float _focus;

    public void Prepare()
    {
        _focus = MathF.Max(0.01f, Focus);

        // Die Blende als Faktor auf den Unterschied der Kehrwerte. Der Bezug auf die
        // Fokusentfernung macht den Regler ueber Szenen hinweg vergleichbar: Bei
        // voller Blende ist das Doppelte der Fokusentfernung ganz unscharf, egal ob
        // das zwei Meter sind oder zweihundert.
        _reach = Math.Clamp(Aperture, 0f, 1f) * 4f * _focus;
    }

    public void Run(LocalPass.Scratch scratch, FloatFrame? data, int[] columns, int[] rows,
                    int imageWidth, int step, int number = 0)
    {
        // Ohne Tiefenpass gibt es keine Entfernung, nach der sich die Schaerfe
        // richten koennte. Der Aufrufer laesst das Werkzeug dann ohnehin ruhen -
        // siehe Optional -, aber die Zusage steht besser hier.
        if (data is null) return;

        int gridWidth = columns.Length;
        int gridHeight = rows.Length;
        int count = gridWidth * gridHeight * 3;

        // Derselbe Weg wie bei den oertlichen Werkzeugen: auf 1080p bezogen, auf die
        // Bildbreite umgerechnet, auf die Gitterweite heruntergeteilt.
        int radius = LocalPass.RadiusFor((int)FullBlur, imageWidth, step);

        scratch.HoldLevels(gridWidth * gridHeight);

        // Zwei Stufen: halb und ganz. Die zweite entsteht aus der ersten - zweimal
        // weichgezeichnet ist weiter weichgezeichnet, und das spart den zweiten Lauf
        // ueber das volle Bild.
        Array.Copy(scratch.Values, scratch.Blurred, count);
        Blur.Apply(scratch.Blurred, gridWidth, gridHeight, Math.Max(1, radius / 2), scratch.Work);

        Array.Copy(scratch.Blurred, scratch.Levels, count);
        Blur.Apply(scratch.Levels, gridWidth, gridHeight, Math.Max(1, radius / 2), scratch.Work);

        var values = scratch.Values;
        var half = scratch.Blurred;
        var full = scratch.Levels;

        float focus = _focus;
        float reach = _reach;

        var depth = data.R;
        int dataWidth = data.Width;
        int dataHeight = data.Height;

        float inverseFocus = 1f / focus;

        Parallel.For(0, gridHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            int line = Math.Min(rows[gy], dataHeight - 1) * dataWidth;
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                float distance = depth[line + Math.Min(columns[gx], dataWidth - 1)];

                // Wo nichts getroffen wurde, ist es unendlich weit weg - und damit
                // so unscharf, wie die Blende es hergibt.
                float inverse = !float.IsFinite(distance) || distance >= FloatFrame.NotHit || distance <= 0f
                    ? 0f
                    : 1f / distance;

                float circle = MathF.Min(1f, MathF.Abs(inverse - inverseFocus) * reach);

                int at = (row + gx) * 3;
                if (circle <= 0f) continue;

                // Die untere Haelfte mischt scharf mit halb, die obere halb mit ganz.
                if (circle < 0.5f)
                {
                    float t = circle * 2f;

                    values[at] += (half[at] - values[at]) * t;
                    values[at + 1] += (half[at + 1] - values[at + 1]) * t;
                    values[at + 2] += (half[at + 2] - values[at + 2]) * t;
                }
                else
                {
                    float t = (circle - 0.5f) * 2f;

                    values[at] = half[at] + (full[at] - half[at]) * t;
                    values[at + 1] = half[at + 1] + (full[at + 1] - half[at + 1]) * t;
                    values[at + 2] = half[at + 2] + (full[at + 2] - half[at + 2]) * t;
                }
            }
        });
    }
}

/// <summary>
/// Nachtraegliche Bewegungsunschaerfe.
///
/// Der Vektorpass sagt je Bildpunkt, wo derselbe Gegenstand im vorigen Bild WAR und
/// wo er im naechsten SEIN WIRD - in Bildpunkten. Damit laesst sich nachtraeglich
/// verschmieren, was sich bewegt hat, ohne das Bild noch einmal zu rechnen. Ein
/// Rendern mit echter Bewegungsunschaerfe kostet ein Vielfaches an Abtastungen; hier
/// kostet es einen Durchgang.
///
/// Gesammelt wird entlang der Strecke, die der Punkt waehrend der Verschlusszeit
/// zurueckgelegt hat: vom halben Rueckwaertsvektor bis zum halben Vorwaertsvektor.
/// Der Verschluss steht dabei fuer den Anteil der Bildzeit, in dem er offen war -
/// eins heisst "die ganze Zeit", ein Halb entspricht 180 Grad und ist das, was eine
/// Filmkamera tut.
///
/// UND ES IST EINE NAEHERUNG, mit demselben benennbaren Fehler wie die
/// Tiefenschaerfe: Ein bewegter Gegenstand verschmiert in sich, aber nicht ueber
/// seine Kante hinaus. Dahinter liegt Hintergrund, der stillsteht und deshalb von
/// sich selbst sammelt - was der Gegenstand im Voruebergehen verdeckt haette, steht
/// nicht in der Datei. Bewegte Kanten bleiben dadurch schaerfer, als sie sein
/// sollten. Wer das braucht, rendert die Bewegungsunschaerfe.
///
/// Die Y-Achse wird umgedreht. Blender rechnet Bildschirmkoordinaten von unten nach
/// oben, die Zeilen eines Bildes laufen von oben nach unten. Ohne das Umdrehen zoege
/// die Unschaerfe senkrecht in die falsche Richtung - und zwar nur senkrecht, was
/// beim ersten Hinsehen aussieht wie ein Fehler im Vektorpass.
/// </summary>
public sealed class MotionBlurTool : IDataTool
{
    public const string KindName = "motion-blur";

    /// <summary>Weiter zu gehen waere kein Verschluss mehr, sondern ein Effekt.</summary>
    private const float LongestShutter = 2f;

    public string Kind => KindName;

    public PassNeed Needs => PassNeed.Motion;

    public DataStage Stage => DataStage.Motion;

    /// <summary>
    /// Der Anteil der Bildzeit, in dem der Verschluss offen ist.
    ///
    /// Ein Halb sind 180 Grad - die uebliche Wahl beim Film, und das, was Blender
    /// beim Rendern voreinstellt. Null heisst: aus.
    /// </summary>
    public float Shutter { get; set; }

    /// <summary>
    /// Wie oft entlang der Strecke abgetastet wird.
    ///
    /// Zu wenige, und eine schnelle Bewegung wird zu einer Reihe von Geisterbildern
    /// statt zu einer Spur. Die noetige Zahl haengt an der Laenge der Strecke, nicht
    /// am Geschmack - deshalb steht hier eine Zahl und kein Regler mit Prozenten.
    /// </summary>
    public int Samples { get; set; } = 12;

    [JsonIgnore]
    public bool IsNeutral => Shutter < 0.005f;

    private float _shutter;
    private int _samples;

    public void Prepare()
    {
        _shutter = Math.Clamp(Shutter, 0f, LongestShutter);
        _samples = Math.Clamp(Samples, 2, 48);
    }

    public void Run(LocalPass.Scratch scratch, FloatFrame? data, int[] columns, int[] rows,
                    int imageWidth, int step, int number = 0)
    {
        // Ohne Vektorpass gibt es keine Bewegung zu verschmieren. Der Aufrufer laesst
        // das Werkzeug dann ohnehin ruhen - siehe Optional -, aber die Zusage steht
        // besser hier als in einem Kommentar dort.
        if (data is null) return;

        int gridWidth = columns.Length;
        int gridHeight = rows.Length;

        var source = scratch.Values;
        var target = scratch.Work;

        var alpha = scratch.Alpha;
        var alphaTarget = scratch.AlphaWork;

        var backX = data.R;
        var backY = data.G;
        var frontX = data.B;

        // Ohne vierten Kanal gibt es nur den halben Vorwaertsvektor. Dann wird die
        // Bewegung des vorigen Bildes gespiegelt - das ist bei gleichfoermiger
        // Bewegung genau richtig und bei einer Wendung zu lang, aber es ist besser
        // als eine Richtung, die zur Haelfte fehlt.
        var frontY = data.A;

        int dataWidth = data.Width;
        int dataHeight = data.Height;

        float shutter = _shutter;
        int samples = _samples;
        float share = 1f / samples;

        Parallel.For(0, gridHeight, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        gy =>
        {
            int line = Math.Min(rows[gy], dataHeight - 1) * dataWidth;
            int row = gy * gridWidth;

            for (int gx = 0; gx < gridWidth; gx++)
            {
                int at = line + Math.Min(columns[gx], dataWidth - 1);

                // In Gitterpunkten, nicht in Bildpunkten: Beim Reglerzug ist das
                // Gitter groeber, und die Strecke wird es mit.
                float backDx = Finite(backX[at]) / step;
                float backDy = -Finite(backY[at]) / step;

                float frontDx = frontY is null ? -backDx : Finite(frontX[at]) / step;
                float frontDy = frontY is null ? -backDy : -Finite(frontY[at]) / step;

                int out0 = (row + gx) * 3;

                // Steht der Punkt still, bleibt er, wie er ist - und das ist der
                // Normalfall in fast jedem Bild.
                if (MathF.Abs(backDx) + MathF.Abs(backDy) + MathF.Abs(frontDx) + MathF.Abs(frontDy) < 0.01f)
                {
                    target[out0] = source[out0];
                    target[out0 + 1] = source[out0 + 1];
                    target[out0 + 2] = source[out0 + 2];
                    alphaTarget[row + gx] = alpha[row + gx];

                    continue;
                }

                float sumR = 0f, sumG = 0f, sumB = 0f, sumA = 0f;

                for (int i = 0; i < samples; i++)
                {
                    // Von -0,5 bis +0,5 der Verschlusszeit.
                    float when = (samples == 1 ? 0f : i / (float)(samples - 1) - 0.5f) * shutter;

                    float dx = when < 0f ? backDx * -when : frontDx * when;
                    float dy = when < 0f ? backDy * -when : frontDy * when;

                    Sample(source, alpha, gx + dx, gy + dy, gridWidth, gridHeight,
                           out float sr, out float sg, out float sb, out float sa);

                    sumR += sr;
                    sumG += sg;
                    sumB += sb;
                    sumA += sa;
                }

                target[out0] = sumR * share;
                target[out0 + 1] = sumG * share;
                target[out0 + 2] = sumB * share;

                alphaTarget[row + gx] = (byte)Math.Clamp(MathF.Round(sumA * share), 0f, 255f);
            }
        });

        scratch.Values = target;
        scratch.Work = source;

        scratch.Alpha = alphaTarget;
        scratch.AlphaWork = alpha;
    }

    /// <summary>Nicht jede Zahl in einem Vektorpass ist eine Bewegung.</summary>
    private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

    private static void Sample(float[] source, byte[] alpha, float x, float y,
                               int width, int height,
                               out float r, out float g, out float b, out float a)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);

        float tx = x - x0;
        float ty = y - y0;

        int left = Math.Clamp(x0, 0, width - 1);
        int right = Math.Clamp(x0 + 1, 0, width - 1);
        int top = Math.Clamp(y0, 0, height - 1);
        int bottom = Math.Clamp(y0 + 1, 0, height - 1);

        int topLeft = (top * width + left) * 3;
        int topRight = (top * width + right) * 3;
        int bottomLeft = (bottom * width + left) * 3;
        int bottomRight = (bottom * width + right) * 3;

        r = Mix(Mix(source[topLeft], source[topRight], tx),
                Mix(source[bottomLeft], source[bottomRight], tx), ty);

        g = Mix(Mix(source[topLeft + 1], source[topRight + 1], tx),
                Mix(source[bottomLeft + 1], source[bottomRight + 1], tx), ty);

        b = Mix(Mix(source[topLeft + 2], source[topRight + 2], tx),
                Mix(source[bottomLeft + 2], source[bottomRight + 2], tx), ty);

        a = Mix(Mix(alpha[top * width + left], alpha[top * width + right], tx),
                Mix(alpha[bottom * width + left], alpha[bottom * width + right], tx), ty);
    }

    private static float Mix(float from, float to, float at) => from + (to - from) * at;
}

/// <summary>
/// Sucht zu einem Bedarf den Pass, der ihn in DIESER Datei deckt.
///
/// An einer Stelle und nicht bei jedem Aufrufer, weil die Liste der Namen eine
/// Vereinbarung mit Blender ist und keine Meinung: "Depth" ist der heutige Name, "Z"
/// der aeltere, "Mist" der Ersatz, wenn jemand ohne Tiefenpass, aber mit Nebel
/// rendert. Drei Aufrufer mit drei Listen haetten eines Tages zwei davon.
/// </summary>
public static class FramePasses
{
    private static readonly string[] DepthNames = { "Depth", "Z", "Mist" };
    private static readonly string[] MotionNames = { "Vector", "Motion", "Speed" };
    private static readonly string[] NormalNames = { "Normal", "N" };

    /// <summary>
    /// Liest zu jedem Werkzeug seinen Pass. Fehlt er, bleibt der Platz leer und das
    /// Werkzeug ruht - eine Datei ohne Tiefe ist kein Fehler, sondern eine Datei
    /// ohne Tiefe.
    /// </summary>
    public static FloatFrame?[] Resolve(IDataTool[] tools, IReadOnlyList<ExrPass> passes,
                                        Func<string, FloatFrame?> read)
    {
        if (tools.Length == 0) return Array.Empty<FloatFrame?>();

        var found = new FloatFrame?[tools.Length];

        for (int i = 0; i < tools.Length; i++)
        {
            string? name = NameFor(tools[i].Needs, passes);
            if (name is not null) found[i] = read(name);
        }

        return found;
    }

    /// <summary>Der Name des Passes, der einen Bedarf deckt - oder null.</summary>
    public static string? NameFor(PassNeed need, IReadOnlyList<ExrPass> passes)
    {
        // Wer nichts braucht, bekommt nichts gesucht - und belegt damit auch keinen
        // Platz im Vorrat. Sonst laese das Programm einen Normalpass, den niemand
        // ansieht, nur weil das Werkzeug ihn frueher einmal wollte.
        if (need == PassNeed.None) return null;

        if (passes.Count == 0) return null;

        var names = need switch
        {
            PassNeed.Depth => DepthNames,
            PassNeed.Normal => NormalNames,
            _ => MotionNames,
        };

        // Eine Entfernung ist eine Groesse je Bildpunkt, eine Bewegung eine Richtung.
        // Die Unterscheidung ist nicht kosmetisch: Ein dreikanaliger Pass namens
        // "Depth" waere etwas anderes als der Tiefenpass, und ein einkanaliger namens
        // "Vector" enthielte keine Richtung. Eine Normale ist wie eine Bewegung eine
        // Richtung.
        bool grey = need == PassNeed.Depth;

        foreach (string wanted in names)
            if (ExrPasses.Find(passes, wanted) is { } pass && pass.Grey == grey) return pass.Name;

        return null;
    }
}
