# Studio / Atelier im Refactoring

[Zurück zum Gesamtplan](Refactoring-Plan.md)

Stand: 20. September 2026. Grundlage ist `feature/atelier` bei `fcf6ad1` plus
die laufende, noch uncommittete Arbeit an Farbbereichsmasken. Der Name im Code
und in der Navigation ist weiterhin **Atelier**. Alle Arbeitspakete unten sind
**geplant**, nicht in dieser Dokumentationsänderung implementiert oder getestet.

**Vorgezogen am 25. September:** S0–S2 kommen vor den übrigen Schnitten, weil
Projektzustand, Autosave und Maskenverlauf darauf aufbauen
([Projekte, Masken und Autosave](Projekte-und-Masken.md)). S3–S6 bleiben zurückgestellt.
Vor dem Einstieg die Befunde unten gegen den aktuellen Stand prüfen.

**Zurückgestellt nach Nutzerentscheidung vom 20. September:** Dieser Bereich
wird erst nach den übrigen Refactoring-Schritten bearbeitet. D2, D1 und der
übrige Gesamtplan hängen nicht von S0 ab. Vor dem späteren Einstieg die
Bestandsaufnahme und Befunde gegen Claudes dann aktuellen Stand prüfen.

## Was sich seit dem ursprünglichen Plan geändert hat

Atelier ist eine eigene, wiederverwendete Seite des Hauptfensters. Es besitzt
Bildquellen, Bearbeitungszustand und Ausgaben, während das Vorschaufenster weiter
dem schnellen Beurteilen dient. Ein Programm und gemeinsame Bildbausteine bleiben
sinnvoll; eine gemeinsame große Fenster- oder Editorsteuerung wäre es nicht.

Die vorhandenen Bausteine sind bereits eine brauchbare Grundlage:

| Verantwortung | Vorhandene Bausteine | Noch gekoppelt |
| --- | --- | --- |
| Lesen und Farbwerte | `FrameDecoderRegistry`, EXR-Leser, `FloatFrame`, `IViewTransform` | `AtelierPage.Open/Load/Show` verwalten Öffnungszustand und UI-Rückgaben selbst. |
| Rezept und Rechenplan | `ImageAdjustments`, `GradingStack`, `PreparedGrading`, `LayerStack`, `ImageLayer`, `LayerMask` | `Bind`, `Snapshot`, `OnToolsChanged` und `OnLayersChanged` verbinden Auswahl, Settings, Panels und Neuberechnung direkt. |
| Komposition | `LayerComposer`, `LayerGrade`, `Overlays`, `LayeredFrameLoader`, `SequenceLink` | Vorschau und Export bereiten Quellen und Frame-Kontext unterschiedlich vor. |
| Werkzeugarten | `IGradingTool`, `ILocalTool`, `IOpticsTool`, `IGeometryTool`, `IDataTool`, `IFramePass` | Kopieren, Speichern, Panelbindung und Ausführung müssen alle sechs Arten vollständig behandeln. |
| Vorschau | `FloatFrameProcessor`, grober Reglerzug, voller Durchgang nach 180 ms, A/B, Histogramm | Scheduling, zusammengesetzter Frame und WPF-Bitmap liegen in der Seite; Drag/Pinsel bündeln zusätzlich über `CompositionTarget.Rendering`. |
| Interaktion | `ToolColumn`, `PropertiesPanel`, `PlacementAdorner`, `ImageHit`, `PlacementDrag`, `LayerPanel`, `GradingPanel` | Große Panels führen Modelländerungen selbst aus; die `AtelierPage.*`-Dateien teilen weiterhin denselben Zustand. |
| Ausgabe | `GradeBatch`, `GradeVideo`, Request-/Result-Typen, ffmpeg-Anbindung | `AtelierPage.Batch` besitzt Ablauf und Abbruch; Workerzahl kommt über den statischen `AtelierPage.Workers`-Callback aus `AppHost`. |
| Speicher und Sitzung | Quell-/Thumbnail-Vorräte, wiederverwendeter Kompositionspuffer, gespeichertes Rezept/Quellbild | Die Hauptseite hält `_atelierPage`; ein expliziter Atelier-Endpfad und ein gemeinsames Ressourcenbudget fehlen. |

Die Aufteilung in `partial`-Dateien ist noch keine Trennung dieser
Verantwortungen. Umgekehrt müssen vorhandene Rechenbausteine nicht neu erfunden
oder allein wegen ihrer Größe verschoben werden.

## Abgrenzung zur laufenden Claude-Arbeit

Die aktuelle Sitzung „FrameFlip Mehrschichten und Compositing“ enthält zuletzt:

- umgesetzt im Commitstand: gemalte Masken mit sequenzweiter oder bildbezogener
  Geltung, Pinselgröße/-härte/-stärke/-deckkraft, Maskenebene, sichtbarer
  Schnittmaskenschalter und gebündelte Pinselaktualisierung;
- noch in Arbeit: Farbbereichsmaske auf dem Untergrund, Vorgaben für Tiefen,
  Mitten und Lichter sowie die zugehörigen Tests;
- als Produktwünsche geführt: weitere Glitch-Effekte, Undo und weitere
  Oberflächenarbeit. Ihr Status wird vor einem betroffenen Schnitt neu gelesen.

Die aktive Konfliktzone umfasst derzeit `LayerMask`, `LayerComposer`,
`LayerPanel.xaml*`, `PaintedMaskInvariants` und beide Sprachdateien. Auch
`AtelierPage.*`, `GradingPanel*`, `PlacementAdorner*` und `PropertiesPanel*`
sind bei weiterer UI-/Pinselarbeit vor einem Schnitt abzugleichen. Hier keine
parallele Umbenennung, Verschiebung oder Formatierung. Die Liste ist eine
Momentaufnahme, keine dauerhafte Sperre dieser Dateien.

Je Paket gilt: Feature abschließen und Commit benennen → aktuellen Diff und
betroffene Tests prüfen → Strukturänderung separat ausführen. Bestehende Fehler
mit einem Regressionstest in einem eigenen Fix behandeln, bevor das zugehörige
Verhalten als Ausgangspunkt des Refactorings festgeschrieben wird. Diese Planung
ändert weder den laufenden Code noch die Produktentwürfe.

## Zielgrenzen

Die folgenden Namen sind Arbeitsnamen. Zunächst genügen interne Klassen im
bestehenden Projekt; ein weiteres Programm, ein DI-Framework oder ein kompletter
MVVM-Umbau sind dafür nicht erforderlich.

| Geplante Grenze | Besitzt | Bleibt außerhalb |
| --- | --- | --- |
| `AtelierSourceSession` (S1) | aktive Öffnungsanfrage, Quellen/Pass-Metadaten, Gültigkeit von Rückgaben, Freigabe | WPF-Controls und Farb-/Maskenalgorithmen |
| `AtelierEditingSession` (S2) | Rezept, gewähltes Bearbeitungsziel, Änderungen und unabhängige Snapshots | Bildpuffer, Dateidialoge, Bitmap-Zeichnen |
| `AtelierExportController` (S3) | eingefrorener Auftrag, Ausgabestatus, Fortschritt, Abbruch und Ende | bestehende Bild-/Videoencoder, Dialogdarstellung |
| `AtelierPreviewController` (S4) | Zusammenfassen von Änderungen, grob/voll, aktuelle Rezept-/Quellrevision | WPF-Ereignisse und `WriteableBitmap`-Zugriff |
| Panel-Adapter (S5) | Zuordnung von Controlwerten zu gezielten Sitzungsaktionen | eigenständige Rezeptkopien und Dateilesen |

Richtung der Abhängigkeiten: WPF meldet Aktionen an Sitzungen/Controller; diese
verwenden die vorhandenen Daten- und Rechenbausteine. `Imaging/Grading` kennt
keine `AtelierPage`. Ein Snapshot enthält keine Controls oder offenen Streams.
Rezeptdaten und kurzlebige, vorbereitete Rechenpläne bleiben unterscheidbar.

## S0: Verhalten und Kompatibilität absichern

**Voraussetzung:** ein benannter, abgeschlossener Feature-Commit für die jeweils
betroffenen Dateien. Kein Warten auf alle künftigen Studio-Funktionen.

Vor Extraktionen sind drei konkrete Befunde zu bearbeiten. Sie stammen aus
statischer Codeprüfung, nicht aus einem in dieser Runde ausgeführten Testlauf:

1. **Gespeicherte Maskenkennungen.** `SettingsStore` speichert Enums derzeit als
   Zahlen. In `fcf6ad1` bedeutet `MaskKind` 5 = `Cryptomatte`, 6 = `Painted`.
   Der untersuchte Arbeitsdiff fügt `Colour` davor ein und verschiebt beide.
   Alte Rezepte würden damit anders gelesen. Vor Übernahme dieses Features die
   bisherigen Werte explizit erhalten und einen neuen freien Wert vergeben;
   Altdatei-Tests müssen die Bedeutung prüfen, nicht nur einen Roundtrip mit der
   neuen Fassung. Falls die Zwischenfassung bereits gespeichert wurde, deren
   Behandlung separat klären; nicht blind anhand derselben Zahl migrieren.
2. **Bildnummer bis in die Komposition.** Die Seite reicht `_number` an
   `LayerComposer.Compose` weiter. `LayeredFrameLoader.LoadAll`, das Bild- und
   Videoexport verwenden, ruft `Compose` bisher ohne Nummer auf; der Default ist
   0. Erst im späteren Prozessoraufruf wird die Dateinummer gesetzt. Damit fehlt
   sie bei der Wahl einer ungesperrten gemalten Maske. Eine synthetische Sequenz
   mit verschiedenen Masken für Bild 1 und 2 muss diesen Weg vor einer Korrektur
   reproduzieren und danach Vorschau und beide Ausgaben vergleichen.
3. **Überholte Öffnungen.** `Open` startet Hintergrundarbeit; `Show` übernimmt
   deren Ergebnis ohne Anfragekennung. Der Pass-Nachladeweg prüft nur den Pfad,
   nicht die Revision. Kontrolliert langsames A, schnelles B sowie A→B→A testen;
   weder alte Bilder noch alte Fehler oder Busy-Rückgaben dürfen B verändern.

**Abnahme:** ein reproduzierbarer Ausgangspunkt, Alt-Rezept-Fixtures, benannte
behobene oder noch offene Befunde und dokumentierte Ergebnisse beider vorhandenen
Desktop-Prüfreihen. Keine historische Zusicherungszahl als aktuellen Nachweis
übernehmen. Sicherheits-/Verhaltenskorrekturen bleiben eigene Änderungen.

### Stand S0 (25. September 2026)

Auf `refactor/studio-s0`, aufgebaut auf `feature/atelier` bei `bbe0e49`, **implementiert,
geprüft und am 26. September in `feature/atelier` gemergt** (#26, `94645dc`), noch nicht
in `main`:

1. **Maskenkennungen:** Die Zählung vom 18. bis 20. September (5 = Cryptomatte,
   6 = Painted) hat `main` nie erreicht. Das Atelier kam erst am 24. September mit
   PR #23, schon mit Colour = 5, Cryptomatte = 6, Painted = 7. Eine Migration entfällt
   deshalb. `PersistedEnumInvariants` schreibt die Zahlen aller 17 Aufzählungen fest,
   die von `AppSettings` aus gespeichert werden, einschließlich der abgeleiteten
   Werkzeugtypen. Angehängte Werte sind erlaubt, verschobene machen die Probe rot
   (Gegenprobe: ein eingefügter Wert vor Cryptomatte). Eine Altdatei prüft die
   Bedeutung von 5, 6 und 7 nach Laden und erneutem Speichern. Offen und bewusst nicht
   geändert: `config.json` schreibt weiter Zahlen. Die Projektdatei aus
   [Projekte und Masken](Projekte-und-Masken.md) soll Namen schreiben.
2. **Bildnummer im Export:** bestätigt und behoben (`fix:`). `LayeredFrameLoader.LoadAll`
   setzte den Stapel ohne Nummer zusammen, also las jedes Bild die Maske von Bild 0. Eine
   entsperrte gemalte Maske fand keinen Anstrich, und die Ebene wirkte überall. Der
   Knotenweg gab die Nummer schon weiter. `ExportFrameNumberInvariants` exportiert zwei
   gleiche Bilder mit links bzw. rechts gemalter Maske; der Stapelfall war vor der
   Korrektur rot.
3. **Überholte Öffnungen:** bestätigt und behoben (`fix:`). Ein langsames A nach einem
   schnellen B zeigte A, hielt B für offen und merkte sich A für den nächsten Start. Jedes
   Öffnen trägt jetzt eine Nummer. Hauptlesen, Pass-Nachladen und beide
   Miniaturwege verwerfen Rückgaben eines abgelösten Öffnens, auch bei A → B → A. Eine
   Lesenaht (`AtelierPage.Reader`) lässt die Probe das erste Lesen anhalten. Beide Fälle
   waren vor der Korrektur rot.

Abnahme: **4.550 Zusicherungen** in `FrameFlip.Tests` (ein Gesamtlauf, 105 Sekunden, Release).
`FrameFlip.UiTests` läuft in der CI des PR. Nicht Teil von S0 und weiter offen: fehlende
oder defekte Bilder und Lesefehler als eigene Abnahmefälle, sie gehören zu S1.

## S1: Öffnung und Lebenszyklus herauslösen

**Nach S0; erster struktureller Studio-Schnitt.** Ausgangspunkte:
`AtelierPage.xaml.cs` (`Open/Load/Show`), `.Layers.cs` und `.Thumbnails.cs`.

Die Quellsitzung erhält austauschbare Lesequellen und eine Anfrage-/Sitzungsnummer.
Sie liefert nur gültige Ergebnisse zurück. Die Seite übernimmt diese über ihren
Dispatcher und zeichnet. Abbruch muss neue Arbeit verhindern und verspätete
Ergebnisse entwerten, auch wenn ein Decoder selbst nicht unterbrechbar ist.
Worker und Warteschlange bleiben begrenzt, statt pro Klick unbegrenzt zu starten.

In getrennten kleinen PRs: zuerst Hauptbild-Öffnung, danach Pass-/Bildquellen und
Thumbnails, schließlich den expliziten Endpfad am Hauptfenster anschließen.
`ProjectScanService` liefert ein vorhandenes Vorbild für überholte Anfragen;
dessen Bibliothekssemantik wird nicht auf Float-Bildquellen übertragen.

**Abnahme:** A/B/A-Rennen, fehlendes/defektes Bild, Wiederholen nach Lesefehler,
Seitenwechsel und Schließen bei laufenden Lesern. Geschlossene Sitzungen liefern
keine UI-Änderungen; Timer/Rendering-Abos enden und eigene Bild-/Thumbnail-Puffer
werden freigegeben. Aus-/Einblenden einer weiterhin vorhandenen Ebene bleibt ohne
unnötiges Neulesen möglich. Rezept und Auswahl überstehen einen Seitenwechsel.
Ressourcenpolitik beim Verbergen und Exportbesitz werden in S3/S4 vervollständigt.

### Stand S1, erster Teil (25. September 2026)

Auf `refactor/studio-s1-open`, aufgebaut auf `refactor/studio-s0`, **implementiert,
geprüft und am 26. September in `feature/atelier` gemergt** (#27, `799edfa`), noch nicht
in `main`: die Hauptbild-Öffnung.

- `FrameFlip/Atelier/AtelierSourceSession` besitzt die laufende Anfrage, ihre Nummer,
  das Lesen im Hintergrund und die Zustellung auf den Oberflächenfaden. Zugestellt wird
  nur, was noch gilt. Ein Lesefehler kommt als unlesbares Bild an. Pass-Nachladen und
  Miniaturen fragen die Sitzung, ob ihr Öffnen noch gilt.
- Anzeigen, Werkzeuge, Rezept und der Vorrat gelesener Quellen bleiben bei der Seite.
- Charakterisierung vorher (`AtelierOpenInvariants`): lesbar, unlesbar (Hinweis, leere
  Fläche, nicht gemerkt), Lesefehler als Ausnahme, erneutes Öffnen nach einem Fehler.
  Dazu die Rennen aus S0. Beide liefen vor und nach der Auslagerung unverändert grün.
  `AtelierSourceSessionInvariants` prüft die Sitzung ohne Fenster: neueres Öffnen,
  A → B → A, Lesefehler, Nummern und das Ende.

Abnahme: **4.561 Zusicherungen** in `FrameFlip.Tests` (Release). Offen für die weiteren
Teile von S1: Pass- und Bildquellen samt Vorrat in die Sitzung, Miniaturen, und der
Endpfad am Hauptfenster. Die Sitzung wird heute nie beendet, daher liefert ein
Schließen bei laufendem Lesen weiter eine späte Rückgabe an die Seite.

## S2: Rezept, Auswahl und Speichern trennen

**Nach S0; Integration nach S1.** Zuerst `Bind`, `Snapshot`, `OnToolsChanged`
und `OnLayersChanged` charakterisieren, dann die Sitzung dahinter extrahieren.
„Fertiges Bild“ und „gewählte Ebene“ sind explizite Bearbeitungsziele. Das
`GradingPanel` zeigt eines davon; die finale Korrektur hängt nicht davon ab,
welches gerade angezeigt wird.

Bestehende Datenmodelle und JSON-Feldnamen bleiben zunächst erhalten.
`AppSettings` wird über einen schmalen Adapter gelesen/geschrieben. Ein eigenes
Rezept-Dateiformat, Speicherort neben der Sequenz oder ein Versionswechsel wäre
eine separate Produkt-/Migrationsentscheidung.

**Abnahme:** Wechsel Bild→Ebene→Bild verliert kein Werkzeug, einschließlich
aller sechs Werkzeuglisten. Duplikate und Export-Snapshots teilen keine
veränderlichen Werkzeuge, Gruppen, Masken oder Pinselarrays mit dem Original.
Speichern/Neuladen erhält Reihenfolge, Mischraum, Schnittmasken, Platzierung,
Datei-/Sequenzbezüge und pro Bild gespeicherte Anstriche. Die Auswahl allein
ändert weder Bild noch Histogramm. Ein konsistenter Änderungsweg kann später
Undo tragen; Undo selbst gehört nicht in diesen Refactoring-PR.

### Stand S2, erster Teil (25. September 2026)

Auf `refactor/studio-s2-editing`, aufgebaut auf `refactor/studio-s1-open`, **implementiert,
geprüft und am 26. September in `feature/atelier` gemergt** (#28, `7245027`), noch nicht
in `main`: Rezept und Speichern.

- `FrameFlip/Atelier/AtelierEditingSession` besitzt das Rezept: Grundregler und
  Werkzeuge des fertigen Bildes, Ebenenstapel, Graph. Die Seite las und schrieb diese
  Felder an rund zwanzig Stellen direkt in `AppSettings`. Jetzt läuft jeder Zugriff über
  die Sitzung. Sie meldet jede Änderung und weiß, ob seit dem letzten festgehaltenen
  Stand etwas dazukam (`Dirty`, `Revision`, `MarkKept`).
- `IAtelierRecipeStore` ist der schmale Adapter. `SettingsRecipeStore` schreibt in
  dieselben Felder wie bisher, `config.json` bleibt unverändert. Die Projektablage je
  Sequenz ([Projekte und Masken](Projekte-und-Masken.md), Phase C) wird eine zweite
  Ablage hinter derselben Schnittstelle.
- Beobachtet, nicht geändert: Die Grundregler des fertigen Bildes teilt sich das Atelier
  mit dem Vorschaufenster (`AppSettings.Adjustments`). Ob sie je Projekt gelten sollen,
  entscheidet die Projektablage.
- Charakterisierung vorher (`AtelierRecipeInvariants`): wo das Rezept nach Öffnen, Regler
  am Bild, Einstellungsebene, Rückwechsel und Umwandeln in Knoten steht und wann
  geschrieben wird. Sie lief vor und nach dem Umbau unverändert grün, ebenso
  `AtelierLayerInvariants`. `AtelierEditingSessionInvariants` prüft die Sitzung ohne
  Fenster.

Abnahme: **4.578 Zusicherungen** in `FrameFlip.Tests` (Release). Offen für den zweiten
Teil: Bild und Ebene als ausdrückliche Bearbeitungsziele, unabhängige Export-Snapshots
aus der Sitzung, die Abnahmefälle oben zu allen sechs Werkzeuglisten und den Anstrichen.

## S3: Exportauftrag und Frame-Kontext vereinheitlichen

**Nach S2, auf dem gesicherten S0-Ausgabeverhalten.** `GradeBatch` und
`GradeVideo` bleiben die vorhandenen Ausführungswege. Der Controller löst
Zustand, Request-Erzeugung, Fortschritt und Abbruch aus `AtelierPage.Batch`.

Ein Auftrag friert Rezept, Reihenfolge, Ziel, Format, FPS und Sichtumwandlung
ein. Jeder Frame trägt seine Dateinummer bereits beim Quellenladen und
Zusammensetzen mit; sie gilt für verknüpfte Sequenzen, gemalte Masken und Korn.
Renderdaten und Overlays müssen beide Ausgaben erreichen. Fortschrittsmeldungen
eines alten Auftrags dürfen keinen neuen Auftrag verändern.

**Abnahme:** gleiche Korrektur trotz anderer Panelauswahl oder späterer
Rezeptänderung; synthetische PNG- und EXR-Sequenzen mit Gruppen, Maske und
Overlay; fehlende Frames, unveränderte Quelldateien, geordnete Videoausgabe und
Abbruch. Aufträge besitzen und beenden Worker/ffmpeg auch bei geschlossenem
Fenster. Ein Seitenwechsel verliert den Auftragsstatus nicht; beim Ende der
Sitzung wird kontrolliert abgebrochen und ausstehende Arbeit beendet. Soll ein
Export später bewusst im Tray weiterlaufen, braucht er vorher einen vom Fenster
unabhängigen Besitzer und eine eigene Produktentscheidung.

Der normale `VideoExporter` nutzt einen anderen Weg für die Vorschaukorrektur.
Seine ffmpeg-Filter werden nicht als Ersatz für die Studio-Rechnung verwendet.

## S4: Vorschauplanung und Ressourcen zusammenführen

**Nach S1–S3, sobald die aktuelle Pinselinteraktion stabil ist.** Zwei Schnitte:
erst das Planen von Vorschauen, danach ihre Ressourcenanbindung.

Der Preview-Controller führt Regler-, Platzierungs- und Pinseländerungen auf
aktuelle Quell-/Rezeptrevisionen zurück. Bestehende Bündelung pro Rendering-Takt,
grobe Vorschau und voller Abschluss bleiben erhalten. WPF-Bitmapzugriffe bleiben
am Dispatcher. Eine Verlagerung der Berechnung auf Worker ist ein eigener
Schritt mit unabhängigen Rezepten/Puffern; bloßes `Task.Run(Render)` reicht nicht.

Danach den statischen `AtelierPage.Workers`-Callback durch eine instanzbezogene
Ressourcenquelle ersetzen. Studio-Vorschau, Export und Dashboard müssen als
Verbraucher sichtbar sein. Float-Quellen, Kompositions-/Zwischenpuffer,
Thumbnails und Export-Vorlauf zählen gemeinsam mit dem Viewer in eine begrenzte
Speicherplanung. Die vorhandene Lastmessung wird erweitert, nicht verdoppelt.
Beachten: Die aktuelle Workerzahl wird beim Exportstart übernommen; das ist noch
keine während des Laufs nachgeführte Drosselung.

**Abnahme:** ein schneller Regler-/Pinselzug erzeugt keine anwachsende Queue;
das letzte volle Bild entspricht dem letzten Rezept; alte Mess-/Bildrückgaben
werden verworfen. Nach Verbergen einer untätigen Seite können große Puffer
freigegeben und beim Wiederkommen geladen werden, ohne das Rezept zu verlieren.
Beim Fensterende bleibt kein unsichtbarer Export-/Timerbesitzer zurück.
Kleine synthetische Messfälle prüfen Budget und Parallelitätsgrenzen bei
gleichzeitiger Vorschau, Export und Renderlast. Änderungen der Laufzeitpolitik
separat von der reinen Extraktion ausweisen.

## S5: Panels nach stabiler Zuständigkeit aufteilen

**Nach S2/S4 und Abschluss des betroffenen UI-Features.** Zuerst die
Modellaktionen aus `LayerPanel` (Auswahl, Reihenfolge, Gruppe, Schnittmaske),
dann Maskeneditor und schließlich die Bindung der Werkzeuggruppen aus
`GradingPanel` lösen. Jeweils eigene PRs; kein gleichzeitiger Layoutumbau.

`ToolColumn`, `PropertiesPanel`, `CurveEditor`, `ColourWheel` und
`PlacementAdorner` bleiben vorhandene Controls. WPF-Ereignisse, Fokus,
Mausfang und Adorner-Zeichnung bleiben in der UI. Berechnungen wie `ImageHit`
und `PlacementDrag` sind bereits getrennt und werden weiterverwendet.

**Abnahme:** ein Werkzeug bestimmt die Bedeutung des Klicks; Pinsel malt in die
Maskenebene; Schnittmaskenschalter und Auswahl stimmen überein. Textfelder und
Regler behalten ihre Tasten; Drag, Pipette und Maskenauswahl funktionieren mit
Zoom, Beschnitt und Skalierung. XAML-Controls wirklich konstruieren und mit
synthetischem Inhalt anordnen, nicht nur kompilieren.

## S6: Rechenpipeline nur bei belegtem Bedarf weiter zerlegen

**Nach S3/S4; nachrangig.** Erst mit gesicherten Vergleichsfällen entscheiden,
ob Passplanung, Maskenauswertung oder Pufferverwaltung aus `LayerComposer` und
`FloatFrameProcessor` herausgelöst werden sollen. Die sechs Werkzeugarten
behalten ihre unterschiedlichen Ausführungsbedingungen.

Unveränderliche Verträge: EXR-Szenenlicht gegenüber dekodierten Anzeigewerten,
Sichtumwandlung genau an der bisherigen Stelle, PNG-Deckung gegenüber EXR-Alpha,
Mischraum pro Ebene, Gruppen/Schnittmasken und Overlays. `IFramePass` läuft im
vollen 8-Bit-Pfad sowohl mit als auch ohne lokale Werkzeuge, während des groben
Zugs und in `ApplyRgba64` derzeit nicht. Diese bewussten Unterschiede zunächst
absichern und dokumentieren; eine andere 16-Bit-Effektpolitik ist eine eigene
Verhaltensänderung, keine scheinbar fehlende Refactoring-Parität.

## Prüfbasis und Reihenfolge

| Paket | Vorhandene Prüfbasis in `FrameFlip.Tests` | Wichtigste Ergänzung |
| --- | --- | --- |
| S0/S1 | `AtelierLayerInvariants`, `AtelierPageInvariants`, `PaintedMaskInvariants` | Alte numerische Maskenkennungen, kontrollierte Leser-Rennen und Ende mit ausstehenden Rückgaben |
| S2 | `GradingInvariants`, `GradingGroupInvariants`, `DitherPanelInvariants`, `AtelierLayerInvariants`, `MaskInvariants` | Vollständige Zielwechsel-/Snapshot-/Altdatei-Matrix |
| S3 | `GradeBatchInvariants`, `GradeVideoInvariants`, `ImageAndGroupInvariants` | Frame 1/2 mit verschiedenen Anstrichen bis in Bild- und Videoausgabe; Abbruch bei Fensterende |
| S4 | `PlacementDragInvariants`, `PaintedMaskInvariants`, `LoadLifecycleInvariants` | Gebündelte Anfragen, verworfene Revisionen und Freigabe/Budget für Studio plus Export |
| S5 | `LayerPanelInvariants`, `PanelPolishInvariants`, `CurveEditingInvariants`, `ColourWheelInvariants`, `ImageHitInvariants` | Echte Seitenverdrahtung, Fokus und Bedienung nach dem Schnitt |
| S6 | `DitherPipelineInvariants`, `FloatImagingInvariants`, `BlendSpaceInvariants`, `PngLayerInvariants`, `RenderDataInvariants`, `StackReproInvariants` | Vorschau-/Ausgabevergleich beider vollen Pfade und dokumentierte 16-Bit-Ausnahme |

Die vorhandene Prüfbasis ist kein Beleg, dass die Ergänzungen schon abgedeckt
sind. Gerade die Rasterkorrekturen haben gezeigt, dass richtige Rechenkerne
eine verlorene Einstellung in der Seite nicht erkennen.

Für Code-PRs laufen die beiden in CI verwendeten Befehle:

```powershell
dotnet run --project FrameFlip.Tests/FrameFlip.Tests.csproj -c Release
dotnet run --project FrameFlip.UiTests/FrameFlip.UiTests.csproj -c Release
```

Lokale Prüfungen kurz und begrenzt halten; eventuelle Fenster vorher ankündigen.
Nur synthetische Testdaten und eine isolierte `FRAMEFLIP_CONFIG` verwenden,
keine privaten Medien oder persönliche Einstellungen als Testfixture. Für eine
reine Planänderung genügen Dokument-, Link- und Diff-Prüfung.

Die interne Studio-Reihenfolge ist **S0 → S1 → S2 → S3 → S4 → S5**, S6 bei
Bedarf. Sie beginnt erst am Ende des Gesamtplans, nach den übrigen vorgesehenen
Refactoring-Schritten. Vor jedem Schnitt werden Commitstand, Konfliktzone und
offene Produktentscheidungen gegen die weitergelaufene Featurearbeit aktualisiert.
