namespace FrameFlip.Imaging.Grading;

/// <summary>Ein Punkt auf dem Farbrad. Beide Werte laufen von -1 bis 1.</summary>
public readonly record struct WheelPoint(float X, float Y)
{
    public float Radius => MathF.Min(1f, MathF.Sqrt(X * X + Y * Y));

    public bool IsCentre => Radius < 0.004f;
}

/// <summary>
/// Die Rechnung hinter einem Farbrad.
///
/// Ein Rad hat zwei Freiheitsgrade, eine Zone hat drei - Rot, Gruen und Blau.
/// Deshalb teilt sich die Bedienung auf: Das Rad bestimmt die FARBRICHTUNG, ein
/// Regler daneben die HELLIGKEIT. Zusammen sind es wieder drei, und die Aufteilung
/// entspricht dem, wonach man greift: "waermer" ist eine Richtung, "heller" ist es
/// nicht.
///
/// Damit das sauber bleibt, verschiebt das Rad die drei Kanaele so gegeneinander,
/// dass ihre Summe null bleibt: Die Richtungen von Rot, Gruen und Blau stehen um je
/// 120 Grad versetzt, und der Kosinus ueber drei solche Winkel summiert sich zu
/// null. Das Rad aendert also nie die Helligkeit, auch nicht ein bisschen - sonst
/// zoege jeder Griff ins Farbige den Helligkeitsregler hinter sich her.
/// </summary>
public static class ColourWheelMath
{
    /// <summary>Wo Rot liegt: rechts. Gruen und Blau folgen im Abstand von 120 Grad.</summary>
    private const float RedAngle = 0f;
    private const float GreenAngle = 2f * MathF.PI / 3f;
    private const float BlueAngle = 4f * MathF.PI / 3f;

    /// <summary>
    /// Der Farbversatz zu einem Radpunkt. Die drei Werte summieren sich zu null.
    /// </summary>
    public static (float R, float G, float B) Offset(WheelPoint point)
    {
        if (point.IsCentre) return (0f, 0f, 0f);

        float angle = MathF.Atan2(point.Y, point.X);
        float radius = point.Radius;

        return (radius * MathF.Cos(angle - RedAngle),
                radius * MathF.Cos(angle - GreenAngle),
                radius * MathF.Cos(angle - BlueAngle));
    }

    /// <summary>
    /// Aus drei Kanalwerten den Radpunkt zurueckrechnen.
    ///
    /// Gebraucht beim Laden eines Rezepts: Der Griff muss dort stehen, wo die Werte
    /// ihn hinsetzen wuerden, sonst springt er beim ersten Anfassen.
    /// </summary>
    public static WheelPoint ToPoint(float r, float g, float b)
    {
        // Der gemeinsame Anteil ist Helligkeit, nicht Farbe - er gehoert nicht aufs Rad.
        float mean = (r + g + b) / 3f;
        float dr = r - mean, dg = g - mean, db = b - mean;

        // Die Umkehrung der Summe oben: Jeder Kanal traegt seine Richtung bei.
        // Der Faktor 2/3 faellt aus der Projektion auf drei um 120 Grad versetzte
        // Achsen - ohne ihn kaeme ein um die Haelfte zu kleiner Radius heraus.
        float x = (dr * MathF.Cos(RedAngle) + dg * MathF.Cos(GreenAngle) + db * MathF.Cos(BlueAngle)) * 2f / 3f;
        float y = (dr * MathF.Sin(RedAngle) + dg * MathF.Sin(GreenAngle) + db * MathF.Sin(BlueAngle)) * 2f / 3f;

        // Ausserhalb des Kreises gibt es keinen Griff; ein Rezept aus einer anderen
        // Quelle koennte trotzdem dort liegen.
        float radius = MathF.Sqrt(x * x + y * y);
        if (radius > 1f)
        {
            x /= radius;
            y /= radius;
        }

        return new WheelPoint(x, y);
    }

    /// <summary>
    /// Die drei Kanalwerte einer Zone aus Rad, Helligkeit und Massstab.
    ///
    /// <paramref name="neutral"/> ist der Wert, bei dem nichts geschieht - null fuer
    /// Lift, eins fuer Gamma und Gain. Der Massstab bestimmt, wie weit der Rand des
    /// Rades traegt; er ist je Zone verschieden, weil ein Lift von 0,3 viel und ein
    /// Gain von 0,3 wenig waere.
    /// </summary>
    public static (float R, float G, float B) ToChannels(WheelPoint point, float brightness,
                                                        float neutral, float scale)
    {
        var (dr, dg, db) = Offset(point);
        float centre = neutral + brightness;

        return (centre + dr * scale, centre + dg * scale, centre + db * scale);
    }

    /// <summary>Die Helligkeit, die in drei Kanalwerten steckt.</summary>
    public static float ToBrightness(float r, float g, float b, float neutral)
        => (r + g + b) / 3f - neutral;
}
