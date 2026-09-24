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

    /// <summary>Der Griff ueber der oberen Kante - drehen.</summary>
    Rotate,
}

/// <summary>
/// Die Rechnung hinter dem Ziehen im Bild.
///
/// Steht fuer sich und nicht im Steuerelement, weil sie die Art Fehler beherbergt,
/// die man nicht sieht: Eine Ecke, die beim Ziehen wegwandert statt stehenzubleiben,
/// fuehlt sich nur "komisch" an - man haelt es fuer die Maus und nicht fuer die
/// Formel. Hier laesst sie sich ohne Fenster pruefen.
///
/// Gerechnet wird in BILDPUNKTEN der Leinwand und nicht in Anteilen. Das war anfangs
/// andersherum und ging nur so lange gut, wie es keine Drehung gab: In Anteilen sind
/// die beiden Achsen verschieden lang, und eine Drehung darin schert, statt zu
/// drehen - ein Quadrat kaeme als Raute heraus. Erst beim Zurueckschreiben wird
/// wieder in Anteile umgerechnet.
/// </summary>
public readonly struct PlacementDrag
{
    private PlacementDrag(LayerTransform start, Frame frame, DragHandle handle,
                          float grabX, float grabY, float grabAngle)
    {
        Start = start;
        Box = frame;
        Handle = handle;
        GrabX = grabX;
        GrabY = grabY;
        GrabAngle = grabAngle;
    }

    private readonly LayerTransform Start;
    private readonly Frame Box;
    private readonly float GrabX, GrabY, GrabAngle;

    public readonly DragHandle Handle;

    public bool IsActive => Handle != DragHandle.None;

    /// <summary>
    /// Wo die Ebene liegt, in Bildpunkten der Leinwand: Mitte, halbe Kantenlaengen
    /// und Drehung.
    /// </summary>
    public readonly struct Frame
    {
        public Frame(float centreX, float centreY, float halfWidth, float halfHeight, float degrees)
        {
            CentreX = centreX;
            CentreY = centreY;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;

            float radians = degrees * MathF.PI / 180f;
            Cos = MathF.Cos(radians);
            Sin = MathF.Sin(radians);
        }

        public readonly float CentreX, CentreY, HalfWidth, HalfHeight, Cos, Sin;

        /// <summary>Ein Punkt der Leinwand im ungedrehten Bezug der Ebene, Mitte bei null.</summary>
        public void ToLocal(float x, float y, out float u, out float v)
        {
            float dx = x - CentreX;
            float dy = y - CentreY;

            u = dx * Cos + dy * Sin;
            v = -dx * Sin + dy * Cos;
        }

        /// <summary>Der Rueckweg: aus dem Bezug der Ebene auf die Leinwand.</summary>
        public void ToCanvas(float u, float v, out float x, out float y)
        {
            x = CentreX + u * Cos - v * Sin;
            y = CentreY + u * Sin + v * Cos;
        }

        /// <summary>Die vier Ecken auf der Leinwand, beginnend oben links im Uhrzeigersinn.</summary>
        public void Corners(out float x0, out float y0, out float x1, out float y1,
                            out float x2, out float y2, out float x3, out float y3)
        {
            ToCanvas(-HalfWidth, -HalfHeight, out x0, out y0);
            ToCanvas(HalfWidth, -HalfHeight, out x1, out y1);
            ToCanvas(HalfWidth, HalfHeight, out x2, out y2);
            ToCanvas(-HalfWidth, HalfHeight, out x3, out y3);
        }

        /// <summary>Wo der Drehgriff sitzt: ueber der oberen Kante, mitgedreht.</summary>
        public void RotateGrip(float distance, out float x, out float y)
            => ToCanvas(0f, -HalfHeight - distance, out x, out y);
    }

    /// <summary>
    /// Die Lage der Ebene auf der Leinwand, in Bildpunkten.
    ///
    /// Die Mitte ist die Bildmitte plus der Versatz - das faellt direkt aus der
    /// Platzierung, weil sie mittig einpasst und den Versatz danach addiert. Genau
    /// deshalb ist diese Rechnung so kurz.
    /// </summary>
    public static Frame Region(LayerTransform transform, int layerWidth, int layerHeight,
                               int canvasWidth, int canvasHeight)
    {
        float basis = Basis(layerWidth, layerHeight, canvasWidth, canvasHeight);
        float scale = basis * MathF.Max(0.001f, transform.Scale);

        return new Frame(
            canvasWidth / 2f + transform.OffsetX * canvasWidth,
            canvasHeight / 2f + transform.OffsetY * canvasHeight,
            layerWidth * scale / 2f,
            layerHeight * scale / 2f,
            transform.Rotation);
    }

    /// <summary>Der Massstab, bei dem die Ebene ohne Einstellung liegt.</summary>
    public static float Basis(int layerWidth, int layerHeight, int canvasWidth, int canvasHeight)
    {
        if (layerWidth <= 0 || layerHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0) return 1f;

        return layerWidth == canvasWidth && layerHeight == canvasHeight
            ? 1f
            : MathF.Min(canvasWidth / (float)layerWidth, canvasHeight / (float)layerHeight);
    }

    /// <summary>Wie weit der Drehgriff ueber der oberen Kante sitzt, in Bildpunkten.</summary>
    public const float RotateDistance = 26f;

    /// <summary>
    /// Welcher Griff an dieser Stelle liegt. <paramref name="reach"/> ist der
    /// Fangbereich, ebenfalls in Bildpunkten der Leinwand.
    /// </summary>
    public static DragHandle HandleAt(in Frame box, float x, float y, float reach)
    {
        box.ToLocal(x, y, out float u, out float v);

        float hw = box.HalfWidth, hh = box.HalfHeight;

        // Der Drehgriff zuerst: Er sitzt ausserhalb, kann also keinem anderen im Weg
        // sein - aber wer ihn nach der Flaeche prueft, faengt ihn nie, wenn die
        // Ebene gross genug ist, dass sein Platz noch in ihr liegt.
        float gripV = -hh - RotateDistance;

        if (MathF.Abs(u) <= reach && MathF.Abs(v - gripV) <= reach) return DragHandle.Rotate;

        // Dann die Ecken: Sie liegen auf dem Rand der Flaeche, und wer dort zuerst
        // die Flaeche traefe, kaeme nie an eine Ecke.
        if (Near(u, -hw, reach) && Near(v, -hh, reach)) return DragHandle.TopLeft;
        if (Near(u, hw, reach) && Near(v, -hh, reach)) return DragHandle.TopRight;
        if (Near(u, -hw, reach) && Near(v, hh, reach)) return DragHandle.BottomLeft;
        if (Near(u, hw, reach) && Near(v, hh, reach)) return DragHandle.BottomRight;

        return MathF.Abs(u) <= hw && MathF.Abs(v) <= hh ? DragHandle.Body : DragHandle.None;

        static bool Near(float value, float target, float reach) => MathF.Abs(value - target) <= reach;
    }

    /// <summary>Faengt ein Ziehen an dieser Stelle an.</summary>
    public static PlacementDrag Begin(LayerTransform transform, in Frame box,
                                      float x, float y, float reach)
    {
        var handle = HandleAt(in box, x, y, reach);
        if (handle == DragHandle.None) return default;

        float angle = MathF.Atan2(y - box.CentreY, x - box.CentreX) * 180f / MathF.PI;

        return new PlacementDrag(transform.Clone(), box, handle, x, y, angle);
    }

    /// <summary>
    /// Die neue Platzierung, waehrend gezogen wird.
    ///
    /// Beim Verschieben wandert der Versatz mit der Maus. Beim Ziehen an einer Ecke
    /// bleibt die GEGENUEBERLIEGENDE stehen - das ist das Verhalten, das jeder
    /// erwartet, und es ist der Grund, warum sich dabei Massstab UND Versatz
    /// aendern: Die Platzierung rechnet von der Mitte aus, und die Mitte wandert,
    /// wenn eine Ecke stehenbleibt. Beim Drehen aendert sich nur der Winkel; die
    /// Mitte ist der Drehpunkt und bleibt, wo sie ist.
    /// </summary>
    public LayerTransform To(float x, float y, int canvasWidth, int canvasHeight)
    {
        var result = Start.Clone();

        if (canvasWidth <= 0 || canvasHeight <= 0) return result;

        if (Handle == DragHandle.Body)
        {
            result.OffsetX = Start.OffsetX + (x - GrabX) / canvasWidth;
            result.OffsetY = Start.OffsetY + (y - GrabY) / canvasHeight;

            return result;
        }

        if (Handle == DragHandle.Rotate)
        {
            float angle = MathF.Atan2(y - Box.CentreY, x - Box.CentreX) * 180f / MathF.PI;

            result.Rotation = Snap(Start.Rotation + (angle - GrabAngle));
            return result;
        }

        // --- eine Ecke ---
        //
        // Gerechnet wird im ungedrehten Bezug der Ebene. Dort ist "die
        // gegenueberliegende Ecke" wieder eine einfache Spiegelung, und die Drehung
        // stoert nicht.
        Box.ToLocal(x, y, out float mu, out float mv);

        bool left = Handle is DragHandle.TopLeft or DragHandle.BottomLeft;
        bool top = Handle is DragHandle.TopLeft or DragHandle.TopRight;

        // Die feste Ecke ist die gegenueberliegende, der Arm zeigt von ihr zur
        // angefassten - im ungedrehten Bezug also genau ueber die Diagonale.
        float fixedU = left ? Box.HalfWidth : -Box.HalfWidth;
        float fixedV = top ? Box.HalfHeight : -Box.HalfHeight;

        float grabbedU = left ? -Box.HalfWidth : Box.HalfWidth;
        float grabbedV = top ? -Box.HalfHeight : Box.HalfHeight;

        float armU = grabbedU - fixedU;
        float armV = grabbedV - fixedV;

        float length = armU * armU + armV * armV;
        if (length < 1e-6f) return result;

        // Der Schatten des Zugs auf die Diagonale. Die Maus laeuft beim Ziehen an
        // einer Ecke selten genau diagonal; sie darauf zu beziehen haelt die Ebene
        // im Seitenverhaeltnis, statt sie zu verzerren - und der Massstab ist
        // ohnehin nur eine Zahl.
        float factor = ((mu - fixedU) * armU + (mv - fixedV) * armV) / length;
        factor = MathF.Max(0.02f, factor);

        result.Scale = MathF.Max(0.01f, Start.Scale * factor);

        // Die Mitte lag im Bezug bei null; nach dem Skalieren um die feste Ecke
        // liegt sie bei F*(1-f). Zurueckgedreht ergibt das die neue Mitte auf der
        // Leinwand - und damit bleibt die feste Ecke wirklich stehen.
        Box.ToCanvas(fixedU * (1f - factor), fixedV * (1f - factor),
                     out float centreX, out float centreY);

        result.OffsetX = (centreX - canvasWidth / 2f) / canvasWidth;
        result.OffsetY = (centreY - canvasHeight / 2f) / canvasHeight;

        return result;
    }

    /// <summary>
    /// Rastet nahe an einem Vielfachen von 15 Grad ein.
    ///
    /// Eng gefasst, damit es nur faengt, wenn man ohnehin fast dort ist: Wer einmal
    /// gedreht hat, will auch wieder gerade werden koennen, und das von Hand auf
    /// null zu treffen ist Gluecksache. Wer 7 Grad will, nimmt den Regler.
    /// </summary>
    private static float Snap(float degrees)
    {
        while (degrees < 0f) degrees += 360f;
        while (degrees >= 360f) degrees -= 360f;

        float nearest = MathF.Round(degrees / 15f) * 15f;

        return MathF.Abs(degrees - nearest) <= 1.5f ? nearest % 360f : degrees;
    }
}
