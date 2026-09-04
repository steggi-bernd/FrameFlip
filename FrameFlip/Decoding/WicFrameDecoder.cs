using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameFlip.Decoding;

/// <summary>
/// Windows Imaging Component ueber die WPF-Wrapper. Deckt PNG, JPEG, TIFF, BMP und
/// - sofern die Windows-Erweiterung installiert ist - WebP ab. Keine externe Abhaengigkeit.
/// </summary>
public sealed class WicFrameDecoder : IFrameDecoder
{
    private static readonly string[] Extensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".tif", ".tiff", ".bmp", ".webp"
    };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool TryProbeSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = Open(path);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0) return false;
            var first = decoder.Frames[0];
            width = first.PixelWidth;
            height = first.PixelHeight;
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryProbeInfo(string path, out ImageInfo info)
    {
        info = default;

        try
        {
            using var stream = Open(path);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0) return false;
            var first = decoder.Frames[0];
            if (first.PixelWidth <= 0 || first.PixelHeight <= 0) return false;

            info = new ImageInfo(first.PixelWidth, first.PixelHeight,
                                 first.Format.BitsPerPixel, first.Format.ToString());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryDecode(string path, int maxWidth, int maxHeight, PixelBufferAllocator allocate, out DecodedFrame frame)
    {
        frame = default;
        if (maxWidth <= 0 || maxHeight <= 0) return false;

        // Erster Durchgang: nur der Header, um die Quellgroesse zu kennen.
        // Ohne die waere nicht entscheidbar, ob DecodePixelWidth herunter- oder hochskalieren wuerde.
        if (!TryProbeSize(path, out int sourceWidth, out int sourceHeight)) return false;

        double scale = Math.Min(1.0, Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight));

        BitmapSource source;
        using (var stream = Open(path))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;               // laedt sofort, Stream darf danach zu
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            if (scale < 1.0)
            {
                // Nur eine Achse setzen, damit WIC das Seitenverhaeltnis exakt haelt.
                if ((long)sourceWidth * maxHeight >= (long)sourceHeight * maxWidth)
                    image.DecodePixelWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
                else
                    image.DecodePixelHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            }

            image.EndInit();
            image.Freeze();
            source = image;
        }

        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            source = converted;
        }

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || height <= 0) return false;

        int stride = width * 4;
        byte[] pixels = allocate(stride * height);
        source.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

        frame = new DecodedFrame(pixels, width, height, stride);
        return true;
    }

    /// <summary>
    /// FileShare.ReadWrite ist Pflicht: Blender schreibt womoeglich gerade in denselben Ordner.
    /// SequentialScan gibt dem Cache-Manager den richtigen Hinweis fuer den Readahead.
    /// </summary>
    private static FileStream Open(string path)
        => new(path, FileMode.Open, FileAccess.Read,
               FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
}
