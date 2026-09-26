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
        => LoadAll(path, layers, plain).Frame;

    /// <summary>
    /// Wie <see cref="Load"/>, gibt aber auch die Ebenen zurueck, die OBENAUF
    /// liegen.
    ///
    /// Die gehoeren nicht ins zusammengesetzte Bild - sie werden erst nach der
    /// Bildwerdung aufgetragen. Wer nur den Frame nimmt, bekommt das Bild ohne
    /// Wasserzeichen; das ist fuer alles richtig, was mit dem Bild rechnet, und
    /// falsch fuer das, was es ausgibt.
    /// </summary>
    public static (FloatFrame? Frame, OverlayPlan[] Overlays) LoadAll(
        string path, LayerStack? layers, Func<string, FloatFrame?>? plain = null)
    {
        plain ??= Plain;

        if (layers is null || layers.IsPassThrough) return (plain(path), Overlays.None);

        var sources = new Dictionary<string, FloatFrame>(StringComparer.Ordinal);

        foreach (var read in layers.Reads())
        {
            var frame = Read(read, path, plain);
            if (frame is not null) sources[read.Key] = frame;
        }

        // Nichts Lesbares dabei - etwa, weil das Rezept von einer Datei mit anderen
        // Passen stammt. Das Bild selbst zu zeigen ist dann die bessere Antwort als
        // ein schwarzes Feld: Man sieht, dass die Datei in Ordnung ist, und sucht
        // den Fehler dort, wo er liegt.
        //
        // Mit der Nummer des Bildes: Eine je Bild gemalte Maske waehlt ihren Anstrich
        // danach. Ohne sie nahm jedes Bild des Exports den Anstrich von Bild 0 - also
        // keinen, und die Ebene wirkte ueberall, waehrend die Vorschau sie richtig zeigte.
        var built = LayerComposer.Compose(layers, sources, number: SequenceLink.NumberOf(path) ?? 0) ?? plain(path);
        if (built is null) return (null, Overlays.None);

        return (built, Grading.Overlays.Prepare(layers, sources, built.Width, built.Height));
    }

    /// <summary>
    /// Liest, was eine Ebene verlangt - einen Pass aus dieser Datei oder eine
    /// andere Datei ganz.
    /// </summary>
    public static FloatFrame? Read(LayerRead read, string framePath, Func<string, FloatFrame?>? plain = null)
    {
        plain ??= Plain;

        if (read.Kind == LayerContent.Image)
        {
            // Eine Bildebene laeuft mit der Nummer mit: Bei Bild 47 meint sie auch
            // dort Bild 47.
            string paired = SequenceLink.Pair(read.Key, framePath, read.FollowSequence);
            return Plain(paired);
        }

        return read.Key.Length == 0 ? plain(framePath) : FloatFrame.FromExrPass(framePath, read.Key);
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
