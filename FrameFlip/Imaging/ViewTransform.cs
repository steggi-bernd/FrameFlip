using System.IO;

namespace FrameFlip.Imaging;

/// <summary>
/// Der Weg von Szenenlicht zur Anzeige.
///
/// Warum das ueberhaupt gebraucht wird: Blender rechnet in linearem Licht ohne obere
/// Grenze. Beim Schreiben eines PNG wendet es die Sichtumwandlung an - seit 4.0 AgX,
/// davor Filmic. Beim Schreiben eines EXR tut es das NICHT; dort stehen die rohen
/// Szenenwerte. Wer ein solches EXR ohne Umwandlung anzeigt, sieht ein flaues, dunkles
/// Bild, das mit Blenders Vorschau nichts zu tun hat - und meldet das, zu Recht, als
/// Fehler.
/// </summary>
public interface IViewTransform
{
    /// <summary>Name fuer die Anzeige - "AgX", "Standard".</summary>
    string Name { get; }

    /// <summary>
    /// Rechnet lineares Szenenlicht (Rec.709-Primaerfarben, Werte auch ueber 1) in
    /// Anzeigewerte von 0 bis 1 um.
    /// </summary>
    void Apply(ref float r, ref float g, ref float b);
}

/// <summary>
/// Die einfache Umwandlung: beschneiden und als sRGB kodieren. Entspricht Blenders
/// Sichtumwandlung "Standard".
///
/// Sie ist der Rueckfall, wenn sich keine Blender-Installation finden laesst, und
/// gleichzeitig eine ehrliche Wahl fuer Material, das bereits durchgereicht wurde.
/// Fuer ein frisches Render ist sie es nicht: alles ueber 1 laeuft hart auf Weiss,
/// und genau dort sitzt bei einem Render die Zeichnung.
/// </summary>
public sealed class StandardViewTransform : IViewTransform
{
    public string Name => "Standard";

    public void Apply(ref float r, ref float g, ref float b)
    {
        r = Srgb.Encode(r);
        g = Srgb.Encode(g);
        b = Srgb.Encode(b);
    }
}

/// <summary>
/// AgX, zusammengesetzt aus Blenders eigener Farbverwaltung.
///
/// Der Aufbau ist nicht erfunden, sondern aus config.ocio abgelesen. Dort steht AgX
/// als Kette von fuenf Schritten:
///
///   1. Matrix von Rec.709 nach FilmLight E-Gamut
///   2. Logarithmische Verteilung ueber 25 Blendenstufen
///   3. die dreidimensionale Tabelle AgX_Base_sRGB.cube
///   4. Potenz 2,4 - macht die Tabellenausgabe wieder linear
///   5. Kodierung nach sRGB
///
/// Blender 5.1 schreibt die letzten beiden Schritte als "Rec.1886 nach sRGB", was
/// dieselbe Rechnung ist. Die Kette hat sich zwischen 4.0 und 5.1 nicht geaendert.
///
/// Nachgebaut wird also nichts: die Tabelle, in der die eigentliche Gestaltung
/// steckt, kommt aus der Installation. Was hier im Quelltext steht, ist
/// Farbraummathematik, die sich nicht aendert.
/// </summary>
public sealed class AgxViewTransform : IViewTransform
{
    private readonly CubeLut _lut;
    private readonly float[] _toEGamut;     // 3x3, zeilenweise

    public string Name => "AgX";

    /// <summary>Woher die Tabelle stammt - fuer die Anzeige in den Einstellungen.</summary>
    public string LutPath { get; }

    private AgxViewTransform(CubeLut lut, string lutPath, float[] matrix)
    {
        _lut = lut;
        LutPath = lutPath;
        _toEGamut = matrix;
    }

    /// <summary>
    /// Die Verteilung aus der Konfiguration: lg2 ueber [-12,47393, 12,5260688117].
    /// Die Spanne ist damit genau 25 Blendenstufen.
    /// </summary>
    private const float AllocationMin = -12.47393f;
    private const float AllocationMax = 12.5260688117f;

    /// <summary>
    /// Kleinster Wert, der die logarithmische Verteilung noch erreicht. Alles
    /// darunter - und alles Negative, das aus einem Renderfehler stammen kann -
    /// landet am unteren Ende, statt als NaN weiterzulaufen.
    /// </summary>
    private static readonly float AllocationFloor = MathF.Pow(2f, AllocationMin);

    public static AgxViewTransform FromLut(CubeLut lut, string path)
        => new(lut, path, BuildRec709ToEGamut());

    /// <summary>
    /// Laedt die Tabelle aus einer Blender-Installation. Erwartet den Pfad der
    /// ausfuehrbaren Datei; die Farbverwaltung liegt darunter in einem nach der
    /// Fassung benannten Ordner.
    /// </summary>
    public static AgxViewTransform? TryLoad(string blenderExecutable)
    {
        string? lut = FindLut(blenderExecutable);
        if (lut is null) return null;

        try
        {
            return FromLut(CubeLut.Load(lut), lut);
        }
        catch (Exception)
        {
            // Eine unlesbare Tabelle ist kein Grund, das Bild gar nicht zu zeigen -
            // der Aufrufer faellt dann auf "Standard" zurueck.
            return null;
        }
    }

    /// <summary>
    /// Sucht luts/AgX_Base_sRGB.cube unterhalb des Programmordners. Der Zwischenordner
    /// traegt die Fassungsnummer ("4.5"), die hier nicht bekannt sein muss - es gibt
    /// ohnehin nur einen.
    /// </summary>
    public static string? FindLut(string blenderExecutable)
    {
        try
        {
            string? root = Path.GetDirectoryName(blenderExecutable);
            if (root is null || !Directory.Exists(root)) return null;

            foreach (string version in Directory.EnumerateDirectories(root))
            {
                string candidate = Path.Combine(version, "datafiles", "colormanagement", "luts",
                                                "AgX_Base_sRGB.cube");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception)
        {
            // Ein unzugaenglicher Ordner heisst: hier ist es nicht.
        }

        return null;
    }

    public void Apply(ref float r, ref float g, ref float b)
    {
        // 1. In den Arbeitsfarbraum der Tabelle.
        float er = _toEGamut[0] * r + _toEGamut[1] * g + _toEGamut[2] * b;
        float eg = _toEGamut[3] * r + _toEGamut[4] * g + _toEGamut[5] * b;
        float eb = _toEGamut[6] * r + _toEGamut[7] * g + _toEGamut[8] * b;

        // 2. Logarithmisch verteilen, damit die 25 Blendenstufen gleichmaessig auf
        //    den Wertebereich der Tabelle fallen.
        er = Allocate(er);
        eg = Allocate(eg);
        eb = Allocate(eb);

        // 3. Die Tabelle - hier passiert die eigentliche Bildwerdung.
        _lut.Apply(ref er, ref eg, ref eb);

        // 4. und 5. Die Ausgabe der Tabelle ist mit 2,4 kodiert; linearisieren und
        //    als sRGB wieder kodieren.
        r = Srgb.Encode(MathF.Pow(Math.Clamp(er, 0f, 1f), 2.4f));
        g = Srgb.Encode(MathF.Pow(Math.Clamp(eg, 0f, 1f), 2.4f));
        b = Srgb.Encode(MathF.Pow(Math.Clamp(eb, 0f, 1f), 2.4f));
    }

    private static float Allocate(float value)
    {
        if (!(value > AllocationFloor)) return 0f;     // faengt auch NaN ab
        return (MathF.Log2(value) - AllocationMin) / (AllocationMax - AllocationMin);
    }

    /// <summary>
    /// Die Matrix von Rec.709 nach FilmLight E-Gamut.
    ///
    /// Beide Werte stehen so in config.ocio; gerechnet wird hier, damit im Quelltext
    /// nachvollziehbare Groessen stehen und keine neun Zahlen ohne Herkunft.
    /// </summary>
    private static float[] BuildRec709ToEGamut()
    {
        // Rec.709 nach CIE XYZ mit Weisspunkt D65 - die uebliche sRGB-Matrix.
        float[] rec709ToXyz =
        {
            0.4123908f, 0.3575843f, 0.1804808f,
            0.2126390f, 0.7151687f, 0.0721923f,
            0.0193308f, 0.1191948f, 0.9505322f,
        };

        // E-Gamut nach CIE XYZ D65, aus der Konfiguration. Die Zeilensummen ergeben
        // den Weisspunkt D65 - daran laesst sich die Richtung pruefen.
        float[] eGamutToXyz =
        {
             0.7053968501f,  0.1640413283f,  0.08101774865f,
             0.2801307241f,  0.8202066415f, -0.1003373656f,
            -0.1037815116f, -0.07290725703f, 1.265746519f,
        };

        return Multiply(Invert(eGamutToXyz), rec709ToXyz);
    }

    private static float[] Multiply(float[] a, float[] b)
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

    private static float[] Invert(float[] m)
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
}

/// <summary>Die Uebertragungsfunktion von sRGB. Kein Gamma 2,2 - die Norm hat unten ein gerades Stueck.</summary>
public static class Srgb
{
    public static float Encode(float linear)
    {
        if (!(linear > 0f)) return 0f;          // faengt auch NaN ab
        if (linear >= 1f) return 1f;

        return linear <= 0.0031308f
            ? 12.92f * linear
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    public static float Decode(float encoded)
    {
        if (!(encoded > 0f)) return 0f;
        if (encoded >= 1f) return 1f;

        return encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
    }
}
