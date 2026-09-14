using System.IO;
using FrameFlip.Decoding.Exr;
using FrameFlip.Imaging;

namespace FrameFlip.Decoding;

/// <summary>
/// EXR fuer die Wiedergabe: liest die Farbkanaele, wendet die Sichtumwandlung an und
/// liefert Bgra32 wie jeder andere Decoder auch. Puffer, Cache und Oberflaeche merken
/// keinen Unterschied.
///
/// Was dabei verlorengeht, ist Absicht: Die Gleitkommawerte werden auf acht Bit
/// gebracht, weil die Wiedergabe genau darauf ausgelegt ist. Wer mit den Werten
/// oberhalb von Weiss arbeiten will, liest die Datei ein zweites Mal - ueber
/// <see cref="ExrReader"/>, der sie unveraendert herausgibt.
/// </summary>
public sealed class ExrFrameDecoder : IFrameDecoder
{
    private static readonly string[] Extensions = { ".exr" };

    private readonly Lazy<IViewTransform> _view;

    /// <summary>
    /// Die Sichtumwandlung wird erst beim ersten EXR gebaut, nicht beim Start.
    ///
    /// Das ist kein Feinschliff: AgX_Base_sRGB.cube hat 57 Stufen je Achse, also
    /// 185 193 Zeilen Text, die eingelesen und in 2,2 MB Gleitkomma verwandelt werden
    /// wollen. Wer nur PNG-Sequenzen ansieht, soll dafuer nichts zahlen - und das
    /// Programm startet in den Infobereich, nicht in ein Bild.
    /// </summary>
    public ExrFrameDecoder(Func<IViewTransform> view)
        => _view = new Lazy<IViewTransform>(view, isThreadSafe: true);

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    /// <summary>
    /// Welche Umwandlung benutzt wird - fuer die Anzeige. Loest das Laden aus.
    /// </summary>
    public string ViewName => _view.Value.Name;

    /// <summary>
    /// Die Umwandlung selbst, fuer den Gleitkommaweg der angehaltenen Anzeige.
    ///
    /// Sie muss dieselbe sein wie hier im Decoder, sonst saehe das stehende Bild
    /// anders aus als das laufende - und zwar genau in dem Moment, in dem man
    /// anhaelt, um genauer hinzusehen.
    /// </summary>
    public IViewTransform View => _view.Value;

    public bool TryProbeSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = ExrReader.Open(path);
            var header = ExrHeaderReader.Read(stream);
            width = header.DataWindow.Width;
            height = header.DataWindow.Height;
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
            using var stream = ExrReader.Open(path);
            var header = ExrHeaderReader.Read(stream);
            if (!header.DataWindow.IsValid) return false;

            // Die Farbtiefe ist die der Farbkanaele, nicht die der Datei: in einem
            // Multilayer-Render liegt Z als 32 Bit neben Farbkanaelen mit 16.
            var picked = ExrChannelPick.Colour(header);
            int bits = picked.Red is null ? 16
                     : header.Channels.First(c => c.Name == picked.Red).BytesPerSample * 8;

            int channels = picked.HasAlpha ? 4 : 3;
            string format = $"EXR {header.Compression}" + (picked.Layer is null ? "" : $", {picked.Layer}");

            info = new ImageInfo(header.DataWindow.Width, header.DataWindow.Height,
                                 bits * channels, format)
            {
                ChannelCount = channels,
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryDecode(string path, int maxWidth, int maxHeight, PixelBufferAllocator allocate,
                          out DecodedFrame frame)
    {
        frame = default;
        if (maxWidth <= 0 || maxHeight <= 0) return false;

        try
        {
            using var stream = ExrReader.Open(path);
            var header = ExrHeaderReader.Read(stream);

            var picked = ExrChannelPick.Colour(header);
            if (picked.Red is null || picked.Green is null || picked.Blue is null) return false;

            var wanted = new List<string> { picked.Red, picked.Green, picked.Blue };
            if (picked.Alpha is not null) wanted.Add(picked.Alpha);

            var image = ExrReader.Read(stream, header, wanted);

            var r = image.Channel(picked.Red)!;
            var g = image.Channel(picked.Green)!;
            var b = image.Channel(picked.Blue)!;
            var a = picked.Alpha is null ? null : image.Channel(picked.Alpha);

            // Ganzzahlige Verkleinerung, wie sie der Puffer verlangt. Anders als WIC
            // kann EXR nicht schon beim Dekodieren kleiner werden - die Zeilen liegen
            // gepackt in Bloecken.
            int step = 1;
            while (image.Width / (step + 1) >= 1 && image.Height / (step + 1) >= 1 &&
                   (image.Width / step > maxWidth || image.Height / step > maxHeight))
            {
                step++;
            }

            int width = Math.Max(1, image.Width / step);
            int height = Math.Max(1, image.Height / step);
            int stride = width * 4;

            byte[] pixels = allocate(stride * height);
            Convert(r, g, b, a, image.Width, step, width, height, stride, pixels, _view.Value);

            frame = new DecodedFrame(pixels, width, height, stride);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Rechnet die Gleitkommakanaele nach Bgra32.
    ///
    /// Gemittelt wird VOR der Sichtumwandlung, also in linearem Licht. Der umgekehrte
    /// Weg waere der haeufigere Fehler und ein sichtbarer: ein Mittelwert aus bereits
    /// kodierten Werten faellt zu dunkel aus, weil die Kodierung nicht gerade ist.
    /// Bei einer verkleinerten Vorschau eines Renders mit hellen Kanten sieht man das
    /// sofort.
    /// </summary>
    private static void Convert(float[] r, float[] g, float[] b, float[]? a,
                                int sourceWidth, int step,
                                int width, int height, int stride,
                                byte[] target, IViewTransform view)
    {
        float divisor = step * step;

        Parallel.For(0, height, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        },
        y =>
        {
            int row = y * stride;

            for (int x = 0; x < width; x++)
            {
                float sr = 0, sg = 0, sb = 0, sa = 0;

                for (int dy = 0; dy < step; dy++)
                {
                    int line = (y * step + dy) * sourceWidth + x * step;

                    for (int dx = 0; dx < step; dx++)
                    {
                        int i = line + dx;
                        sr += r[i];
                        sg += g[i];
                        sb += b[i];
                        if (a is not null) sa += a[i];
                    }
                }

                sr /= divisor;
                sg /= divisor;
                sb /= divisor;

                view.Apply(ref sr, ref sg, ref sb);

                int at = row + x * 4;
                target[at] = ToByte(sb);
                target[at + 1] = ToByte(sg);
                target[at + 2] = ToByte(sr);
                target[at + 3] = a is null ? (byte)255 : ToByte(Math.Clamp(sa / divisor, 0f, 1f));
            }
        });
    }

    private static byte ToByte(float value)
        => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    /// <summary>
    /// Baut den Decoder mit der Sichtumwandlung aus einer Blender-Installation.
    ///
    /// Der Pfad wird als Funktion gereicht, weil die Decoder angelegt werden, bevor
    /// die Einstellungen gelesen sind - und weil er sich danach noch aendern kann.
    /// Gefragt wird erst, wenn das erste EXR aufgeht.
    ///
    /// Ohne Blender bleibt "Standard". Das Bild ist dann nicht falsch, nur flacher
    /// in den Lichtern als Blenders Vorschau.
    /// </summary>
    public static ExrFrameDecoder Create(Func<string?>? blenderExecutable = null)
        => new(() => ViewTransformCache.For((blenderExecutable ?? ExrViewSettings.Resolve)()));
}

/// <summary>
/// Woher die Sichtumwandlung kommt.
///
/// Der eingetragene Pfad hat Vorrang; ist keiner da, wird gesucht. Das ist die
/// wichtigere Haelfte: Wer ein EXR oeffnet, soll Blenders Bild sehen, ohne vorher
/// etwas eingestellt zu haben - und wer kein Blender hat, bekommt "Standard" und
/// keine Fehlermeldung.
/// </summary>
public static class ExrViewSettings
{
    /// <summary>
    /// Der eingetragene Pfad zu blender.exe. Wird vom Programm aus den Einstellungen
    /// gesetzt; gefragt wird erst beim ersten EXR.
    /// </summary>
    public static Func<string?>? BlenderPath { get; set; }

    internal static string? Resolve()
    {
        string? configured = BlenderPath?.Invoke();
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        // Nichts eingetragen: selbst nachsehen. Die Suche geht ueber Registrierung,
        // Steam-Bibliotheken und Suchpfad und kostet einen Moment - sie passiert
        // einmal, beim ersten EXR, und das Ergebnis bleibt im Zwischenspeicher.
        try
        {
            foreach (var install in Rendering.BlenderFinder.Find())
            {
                if (Imaging.AgxViewTransform.FindLut(install.Path) is not null) return install.Path;
            }
        }
        catch (Exception)
        {
            // Kein Blender zu finden ist kein Fehler, sondern ein Rechner ohne Blender.
        }

        return null;
    }
}

/// <summary>
/// Haelt geladene Sichtumwandlungen fest.
///
/// Es gibt mehrere Decoder-Registrierungen im Programm - Vorschaufenster, Fernzugriff,
/// Projektansicht legen jede ihre eigene an. Ohne diesen Zwischenspeicher laege
/// dieselbe 2,2-MB-Tabelle mehrfach im Speicher, und jedes Mal waeren 185 193 Zeilen
/// Text neu zu lesen.
/// </summary>
internal static class ViewTransformCache
{
    private static readonly object Lock = new();
    private static string? _key;
    private static IViewTransform? _cached;

    public static IViewTransform For(string? blenderExecutable)
    {
        string key = blenderExecutable ?? string.Empty;

        lock (Lock)
        {
            if (_cached is not null && string.Equals(_key, key, StringComparison.OrdinalIgnoreCase))
                return _cached;

            IViewTransform built = string.IsNullOrWhiteSpace(blenderExecutable)
                ? new StandardViewTransform()
                : AgxViewTransform.TryLoad(blenderExecutable) ?? (IViewTransform)new StandardViewTransform();

            _key = key;
            _cached = built;
            return built;
        }
    }
}
