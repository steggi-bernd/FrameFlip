using System.Buffers.Binary;
using System.IO;

namespace FrameFlip.Remote;

/// <summary>Ob ein Film losspielen kann, bevor er ganz da ist.</summary>
public enum MovieStart
{
    /// <summary>Laesst sich nicht sagen - kein MP4/MOV, oder nicht lesbar.</summary>
    Unknown,

    /// <summary>Das Inhaltsverzeichnis steht vorne. Der Film laeuft, waehrend er kommt.</summary>
    Front,

    /// <summary>Es steht hinten. Ohne das letzte Byte faengt nichts an.</summary>
    Back,
}

/// <summary>
/// Ein Blick in den Kopf einer Filmdatei.
///
/// Ein MP4 besteht aus Kaesten: "moov" ist das Inhaltsverzeichnis, "mdat" sind die
/// Bilder. In welcher Reihenfolge die beiden liegen, entscheidet alles ueber das
/// Ansehen aus der Ferne. Steht moov vorne, kann ein Spieler nach den ersten
/// Kilobyte anfangen. Steht es hinten - und dorthin schreibt es jeder Encoder, der
/// nicht ausdruecklich "faststart" bekommt, Blenders FFMPEG-Ausgabe eingeschlossen -,
/// dann braucht der Spieler das Ende, bevor er den Anfang zeigen kann.
///
/// Das ist eine Eigenschaft der Datei und nichts, was sich am Handy beheben laesst.
/// Genau deshalb wird sie hier gelesen und mitgeschickt: Ein Film, der erst bei 100 %
/// anfaengt, sieht sonst aus wie eine kaputte Verbindung. Mit der Antwort steht auf
/// dem Bildschirm, was los ist, bevor jemand wartet.
///
/// Gelesen wird dabei fast nichts - nur die Koepfe der obersten Kaesten, acht Bytes
/// je Sprung. Eine vier Gigabyte grosse Datei kostet ein paar Suchvorgaenge.
/// </summary>
public static class MovieProbe
{
    /// <summary>Soviele Kaesten weit wird gesucht. Danach ist es keine gewoehnliche Datei mehr.</summary>
    private const int MaxBoxes = 64;

    /// <summary>Kleinster gueltiger Kasten: vier Bytes Groesse, vier Bytes Name.</summary>
    private const int Header = 8;

    public static MovieStart Of(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return MovieStart.Unknown;

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                            FileShare.ReadWrite | FileShare.Delete);

            return Read(file);
        }
        catch (Exception)
        {
            // Eine Datei, die sich nicht lesen laesst, ist keine Aussage wert.
            return MovieStart.Unknown;
        }
    }

    /// <summary>Getrennt, damit sich das Lesen ohne eine Datei auf der Platte pruefen laesst.</summary>
    public static MovieStart Read(Stream stream)
    {
        byte[] header = new byte[16];
        long at = 0;
        long length = stream.Length;

        for (int box = 0; box < MaxBoxes && at + Header <= length; box++)
        {
            stream.Position = at;

            if (!Fill(stream, header, Header)) return MovieStart.Unknown;

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string name = Name(header.AsSpan(4, 4));

            if (name.Length == 0) return MovieStart.Unknown;

            // Der erste Kasten sagt, ob das ueberhaupt ein MP4 ist. Ein MKV faengt
            // mit ganz anderen Bytes an, und darueber zu raten waere schlechter als
            // zuzugeben, dass man es nicht weiss.
            if (box == 0 && name is not ("ftyp" or "moov" or "mdat" or "free" or "skip" or "wide" or "pnot"))
                return MovieStart.Unknown;

            // Die Antwort steht schon fest, sobald einer der beiden auftaucht.
            if (name == "moov") return MovieStart.Front;
            if (name == "mdat") return MovieStart.Back;

            if (size == 1)
            {
                // Ein Kasten ueber vier Gigabyte traegt seine Groesse in acht Bytes
                // dahinter. Bei Renderausgaben ist das der Normalfall, nicht die
                // Ausnahme.
                stream.Position = at + Header;

                if (!Fill(stream, header, 8)) return MovieStart.Unknown;

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header);
            }
            else if (size == 0)
            {
                // Null heisst "bis zum Ende". Dahinter kommt nichts mehr, was noch
                // zu finden waere.
                return MovieStart.Unknown;
            }

            // Eine Groesse, die nicht vorwaerts fuehrt, ist der Weg in eine
            // Endlosschleife. Lieber keine Aussage.
            if (size < Header) return MovieStart.Unknown;

            at += size;
        }

        return MovieStart.Unknown;
    }

    /// <summary>Der Name eines Kastens - leer, wenn es keiner sein kann.</summary>
    private static string Name(ReadOnlySpan<byte> raw)
    {
        foreach (byte letter in raw)
            if (letter < 0x20 || letter > 0x7E)
                return string.Empty;

        return System.Text.Encoding.ASCII.GetString(raw);
    }

    /// <summary>Genau soviele Bytes lesen - ein kurzer Rest ist ein Nein, kein Teilerfolg.</summary>
    private static bool Fill(Stream stream, byte[] buffer, int count)
    {
        int filled = 0;

        while (filled < count)
        {
            int read = stream.Read(buffer, filled, count - filled);

            if (read <= 0) return false;

            filled += read;
        }

        return true;
    }

    /// <summary>Wie es auf der Leitung heisst.</summary>
    public static string Word(MovieStart start) => start switch
    {
        MovieStart.Front => "fast",
        MovieStart.Back => "slow",
        _ => "unknown",
    };
}
