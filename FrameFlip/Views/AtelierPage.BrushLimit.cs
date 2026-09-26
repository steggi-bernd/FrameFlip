using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;

namespace FrameFlip.Views;

/// <summary>
/// Der objektgebundene Pinsel (docs/Atelier-Werkzeugplan.md, W1): Ein Strich bleibt auf
/// dem Objekt, auf dem er beginnt. Welches Objekt das ist und wie weit es jeden Punkt
/// deckt, sagt die Kryptomatte der Datei - exakt, auch an weichen Kanten.
///
/// Die Deckung wird einmal je Objekt und Bild in der Groesse der Maske gebaut, gepackt und
/// dem Strich mitgegeben. So spielt er im Maskenverlauf genau so nach, wie er gemalt wurde,
/// auch ohne die Datei.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Gebaute Begrenzungen: Kryptomatte, Kennung, Bild.</summary>
    private readonly Dictionary<(string Set, float Id, int Number), string> _limits = new();

    /// <summary>
    /// Die gepackte Deckung des Objekts an diesem Punkt des Bildes - oder null, wenn dort
    /// keines steht oder die Datei keine Kryptomatte hat. Dann malt der Strich ungebunden.
    /// </summary>
    internal string? ObjectLimitAt(float imageX, float imageY)
    {
        if (_frame is null || _path is null || _cryptomattes.Count == 0) return null;

        // Die Objekt-Ebene, wenn es sie gibt - sonst die erste.
        var set = _cryptomattes.FirstOrDefault(s => s.ShortName.Contains("Object", StringComparison.OrdinalIgnoreCase))
                  ?? _cryptomattes[0];

        var levels = Cryptomatte.Levels(_passes, set.Prefix)
            .Select(level => _sources.TryGetValue(level, out var known) ? known : FloatFrame.FromExrPass(_path, level))
            .OfType<FloatFrame>()
            .ToList();

        if (levels.Count == 0) return null;

        int width = levels[0].Width, height = levels[0].Height;
        int x = (int)imageX, y = (int)imageY;

        if (x < 0 || y < 0 || x >= width || y >= height) return null;

        float id = levels[0].R[y * width + x];
        if (id == 0f) return null;

        var key = (set.Prefix, id, _number);
        if (_limits.TryGetValue(key, out var known)) return known;

        // In der Groesse der Maske: je Maskenpunkt die Deckung in der Mitte seines Feldes.
        int cols = Math.Max(1, width / PaintedMask.Coarse);
        int rows = Math.Max(1, height / PaintedMask.Coarse);
        var cover = new byte[cols * rows];
        var ids = new[] { id };

        Parallel.For(0, rows, my =>
        {
            int py = Math.Min(height - 1, my * PaintedMask.Coarse + PaintedMask.Coarse / 2);

            for (int mx = 0; mx < cols; mx++)
            {
                int px = Math.Min(width - 1, mx * PaintedMask.Coarse + PaintedMask.Coarse / 2);
                cover[my * cols + mx] = (byte)MathF.Round(Masking.Coverage(levels, ids, py * width + px) * 255f);
            }
        });

        // Bilder einer Folge haben verschiedene Objekte an derselben Stelle - der Vorrat
        // bleibt klein.
        if (_limits.Count > 16) _limits.Clear();

        return _limits[key] = PaintedMask.Pack(cover);
    }
}
