using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameFlip.Imaging;
using FrameFlip.Imaging.Grading;
using PixelFormats = System.Windows.Media.PixelFormats;

namespace FrameFlip.Views;

/// <summary>
/// Die kleinen Bilder in der Ebenenliste.
///
/// Ein Name sagt, wie ein Pass heisst. Eine Miniatur sagt, was darin steht - und das
/// ist die haeufigere Frage: Ob der Glanzpass ueberhaupt etwas enthaelt, ob die
/// zweite Fassung dieselbe Szene zeigt, ob das Wasserzeichen das richtige ist. Ein
/// leerer Pass sieht schwarz aus, und das ist eine Antwort; "GlossDir" ist keine.
/// </summary>
public partial class AtelierPage
{
    /// <summary>Breite der Miniatur in Bildpunkten. Doppelt so gross wie gezeigt, damit sie scharf steht.</summary>
    private const int ThumbWidth = 60;

    private const int ThumbHeight = 36;

    private readonly Dictionary<string, ImageSource> _thumbnails = new(StringComparer.Ordinal);

    /// <summary>
    /// Die Miniatur einer Ebene. Null, wenn sie keine eigene Flaeche hat.
    ///
    /// Eine Einstellungsebene und eine Gruppe bringen kein Bild mit - fuer sie eine
    /// Miniatur zu erfinden hiesse, das Ergebnis darunter zu zeigen, und das waere
    /// eine Auskunft ueber etwas anderes.
    /// </summary>
    private ImageSource? Thumbnail(ImageLayer layer)
    {
        if (layer.Content is LayerContent.Adjustment or LayerContent.Group) return null;
        if (!_sources.TryGetValue(layer.Source, out var frame)) return null;

        if (_thumbnails.TryGetValue(layer.Source, out var cached)) return cached;

        var made = Render(frame);
        _thumbnails[layer.Source] = made;

        return made;
    }

    /// <summary>
    /// Zeichnet die Miniatur - abgetastet, nicht gemittelt.
    ///
    /// Bei 4K waeren es sonst fuenfundzwanzig Millionen Bildpunkte fuer ein Bild von
    /// sechzig. Abtasten heisst, dass feines Rauschen flimmert; in einer Miniatur von
    /// dreissig Punkten Breite sieht man das nicht, und die Frage, die sie beantwortet,
    /// beantwortet sie trotzdem.
    /// </summary>
    private ImageSource Render(FloatFrame frame)
    {
        var view = ViewFor(frame);

        int stride = ThumbWidth * 4;
        var pixels = new byte[stride * ThumbHeight];

        for (int y = 0; y < ThumbHeight; y++)
        {
            int source = Math.Min(y * frame.Height / ThumbHeight, frame.Height - 1);

            for (int x = 0; x < ThumbWidth; x++)
            {
                int column = Math.Min(x * frame.Width / ThumbWidth, frame.Width - 1);
                int i = source * frame.Width + column;

                float r = frame.R[i], g = frame.G[i], b = frame.B[i];
                view.Apply(ref r, ref g, ref b);

                int at = y * stride + x * 4;
                pixels[at] = Byte(b);
                pixels[at + 1] = Byte(g);
                pixels[at + 2] = Byte(r);
                pixels[at + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(ThumbWidth, ThumbHeight, 96, 96,
                                         PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        return bitmap;

        static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }

    /// <summary>
    /// Vergisst die Miniaturen, deren Pass nicht mehr gelesen ist.
    ///
    /// Aufgehoben werden sie nach dem Namen der Quelle - und derselbe Name meint in
    /// einer anderen Datei etwas anderes. Beim Oeffnen muss deshalb alles weg, sonst
    /// zeigt die Liste das vorige Bild.
    /// </summary>
    private void ForgetThumbnails() => _thumbnails.Clear();

    // ------------------------------------------------------- Ziehen und Fallenlassen

    /// <summary>
    /// Eine Datei auf das Bild gezogen wird eine Bildebene.
    ///
    /// Dieselbe Geste wie auf der Ebenenliste, nur auf der groesseren Flaeche - und
    /// das ist die, auf die man zielt, wenn man etwas "ins Bild" legen will.
    /// </summary>
    /// <summary>
    /// Eine Datei wurde ins Bild fallen gelassen.
    ///
    /// Ist noch nichts offen, wird die erste Datei das BILD und nicht eine Ebene
    /// darauf. Vorher tat ein Zug auf die leere Flaeche gar nichts: Die Bedingung
    /// verlangte ein offenes Bild, und genau das wollte ja gerade jemand oeffnen.
    /// Eine Ebene ohne Bild darunter gibt es ohnehin nicht - die unterste Ebene IST
    /// das Bild.
    /// </summary>
    private void OnImageFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        e.Handled = true;

        var usable = files.Where(Readable).ToList();
        if (usable.Count == 0) return;

        int first = 0;

        if (_frame is null)
        {
            Open(usable[0]);
            first = 1;
        }

        for (int i = first; i < usable.Count; i++) Layers.AddImage(usable[i]);
    }

    /// <summary>
    /// Was sich oeffnen laesst. Dieselbe Liste wie im Ebenenstreifen.
    ///
    /// Geprueft wird die Endung und nicht der Inhalt: Die Antwort muss da sein,
    /// waehrend die Maus ueber der Flaeche schwebt, und eine Datei dafuer zu oeffnen
    /// waere ein Lesezugriff je Mausbewegung.
    /// </summary>
    private static bool Readable(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        return extension.ToLowerInvariant() is ".exr" or ".png" or ".jpg" or ".jpeg"
                                            or ".tif" or ".tiff" or ".bmp" or ".webp";
    }

    private void OnImageFilesDragOver(object sender, DragEventArgs e)
    {
        // Auch ohne offenes Bild: Dann wird die Datei das Bild. Vorher sagte der
        // Zeiger hier nein, und zwar in genau der Lage, in der jemand anfangen will.
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(Readable)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }
}
