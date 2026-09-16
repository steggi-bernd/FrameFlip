namespace FrameFlip.Imaging.Grading;

/// <summary>Welcher Griff angefasst wurde.</summary>
public enum DragHandle
{
    None,

    /// <summary>Die Flaeche selbst - verschieben.</summary>
    Body,

    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Die Rechnung hinter dem Ziehen im Bild.
///
/// Steht fuer sich und nicht im Steuerelement, weil sie die Art Fehler beherbergt,
/// die man nicht sieht: Eine Ecke, die beim Ziehen wegwandert statt stehenzubleiben,
/// fuehlt sich nur "komisch" an - man haelt es fuer die Maus und nicht fuer die
/// Formel. Hier laesst sie sich ohne Fenster pruefen.
///
/// Gerechnet wird durchgehend in ANTEILEN der Leinwand, weil die Platzierung selbst
/// in Anteilen steht. Ein Umweg ueber Bildpunkte waere eine zweite Einheit und damit
/// eine zweite Gelegenheit, sie zu verwechseln.
/// </summary>
public readonly struct PlacementDrag
{
    private PlacementDrag(LayerTransform start, float baseWidth, float baseHeight,
                          DragHandle handle, float grabX, float grabY)
    {
        Start = start;
        BaseWidth = baseWidth;
        BaseHeight = baseHeight;
        Handle = handle;
        GrabX = grabX;
        GrabY = grabY;
    }

    private readonly LayerTransform Start;
    private readonly float BaseWidth, BaseHeight;
    private readonly float GrabX, GrabY;

    public readonly DragHandle Handle;

    public bool IsActive => Handle != DragHandle.None;

    /// <summary>
    /// Wo die Ebene liegt, in Anteilen der Leinwand: Mitte und halbe Kantenlaenge.
    ///
    /// Die Mitte ist 0,5 plus der Versatz - das faellt direkt aus der Platzierung,
    /// weil sie mittig einpasst und den Versatz danach addiert. Genau deshalb ist
    /// diese Rechnung so kurz.
    /// </summary>
    public static void Region(LayerTransform transform, float baseWidth, float baseHeight,
                              out float centreX, out float centreY,
                              out float halfWidth, out float halfHeight)
    {
        centreX = 0.5f + transform.OffsetX;
        centreY = 0.5f + transform.OffsetY;

        halfWidth = baseWidth * MathF.Max(0.001f, transform.Scale) / 2f;
        halfHeight = baseHeight * MathF.Max(0.001f, transform.Scale) / 2f;
    }

    /// <summary>
    /// Die Grundbreite und -hoehe einer Ebene, in Anteilen der Leinwand - also ihre
    /// Groesse bei Massstab eins.
    /// </summary>
    public static void Basis(int layerWidth, int layerHeight, int canvasWidth, int canvasHeight,
                             out float baseWidth, out float baseHeight)
    {
        if (layerWidth <= 0 || layerHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            baseWidth = baseHeight = 1f;
            return;
        }

        float basis = layerWidth == canvasWidth && layerHeight == canvasHeight
            ? 1f
            : MathF.Min(canvasWidth / (float)layerWidth, canvasHeight / (float)layerHeight);

        baseWidth = layerWidth * basis / canvasWidth;
        baseHeight = layerHeight * basis / canvasHeight;
    }

    /// <summary>
    /// Welcher Griff an dieser Stelle liegt. <paramref name="reach"/> ist der
    /// Fangbereich einer Ecke, ebenfalls als Anteil.
    /// </summary>
    public static DragHandle HandleAt(LayerTransform transform, float baseWidth, float baseHeight,
                                      float x, float y, float reach)
    {
        Region(transform, baseWidth, baseHeight, out float cx, out float cy, out float hw, out float hh);

        float left = cx - hw, right = cx + hw;
        float top = cy - hh, bottom = cy + hh;

        // Die Ecken zuerst: Sie liegen auf dem Rand der Flaeche, und wer dort
        // zuerst die Flaeche traefe, kaeme nie an eine Ecke.
        if (Near(x, left, reach) && Near(y, top, reach)) return DragHandle.TopLeft;
        if (Near(x, right, reach) && Near(y, top, reach)) return DragHandle.TopRight;
        if (Near(x, left, reach) && Near(y, bottom, reach)) return DragHandle.BottomLeft;
        if (Near(x, right, reach) && Near(y, bottom, reach)) return DragHandle.BottomRight;

        return x >= left && x <= right && y >= top && y <= bottom ? DragHandle.Body : DragHandle.None;

        static bool Near(float value, float target, float reach) => MathF.Abs(value - target) <= reach;
    }

    /// <summary>Faengt ein Ziehen an dieser Stelle an.</summary>
    public static PlacementDrag Begin(LayerTransform transform, float baseWidth, float baseHeight,
                                      float x, float y, float reach)
    {
        var handle = HandleAt(transform, baseWidth, baseHeight, x, y, reach);

        return handle == DragHandle.None
            ? default
            : new PlacementDrag(transform.Clone(), baseWidth, baseHeight, handle, x, y);
    }

    /// <summary>
    /// Die neue Platzierung, waehrend gezogen wird.
    ///
    /// Beim Verschieben wandert der Versatz mit der Maus. Beim Ziehen an einer Ecke
    /// bleibt die GEGENUEBERLIEGENDE stehen - das ist das Verhalten, das jeder
    /// erwartet, und es ist der Grund, warum sich dabei Massstab UND Versatz
    /// aendern: Die Platzierung rechnet von der Mitte aus, und die Mitte wandert,
    /// wenn eine Ecke stehenbleibt.
    /// </summary>
    public LayerTransform To(float x, float y)
    {
        var result = Start.Clone();

        if (Handle == DragHandle.Body)
        {
            result.OffsetX = Start.OffsetX + (x - GrabX);
            result.OffsetY = Start.OffsetY + (y - GrabY);

            return result;
        }

        Region(Start, BaseWidth, BaseHeight, out float cx, out float cy, out float hw, out float hh);

        // Die feste Ecke ist die gegenueberliegende.
        float fixedX = Handle is DragHandle.TopLeft or DragHandle.BottomLeft ? cx + hw : cx - hw;
        float fixedY = Handle is DragHandle.TopLeft or DragHandle.TopRight ? cy + hh : cy - hh;

        float startX = Handle is DragHandle.TopLeft or DragHandle.BottomLeft ? cx - hw : cx + hw;
        float startY = Handle is DragHandle.TopLeft or DragHandle.TopRight ? cy - hh : cy + hh;

        float armX = startX - fixedX;
        float armY = startY - fixedY;

        float reachX = x - fixedX;
        float reachY = y - fixedY;

        float length = armX * armX + armY * armY;
        if (length < 1e-9f) return result;

        // Der Schatten des Zugs auf die Diagonale. Die Maus laeuft beim Ziehen an
        // einer Ecke selten genau diagonal; sie darauf zu beziehen haelt die Ebene
        // im Seitenverhaeltnis, statt sie zu verzerren - und der Massstab ist
        // ohnehin nur eine Zahl.
        float factor = (reachX * armX + reachY * armY) / length;
        factor = MathF.Max(0.02f, factor);

        result.Scale = MathF.Max(0.01f, Start.Scale * factor);

        // Die Mitte wandert mit, damit die feste Ecke wirklich stehenbleibt.
        result.OffsetX = fixedX + (cx - fixedX) * factor - 0.5f;
        result.OffsetY = fixedY + (cy - fixedY) * factor - 0.5f;

        return result;
    }
}
