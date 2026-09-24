# Refactoring-Fahrplan

Dieser Fahrplan hält die Reihenfolge und den erreichten Stand der Strukturarbeit
fest. Jeder Schnitt baut auf einem getesteten Sicherheits- und Funktionsstand auf.

**Priorität nach Nutzerentscheidung vom 20. September:** Zuerst D2
(Watch-Lebenszyklus), danach D1 (Dashboard) und die übrigen offenen Schnitte
(Android, feldweise Protokolltypen, Relay/Bridge nur bei Bedarf).
**Studio / Atelier kommt erst am Ende.** S0–S6 sind bis dahin vorgemerkt und
keine Voraussetzung für das übrige Refactoring. Die aktive Atelier-Entwicklung
läuft separat weiter.

## Bestandsaufnahme vom 20. September 2026

Die ursprünglichen Desktop-Schnitte sind vorhanden. Seitdem sind das Dashboard
als Hauptoberfläche, die Zuschauerseite und insbesondere **Studio / Atelier**
hinzugekommen. Deshalb bedeutet „Desktop-Schnitte umgesetzt“ unten nur den
damaligen Umfang, nicht einen abgeschlossenen Umbau der heutigen Anwendung.

Mit **Studio** ist hier der Bearbeitungsbereich gemeint, der in Oberfläche und
Quelltext derzeit **Atelier** heißt (`AtelierPage`). Eine Umbenennung ist nicht
Teil dieses Plans. Der detaillierte Fahrplan steht in
[Studio / Atelier: Refactoring](Refactoring-Studio.md).

Grundlage dieser Aktualisierung: lokaler Branch `feature/atelier`, Commit
`fcf6ad1`, zusätzlich der noch uncommittete Masken-Arbeitsstand und das lokale
Protokoll der laufenden Claude-Code-Sitzung „FrameFlip Mehrschichten und
Compositing“ bis zum 20. September. Das ist eine Bestandsaufnahme des
Arbeitsbranches, keine Aussage über den Merge- oder Release-Stand. In dieser
Planungsrunde wurden keine Anwendungstests ausgeführt.

| Bereich | Tatsächlicher Stand | Folgerung für das Refactoring |
| --- | --- | --- |
| Frühere Desktop-Controller | Playback, Projektnavigation/-quellen und App-Lebenszyklen sind getrennt; siehe historischen Stand unten. | Erhalten und gezielt erweitern, nicht erneut aufbauen. |
| Dashboard und Einstellungen | `MainWindow` trägt jetzt eigene Sequenzwiedergabe, Vorladen, Ordnerbeobachtung und Seitenwechsel; Einstellungen sind integriert. | Neuer Schnitt D1 für die Dashboard-Sitzung; die alte Viewer-Auslagerung deckt diese Logik nicht ab. |
| Zuschauerseite und Zustimmung | `WatchService`, `WatchState`, `NewestFrame`, Watch-Verbindungsbausteine und `web/` sind hinzugekommen; Start/Austausch des Dienstes liegen in `AppHost`. | Kleiner Lebenszyklus-Schnitt D2; Zustimmung, Schlüssel und Protokolle unverändert halten. |
| Studio / Atelier | Eigene Seite, Float-/EXR-Verarbeitung, sechs Werkzeugarten, Ebenen/Gruppen, Masken, Bild- und Videoexport, Werkzeug- und Eigenschaftsspalte sind im Branch vorhanden. | Eigene Folge S0–S6 statt Erweiterung des alten `ViewerPlaybackController` um Editoraufgaben. |
| Aktuelle Studio-Arbeit | Gemalte Masken, Pinselschalter/-einstellungen und Drag-Bündelung sind committed. Farbbereichsmasken und Tiefen-/Mitten-/Lichter-Vorgaben werden gerade ergänzt. | Betroffene Masken-/Panel-Dateien erst nach einem abgeschlossenen Feature-Stand strukturell umbauen. |

Die Produktentwürfe [Atelier](Atelier.md),
[Atelier-Oberfläche](Atelier-Oberflaeche.md) und
[Glitch-Galerie](Atelier-Glitch.md) bleiben die Quellen für Produktwünsche.
Einige Statusangaben sind überholt: gemalte Masken werden dort noch als fehlend
beschrieben, sind aber implementiert. Offene Wünsche wie Undo, weitere
Glitch-Effekte oder ablösbare Panels sind keine Voraussetzung für sämtliche
Refactoring-Schritte und werden nicht als bereits umgesetzt gezählt.

## Fortschritt am 23. September 2026

Die Strukturarbeit entstand separat auf `codex/refactor-watch-lifecycle`, aufgebaut
auf Claudes gespeichertem Feature-Stand `7e92276`. Der gemeinsam benutzte
Feature-Checkout bleibt bei Claude. Die folgenden Schnitte sind **implementiert,
geprüft und am 24. September in `feature/atelier` gemergt** (`2675ed2`), noch
nicht in `main`:

- **D2 abgeschlossen:** `AppWatchController` besitzt Start, Austausch,
  Einstellungsvergleich und Ende des Zuschauerdienstes. Schlüsselverwaltung,
  Zustimmung, Kennwort und die öffentliche `WatchService`-Fassade bleiben im
  bisherigen Ablauf. 51 Zusicherungen sichern die Host-Integration ab. Der
  fehlende Lastbedarf für allein laufendes Watch wurde vor der Auslagerung
  separat nachgewiesen und korrigiert.
- **D1a abgeschlossen:** `DashboardLiveController` besitzt Ordnerbeobachtung
  und die gemeinsame Ruhefrist von Dateimeldungen und Bridge. 13 Prüfungen am
  echten Dashboard charakterisieren Follow, Bereichsenden, gefüllte Lücken,
  Ordnerwechsel und einen entfernten Seed. Weitere 19 Controller-Prüfungen
  sichern Filter, Bündelung, Fehler beim Ordnerwechsel und das Beenden ab.
  Neun zunächst fehlgeschlagene Zusicherungen haben verspätete Rückrufe
  nach Auswahlwechsel oder Schließen nachgewiesen; die Korrektur ist ein
  eigener Commit nach der reinen Auslagerung.
- **D1b abgeschlossen:** `DashboardSequenceController` besitzt Bibliotheks-
  und Sitzungseinträge, Auswahl, Seed-/Ordnersuche, Live-Scan und die
  Hintergrundzählungen. `MainWindow` zeichnet die Zeilen und hält nach diesem
  Schnitt noch Abspielposition, Follow, Bereiche und Bildspeicher. 22 Prüfungen am echten
  Dashboard sichern Auswahl, Verlauf, Neuaufbau und leere Ausgaben; 33
  Controller-Prüfungen sichern Lesefehler, Dispatcher-Zustellung und verspätete
  Ergebnisse. Höchstens eine Hintergrundzählung liest gleichzeitig; überholte
  Listen beginnen nach einem blockierten Zugriff keinen weiteren Scan.
  Listen- und Eintragsrevisionen verhindern, dass alte Zählungen neuere
  Auswahl-/Live-Ergebnisse überschreiben. Schließen wartet nicht auf den
  Dateizugriff und verwirft seine Rückgaben.
  Zwei vorhandene Fehler wurden vor der Auslagerung separat korrigiert und
  mit drei zunächst fehlgeschlagenen Zusicherungen nachgewiesen: Der erste
  Frame erscheint jetzt auch in einer zuvor leeren Ausgabe, und eine geleerte
  Bibliothek gibt Auswahl und Wiedergabestatus frei.
- **D1c abgeschlossen:** `DashboardFrameController` besitzt Einzelbilddecoder,
  den jeweils letzten Bildwunsch, Vorlader und Bildspeicher. Die vorhandenen
  Decoder-/Preloader-Algorithmen bleiben erhalten; Decode-Breite, Speicherbudget
  und Lastregelung liefert weiterhin das Fenster. 17 Charakterisierungsprüfungen
  sichern Bildwünsche, Teilvorladen, Bereichsprüfung, Fortschritt und Abbruch.
  Acht Regressionstests sichern verspätete Bilder und Fortschrittsmeldungen
  nach Auswahlwechsel, Cache-Treffer oder Schließen; sieben davon schlugen vor
  der separat committed Korrektur fehl. Auch eine leere Auswahl gibt den
  Bildspeicher und die laufende Vorbereitung sofort frei.
  `DashboardVideoController` besitzt die optionale Videovorbereitung. Bereich,
  Frame-Liste und Exportwerte werden vor dem Hintergrundzugriff festgehalten.
  Ein abgebrochener Auftrag liefert keine Cache-Metadaten oder UI-Rückgaben an
  seinen Nachfolger. Der bisherige `PreparedVideo`-Cache und `VideoExporter`
  bleiben erhalten. `DashboardMediaControllerInvariants` ergänzt 32 Prüfungen:
  zehn für Sitzungen und Fehlerrückgaben des Bildcontrollers, 17 für die
  Videovorbereitung am Controller und fünf für ihre Fensteranbindung. Sie sichern
  Wiederverwendung, Fehler, Abbruch während des Dateizugriffs, erneuten Start,
  Besitzwechsel und Schließen. Die neuen Videotests verwenden einen
  austauschbaren Encoder; sie prüfen den Ablauf, keine reale Kodierung.
- **D1 blieb nach diesen Schnitten offen:** Playback folgte am 24. September
  als eigener Schnitt (siehe unten). Auswahl- und Live-Scans laufen wie bisher
  synchron; die Begrenzung der Leser betrifft die beiläufigen Zeilenzählungen.
- **S0–S6 bleiben zurückgestellt:** Atelier-Dateien und die gemeinsamen
  Kopplungs-/Watch-Protokolle wurden in diesen Schnitten nicht geändert.

Der Testläufer nimmt Gruppen als Namen (`Klasse Klasse.Methode`) oder als
`--only=Klasse,Klasse.Methode`, damit sämtliche registrierten Prüfungen in
kurzen Gruppen laufen können. Ohne Angabe bleibt der bisherige Gesamtlauf
erhalten. Tests verwenden eigene Konfigurationen und synthetische Bilder.

Abnahme des Branchstands bis zum Bildspeicher-Schnitt (`d0239e7`): **3.601
Zusicherungen aus 92 registrierten Kern-Testaufrufen** in zehn Gruppen (jede
unter 55 Sekunden) sowie **227 UI-Prüfungen** erfolgreich; beide Projekte bauen
in Release. Vier bereits vorhandene Nullable-Warnungen bleiben in
`AtelierLayerInvariants` und `StackReproInvariants`. Der anschließende
Video-Schnitt wurde am 24. September gezielt geprüft: Debug-Build ohne neue
Warnungen und **140 Zusicherungen** aus den sieben Dashboard-Testaufrufen
(`DashboardMediaControllerInvariants`, `DashboardFrameInvariants` mit
`RetiredWork`, `DashboardSelectionInvariants`, `DashboardLiveInvariants`,
`DashboardSequenceControllerInvariants`, `DashboardLiveControllerInvariants`).
Nach dem Merge in `feature/atelier` liefen beide Prüfreihen vollständig über den
gemeinsamen Stand, einschließlich Claudes Atelier-Commits bis `4c60517`:
**4.274 Zusicherungen** in `FrameFlip.Tests` (ein Gesamtlauf, 67 Sekunden) und
**227 UI-Prüfungen**, beide erfolgreich.

## Fortschritt am 24. September 2026

Der Playback-Schnitt liegt auf `refactor/dashboard-playback`, aufgebaut auf dem
gemergten Stand `b3bc6c9`. Er ist **implementiert, geprüft und am 24. September
in `feature/atelier` gemergt** (`d35aa36`), noch nicht in `main`.

- **Testläufer nach dem Merge:** Atelier und dieser Strang hatten unabhängig je
  einen Gruppenfilter eingeführt. Nach dem textlich konfliktfreien Merge landete
  jedes Argument im Filter der Atelier-Seite; `--only=` und `--watch-lifecycle`
  meldeten eine unbekannte Gruppe. Ein eigener Commit führt beide Schreibweisen
  in einem Filter zusammen.
- **D1d abgeschlossen:** `DashboardPlaybackController` besitzt Kopf, Bereich,
  Abspielzustand, Follow und Bildrate. Alles zählt in Framenummern: Lücken
  bleiben Lücken, und ein Bereich überdauert das Nachwachsen der Folge. Der
  Controller entscheidet den nächsten Takt, Schrittziele, die Begrenzung von
  Start und Ende, das Mitwachsen beim Live-Scan, Follow-Sprünge und die
  Ratenregeln. Zeitgeber, Schleifenschalter, Eingaben und Darstellung bleiben
  wie beim Viewer am Fenster. Der vorhandene `ViewerPlaybackController` wurde
  bewusst nicht wiederverwendet: Er rechnet mit Positionen statt Framenummern,
  verwirft einen gegenläufigen Bereich statt ihn zu begrenzen und kennt weder
  Follow noch eine wachsende Folge.
  36 Prüfungen am echten Dashboard charakterisieren vor der Auslagerung
  Abspielen/Pause, Lücken, Schleife mit und ohne Bereich, Einzelschritte,
  Sprünge, Start/Ende, Bildraten samt Speichern, den Übergang vom Vorladen,
  Follow und den Auswahlwechsel. Weitere 23 Controller-Prüfungen ohne Fenster
  sichern leere und einzelne Folgen, Gleichstände beim nächsten Frame, einen
  Kopf außerhalb des Bereichs, Nachwachsen, Schrumpfen und Ratenfehler ab. Die
  Charakterisierung lief vor und nach der Auslagerung unverändert grün; die
  bestehenden Dashboard-Prüfungen setzen den Wiedergabezustand jetzt über den
  Controller statt über Fensterfelder. Es wurde kein Fehler gefunden.
- **Beobachtet, nicht geändert:** Ohne Schleife beginnt ein Start am
  Bereichsende nicht von vorn, sondern hält beim ersten Takt wieder an. Schrumpft
  eine Folge beim Live-Scan über einen von Hand gesetzten Start oder ein von Hand
  gesetztes Ende hinweg, kann der Start hinter dem Ende stehen; Export und
  Bereichsanzeige finden dann keine Bilder. Beides ist Produktverhalten und bleibt einer eigenen
  Entscheidung vorbehalten.

Abnahme dieses Branchstands: **4.333 Zusicherungen** in `FrameFlip.Tests` (ein
Gesamtlauf, 65 Sekunden) und **227 UI-Prüfungen**; beide Testprojekte bauen in
Debug und Release ohne neue Warnungen. Im Gesamtlauf überschritt einmal die
bekannte Zeitprüfung „beide zusammen bleiben im Rahmen“ in `OpticsInvariants`
ihre Grenze (95,6 ms). Die Maschine war dabei kaum belastet; die Gruppe lief
danach einzeln dreimal grün. Der Schnitt berührt keinen Optik-Code.
Nach dem Merge liefen beide Prüfreihen vollständig über den gemeinsamen Stand,
einschließlich Claudes Atelier-Commits bis `1659d67`: **4.350 Zusicherungen** in
`FrameFlip.Tests` (ein Gesamtlauf, 70 Sekunden) und **227 UI-Prüfungen**, alle
erfolgreich.

## Historischer Stand der ersten Desktop-Schnitte (9. September 2026)

- Der gemeinsame Ausgangspunkt ist mit `v2-secure-baseline` markiert; die
  v2-Konformitätstests und der Relay-Reconnect-Fix sind integriert.
- `RemoteLink` bleibt die öffentliche Fassade. Status-Telemetrie (PR #8),
  Befehlsverteilung (PR #9) sowie Vorschau/Follow (PR #10) sind ausgelagert und
  gemergt.
- `ViewerPlaybackController` ist mit PR #11 integriert. Er besitzt
  Uhr, Position, Wiedergabe-/Pufferzustand und den aktiven Framebereich. WPF-Timer,
  Cache, Fensterlebenszyklus und Bilddarstellung bleiben im `ViewerWindow`.
  Die Bedienung ist am Fenster charakterisiert; zusätzliche Controller-Tests
  prüfen Pufferfristen ohne reale Wartezeit.
- `ProjectNavigation` ist mit PR #12 integriert und übernimmt die
  Projekt-/Ordnerauswahl, Zurück-Navigation,
  Brotkrumen und den Zustand der Versionsansicht aus `ProjectsPage`. Die Seite
  schließt weiterhin zuerst eine offene Bildvorschau.
  Navigationstests verwenden austauschbare Scan-/Verlaufsquellen, damit sie
  keine persönliche Projektbibliothek lesen.
- `ProjectScanService` ist mit PR #13 integriert und übernimmt Bibliotheks-, Ordner-, Frame- und
  Verlaufslesezugriffe sowie die Suche nach Thumbnail-Pfaden. Die Quellen laufen
  außerhalb des UI-Threads; Bibliothek und Inhalt haben getrennte, jeweils
  begrenzte Worker. Überholte Anfragen liefern weder alte Kacheln noch alte
  Fehler an die Seite. Charakterisierungstests sichern Reihenfolge und
  Ordnernavigation; zusätzliche Tests prüfen blockierte Leser, Abbruch,
  Dispatcher-Rückkehr und erneutes Einlesen nach Fehlern.
- `ProjectThumbnailService` ist mit PR #14 integriert und übernimmt Bitmap-Dekodierung und den gemeinsamen,
  nach Dateipfad und Breite getrennten Cache. Größen, Farbprofilbehandlung und
  die bisherige Cache-Grenze bleiben erhalten. Ein eigener Dateistream wird
  auch nach defekten Bildern sofort geschlossen. Abgelöste Ladevorgänge liefern
  keine Bilder mehr; die Seite zeichnet nur noch über ihren Dispatcher und
  prüft dabei die aktuelle Ansicht. Tests sichern Dateifreigabe, erneutes Laden,
  gemeinsame Cache-Nutzung, parallele Zugriffe und verspätete Rückgaben ab.
  Kacheln, Navigationseingaben und WPF-Darstellung bleiben in `ProjectsPage`.
- `AppWindowController` ist mit PR #15 integriert und übernimmt die Lebenszyklen von Hauptfenster,
  Einstellungen und Kopplungsdialog aus `AppHost`: einmaliges Erzeugen,
  Wiederverwenden, Owner-Zuordnung und Freigabe nach `Closed`. Der Host stellt
  die Fenster mit aktuellen Einstellungen und Callbacks bereit. Er entscheidet
  weiter über die Lastmessung; der Controller meldet das neue Hauptfenster vor
  `Show` und sein Ende nach dem Freigeben der Referenz. Charakterisierungs- und
  Controller-Tests sichern minimierte Fenster, Owner-Schließung, abgebrochene
  Schließvorgänge und den Fokusverlust-Schutz des ursprünglichen Viewers ab.
- `ViewerOpenController` ist mit PR #16 integriert und übernimmt die Öffnungsentscheidungen aus `AppHost`:
  Explorer-Auswahl und Ordner-Fallback, explizites Dateiöffnen, Headerprüfung,
  Hotkey-Schließen, Aktivieren derselben Sequenz und Laden einer anderen Sequenz
  im vorhandenen Fenster. Austauschbare Quellen und ein internes Viewer-Ziel
  ermöglichen Tests ohne persönliche Dateien oder echte Explorer-Auswahl.
  `AppHost` erzeugt weiterhin den Viewer und verbindet ihn mit Einstellungen,
  Render-/Lastmonitor und Verlauf. Tests am echten WPF-Viewer haben außerdem
  eine ungültige Dekodier-Untergrenze bei Bildern unter 16 Pixeln aufgedeckt;
  sie ist für Breite und Höhe korrigiert und bis 1 × 1 Pixel abgesichert.
- `AppTrayController` ist mit PR #17 integriert und übernimmt Tray-Menü, Doppelklick, Sprachwechsel,
  Hotkey-Tooltip und Benachrichtigungen aus `AppHost`. Der Host liefert die
  Anwendungsaktionen und besitzt weiterhin Start und Ende der Anwendung.
  Der Controller gibt Menü, NotifyIcon und das geladene Icon frei und meldet
  seinen Sprachwechsel-Handler ab. Die letzten beiden Freigaben fehlten zuvor
  und sind vor der Korrektur mit Regressionstests nachgewiesen worden.
  Tests mit echten, unsichtbaren WinForms-Komponenten sichern Menüaktionen,
  beide Sprachen, Tooltip-Grenzen, abgewiesene Meldungen, wiederholtes Beenden,
  unabhängige Instanzen und die Freigabe durch den Host ab.
- `AppRemoteController` ist mit PR #18 integriert und übernimmt Aufbau, Austausch und Beenden der optionalen
  Remote-Verbindung sowie den Vergleich verbindungsrelevanter Einstellungen.
  Der Host liefert Bridge-Verfügbarkeit und Verbindungsfabrik; Einstellungen
  und Lastwerte werden weiterhin live gelesen. Ein interner Adapter hält die
  öffentliche `RemoteLink`-Fassade unverändert. Das Ende alter Verbindungen
  blockiert den UI-Thread nicht und kann eine neue Verbindung nicht freigeben.
  Auch bei ungültiger Relay-Adresse wird der Lastbedarf jetzt neu bewertet;
  zuvor konnte der Lastmonitor nach einem fehlgeschlagenen Wechsel weiterlaufen.
  Charakterisierungs- und Regressionstests sichern Voraussetzungen, relevante
  Einstellungswechsel, Zustandsweitergabe, ausstehende Freigaben, Startfehler
  und wiederholtes Beenden ab. Ein Integrationstest prüft dabei die Freigabe
  des echten Lastmonitors ohne Netzwerk oder persönliche Konfigurationsdateien.
- `AppLoadController` übernimmt Bedarf, Wiederverwendung und Freigabe der
  Lastmessung sowie Render-Modus, Messwertzustellung und Prozesspriorität.
  Interne Adapter erhalten die öffentlichen Monitor- und Viewer-Schnittstellen.
  Der erste Viewer zählt schon vor seiner Konstruktion als Verbraucher und
  erhält das passende Decoderlimit. Ein fehlgeschlagener Fensteraufbau gibt
  diese Reservierung wieder frei. Alte Monitor-Sitzungen dürfen weder bereits
  eingereihte noch verspätet gemeldete Messwerte an die UI liefern.
  Der Render-Modus bleibt auch ohne Monitor erhalten; unveränderte
  Fortschrittsmeldungen setzen den Messtimer nicht wiederholt zurück.
  Tests sichern alle Kombinationen von Verbrauchern und adaptiver Regelung,
  Wiederverwendung, Startfehler, Beenden, Render-Ereignisse und die Zustellung
  über den echten WPF-Dispatcher ab. Die Mess- und Ressourcenalgorithmen in
  `SystemLoadMonitor` bleiben unverändert.

## Ausgangspunkt und weitergeltende Protokollgrenze

Der ursprüngliche Einstieg verlangte integrierte Fehler-/Sicherheitskorrekturen,
einen echten QR-Test und den gemeinsamen v2-Ausgangspunkt. Der markierte Stand
`v2-secure-baseline` und die darauf aufgebauten Schnitte sind oben dokumentiert;
dieser Einstieg wird durch Studio nicht erneut aufgerollt. Die v2-Kopplung bleibt
eine gemeinsame Desktop-, Android- und Relay-Schnittstelle. Änderungen daran
brauchen weiterhin gemeinsame Gegenprüfungen und einen echten QR-Kopplungstest.

Während der hier geplanten Strukturarbeit bleiben diese Protokollteile unverändert:

- Desktop: `PairingKey`, `SecureChannel`, `RelayClient`, `RelayControl`,
  `Envelope` und `PairingInvite`.
- Android: die jeweiligen `PairingKey`-, `SecureChannel`-, `RelayClient`-,
  `Pairing`- und `Envelope`-Gegenstücke.
- Relay: Control-Frames und Zugangsbegrenzungen.

Das schützt die wichtigste Eigenschaft der App: weiterhin QR-Code scannen und
sofort koppeln, ohne zusätzliche Schritte.

## Reihenfolge

1. **Stabilen Ausgangspunkt markieren.** Nach QR-Test und gemeinsamen v2-Merge
   wird ein Git-Tag gesetzt. Die bekannten v2-Verbindungstests und feste
   Testvektoren für Raum-ID, Schlüssel, Hello, Confirmation und Cipherframes
   werden davor oder im selben kleinen PR ergänzt.

2. **Desktop: `RemoteLink` zerlegen, Fassade behalten.** Die öffentliche
   `RemoteLink`-Klasse bleibt zunächst unverändert. Intern werden nacheinander
   ein `RemoteCommandRouter`, ein Status-/Telemetrie-Publisher und ein
   Preview-/Follow-Service herausgezogen. So bleiben `AppHost` und die UI von
   der Strukturänderung unberührt.

3. **Desktop-Fenster in kleinen Schritten entflechten.** Zuerst erhält
   `ViewerWindow` einen testbaren Sitzungs-/Playback-Controller. Danach folgen
   in separaten PRs Scan/Navigation/Thumbnails aus `ProjectsPage`; erst zum
   Schluss wird der Window-, Tray-, Remote- und Load-Monitor-Lebenszyklus aus
   `AppHost` aufgeteilt. **Dieser ursprüngliche Umfang ist umgesetzt.** Die
   hinzugekommenen Desktop-Bereiche folgen jetzt als eigene Schnitte:

   - **D1: Dashboard-Sitzung.** Nach einem stabilen UI-Stand Sequenzauswahl und
     Watcher, danach Vorladen/Cache, danach Playback aus `MainWindow` lösen;
     jeder Teil ist ein eigener PR. Vor Wiederverwendung von
     `ViewerPlaybackController` die Unterschiede bei Follow, Lücken,
     In-/Out-Punkten und vorbereitetem Video charakterisieren. Layout,
     Zustimmungs-/Kopplungstafeln und WPF-Eingaben bleiben am Fenster.
   - **D2: Watch-Lebenszyklus.** Start, Austausch und Ende aus `AppHost` in
     einen kleinen internen Controller verlagern, nach dem Muster von
     `AppRemoteController`. `WatchService` bleibt die Fassade. Linkerneuerung,
     Kennwortwechsel, fehlende Zustimmung, alte asynchrone Freigaben und
     Lastbedarf ohne offenes Hauptfenster sind die Abnahmefälle. Watch-Transport,
     `web/` und Relay werden dabei nicht umgebaut.

4. **Android: `RemoteHub` als Fassade erhalten.** Aus dem großen Hub werden
   einzeln `ConnectionController`, `LibraryFeature`, `RenderFeature`,
   `VaultFeature`, `PlaybackFeature` und `LiveFeature`. Compose-Screens bleiben
   beim jeweiligen Schritt erreichbar und werden nicht gleichzeitig umgebaut.
   Dieser Strang bleibt offen. Er kann während laufender Studio-Featurearbeit
   unabhängig fortgesetzt werden, soweit keine gemeinsamen Protokoll- oder
   Desktop-Dateien verändert werden müssen. Die alten Nummern sind keine neue
   Pflicht, erst alle Studio-Produktwünsche fertigzustellen.

5. **Protokoll feldweise typisieren.** Nicht alle JSON-Befehle auf einmal
   ersetzen. Die Umstellung folgt der Funktion: zuerst Bibliothek, dann Render,
   dann Upload. Jeder PR enthält Parser, Builder und Gegenvektoren für Desktop
   und Android.

6. **Relay und Bridge zuletzt und nur bei Bedarf.** Das Relay ist bereits klein
   genug; keine Framework- oder DI-Schicht einführen. Bei der Blender-Bridge
   lohnt sich eine weitere Trennung erst nach den Desktop-/Android-Schnitten.

7. **Studio / Atelier erst am Ende.** S0–S6 sind für den Abschluss vorgemerkt:
   Verhalten und Rezeptkompatibilität, Öffnung/Lebenszyklus, Bearbeitungszustand,
   Export, Vorschauplanung und stabilisierte Panels. Rechenkerne nur bei
   nachgewiesenem Bedarf weiter aufteilen.
   [Arbeitspakete, Abhängigkeiten und Abnahme](Refactoring-Studio.md).

## Arbeitsregeln

- Ein PR enthält genau einen Schnitt und bleibt rückrollbar.
- Zuerst wird eine Charakterisierungs- oder Regressionstest ergänzt, dann wird
  Code extrahiert, zuletzt wird unbenutzter Altcode entfernt.
- Öffentliche Fassaden, Befehlsnamen und die QR-Kopplung bleiben während eines
  Schritts stabil.
- Ein Refactoring-PR darf keine neue Produktfunktion und keine
  Sicherheitsänderung vermischen.
- Nach jedem Schritt laufen die Desktop-Tests; bei Protokolländerungen zusätzlich
  die Gegenprüfung auf Android und am Relay.
- Für Desktop-Codeänderungen gehören die beiden vorhandenen Prüfreihen
  `FrameFlip.Tests` und `FrameFlip.UiTests` zur Abnahme. Studio-Schritte brauchen
  zusätzlich die jeweils genannten Verhaltensfälle, nicht nur Tests der
  Rechenkerne. Eine reine Planänderung verlangt keinen Anwendungsstart.
- Laufende Featurearbeit und Strukturarbeit bekommen getrennte Branches/PRs.
  Im gemeinsam benutzten Checkout keine Branchwechsel, Datei-Verschiebungen oder
  flächigen Formatierungen unter der laufenden Claude-Sitzung. Sobald Codearbeit
  beginnt, von einem vereinbarten Commit in einem eigenen Git-Worktree arbeiten;
  keine weiteren vollständigen Ordnerkopien.
- Der Abschluss eines betroffenen Feature-Pakets reicht für dessen Refactoring.
  Kein globaler Entwicklungsstopp und kein Warten auf ein „fertiges Studio“.

## Nächster Startpunkt

**D1 ist mit dem Playback-Schnitt abgeschlossen.** D2 und D1a–D1d sind in
`feature/atelier` gemergt, noch nicht in `main`. Danach folgen die übrigen
offenen Schritte in der oben festgelegten Reihenfolge: Android-`RemoteHub`,
feldweise Protokolltypen, Relay/Bridge nur bei Bedarf. Die Atelier-Seite bleibt
außerhalb dieser Schnitte.

**Erst abschließend S0–S6.** Vor dem Einstieg den dann aktuellen Atelier-Stand
neu lesen: Die jetzt dokumentierten Codebefunde können durch die laufende
Featurearbeit bereits verändert oder behoben sein. Für jedes Paket bleiben
„geplant“, „im Branch implementiert“, „geprüft“ und „gemergt“ getrennte Zustände.
