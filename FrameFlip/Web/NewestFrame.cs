using System.IO;
using FrameFlip.Bridge;
using FrameFlip.Decoding;
using FrameFlip.Projects;

namespace FrameFlip.Web;

/// <summary>
/// Welches Bild die Seite zeigt.
///
/// Waehrend eines Renders ist die Antwort einfach: das zuletzt geschriebene. Steht
/// gerade nichts, waere eine leere Flaeche die schlechtere Auskunft - wer auf die
/// Seite sieht, will meist wissen, wie das letzte Ergebnis aussieht. Also das
/// neueste Bild aus dem Ausgabeordner des zuletzt protokollierten Renders.
///
/// GESUCHT WIRD NUR DORT. Kein Durchsuchen der Platte, keine Ordner, die der
/// Aufrufer nennt - die Quelle ist das, was FrameFlip beim Rendern selbst vermerkt
/// hat. Findet sich dort nichts, bleibt es eben bei der leeren Flaeche.
///
/// Und es wird nicht bei jeder Anfrage gesucht: Ein Ordner mit tausend Bildern
/// kostet bei jedem Abruf Zeit, und zwei Sekunden alt ist die Antwort allemal gut
/// genug fuer ein Bild, das im Minutentakt entsteht.
/// </summary>
public sealed class NewestFrame
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(3);

    private readonly Func<RenderJob?> _job;
    private readonly FrameDecoderRegistry _decoders;
    private readonly object _gate = new();

    private string? _cached;
    private DateTime _checkedUtc = DateTime.MinValue;

    public NewestFrame(Func<RenderJob?> job, FrameDecoderRegistry decoders)
    {
        _job = job;
        _decoders = decoders;
    }

    public string? Path()
    {
        // Laeuft ein Render, ist die Bruecke die beste Quelle - sie meldet den Frame,
        // sobald er geschrieben ist, ohne dass jemand nachsehen muesste.
        if (_job() is { IsRunning: true, LatestFrameFile: { Length: > 0 } live }) return live;

        lock (_gate)
        {
            if (DateTime.UtcNow - _checkedUtc < Freshness) return _cached;

            _checkedUtc = DateTime.UtcNow;
            _cached = FindLast();

            return _cached;
        }
    }

    private string? FindLast()
    {
        // Zuerst der Frame des letzten Auftrags, auch wenn er nicht mehr laeuft.
        if (_job()?.LatestFrameFile is { Length: > 0 } last && File.Exists(last)) return last;

        try
        {
            foreach (var known in ProjectLibrary.Load().Files)
            {
                if (!known.HasOutput) continue;

                string? newest = NewestIn(known.Output);

                if (newest is not null) return newest;
            }
        }
        catch (Exception)
        {
            // Merkliste unlesbar, Ordner verschwunden: Dann gibt es kein Bild, und
            // die Seite sagt das auch.
        }

        return null;
    }

    private string? NewestIn(string folder)
    {
        try
        {
            string? best = null;
            DateTime bestTime = DateTime.MinValue;

            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (!_decoders.IsSupported(System.IO.Path.GetExtension(file))) continue;

                var written = File.GetLastWriteTimeUtc(file);

                if (written <= bestTime) continue;

                bestTime = written;
                best = file;
            }

            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
