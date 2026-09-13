using FrameFlip.Remote;

namespace FrameFlip.Tests;

/// <summary>
/// Zusicherungen fuer die Ausgangsbremse.
///
/// Der Sinn dieser Datei ist eine Zahl, die woanders steht: Der Relay trennt eine
/// Verbindung, die mehr als 64 Nachrichten oder 4 MiB je Sekunde schickt - ohne
/// Vorwarnung, mitten im Satz. Gemessen wurde das an einer laufenden Installation:
/// Eine Dateiuebertragung fiel nach 10,5 MiB und gut einer Sekunde.
///
/// Hier wird deshalb nicht geprueft, ob die Bremse "funktioniert", sondern ob das,
/// was FrameFlip tatsaechlich sendet, unter jenen Grenzen bleibt. Die Rechnung ist
/// rein und braucht keine Uhr - <see cref="OutboundPace.Reserve"/> sagt nur, wie
/// lange zu warten waere, und wartet nicht selbst.
/// </summary>
public static class PaceInvariants
{
    /// <summary>Was der Relay durchlaesst. Mehr trennt er.</summary>
    private const double RelayMessagesPerSecond = 64;
    private const double RelayBytesPerSecond = 4 * 1024 * 1024;

    /// <summary>Ein Dateistueck, wie FrameFlip es schickt - samt Umschlag und Siegel.</summary>
    private static readonly int ChunkFrame = Envelope.ChunkBytes + SecureChannel.Overhead + 16;

    public static void Run()
    {
        Vorrat();
        Dauerlast();
        GrosseNachricht();
        RueckwaertsUhr();
    }

    private static void Vorrat()
    {
        Check.Group("Ausgangsbremse - der Vorrat traegt den Anfang");

        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pace = new OutboundPace(start);

        // Eine Zustandsmeldung sind ein paar hundert Byte. Die darf nie anstehen.
        Check.That(pace.Reserve(400, start) == TimeSpan.Zero,
            "eine kleine Meldung geht ohne Pause hinaus");

        var pace2 = new OutboundPace(start);
        int ohnePause = 0;

        for (int i = 0; i < 40; i++)
        {
            if (pace2.Reserve(ChunkFrame, start) != TimeSpan.Zero) break;
            ohnePause++;
        }

        // Der Vorrat sind 2 MiB, ein Stueck gut 128 KiB: etwa sechzehn.
        Check.That(ohnePause >= 12 && ohnePause <= 20,
            $"aus dem Stand gehen {ohnePause} Dateistuecke ohne Pause hinaus", ohnePause.ToString());
    }

    private static void Dauerlast()
    {
        Check.Group("Ausgangsbremse - Dauerlast bleibt unter den Grenzen des Relays");

        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pace = new OutboundPace(start);

        var now = start;
        long bytes = 0;
        int messages = 0;

        /* Nachgestellt wird der Dateiweg in seinem schnellsten Fall: Er schiebt
           so zuegig nach, wie die Bremse es zulaesst. Genau dieser Fall hat die
           Verbindung auf dem Server gerissen. */
        for (int i = 0; i < 400; i++)
        {
            now += pace.Reserve(ChunkFrame, now);

            bytes += ChunkFrame;
            messages++;
        }

        double seconds = (now - start).TotalSeconds;
        double bytesPerSecond = bytes / seconds;
        double messagesPerSecond = messages / seconds;

        Check.That(bytesPerSecond < RelayBytesPerSecond,
            $"{bytesPerSecond / 1048576:0.0} MiB/s bleiben unter den erlaubten 4",
            $"{bytesPerSecond:0} statt < {RelayBytesPerSecond:0}");

        Check.That(messagesPerSecond < RelayMessagesPerSecond,
            $"{messagesPerSecond:0} Nachrichten/s bleiben unter den erlaubten 64",
            $"{messagesPerSecond:0.0}");

        // Und nicht so streng, dass eine Datei unzumutbar lange braeuchte: ueber
        // 2 MiB/s ist mehr, als ein ueblicher Heimanschluss hinaufschickt.
        Check.That(bytesPerSecond > 2 * 1024 * 1024,
            $"und ueber 2 MiB/s - eine Datei soll nicht kriechen",
            $"{bytesPerSecond / 1048576:0.00} MiB/s");
    }

    private static void GrosseNachricht()
    {
        Check.Group("Ausgangsbremse - eine Nachricht groesser als der Vorrat");

        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pace = new OutboundPace(start);

        // Der Relay laesst bis zu 1 MiB je Nachricht durch; das passt in den Vorrat.
        Check.That(pace.Reserve(1024 * 1024, start) == TimeSpan.Zero,
            "die groesste erlaubte Nachricht geht ohne Pause");

        // Und die zweite auch noch - zusammen sind es genau die 2 MiB des Vorrats.
        Check.That(pace.Reserve(1024 * 1024, start) == TimeSpan.Zero,
            "und die zweite unmittelbar danach ebenfalls");

        // Erst die dritte muss warten, denn jetzt ist er leer.
        var pause = pace.Reserve(1024 * 1024, start);

        Check.That(pause > TimeSpan.Zero, "die dritte wartet");
        Check.That(pause <= TimeSpan.FromSeconds(5),
            "aber hoechstens fuenf Sekunden - eine Pause ist kein Stillstand",
            pause.ToString());
    }

    private static void RueckwaertsUhr()
    {
        Check.Group("Ausgangsbremse - eine Uhr, die zurueckspringt");

        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pace = new OutboundPace(start);

        // Vorrat aufbrauchen.
        for (int i = 0; i < 40; i++) pace.Reserve(ChunkFrame, start);

        // Zeitumstellung oder Winterschlaf: Die Uhr geht eine Stunde zurueck. Daraus
        // darf kein negativer Nachschub werden - sonst waere die Bremse danach
        // strenger als gedacht oder gar blockiert.
        var pause = pace.Reserve(ChunkFrame, start - TimeSpan.FromHours(1));

        Check.That(pause > TimeSpan.Zero && pause <= TimeSpan.FromSeconds(5),
            "sie bremst weiter, aber sie klemmt nicht", pause.ToString());
    }
}
