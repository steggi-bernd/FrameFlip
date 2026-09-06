using System.IO;
using System.Windows.Media.Imaging;
using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Der Umschlag um jede Nachricht und das Bild darin.
///
/// Der Umschlag traegt den Typ als eigenes Byte, statt ihn am Inhalt zu erraten.
/// Beim Relay war genau das der Fehler, den ein Test gefunden hat - eine Nutzlast,
/// die mit '{' beginnt, wurde fuer Text gehalten. Hier waere es umgekehrt genauso:
/// Ein JPEG faengt mit 0xFF 0xD8 an, aber darauf zu bauen heisst raten.
/// </summary>
public static class PreviewInvariants
{
    public static void Run()
    {
        Check.Group("Umschlag");

        byte[] json = Envelope.Json("""{"t":"idle"}""");

        Check.That(json[0] == (byte)PayloadKind.Json, "JSON traegt seinen Typ");
        Check.That(Envelope.TryRead(json, out PayloadKind kind, out byte[] body)
                   && kind == PayloadKind.Json
                   && System.Text.Encoding.UTF8.GetString(body) == """{"t":"idle"}""",
                   "JSON kommt unveraendert zurueck");

        byte[] fake = { 0xFF, 0xD8, 0x00, 0x7B, 0xAB };
        byte[] wrapped = Envelope.Preview(412, fake);

        Check.That(Envelope.TryRead(wrapped, out kind, out byte[] previewBody)
                   && kind == PayloadKind.Preview,
                   "ein Bild traegt seinen Typ");

        Check.That(previewBody.Length == 4 + fake.Length, "Framenummer plus Bild",
                   previewBody.Length.ToString());

        int frame = (previewBody[0] << 24) | (previewBody[1] << 16) | (previewBody[2] << 8) | previewBody[3];
        Check.That(frame == 412, "die Framenummer ueberlebt", frame.ToString());
        Check.That(previewBody[4..].SequenceEqual(fake), "und das Bild auch");

        // Der Fall, an dem eine Erkennung am Inhalt scheitern wuerde: ein "Bild",
        // das mit '{' anfaengt. Der Typ steht davor, also ist es egal.
        byte[] tricky = Envelope.Preview(1, "{\"nicht\":\"json\"}"u8.ToArray());

        Check.That(Envelope.TryRead(tricky, out kind, out _) && kind == PayloadKind.Preview,
                   "ein Bild, das mit { beginnt, bleibt ein Bild");

        // Unbekanntes wird verworfen, nicht geworfen - eine spaetere Fassung darf
        // Typen hinzufuegen, ohne diese Seite zu brechen.
        Check.That(!Envelope.TryRead(new byte[] { 0x7F, 1, 2 }, out _, out _), "unbekannter Typ wird verworfen");
        Check.That(!Envelope.TryRead(Array.Empty<byte>(), out _, out _), "nichts wird verworfen");

        Check.Group("Vorschaubild");

        string? source = FindTestFrame();

        if (source is null)
        {
            Check.That(false, "ein Testbild wurde gefunden");
            return;
        }

        var original = BitmapFrame.Create(new Uri(source), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Check.That(original.PixelWidth == 1920, "das Testbild ist 1920 breit", original.PixelWidth.ToString());

        byte[]? jpeg = PreviewEncoder.Encode(source);

        Check.That(jpeg is not null, "das Bild laesst sich kodieren");

        if (jpeg is null) return;

        Check.That(jpeg[0] == 0xFF && jpeg[1] == 0xD8, "es ist wirklich ein JPEG");
        Check.That(jpeg.Length <= PreviewEncoder.MaxBytes, "und unter der Groessengrenze",
                   $"{jpeg.Length / 1024} KB");

        // Der Punkt der ganzen Uebung: Was am Handy ankommt, ist ein Bruchteil des
        // Originals. Sonst waere jede Vorschau ein Griff ins Datenvolumen.
        long originalSize = new FileInfo(source).Length;

        Check.That(jpeg.Length < originalSize / 2,
                   "deutlich kleiner als das Original",
                   $"{jpeg.Length / 1024} KB statt {originalSize / 1024} KB");

        var decoded = BitmapFrame.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        Check.That(decoded.PixelWidth == PreviewEncoder.Width,
                   "auf Handygroesse verkleinert", decoded.PixelWidth.ToString());

        // Das Seitenverhaeltnis muss stehen bleiben - ein verzerrtes Bild waere
        // schlimmer als keins.
        double before = original.PixelWidth / (double)original.PixelHeight;
        double after = decoded.PixelWidth / (double)decoded.PixelHeight;

        Check.Near(after, before, 0.01, "das Seitenverhaeltnis bleibt");

        Check.Group("Vorschaubild - was nicht geht");

        Check.That(PreviewEncoder.Encode(null) is null, "ohne Pfad kein Bild");
        Check.That(PreviewEncoder.Encode("") is null, "leerer Pfad ergibt nichts");
        Check.That(PreviewEncoder.Encode(@"C:\gibt\es\nicht.png") is null, "fehlende Datei ergibt nichts");

        // Eine Datei, die kein Bild ist - etwa ein halb geschriebener Frame.
        string junk = Path.Combine(Path.GetTempPath(), "frameflip-kein-bild.png");
        File.WriteAllText(junk, "das ist kein PNG");

        try
        {
            Check.That(PreviewEncoder.Encode(junk) is null, "Unsinn ergibt nichts, statt zu werfen");
        }
        finally
        {
            try { File.Delete(junk); } catch (IOException) { }
        }
    }

    /// <summary>Sucht die Testsequenz vom Ausfuehrungsverzeichnis aus aufwaerts.</summary>
    private static string? FindTestFrame()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName,
                                            "FrameFlip-Testsequenzen", "lueckenlos_1920x1080", "f_0001.png");

            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
