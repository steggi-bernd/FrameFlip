namespace FrameFlip.Imaging;

/// <summary>
/// Farbraummathematik, die mehr als eine Stelle braucht: Matrizen zwischen Rec.709
/// und CIE XYZ, ihre Umkehrung, und die chromatische Anpassung nach Bradford.
///
/// Zusammengelegt, weil dieselben neun Zahlen sonst an zwei Stellen stuenden - in
/// der Sichtumwandlung und im Weissabgleich - und zwei Abschriften derselben Matrix
/// einmal auseinanderlaufen.
/// </summary>
public static class Colorimetry
{
    /// <summary>
    /// Rec.709 nach CIE XYZ, Weisspunkt D65. Die uebliche sRGB-Matrix; die
    /// Zeilensummen ergeben den Weisspunkt und sind damit nachrechenbar.
    /// </summary>
    public static readonly float[] Rec709ToXyz =
    {
        0.4123908f, 0.3575843f, 0.1804808f,
        0.2126390f, 0.7151687f, 0.0721923f,
        0.0193308f, 0.1191948f, 0.9505322f,
    };

    public static readonly float[] XyzToRec709 = Invert(Rec709ToXyz);

    /// <summary>Der Weisspunkt D65 in XYZ - die Zeilensummen der Matrix oben.</summary>
    public static readonly float[] D65 = { 0.95047f, 1.00000f, 1.08883f };

    /// <summary>
    /// Die Bradford-Matrix: XYZ in einen Raum, in dem die drei Zapfenantworten
    /// getrennt skaliert werden koennen.
    ///
    /// Eine Anpassung des Weisspunkts direkt in XYZ zu rechnen - also XYZ einfach
    /// komponentenweise zu skalieren - ist die von-Kries-Naeherung im falschen Raum
    /// und faerbt gesaettigte Toene sichtbar daneben. Bradford ist die Matrix, die
    /// dafuer gemacht ist.
    /// </summary>
    public static readonly float[] Bradford =
    {
         0.8951f,  0.2664f, -0.1614f,
        -0.7502f,  1.7135f,  0.0367f,
         0.0389f, -0.0685f,  1.0296f,
    };

    public static readonly float[] BradfordInverse = Invert(Bradford);

    /// <summary>Produkt zweier 3x3-Matrizen, zeilenweise gespeichert.</summary>
    public static float[] Multiply(float[] a, float[] b)
    {
        var result = new float[9];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                result[row * 3 + column] =
                    a[row * 3] * b[column] +
                    a[row * 3 + 1] * b[3 + column] +
                    a[row * 3 + 2] * b[6 + column];
            }
        }

        return result;
    }

    public static float[] Transform(float[] m, float x, float y, float z)
        => new[]
        {
            m[0] * x + m[1] * y + m[2] * z,
            m[3] * x + m[4] * y + m[5] * z,
            m[6] * x + m[7] * y + m[8] * z,
        };

    /// <summary>Gerechnet wird in double: die Determinante kann klein werden.</summary>
    public static float[] Invert(float[] m)
    {
        double a = m[0], b = m[1], c = m[2];
        double d = m[3], e = m[4], f = m[5];
        double g = m[6], h = m[7], i = m[8];

        double determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (Math.Abs(determinant) < 1e-12)
            throw new InvalidOperationException("Matrix laesst sich nicht umkehren.");

        double s = 1.0 / determinant;

        return new[]
        {
            (float)((e * i - f * h) * s), (float)((c * h - b * i) * s), (float)((b * f - c * e) * s),
            (float)((f * g - d * i) * s), (float)((a * i - c * g) * s), (float)((c * d - a * f) * s),
            (float)((d * h - e * g) * s), (float)((b * g - a * h) * s), (float)((a * e - b * d) * s),
        };
    }

    /// <summary>
    /// Die Farbart eines schwarzen Strahlers bei der gegebenen Temperatur, nach der
    /// Naeherung von Kang und anderen (2002). Gilt von 1667 bis 25000 Kelvin, was
    /// den ganzen Bereich abdeckt, den ein Regler sinnvoll anbietet.
    /// </summary>
    public static (float X, float Y) PlanckianXy(float kelvin)
    {
        double t = Math.Clamp(kelvin, 1667.0, 25000.0);
        double t2 = t * t;
        double t3 = t2 * t;

        double x = t <= 4000
            ? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
            : -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;

        double x2 = x * x;
        double x3 = x2 * x;

        double y = t <= 2222 ? -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683
                 : t <= 4000 ? -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867
                 :              3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483;

        return ((float)x, (float)y);
    }

    /// <summary>Farbart nach XYZ, auf Helligkeit 1 normiert.</summary>
    public static float[] XyToXyz(float x, float y)
    {
        if (MathF.Abs(y) < 1e-6f) return new[] { 0f, 1f, 0f };
        return new[] { x / y, 1f, (1f - x - y) / y };
    }

    /// <summary>
    /// Die Matrix, die ein Bild von einem Weisspunkt auf einen anderen bringt -
    /// fertig in Rec.709 hinein und wieder heraus, damit sie je Bildpunkt nur noch
    /// eine Multiplikation ist.
    /// </summary>
    public static float[] Adaptation(float[] sourceWhite, float[] destinationWhite)
    {
        var source = Transform(Bradford, sourceWhite[0], sourceWhite[1], sourceWhite[2]);
        var destination = Transform(Bradford, destinationWhite[0], destinationWhite[1], destinationWhite[2]);

        // Die Skalierung der drei Zapfenantworten - das ist der eigentliche Schritt.
        var scale = new[]
        {
            Safe(destination[0] / source[0]), 0f, 0f,
            0f, Safe(destination[1] / source[1]), 0f,
            0f, 0f, Safe(destination[2] / source[2]),
        };

        var inXyz = Multiply(BradfordInverse, Multiply(scale, Bradford));
        return Multiply(XyzToRec709, Multiply(inXyz, Rec709ToXyz));

        static float Safe(float value) => float.IsFinite(value) && value > 0 ? value : 1f;
    }
}
