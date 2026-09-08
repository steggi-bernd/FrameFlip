# Refactoring-Fahrplan

Dieser Fahrplan hält die Reihenfolge und den erreichten Stand der Strukturarbeit
fest. Jeder Schnitt baut auf einem getesteten Sicherheits- und Funktionsstand auf.

## Stand am 8. September 2026

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
- `ProjectNavigation` ist mit PR #12 integriert und übernimmt die Projekt-/Ordnerauswahl, Zurück-Navigation,
  Brotkrumen und den Zustand der Versionsansicht aus `ProjectsPage`. Die Seite
  schließt weiterhin zuerst eine offene Bildvorschau.
  Navigationstests verwenden austauschbare Scan-/Verlaufsquellen, damit sie
  keine persönliche Projektbibliothek lesen.
- `ProjectScanService` übernimmt Bibliotheks-, Ordner-, Frame- und
  Verlaufslesezugriffe sowie die Suche nach Thumbnail-Pfaden. Die Quellen laufen
  außerhalb des UI-Threads; Bibliothek und Inhalt haben getrennte, jeweils
  begrenzte Worker. Überholte Anfragen liefern weder alte Kacheln noch alte
  Fehler an die Seite. Charakterisierungstests sichern Reihenfolge und
  Ordnernavigation; zusätzliche Tests prüfen blockierte Leser, Abbruch,
  Dispatcher-Rückkehr und erneutes Einlesen nach Fehlern. Bitmap-Dekodierung,
  Thumbnail-Cache und WPF-Darstellung bleiben in `ProjectsPage`.

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

Nach Navigation und Scan-Ausführung folgt das Thumbnail-Laden mit Cache aus
`ProjectsPage` als eigener überprüfbarer Schnitt. Danach wird der
Lebenszyklus in `AppHost` aufgeteilt; erst anschließend beginnt der Android-Umbau.
