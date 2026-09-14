namespace FrameFlip.Tests;

/// <summary>
/// Kleine EXR-Dateien, von Blender 4.5 geschrieben.
///
/// Die uebrigen Tests erzeugen ihr Bildmaterial selbst - hier geht das nicht, und
/// zwar aus einem inhaltlichen Grund: Ein Roundtrip gegen einen selbstgeschriebenen
/// Packer wuerde genau die Fehler nicht finden, auf die es ankommt. Ein vertauschtes
/// Vorzeichen im Praediktor oder eine verkehrt herum sortierte Bytehaelfte hebt sich
/// auf, wenn dieselbe falsche Annahme auf beiden Seiten steht. Geprueft wird deshalb
/// gegen echte Dateien einer fremden Implementierung.
///
/// Das Bild ist 8x4 und traegt Werte, an denen sich zwei Dinge ablesen lassen:
/// Gruen liegt ueberall bei 0,5 - waere unterwegs ein sRGB-Transform passiert,
/// stuende dort 0,735. Blau ist in der ersten Spalte 4,0, also weit ueber Weiss;
/// dieser Wert ueberlebt nur in einem echten Gleitkommaformat.
///
/// Zu beachten: Blenders Pixelfeld laeuft von unten nach oben, die EXR-Datei von
/// oben nach unten. Die erste Zeile der Datei ist also Blenders letzte.
/// </summary>
internal static class ExrSamples
{
    public const int ProbeWidth = 8;
    public const int ProbeHeight = 4;

    public const int BigWidth = 40;
    public const int BigHeight = 64;

    /// <summary>Erwartetes Rot an einer Stelle der Probe-Dateien.</summary>
    public static float ProbeRed(int row, int x) => ((ProbeHeight - 1 - row) * ProbeWidth + x) / 100f;

    public const float ProbeGreen = 0.5f;

    public static float ProbeBlue(int x) => x == 0 ? 4.0f : 0.25f;

    /// <summary>Das grosse Bild: Rot traegt die Spalte, Gruen die Zeile, Blau die Summe.</summary>
    public static float BigRed(int x) => x / 100f;

    public static float BigGreen(int row) => (BigHeight - 1 - row) / 100f;

    public static float BigBlue(int row, int x) => (x + (BigHeight - 1 - row)) / 100f;

    public static byte[] ZipHalf() => Convert.FromBase64String(ZipHalfData);

    public static byte[] ZipFloat() => Convert.FromBase64String(ZipFloatData);

    public static byte[] Rle() => Convert.FromBase64String(RleData);

    public static byte[] Uncompressed() => Convert.FromBase64String(NoneData);

    /// <summary>40x64 mit ZIP - vier Bloecke zu je 16 Zeilen statt nur einem.</summary>
    public static byte[] MultiBlock() => Convert.FromBase64String(BigZipData);

    private const string ZipHalfData =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QASQAAAEEAAQAAAAAAAAABAAAAAQAAAEIAAQAAAAAAAAABAAAAAQAAAEcAAQAAAAAA" +
        "AAABAAAAAQAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAA2RhdGFXaW5kb3cAYm94" +
        "MmkAEAAAAAAAAAAAAAAABwAAAAMAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAAcAAAADAAAAbGluZU9yZGVy" +
        "AGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgA" +
        "AAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/eERlbnNpdHkAZmxvYXQABAAAAAAAkEIAagEAAAAA" +
        "AAAAAAAAagAAAHheY2jADvQurQSDLixyDQ0NDfMvXbp46dKlSypY5BoaGhrmqyirqPy+dGkzFjkwmN/A//H4CV9HKLejAEK3" +
        "QPnVjVBxHPKVDQ2NDQ2NDZ045EsbGhsbGhsaenDIeyxsaWpqbGwEAEnagq0=";

    private const string ZipFloatData =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QASQAAAEEAAgAAAAAAAAABAAAAAQAAAEIAAgAAAAAAAAABAAAAAQAAAEcAAgAAAAAA" +
        "AAABAAAAAQAAAFIAAgAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAA2RhdGFXaW5kb3cAYm94" +
        "MmkAEAAAAAAAAAAAAAAABwAAAAMAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAAcAAAADAAAAbGluZU9yZGVy" +
        "AGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgA" +
        "AAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/eERlbnNpdHkAZmxvYXQABAAAAAAAkEIAagEAAAAA" +
        "AAAAAAAAtwAAAHheYyAEGlABfxo3ww7fnJnyzy4Zt9VbnnmERQsKQNPfNfOGsd2ZJWnc/wtnXQeyRLFoQQHo+iVP+kytT9qs" +
        "98z5LMiUdCxaUACafpAL0iVznk2dqfX/cJrJs4/7HVHhAYd9TsgQTZ75j9O+hAX1jfOS9rjcVv3Fhi5PSL/E87BVk3Mv6vN9" +
        "8tnSWX7ckmT9z6bk8H/sKmP9nbhgXtJtVVL1OzaEP5V+yvZL+hn/R9bf8xMB++K/vg==";

    private const string RleData =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QASQAAAEEAAQAAAAAAAAABAAAAAQAAAEIAAQAAAAAAAAABAAAAAQAAAEcAAQAAAAAA" +
        "AAABAAAAAQAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAAWRhdGFXaW5kb3cAYm94" +
        "MmkAEAAAAAAAAAAAAAAABwAAAAMAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAAcAAAADAAAAbGluZU9yZGVy" +
        "AGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgA" +
        "AAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/eERlbnNpdHkAZmxvYXQABAAAAAAAkEIAggEAAAAA" +
        "AAClAQAAAAAAAM4BAAAAAAAA+AEAAAAAAAAAAAAAGwAAAP8AFoD+LtIFqf/GBoD+iHAFgP+EBoD+e4EFgAEAAAAhAAAA/wAW" +
        "gPyf0tLRA9L/YAaA/ohwBYD/hAaA+HmAgIGAgIGAAgAAACIAAAD/ABaA958kIyQk+9LS7waA/ohwBYD/hAaA+HWAgYGAgYCA" +
        "AwAAACAAAAD/ABeA+J+AD/HHyE1BBoD+iHAFgP+EBoD7SKGEgoICgQ==";

    private const string NoneData =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QASQAAAEEAAQAAAAAAAAABAAAAAQAAAEIAAQAAAAAAAAABAAAAAQAAAEcAAQAAAAAA" +
        "AAABAAAAAQAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAAGRhdGFXaW5kb3cAYm94" +
        "MmkAEAAAAAAAAAAAAAAABwAAAAMAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAAcAAAADAAAAbGluZU9yZGVy" +
        "AGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgA" +
        "AAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/eERlbnNpdHkAZmxvYXQABAAAAAAAkEIAggEAAAAA" +
        "AADKAQAAAAAAABICAAAAAAAAWgIAAAAAAAAAAAAAQAAAAAA8ADwAPAA8ADwAPAA8ADwARAA0ADQANAA0ADQANAA0ADgAOAA4" +
        "ADgAOAA4ADgAOK4zADQpNFI0ezSkNM009jQBAAAAQAAAAAA8ADwAPAA8ADwAPAA8ADwARAA0ADQANAA0ADQANAA0ADgAOAA4" +
        "ADgAOAA4ADgAOB8xcTHDMRQyZjK4MgozXDMCAAAAQAAAAAA8ADwAPAA8ADwAPAA8ADwARAA0ADQANAA0ADQANAA0ADgAOAA4" +
        "ADgAOAA4ADgAOB8twy1mLgovri8pMHswzTADAAAAQAAAAAA8ADwAPAA8ADwAPAA8ADwARAA0ADQANAA0ADQANAA0ADgAOAA4" +
        "ADgAOAA4ADgAOAAAHyEfJa4nHylmKq4reyw=";

    private const string BigZipData =
        "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";
}
