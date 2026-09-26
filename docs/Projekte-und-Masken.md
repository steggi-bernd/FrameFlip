# Projekte, Masken und Autosave – Plan

Stand: 25. September 2026, Grundlage `feature/atelier` bei `4244cb2`. Dieses
Dokument ist ein **Plan**; die Entscheidungen vom selben Tag stehen in Abschnitt 6.
Es ordnet die zwölf
Wünsche vom 25. September in den Bestand und den Refactoring-Stand ein
([Refactoring-Plan](Refactoring-Plan.md), [Studio / Atelier](Refactoring-Studio.md))
und legt fest, welche Daten sie gemeinsam benutzen.

## 1. Ausgangslage

| Bereich | Bestand | Folgerung |
| --- | --- | --- |
| Atelier-Zustand | **Ein** Rezept für alles, in `config.json`: `Grading`, `Layers`, `AtelierNodes`, `AtelierImage`. Geschrieben beim Öffnen, beim Umwandeln in Knoten und beim Beenden. | Ein Absturz verliert alles seit dem Start. Ein anderes Projekt öffnet mit dem alten Rezept. Das ist die gemeinsame Ursache von Punkt 7, 8 und 9. |
| Dashboard | Refaktoriert (D1a–D1d). `DashboardSequenceController` kennt Blend-Projekte aus der Bibliothek und Sitzungseinträge (`AddPath`). Die Sitzungseinträge werden **nicht** gespeichert. `RecentSequences` (`sequences.json`) gibt es, das Dashboard liest es aber nicht. | Punkt 8 erweitert den vorhandenen Controller um eine Quelle, statt eine zweite Liste zu bauen. |
| Atelier-Struktur | S0–S6 sind geplant, nicht begonnen. Vorgesehene Grenzen: `AtelierSourceSession` (Öffnen), `AtelierEditingSession` (Rezept, Auswahl, Snapshots), `AtelierExportController`, `AtelierPreviewController`. | Projektzustand und Autosave **sind** der Kern von S2. Sie gehören dorthin, nicht als Nebenweg in die Seite. |
| Masken | Im Knotenmodus ist eine Maske schon ein Knoten (`MaskNode`), und sein Ausgang kann an mehrere Ebenen gehen. Die gemalte Deckung steckt als gepacktes Base64 im Rezept (`LayerMask.Paint`, je Bild `PaintFrames`). Der Pinselabstand ist fest (ein Viertel des Radius) und wird im Adorner gerechnet, nicht im Modell. | Die Grundlage für eigenständige Masken ist da. Es fehlen eine feste Kennung, Befehle zum Lösen und Teilen, eine klare Darstellung und ein Verlauf. |
| Zoom | Zwei Stufen, Einpassen und 100 %, über einen `ScrollViewer`. Der Viewer hat mit `ZoomController` schon Mausrad-Zoom um den Zeiger mit Einrasten. | Die Rechnung des Viewers teilen, die Atelier-Anzeige nicht umbauen (Anfasser, Pinsel, Knoten und Bildmenü rechnen in Anzeige-Koordinaten). |

## 2. Messung: was ein Pinselstrich kostet

Synthetisches Bild 3840 × 2160, Release-Build, Knotenmodus mit einer gemalten
Maskenebene (8 Knoten).

| Schritt | Kosten |
| --- | --- |
| Ein Pinseltupfer, Radius 80 px | 0,02 ms |
| Ein Pinseltupfer, Radius 300 px | 0,08 ms |
| Graph als Text (Undo-Vergleich) | 0,1–0,2 ms |
| Maske packen beim Loslassen | 3,5 ms |
| **Bild neu rechnen, grob (je Bild während des Strichs)** | **23 ms** |
| **Bild neu rechnen, voll (nach dem Loslassen)** | **133 ms** |

Die Tupfer und damit der Pinselabstand spielen für die Geschwindigkeit fast
keine Rolle. Teuer ist das Neurechnen des ganzen Bildes bei jeder Bewegung, und
mit einer echten Datei mit vielen Passen ist es noch teurer. Der Hebel ist deshalb:

- **Nur den berührten Bereich neu rechnen.** Ein Strich ändert einen kleinen Teil der
  Maske. Solange hinter der Maske nur punktweise Knoten liegen (Mischen, Farbe,
  Kurven), ändert sich auch nur dieser Teil des Bildes. Bei Radius 80 sind das statt
  8,3 Mio. Bildpunkten etwa 40.000 je Bild. Liegt ein räumlicher Knoten dahinter
  (Unschärfe, Glare, Verschiebung), rechnet es wie bisher.
- Das volle Neurechnen nach dem Loslassen entfällt dann in der Regel, weil das Bild
  schon stimmt.
- Der Pinselabstand kommt trotzdem als Einstellung (Punkt 2), aber als Werkzeug für
  den Strich und nicht als Performance-Mittel.

## 3. Das gemeinsame Modell

### 3.1 Sequenzschlüssel

Eine Kennung für „dieses Material“, die Dashboard, Atelier, Autosave und Export
gemeinsam benutzen: Ordner, Namensmuster und Endung, also z. B.
`D:\renders\shot010\render_####.exr`. Ein Einzelbild ist Ordner plus Dateiname. Das
Muster liefert `SequenceScanner.DerivePattern` bereits. Neu ist nur, dass es als
Schlüssel weitergereicht wird statt eines beliebigen Bildpfads.

### 3.2 Atelier-Projekt

Der Bearbeitungszustand **je Sequenz** statt global:

- Modus (Stapel oder Knoten), Rezept (`Grading`, `Layers`) bzw. Graph
- das zuletzt im Atelier gewählte Bild der Folge
- gemalte Masken (im Speicher weiter eingebettet wie heute, siehe 3.4)
- nicht darin: Fensterlayout, Dock, Spaltenbreiten; die bleiben in `config.json`

Im Speicher gehört er der `AtelierEditingSession` aus S2. Die Seite zeigt und meldet
Aktionen, die Sitzung besitzt Zustand, Änderungen, Undo und „ungespeichert“. Der
Export (S3) bekommt einen eingefrorenen Snapshot daraus. Weil das Rezept im Speicher
eingebettet bleibt, ändert sich am Export und an den Rechenkernen nichts.

### 3.3 Speicherort

Vorschlag: ein Unterordner **`FrameFlip/`** im Quellordner.

```
D:\renders\shot010\
  render_0001.exr … render_0240.exr
  FrameFlip\
    render.ffproj                 Projektzustand (klein, JSON)
    render.ffdata\masks\…         Maskenraster nach Inhalt (nur neue werden geschrieben)
    render.ffdata\history\…       Maskenverlauf je Maske
    render_FrameFlip_001.png      Schnell-Exporte
```

- Ein eigener Ordner statt `FFautosave_…` direkt neben den Bildern: Die
  Ordnerbeobachtung des Dashboards reagiert nicht auf Autosaves, und Blender-Ausgaben
  bleiben sauber. So entschieden (Entscheidung 2).
- Nicht beschreibbar (Netzlaufwerk, schreibgeschützt): Ausweichen nach
  `%APPDATA%\FrameFlip\projects\<Kennung>\`, sichtbar angezeigt.
- Geschrieben wird immer über eine temporäre Datei mit anschließendem Austausch, wie
  es `SettingsStore` schon tut. Ein Absturz mitten im Schreiben zerreißt nichts.

### 3.4 Masken als eigene Elemente

Die Masken-Funktionen gehören in den **Knotenmodus**. Dort ist eine Maske bereits
ein Knoten mit einem Ausgang, der an beliebig viele Ebenen gehen kann. Der Stapel
bleibt, wie er ist: eine Maske je Ebene.

- Jede Maske bekommt eine feste Kennung (`MaskId`, neues Feld in `LayerMask`). Alte
  Rezepte bekommen sie beim Laden. Damit hängen Verlauf, „geteilt mit …“ und Duplikat
  an etwas Stabilem statt am Knoten.
- Beim Speichern wird das Maskenraster nach Inhalt (Hash) in `render.ffdata\masks\`
  abgelegt. Das Projekt verweist nur darauf. Unveränderte Masken werden beim Autosave
  nicht neu geschrieben.
- Neuer Knoten **Ausschneiden**: Bild × Maske → Bild mit Deckung. Er ist die gemeinsame
  Grundlage für „Maske mit Bildinhalt lösen“ (Punkt 3) und „Extract“ (Punkt 4).

### 3.5 Maskenverlauf

- **Erfassen während des Malens:** Der Strich wird als Befehl aufgezeichnet: Punkte
  (Position, ggf. Druck), Radius, Abstand, Härte, Fluss, Deckkraft, Malen/Radieren,
  Bildnummer. Dazu ein Bitfeld in Maskenauflösung (bei 4K 960 × 540 Zellen = 64 KB), das
  jede Zelle markiert, deren Wert sich seit dem letzten Stand geändert hat. Gezählt
  werden **markierte Zellen**. Zehnmal über dieselbe Stelle zählt einmal.
- **Neuer Stand bei ≥ 10 %** eindeutig geänderter Fläche. Danach beginnt die Zählung
  bei 0.
- **Speicherung hybrid:** Jeder fünfte Stand (und jeder nach einer Operation, die kein
  Strich ist: Füllen, Umkehren, Leeren, Wiederherstellen, globales Undo) ist ein
  vollständiger, gepackter Schnappschuss. Dazwischen stehen nur die Striche. Das
  Wiederherstellen nimmt den letzten Schnappschuss davor und spielt die Striche
  nach. Grobe Größenordnung für 20 Stände bei 4K: 4 Schnappschüsse à 25–100 KB plus
  Striche à wenige KB, also unter 0,5 MB je Maske.
- **Voraussetzung:** Das Nachspielen muss exakt dasselbe Raster ergeben. Deshalb zieht
  die Strichrechnung (Zwischenschritte, Abstand) aus dem Adorner ins Modell
  (`PaintedMask`). Der Adorner liefert nur noch Punkte. Eine Probe vergleicht Live-Strich
  und Nachspielen Byte für Byte.
- Das globale Strg+Z bleibt unabhängig davon. Setzt es eine Maske zurück, markiert der
  Verlauf das als Operation ohne Strich, und der nächste Stand wird ein Schnappschuss.

### 3.6 Datenfluss

```
Dashboard-Liste ──Auswahl──▶ Arbeitsbereich (aktueller Sequenzschlüssel, Bild)
      ▲                              │
      │ neuer Eintrag                ├──▶ Dashboard: Vorschau dieser Sequenz
      │                              └──▶ Atelier: Projekt laden (erst beim Anzeigen)
Atelier öffnen / Ordner ablegen ─────┘          │
                                                ▼
                        AtelierEditingSession (Zustand, Undo, ungespeichert)
                           │ Änderung                     │ Snapshot
                           ▼                              ▼
                  Projektablage (Autosave,         Export (Schnell-Export
                  Knopf „Speichern“)               und Stapellauf)
```

Der „Arbeitsbereich“ ist ein kleiner Controller am Hauptfenster, nach dem Muster der
D1-Controller. Er hält den gewählten Sequenzschlüssel und verhindert Schleifen: Wählt
das Dashboard, was das Atelier ohnehin zeigt, passiert nichts.

## 4. Die zwölf Punkte

**1. Zoom im Verschieben-Modus.** Das Mausrad zoomt um den Punkt unter dem Zeiger.
Beim Herauszoomen rastet es unterhalb der Einpassung zurück auf „Einpassen“, also
zentriert. Nur beim Werkzeug Verschieben (und beim Pinsel, Punkt 2), nicht bei Knoten
oder Auswahl. Die Stufen und das Einrasten (100 %, Einpassen) kommen aus der Rechnung
des Viewer-`ZoomController`, als gemeinsame kleine Hilfsfunktion statt einer Kopie.
Anfasser, Pinsel und Bildmenü rechnen weiter in Anzeige-Koordinaten und bleiben
unberührt. *Abnahme:* Der Punkt unter dem Zeiger bleibt beim Zoomen stehen,
Einrasten bei 100 % und beim Einpassen, andere Werkzeuge reagieren nicht aufs Rad.

**2. Zoom und Pinselabstand beim Malen.** Rad = Zoom, Strg + Rad = Abstand (5–200 %
des Radius, Standard 25 % wie heute), live am Pinselring angezeigt wie Größe und
Härte. Der Abstand ist eine Pinseleinstellung neben Größe und Härte und wird mit dem
Strich aufgezeichnet (3.5). Performance siehe Abschnitt 2: Bereichsrechnung im
Graphen, danach dasselbe für den Stapel. *Abnahme:* Bereichsrechnung ergibt
bildpunktgenau dasselbe wie die volle Rechnung, gemessen an 4K. Mit räumlichem Knoten
hinter der Maske wird voll gerechnet.

**3. Masken ausbauen.** Rechtsklick auf eine Maske (Knoten oder Maskenbild in der
Ebenenliste):
- *Maske lösen:* Die Maske wird von der Ebene getrennt und bleibt als freie Maske
  stehen. Die Ebene ist danach unmaskiert.
- *Maske mit Bildinhalt lösen:* Der durch die Maske sichtbare Teil wird eine
  zusätzliche Ebene obendrauf (Ausschneiden-Knoten). Die Originalebene behält ihre
  Maske. Das ist derselbe Weg wie Extract in Punkt 4 (Entscheidung 4).
- *Maske duplizieren:* Die Maske bleibt, eine Kopie mit neuer `MaskId` steht frei daneben.
- *Mit Ebene verbinden …:* Liste der Ebenen, an die sie zusätzlich soll.

Freie Masken erscheinen in der Ebenenliste in einem eigenen Abschnitt „Masken“.

**4. Extract / Schnittmaske.** „Als Ebene ausschneiden“ aus einer gemalten Maske, einer
Kryptomatte-Auswahl oder direkt im Bildmenü („Objekt hier als Ebene“): Ausschneiden-
Knoten aus dem Quellbild und der Maske, dann ein eigener Platz- und Mischknoten ganz
oben. Das Original bleibt unverändert, die neue Ebene lässt sich verschieben, bearbeiten
und maskieren wie jede andere. *Abnahme:* Ohne weitere Änderung ist das Bild vorher und
nachher gleich. Verschieben der neuen Ebene bewegt nur den ausgeschnittenen Teil.

**5. Passes beim Import erkennen.** Die vorhandene Pass-Kenntnis (`PassStack`: Licht
addiert, Farbe multipliziert) wird zu einer Rollen-Erkennung erweitert: Glare, Bloom,
Streaks, Fog Glow, Emit, Env → Addieren (bei 8-Bit-PNG Negativ multiplizieren), AO und
Schatten → Multiplizieren. Ohne erkennbaren Namen als Zweitprüfung: überwiegend
schwarz und ohne Alpha → Addieren. Das gilt **nur beim Anlegen** der Ebene (Stapel,
Knoten, Hub, Ablegen). Danach ist der Modus ein normaler Wert, den nichts
neu bewertet. Duplizieren übernimmt den aktuellen Modus. *Abnahme:* Nach manuellem
Umstellen überlebt der Modus Speichern, Laden, Umwandeln in Knoten und Duplizieren.

**6. Export.** Neben dem Export-Knopf ein **Schnell-Export** und der vorhandene
Zielordner-Knopf als optionale Abweichung. Der Schnell-Export schreibt ohne Dialog
nach `<Quellordner>\FrameFlip\`:
- Einzelbild (keine Sequenz erkannt): `render_FrameFlip_001.png`, dann `_002`, …
- Sequenz: ein Ordner `render_FrameFlip_001\` mit allen Bildern; bei Video
  `render_FrameFlip_001.mp4`.

Die Nummer ist die nächste freie. Es wird nie überschrieben (Anlegen mit
„nur wenn neu“, nicht erst prüfen, dann schreiben). Geht in S3 auf: Der
Exportauftrag bekommt Ziel und Namensregel eingefroren.

**7. Projekte, Sequenzen, Übersicht und Atelier verbinden.** Über den Arbeitsbereich
(3.6): Die Auswahl im Dashboard bestimmt das Projekt im Atelier. Das Atelier lädt beim
Anzeigen das Projekt dieser Sequenz und das Bild, auf dem das Dashboard steht. Innerhalb
desselben Projekts behält das Atelier sein eigenes Bild. Umgekehrt wählt ein im
Atelier geöffnetes Bild seine Sequenz im Dashboard. Vor jedem Wechsel wird
ungespeicherte Arbeit weggeschrieben.

**8. Ordner als Sequenz registrieren.** Ein Ordner, der in die Sequenzliste gezogen
oder über Projekte bzw. Atelier geöffnet wird, wird dauerhaft ein Eintrag.
`DashboardSequenceSources` bekommt dafür eine dritte Quelle (die bisherigen
`RecentSequences`), `AddPath` schreibt sie fort. Entfernen per Rechtsklick
(„Aus der Liste“), ohne dass Dateien angefasst werden.

**9. Autosave.** Nach einer Änderung wird mit kurzer Verzögerung gespeichert (etwa 2 s
nach der letzten Änderung, sofort beim Loslassen eines Pinselstrichs, beim
Projektwechsel und beim Beenden). Geschrieben wird nur, was sich geändert hat: das
kleine Projekt-JSON und neue Maskenraster. Beim Öffnen einer Sequenz wird ihr Projekt
geladen, falls vorhanden. Der Knopf **Speichern** schreibt sofort und zeigt „Gespeichert
14:32“. Einmalige Übernahme: Das heutige Rezept aus `config.json` wird beim ersten
Start das Projekt des zuletzt geöffneten Bildes (mit Sicherungskopie der alten Datei).

**10. Maskenverlauf.** Wie in 3.5. Rechtsklick auf eine Maske → „Maskenverlauf …“ mit
Vorschaubildern der Stände. Wiederherstellen ist selbst ein Schritt im globalen Undo.

**11. Masken im Graphen sichtbar machen.**
- Maskenkabel in eigener Farbe und gestrichelt, damit sie sich von Bild- und
  Wertkabeln abheben.
- Ein Mischknoten mit Maske zeigt ein kleines Maskenbild mit Namen im Kopf.
- Ein Maskenknoten zeigt ein Vorschaubild, „→ 3 Ebenen“, wenn er geteilt ist, und „frei“,
  wenn er nirgends steckt.
- Eine gewählte Maske hebt alle Ebenen hervor, die sie benutzen, im Graphen und in
  der Ebenenliste.

**12. Einstellungen.** Entwurf A ist gewählt: Kategorien in einer Seitenleiste links,
der Inhalt zentriert mit begrenzter Breite, die beiden QR-Codes als gleichwertige
Karten nebeneinander. Die Seite liegt in `SettingsEditor.xaml` mit `SettingGroup` und `SettingRow`.
Die zwei QR-Codes sind die App-Kopplung (heute in den Einstellungen) und die
Zuschauerseite (heute nur im Dashboard). Vor dem Umbau `ui/shell-overhaul` abgleichen.

*Umgesetzt (26. September, Phase E):*

- **Abgleich mit `ui/shell-overhaul`:** Der Zweig ist weitgehend überholt.
  Titelleiste, Bedienskalierung und ziehbare Aufteilung kamen über „Das Dashboard wird
  die Hauptoberfläche“ schon in `feature/atelier`. Die Einstellungen berührt er nur mit
  einer Stilzeile für die Fensterschale. Übrig ist allein der Fensterstil für Dialoge,
  und der hat mit dieser Seite nichts zu tun.
- **Vorbereitung (`refactor/watch-card`):** Die Logik der Zuschauerkarte zog aus dem
  Hauptfenster in `WatchCard`, mit Charakterisierung vorher. So zeigen Dashboard und
  Einstellungen dieselbe Karte ohne zweite Kopie.
- **Seite (`feature/einstellungen`):**
  - Die Abschnitte stehen als Leiste links, der Inhalt steht mittig daneben, höchstens
    760 Punkte breit, mit Titel und Untertitel. Bei schmaler Seite rückt die Leiste
    nach oben.
  - „Fernsteuerung“ heißt jetzt „Verbindungen“. Oben stehen zwei gleichwertige Karten,
    App koppeln und Zuschauerseite, nebeneinander oder bei wenig Platz untereinander.
    Darunter folgen Relay-Server, Fernsteuerung und Bibliothek.
  - Der Schalter der Zuschauerseite wirkt sofort, wie im Dashboard, und fragt vorher
    nach der Zustimmung. „Übernehmen“ geht vom neuesten Stand aus und dreht ihn nicht
    zurück.
  - Gruppenüberschriften, die nur den Seitentitel wiederholten, sind entfallen.

Offen: Die Seite „Arbeitsbereich“ hat ihre eigene Einleitung und eine Gruppe gleichen
Namens. Sie bleibt, wie sie war, weil das Panel nur hier hängt und sein Umbau ein
eigener Schritt wäre.

*Nachgebessert (26. September, Rückmeldung „zu viel Platz, zu mau“, Entwurf 2):*

- **Übersicht** als erster Abschnitt:
  - Oben stehen vier Zustandsfelder: Handy, Blender-Brücke, Zuschauer und
    Vorschau-Speicher.
  - Darunter hat jede Kategorie eine Kachel mit dem, was man am häufigsten ändert.
  - Die Regler der Kacheln sind an dieselben Felder gebunden wie auf den Kategorieseiten.
    Es gibt keinen zweiten Stand, und „Übernehmen“ nimmt beides.
  - Ein Klick auf eine Kachel öffnet ihre Kategorie.
- **Breite wächst mit:** Die Übersicht wird bis 1320 Punkte breit, mit ein bis drei
  Kachelspalten. Die Kategorieseiten werden bis 960 breit, weil eine Zeile mit
  Beschriftung links und Regler rechts über mehr Breite nicht besser wird.
- **Skalierung „Nach Bildschirm“:**
  - 1080 Punkte Höhe ergeben 100 %, 1440 etwa 115 %, 4K mit 100 % ergibt 135 %.
  - Sie ersetzt die eine grobe Stufe für große Fenster und folgt dem Bildschirm beim
    Verschieben.
  - Strg+Plus/Minus geht vom automatischen Wert aus in feste Schritte über.
  - Ist die Automatik aus, bleibt alles wie bisher.
- **Befunde:**
  - Die Einheit „fps“ stand fest im Stil der Auswahllisten, also auch hinter Sprache
    und Kanal. Behoben.
  - Die Einstellungstests aus #32 lasen die `desktop-layout.json` des Nutzers, weil der
    Editor ohne eigenes Layout gebaut wurde. Geschrieben haben sie nichts. Behoben: Die
    Tests laufen in einem eigenen Ordner.
  - Die UI-Testreihe scheitert gelegentlich an „Arrow key on the viewer advances exactly
    one frame“, auch auf dem unveränderten Stand. Das ist ein Zeitproblem beim
    Dekodieren. Notiert, nicht geändert.

## 5. Reihenfolge

Jede Phase besteht aus eigenen Zweigen und PRs. Für die Strukturteile gelten die Regeln
aus dem Refactoring-Plan: Charakterisierung, Fixes, Auslagerung, Plan.

| Phase | Inhalt | Warum hier |
| --- | --- | --- |
| **A** | Zoom (1), Pinselabstand und Rad (2), Bereichsrechnung beim Malen, Pass-Rollen (5) | Hängt am gemeinsamen Modell nicht. Spürbar sofort. |
| **B** | Studio S0 (Maskenkennungen als Zahlen, Bildnummer im Export, überholte Öffnungen), S1-Teil Öffnung, S2 `AtelierEditingSession` mit Ablage-Adapter | Fundament für 6–10. **Zieht S0–S2 vor**, entgegen der Reihenfolge vom 20. September (Entscheidung 1). |
| **C** | Projektablage und Autosave (9), Übernahme aus `config.json`, Sequenzeinträge dauerhaft (8), Arbeitsbereich Dashboard ↔ Atelier (7), Schnell-Export (6) | Alles auf dem Sequenzschlüssel und der Ablage aus B. |
| **D** | `MaskId`, Maskenmenü (3), Ausschneiden-Knoten und Extract (4), Darstellung (11), Maskenverlauf (10) | Der Verlauf braucht die Ablage aus C und die Strichrechnung im Modell. |
| **E** | Einstellungen (12) nach gewähltem Entwurf | Unabhängig, aber nach dem Entwurf. |

### Stand

**Phase A ist umgesetzt** (25. September, `20e3ec2`, `cccf81c`, `85bff41`):

- Das Mausrad zoomt beim Verschieben und beim Pinsel um den Zeiger, hält bei 100 % und
  rastet beim Herauszoomen eingepasst ein. Die Stufen teilt das Atelier mit dem
  Vorschaufenster (`ZoomSteps`).
- Pinselabstand 5–400 % des Radius, per Strg + Rad und als Regler. Die Tupfer setzt
  jetzt `PaintStroke` im Modell, im Abstand entlang des Weges. Ein Zug lässt sich Byte für
  Byte nachspielen, womit die Voraussetzung für den Maskenverlauf (3.5) erfüllt ist.
- Pass-Rollen beim Anlegen (`PassRoles`, `ImageProbe`), nur beim Anlegen.
- Beim Malen im Knotenmodus wird nur der Ausschnitt unter dem Pinsel voll aufgelöst neu
  gerechnet (`GraphEvaluator.RenderRegion`): bei 4K und Radius 80 4,6 ms statt 33 ms für
  das ganze Bild grob. Räumliche Knoten hinter der Maske schalten auf den bisherigen Weg
  zurück, Optik (Vignette, Korn, Dither) rechnet mit.
- Nachbesserung nach „ruckelt immer wieder mal“: kein Undo-Vergleich mehr je Mausmeldung,
  kein ganzes Bild direkt nach dem Loslassen (es folgt, wenn der Pinsel 0,7 s ruht), ein
  eigener Feldvorrat für die Ausschnitte. Bei 4K mit sechs Masken: 0,013 ms je
  Mausmeldung, 3,1 ms je Takt, 1,1 ms beim Loslassen.

Offen aus Phase A:

- Das ganze Bild nach dem Malen (bei 4K rund 170 ms) läuft noch auf dem
  Oberflächenfaden. Setzt man genau dann wieder an, stockt der Anfang des Strichs. Das
  gehört zur Vorschauplanung aus S4 (Rechnen außerhalb des Oberflächenfadens).
- Die Bereichsrechnung gibt es nur im Knotenmodus, im Stapel noch nicht.
- Befund, nicht geändert: Im Knotenmodus landet eine Bilddatei, die bei ausgeblendetem
  Graphen aufs Bild gezogen wird, im Ebenenstapel statt im Graphen.

**Phase B** (Studio S0, S1 erster Teil, S2 erster Teil) steht in
[Refactoring-Studio](Refactoring-Studio.md).

**Phase C ist umgesetzt** (26. September, Zweig `feature/projekte` auf S2):

- **Projektdatei je Sequenz** (9): `<Quellordner>\FrameFlip\<name><endung>.ffproj`, bei
  nicht beschreibbarem Ordner unter den Einstellungen in `projects\`. Autosave 2 s nach
  der letzten Änderung, beim Wechsel der Folge, wenn die Seite geht und beim Ende; dazu
  „Speichern“ in der Kopfzeile und Strg+S, daneben der Stand. Geschrieben wird im
  Hintergrund, prozessweit der Reihe nach; Lesen wartet darauf. Aufzählungen stehen
  als Namen in der Datei.
- **Einmalige Übernahme:** Das erste Projekt ohne Datei bekommt das bisherige Rezept aus
  `config.json` (`AtelierRecipeMoved`). Die Felder dort bleiben als Sicherung stehen.
- **Knotenmodus je Projekt:** Er richtet sich nach dem Projekt; es gibt einen Weg zurück
  in den Stapel für eine Folge ohne Graphen. Rückgängig gilt je Projekt.
- **Sequenzliste** (8): Früher geöffnete Ordner stehen nach den Blend-Projekten in der
  Liste, auch nach einem Neustart. Ordner und Bilder lassen sich auf die Liste ziehen;
  „Aus der Liste nehmen“ im Rechtsklick. Erweitert den vorhandenen
  `DashboardSequenceController` um zwei Quellen.
- **Arbeitsbereich** (7): Beim Start wählt die Übersicht die Folge des Ateliers. Beim
  Wechsel ins Atelier öffnet es das Bild der Übersicht, wenn diese eine andere Folge zeigt;
  bei derselben Folge behält es sein Bild. Ein im Atelier geöffnetes Bild wählt seine
  Folge in der Übersicht und bleibt in der Liste.
- **Schnell-Export** (6): ohne Dialog in den Ordner `FrameFlip` oder den gewählten
  Zielordner, nächste freie Nummer, nie überschreibend. Einzelbild als Datei, Sequenz als
  Ordner, Video als Datei.

Entschieden beim Bauen, bitte prüfen:

- **Eine neue Folge beginnt frisch**, nicht mit dem Rezept der vorigen. Ein Rezept von
  einer Folge auf die nächste zu übernehmen wäre ein eigener Befehl („Rezept übernehmen
  von …“), noch nicht gebaut.
- **Die Grundregler des Ateliers gelten je Projekt.** Das Vorschaufenster behält seine
  eigenen in `config.json`; bisher teilten sich beide dasselbe Feld.
- **Maskenraster stehen vorerst in der Projektdatei selbst**, nicht getrennt nach Inhalt
  wie in 3.3 geplant. Eine Maske sind bei 4K 25–100 KB, das Schreiben läuft im
  Hintergrund. Die Trennung kommt mit dem Maskenverlauf in Phase D, der denselben Ordner
  braucht.
- Der normale Export verlangt weiter einen gewählten Zielordner; nur der Schnell-Export
  hat einen voreingestellten.

**Phase D ist umgesetzt** (26. September, Zweig `feature/masken` auf Phase C,
Einzelheiten in [Atelier-Nodes](Atelier-Nodes.md), Abschnitt 15):

- **Maskenmenü** (3): Als Ebene ausschneiden, Maske lösen, Maske duplizieren (neue
  Kennung, frei), Mit Ebene verbinden; bei gemalten Masken „Maskenverlauf …“. Im Bildmenü
  im Knotenmodus „Objekt hier als Ebene“ je Kryptomatte. Jede Maske hat eine feste
  Kennung (`LayerMask.Id`).
- **Ausschneiden** (4): neuer Knoten „Ausschneiden“ (Bild × Maske → Bild mit Deckung),
  darüber ein eigenes Mischen direkt über der ersten Ebene der Maske, bei einer freien
  Maske oben auf den Ebenen. Die Originalebene behält ihre Maske. Das Bild bleibt Byte für
  Byte gleich, auch an weichen Rändern und unter jeder Mischart.
- **Darstellung** (11): Maskenkabel gestrichelt in eigener Farbe, ein Schild mit
  Maskenbild und Namen über jedem Mischen mit Maske, „frei“ und „→ n Ebenen“ am
  Maskenknoten, eine gewählte Maske hebt ihre Ebenen im Graphen und in der Liste hervor.
  Freie Masken stehen in der Ebenenliste im Abschnitt „Masken“.
- **Maskenverlauf** (10) wie in 3.5: 10 % eindeutig geänderte Fläche je Stand, höchstens
  20 Stände, jeder fünfte ein Schnappschuss, dazwischen Striche. Gespeichert in
  `FrameFlip\<name><endung>.ffdata\verlauf\`, je Maskenkennung (bei entsperrten Masken
  je Bild). Wiederherstellen ist ein Schritt im globalen Rückgängig. Aufzeichnen kostet
  beim Loslassen 0,9 ms bei 4K.

Entschieden beim Bauen, bitte prüfen:

- **Ausgeschnitten wird, was an der Stelle zu sehen ist**, nicht das Bild der Ebene
  allein. Nur so ist das Bild vorher und nachher gleich (Abnahme aus Punkt 4). Die Ebene
  selbst noch einmal darüberzulegen hätte sie an weichen Rändern verdoppelt, das Bild der
  Datei hätte verloren, was die Ebenen darunter daraus gemacht haben.
- **Die neue Ebene liegt direkt über der Ebene der Maske**, nicht ganz oben. Ebenen
  darüber wirken weiter auf beide. Eine freie Maske kommt oben auf die Ebenen.
- **„→ n Ebenen“ zählt auch ausgeschnittene Ebenen** mit, „Maske lösen“ nimmt die Maske
  aber nur aus dem Faktor ihrer Ebenen. Die ausgeschnittene Ebene hängt weiter an ihr.

- **Verschieben** (Abnahme aus Punkt 4, nachgereicht am selben Tag): Der
  Ausschneiden-Knoten hat eine eigene Lage. Die Maske wählt an der alten Stelle aus, das
  Gewählte wandert, und darunter bleibt das Original. Ist die ausgeschnittene Ebene
  gewählt, zieht der Greifrahmen beim Verschieben ihr Stück, als ein Schritt im Verlauf.
  Versetzt rechnet das Malen dahinter wieder voll, weil der Knoten dann außerhalb des
  Ausschnitts liest.

**Gemergt am 26. September:** Die Phasen B bis E sind in `feature/atelier`, noch nicht in
`main`. Dazu gehören Studio S0–S2 (#26–#28), Projekte (#29), Masken (#30), `WatchCard`
(#31) und die Einstellungen (#32). Phase A war schon vorher dort.

Offen aus Phase D:

- Die Maskenraster stehen weiter in der Projektdatei, nicht nach Inhalt in `.ffdata`
  (3.4). Nur der Verlauf liegt dort. **Ans Ende der Liste gestellt** (Entscheidung 7).
- Befund, nicht geändert: Die UI-Testreihe merkt sich in ihren Testdaten die zuletzt
  geöffnete Folge (`sequences.json`). Ein zweiter Lauf in denselben Ausgabeordner beginnt
  deshalb mit geladener Folge und scheitert an „Playback controls wait for a loaded
  sequence“. In einem frischen Ordner, wie in der CI, läuft sie durch.

## 6. Entscheidungen

Getroffen am 25. September 2026:

1. **S0–S2 werden vorgezogen.** Autosave und Projektzustand bauen auf der
   `AtelierEditingSession` auf, nicht daneben. Die Schnitte folgen den Regeln des
   Refactoring-Plans (eigene Zweige, erst Charakterisierung).
2. **Speicherort ist der Unterordner `FrameFlip/`** im Quellordner, mit Ausweichen nach
   `%APPDATA%` bei nicht beschreibbaren Ordnern.
3. Wiederherstellen **automatisch** ohne Nachfrage (Empfehlung, ohne Widerspruch).
4. **„Maske mit Bildinhalt lösen“ legt eine zusätzliche Ebene an.** Die Originalebene
   behält ihre Maske. Damit fällt es mit Extract aus Punkt 4 zusammen.
5. Masken-Funktionen **nur im Knotenmodus** (Empfehlung, ohne Widerspruch).
6. **Einstellungen: Entwurf A** (Seitenleiste, zentrierter Inhalt, QR-Karten nebeneinander).

Getroffen am 26. September 2026:

7. **Das Auslagern der Maskenraster (3.4) kommt ans Ende der Liste.** Der Nutzen ist bei
   den heutigen Größen klein. Das Risiko, dass Projekt und Raster auseinanderlaufen, ist
   dagegen ein Datenverlust. Neu bewerten, wenn entsperrte Masken je Bild über lange
   Folgen die Projektdateien groß machen.
8. **`main` erst nach dem Ausprobieren.** Die Phasen gehen zuerst nach `feature/atelier`.
   #25 (nach `main`) folgt, wenn der vereinte Stand an echten Projekten erprobt ist.
9. **Maskenverlauf ab 1 %** geänderter Fläche und von Ebene und Pinsel aus erreichbar
   (#33).
10. **Einstellungen: Entwurf 2**, eine Übersicht mit Zustandsfeldern und Kacheln, dazu
    die Skalierung nach Bildschirm.
