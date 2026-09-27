# Atelier: Arbeitsablauf und Bearbeitungsziel

Stand 27. September 2026. Analyse zum Nutzerbericht mit den Punkten 1 und 3–20. Die
Entscheidungen und die Reihenfolge stehen in Abschnitt 8; begonnen wird mit Phase A.

Leitlinie des Nutzers: **Intern knotenbasiert, im Alltag „Auswahl → Aktion → Ergebnis“.**
Wer ein Auto farblich korrigieren will, wählt das Auto und sagt „Farbe korrigieren“.
Die Knoten dafür entstehen, verbunden, an der richtigen Stelle.

## 1. Kurzfassung

- **Es gibt kein gemeinsames „woran arbeite ich gerade“.** Sieben voneinander unabhängige
  Felder sagen jeweils einem Teil der Oberfläche, wohin ein Klick, ein Regler oder ein
  Strich geht. Jedes braucht seinen eigenen Rückweg beim Wechsel von Bild, Projekt oder
  Modus. Zwei davon fehlen heute (Abschnitt 4.1).
- **Die Bausteine für „Auswahl → Aktion“ gibt es schon:** Maskenebene anlegen
  (`AddNodeMaskLayer`), in den Zweig einer Ebene einfügen (`AddIntoLayer`),
  Objektmaske (R4), Knotenmenü mit Ziel (`ShowNodeHub(into:)`), Betrachter für einen
  Knotenausgang (`_viewer`). Was fehlt, ist das eine Ziel, nach dem sie sich richten.
- **Vorschlag:** ein Bearbeitungsziel in `AtelierEditingSession`. S2 hat genau das als
  zweiten Teil offen („Bild und Ebene als ausdrückliche Bearbeitungsziele“). Kein
  paralleles System: Die heutigen Auswahlen werden Ansichten dieses einen Ziels.
- **Pixel Sort rechnet richtig**, gemessen am Testbild: Stapel und Knoten gleich. Es wirkt
  aber in Grundstellung gar nicht, beim Ziehen nicht und in der 16-Bit-Ausgabe nicht.
  Dasselbe „tut nichts nach dem Hinzufügen“ gilt für alle 25 Effekte und Korrekturen der Palette.
- **Fehler 1 ließ sich im einfachen Ablauf nicht nachstellen.** Die Probe dazu ist grün.
  Gefunden wurden drei echte Lücken, eine davon passt zum Bericht (Abschnitt 4.1).

## 2. Welche Zustände es heute gibt

| Zustand | Wo | Sagt | Beim Wechsel von Bild/Projekt |
|---|---|---|---|
| Rezept (Stapel, Grundregler, Werkzeuge, Graph) | `AtelierEditingSession` (S2) | was gerechnet wird | je Projekt getauscht, geprüft |
| Projekt, Speicherstand | `AtelierProjectKeeper` | welche Datei | getauscht, geprüft |
| Maskenverläufe | `MaskHistoryKeeper` | Pinselschritte je Maske | je Projekt (`Enter`) |
| Gewählte Ebene im Stapel | `LayerPanel._selected`, gespiegelt in `_editing` | wohin Regler und Pinsel gehen | neu beim Laden des Stapels |
| Gewählter Knoten | `NodeEditor.Selected`, gespiegelt in `_shownNode` | wohin Regler, Pinsel und Kryptomatte-Klick gehen | mit dem Graphen |
| Ebene in der Knotenliste | `_layerFocus` | wohin ein Effekt aus der Liste geht | bei Knotenwahl gelöscht |
| Kryptomatte wählen | `_picking` (Stapel) und gewählter Maskenknoten (Knoten) | was ein Klick ins Bild tut | `StopPicking` beim Laden |
| Objektmaske wählen (R4) | `_objectPick` | Klick legt Objekt in Ebenenmaske | `EndObjectMask` beim Öffnen |
| Tonwert-Pipette (W2b) | `_levelsPick` | Klick setzt Schwarz/Grau/Weiß | **bleibt stehen** |
| Betrachter | `_viewer` | welcher Knotenausgang groß zu sehen ist | geleert |
| Mauswerkzeug | `_tool` | Grundbedeutung eines Klicks | bleibt (gewollt) |
| Pinseleinstellungen | `PropertiesPanel` | Größe, Härte, Form, Stempel | bleiben (gewollt, Werkzeug) |

Global gehalten wird kein Projektzustand. Statisch sind nur zwei Bequemlichkeiten im
Knotenmenü: zuletzt benutzte Knoten und Kategorie.

Das eigentliche Problem ist die Verteilung. Welches Ziel gilt, ergibt sich aus einer
Kette von Fragen: Stapel oder Knoten? Ist ein Knoten gewählt? Ist er ein Maskenknoten?
Ist eine Ebene in der Liste fokussiert? Ist ein Wählmodus an? Jede Stelle, die das
wissen muss (`PaintTarget`, `PickAt`, `Bind`, `ShowNodeSettings`, `ChosenLayer`),
beantwortet die Kette selbst, und nicht alle gleich.

## 3. Moduswechsel: nötig oder nur Oberfläche

**Technisch nötig** ist, dass ein Klick ins Bild eine Bedeutung hat. Malen, Rahmen
ziehen, Zuschneiden und Verschieben der Ansicht schließen sich aus. Das sind die
Werkzeuge Pinsel, Verschieben, Zuschneiden und Hand.

**Nur durch die Oberfläche entstanden:**

- **Vier Arten, ins Bild zu tippen:** Auswählen (Kryptomatte), Pipette, Objektmaske-Chip
  und Tonwert-Pipette sind alle „lies an dieser Stelle etwas ab und gib es dem Ziel“.
  Heute sind es vier Zustände mit vier Enden. Mit einem Ziel wird es eine Geste:
  Der Klick fragt das Ziel, was es mit der Stelle anfängt.
- **Das Werkzeug „Knoten“:** Die Knoten liegen nur über dem Bild, solange ihr Werkzeug
  gilt. Wer malen will, wechselt zum Pinsel, und die Knoten verschwinden. Das ist der
  Hin und Her zwischen Verschieben, Knoten, Ebenen und Farbe aus Punkt 6. Knoten sind
  eine Ansicht, kein Werkzeug.
- **Kryptomatte im Knotenmodus:** Ein Klick wählt nur, wenn vorher ein Maskenknoten
  gewählt ist. Sonst passiert nichts, ohne Hinweis (`PickAt` gibt `false`).
- **Stapel gegen Knoten:** zwei Modelle derselben Sache. Die Umwandlung geht nur in eine
  Richtung. Die Alltagsaktionen müssen in beiden gleich heißen und gleich wirken. Intern
  bleiben es zwei Rechenwege, bis der Stapel nur noch eine Ansicht des Graphen ist. Das
  ist ein eigenes, späteres Vorhaben und nicht Teil dieses Plans.

**Automatisierbar**, weil die Absicht aus Ziel und Aktion folgt:

- Maskenebene samt Mischen und Korrektur anlegen, wenn auf eine Auswahl ein Filter kommt.
- Den Zweig einer Ebene finden und den Filter hinter ihre Korrektur hängen.
- Datenpässe (Tiefe, Bewegung, Normalen) anschließen. Das gibt es schon (`WireData`).
- Den Betrachter auf die Maske stellen, solange an ihr gemalt wird, als Angebot.

## 4. Befunde, geprüft

### 4.1 Maske aus einem anderen Projekt (Punkt 1)

**Nachgestellt, nicht reproduziert.** Die Probe `MaskCarryInvariants` (noch nicht
eingecheckt, gehört in den Fix-Schnitt) nimmt die echten Maustasten des Pinselrahmens:

1. Projekt A im Knotenmodus, ein Strich legt eine gemalte Maske an.
2. Ein neues Bild in einem anderen Ordner, im Stapel, der Pinsel bleibt in der Hand.
3. Ein Strich an anderer Stelle.

Ergebnis:

- Der Strich landet in einer neuen Maskenebene des neuen Projekts.
- Die alte Maske ist nicht mitgekommen, der neue Strich nicht in die alte gegangen.
- Aus dem Stapel heraus gilt dasselbe.

Die bestehende `AtelierProjectPageInvariants` prüft Stapel und Graph beim Wechsel ebenfalls.

**Testordner:** Die Projekte in `passes` und `passes png` haben gemalte Masken mit
gleicher Prüfsumme. Beide sind aber leer (kein gedeckter Punkt), das ist also kein
Beleg. Maskendaten wurden dafür nicht ausgegeben, nur Zählwerte.

**Gefundene Lücken, jede mit eigenem Rückweg-Fehler:**

1. **Objektgebundener Pinsel** (`AtelierPage.BrushLimit.cs`):
   - Die Deckung des Objekts wird gemerkt nach Kryptomatte, Objektkennung und
     Bildnummer, **ohne Datei**.
   - Kryptomatte-Kennungen sind Hashes der Objektnamen. Ein Objekt gleichen Namens in
     einem anderen Render (Version 2 derselben Szene, gleiche Bildnummer) bekommt die
     gespeicherte Deckung des alten Renders.
   - Der Strich bleibt dann auf dem Umriss des Objekts aus dem anderen Projekt. Das
     sähe genau aus wie „eine Maske aus einem anderen Projekt taucht beim Pinsel auf“.
   - Gelöscht wird der Vorrat erst ab 16 Einträgen, nie beim Wechsel.
   - **Plausibelster Kandidat**, setzt eine EXR mit Kryptomatte und eingeschaltetes
     „Objekt“ am Pinsel voraus.
2. **Tonwert-Pipette:** Sie überlebt das Öffnen eines anderen Bildes und hält das
   Werkzeug des alten Rezepts. Der nächste Klick ändert ein Objekt, das nicht mehr
   gerechnet wird. Es passiert sichtbar nichts.
3. **Knotenvorschauen** (`NodePreviews`):
   - Knoten heißen in jedem Graphen `n1`, `n2`, …, und die Vorschauen werden nach
     dieser Kennung gemerkt und beim Projektwechsel nicht geleert.
   - Ein Knoten `n5` im neuen Projekt zeigt die Vorschau von `n5` im alten, bis er
     selbst gerechnet wird. Ein Zweig, der gar nicht gerechnet wird (ausgeblendete
     Ebene), behält sie.
   - Betrifft Knotenliste und Ebenenliste im Knotenmodus.

Nötig zur Eingrenzung, falls es wieder auftritt: War es eine EXR mit Kryptomatte? War
„Objekt“ am Pinsel an? Erschien die Maske als roter Schleier beim Malen, als Zeile in
den Ebenen oder als Miniatur?

**Grundsätzlich:** Alle drei Lücken sind derselbe Fehler. Ein Zustand hat keinen
Besitzer, der ihn beim Wechsel zurücksetzt. Das Bearbeitungsziel (Abschnitt 5) macht
daraus eine Stelle: `Switch(recipe)` setzt das Ziel zurück, und wer vom Ziel abhängt,
hört auf dessen Änderung.

### 4.2 Pixel Sort und die übrigen Knoten (Punkt 13)

Gemessen mit dem Test-PNG (4096 × 2160) aus dem erlaubten Testordner:

| Fall | Stapel | Knoten | Stapel = Knoten |
|---|---|---|---|
| Grundstellung | 0 Bildpunkte geändert | 0 | ja |
| Fenster 0,2–0,8 | 4,51 Mio. (176 ms) | 4,51 Mio. (288 ms) | ja |
| Fenster 0,2–0,8, 90° | 4,49 Mio. (125 ms) | 4,49 Mio. (249 ms) | ja |
| Grob (beim Ziehen) | 0 | 0 | ja |

Die Ausführung stimmt, die Parameter kommen an, Vorschau und Stapel gehen denselben Weg.
Fehler werden nicht verschluckt: Ein Graph, der scheitert, meldet sich als Warnung am
Editor. Warum es trotzdem „nicht funktioniert“:

1. **Grundstellung wirkt nicht.** Das Sortierfenster ist geschlossen (unten = oben = 0).
   Die Kachel gilt dann als aus. Wer den Knoten hinzufügt, sieht keine Veränderung.
2. **Beim Ziehen keine Vorschau.** Durchgänge über das fertige Bild laufen nur im vollen
   Durchgang. Während ein Regler gezogen wird, rechnet die Seite grob, und Pixel Sort
   fällt weg. Erst beim Loslassen erscheint die Wirkung.
3. **16-Bit- und EXR-Ausgabe lassen es weg.** So im Code beschrieben
   (`FloatFrameProcessor.ApplyRgba64`, `FramePassNode`). In der Oberfläche steht es
   nirgends. Vorschau und Ausgabe unterscheiden sich also.
4. **Mitten im Graphen** wird das Bild für den Durchgang auf acht Bit gebracht. Auf
   Szenenlicht vor der Anzeige ist das verlustbehaftet.

**Katalogprobe:** Jeder Knoten des Menüs in Grundstellung vor den Ausgang gesetzt
(1024 × 540):

- Alle 25 Effekte und Korrekturen der Palette ändern keinen Bildpunkt, von Korrektur bis Tabelle.
  Für Korrekturen ist das richtig. Für Effekte wie Pixel Sort, Rasterdiffusion, Glanz,
  Halation, Filmkorn, Vignette und Rastern heißt es: hinzugefügt, und nichts passiert.
- Jeder Effekt hat seine Einstellungskarte. Kein Knoten scheitert beim Rechnen.
- „Platzieren“ nimmt nur eine Quelle an (Pass oder Datei), keinen bearbeiteten Zweig.
  Das ist gewollt, begrenzt aber Punkt 11: Ein ausgeschnittener, bearbeiteter Bereich
  lässt sich nicht als eigener Zweig verschieben.
- „Anzeige“ ein zweites Mal eingefügt wendet die Sichtumwandlung doppelt an. Das ist
  richtig gerechnet, aber eine Falle im Menü.

### 4.3 Kleinere Punkte (14–17)

- **14, Zuletzt geöffnet:**
  - „Liste leeren“ gibt es schon.
  - Ein einzelner Eintrag lässt sich nicht entfernen, die Kachel hat kein Kontextmenü.
  - `RecentSequences.Forget(folder)` gibt es, entfernt aber alle Folgen eines Ordners
    und braucht eine Form je Eintrag (Ordner und Muster).
  - Der Ordner selbst bleibt immer unberührt.
- **15, Web-QR-Adresse:** `WatchAddress` ist ein reiner Text, nicht wähl- oder kopierbar.
  Geplant: Klick oder Symbol kopiert, danach steht kurz „Kopiert“.
- **16, doppelter Satz:** `SettingsEditor.xaml.cs:244` setzt den Hinweis unter dem
  App-Code auf `S_ScanHint`, und direkt darunter steht derselbe Text fest im XAML.
  Die übrigen Texte der Einstellungen werden im selben Schnitt durchgesehen.
- **17, Ecken der Vorschau:** `StageFrame` (`MainWindow.xaml:263`) schneidet mit
  `ClipToBounds`. Das schneidet in WPF rechteckig und kennt die abgerundeten Ecken
  nicht. Nötig ist ein abgerundeter Zuschnitt, der mit Größe, Format und Zoom mitgeht.

### 4.4 Kryptomatte und Pipette heute (Punkte 3 und 4)

- **Kryptomatte:**
  - Ein Klick wählt das Objekt unter dem Zeiger und nimmt es in die Maske auf.
  - Vorher gibt es keinen Namen, keine Hervorhebung, keinen Umriss.
  - Die gewählten Objekte stehen nur als Liste im Maskenbereich des Streifens.
  - Im Knotenmodus wählt ein Klick nur mit gewähltem Maskenknoten, sonst still gar nicht.
- **Pipette:**
  - Sie liest **das Quellbild**, nicht das angezeigte Ergebnis, und nur beim Klick.
  - Sie zeigt Koordinaten, 8-Bit-Werte, lineare Werte und bei EXR die Tiefe.
  - Als einzige Weiterverwendung übernimmt sie die Tiefe als Schärfeebene.
  - Es gibt kein HEX/HSV und keine Vorschau beim Überfahren.
- **Farbbereich-Maske:** Sie hat Regler für Farbton und Breite, aber keine Pipette.

## 5. Der Bearbeitungskontext (Punkte 5, 12, 20)

**Wo er lebt:** in `AtelierEditingSession`, als S2 zweiter Teil. Die Sitzung besitzt
schon das Rezept. Das Ziel gehört daneben, weil es nur zusammen mit dem Rezept Sinn hat.
Ein neues Rezept (`Switch`) setzt das Ziel auf „Gesamtbild“. Das ist der eine Rückweg,
der heute siebenmal einzeln fehlt.

**Was ein Ziel ist:**

```
EditingTarget
  Gesamtbild                         alles, hinter allen Ebenen
  Ebene(id)                          eine Ebene: Stapelzeile oder Knotenebene (Mischen)
  Ebenen(ids)                        mehrere
  Maske(Ebene, Masken-Id)            die Maske einer Ebene: gemalt, Kryptomatte, Bereich
  Auswahl(Kryptomatte, Kennungen)    Objekte, noch ohne Maske - vorläufig
  Knoten(id)                         ein bestimmter Knoten
```

- Jedes Ziel wird **in beiden Modellen** aufgelöst: im Stapel auf eine Zeile, im Graphen
  auf Knoten. Die Kennungen dafür gibt es schon: Ebenen-Mischen im Graphen, Masken-Id
  (`LayerMask.EnsureId`), Knoten-Id.
- Geändert wird das Ziel an einer Stelle (`Focus(target)`), gemeldet mit einem Ereignis.
  Die Auswahl in Ebenenliste, Knoteneditor und Knotenliste setzt es und folgt ihm. Sie
  sind keine eigenen Wahrheiten mehr.
- Wer heute die Kette aus Abschnitt 2 durchgeht, fragt nur noch das Ziel: Farbstreifen
  (`Bind`/`ShowNodeSettings`), Pinsel (`PaintTarget`), Klick ins Bild (`PickAt`,
  Objektmaske, Pipetten), Werkzeugeinstellungen.
- **Vorläufiges** (eine Kryptomatte-Auswahl vor der ersten Aktion, eine gespannte
  Pipette) hängt am Ziel und endet mit ihm. Die drei Lücken aus 4.1 fallen damit weg.

**Anzeige „Woran arbeite ich?“:** eine schmale Zeile über dem Bild, aus dem Ziel
abgeleitet, nicht zusätzlich gepflegt:

- `Ebene: Beauty › Maske: Auto › Knoten: Farbbalance`
- `Gesamtbild › alle Ebenen`
- `Auswahl: Auto, Rad (2 Objekte) › noch keine Aktion`

Jedes Glied ist klickbar und macht dieses Glied zum Ziel. Rechts daneben steht, was
gerade isoliert ist (Abschnitt 7).

**Schnittreihenfolge nach den Refactoring-Regeln:**

1. `test:` Charakterisierung. Wohin gehen Regler, Strich und Klick bei jeder
   Kombination aus Modus und Auswahl? Die Probe aus 4.1 kommt dazu.
2. `refactor:` Das Ziel in die Sitzung. Die heutigen Felder lesen es, ohne
   Produktänderung.
3. `docs:` Plan nachtragen.

Die drei Lücken aus 4.1 sind `fix:`-Commits davor, jeder mit seiner Probe.

## 6. Auswahl → Aktion → Ergebnis (Punkte 6, 7, 8, 11)

Eine Aktion bekommt kein Ziel mitgegeben, sie nimmt das aktuelle. Was im Graphen
entsteht:

| Ziel | „Farbe korrigieren“ / „Filter hinzufügen“ | Baustein heute |
|---|---|---|
| Gesamtbild | Knoten vor Anzeige und Ausgabe | Einfügen am Ende |
| Ebene | hinter die Korrektur der Ebene, vor ihr Mischen | `AddIntoLayer` (R3) |
| mehrere Ebenen | eine gemeinsame Korrektur darüber, auf diese Ebenen begrenzt (Entscheidung 4) | Gruppe, neu |
| Auswahl (Kryptomatte) | neue Maskenebene: Korrektur + Kryptomaske + Mischen; die Auswahl wird ihre Maske | `AddNodeMaskLayer`, Objektmaske (R4) |
| Maske einer Ebene | wie Ebene: Die Maske begrenzt schon | `AddIntoLayer` |
| Knoten | direkt hinter den Knoten, sein Ausgang läuft weiter | `ShowNodeHub(into:)` |
| ausgeschnittener Bereich | Ausschneiden + Korrektur als eigener Zweig über dem Bild | `ObjectAsLayerAt` (Bildmenü), „Platzieren“ braucht dafür einen bearbeiteten Eingang |

**Wo die Aktionen stehen:**

- im Rechtsklickmenü des Bildes (gibt es, bekommt die Zielzeile);
- als schmale Leiste unter der Zielzeile: Korrigieren, Belichtung, Sättigung, Tonwert,
  Weichzeichnen, Filter …, Maske, Isolieren;
- am Knoten per Doppelklick (Schnellfeld, Entscheidung 3).

Jede Aktion legt an, verbindet und wählt das Neue als Ziel. Der Farbstreifen zeigt sofort
dessen Karte. Rückgängig nimmt die ganze Aktion in einem Schritt zurück.

**Eigenschaften des gewählten Knotens (Punkt 8):** Heute zeigt der Farbstreifen die Karte
des gewählten Knotens schon. Er steht aber als Reiter neben den Ebenen und bleibt
zugeklappt, wenn man ihn zugeklappt hat. Vorschlag: Das Feld „Eigenschaften“ im Dock
folgt dem Ziel. Wird ein Knoten gewählt, klappt es auf und kommt nach vorn. Ohne Ziel
mit Einstellungen klappt es zu. Kein neues Fenster, dasselbe Dock wie die
Werkzeugeinstellungen (Entscheidung 10).

## 7. Vorschläge je Punkt

**Kryptomatte (3):**

- **Beim Überfahren:** Name an der Maus (Objekt, dazu Material, wenn die Datei es führt).
  Das Objekt ist hell überlagert und wird mit grober Deckung (¼ Auflösung) je Kennung
  gemerkt, nach **Datei** und Bild.
- **Gewählt:** Umriss in Akzentfarbe um alle gewählten Objekte.
- **In den Werkzeugeinstellungen:** die Auswahl als Chips (`Auto ✕  Rad ✕`) und die
  Zielzeile „Auswahl: 2 Objekte“.
- **Tasten:** Klick ersetzt, Umschalt+Klick fügt hinzu, Alt+Klick nimmt heraus.
- **Knotenmodus:** gleich. Ohne gewählten Maskenknoten entsteht eine vorläufige Auswahl
  statt eines stillen Nichts.

**Pipette (4):**

- **Beim Überfahren:** eine Lupe mit 11 × 11 Punkten, Farbfeld, RGB, HEX, HSV/HSL und
  lineare Werte.
- **Was sie liest:** standardmäßig das Ergebnis, umschaltbar auf die Quelle.
- **Klick:** Er gibt die Farbe dem Ziel. Farbbereich setzt Farbton und Breite
  (Umschalt erweitert), Tonwert setzt einen Punkt, Weißabgleich den Neutralpunkt.
  Die Zielzeile sagt vorher: „Pipette für: Farbbereich ‚Himmel‘“. Ohne passendes Ziel
  liest sie nur ab und merkt die Farbe.

**„Woran arbeite ich?“ (5):** die Zielzeile aus Abschnitt 5.

**Bereichsregler (9):** ein Baustein `RangeSlider` für alles, was ein Fenster auf einer
Skala hat.

- **Wo er gilt:** Helligkeitsmaske, Farbbereich (umlaufend), Pixel-Sort-Fenster,
  Keying, später die Clipping-Anzeige aus W2c.
- **Aufbau:** vier Griffe, je zwei als Paar. Das Paar zieht zusammen. Mit Alt gezogen
  trennt es sich wie „Farbbereich“ in Photoshop, und der Abstand wird die weiche Kante.
- **Bedienung:** Doppelklick setzt zurück, Umschalt zieht fein. Unter der Skala steht ein
  Verlauf (Helligkeit oder Farbton), wahlweise das Histogramm.
- **Tonwert:** Er behält seine drei Eingangsgriffe. Der Bereichsregler kommt dort nur
  dazu, wenn Ausgabe-Schwarz und -Weiß weich werden sollen. Das wäre eine eigene
  Entscheidung.

**Isolieren (10):** alles über den vorhandenen Betrachter (`_viewer`), kein neuer Zustand.

- **Knoten:** Auge im Kopf des Knotens, Rechtsklick „Ausgabe ansehen“ oder Strg+Umschalt+Klick
  (gibt es schon).
- **Maske:** Alt+Klick auf das Maskenzeichen der Ebene zeigt die Maske grau. Mit
  Umschalt kommt sie als roter Schleier über das Bild.
- **Ebene:** Alt+Klick auf das Auge zeigt sie allein, wie in Photoshop und Blender.
- **Ende:** Über dem Bild steht dann „Isoliert: Maske ‚Auto‘ – Esc beendet“. Esc, ein
  zweiter Klick oder ein Projektwechsel beendet es.

## 8. Reihenfolge und Entscheidungen

**Vorschlag:**

1. **A, kleine Schnitte, unabhängig:**
   - Rückwege beim Wechsel (4.1: Pinselgrenze nach Datei, Pipette beenden,
     Vorschauen leeren), mit Probe.
   - Einstellungen: doppelter Satz, Texte durchsehen, Web-Adresse kopierbar (15, 16).
   - Zuletzt geöffnet: Eintrag entfernen (14).
   - Übersicht: abgerundeter Zuschnitt (17).
   - Pixel Sort: Hinweis bei 16 Bit, Startwerte nach Entscheidung 2 (13).
2. **B, das Bearbeitungsziel** (S2 zweiter Teil): Charakterisierung, Umbau ohne
   Produktänderung, Plan.
3. **C, auf B:**
   - C1 Zielzeile (5).
   - C2 Aktionen am Ziel (6, 11, 12).
   - C3 Kryptomatte (3).
   - C4 Pipette (4).
   - C5 Eigenschaften folgen dem Ziel, Schnellfeld am Knoten (7, 8).
   - C6 Isolieren (10).
   - C7 Bereichsregler (9).
4. **W2c–W2f** danach. C7 steht vor W2c, weil die Clipping-Anzeige denselben Regler
   braucht.

**Entschieden am 27. September, alle vier wie empfohlen:**

1. **Reihenfolge:** erst A, dann B, dann C, danach W2c–W2f.
2. **Effekte beim Hinzufügen:** Effekte bekommen sichtbare Startwerte (Pixel Sort mit
   offenem Fenster, Filmkorn mit etwas Korn …). Korrekturen bleiben neutral.
3. **Knoteneinstellungen:** beides. Die Eigenschaften im Dock folgen dem Ziel, dazu kommt
   ein Schnellfeld am Knoten per Doppelklick.
4. **Filter auf mehrere Ebenen:** eine gemeinsame Korrektur darüber, auf diese Ebenen
   begrenzt.

## 9. Testmaterial (Punkt 18)

- `Downloads\test\passes` und `Downloads\test\passes png`: eine Blender-Multilayer-EXR
  (4096 × 2160, 4,6 GB, alle Pässe, Kryptomatte) und ein PNG in 4K.
- Genutzt nur als Testmaterial. Die Messproben lesen den Pfad aus einer
  Umgebungsvariable (`FRAMEFLIP_PROBE_IMAGE`) und sind nicht eingecheckt. Kein Pfad
  steht im Programm oder in der Testreihe.
- Die Projektdateien darin wurden nur auf ihre Struktur gelesen (Knotenarten,
  Maskenarten, Zählwerte), nie die Maskendaten selbst.
- Für die CI bleiben die Proben synthetisch.
- Die EXR ist für Proben unter einer Minute zu groß, sie dient nur für einzelne
  Messungen von Hand.

## 10. Stand der Umsetzung

### A1: Rückwege beim Wechsel (`fix/wechsel-rueckwege`)

Die drei Lücken aus 4.1 sind geschlossen, jede mit eigenem Commit:

- **Objektgebundener Pinsel:** Die gemerkte Deckung trägt jetzt die Datei im Schlüssel.
  Ein zweiter Render derselben Szene bekommt die Deckung seines eigenen Objekts.
- **Tonwert-Pipette:** Öffnen eines anderen Bildes beendet sie. Die Maus geht zu dem
  Werkzeug zurück, das sie vor der Pipette hatte, wie nach einem Klick ins Bild.
- **Knotenvorschauen:** Ein Projektwechsel leert sie. Ein Knoten des neuen Projekts zeigt
  keine Vorschau seines Namensvetters im alten mehr.

**Proben** (`SwitchLeftoverInvariants`):

- Ein Pinselstrich über die echten Tasten des Rahmens, nach einem Wechsel aus dem
  Knotenmodus und aus dem Stapel. Er landet im neuen Projekt, und nichts vom alten kommt
  mit. Das war schon vorher richtig und bleibt als Schutz stehen.
- Zwei Renders mit derselben Kugel, einmal links, einmal rechts. Vorher bekam der zweite
  die Deckung von links.
- Eine wartende Pipette, dann ein anderes Bild. Vorher wartete sie weiter.
- Vorschauen nach dem Projektwechsel. Vorher stand die des alten `n1` noch da.

Vor der Behebung schlugen vier der dreizehn Zusicherungen fehl, danach keine. Die
Nachbargruppen (Tonwert, Pinsel, Knotenmodus, Projekte, Objektmaske, Knoten bearbeiten)
liefen mit 328 Zusicherungen grün.

Fehler 1 selbst ließ sich damit nicht belegen. Die Lücke beim objektgebundenen Pinsel ist
aber die, die am besten zum Bericht passt.

### A2: Einstellungen, Texte und Ordnung (`fix/einstellungen-texte`)

Punkte 15 und 16. Durchgesehen wurden alle sieben Seiten, als Textliste und als Bild mit
synthetischer Konfiguration (lokal erzeugte Schlüssel, `relay.example.org`, kein Netz).

- **Doppelter Satz unter dem App-Code:** Der Hinweis richtet sich nach dem Zustand und sagt
  „Mit der App abfotografieren …“ schon, sobald ein Code dasteht. Ein fester Satz darunter
  wiederholte ihn, und ohne Code bat er darum, etwas abzufotografieren, das es nicht gab.
  Er ist weg.
- **Hinweise nennen den Schalter, wie er heißt:** „Fernsteuerung aktivieren“ meinte den
  Schalter „Renderfortschritt ans Handy senden“. Kopplung, Render und Austauschordner nennen
  ihn jetzt wörtlich.
- **Gruppe und Zeile mit demselben Namen:** „Dynamische Last“ (Zeile jetzt „Automatisch
  anpassen“), „Blender“ („Programm“), „Austauschordner“ („Ordner“), dazu die erste Gruppe
  der Seite „Arbeitsbereich“, die wie ihre Seite hieß („Größen und Aufteilung“). Das ältere
  Kopplungsfenster behält seine Beschriftungen.
- **Falscher Verweis:** Der Hinweis zum Annehmen von Dateien sprach vom „Austauschordner
  unten“. In den Einstellungen steht er oben, im Kopplungsfenster unten. Der Hinweis nennt
  jetzt keinen Ort mehr.
- **Tonlage:** Der Untertitel von „Blender & Dateien“ duzte und sagte „PC“. Er spricht jetzt
  wie die anderen Seiten und sagt „Rechner“.
- **Jede Zeile erklärt sich:** „Nahtlos von vorn“ und „Puffer zurück“ hatten kein
  Info-Zeichen.
- **Abstände:**
  - Die Info-Zeichen einer Gruppe stehen untereinander. Die Spalte der Bedienelemente ist
    je Gruppe gleich breit (`SharedSizeGroup`), vorher schob ein breites Feld sein Zeichen
    nach links.
  - Jede Seite beginnt an derselben Kante. „Arbeitsbereich“ war mit eigener Breite
    eingerückt. „Verbindungen“ rutschte um die halbe Breite des Rollbalkens nach links,
    weil nur sie lang genug für einen ist. Jetzt halten alle Seiten den Platz frei, und die
    Spur bleibt leer, solange nichts zu rollen ist.
- **Adresse der Zuschauerseite (15):** Sie ist selbst der Knopf, der sie kopiert, mit
  Kopier-Zeichen, Tooltip und über die Tastatur erreichbar, in den Einstellungen und im
  Dashboard. An ihrer Stelle steht danach gut eine Sekunde lang „✓ Kopiert“, auch nach
  „Link kopieren“. Ist die Zwischenablage belegt, behauptet die Karte nichts.

**Proben:**

- `SettingsTextInvariants`: sechs Zusicherungen über alle Seiten. Vorher schlugen alle
  sechs fehl, unter dem App-Code stand der Satz zweimal.
- `WatchCardInvariants.TheAddressCopies`: Klick, Bestätigung, Rückkehr zur Adresse, „Link
  kopieren“, belegte Zwischenablage. Kopiert wird über einen Wirt, der nur mitschreibt.
- **Echte Instanz:** Debug-Build mit eigener Konfiguration. Alle sieben Seiten lassen sich
  wählen, das Protokoll enthält keine Ausnahme, der Kopplungshinweis zeigt den neuen Text.

### A3: Einträge aus „Zuletzt geöffnet“ nehmen (`feature/zuletzt-entfernen`)

Punkt 14. „Liste leeren“ gab es schon. Jetzt hat jede Kachel unter „Zuletzt geöffnet“ ein
Kontextmenü:

- **„Aus der Liste nehmen“:** mit denselben Worten wie die Sequenzliste im Dashboard.
  Vergessen wird nur der Eintrag, der Ordner und seine Bilder bleiben.
- **„Im Explorer zeigen“:** solange der Ordner da ist.

Auch ein Eintrag, dessen Ordner verschwunden ist, lässt sich so austragen. Gerade der
sollte gehen können, ohne die ganze Liste zu leeren.

**Probe** (`RecentForgetInvariants`, eigene Konfiguration): drei Einträge, einer davon mit
gelöschtem Ordner. Ein Eintrag geht, die anderen bleiben in ihrer Reihenfolge. Der Ordner
ist unberührt, die Seite zeigt ihn nicht mehr, und der verschwundene lässt sich ebenso
austragen.

### A4: Ecken der Übersicht-Vorschau (`fix/vorschau-ecken`)

Punkt 17. Der Rahmen der Bühne nimmt das Seitenverhältnis der Folge an, das Bild füllt ihn
also bis in die Ecken. `ClipToBounds` schneidet in WPF aber rechteckig, deshalb standen die
Ecken des Bildes über die runden des Rahmens.

- **Behebung:** Ein kleiner Baustein `RoundedClip` schneidet den Inhalt auf den inneren
  Radius (Eckradius 10 weniger Rand 1) und zieht den Zuschnitt bei jeder Größenänderung
  nach. Er liegt in den Koordinaten des Inhalts, gilt also für Quer- und Hochformat und
  skaliert mit der `Viewbox` darüber. Eine Zoomstufe hat die Übersicht nicht, das Bild wird
  immer eingepasst.
- **Probe** (`RoundedClipInvariants`), am Pixel:
  - Der Baustein allein in drei Formaten, nach Größenänderungen: in den Ecken der Grund,
    in der Mitte und an den Rändern das Bild.
  - Die echte Bühne im Hauptfenster, quer und hoch, mit einem weißen Bild im Format des
    Rahmens. Gezeichnet wird der Rahmen allein über einen `VisualBrush` in seiner eigenen
    Größe, so dass die `Viewbox` darüber nicht mitspielt.
  - Gegenprobe ohne Zuschnitt: alle vier Bühnenprüfungen rot. Frühere Fassungen der Probe
    blieben dabei grün oder hingen am Bildschirm. Ein quadratisches Probebild erreichte die
    Ecken gar nicht. Auf dem kleinen Bildschirm der CI verkleinerte die `Viewbox` die
    Hochformat-Bühne auf ein Viertel, und die Ecke verschmolz mit der Randlinie. Beides ist
    korrigiert.
- **Echte Instanz:** Debug-Build mit eigener Konfiguration startet ohne Ausnahme.

### A5: Pixel Sort und die Startwerte der Effekte (`feature/effekt-startwerte`)

Punkt 13 mit Entscheidung 2. Pixel Sort rechnete richtig (4.2), wirkte aber nach dem
Hinzufügen nicht, und eine 16-Bit-Ausgabe ließ es stillschweigend weg. Die Exportleiste
steht dabei standardmäßig auf „PNG · 16 Bit“.

- **Startwerte:** Ein neu hinzugefügter Effekt kommt sichtbar, im Knotenmenü wie in der
  Palette des Stapels (`EffectStart`).
  - Pixel Sort 0,25 bis 0,8, Filmkorn 0,25, Vignette −0,35, Glanz und Halation 0,4.
  - Rastern und Diffusion voll, Farbsäume 0,3, Verzeichnung 0,15.
  - Bewegungsunschärfe 0,5, Verschiebung 20, Tiefenschärfe 0,3.
  - Korrekturen bleiben neutral. Ein schon eingestellter oder kopierter Effekt behält
    seine Werte.
  - Die Werkzeugklassen bleiben neutral, weil der Stapel jedes Werkzeug auch ungenutzt
    anlegt. Gesetzt wird nur im Augenblick des Hinzufügens.
  - Bei der Karte „Rastern“, die Raster und Diffusion über einen Regler bedient, bekommt
    nur das Raster den Startwert.
- **Grenzen der Startwerte:** Glanz und Halation greifen erst über ihrer Schwelle. Die drei
  Effekte mit Renderdaten wirken nur, wenn die Datei die Pässe führt.
- **Ausgabe:** Wirkt Pixel Sort oder Fehlerdiffusion und ist PNG 16 Bit oder TIFF gewählt,
  steht an der Exportleiste: „Pixel Sort und Fehlerdiffusion rechnen in 8 Bit und fehlen in
  diesem Format …“. Die Karte von Pixel Sort sagt es ebenfalls.
- **Offen:** Während ein Regler gezogen wird, zeigt die grobe Vorschau Pixel Sort noch
  nicht. Das kommt beim Loslassen, wie bisher. Eine grobe Fassung des Durchgangs wäre ein
  eigener Schnitt.
- **Probe** (`EffectStartInvariants`, 15 Zusicherungen):
  - Jeder Effekt des Knotenmenüs mit Startwert, jede Korrektur neutral.
  - Pixel Sort, Filmkorn und Vignette aus dem Menü verändern ein synthetisches Bild sofort.
  - Im Stapel dasselbe, beim Rastern nur das Raster. Eine schon hinzugefügte Karte wird
    nicht wieder aufgezogen.
  - Der Hinweis an der Ausgabe erscheint bei PNG 16 Bit und TIFF, nicht bei PNG 8 Bit,
    ebenso im Knotenmodus.
  - `GradingGroupInvariants` hielt fest, dass ein hinzugefügter Effekt nichts tut. Die
    Probe stellt Vignette und Korn jetzt ausdrücklich auf null, bevor sie Strich und
    Ausschalten prüft.
- **Echte Instanz:** Debug-Build mit eigener Konfiguration und synthetischer Folge. Das
  Atelier öffnet mit Exportleiste, ohne Ausnahme.

### B: Das Bearbeitungsziel (`refactor/studio-s2-target`)

Umgesetzt wie in Abschnitt 5 entworfen, als Refactoring ohne Produktänderung. Einzelheiten
und Abnahme stehen im [Refactoring-Studio](Refactoring-Studio.md), S2 zweiter Teil.

- `EditingTarget` in der Sitzung, drei Fälle: Bild, Stapelebene, Knoten. Die Masken- und
  Auswahlziele aus Abschnitt 5 kommen mit den Schnitten, die sie brauchen (C2, C3).
- Die Wahl in Ebenenstreifen, Knoteneditor und Ebenenliste setzt das Ziel. Farbstreifen,
  Pinsel, Klick ins Bild, Objektmaske, Werkzeugleiste und Hub lesen es.
- Ein neues Rezept setzt das Ziel aufs Bild zurück. Damit gibt es für die Rückwege aus 4.1
  eine Stelle statt vieler.
- Im Stapel lesen Rahmen und Pinsel noch die Auswahl des Ebenenstreifens. Sie ziehen mit C
  um.

### C1: Die Zielzeile (`feature/zielzeile`)

Punkt 5. Über dem Bild steht, woran gerade gearbeitet wird. Die Zeile ist aus dem
Bearbeitungsziel abgeleitet (B) und wird nicht zusätzlich gepflegt. Sie kann deshalb nichts
anderes sagen als das, wohin der nächste Regler geht.

- **Knotenmodus:**
  - „Ebene: Licht“ – die Ebene aus der Liste oder ihr Mischen.
  - „Ebene: Licht › Maske: Auto, Rad“ – ihre Maske; eine Kryptomatte nennt die gewählten
    Objekte.
  - „Ebene: Licht › Knoten: Korrektur“ – ein Knoten in ihrem Zweig.
  - „Gesamtbild › Knoten: Tonwert“ – ein Knoten hinter allen Ebenen.
  - „Gesamtbild“ – nichts gewählt.
- **Zuordnung:** Ein Knoten gehört zur innersten Ebene, in deren Zweig er liegt. Zur Maske
  zählt nur, was ausschließlich in sie fließt. Masken lesen das Bild der Ebene als Eingang,
  die Korrektur der Ebene bleibt trotzdem ein Knoten der Ebene.
- **Stapel:** „Ebene: Name“. Rechts steht „Farbstreifen wirkt aufs Gesamtbild“, wenn die
  Ebene gewählt ist, die Regler aber dem Bild gelten.
- **Klick:** Ein Glied, das nicht selbst das Ziel ist, wählt, wofür es steht. Die Ebene
  wird gewählt wie in der Liste, „Gesamtbild“ wählt ab. Ebenenliste und Editor gehen mit.
- **Probe** (`TargetPathInvariants`, 15 Zusicherungen):
  - Ohne Fenster: alle Fälle der Zuordnung.
  - Auf der Seite: sichtbar nur mit Bild, der Hinweis im Stapel, Klick auf „Gesamtbild“ und
    auf die Ebene, ein umbenanntes Mischen.
- **Echte Instanz:** Debug-Build mit eigener Konfiguration und synthetischer Folge. Die
  Zeile steht samt Hinweis da, ohne Ausnahme.

### C2a: Aktionen am Ziel (`feature/ziel-aktionen`)

Punkte 6, 11 und 12 für Einzelziele. Ein Effekt richtet sich im Knotenmodus nach dem
Bearbeitungsziel. Das gilt für die Werkzeugleiste, die Suche und die neuen Knöpfe
„+ Korrektur“ und „+ Effekt …“ rechts in der Zielzeile.

| Ziel | Wohin der Effekt geht | Vorher |
|---|---|---|
| Gesamtbild (nichts gewählt) | an seine Stelle in der Kette des Bildes, wie der Stapel rechnet | frei in die Mitte, ohne Kabel |
| Ebene aus der Liste | in ihren Zweig, vor ihr Mischen | ebenso |
| Maske einer Ebene | in den Zweig der Ebene; die Maske begrenzt ihn schon | frei in die Mitte, ohne Kabel |
| ein Knoten mit Bildausgang | direkt dahinter | ebenso |

- **`GlobalChain`:** Kennt die Rechenordnung, in der `StackToGraph` das fertige Bild
  anlegt: Licht, Szenen-Werkzeuge, Objektiv, Geometrie, Renderdaten, frühe lokale
  Werkzeuge, Film, Sichtumwandlung, Tonwerte, Anzeige-Werkzeuge, späte lokale Werkzeuge,
  Wasserzeichen, Durchgänge. Ein neuer Knoten kommt hinter den letzten, der vor ihm dran
  ist. Hat er keinen Platz darin, kommt er vor die Ausgabe, verbunden.
- **Eine Zuordnung für beides:** Zielzeile und Einfügestelle fragen dieselbe Frage
  (`TargetPath.OwnerOf`), damit ein Effekt dorthin geht, wo die Zeile es sagt.
- **Im Stapel:** Beide Knöpfe nehmen den Weg der Werkzeugleiste. „+ Korrektur“ zeigt die
  Grundkarte des Ziels, „+ Effekt …“ öffnet die Suche.
- **Probe** (`TargetActionInvariants`, 11 Zusicherungen):
  - Die Kette und die Einfügestellen ohne Fenster.
  - Auf der Seite: Vignette, Pixel Sort und Korrektur am Gesamtbild, Filmkorn an der Maske
    einer Ebene, dazu die beiden Knöpfe.
- **Echte Instanz:** Debug-Build, beide Knöpfe in der Zeile, ohne Ausnahme.
- **Offen:** C2b, mehrere Ebenen (Entscheidung 4: eine gemeinsame Korrektur darüber).
  Dafür braucht es erst eine Mehrfachauswahl in Ebenenliste und Ebenenstreifen. Die
  Kryptomatte-Auswahl als Ziel kommt mit C3.

### C3a: Rückmeldung beim Wählen (`feature/krypto-rueckmeldung`)

Punkt 3, sichtbarer Teil. Mit dem Werkzeug „Auswählen“ gibt es jetzt:

- **Am Zeiger:** der Name des Objekts. Führt die Datei eine zweite Kryptomatte, etwa das
  Material, und sind deren Stufen schon da, steht deren Name dabei.
- **Über dem Bild:**
  - Das Objekt unter dem Zeiger ist hell überlagert. Die Deckung wird aus jedem vierten
    Bildpunkt gerechnet und je Objekt gemerkt.
  - Die gewählten Objekte sind leise gefüllt und kräftig umrandet, gerechnet aus jedem
    zweiten Bildpunkt, sobald sich die Auswahl ändert.
  - Beides liegt genau so groß wie das Bild darüber, beim Einpassen wie beim Zoom.
- **In den Werkzeugeinstellungen:** die Kryptomatte, die Auswahl als Chips mit Kreuz und
  was unter dem Zeiger liegt. Das Kreuz nimmt ein Objekt aus der Maske.
- **Ziel:** Gewählt wird an der Kryptomatte des Bearbeitungsziels: im Stapel die Maske der
  gewählten Ebene, im Graphen der gewählte Maskenknoten. Ohne ein solches Ziel werden die
  Objekte der Objekt-Kryptomatte nur angezeigt, und das steht da.
- **Stufen:** Fehlende Stufen werden im Hintergrund gelesen. Baut die Seite ihren Vorrat
  danach neu auf, etwa weil eine Maskenebene dazukam, werden sie wieder geholt. Die
  Probe hat genau diesen Fall gefunden.
- **Probe** (`CryptoFeedbackInvariants`, 10 Zusicherungen): Deckung und Umriss ohne
  Fenster. Auf der Seite mit einem synthetischen Render (Kugel, Boden) prüft sie Name und
  Hervorhebung, den Hinweis ohne Ziel, Umriss und Chip nach „Kugel als Maske“, das Kreuz am
  Chip und das Verschwinden bei einem anderen Werkzeug.
- **Echte Instanz:** Debug-Build ohne Ausnahme.
- **Offen, C3b:** Im Knotenmodus ohne gewählten Maskenknoten entsteht noch keine
  vorläufige Auswahl. Dazu kommen Klick ersetzt / Umschalt fügt hinzu / Alt nimmt heraus
  und „+ Korrektur“ auf die Auswahl.
