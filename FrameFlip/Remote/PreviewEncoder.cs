using System.IO;
using System.Windows.Media.Imaging;

namespace FrameFlip.Remote;

/// <summary>
/// Macht aus einem gerenderten Frame ein Bild, das durch ein Mobilnetz passt.
///
/// Der Renderordner enthaelt PNGs in voller Aufloesung - bei 4096x2304 sind das
/// leicht 70 MB je Datei. So etwas ungefragt an ein Handy zu schicken waere in
/// jeder Hinsicht falsch: Es dauert zu lange, es kostet Datenvolumen, und auf einem
/// Bildschirm mit 400 Punkten Breite sieht man davon nichts.
///
/// Deshalb wird beim Dekodieren bereits verkleinert. DecodePixelWidth ist kein
/// nachtraegliches Skalieren: Der Dekoder liest das Bild gar nicht erst in voller
/// Groesse ein, was Zeit und Speicher spart - und beides zaehlt hier, weil daneben
/// ein Render laeuft.
/// </summary>
public static class PreviewEncoder
{
    /// <summary>
    /// Breite, auf die verkleinert wird.
    ///
    /// 1280 ist mehr als jedes Handy darstellt und laesst trotzdem Luft zum
    /// Hineinzoomen. Darueber waechst nur die Datei.
    /// </summary>
    public const int Width = 1280;

    /// <summary>
    /// JPEG-Qualitaet. 80 ist die uebliche Grenze, ab der mehr Qualitaet vor allem
    /// mehr Bytes bedeutet.
    /// </summary>
    public const int Quality = 80;

    /// <summary>Deckel, damit ein absurdes Bild nicht die Nachrichtengrenze sprengt.</summary>
    public const int MaxBytes = 900 * 1024;

    /// <summary>
    /// null heisst: ging nicht. Datei weg, halb geschrieben, unbekanntes Format -
    /// alles derselbe Fall, und keiner davon darf hier etwas werfen.
    /// </summary>
    public static byte[]? Encode(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            // FileShare.ReadWrite | Delete: Blender schreibt moeglicherweise gerade
            // in denselben Ordner. Ein exklusiver Zugriff waere ein Grund, warum ein
            // Render scheitert - das waere die Vorschau nicht wert.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                            FileShare.ReadWrite | FileShare.Delete);

            var decoded = BitmapFrame.Create(
                file,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            var source = decoded.PixelWidth > Width
                ? (BitmapSource)new TransformedBitmap(decoded, new System.Windows.Media.ScaleTransform(
                    Width / (double)decoded.PixelWidth,
                    Width / (double)decoded.PixelWidth))
                : decoded;

            var encoder = new JpegBitmapEncoder { QualityLevel = Quality };
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);

            return buffer.Length <= MaxBytes ? buffer.ToArray() : null;
        }
        catch (Exception)
        {
            // Ein Frame, der sich nicht lesen laesst, ist kein Grund, irgendetwas
            // anzuhalten. Das Handy zeigt dann eben weiter die Leerstelle.
            return null;
        }
    }
}
