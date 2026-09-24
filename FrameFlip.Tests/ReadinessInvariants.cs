using FrameFlip.Setup;

namespace FrameFlip.Tests;

/// <summary>
/// Was FrameFlip beim ersten Start ueber sich selbst sagt.
///
/// Die Matrix ist groesser, als sie aussieht: drei Themen, und die Bruecke allein hat
/// vier Lagen, die von aussen ununterscheidbar sind - es meldet sich nichts. Genau
/// deshalb steht der Entscheider in einer Klasse ohne WPF; hier laesst er sich
/// vollstaendig durchgehen, statt ihn im Fenster stichprobenartig anzusehen.
///
/// Zwei Zusicherungen tragen den ganzen Entwurf, und sie stehen am Anfang: Ist alles
/// bereit, ist die Liste LEER - die Leiste verschwindet von selbst. Und was jemand
/// ausdruecklich abgeschaltet hat, wird nicht angemahnt.
/// </summary>
public static class ReadinessInvariants
{
    /// <summary>Der eingeschwungene Normalfall: alles da, nichts abgeschaltet.</summary>
    private static ReadinessInput Alles() => new(
        FfmpegFound: true,
        WingetAvailable: true,
        BridgeEnabled: true,
        BridgeListening: true,
        BridgeSeenEver: true,
        RelayWanted: false,
        RelayConnected: false);

    private static bool Hat(ReadinessInput input, ReadyTopic topic)
        => Readiness.Check(input).Any(r => r.Topic == topic);

    private static ReadyItem Zeile(ReadinessInput input, ReadyTopic topic)
        => Readiness.Check(input).First(r => r.Topic == topic);

    public static void Run()
    {
        Check.Group("Bereitschaft - ist alles da, sagt die Anzeige nichts");

        Check.That(Readiness.Check(Alles()).Count == 0,
            "bei vollstaendiger Bereitschaft bleibt die Liste leer");

        Check.That(Readiness.Check(Alles() with { RelayWanted = true, RelayConnected = true }).Count == 0,
            "auch mit stehender Relay-Verbindung");

        // ------------------------------------------------------------------ ffmpeg

        Check.Group("Bereitschaft - ffmpeg");

        var ohneFfmpeg = Alles() with { FfmpegFound = false };

        Check.That(Hat(ohneFfmpeg, ReadyTopic.Ffmpeg),
            "fehlt ffmpeg, erscheint eine Zeile");

        Check.That(Zeile(ohneFfmpeg, ReadyTopic.Ffmpeg).Action == ReadyAction.InstallFfmpeg,
            "mit winget wird das Installieren angeboten");

        var ohneWinget = ohneFfmpeg with { WingetAvailable = false };

        Check.That(Zeile(ohneWinget, ReadyTopic.Ffmpeg).Action == ReadyAction.None,
            "ohne winget wird nichts angeboten, das nicht geht");

        Check.That(Zeile(ohneWinget, ReadyTopic.Ffmpeg).TextKey != Zeile(ohneFfmpeg, ReadyTopic.Ffmpeg).TextKey,
            "und der Text ist ein anderer - sonst stuende da eine Anleitung ins Leere");

        // Es gibt keinen Schalter, mit dem sich jemand den Export abwaehlt. Die Zeile
        // darf deshalb an keiner anderen Einstellung haengen.
        Check.That(Hat(ohneFfmpeg with { BridgeEnabled = false }, ReadyTopic.Ffmpeg),
            "die ffmpeg-Zeile haengt nicht an der Bruecke");

        // ------------------------------------------------------------------ Bruecke

        Check.Group("Bereitschaft - die vier Lagen der Bruecke");

        Check.That(!Hat(Alles() with { BridgeEnabled = false, BridgeSeenEver = false }, ReadyTopic.Bridge),
            "abgeschaltet: kein Wort, auch wenn nie ein Addon da war");

        var neuling = Alles() with { BridgeSeenEver = false };

        Check.That(Zeile(neuling, ReadyTopic.Bridge).Action == ReadyAction.GetBridge,
            "nie gehoert: der Weg zum Addon");

        Check.That(!Hat(Alles(), ReadyTopic.Bridge),
            "frueher gehoert, gerade still: kein Wort - Blender ist nur zu");

        var belegt = Alles() with { BridgeListening = false };

        Check.That(Zeile(belegt, ReadyTopic.Bridge).Action == ReadyAction.OpenSettings,
            "Port belegt: in die Einstellungen, nicht zum Addon");

        Check.That(Zeile(belegt, ReadyTopic.Bridge).TextKey != Zeile(neuling, ReadyTopic.Bridge).TextKey,
            "und mit eigenem Text - ein belegter Port ist keine fehlende Einrichtung");

        // Der Fall, der ohne den gespeicherten Zeitpunkt gar nicht zu treffen waere:
        // eingerichtet, Port offen, Blender zu. Ohne BridgeLastSeen saehe das aus wie
        // ein Neuling, und die Einrichtungsanleitung erschiene jedes Mal neu.
        Check.That(!Hat(Alles() with { BridgeListening = true, BridgeSeenEver = true }, ReadyTopic.Bridge),
            "der gespeicherte Zeitpunkt haelt die Anleitung von Fortgeschrittenen fern");

        // ------------------------------------------------------------------- Relay

        Check.Group("Bereitschaft - der Relay draengt sich nicht auf");

        Check.That(!Hat(Alles() with { RelayWanted = false, RelayConnected = false }, ReadyTopic.Relay),
            "will ihn niemand, steht auch nichts da");

        Check.That(Hat(Alles() with { RelayWanted = true, RelayConnected = false }, ReadyTopic.Relay),
            "ist er eingeschaltet und nicht verbunden, wartet jemand vergeblich");

        Check.That(!Hat(Alles() with { RelayWanted = true, RelayConnected = true }, ReadyTopic.Relay),
            "steht die Verbindung, ist nichts zu melden");

        // ------------------------------------------------------------- Alles zugleich

        Check.Group("Bereitschaft - ein frisch installiertes FrameFlip");

        var frisch = new ReadinessInput(
            FfmpegFound: false, WingetAvailable: true,
            BridgeEnabled: true, BridgeListening: true, BridgeSeenEver: false,
            RelayWanted: false, RelayConnected: false);

        var zeilen = Readiness.Check(frisch);

        Check.That(zeilen.Count == 2,
            $"genau zwei Zeilen: ffmpeg und Bruecke ({zeilen.Count})");

        Check.That(zeilen.All(r => r.Topic != ReadyTopic.Relay),
            "und kein Wort ueber einen Relay, dem niemand zugestimmt hat");

        Check.That(zeilen.Select(r => r.Topic).Distinct().Count() == zeilen.Count,
            "kein Thema doppelt");

        Check.That(zeilen.All(r => !string.IsNullOrWhiteSpace(r.TextKey)),
            "jede Zeile bringt ihren Text mit");
    }
}
