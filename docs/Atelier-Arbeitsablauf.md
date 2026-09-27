# Atelier: Arbeitsablauf und Bearbeitungsziel

Stand 27. September 2026. Analyse zum Nutzerbericht mit den Punkten 1 und 3–20. Noch
keine Umsetzung, die großen Entscheidungen stehen in Abschnitt 8.

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
| mehrere Ebenen | eine gemeinsame Korrektur darüber, auf diese Ebenen begrenzt (Frage 4) | Gruppe, neu |
| Auswahl (Kryptomatte) | neue Maskenebene: Korrektur + Kryptomaske + Mischen; die Auswahl wird ihre Maske | `AddNodeMaskLayer`, Objektmaske (R4) |
| Maske einer Ebene | wie Ebene: Die Maske begrenzt schon | `AddIntoLayer` |
| Knoten | direkt hinter den Knoten, sein Ausgang läuft weiter | `ShowNodeHub(into:)` |
| ausgeschnittener Bereich | Ausschneiden + Korrektur als eigener Zweig über dem Bild | `ObjectAsLayerAt` (Bildmenü), „Platzieren“ braucht dafür einen bearbeiteten Eingang |

**Wo die Aktionen stehen:**

- im Rechtsklickmenü des Bildes (gibt es, bekommt die Zielzeile);
- als schmale Leiste unter der Zielzeile: Korrigieren, Belichtung, Sättigung, Tonwert,
  Weichzeichnen, Filter …, Maske, Isolieren;
- am Knoten per Doppelklick (Schnellfeld, Frage 3).

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

## 8. Reihenfolge und offene Entscheidungen

**Vorschlag:**

1. **A, kleine Schnitte, unabhängig:**
   - Rückwege beim Wechsel (4.1: Pinselgrenze nach Datei, Pipette beenden,
     Vorschauen leeren), mit Probe.
   - Einstellungen: doppelter Satz, Texte durchsehen, Web-Adresse kopierbar (15, 16).
   - Zuletzt geöffnet: Eintrag entfernen (14).
   - Übersicht: abgerundeter Zuschnitt (17).
   - Pixel Sort: Hinweis bei 16 Bit, Startwerte nach Frage 2 (13).
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

**Offene Entscheidungen:**

1. Reihenfolge: erst A, dann B, dann C? Oder C1–C3 vorziehen?
2. Effekte beim Hinzufügen: sichtbare Startwerte (Pixel Sort mit offenem Fenster,
   Filmkorn mit etwas Korn …) oder neutral mit Hinweis „noch ohne Wirkung – Regler
   aufziehen“?
3. Knoteneinstellungen: Eigenschaften im Dock folgen dem Ziel, dazu ein Schnellfeld am
   Knoten per Doppelklick? Oder nur eines von beiden?
4. Filter auf mehrere Ebenen: eine gemeinsame Korrektur darüber, oder jede Ebene ihre
   eigene Kopie?

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
