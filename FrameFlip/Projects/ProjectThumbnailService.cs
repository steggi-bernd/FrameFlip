using System.IO;
using System.Windows.Media.Imaging;

namespace FrameFlip.Projects;

/// <summary>
/// Verkleinerte Bilder und ihr gemeinsamer Cache. Die Seite besitzt weiterhin
/// Kacheln und Ansichtswechsel; dieser Dienst liefert nur eingefrorene Bitmaps.
/// </summary>
internal sealed class ProjectThumbnailService
{
    internal static ProjectThumbnailService Shared { get; } = new();

    private readonly Dictionary<(string Path, int Width), BitmapSource> _cache = new();
    private readonly Func<string, int, BitmapSource?> _decode;

    /// <param name="decode">Testquelle; gelieferte Bilder muessen eingefroren sein.</param>
    internal ProjectThumbnailService(Func<string, int, BitmapSource?>? decode = null)
        => _decode = decode ?? Decode;

    internal async Task<BitmapSource?> LoadAsync(string path, int width, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = (path, width);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var known)) return known;
        }

        var image = await Task.Run(() =>
        {
            try { return _decode(path, width); }
            catch (Exception)
            {
                // EXR, halbfertiger Frame oder defektes PNG: Der Hintergrund
                // bleibt stehen; ein spaeterer Versuch darf erneut lesen.
                return null;
            }
        }, token).ConfigureAwait(false);

        token.ThrowIfCancellationRequested();
        if (image is null) return null;

        lock (_cache)
        {
            token.ThrowIfCancellationRequested();
            // Die bisherige Grenze beibehalten: Ist der Cache schon groesser
            // als 400, beginnt das naechste neue Bild wieder von vorne.
            if (_cache.Count > 400) _cache.Clear();
            _cache[key] = image;
        }
        return image;
    }

    private static BitmapSource Decode(string path, int width)
    {
        // Der eigene Stream wird auch dann geschlossen, wenn WIC ein defektes
        // Bild ablehnt. OnLoad macht das fertige Bitmap unabhaengig vom Stream.
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.DecodePixelWidth = width;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
