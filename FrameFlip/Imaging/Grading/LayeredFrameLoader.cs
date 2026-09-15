using System.IO;

namespace FrameFlip.Imaging.Grading;

/// <summary>
/// Liest ein Bild so, wie der Ebenenstapel es meint.
///
/// Es gibt genau einen Weg von der Datei zum Frame, und er steht hier. Die Vorschau,
/// der Bilderlauf und der Videolauf nehmen alle diesen - sonst saehe der Export
/// eines Tages anders aus als das, worauf sich jemand beim Einstellen verlassen hat,
/// und der Unterschied fiele erst nach dreihundert Bildern auf.
/// </summary>
public static class LayeredFrameLoader
{
    /// <summary>
    /// Liest die Passe, die der Stapel braucht, und setzt sie zusammen.
    ///
    /// Ohne Stapel oder mit einem, der nichts tut, wird die Datei schlicht gelesen -
    /// dann gibt es weder Zusammensetzung noch eine zweite Kopie im Speicher.
    /// </summary>
    /// <param name="plain">
    /// Wie das Bild ohne Ebenen gelesen wird. Die Atelierseite reicht hier ihre
    /// eigene Decoderliste durch; wer nichts angibt, bekommt EXR direkt und alles
    /// uebrige ueber die Windows-Bildverarbeitung.
    /// </param>
    public static FloatFrame? Load(string path, LayerStack? layers, Func<string, FloatFrame?>? plain = null)
    {
        plain ??= Plain;

        if (layers is null || layers.IsPassThrough) return plain(path);

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

        foreach (string source in layers.NeededSources())
        {
            var frame = source.Length == 0 ? plain(path) : FloatFrame.FromExrPass(path, source);
            if (frame is not null) sources[source] = frame;
        }

        // Nichts Lesbares dabei - etwa, weil das Rezept von einer Datei mit anderen
        // Passen stammt. Das Bild selbst zu zeigen ist dann die bessere Antwort als
        // ein schwarzes Feld: Man sieht, dass die Datei in Ordnung ist, und sucht
        // den Fehler dort, wo er liegt.
        return LayerComposer.Compose(layers, sources) ?? plain(path);
    }

    /// <summary>Der gewoehnliche Weg: EXR direkt, alles andere ueber Windows.</summary>
    public static FloatFrame? Plain(string path)
    {
        if (Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return FloatFrame.FromExr(path);

        var decoder = new Decoding.WicFrameDecoder();
        return decoder.TryDecode(path, 16384, 16384, n => new byte[n], out var decoded)
            ? FloatFrame.FromBgra32(decoded.Pixels, decoded.Width, decoded.Height, decoded.Stride)
            : null;
    }
}
