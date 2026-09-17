using System.Text.Json.Serialization;

namespace FrameFlip.Configuration;

/// <summary>Persistierter Zustand. Liegt als JSON in %APPDATA%\FrameFlip\config.json.</summary>
public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Wiedergabegeschwindigkeit. Wird beim Umstellen im Overlay persistiert.</summary>
    public double Fps { get; set; } = 24.0;

    public bool Loop { get; set; } = true;

    /// <summary>
    /// Bei Sollraten dicht am Bildschirmtakt je Zeichenschritt genau ein Bild
    /// weiterschalten, statt die Position aus der Uhr zu rechnen.
    ///
    /// Gemessen an 60 fps auf einem 60-Hz-Schirm: zeitbasiert kamen 40 von 60 Bildern
    /// an, bei 15 Spruengen je Sekunde - es gibt dort keine Reserve, jeder ausgelassene
    /// Kompositionsschritt verschluckt ein Bild. Gekoppelt sind es 59 Bilder ohne
    /// Sprung. Der Preis ist der Unterschied zwischen der Sollrate und dem echten
    /// Schirmtakt, also etwa ein Promille. Wer die Zeitachse exakt braucht, schaltet
    /// es ab.
    /// </summary>
    public bool LockToDisplay { get; set; } = true;

    /// <summary>Aufloesung, Farbtiefe und Dateigroesse in der Kopfleiste anzeigen.</summary>
    public bool ShowMetadata { get; set; } = true;

    /// <summary>
    /// Schliesst die Vorschau, sobald sie den Fokus verliert - das QuickLook-Verhalten.
    /// Wer nebenher in Blender arbeitet und die Sequenz stehen lassen will, schaltet
    /// es ab.
    /// </summary>
    public bool CloseOnFocusLoss { get; set; } = true;

    /// <summary>
    /// Obergrenze fuer dekodierte Frames im Ringpuffer.
    ///
    /// 1 GB statt der frueheren 512 MB: bei 1080p belegt ein Frame 7,9 MB, aus
    /// 512 MB werden also nur 64 Frames - bei 24 fps ganze 2,7 Sekunden Vorrat.
    /// Stockt der Decoder auch nur kurz, laeuft der Ring leer und die Wiedergabe
    /// setzt zum Nachpuffern aus. Der Speicher wird ohnehin nur belegt, solange eine
    /// Vorschau offen ist.
    /// </summary>
    public int MemoryBudgetMb { get; set; } = 1024;

    /// <summary>
    /// Ob Abspielen die Folge erst vollstaendig in den Speicher liest.
    ///
    /// Voreingestellt an: Eine Folge von der Platte abzuspielen ruckelt genau dann am
    /// staerksten, wenn nebenan ein Render laeuft - also in dem Fall, fuer den es
    /// FrameFlip gibt.
    /// </summary>
    public bool Prebuffer { get; set; } = true;

    /// <summary>
    /// Ob beim Vorausladen nebenher schon ein Video entsteht.
    ///
    /// Die Bilder werden dabei ohnehin alle gelesen; sie im selben Zug an ffmpeg zu
    /// reichen kostet wenig zusaetzlich und erspart beim spaeteren Export das ganze
    /// Kodieren. Die fertige Datei liegt im Temp-Ordner und wird nur dann an ihren
    /// Platz gebracht, wenn wirklich exportiert wird - wer nie exportiert, hat nur
    /// eine Datei im Temp-Ordner, die beim naechsten Start aufgeraeumt wird.
    /// </summary>
    public bool PrepareVideo { get; set; }

    /// <summary>Zuletzt gewaehlte Qualitaetsstufe im Exportdialog.</summary>
    public string ExportQuality { get; set; } = "Hoch";

    /// <summary>Zuletzt gewaehltes Encoder-Tempo im Exportdialog.</summary>
    public string ExportSpeed { get; set; } = "Ausgewogen";

    /// <summary>
    /// Was diese Maschine beim letzten Export geschafft hat, in Megabildpunkten je
    /// Sekunde.
    ///
    /// Damit wird die Dauerschaetzung mit jedem Export besser. Eine feste Zahl im
    /// Code koennte das nicht: Zwischen einem Notebook und einer Renderkiste liegt
    /// leicht der Faktor zehn.
    /// </summary>
    public double ExportThroughput { get; set; }

    /// <summary>
    /// Wie sich die geschaetzte Groesse beim letzten Export zur tatsaechlichen
    /// verhalten hat. 0 heisst: noch nie gemessen.
    ///
    /// Damit lernt die Schaetzung das Material kennen. Wer immer dieselbe Art Szene
    /// rendert - und das tun die meisten - bekommt nach zwei Exporten eine Zahl, die
    /// stimmt.
    /// </summary>
    public double ExportSizeFactor { get; set; }

    /// <summary>
    /// Dekodiergroesse als Stufe: 0 = voll, 1 = halb, 2 = viertel. Verlaengert den
    /// Puffervorlauf um das Vier- bzw. Sechzehnfache.
    /// </summary>
    public int DraftStep { get; set; }

    /// <summary>Frames, die in Laufrichtung vorgehalten werden.</summary>
    public int PrefetchAhead { get; set; } = 60;

    /// <summary>Frames, die entgegen der Laufrichtung vorgehalten werden.</summary>
    public int PrefetchBehind { get; set; } = 15;

    /// <summary>Passt Threads, Prioritaet und Puffer an die gemessene Systemlast an.</summary>
    public bool AdaptiveResources { get; set; } = true;

    /// <summary>Messtakt der Lasterkennung, solange eine Vorschau offen ist.</summary>
    public int LoadIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Obergrenze fuer Decoder-Threads im Leerlauf. Zusaetzlich auf die Kernzahl
    /// minus zwei begrenzt.
    ///
    /// Der Wert entscheidet, welche Bildrate ueberhaupt erreichbar ist: ein
    /// 1080p-PNG mit 8,5 MB kostet rund 46 ms zum Entpacken, ein Thread schafft
    /// damit etwa 18 Bilder je Sekunde. Fuer 60 fps braucht es vier, mit Reserve
    /// sechs. Der fruehere Standard von vier reichte fuer 24 fps, nicht fuer 60.
    /// </summary>
    public int MaxDecoderThreads { get; set; } = 8;

    /// <summary>Frames, die vor dem Start der Wiedergabe im Ring liegen muessen (0 = automatisch aus der Bildrate).</summary>
    public int WarmupFrames { get; set; } = 0;

    /// <summary>
    /// Pfad zu ffmpeg.exe. Leer heisst: bei jedem Export neu suchen. ffmpeg wird
    /// nicht mitgeliefert, weil uebliche Builds unter der GPL stehen.
    /// </summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>Zuletzt benutztes Ausgabeformat im Exportdialog.</summary>
    public string ExportPreset { get; set; } = "H.264 / MP4";

    /// <summary>Fehlende Frames im Export als Standbild halten statt ueberspringen.</summary>
    public bool ExportHoldLastFrame { get; set; } = true;

    /// <summary>
    /// Zweite Cachestufe: dekodierte Frames als rohe Bloecke auf der Platte ablegen.
    ///
    /// Lohnt sich, sobald eine Sequenz nicht vollstaendig in den Arbeitsspeicher
    /// passt: einen rohen Block zu lesen kostet rund ein Fuenftel dessen, was das
    /// erneute Entpacken des PNG kostet. Auf einer SSD ohne Nachteil, auf einer
    /// mechanischen Platte eher nicht.
    /// </summary>
    public bool RawCacheEnabled { get; set; } = true;

    /// <summary>Obergrenze fuer den Rohcache auf der Platte, in Gigabyte.</summary>
    public int RawCacheMaxGb { get; set; } = 16;

    /// <summary>Seitenpanel mit Bildanpassung beim Oeffnen ausgeklappt.</summary>
    public bool PanelOpen { get; set; }

    /// <summary>
    /// Sprache der Oberflaeche: "de" oder "en".
    ///
    /// Wird zur Laufzeit umgeschaltet, ohne Neustart - die Texte liegen als
    /// ResourceDictionary je Sprache und werden gegeneinander getauscht.
    /// </summary>
    public string Language { get; set; } = "de";

    /// <summary>
    /// Meldungen des Blender-Addons entgegennehmen.
    ///
    /// Der Empfaenger bindet ausschliesslich an 127.0.0.1 und verlangt ein Token aus
    /// dem Benutzerprofil - aus dem Netz ist er nicht erreichbar. Wer trotzdem keinen
    /// offenen Port moechte, schaltet es hier ab; die Vorschau selbst braucht ihn nicht.
    /// </summary>
    public bool BridgeEnabled { get; set; } = true;

    /// <summary>Port fuer die Bruecke. 0 heisst: einen freien nehmen.</summary>
    public int BridgePort { get; set; } = 47823;

    /// <summary>
    /// Wann sich das Addon zuletzt gemeldet hat - ueber Programmstarts hinweg.
    ///
    /// Ohne diesen Wert kann die Oberflaeche zwei voellig verschiedene Lagen nicht
    /// auseinanderhalten: Jemand hat das Addon nie installiert, oder jemand hat es
    /// laengst installiert und Blender gerade nicht offen. Beide sehen zur Laufzeit
    /// gleich aus - es meldet sich nichts.
    ///
    /// Dem Neuling gehoert die Anleitung zum Einrichten. Dem anderen waere sie eine
    /// Zumutung, und zwar jedes Mal, wenn er Blender schliesst. Deshalb wird der
    /// Zeitpunkt aufgehoben und nicht nur der laufende Zustand angesehen.
    ///
    /// Kein personenbezogener Wert: ein Zeitstempel auf dem eigenen Rechner, der das
    /// Geraet nie verlaesst.
    /// </summary>
    public DateTime? BridgeLastSeen { get; set; }

    /// <summary>
    /// Die Seite zum Zusehen im Browser, ueber den Relay.
    ///
    /// Aus, solange niemand sie einschaltet. Eine Verbindung nach draussen, die man
    /// nicht bestellt hat, ist genau die Art Ueberraschung, die ein Programm nicht
    /// bereiten soll - und die Vorschau selbst braucht sie nicht.
    ///
    /// Der Weg fuehrt bewusst ueber denselben Relay wie die Kopplung ans Handy und
    /// nicht ueber einen eigenen Server im Heimnetz. Ein eigener Server brauchte eine
    /// Oeffnung in der Firewall, also einen Weg von aussen nach innen. So baut
    /// FrameFlip die Verbindung selbst auf, nach draussen, wie ein Browser auch -
    /// es gibt keinen Eingang, der offen stehen koennte.
    /// </summary>
    public bool WatchEnabled { get; set; }

    /// <summary>
    /// Zuschauer-Geheimnis und Kennwort, mit DPAPI geschuetzt.
    ///
    /// Beides zusammen in einem Feld, siehe <see cref="Remote.WatchStore"/>. Leer
    /// heisst: Es gibt noch keinen Link; beim Einschalten entsteht einer.
    /// </summary>
    public string WatchSecret { get; set; } = string.Empty;

    /// <summary>
    /// Renderfortschritt an ein gekoppeltes Handy weiterreichen.
    ///
    /// Bleibt aus, solange kein Relay eingetragen und kein Handy gekoppelt ist.
    /// Nichts davon laeuft nebenher mit: Ohne Kopplung wird keine Verbindung
    /// aufgebaut und kein Schluessel erzeugt.
    /// </summary>
    public bool RemoteEnabled { get; set; }

    /// <summary>
    /// Welcher Fassung der Nutzungsbedingungen zugestimmt wurde. 0 heisst: keiner.
    ///
    /// Eine Zahl und kein Ja/Nein, damit sich die Zustimmung erneuern laesst. Aendern
    /// sich die Bedingungen wesentlich, wird <see cref="TermsVersion"/> hochgezaehlt -
    /// und die alte Zustimmung traegt nicht mehr. Ein Haken, der einmal gesetzt fuer
    /// immer gilt, waere eine Zustimmung zu etwas, das der Nutzer nie gesehen hat.
    /// </summary>
    public int TermsAccepted { get; set; }

    /// <summary>
    /// Die derzeit gueltige Fassung. Beim Aendern der Bedingungen hochzaehlen.
    /// </summary>
    public const int TermsVersion = 1;

    /// <summary>Liegt eine Zustimmung zur aktuellen Fassung vor?</summary>
    [JsonIgnore]
    public bool TermsOk => TermsAccepted >= TermsVersion;

    /// <summary>
    /// Wirtsname des Relays, ohne Schema und Pfad - die Verbindung wird immer als
    /// wss aufgebaut.
    ///
    /// Voreingestellt ist <see cref="DefaultRelayHost"/>, damit die Kopplung ohne
    /// eigene Serverei funktioniert. Das Feld steht im Einstellungsdialog sichtbar
    /// da und laesst sich ueberschreiben - wer einen eigenen Relay betreibt, traegt
    /// ihn ein, und ab dann geht nichts mehr ueber den fremden.
    ///
    /// Vertretbar ist das, weil der Relay nichts sehen kann: Er lernt die
    /// Raumkennung und sonst nichts, und die ist eine Einbahnstrasse aus einem
    /// Schluessel heraus, den nur dieser Rechner und das gekoppelte Handy kennen.
    /// Zwei Installationen kommen automatisch in verschiedene Raeume - der
    /// Schluessel sind 256 zufaellige Bit je Rechner, nicht etwas Abgeleitetes.
    /// </summary>
    public string RelayHost { get; set; } = DefaultRelayHost;

    /// <summary>
    /// Der oeffentliche Relay des Projekts.
    ///
    /// Steht hier als Konstante und nicht verstreut im Quelltext, damit ein Fork
    /// genau eine Zeile aendern muss - und damit man beim Lesen sofort sieht,
    /// wohin die Verbindung standardmaessig geht.
    /// </summary>
    public const string DefaultRelayHost = "relay.steggi-matrix.work";

    /// <summary>
    /// Der Kopplungsschluessel, mit DPAPI gegen das Windows-Konto verschluesselt.
    /// Siehe <see cref="Remote.PairingStore"/> - im Klartext steht er nirgends.
    /// </summary>
    public string PairingSecret { get; set; } = string.Empty;

    /// <summary>
    /// Dem Handy erlauben, .blend-Dateien aus dem Austauschordner zu holen.
    ///
    /// Aus, und zwar nicht nur als Voreinstellung: Solange das hier aus ist, kennt
    /// die Gegenseite den Ordner nicht einmal. Bis hierher konnte das Handy nur
    /// zusehen - Zahlen und ein Vorschaubild. Dateien sind der Punkt, an dem ein
    /// verlorenes Handy mehr waere als ein Mithoerer, deshalb muss das jemand hier
    /// von Hand einschalten.
    /// </summary>
    public bool FileAccessEnabled { get; set; }

    /// <summary>
    /// Zusaetzlich erlauben, Dateien ABZULEGEN.
    ///
    /// Getrennt vom Holen, weil es etwas anderes ist: Lesen kostet eine Kopie,
    /// Schreiben legt etwas auf dieser Platte an. Ueberschrieben wird trotzdem nie -
    /// siehe <see cref="Remote.FileVault"/>.
    /// </summary>
    public bool FilePushEnabled { get; set; }

    /// <summary>
    /// Dem Handy erlauben, in den Projektordnern zu blaettern und Bilder anzusehen.
    ///
    /// Etwas anderes als der Austauschordner und deshalb ein eigener Schalter: Hier
    /// geht es um alles, was im Projektbrowser steht - jeden Unterordner, jeden
    /// gerenderten Frame. Dafuer ist es ausdruecklich NUR Lesen. Wer unterwegs einen
    /// Frame ansehen will, muss dafuer nichts ablegen duerfen.
    /// </summary>
    public bool LibraryAccessEnabled { get; set; }

    /// <summary>
    /// Dem Handy erlauben, hier einen Render zu starten.
    ///
    /// Die weitreichendste der Erlaubnisse, und deshalb die letzte: Bis hierher
    /// konnte die Gegenseite lesen. Ein Render startet ein Programm auf diesem
    /// Rechner. Er tut das nur mit einer .blend-Datei, die ohnehin schon freigegeben
    /// ist, und schreibt nur in einen Ordner, den FrameFlip selbst dafuer anlegt -
    /// aber ein gestartetes Programm bleibt ein gestartetes Programm.
    /// </summary>
    public bool HeadlessRenderEnabled { get; set; }

    /// <summary>
    /// Weitere Blender-Fassungen, die von Hand eingetragen wurden.
    ///
    /// FrameFlip findet die ueblichen Orte von selbst - Steam, den
    /// Installationsordner, die Dateiverknuepfung, den Suchpfad. Ein entpacktes
    /// Blender auf einer Datenplatte findet es nicht, und genau dafuer ist das hier:
    /// Was hier steht, erscheint in der Auswahl wie jedes andere.
    /// </summary>
    public List<string> ExtraBlenders { get; set; } = new();

    /// <summary>
    /// Wo blender.exe liegt. Leer heisst: kein Render aus der Ferne.
    ///
    /// Ausdruecklich eingetragen und nicht gesucht: Auf einem Rechner mit vier
    /// Blender-Fassungen ist die Frage, WELCHE rechnet, keine, die ein Programm fuer
    /// jemanden entscheiden sollte.
    /// </summary>
    public string BlenderPath { get; set; } = string.Empty;

    /// <summary>
    /// Der EINE Ordner, in dem das stattfindet. Leer heisst: nichts geht.
    ///
    /// Kein Suchpfad, keine Liste, keine Unterordner - ein Ordner. Was sich nicht
    /// aufzaehlen laesst, laesst sich auch nicht ueberblicken, und ein Zugriff, den
    /// man nicht ueberblickt, ist keiner, den man erlauben will.
    /// </summary>
    public string FileFolder { get; set; } = string.Empty;

    /// <summary>
    /// Wo das Hauptfenster zuletzt stand. NaN heisst "noch nie" - dann wird
    /// zentriert.
    ///
    /// Gespeichert wird nur die Lage, nicht ob sie gueltig ist: Ein Bildschirm kann
    /// zwischen zwei Starts abgezogen worden sein, und dann liegt ein gemerkter Ort
    /// im Nichts. Geprueft wird deshalb beim Anzeigen, nicht beim Speichern - siehe
    /// Views.WindowPlacer.
    /// </summary>
    public double? MainLeft { get; set; }

    public double? MainTop { get; set; }
    public double MainWidth { get; set; } = 1080;
    public double MainHeight { get; set; } = 720;
    public bool MainMaximized { get; set; }

    /// <summary>
    /// Zuletzt eingestellte Anzeigekorrektur. Betrifft nur die Darstellung; die
    /// Dateien bleiben unberuehrt.
    /// </summary>
    public Imaging.ImageAdjustments? Adjustments { get; set; }

    /// <summary>
    /// Die Werkzeuge der Farbkorrektur - Kurven und was noch dazukommt.
    ///
    /// Getrennt von <see cref="Adjustments"/>, obwohl beides zusammen das Bild
    /// ergibt: Die Regler dort sind der schnelle Griff beim Beurteilen und sollen
    /// auch ohne Gleitkommamaterial wirken. Der Stapel hier greift nur auf dem
    /// angehaltenen Bild und faellt sonst nicht ins Gewicht.
    /// </summary>
    public Imaging.Grading.GradingStack? Grading { get; set; }

    /// <summary>
    /// Der Ebenenstapel des Ateliers.
    ///
    /// Er nennt Passe mit Namen, nicht mit Inhalt. Beim Oeffnen einer Datei, die
    /// diese Passe nicht fuehrt, faellt er deshalb auf die eine Grundebene zurueck -
    /// zwanzig ausgegraute Zeilen waeren keine Hilfe, sondern ein Raetsel.
    /// </summary>
    public Imaging.Grading.LayerStack? Layers { get; set; }

    /// <summary>
    /// Das Bild, das im Atelier zuletzt offen war.
    ///
    /// Gemerkt, weil der Stapel es ohnehin wird: Ein Rezept ohne sein Bild ist beim
    /// naechsten Start ein Stapel, der auf die erste beste Datei faellt, die jemand
    /// oeffnet - und dann sieht alles verschoben aus, weil es fuer eine andere
    /// Leinwand gemacht wurde. Entweder beides oder nichts.
    /// </summary>
    public string? AtelierImage { get; set; }

    /// <summary>
    /// Breite der rechten Spalte im Atelier, in Punkten.
    ///
    /// Gemerkt, weil sie nicht Geschmack ist, sondern vom Bildschirm abhaengt: Auf
    /// einem breiten Schirm will man die Ebenennamen lesen koennen, auf einem engen
    /// will man das Bild. Wer das bei jedem Start neu einstellt, stellt es
    /// irgendwann nicht mehr ein.
    /// </summary>
    public double AtelierColumnWidth { get; set; } = 300;

    /// <summary>Hoehe des Ebenenstreifens unten in der rechten Spalte.</summary>
    public double AtelierLayersHeight { get; set; } = 240;

    /// <summary>Gespeicherte Korrektureinstellungen, im Panel auswaehlbar.</summary>
    public List<AdjustmentPreset> AdjustmentPresets { get; set; } = new();

    /// <summary>
    /// Beim Export fragen, ob die Anzeigekorrektur uebernommen werden soll.
    /// null heisst "noch nicht entschieden" - dann fragt der Dialog.
    /// </summary>
    public bool? ExportApplyAdjustments { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Ctrl+Alt+Space";
        if (!(Fps > 0) || double.IsNaN(Fps)) Fps = 24.0;
        Fps = Math.Clamp(Fps, 1.0, 240.0);
        MemoryBudgetMb = Math.Clamp(MemoryBudgetMb, 64, 8192);
        PrefetchAhead = Math.Clamp(PrefetchAhead, 1, 2000);
        PrefetchBehind = Math.Clamp(PrefetchBehind, 0, 2000);
        LoadIntervalSeconds = Math.Clamp(LoadIntervalSeconds, 2, 300);
        MaxDecoderThreads = Math.Clamp(MaxDecoderThreads, 1, 16);
        WarmupFrames = Math.Clamp(WarmupFrames, 0, 2000);
        DraftStep = Math.Clamp(DraftStep, 0, 2);
        RawCacheMaxGb = Math.Clamp(RawCacheMaxGb, 1, 512);

        // Ein leeres Feld heisst "nimm den Standard", nicht "kein Relay". Wer keinen
        // will, schaltet die Fernsteuerung ab - das ist der eindeutige Weg.
        RelayHost = RelayHost?.Trim() is { Length: > 0 } host ? host : DefaultRelayHost;

        /* Ohne Zustimmung geht NICHTS nach draussen.
         *
         * Der Riegel sitzt hier und nicht in der Oberflaeche. Die Oberflaeche ist ein
         * Weg von vielen - es gibt den Einstellungsdialog, die Kopplungstafel, eine von
         * Hand geaenderte config.json und jeden kuenftigen Weg, den noch niemand
         * gebaut hat. Jeden einzeln zu sichern hiesse, einen davon zu vergessen.
         *
         * Normalize laeuft dagegen bei jedem Laden und bei jeder Uebernahme. Was hier
         * abgeschaltet wird, bleibt abgeschaltet, ganz gleich wer es eingeschaltet hat.
         * Die Oberflaeche holt die Zustimmung ein; durchgesetzt wird sie hier. */
        if (TermsAccepted < TermsVersion)
        {
            RemoteEnabled = false;
            WatchEnabled = false;
        }

        // Eingeschaltet ohne Schluessel waere ein Zustand, den die Oberflaeche
        // anzeigt und der nichts tut. Lieber ehrlich aus.
        if (PairingSecret.Length == 0) RemoteEnabled = false;

        // Dasselbe fuer den Dateizugriff, nur strenger: Ohne Ordner gibt es nichts
        // zuzugreifen, ohne Fernsteuerung niemanden, der zugreift, und Ablegen ohne
        // Holen waere ein blinder Briefkasten. Jede dieser Ecken einzeln zu pruefen
        // hiesse, sich an jeder Stelle daran zu erinnern.
        FileFolder = FileFolder?.Trim() ?? string.Empty;

        if (FileFolder.Length == 0 || !RemoteEnabled) FileAccessEnabled = false;
        if (!FileAccessEnabled) FilePushEnabled = false;

        // Die Bibliothek haengt nicht am Austauschordner - aber ohne Fernsteuerung
        // ist auch sie sinnlos.
        if (!RemoteEnabled) LibraryAccessEnabled = false;

        // Und der Render aus der Ferne braucht beides: jemanden, der ihn ausloest,
        // und ein Blender, das rechnet.
        BlenderPath = BlenderPath?.Trim() ?? string.Empty;

        ExtraBlenders = (ExtraBlenders ?? new List<string>())
                        .Select(path => path?.Trim() ?? string.Empty)
                        .Where(path => path.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

        if (!RemoteEnabled || BlenderPath.Length == 0) HeadlessRenderEnabled = false;
    }

    /// <summary>Abgeleitet - gehoert nicht in die Konfigurationsdatei.</summary>
    [JsonIgnore]
    public long MemoryBudgetBytes => (long)MemoryBudgetMb * 1024L * 1024L;
}
