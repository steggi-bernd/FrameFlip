namespace FrameFlip.Setup;

/// <summary>Worum es bei einer Zeile geht.</summary>
public enum ReadyTopic
{
    /// <summary>ffmpeg fuer den Videoexport.</summary>
    Ffmpeg,

    /// <summary>Das Blender-Addon.</summary>
    Bridge,

    /// <summary>Die Verbindung zum Relay.</summary>
    Relay,
}

/// <summary>Was die Zeile anbietet. Genau eine Handlung je Zeile - oder keine.</summary>
public enum ReadyAction
{
    /// <summary>Nichts zu tun.</summary>
    None,

    /// <summary>ffmpeg ueber winget installieren lassen.</summary>
    InstallFfmpeg,

    /// <summary>Die Anleitung zum Addon oeffnen.</summary>
    GetBridge,

    /// <summary>In die Einstellungen wechseln.</summary>
    OpenSettings,
}

/// <summary>Eine Zeile der Bereitschaftsanzeige.</summary>
/// <param name="Topic">Worum es geht.</param>
/// <param name="TextKey">Ressourcenschluessel des Erklaertextes.</param>
/// <param name="Action">Die eine angebotene Handlung.</param>
public readonly record struct ReadyItem(ReadyTopic Topic, string TextKey, ReadyAction Action);

/// <summary>Alles, woraus sich die Anzeige ergibt. Bewusst nur Werte, keine Dienste.</summary>
/// <param name="FfmpegFound">Wurde ffmpeg gefunden?</param>
/// <param name="WingetAvailable">Steht winget zur Verfuegung?</param>
/// <param name="BridgeEnabled">Ist die Bruecke eingeschaltet?</param>
/// <param name="BridgeListening">Lauscht der eigene Port wirklich?</param>
/// <param name="BridgeSeenEver">Hat sich das Addon jemals gemeldet?</param>
/// <param name="RelayWanted">Will jemand Handy oder Zuschauerseite?</param>
/// <param name="RelayConnected">Steht die Verbindung?</param>
public readonly record struct ReadinessInput(
    bool FfmpegFound,
    bool WingetAvailable,
    bool BridgeEnabled,
    bool BridgeListening,
    bool BridgeSeenEver,
    bool RelayWanted,
    bool RelayConnected);

/// <summary>
/// Was FrameFlip noch fehlt, damit es das kann, wofuer man es geholt hat.
///
/// Der Anlass ist der erste Start. Wer FrameFlip heute zum ersten Mal oeffnet, findet
/// einen Exportknopf, der wortlos nichts tut, und im Metrikbereich den Satz, dass ein
/// Render "gleich hier erscheint" - obwohl ohne Addon nie einer erscheinen wird. Beides
/// ist nicht falsch programmiert, es ist nur nirgends gesagt.
///
/// Die Klasse kennt kein WPF und keine Dienste, nur Werte. Das ist keine Formsache: Die
/// Matrix hat mehr Faelle, als man beim Hinsehen glaubt, und eine Matrix, die sich nur
/// mit einem Fenster pruefen laesst, wird nie vollstaendig geprueft.
///
/// ZWEI REGELN, die wichtiger sind als die Liste selbst:
///
/// Erstens: Eine Zeile erscheint nur, wenn etwas fehlt, das der Benutzer nicht selbst
/// abgewaehlt hat. Wer die Bruecke ausschaltet, hat sich dabei etwas gedacht und
/// braucht keine Erinnerung daran.
///
/// Zweitens: Die Anzeige macht sich selbst ueberfluessig. Ist alles bereit, ist die
/// Liste leer und die Leiste verschwindet - ohne Wegklicken, ohne "nicht mehr
/// anzeigen". Eine Anzeige, die man abstellen muss, war eine Zumutung.
/// </summary>
public static class Readiness
{
    /// <summary>Die Zeilen, die angezeigt werden sollen. Leer heisst: alles bereit.</summary>
    public static IReadOnlyList<ReadyItem> Check(ReadinessInput now)
    {
        var rows = new List<ReadyItem>(3);

        /* ffmpeg: fehlt oder fehlt nicht.
         *
         * Ohne ffmpeg ist der Videoexport tot, und das ist eine der beiden Sachen, fuer
         * die man FrameFlip holt. Die Zeile steht deshalb ohne Wenn und Aber - es gibt
         * keinen Schalter, mit dem sich jemand den Export abgewaehlt haette. */
        if (!now.FfmpegFound)
        {
            rows.Add(new ReadyItem(
                ReadyTopic.Ffmpeg,
                now.WingetAvailable ? "S_ReadyFfmpegMissing" : "S_ReadyFfmpegNoWinget",
                now.WingetAvailable ? ReadyAction.InstallFfmpeg : ReadyAction.None));
        }

        /* Die Bruecke hat drei Lagen, die von aussen gleich aussehen - es meldet sich
         * nichts - und drei voellig verschiedene Antworten verlangen.
         *
         * Abgeschaltet: kein Wort. Das war eine Entscheidung.
         *
         * Eingeschaltet, aber der Port lauscht nicht: Das ist ein Fehler, nicht eine
         * fehlende Einrichtung. Meist haelt ein anderes Programm den Port. Das Addon zu
         * holen wuerde nichts bessern, also fuehrt die Zeile in die Einstellungen.
         *
         * Eingeschaltet, Port offen, nie ein Addon gehoert: DAS ist der Neuling. Nur
         * hier gehoert die Anleitung hin.
         *
         * Eingeschaltet, Port offen, frueher schon gehoert: Blender ist gerade zu. Kein
         * Wort - sonst erschiene die Aufforderung jedes Mal, wenn jemand Blender
         * schliesst, und zwar fuer immer. */
        if (now.BridgeEnabled)
        {
            if (!now.BridgeListening)
                rows.Add(new ReadyItem(ReadyTopic.Bridge, "S_ReadyBridgeBlocked", ReadyAction.OpenSettings));
            else if (!now.BridgeSeenEver)
                rows.Add(new ReadyItem(ReadyTopic.Bridge, "S_ReadyBridgeNever", ReadyAction.GetBridge));
        }

        /* Der Relay taucht nur auf, wenn ihn jemand WILL.
         *
         * Hier weiche ich von meinem eigenen Entwurf ab, in dem eine Relayzeile fest
         * vorgesehen war. Beim Schreiben faellt auf, dass sie dort falsch waere: Handy
         * und Zuschauerseite sind ab Werk aus und verlangen eine ausdrueckliche
         * Zustimmung. Einem Neuling zu melden, dass eine Verbindung fehlt, die er nie
         * wollte und der er nie zugestimmt hat, waere genau das Draengen, das diese
         * Anzeige vermeiden soll.
         *
         * Ist dagegen einer der beiden Schalter an und die Verbindung steht trotzdem
         * nicht, dann wartet jemand vergeblich aufs Handy - und das gehoert gesagt. */
        if (now.RelayWanted && !now.RelayConnected)
            rows.Add(new ReadyItem(ReadyTopic.Relay, "S_ReadyRelayDown", ReadyAction.OpenSettings));

        return rows;
    }
}
