using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Ein kurzer Blick in eine Bilddatei beim Anlegen einer Ebene - wie viel davon schwarz
/// ist und ob sie Deckung mitbringt.
///
/// Auf 128 Punkte Breite verkleinert gelesen: Die Frage ist "ueberwiegend schwarz?",
/// und dafuer genuegt ein Daumennagel. Die ganze Datei zu lesen hiesse, beim Ablegen
/// eines 4K-Bildes eine spuerbare Pause zu machen - fuer eine Voreinstellung.
/// </summary>
internal static class ImageProbe
{
    /// <summary>
    /// Anteil fast schwarzer Bildpunkte und ob Deckung vorkommt. Null bei einer EXR
    /// (die liest WIC nicht) und bei allem, was sich so nicht lesen laesst.
    /// </summary>
    public static (double BlackShare, bool HasAlpha)? Look(string path)
    {
        if (PassRoles.IsSceneLinear(path) || !File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 128;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();

            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            if (width <= 0 || height <= 0) return null;

            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);

            int black = 0, clear = 0;

            for (int i = 0; i < width * height; i++)
            {
                byte b = pixels[i * 4], g = pixels[i * 4 + 1], r = pixels[i * 4 + 2], a = pixels[i * 4 + 3];

                if (Math.Max(r, Math.Max(g, b)) <= 6) black++;
                if (a < 250) clear++;
            }

            int count = width * height;

            // Ein paar durchsichtige Punkte am Rand machen noch kein Bild mit Deckung.
            return (black / (double)count, clear > count / 100);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException
                                   or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Der Mischmodus einer neuen Ebene aus dieser Datei: erst der Name, dann der Inhalt,
    /// sonst Normal. Siehe <see cref="PassRoles"/> - nur beim Anlegen zu fragen.
    /// </summary>
    public static BlendMode ModeFor(string path)
    {
        bool linear = PassRoles.IsSceneLinear(path);

        return PassRoles.ByName(path, linear)
               ?? (Look(path) is var (black, alpha) ? PassRoles.ByContent(black, alpha, linear) : null)
               ?? BlendMode.Normal;
    }
}
