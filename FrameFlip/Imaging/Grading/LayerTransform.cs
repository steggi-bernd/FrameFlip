using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Wo eine Ebene liegt, wie gross sie ist und wie sie steht.
///
/// In ANTEILEN und nicht in Bildpunkten, und das ist die Entscheidung, die zaehlt:
/// Ein Rezept, das auf einem 1080p-Bild eingerichtet wurde, gilt danach auch fuer
/// 4K. In Bildpunkten gerechnet saesse das Wasserzeichen dort in einer Ecke, die
/// niemand gemeint hat.
///
/// Der Bezug ist die Grundlage - die Lage, in der die Ebene ohne jede Einstellung
/// liegt: Hat sie die Groesse des Bildes, deckt sie es Punkt fuer Punkt. Hat sie
/// eine andere, wird sie mittig eingepasst. Massstab, Versatz und Drehung rechnen
/// von dort aus weiter. Damit tut die Grundstellung immer das Naheliegende, und zwar
/// auch bei einem Logo, das viermal kleiner ist als das Bild.
/// </summary>
public sealed class LayerTransform
{
    /// <summary>Versatz in Anteilen der Bildbreite. 0,5 ist eine halbe Bildbreite nach rechts.</summary>
    public float OffsetX { get; set; }

    /// <summary>Versatz in Anteilen der Bildhoehe. Positiv ist nach unten.</summary>
    public float OffsetY { get; set; }

    /// <summary>Massstab auf die Grundlage. 1 laesst sie, wie sie liegt.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// Drehung in Grad, im Uhrzeigersinn, um die Mitte der Ebene.
    ///
    /// Um die MITTE und nicht um eine Ecke: Das ist der Drehpunkt, den man meint,
    /// wenn man ein Wasserzeichen schraeg stellt - und es ist derselbe, um den auch
    /// der Massstab rechnet, sodass sich beide nicht gegenseitig verschieben.
    ///
    /// Gedreht wird in BILDPUNKTEN, nicht in Anteilen. In Anteilen gerechnet wuerde
    /// eine Drehung auf einem nicht quadratischen Bild scheren statt drehen - ein
    /// Quadrat kaeme als Raute heraus.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    /// Anschnitt, in Anteilen der EIGENEN Flaeche der Ebene.
    ///
    /// Auf die eigene Flaeche bezogen und nicht auf das Bild, weil ein Anschnitt
    /// sagt, welcher Teil der Ebene gilt - und das bleibt richtig, wenn man sie
    /// danach verschiebt, dreht oder kleiner macht.
    /// </summary>
    public float CropLeft { get; set; }

    public float CropTop { get; set; }

    public float CropRight { get; set; }

    public float CropBottom { get; set; }

    [JsonIgnore]
    public bool IsNeutral
        => MathF.Abs(OffsetX) < 0.0005f && MathF.Abs(OffsetY) < 0.0005f &&
           MathF.Abs(Scale - 1f) < 0.0005f && MathF.Abs(Rotation) < 0.01f && !IsCropped;

    [JsonIgnore]
    public bool IsCropped
        => CropLeft > 0.0005f || CropTop > 0.0005f ||
           CropRight > 0.0005f || CropBottom > 0.0005f;

    public LayerTransform Clone() => new()
    {
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Scale = Scale,
        Rotation = Rotation,
        CropLeft = CropLeft,
        CropTop = CropTop,
        CropRight = CropRight,
        CropBottom = CropBottom,
    };
}

/// <summary>
/// Die Rechnung hinter der Platzierung: von einem Bildpunkt der Leinwand zurueck in
/// die Ebene.
///
/// Rueckwaerts und nicht vorwaerts - das ist der uebliche Weg und der einzige, der
/// keine Loecher laesst. Wer vorwaerts rechnet, traegt die Punkte der Ebene auf der
/// Leinwand ein und muss die Luecken dazwischen fuellen; wer rueckwaerts rechnet,
/// fragt fuer jeden Punkt der Leinwand, woher er kommt, und bekommt fuer jeden eine
/// Antwort. Bei einer Drehung ist das kein Feinheitsunterschied mehr, sondern der
/// Unterschied zwischen einem Bild und einem Sieb.
/// </summary>
public readonly struct LayerPlacement
{
    private LayerPlacement(float scale, float centreX, float centreY, float cos, float sin,
                           int layerWidth, int layerHeight,
                           float cropLeft, float cropTop, float cropRight, float cropBottom)
    {
        Scale = scale;
        CentreX = centreX;
        CentreY = centreY;
        Cos = cos;
        Sin = sin;
        LayerWidth = layerWidth;
        LayerHeight = layerHeight;
        CropLeft = cropLeft;
        CropTop = cropTop;
        CropRight = cropRight;
        CropBottom = cropBottom;
    }

    private readonly float Scale, CentreX, CentreY, Cos, Sin;
    private readonly int LayerWidth, LayerHeight;
    private readonly float CropLeft, CropTop, CropRight, CropBottom;

    /// <summary>
    /// Bereitet die Platzierung vor - einmal je Bild, nicht je Bildpunkt.
    /// </summary>
    public static LayerPlacement Prepare(LayerTransform transform, int layerWidth, int layerHeight,
                                         int canvasWidth, int canvasHeight)
    {
        // Die Grundlage: gleich gross heisst Punkt auf Punkt, sonst mittig
        // eingepasst. Einpassen und nicht fuellen, weil ein Logo ganz zu sehen sein
        // soll - was ueber den Rand liefe, waere abgeschnitten, ohne dass jemand
        // einen Anschnitt verlangt hat.
        float basis = layerWidth == canvasWidth && layerHeight == canvasHeight
            ? 1f
            : MathF.Min(canvasWidth / (float)layerWidth, canvasHeight / (float)layerHeight);

        float scale = basis * MathF.Max(0.001f, transform.Scale);

        // Der Rueckweg dreht um den Gegenwinkel - deshalb steht hier das Vorzeichen
        // umgekehrt, und nicht erst spaeter in der Schleife.
        float radians = -transform.Rotation * MathF.PI / 180f;

        return new LayerPlacement(
            scale,
            canvasWidth / 2f + transform.OffsetX * canvasWidth,
            canvasHeight / 2f + transform.OffsetY * canvasHeight,
            MathF.Cos(radians), MathF.Sin(radians),
            layerWidth, layerHeight,
            transform.CropLeft * layerWidth,
            transform.CropTop * layerHeight,
            (1f - transform.CropRight) * layerWidth,
            (1f - transform.CropBottom) * layerHeight);
    }

    /// <summary>
    /// Wie stark dieser Bildpunkt der Leinwand von der Ebene gedeckt wird - 0
    /// draussen, 1 drinnen, dazwischen an der Kante.
    ///
    /// Die weiche Kante ist bei einer Drehung keine Zierde. Ein gerade liegendes
    /// Rechteck hat seine Kanten auf der Punktreihe; ein gedrehtes hat sie quer
    /// darueber, und hart entschieden sieht jede der vier Seiten nach Treppe aus.
    /// Ein Bildpunkt Uebergang kostet zwei Vergleiche und nimmt der Sache das
    /// Selbstgebaute.
    /// </summary>
    public float Coverage(int x, int y, out float u, out float v)
    {
        float dx = x + 0.5f - CentreX;
        float dy = y + 0.5f - CentreY;

        // Zurueckdrehen, dann den Massstab herausrechnen, dann in die Ecke der Ebene
        // verschieben.
        u = (dx * Cos - dy * Sin) / Scale + LayerWidth / 2f;
        v = (dx * Sin + dy * Cos) / Scale + LayerHeight / 2f;

        float left = MathF.Max(CropLeft, 0f);
        float top = MathF.Max(CropTop, 0f);
        float right = MathF.Min(CropRight, LayerWidth);
        float bottom = MathF.Min(CropBottom, LayerHeight);

        // Der Abstand zur naechsten Kante, in Bildpunkten der Ebene.
        float inside = MathF.Min(MathF.Min(u - left, right - u),
                                 MathF.Min(v - top, bottom - v));

        if (inside <= 0f && inside * Scale <= -0.5f) return 0f;

        // In Bildpunkte der Leinwand umgerechnet: Eine kleingezogene Ebene hat ihre
        // Kante auf weniger Punkten, und der Uebergang muss dort schmaler sein.
        return Math.Clamp(inside * Scale + 0.5f, 0f, 1f);
    }

    /// <summary>
    /// Wo dieser Bildpunkt in der Ebene liegt. False, wenn er ganz ausserhalb liegt.
    /// </summary>
    public bool Locate(int x, int y, out float u, out float v)
        => Coverage(x, y, out u, out v) > 0f;

    /// <summary>
    /// Holt die Farbe an einer gebrochenen Stelle - bilinear zwischen den vier
    /// Nachbarn.
    ///
    /// Bilinear und nicht der naechste Nachbar: Ein Wasserzeichen auf 37 Prozent
    /// zeigt sonst Treppen an jeder Kante, und das sieht nach einem Fehler aus. Die
    /// vier Nachbarn zu holen kostet Speicherzugriffe und keine Rechnung.
    /// </summary>
    public void Sample(FloatFrame frame, float u, float v,
                       out float r, out float g, out float b, out float a)
    {
        float fx = u - 0.5f;
        float fy = v - 0.5f;

        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);

        float tx = fx - x0;
        float ty = fy - y0;

        int x1 = Math.Clamp(x0 + 1, 0, frame.Width - 1);
        int y1 = Math.Clamp(y0 + 1, 0, frame.Height - 1);

        x0 = Math.Clamp(x0, 0, frame.Width - 1);
        y0 = Math.Clamp(y0, 0, frame.Height - 1);

        int a00 = y0 * frame.Width + x0;
        int a10 = y0 * frame.Width + x1;
        int a01 = y1 * frame.Width + x0;
        int a11 = y1 * frame.Width + x1;

        r = Mix(frame.R, a00, a10, a01, a11, tx, ty);
        g = Mix(frame.G, a00, a10, a01, a11, tx, ty);
        b = Mix(frame.B, a00, a10, a01, a11, tx, ty);
        a = frame.A is null ? 1f : Mix(frame.A, a00, a10, a01, a11, tx, ty);
    }

    private static float Mix(float[] values, int a00, int a10, int a01, int a11, float tx, float ty)
    {
        float top = values[a00] + (values[a10] - values[a00]) * tx;
        float bottom = values[a01] + (values[a11] - values[a01]) * tx;

        return top + (bottom - top) * ty;
    }
}
