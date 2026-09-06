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
    /// Renderfortschritt an ein gekoppeltes Handy weiterreichen.
    ///
    /// Bleibt aus, solange kein Relay eingetragen und kein Handy gekoppelt ist.
    /// Nichts davon laeuft nebenher mit: Ohne Kopplung wird keine Verbindung
    /// aufgebaut und kein Schluessel erzeugt.
    /// </summary>
    public bool RemoteEnabled { get; set; }

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
    /// Zuletzt eingestellte Anzeigekorrektur. Betrifft nur die Darstellung; die
    /// Dateien bleiben unberuehrt.
    /// </summary>
    public Imaging.ImageAdjustments? Adjustments { get; set; }

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

        // Eingeschaltet ohne Schluessel waere ein Zustand, den die Oberflaeche
        // anzeigt und der nichts tut. Lieber ehrlich aus.
        if (PairingSecret.Length == 0) RemoteEnabled = false;
    }

    /// <summary>Abgeleitet - gehoert nicht in die Konfigurationsdatei.</summary>
    [JsonIgnore]
    public long MemoryBudgetBytes => (long)MemoryBudgetMb * 1024L * 1024L;
}
