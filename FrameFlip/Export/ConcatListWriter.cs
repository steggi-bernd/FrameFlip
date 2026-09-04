using System.Globalization;
using System.IO;
using System.Text;
using FrameFlip.Sequencing;

namespace FrameFlip.Export;

/// <summary>Wie mit fehlenden Frames innerhalb des Exportbereichs umgegangen wird.</summary>
public enum GapHandling
{
    /// <summary>Luecke ueberspringen. Die Bewegung springt, das Video wird kuerzer.</summary>
    Skip,

    /// <summary>
    /// Den letzten vorhandenen Frame stehen lassen. Ergibt ein kurzes Standbild
    /// statt eines Zeitsprungs - zum Beurteilen von Bewegung meist das bessere Bild.
    /// </summary>
    HoldLast,
}

/// <summary>
/// Schreibt die Eingabeliste fuer ffmpegs concat-Demuxer.
///
/// FrameFlip benutzt die Liste IMMER, nicht nur bei Luecken. Der naheliegende Weg
/// ueber -i "f_%04d.png" hat zwei Bruchstellen, die bei Renderausgaben beide
/// regelmaessig auftreten: er bricht bei der ersten fehlenden Nummer ab, und er
/// versteht keinen Padding-Ueberlauf - nach f_99 sucht er f_00100 statt f_100.
/// Eine explizite Liste kennt diese Faelle nicht.
/// </summary>
public static class ConcatListWriter
{
    /// <summary>
    /// Baut den Inhalt der Liste. Getrennt vom Schreiben, damit das Ergebnis ohne
    /// Dateisystem geprueft werden kann.
    /// </summary>
    public static string Build(IReadOnlyList<SequenceFrame> frames, double fps, GapHandling gaps)
    {
        if (frames.Count == 0) return "ffconcat version 1.0\n";

        var duration = (1.0 / (fps > 0 ? fps : 24.0)).ToString("F8", CultureInfo.InvariantCulture);
        var text = new StringBuilder("ffconcat version 1.0\n");

        for (int i = 0; i < frames.Count; i++)
        {
            if (gaps == GapHandling.HoldLast && i > 0)
            {
                // Fehlende Nummern durch den vorhergehenden Frame ersetzen, damit die
                // Zeitachse stimmt und die Bewegung nicht springt.
                int missing = frames[i].Number - frames[i - 1].Number - 1;
                for (int k = 0; k < missing; k++) Append(text, frames[i - 1].Path, duration);
            }

            Append(text, frames[i].Path, duration);
        }

        // Der letzte Dateiname steht bewusst NICHT ein zweites Mal in der Liste.
        //
        // Die verbreitete Empfehlung, ihn zu wiederholen, stammt aus einer Zeit, in
        // der concat die duration des letzten Eintrags verworfen hat. Aktuelles ffmpeg
        // wertet sie aus: nachgemessen mit 9.0.1 ergeben 16 Frames mit Wiederholung
        // 17 Bilder (0,708 s statt 0,667 s bei 24 fps), ohne Wiederholung exakt 16.
        // Die Wiederholung erzeugt heute also genau den Fehler, den sie einmal
        // verhindert hat.

        return text.ToString();
    }

    public static string Write(IReadOnlyList<SequenceFrame> frames, double fps,
                              GapHandling gaps, string listPath)
    {
        // Ohne BOM: ffmpeg wertet die erste Zeile sonst nicht als ffconcat-Kennung.
        File.WriteAllText(listPath, Build(frames, fps, gaps), new UTF8Encoding(false));
        return listPath;
    }

    private static void Append(StringBuilder text, string path, string duration)
    {
        text.Append("file '").Append(Escape(path)).Append("'\n");
        text.Append("duration ").Append(duration).Append('\n');
    }

    /// <summary>
    /// Einfache Anfuehrungszeichen im Pfad beenden sonst den Dateinamen mitten
    /// darin. Unter Windows selten, aber moeglich - und dann unerklaerlich.
    /// </summary>
    private static string Escape(string path)
        => path.Replace('\\', '/').Replace("'", @"'\''");
}
