# Refactoring-Fahrplan

Dieser Fahrplan hält die Reihenfolge und den erreichten Stand der Strukturarbeit
fest. Jeder Schnitt baut auf einem getesteten Sicherheits- und Funktionsstand auf.

## Stand am 9. September 2026

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
- `ViewerOpenController` übernimmt die Öffnungsentscheidungen aus `AppHost`:
  Explorer-Auswahl und Ordner-Fallback, explizites Dateiöffnen, Headerprüfung,
  Hotkey-Schließen, Aktivieren derselben Sequenz und Laden einer anderen Sequenz
  im vorhandenen Fenster. Austauschbare Quellen und ein internes Viewer-Ziel
  ermöglichen Tests ohne persönliche Dateien oder echte Explorer-Auswahl.
  `AppHost` erzeugt weiterhin den Viewer und verbindet ihn mit Einstellungen,
  Render-/Lastmonitor und Verlauf. Tests am echten WPF-Viewer haben außerdem
  eine ungültige Dekodier-Untergrenze bei Bildern unter 16 Pixeln aufgedeckt;
  sie ist für Breite und Höhe korrigiert und bis 1 × 1 Pixel abgesichert.

## Ausgangspunkt und Sperre

Vor dem ersten größeren Refactoring müssen die offenen Fehler- und
Sicherheits-PRs in `main` sein und der Stand getaggt werden. Die v2-Kopplung ist
eine gemeinsame Desktop-, Android- und Relay-Schnittstelle. Sie wird erst nach
einem erfolgreichen echten QR-Kopplungstest gemeinsam zusammengeführt.

Bis dahin bleiben diese Protokollteile unverändert:

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
   `AppHost` aufgeteilt.

4. **Android: `RemoteHub` als Fassade erhalten.** Aus dem großen Hub werden
   einzeln `ConnectionController`, `LibraryFeature`, `RenderFeature`,
   `VaultFeature`, `PlaybackFeature` und `LiveFeature`. Compose-Screens bleiben
   beim jeweiligen Schritt erreichbar und werden nicht gleichzeitig umgebaut.

5. **Protokoll feldweise typisieren.** Nicht alle JSON-Befehle auf einmal
   ersetzen. Die Umstellung folgt der Funktion: zuerst Bibliothek, dann Render,
   dann Upload. Jeder PR enthält Parser, Builder und Gegenvektoren für Desktop
   und Android.

6. **Relay und Bridge zuletzt und nur bei Bedarf.** Das Relay ist bereits klein
   genug; keine Framework- oder DI-Schicht einführen. Bei der Blender-Bridge
   lohnt sich eine weitere Trennung erst nach den Desktop-/Android-Schnitten.

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

## Nächster Startpunkt

Nach Fenster- und Viewer-Öffnungssteuerung folgt der Tray-Lebenszyklus aus
`AppHost`: Menüaktionen, Sprachwechsel, Benachrichtigungen und Freigabe beim
Beenden werden zuerst charakterisiert. Danach folgen Remote- und
Load-Monitor-Zuständigkeiten. Erst anschließend beginnt der Android-Umbau.
