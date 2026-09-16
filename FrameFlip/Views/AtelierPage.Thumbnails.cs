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
    private void OnImageFilesDropped(object sender, DragEventArgs e)
    {
        if (_frame is null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        e.Handled = true;

        foreach (string file in files) Layers.AddImage(file);
    }

    private void OnImageFilesDragOver(object sender, DragEventArgs e)
    {
        // Ohne offenes Bild gibt es keinen Stapel, in den etwas gelegt werden
        // koennte - der Zeiger soll das sagen, statt es klaglos zu schlucken.
        e.Effects = _frame is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }
}
