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
public interface IDataTool
{
    /// <summary>Kennung fuer die Speicherung. Bleibt stabil, auch wenn der Anzeigename wechselt.</summary>
    string Kind { get; }

    /// <summary>Welche Renderdaten gebraucht werden.</summary>
    PassNeed Needs { get; }

    /// <summary>True, wenn nichts zu rechnen ist.</summary>
    bool IsNeutral { get; }

    void Prepare();

    /// <summary>
    /// Rechnet auf dem ganzen Puffer.
    /// </summary>
    /// <param name="data">Der Pass, in voller Bildgroesse.</param>
    /// <param name="columns">Zu welcher Bildspalte eine Gitterspalte gehoert.</param>
    /// <param name="rows">Dasselbe fuer die Zeilen.</param>
    void Run(LocalPass.Scratch scratch, FloatFrame data, int[] columns, int[] rows,
             int imageWidth, int step);
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

    public void Run(LocalPass.Scratch scratch, FloatFrame data, int[] columns, int[] rows,
                    int imageWidth, int step)
    {
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
        if (passes.Count == 0) return null;

        // Bisher gibt es nur einen Bedarf. Die Verzweigung steht trotzdem hier und
        // nicht beim Aufrufer - der naechste Bedarf soll eine Zeile kosten.
        var names = need == PassNeed.Depth ? DepthNames : Array.Empty<string>();

        foreach (string wanted in names)
            if (ExrPasses.Find(passes, wanted) is { } pass && pass.Grey) return pass.Name;

        return null;
    }
}
