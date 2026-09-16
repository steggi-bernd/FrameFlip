using System.Text.Json.Serialization;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Wo eine Ebene liegt und wie gross sie ist.
///
/// In ANTEILEN und nicht in Bildpunkten, und das ist die Entscheidung, die zaehlt:
/// Ein Rezept, das auf einem 1080p-Bild eingerichtet wurde, gilt danach auch fuer
/// 4K. In Bildpunkten gerechnet saesse das Wasserzeichen dort in einer Ecke, die
/// niemand gemeint hat.
///
/// Der Bezug ist die Grundlage - die Lage, in der die Ebene ohne jede Einstellung
/// liegt: Hat sie die Groesse des Bildes, deckt sie es Punkt fuer Punkt. Hat sie
/// eine andere, wird sie mittig eingepasst. Massstab und Versatz rechnen von dort
/// aus weiter. Damit tut die Grundstellung immer das Naheliegende, und zwar auch
/// bei einem Logo, das viermal kleiner ist als das Bild.
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
    /// Anschnitt, in Anteilen der EIGENEN Flaeche der Ebene.
    ///
    /// Auf die eigene Flaeche bezogen und nicht auf das Bild, weil ein Anschnitt
    /// sagt, welcher Teil der Ebene gilt - und das bleibt richtig, wenn man sie
    /// danach verschiebt oder kleiner macht.
    /// </summary>
    public float CropLeft { get; set; }

    public float CropTop { get; set; }

    public float CropRight { get; set; }

    public float CropBottom { get; set; }

    [JsonIgnore]
    public bool IsNeutral
        => MathF.Abs(OffsetX) < 0.0005f && MathF.Abs(OffsetY) < 0.0005f &&
           MathF.Abs(Scale - 1f) < 0.0005f && !IsCropped;

    [JsonIgnore]
    public bool IsCropped
        => CropLeft > 0.0005f || CropTop > 0.0005f ||
           CropRight > 0.0005f || CropBottom > 0.0005f;

    public LayerTransform Clone() => new()
    {
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Scale = Scale,
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
/// Antwort.
/// </summary>
public readonly struct LayerPlacement
{
    private LayerPlacement(float scale, float left, float top,
                           int layerWidth, int layerHeight,
                           float cropLeft, float cropTop, float cropRight, float cropBottom)
    {
        Scale = scale;
        Left = left;
        Top = top;
        LayerWidth = layerWidth;
        LayerHeight = layerHeight;
        CropLeft = cropLeft;
        CropTop = cropTop;
        CropRight = cropRight;
        CropBottom = cropBottom;
    }

    private readonly float Scale, Left, Top;
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

        float drawnWidth = layerWidth * scale;
        float drawnHeight = layerHeight * scale;

        return new LayerPlacement(
            scale,
            (canvasWidth - drawnWidth) / 2f + transform.OffsetX * canvasWidth,
            (canvasHeight - drawnHeight) / 2f + transform.OffsetY * canvasHeight,
            layerWidth, layerHeight,
            transform.CropLeft * layerWidth,
            transform.CropTop * layerHeight,
            (1f - transform.CropRight) * layerWidth,
            (1f - transform.CropBottom) * layerHeight);
    }

    /// <summary>
    /// Wo dieser Bildpunkt der Leinwand in der Ebene liegt. False, wenn er
    /// ausserhalb liegt - dann traegt die Ebene dort nichts bei.
    /// </summary>
    public bool Locate(int x, int y, out float u, out float v)
    {
        u = (x + 0.5f - Left) / Scale;
        v = (y + 0.5f - Top) / Scale;

        return u >= CropLeft && u < CropRight && v >= CropTop && v < CropBottom &&
               u >= 0f && v >= 0f && u < LayerWidth && v < LayerHeight;
    }

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
